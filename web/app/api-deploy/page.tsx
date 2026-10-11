'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { ApplicationServer, ApiDeploymentResult } from '@/lib/api';
import {
  Upload,
  Server,
  CheckCircle2,
  XCircle,
  Loader2,
  Clock,
  AlertTriangle,
  MinusCircle,
  Circle,
  ListChecks,
} from 'lucide-react';

const DEPLOY_STEPS = ['Stop IIS services', 'Back up existing files', 'Deploy new files', 'Restart IIS services'];

// Step log lines are prefixed by the server with ✓ / ⚠ / ✗ / ⊘
function StepIcon({ step }: { step: string }) {
  const className = 'mt-0.5 h-3.5 w-3.5 shrink-0';
  if (step.startsWith('✓')) return <CheckCircle2 className={`${className} text-emerald-600`} />;
  if (step.startsWith('⚠')) return <AlertTriangle className={`${className} text-amber-600`} />;
  if (step.startsWith('✗')) return <XCircle className={`${className} text-red-600`} />;
  if (step.startsWith('⊘')) return <MinusCircle className={`${className} text-muted-foreground`} />;
  return <Circle className={`${className} text-muted-foreground/60`} />;
}

export default function ApiDeployPage() {
  const { selectedClientId } = useClientStore();
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [version, setVersion] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [deploying, setDeploying] = useState(false);
  const [deploymentResult, setDeploymentResult] = useState<ApiDeploymentResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (selectedClientId) loadServers();
  }, [selectedClientId]);

  const loadServers = async () => {
    try {
      const data = await api.getApplicationServers(selectedClientId);
      setServers(data.filter(s => s.isActive));
      setSelectedServerId(0);
    } catch (error: any) {
      console.error('Error loading servers:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setFiles(Array.from(e.target.files));
    }
  };

  const handleDeploy = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!selectedServerId) {
      alert('Please select a server');
      return;
    }

    if (files.length === 0) {
      alert('Please select files to deploy');
      return;
    }

    if (!confirm(`Deploy ${files.length} file(s) to server?\n\nThis will:\n1. Stop IIS services\n2. Backup existing files\n3. Deploy new files\n4. Restart IIS services`)) {
      return;
    }

    setDeploying(true);
    setDeploymentResult(null);

    try {
      const result = await api.deployApi(selectedServerId, files, version || undefined);
      setDeploymentResult(result);

      // Reset form on success
      if (result.status === 'Success') {
        setFiles([]);
        setVersion('');
        (document.getElementById('files') as HTMLInputElement).value = '';
      }
    } catch (error: any) {
      console.error('Deployment error:', error);
      setDeploymentResult({
        deploymentId: 0,
        status: 'Failed',
        message: 'Deployment failed',
        errorMessage: error.response?.data?.details || error.response?.data?.error || error.message,
        deploymentDurationSeconds: 0,
        deploymentSteps: [],
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        deployedFiles: [],
      });
    } finally {
      setDeploying(false);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const pageHeader = (
    <PageHeader
      title="Deploy API"
      description="Deploy DLL files and other API assets to Windows servers"
      icon={Upload}
    />
  );

  if (loading) {
    return (
      <ProtectedLayout requireClient>
        <div className="space-y-6">
          {pageHeader}
          <LoadingState label="Loading servers..." />
        </div>
      </ProtectedLayout>
    );
  }

  const selectedServer = servers.find(s => s.id === selectedServerId);

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        {pageHeader}

        <div className="grid gap-6 lg:grid-cols-3">
          {/* Main flow */}
          <div className="space-y-6 lg:col-span-2">
            {/* Deployment Form */}
            <form onSubmit={handleDeploy}>
              <Card>
                <CardHeader>
                  <CardTitle>Deployment Configuration</CardTitle>
                  <CardDescription>
                    Select target server and upload files to deploy
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-6">
                  {/* Server Selection */}
                  <div className="space-y-1.5">
                    <Label htmlFor="server">Target Server *</Label>
                    <Select
                      id="server"
                      value={selectedServerId}
                      onChange={(e) => setSelectedServerId(Number(e.target.value))}
                      required
                      disabled={deploying}
                    >
                      <option value={0}>Select a server...</option>
                      {servers.map((server) => (
                        <option key={server.id} value={server.id}>
                          {server.serverName} - {server.applicationName} ({server.hostName})
                        </option>
                      ))}
                    </Select>
                  </div>

                  {/* Server Info */}
                  {selectedServer && (
                    <div className="space-y-3 rounded-lg border border-border bg-muted/40 p-4 text-sm">
                      <div className="flex items-center gap-2 font-semibold text-foreground">
                        <Server className="h-4 w-4 text-muted-foreground" />
                        Server Details
                      </div>
                      <div className="grid grid-cols-1 gap-x-4 gap-y-2 sm:grid-cols-2">
                        <div>
                          <span className="text-muted-foreground">Website:</span>
                          <span className="ml-2 break-all font-mono text-xs">{selectedServer.websiteName}</span>
                        </div>
                        <div>
                          <span className="text-muted-foreground">App Pool:</span>
                          <span className="ml-2 break-all font-mono text-xs">{selectedServer.appPoolName}</span>
                        </div>
                        <div className="sm:col-span-2">
                          <span className="text-muted-foreground">Path:</span>
                          <span className="ml-2 break-all font-mono text-xs">{selectedServer.deploymentPath}</span>
                        </div>
                      </div>
                    </div>
                  )}

                  {/* Version */}
                  <div className="space-y-1.5">
                    <Label htmlFor="version">Version (Optional)</Label>
                    <Input
                      id="version"
                      value={version}
                      onChange={(e) => setVersion(e.target.value)}
                      placeholder="1.0.0"
                      disabled={deploying}
                    />
                  </div>

                  {/* File Upload */}
                  <div className="space-y-1.5">
                    <Label htmlFor="files">Files to Deploy *</Label>
                    <input
                      id="files"
                      type="file"
                      onChange={handleFileChange}
                      multiple
                      accept=".dll,.exe,.json,.pdb,.config,.xml"
                      disabled={deploying}
                      className="block w-full text-sm text-muted-foreground file:mr-4 file:rounded-md file:border-0 file:bg-primary/10 file:px-4 file:py-2 file:text-sm file:font-medium file:text-primary hover:file:bg-primary/15 disabled:opacity-50"
                    />
                    <p className="text-xs text-muted-foreground">
                      Select DLL, EXE, JSON, PDB, and config files
                    </p>
                  </div>

                  {/* Selected Files */}
                  {files.length > 0 && (
                    <div className="space-y-2">
                      <Label>Selected Files ({files.length})</Label>
                      <ul className="max-h-48 divide-y divide-border overflow-y-auto rounded-lg border border-border">
                        {files.map((file, index) => (
                          <li key={index} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
                            <span className="flex-1 truncate font-mono text-xs">{file.name}</span>
                            <span className="text-xs text-muted-foreground">{formatFileSize(file.size)}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {/* Deploy Button */}
                  <div className="border-t border-border pt-6">
                    <Button
                      type="submit"
                      className="w-full"
                      disabled={deploying || !selectedServerId || files.length === 0}
                    >
                      {deploying ? (
                        <>
                          <Loader2 className="h-4 w-4 animate-spin" />
                          Deploying...
                        </>
                      ) : (
                        <>
                          <Upload className="h-4 w-4" />
                          Deploy to Server
                        </>
                      )}
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </form>

            {servers.length === 0 && (
              <EmptyState
                icon={Server}
                title="No servers configured"
                description="Add a server first to start deploying"
                action={
                  <Button onClick={() => window.location.href = '/servers'}>
                    Go to Servers
                  </Button>
                }
              />
            )}
          </div>

          {/* Side column */}
          <div className="space-y-6 self-start lg:sticky lg:top-24">
            <Card>
              <CardHeader>
                <div className="flex items-center gap-2">
                  {deploying ? (
                    <Loader2 className="h-4 w-4 animate-spin text-primary" />
                  ) : (
                    <ListChecks className="h-4 w-4 text-muted-foreground" />
                  )}
                  <CardTitle>Deployment Steps</CardTitle>
                </div>
                <CardDescription>What happens when files are deployed</CardDescription>
              </CardHeader>
              <CardContent>
                <ol className="space-y-2">
                  {DEPLOY_STEPS.map((step, index) => (
                    <li key={step} className="flex items-start gap-3 text-sm text-muted-foreground">
                      <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-muted text-[11px] font-medium text-foreground">
                        {index + 1}
                      </span>
                      <span className="pt-px">{step}</span>
                    </li>
                  ))}
                </ol>
                <p className="mt-4 border-t border-border pt-4 text-xs text-muted-foreground">
                  Accepted file types: .dll, .exe, .json, .pdb, .config, .xml
                </p>
              </CardContent>
            </Card>
          </div>
        </div>

        {/* Deployment Result */}
        {deploymentResult && (
          <Card className={deploymentResult.status === 'Success' ? 'border-emerald-200' : 'border-red-200'}>
            <CardHeader>
              <div className="flex items-center gap-3">
                {deploymentResult.status === 'Success' ? (
                  <CheckCircle2 className="h-6 w-6 shrink-0 text-emerald-600" />
                ) : (
                  <XCircle className="h-6 w-6 shrink-0 text-red-600" />
                )}
                <div className="min-w-0 flex-1">
                  <CardTitle>
                    {deploymentResult.status === 'Success' ? 'Deployment Successful' : 'Deployment Failed'}
                  </CardTitle>
                  <CardDescription>{deploymentResult.message}</CardDescription>
                </div>
                <div className="flex items-center gap-3">
                  <span className="hidden items-center gap-1.5 text-sm text-muted-foreground sm:inline-flex">
                    <Clock className="h-4 w-4" />
                    Duration: {deploymentResult.deploymentDurationSeconds}s
                  </span>
                  <Badge variant={deploymentResult.status === 'Success' ? 'success' : 'destructive'}>
                    {deploymentResult.status}
                  </Badge>
                </div>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              {/* Duration (small screens) */}
              <div className="flex items-center gap-2 text-sm text-muted-foreground sm:hidden">
                <Clock className="h-4 w-4" />
                <span>Duration: {deploymentResult.deploymentDurationSeconds}s</span>
              </div>

              {/* Error Message */}
              {deploymentResult.errorMessage && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  <p className="font-medium text-red-800">Error:</p>
                  <p className="mt-1 break-words font-mono text-xs">{deploymentResult.errorMessage}</p>
                </div>
              )}

              {/* Backup Info */}
              {deploymentResult.backupFileName && (
                <div className="rounded-lg border border-blue-200 bg-blue-50/60 p-4 text-sm">
                  <p className="font-medium text-blue-900">Backup Created:</p>
                  <p className="mt-1 break-all font-mono text-xs text-blue-700">{deploymentResult.backupFileName}</p>
                </div>
              )}

              <div className="grid gap-4 lg:grid-cols-2">
                {/* Deployment Steps */}
                {deploymentResult.deploymentSteps.length > 0 && (
                  <div className="space-y-2">
                    <p className="text-sm font-medium text-foreground">Deployment Steps:</p>
                    <ul className="divide-y divide-border rounded-lg border border-border">
                      {deploymentResult.deploymentSteps.map((step, index) => (
                        <li key={index} className="flex items-start gap-2 px-3 py-2 font-mono text-xs">
                          <StepIcon step={step} />
                          <span className="break-words">{step}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                {/* Deployed Files */}
                {deploymentResult.deployedFiles.length > 0 && (
                  <div className="space-y-2">
                    <p className="text-sm font-medium text-foreground">
                      Deployed Files ({deploymentResult.deployedFiles.length}):
                    </p>
                    <div className="max-h-96 overflow-auto rounded-lg border border-border">
                      <table className="w-full text-sm">
                        <thead className="bg-muted/50 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                          <tr>
                            <th className="px-3 py-2 text-left">File</th>
                            <th className="px-3 py-2 text-right">Size</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-border">
                          {deploymentResult.deployedFiles.map((file) => (
                            <tr key={file.id}>
                              <td className="px-3 py-2 font-mono text-xs">{file.fileName}</td>
                              <td className="whitespace-nowrap px-3 py-2 text-right text-xs text-muted-foreground">
                                {formatFileSize(file.fileSize)}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )}
              </div>
            </CardContent>
          </Card>
        )}
      </div>
    </ProtectedLayout>
  );
}
