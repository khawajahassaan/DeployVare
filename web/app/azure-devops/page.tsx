'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import api, {
  Application,
  ArtifactSource,
  AzureDevOpsConnection,
  AzureDevOpsTestResult,
  SaveArtifactSourceRequest,
} from '@/lib/api';
import { Cloud, Plus, Pencil, Trash2, PlugZap, CheckCircle, XCircle, Loader2, Save, GitBranch } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { toast } from 'sonner';

const emptySource = (applicationId = 0): SaveArtifactSourceRequest => ({
  applicationId,
  environment: 'Dev',
  serverType: 'Api',
  repository: '',
  branch: 'main',
  pipelineDefinitionId: null,
  windowsPipelineDefinitionId: null,
  artifactName: '',
  artifactSubfolder: '',
  preserveWebConfig: true,
  keepOnServerPaths: '',
  runDatabaseMigrations: false,
  sqlScriptsArtifactName: '',
});

// What the backend pipeline never overwrites on the server (robocopy /XF appsettings.json + /XD folders)
const BACKEND_KEEP_ON_SERVER_DEFAULTS = [
  'appsettings.json',
  'wwwroot',
  'Svgs',
  'Template',
  'Icons',
  'Assets',
  'Downloads',
  'AuditDocumentSharedFolder',
  'DataCollectionUploads',
  'ExpenseUploads',
].join('\n');

type RequestError = { response?: { data?: { message?: string; error?: string } } };

const errorMessage = (err: unknown, fallback: string): string => {
  const data = (err as RequestError | null)?.response?.data;
  return data?.message || data?.error || fallback;
};

export default function AzureDevOpsPage() {
  const { clients, selectedClientId } = useClientStore();
  const selectedClient = clients.find((c) => c.id === selectedClientId);

  const [loading, setLoading] = useState(true);

  // Connection
  const [connection, setConnection] = useState<AzureDevOpsConnection | null>(null);
  const [connectionForm, setConnectionForm] = useState({ organization: '', project: '', personalAccessToken: '' });
  const [savingConnection, setSavingConnection] = useState(false);
  const [testingConnection, setTestingConnection] = useState(false);
  const [connectionTest, setConnectionTest] = useState<AzureDevOpsTestResult | null>(null);

  // Artifact sources
  const [applications, setApplications] = useState<Application[]>([]);
  const [sources, setSources] = useState<ArtifactSource[]>([]);
  const [sourceDialogOpen, setSourceDialogOpen] = useState(false);
  const [editingSource, setEditingSource] = useState<ArtifactSource | null>(null);
  const [sourceForm, setSourceForm] = useState<SaveArtifactSourceRequest>(emptySource());
  const [savingSource, setSavingSource] = useState(false);
  const [sourceError, setSourceError] = useState('');
  const [testingSourceId, setTestingSourceId] = useState<number | null>(null);
  const [sourceTests, setSourceTests] = useState<Record<number, AzureDevOpsTestResult>>({});

  useEffect(() => {
    if (selectedClientId) loadData();
  }, [selectedClientId]);

  const loadData = async () => {
    if (!selectedClientId) return;
    setLoading(true);
    try {
      const [conn, apps, srcs] = await Promise.all([
        api.getAzureDevOpsConnection(selectedClientId),
        api.getApplications(selectedClientId),
        api.getArtifactSources(selectedClientId),
      ]);
      setConnection(conn);
      setConnectionForm({ organization: conn?.organization ?? '', project: conn?.project ?? '', personalAccessToken: '' });
      setConnectionTest(null);
      setApplications(apps);
      setSources(srcs);
      setSourceTests({});
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to load Azure DevOps configuration'));
    } finally {
      setLoading(false);
    }
  };

  const handleTestConnection = async () => {
    if (!selectedClientId) return;
    setTestingConnection(true);
    setConnectionTest(null);
    try {
      const result = await api.testAzureDevOpsConnection({
        clientId: selectedClientId,
        organization: connectionForm.organization || undefined,
        project: connectionForm.project || undefined,
        personalAccessToken: connectionForm.personalAccessToken || undefined,
      });
      setConnectionTest(result);
      // The backend records the result when the saved configuration was tested
      setConnection(await api.getAzureDevOpsConnection(selectedClientId));
    } catch (err) {
      setConnectionTest({ success: false, message: errorMessage(err, 'Connection test failed') });
    } finally {
      setTestingConnection(false);
    }
  };

  const handleSaveConnection = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedClientId) return;
    setSavingConnection(true);
    try {
      const saved = await api.saveAzureDevOpsConnection({
        clientId: selectedClientId,
        organization: connectionForm.organization,
        project: connectionForm.project,
        personalAccessToken: connectionForm.personalAccessToken || undefined,
      });
      setConnection(saved);
      setConnectionForm({ organization: saved.organization, project: saved.project, personalAccessToken: '' });
      setConnectionTest(null);
      toast.success('Azure DevOps connection saved');
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to save the connection'));
    } finally {
      setSavingConnection(false);
    }
  };

  const openCreateSource = () => {
    setEditingSource(null);
    setSourceForm(emptySource(applications[0]?.id ?? 0));
    setSourceError('');
    setSourceDialogOpen(true);
  };

  const openEditSource = (source: ArtifactSource) => {
    setEditingSource(source);
    setSourceForm({
      applicationId: source.applicationId,
      environment: source.environment,
      serverType: source.serverType,
      repository: source.repository,
      branch: source.branch,
      pipelineDefinitionId: source.pipelineDefinitionId ?? null,
      windowsPipelineDefinitionId: source.windowsPipelineDefinitionId ?? null,
      artifactName: source.artifactName ?? '',
      artifactSubfolder: source.artifactSubfolder ?? '',
      preserveWebConfig: source.preserveWebConfig,
      keepOnServerPaths: (source.keepOnServerPaths ?? '').split(';').join('\n'),
      runDatabaseMigrations: source.runDatabaseMigrations,
      sqlScriptsArtifactName: source.sqlScriptsArtifactName ?? '',
    });
    setSourceError('');
    setSourceDialogOpen(true);
  };

  const handleSaveSource = async (e: React.FormEvent) => {
    e.preventDefault();
    setSourceError('');
    setSavingSource(true);

    const payload: SaveArtifactSourceRequest = {
      ...sourceForm,
      artifactName: sourceForm.artifactName?.trim() || null,
      artifactSubfolder: sourceForm.artifactSubfolder?.trim() || null,
      pipelineDefinitionId: sourceForm.pipelineDefinitionId || null,
      windowsPipelineDefinitionId: sourceForm.windowsPipelineDefinitionId || null,
      keepOnServerPaths: sourceForm.keepOnServerPaths?.trim() || null,
      runDatabaseMigrations: sourceForm.serverType === 'Api' && sourceForm.runDatabaseMigrations,
      sqlScriptsArtifactName: sourceForm.sqlScriptsArtifactName?.trim() || null,
    };

    try {
      if (editingSource) {
        await api.updateArtifactSource(editingSource.id, payload);
        toast.success('Artifact source updated');
      } else {
        await api.createArtifactSource(payload);
        toast.success('Artifact source added');
      }
      setSourceDialogOpen(false);
      if (selectedClientId) setSources(await api.getArtifactSources(selectedClientId));
    } catch (err) {
      setSourceError(errorMessage(err, 'Failed to save the artifact source'));
    } finally {
      setSavingSource(false);
    }
  };

  const handleDeleteSource = async (source: ArtifactSource) => {
    if (!confirm(`Delete the ${source.environment} ${source.serverType} artifact source for ${source.applicationName}?`)) return;
    try {
      await api.deleteArtifactSource(source.id);
      setSources((prev) => prev.filter((s) => s.id !== source.id));
      toast.success('Artifact source deleted');
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to delete the artifact source'));
    }
  };

  const handleTestSource = async (source: ArtifactSource) => {
    setTestingSourceId(source.id);
    try {
      const result = await api.testArtifactSource(source.id);
      setSourceTests((prev) => ({ ...prev, [source.id]: result }));
    } catch (err) {
      setSourceTests((prev) => ({
        ...prev,
        [source.id]: { success: false, message: errorMessage(err, 'Artifact source test failed') },
      }));
    } finally {
      setTestingSourceId(null);
    }
  };

  const renderTestResult = (result: AzureDevOpsTestResult | null | undefined) =>
    result && (
      <div
        className={`flex items-start gap-2 rounded-lg border p-3 text-sm ${
          result.success ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-red-200 bg-red-50 text-red-700'
        }`}
      >
        {result.success ? <CheckCircle className="h-4 w-4 mt-0.5 flex-shrink-0" /> : <XCircle className="h-4 w-4 mt-0.5 flex-shrink-0" />}
        <span className="break-words">{result.message}</span>
      </div>
    );

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Azure DevOps"
          description={`Connect${selectedClient ? ` ${selectedClient.name}` : ''} to Azure DevOps and choose where Dev and UAT build artifacts come from`}
          icon={Cloud}
        />

        {loading ? (
          <LoadingState label="Loading Azure DevOps configuration..." />
        ) : (
          <>
            {/* Connection */}
            <Card>
              <CardHeader>
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <CardTitle className="flex items-center gap-2">
                      <Cloud className="h-5 w-5 text-primary" />
                      Connection
                    </CardTitle>
                    <CardDescription>
                      Uses a personal access token with <strong>Build (Read)</strong> and <strong>Code (Read)</strong> scopes.
                      The token is stored encrypted and is never shown again.
                    </CardDescription>
                  </div>
                  {connection?.lastTestSucceeded === true && (
                    <Badge variant="success">
                      <CheckCircle className="h-3 w-3" />
                      Connected
                    </Badge>
                  )}
                  {connection?.lastTestSucceeded === false && (
                    <Badge variant="destructive">
                      <XCircle className="h-3 w-3" />
                      Last test failed
                    </Badge>
                  )}
                </div>
              </CardHeader>
              <CardContent>
                <form onSubmit={handleSaveConnection} className="space-y-4">
                  <div className="grid gap-4 md:grid-cols-2">
                    <div className="space-y-2">
                      <Label htmlFor="organization">Organization *</Label>
                      <Input
                        id="organization"
                        placeholder="Transviti or https://dev.azure.com/Transviti"
                        value={connectionForm.organization}
                        onChange={(e) => setConnectionForm({ ...connectionForm, organization: e.target.value })}
                        required
                        disabled={savingConnection}
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="project">Project *</Label>
                      <Input
                        id="project"
                        placeholder="e.g., AuditVare_Release_2"
                        value={connectionForm.project}
                        onChange={(e) => setConnectionForm({ ...connectionForm, project: e.target.value })}
                        required
                        disabled={savingConnection}
                      />
                    </div>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="pat">Personal Access Token {connection ? '' : '*'}</Label>
                    <Input
                      id="pat"
                      type="password"
                      autoComplete="new-password"
                      placeholder={connection?.hasPersonalAccessToken ? 'Token saved — leave blank to keep it' : 'Paste the personal access token'}
                      value={connectionForm.personalAccessToken}
                      onChange={(e) => setConnectionForm({ ...connectionForm, personalAccessToken: e.target.value })}
                      required={!connection}
                      disabled={savingConnection}
                    />
                  </div>

                  {renderTestResult(connectionTest)}

                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <p className="text-xs text-muted-foreground">
                      {connection
                        ? `Saved by ${connection.updatedBy ?? connection.createdBy} on ${formatDate(connection.updatedAt ?? connection.createdAt)}`
                          + (connection.lastTestedAt ? ` · Last tested ${formatDate(connection.lastTestedAt)}` : '')
                        : 'Not configured yet'}
                    </p>
                    <div className="flex gap-2">
                      <Button type="button" variant="outline" onClick={handleTestConnection} disabled={testingConnection || savingConnection}>
                        {testingConnection ? <Loader2 className="h-4 w-4 animate-spin" /> : <PlugZap className="h-4 w-4" />}
                        Test Connection
                      </Button>
                      <Button type="submit" disabled={savingConnection}>
                        {savingConnection ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                        Save
                      </Button>
                    </div>
                  </div>
                </form>
              </CardContent>
            </Card>

            {/* Artifact sources */}
            <Card>
              <CardHeader>
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <CardTitle className="flex items-center gap-2">
                      <GitBranch className="h-5 w-5 text-primary" />
                      Artifact Sources
                    </CardTitle>
                    <CardDescription>
                      One source per application, environment and server type. Deploy App uses the source that matches the selected server.
                    </CardDescription>
                  </div>
                  <Button onClick={openCreateSource} disabled={applications.length === 0}>
                    <Plus className="h-4 w-4" />
                    Add Source
                  </Button>
                </div>
              </CardHeader>
              <CardContent>
                {applications.length === 0 ? (
                  <EmptyState compact icon={GitBranch} title="Create an application for this client first." />
                ) : sources.length === 0 ? (
                  <EmptyState compact icon={GitBranch} title="No artifact sources configured yet." />
                ) : (
                  <div className="space-y-3">
                    {sources.map((source) => (
                      <div key={source.id} className="rounded-lg border border-border p-4 space-y-3">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="space-y-1 min-w-0">
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-semibold">{source.applicationName}</span>
                              <Badge variant={source.environment === 'UAT' ? 'warning' : 'default'}>{source.environment}</Badge>
                              <Badge variant="secondary">{source.serverType}</Badge>
                            </div>
                            <p className="text-sm font-mono break-all">
                              {source.repository} @ {source.branch}
                            </p>
                            <p className="text-xs text-muted-foreground">
                              Pipeline: {source.pipelineDefinitionId ?? 'any'}
                              {source.windowsPipelineDefinitionId && ` · Windows pipeline: ${source.windowsPipelineDefinitionId}`} · Artifact: {source.artifactName || 'auto'} · Folder:{' '}
                              {source.artifactSubfolder || '(root)'} · web.config: {source.preserveWebConfig ? 'keep server copy' : 'overwrite'}
                              {source.keepOnServerPaths && ` · keeps ${source.keepOnServerPaths.split(';').length} path(s)`}
                              {source.runDatabaseMigrations && ` · DB migrations from '${source.sqlScriptsArtifactName ?? 'sqlscripts'}'`}
                            </p>
                          </div>
                          <div className="flex gap-1">
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => handleTestSource(source)}
                              disabled={testingSourceId === source.id}
                            >
                              {testingSourceId === source.id ? (
                                <Loader2 className="h-3 w-3 animate-spin" />
                              ) : (
                                <PlugZap className="h-3 w-3" />
                              )}
                              Test
                            </Button>
                            <Button variant="ghost" size="icon" onClick={() => openEditSource(source)}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                            <Button variant="ghost" size="icon" onClick={() => handleDeleteSource(source)}>
                              <Trash2 className="h-4 w-4 text-destructive" />
                            </Button>
                          </div>
                        </div>
                        {renderTestResult(sourceTests[source.id])}
                      </div>
                    ))}
                  </div>
                )}
              </CardContent>
            </Card>
          </>
        )}

        {/* Artifact source dialog */}
        <Dialog open={sourceDialogOpen} onOpenChange={setSourceDialogOpen}>
          <DialogContent className="max-h-[90vh] overflow-y-auto">
            <DialogHeader>
              <DialogTitle>{editingSource ? 'Edit Artifact Source' : 'Add Artifact Source'}</DialogTitle>
              <DialogDescription>Where DeployVare retrieves the latest successful build for this application</DialogDescription>
            </DialogHeader>
            <form onSubmit={handleSaveSource} className="space-y-4">
              {sourceError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{sourceError}</div>
              )}
              <div className="space-y-2">
                <Label htmlFor="sourceApplication">Application *</Label>
                <Select
                  id="sourceApplication"
                  value={sourceForm.applicationId}
                  onChange={(e) => setSourceForm({ ...sourceForm, applicationId: Number(e.target.value) })}
                  required
                  disabled={savingSource}
                >
                  {applications.map((app) => (
                    <option key={app.id} value={app.id}>
                      {app.name}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="sourceEnvironment">Environment *</Label>
                  <Select
                    id="sourceEnvironment"
                    value={sourceForm.environment}
                    onChange={(e) => setSourceForm({ ...sourceForm, environment: e.target.value as 'Dev' | 'UAT' })}
                    disabled={savingSource}
                  >
                    <option value="Dev">Dev</option>
                    <option value="UAT">UAT</option>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="sourceServerType">Server Type *</Label>
                  <Select
                    id="sourceServerType"
                    value={sourceForm.serverType}
                    onChange={(e) => setSourceForm({ ...sourceForm, serverType: e.target.value as 'Api' | 'Frontend' })}
                    disabled={savingSource}
                  >
                    <option value="Api">API (backend)</option>
                    <option value="Frontend">Frontend</option>
                  </Select>
                </div>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="sourceRepository">Repository *</Label>
                  <Input
                    id="sourceRepository"
                    placeholder="e.g., auditvare-dev-backend"
                    value={sourceForm.repository}
                    onChange={(e) => setSourceForm({ ...sourceForm, repository: e.target.value })}
                    required
                    disabled={savingSource}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="sourceBranch">Branch *</Label>
                  <Input
                    id="sourceBranch"
                    placeholder="main"
                    value={sourceForm.branch}
                    onChange={(e) => setSourceForm({ ...sourceForm, branch: e.target.value })}
                    required
                    disabled={savingSource}
                  />
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="sourcePipeline">Pipeline ID</Label>
                <Input
                  id="sourcePipeline"
                  type="number"
                  min="1"
                  placeholder="e.g., 30"
                  value={sourceForm.pipelineDefinitionId ?? ''}
                  onChange={(e) =>
                    setSourceForm({ ...sourceForm, pipelineDefinitionId: e.target.value ? Number(e.target.value) : null })
                  }
                  disabled={savingSource}
                />
                <p className="text-xs text-muted-foreground">
                  Recommended when more than one pipeline builds this repository and branch. Found in the pipeline URL (definitionId=…).
                </p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="sourceWindowsPipeline">Windows Pipeline ID</Label>
                <Input
                  id="sourceWindowsPipeline"
                  type="number"
                  min="1"
                  placeholder="Same as Pipeline ID"
                  value={sourceForm.windowsPipelineDefinitionId ?? ''}
                  onChange={(e) =>
                    setSourceForm({ ...sourceForm, windowsPipelineDefinitionId: e.target.value ? Number(e.target.value) : null })
                  }
                  disabled={savingSource}
                />
                <p className="text-xs text-muted-foreground">
                  Optional. Deploy App (IIS) and manual packages take Windows builds from this pipeline; Linux deployments keep the
                  Pipeline ID above.
                </p>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="sourceArtifact">Artifact Name</Label>
                  <Input
                    id="sourceArtifact"
                    placeholder="e.g., drop"
                    value={sourceForm.artifactName ?? ''}
                    onChange={(e) => setSourceForm({ ...sourceForm, artifactName: e.target.value })}
                    disabled={savingSource}
                  />
                  <p className="text-xs text-muted-foreground">Required if the build publishes several artifacts</p>
                </div>
                <div className="space-y-2">
                  <Label htmlFor="sourceSubfolder">Folder Inside Artifact</Label>
                  <Input
                    id="sourceSubfolder"
                    placeholder="e.g., AuditVare-WebApi"
                    value={sourceForm.artifactSubfolder ?? ''}
                    onChange={(e) => setSourceForm({ ...sourceForm, artifactSubfolder: e.target.value })}
                    disabled={savingSource}
                  />
                  <p className="text-xs text-muted-foreground">Leave empty to deploy the whole artifact</p>
                </div>
              </div>
              <label className="flex items-start gap-2 text-sm cursor-pointer">
                <input
                  type="checkbox"
                  className="mt-0.5 h-4 w-4 rounded border-input accent-primary"
                  checked={sourceForm.preserveWebConfig}
                  onChange={(e) => setSourceForm({ ...sourceForm, preserveWebConfig: e.target.checked })}
                  disabled={savingSource}
                />
                <span>
                  Keep the server&apos;s existing web.config
                  <span className="block text-xs text-muted-foreground">
                    The artifact&apos;s web.config is only used when the server has none
                  </span>
                </span>
              </label>
              <div className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <Label htmlFor="sourceKeepPaths">Keep on Server</Label>
                  {sourceForm.serverType === 'Api' && (
                    <button
                      type="button"
                      className="text-xs text-primary hover:underline"
                      onClick={() => setSourceForm({ ...sourceForm, keepOnServerPaths: BACKEND_KEEP_ON_SERVER_DEFAULTS })}
                      disabled={savingSource}
                    >
                      Use backend pipeline defaults
                    </button>
                  )}
                </div>
                <Textarea
                  id="sourceKeepPaths"
                  rows={3}
                  className="font-mono"
                  placeholder={'appsettings.json\nwwwroot\nTemplate'}
                  value={sourceForm.keepOnServerPaths ?? ''}
                  onChange={(e) => setSourceForm({ ...sourceForm, keepOnServerPaths: e.target.value })}
                  disabled={savingSource}
                />
                <p className="text-xs text-muted-foreground">
                  Files or folders relative to the site folder, one per line. When they already exist on the server they are
                  not overwritten (e.g. appsettings.json and upload folders).
                </p>
              </div>
              {sourceForm.serverType === 'Api' && (
                <div className="space-y-3 rounded-lg border border-border p-3">
                  <label className="flex items-start gap-2 text-sm cursor-pointer">
                    <input
                      type="checkbox"
                      className="mt-0.5 h-4 w-4 rounded border-input accent-primary"
                      checked={sourceForm.runDatabaseMigrations}
                      onChange={(e) => setSourceForm({ ...sourceForm, runDatabaseMigrations: e.target.checked })}
                      disabled={savingSource}
                    />
                    <span>
                      Run database migrations (DbMigrator) before deploying
                      <span className="block text-xs text-muted-foreground">
                        Runs DbMigrator.exe from the package with this build&apos;s SQL scripts against the server&apos;s linked
                        database instance (Server Setup → Database Access), using Windows authentication like the pipeline.
                        Servers without a linked database instance deploy normally and skip migrations.
                        It first checks the database: missing or empty databases are skipped (the deploy continues), while an
                        unreachable SQL Server or a database without DbMigrator&apos;s SchemaVersions history stops the deploy.
                        If anything fails, the site is not changed.
                      </span>
                    </span>
                  </label>
                  {sourceForm.runDatabaseMigrations && (
                    <div className="space-y-2">
                      <Label htmlFor="sourceScriptsArtifact">SQL Scripts Artifact Name</Label>
                      <Input
                        id="sourceScriptsArtifact"
                        placeholder="sqlscripts"
                        value={sourceForm.sqlScriptsArtifactName ?? ''}
                        onChange={(e) => setSourceForm({ ...sourceForm, sqlScriptsArtifactName: e.target.value })}
                        disabled={savingSource}
                      />
                    </div>
                  )}
                </div>
              )}
              <div className="flex justify-end gap-2">
                <Button type="button" variant="outline" onClick={() => setSourceDialogOpen(false)} disabled={savingSource}>
                  Cancel
                </Button>
                <Button type="submit" disabled={savingSource || !sourceForm.applicationId}>
                  {savingSource ? 'Saving...' : editingSource ? 'Save Changes' : 'Add Source'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}
