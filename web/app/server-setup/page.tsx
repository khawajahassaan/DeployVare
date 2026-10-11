'use client';

import { Suspense, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import api, {
  ApplicationServer,
  InstallerFile,
  ReadinessItem,
  ServerReadinessReport,
  ServerSetupActionResult,
  DatabaseInstance,
  SqlServerInfo,
  SqlServerConnection,
  SqlDatabaseUser,
  Application,
  FrontendRuntimeConfig,
  RuntimeSetting,
} from '@/lib/api';
import { useClientStore } from '@/store/clientStore';
import {
  ArrowLeft,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  MinusCircle,
  Loader2,
  RefreshCw,
  ShieldCheck,
  Wrench,
  Upload,
  Lock,
  Package,
  Database,
  KeyRound,
  Copy,
  Eye,
  EyeOff,
  Users,
  PlusCircle,
  FileCode,
  Trash2,
  Server,
} from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { toast } from 'sonner';

const DEFAULT_CERTIFICATE = 'WMSVC-SHA2';

// Random password without characters that break connection strings (; ' " = { } and spaces)
const generatePassword = (length = 20): string => {
  const sets = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@#$%^&*-_+?'];
  const all = sets.join('');
  const random = new Uint32Array(length * 2);
  crypto.getRandomValues(random);
  const chars = sets.map((set, i) => set[random[i] % set.length]);
  for (let i = sets.length; i < length; i++) chars.push(all[random[i] % all.length]);
  for (let i = chars.length - 1; i > 0; i--) {
    const j = random[length + i] % (i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join('');
};

const errorMessage = (err: any, fallback: string): string =>
  err?.response?.data?.message || err?.response?.data?.error || fallback;

function StatusIcon({ status }: { status: ReadinessItem['status'] }) {
  switch (status) {
    case 'Pass':
      return <CheckCircle2 className="h-5 w-5 text-emerald-600 flex-shrink-0" />;
    case 'Fail':
      return <XCircle className="h-5 w-5 text-red-600 flex-shrink-0" />;
    case 'Warning':
      return <AlertTriangle className="h-5 w-5 text-amber-500 flex-shrink-0" />;
    default:
      return <MinusCircle className="h-5 w-5 text-gray-400 flex-shrink-0" />;
  }
}

function ServerSetupContent() {
  const searchParams = useSearchParams();
  const serverId = Number(searchParams?.get('serverId') || 0);
  const { selectedClientId } = useClientStore();

  const [server, setServer] = useState<ApplicationServer | null>(null);
  const [report, setReport] = useState<ServerReadinessReport | null>(null);
  const [installers, setInstallers] = useState<InstallerFile[]>([]);
  const [checking, setChecking] = useState(false);
  const [loadingSaved, setLoadingSaved] = useState(true);
  const [checkError, setCheckError] = useState('');
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<{ title: string; result: ServerSetupActionResult } | null>(null);

  // Site creation
  const [showSiteForm, setShowSiteForm] = useState(false);
  const [siteForm, setSiteForm] = useState({ physicalPath: '', hostName: '', useAsDeploymentPath: true });

  // SSL certificate and bindings
  const [certificateMode, setCertificateMode] = useState<'existing' | 'upload'>('existing');
  const [selectedThumbprint, setSelectedThumbprint] = useState('');
  const [certificateFile, setCertificateFile] = useState<File | null>(null);
  const [certificatePassword, setCertificatePassword] = useState('');
  const [bindingHost, setBindingHost] = useState('');
  const [requireSni, setRequireSni] = useState(false);

  // Database access
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [selectedInstanceId, setSelectedInstanceId] = useState(0);

  // SQL Server login
  const [sqlInfo, setSqlInfo] = useState<SqlServerInfo | null>(null);
  const [sqlChecking, setSqlChecking] = useState(false);
  const [sqlError, setSqlError] = useState('');
  const [sqlForm, setSqlForm] = useState({
    instanceName: '',
    databaseName: '',
    loginName: '',
    password: '',
    resetPasswordIfExists: false,
  });
  const [showSqlPassword, setShowSqlPassword] = useState(false);
  const [sqlAdmin, setSqlAdmin] = useState({ useWindowsAuthentication: false, adminLogin: '', adminPassword: '' });
  const [sqlUsers, setSqlUsers] = useState<SqlDatabaseUser[] | null>(null);
  const [sqlUsersLoading, setSqlUsersLoading] = useState(false);

  // Login just created: offered as a DeployVare database instance (password kept only in page memory)
  const [applications, setApplications] = useState<Application[]>([]);
  const [createdLogin, setCreatedLogin] = useState<{ databaseName: string; loginName: string; password: string } | null>(null);
  const [instanceForm, setInstanceForm] = useState({ applicationId: 0, instanceName: '', serverAddress: '', currentPatchNumber: 0 });
  const [addingInstance, setAddingInstance] = useState(false);

  // Frontend runtime settings (config.js)
  const [runtimeConfig, setRuntimeConfig] = useState<FrontendRuntimeConfig | null>(null);
  const [runtimeRows, setRuntimeRows] = useState<RuntimeSetting[]>([]);
  const [runtimeSaving, setRuntimeSaving] = useState(false);

  useEffect(() => {
    if (serverId) {
      loadServer();
      loadSavedReport();
      loadRuntimeConfig();
      if (selectedClientId) api.getApplications(selectedClientId).then(setApplications).catch(() => setApplications([]));
      api.getServerInstallers().then(setInstallers).catch(() => setInstallers([]));
    }
  }, [serverId]);

  const loadServer = async () => {
    try {
      const data = await api.getApplicationServer(serverId);
      setServer(data);
      api.getDatabaseInstancesByApp(data.applicationId).then(setInstances).catch(() => setInstances([]));
      setSiteForm((form) => ({ ...form, physicalPath: form.physicalPath || data.deploymentPath }));
    } catch (err) {
      setCheckError(errorMessage(err, 'Server not found'));
    }
  };

  const applyReport = (result: ServerReadinessReport) => {
    setReport(result);
    setSelectedInstanceId(result.database?.databaseInstanceId ?? 0);

    // Preselect the certificate already bound to https, else the default WMSVC-SHA2, else the first valid one
    const valid = result.certificates.filter((c) => !c.isExpired);
    const boundThumbprint = result.bindings.find((b) => b.protocol === 'https' && b.certificateThumbprint)?.certificateThumbprint;
    const preferred =
      [boundThumbprint, valid.find((c) => c.friendlyName === DEFAULT_CERTIFICATE)?.thumbprint, valid[0]?.thumbprint]
        .find((t) => t && valid.some((c) => c.thumbprint === t)) ?? '';
    setSelectedThumbprint((prev) => (prev && valid.some((c) => c.thumbprint === prev) ? prev : preferred));
    if (valid.length === 0) setCertificateMode('upload');

    const host = result.bindings.find((b) => b.hostName)?.hostName;
    if (host) {
      setBindingHost((prev) => prev || host);
      setSiteForm((form) => ({ ...form, hostName: form.hostName || host }));
    }
  };

  // Shows the last saved check; the server is only inspected again when the user runs the check
  const loadSavedReport = async () => {
    setLoadingSaved(true);
    try {
      const saved = await api.getServerReadiness(serverId);
      if (saved) applyReport(saved);
    } catch (err) {
      setCheckError(errorMessage(err, 'Could not load the last readiness check'));
    } finally {
      setLoadingSaved(false);
    }
  };

  const runCheck = async () => {
    setChecking(true);
    setCheckError('');
    try {
      applyReport(await api.runServerReadinessCheck(serverId));
    } catch (err) {
      setCheckError(errorMessage(err, 'The readiness check failed'));
    } finally {
      setChecking(false);
    }
  };

  const runAction = async (
    key: string,
    title: string,
    action: () => Promise<ServerSetupActionResult>,
    recheckOnSuccess = true
  ) => {
    setBusyAction(key);
    setLastResult(null);
    try {
      const result = await action();
      setLastResult({ title, result });
      if (result.success) {
        toast.success(`${title} completed`);
        if (recheckOnSuccess) await runCheck();
      } else {
        toast.error(result.message);
      }
      return result;
    } catch (err) {
      const result: ServerSetupActionResult = {
        success: false,
        message: errorMessage(err, `${title} failed`),
        rebootRequired: false,
        steps: [],
      };
      setLastResult({ title, result });
      toast.error(result.message);
      return result;
    } finally {
      setBusyAction(null);
    }
  };

  const handleInstallIis = () => {
    if (!confirm(`Install IIS on ${server?.serverName}? This can take several minutes.`)) return;
    runAction('iis', 'Install IIS', () => api.installIis(serverId));
  };

  const handleInstallComponent = (item: ReadinessItem) => {
    if (!item.component) return;
    if (!confirm(`Install ${item.name} on ${server?.serverName}? The installer runs silently and can take several minutes.`)) return;
    runAction(item.key, `Install ${item.name}`, () => api.installServerComponent(serverId, item.component!));
  };

  const handleCreateSite = async (e: React.FormEvent) => {
    e.preventDefault();
    const result = await runAction('site', 'Create site', () => api.createServerSite(serverId, siteForm));
    if (result.success) {
      setShowSiteForm(false);
      setBindingHost((prev) => prev || siteForm.hostName);
      loadServer();
    }
  };

  const handleUploadCertificate = async () => {
    if (!certificateFile) {
      toast.error('Choose a .pfx certificate file');
      return;
    }
    const result = await runAction('certificate', 'Install certificate', () =>
      api.installServerCertificate(serverId, certificateFile, certificatePassword || undefined)
    );
    if (result.success) {
      // The imported certificate's thumbprint is returned as the message
      setSelectedThumbprint(result.message.toUpperCase());
      setCertificateMode('existing');
      setCertificateFile(null);
      setCertificatePassword('');
    }
  };

  const handleApplyBindings = () => {
    if (!bindingHost.trim()) {
      toast.error('Enter the site URL host name');
      return;
    }
    if (!selectedThumbprint) {
      toast.error('Select or upload a certificate');
      return;
    }
    runAction('bindings', 'Configure bindings', () =>
      api.configureServerBindings(serverId, { hostName: bindingHost.trim(), certificateThumbprint: selectedThumbprint, requireSni })
    );
  };

  const handleSyncPath = async () => {
    const result = await runAction('deployment-path', 'Update deployment path', () => api.syncServerDeploymentPath(serverId));
    if (result.success) loadServer();
  };

  const handleLinkDatabase = () =>
    runAction('db-link', 'Link database instance', () =>
      api.linkServerDatabaseInstance(serverId, selectedInstanceId || null)
    );

  const handleGrantDatabase = () => {
    if (!report?.database) return;
    if (!confirm(`Grant ${report.database.loginName} db_owner on ${report.database.databaseName}?`)) return;
    runAction('db-grant', 'Grant database access', () => api.grantServerDatabaseAccess(serverId));
  };

  // Admin connection details entered by the user; null until a login and password (or Windows auth) are provided
  const sqlConnection = (): SqlServerConnection | null =>
    sqlAdmin.useWindowsAuthentication
      ? { useWindowsAuthentication: true }
      : sqlAdmin.adminLogin.trim() && sqlAdmin.adminPassword
        ? { useWindowsAuthentication: false, adminLogin: sqlAdmin.adminLogin.trim(), adminPassword: sqlAdmin.adminPassword }
        : null;

  // Empty servers start with the standard keys so the user only fills in values
  const toRuntimeRows = (config: FrontendRuntimeConfig): RuntimeSetting[] =>
    config.settings.length > 0 ? config.settings : config.standardKeys.map((key) => ({ key, value: '' }));

  const loadRuntimeConfig = async () => {
    try {
      const config = await api.getServerRuntimeConfig(serverId);
      setRuntimeConfig(config);
      setRuntimeRows(toRuntimeRows(config));
    } catch {
      setRuntimeConfig(null);
    }
  };

  const updateRuntimeRow = (index: number, patch: Partial<RuntimeSetting>) =>
    setRuntimeRows((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  const removeRuntimeRow = (index: number) => setRuntimeRows((rows) => rows.filter((_, i) => i !== index));

  const addRuntimeRow = () => setRuntimeRows((rows) => [...rows, { key: '', value: '' }]);

  const saveRuntimeConfig = async (): Promise<boolean> => {
    setRuntimeSaving(true);
    try {
      const config = await api.saveServerRuntimeConfig(serverId, runtimeRows);
      setRuntimeConfig(config);
      setRuntimeRows(toRuntimeRows(config));
      toast.success('Runtime settings saved');
      return true;
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to save runtime settings'));
      return false;
    } finally {
      setRuntimeSaving(false);
    }
  };

  const handleSaveAndApplyRuntimeConfig = async () => {
    if (!(await saveRuntimeConfig())) return;
    if (!confirm(`Write config.js to ${server?.serverName} now?\n\nThe site uses the new values on the next page load.`)) return;
    runAction('runtime-apply', 'Apply config.js', () => api.applyServerRuntimeConfig(serverId), false);
  };

  const scrollToRuntimeConfig = () =>
    document.getElementById('runtime-config-setup')?.scrollIntoView({ behavior: 'smooth' });

  const handleCheckSqlServer = async () => {
    setSqlChecking(true);
    setSqlError('');
    try {
      const info = await api.checkServerSqlServer(serverId, sqlConnection());
      setSqlInfo(info);
      const instance =
        info.instances.find((i) => i.name === sqlForm.instanceName) ?? info.instances.find((i) => i.running) ?? info.instances[0];
      setSqlForm((form) => ({
        ...form,
        instanceName: instance?.name ?? '',
        databaseName: instance?.databases.includes(form.databaseName) ? form.databaseName : instance?.databases[0] ?? '',
      }));
    } catch (err) {
      setSqlError(errorMessage(err, 'Could not check SQL Server'));
    } finally {
      setSqlChecking(false);
    }
  };

  const handleCreateSqlLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    const connection = sqlConnection();
    if (!connection) {
      toast.error('Enter the SQL admin login and password used to connect');
      return;
    }
    if (!confirm(`Create SQL login ${sqlForm.loginName} with db_owner on ${sqlForm.databaseName}?`)) return;
    const result = await runAction(
      'sql-login',
      'Create SQL login',
      () => api.createServerSqlLogin(serverId, { ...connection, ...sqlForm }),
      false
    );
    if (result.success) {
      setShowSqlPassword(true);
      setCreatedLogin({ databaseName: sqlForm.databaseName, loginName: sqlForm.loginName.trim(), password: sqlForm.password });
      const namedInstance = sqlForm.instanceName && sqlForm.instanceName.toUpperCase() !== 'MSSQLSERVER';
      setInstanceForm({
        applicationId: server?.applicationId ?? applications[0]?.id ?? 0,
        instanceName: server?.serverName ?? sqlForm.databaseName,
        serverAddress: `${server?.hostName ?? ''}${namedInstance ? `\\${sqlForm.instanceName}` : ''}`,
        currentPatchNumber: 0,
      });
      loadSqlUsers();
    }
  };

  const loadSqlUsers = async () => {
    const connection = sqlConnection();
    if (!connection || !sqlForm.instanceName || !sqlForm.databaseName) return;
    setSqlUsersLoading(true);
    try {
      setSqlUsers(await api.getServerSqlDatabaseUsers(serverId, {
        ...connection,
        instanceName: sqlForm.instanceName,
        databaseName: sqlForm.databaseName,
      }));
    } catch (err) {
      toast.error(errorMessage(err, 'Could not load database users'));
    } finally {
      setSqlUsersLoading(false);
    }
  };

  // Connection string values containing separators or quotes must be quoted
  const connectionStringValue = (value: string) =>
    /[;'"=\s{}]/.test(value) ? (value.includes('"') ? `'${value.replace(/'/g, "''")}'` : `"${value}"`) : value;

  const buildConnectionString = (maskPassword: boolean) =>
    createdLogin
      ? [
          `Server=${connectionStringValue(instanceForm.serverAddress.trim())}`,
          `Database=${connectionStringValue(createdLogin.databaseName)}`,
          `User Id=${connectionStringValue(createdLogin.loginName)}`,
          `Password=${maskPassword ? '********' : connectionStringValue(createdLogin.password)}`,
          'Encrypt=True',
          'TrustServerCertificate=True',
          'MultipleActiveResultSets=True',
        ].join(';')
      : '';

  const handleAddInstance = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createdLogin) return;
    setAddingInstance(true);
    try {
      const instance = await api.createDatabaseInstance({
        applicationId: instanceForm.applicationId,
        instanceName: instanceForm.instanceName.trim(),
        connectionString: buildConnectionString(false),
        currentPatchNumber: instanceForm.currentPatchNumber,
      });
      toast.success(`Database instance '${instance.instanceName}' added`);
      setCreatedLogin(null);
      setSqlForm((form) => ({ ...form, password: '' }));
      if (server) api.getDatabaseInstancesByApp(server.applicationId).then(setInstances).catch(() => undefined);
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to add the database instance'));
    } finally {
      setAddingInstance(false);
    }
  };

  const handleEnableSqlAuthentication = async (instanceName: string) => {
    if (!confirm(`Enable SQL Server and Windows authentication on '${instanceName}'?\n\nThe SQL Server service restarts, which briefly disconnects applications using it.`)) return;
    const result = await runAction(
      'sql-auth',
      'Enable SQL authentication',
      () => api.enableServerSqlAuthentication(serverId, instanceName),
      false
    );
    if (result.success) handleCheckSqlServer();
  };

  const copySqlPassword = async () => {
    try {
      await navigator.clipboard.writeText(sqlForm.password);
      toast.success('Password copied');
    } catch {
      toast.error('Could not copy the password');
    }
  };

  const scrollToDatabase = () => document.getElementById('database-setup')?.scrollIntoView({ behavior: 'smooth' });

  const scrollToSsl = () => document.getElementById('ssl-setup')?.scrollIntoView({ behavior: 'smooth' });

  const busy = checking || busyAction !== null;
  const siteExists = report?.items.find((i) => i.key === 'site')?.status === 'Pass';
  const iisInstalled = report?.items.find((i) => i.key === 'iis')?.status !== 'Fail';
  const hostPlaceholder = server?.serverType === 'Frontend' ? 'e.g., dev.auditvare.cloud' : 'e.g., dev-api.auditvare.cloud';

  const renderFixButton = (item: ReadinessItem) => {
    if (item.status === 'Pass' || !item.fix) return null;
    const loading = busyAction === item.key;
    const icon = loading ? <Loader2 className="h-3 w-3 animate-spin" /> : <Wrench className="h-3 w-3" />;

    switch (item.fix) {
      case 'InstallIis':
        return <Button size="sm" onClick={handleInstallIis} disabled={busy}>{icon}Install IIS</Button>;
      case 'InstallComponent':
        return (
          <Button size="sm" onClick={() => handleInstallComponent(item)} disabled={busy || !item.installerAvailable}>
            {icon}Install
          </Button>
        );
      case 'CreateSite':
        return (
          <Button size="sm" onClick={() => setShowSiteForm(true)} disabled={busy || !iisInstalled}>
            <Wrench className="h-3 w-3" />Create Site
          </Button>
        );
      case 'ConfigureCertificate':
      case 'ConfigureBindings':
        return (
          <Button size="sm" variant="outline" onClick={scrollToSsl} disabled={busy}>
            <Lock className="h-3 w-3" />Configure SSL
          </Button>
        );
      case 'ConfigureRuntimeSettings':
        return (
          <Button size="sm" variant="outline" onClick={scrollToRuntimeConfig} disabled={busy}>
            <FileCode className="h-3 w-3" />Configure
          </Button>
        );
      case 'ConfigureDatabase':
        return (
          <Button size="sm" variant="outline" onClick={scrollToDatabase} disabled={busy}>
            <Database className="h-3 w-3" />Select Database
          </Button>
        );
      case 'GrantDatabaseAccess':
        return <Button size="sm" onClick={handleGrantDatabase} disabled={busy}>{icon}Grant Access</Button>;
      case 'UpdateDeploymentPath':
        return <Button size="sm" variant="outline" onClick={handleSyncPath} disabled={busy}>{icon}Use site folder</Button>;
      default:
        return null;
    }
  };

  if (!serverId) {
    return (
      <ProtectedLayout requireClient>
        <div className="space-y-6">
          <PageHeader title="Server Setup" icon={Wrench} />
          <EmptyState
            icon={Server}
            title="No server selected."
            action={<Link href="/servers"><Button>Go to Servers</Button></Link>}
          />
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <div className="space-y-3">
          <Link href="/servers" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="h-4 w-4" />
            Servers
          </Link>
          <PageHeader
            title="Server Setup"
            description={
              server
                ? `${server.serverName} · ${server.applicationName} · ${server.serverType} · ${server.hostName}`
                : 'Loading server...'
            }
            icon={Wrench}
            actions={
              <Button variant="outline" onClick={runCheck} disabled={busy}>
                <RefreshCw className={`h-4 w-4 ${checking ? 'animate-spin' : ''}`} />
                {report ? 'Run Check Again' : 'Run Check'}
              </Button>
            }
          />
        </div>

        {/* Summary */}
        <Card className={report ? (report.isReady ? 'border-emerald-300' : 'border-amber-300') : ''}>
          <CardHeader>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                {checking ? (
                  <Loader2 className="h-6 w-6 animate-spin text-primary" />
                ) : report?.isReady ? (
                  <ShieldCheck className="h-6 w-6 text-emerald-600" />
                ) : (
                  <AlertTriangle className="h-6 w-6 text-amber-500" />
                )}
                <div>
                  <CardTitle>
                    {checking
                      ? 'Checking server...'
                      : !report
                        ? loadingSaved ? 'Loading last check...' : 'Not checked yet'
                        : report.isReady
                          ? 'Server is ready for deployment'
                          : 'Server needs setup before deployment'}
                  </CardTitle>
                  <CardDescription>
                    {report
                      ? `${report.operatingSystem ?? 'Windows'} · last checked ${formatDate(report.checkedAt)}${report.checkedBy ? ` by ${report.checkedBy}` : ''}`
                      : 'Run Check to inspect IIS, runtimes, Redis, SSL, bindings and database access over SSH. The result is saved for next time.'}
                  </CardDescription>
                </div>
              </div>
              {report && (
                <Badge variant={report.isReady ? 'success' : 'warning'}>{report.isReady ? 'Ready' : 'Not ready'}</Badge>
              )}
            </div>
          </CardHeader>
          {(checkError || report?.rebootPending) && (
            <CardContent className="space-y-2">
              {checkError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{checkError}</div>
              )}
              {report?.rebootPending && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                  Windows reports a pending restart. Restart the server so installed components take effect.
                </div>
              )}
            </CardContent>
          )}
        </Card>

        {/* Checklist */}
        {report && (
          <Card>
            <CardHeader>
              <CardTitle>Checklist</CardTitle>
              <CardDescription>
                {report.serverType === 'Api'
                  ? 'API server: IIS, site, .NET Hosting Bundle, .NET SDK, Redis, SSL and bindings'
                  : 'Frontend server: IIS, site, URL Rewrite, SSL and bindings'}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="divide-y divide-border">
                {report.items.map((item) => (
                  <div key={item.key} className="flex flex-wrap items-start justify-between gap-3 py-3">
                    <div className="flex items-start gap-3 min-w-0 flex-1">
                      <StatusIcon status={item.status} />
                      <div className="min-w-0">
                        <p className="font-medium">
                          {item.name}
                          {!item.required && <span className="ml-2 text-xs font-normal text-muted-foreground">(optional)</span>}
                        </p>
                        <p className="text-sm text-muted-foreground break-words">{item.detail}</p>
                      </div>
                    </div>
                    {renderFixButton(item)}
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        )}

        {/* Create site */}
        {showSiteForm && (
          <Card>
            <CardHeader>
              <CardTitle>Create IIS Site</CardTitle>
              <CardDescription>
                Creates application pool <strong>{server?.appPoolName}</strong> (No Managed Code) and site{' '}
                <strong>{server?.websiteName}</strong> with an http binding on port 80.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <form onSubmit={handleCreateSite} className="space-y-4">
                <div className="grid gap-4 md:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="physicalPath">Physical Folder *</Label>
                    <Input
                      id="physicalPath"
                      placeholder="C:\inetpub\wwwroot\AuditVareDevBackend"
                      value={siteForm.physicalPath}
                      onChange={(e) => setSiteForm({ ...siteForm, physicalPath: e.target.value })}
                      required
                      disabled={busy}
                    />
                    <p className="text-xs text-muted-foreground">Created if it does not exist. The app pool identity gets read access.</p>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="siteHost">Site URL ({server?.serverType === 'Frontend' ? 'frontend' : 'backend'} host name) *</Label>
                    <Input
                      id="siteHost"
                      placeholder={hostPlaceholder}
                      value={siteForm.hostName}
                      onChange={(e) => setSiteForm({ ...siteForm, hostName: e.target.value })}
                      required
                      disabled={busy}
                    />
                  </div>
                </div>
                <label className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="checkbox"
                    checked={siteForm.useAsDeploymentPath}
                    onChange={(e) => setSiteForm({ ...siteForm, useAsDeploymentPath: e.target.checked })}
                    disabled={busy}
                  />
                  Use this folder as the DeployVare deployment path
                </label>
                <div className="flex justify-end gap-2">
                  <Button type="button" variant="outline" onClick={() => setShowSiteForm(false)} disabled={busy}>
                    Cancel
                  </Button>
                  <Button type="submit" disabled={busy}>
                    {busyAction === 'site' && <Loader2 className="h-4 w-4 animate-spin" />}
                    Create Site
                  </Button>
                </div>
              </form>
            </CardContent>
          </Card>
        )}

        {/* SSL certificate and bindings */}
        {report && (
          <Card id="ssl-setup">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Lock className="h-5 w-5 text-primary" />
                SSL Certificate &amp; Bindings
              </CardTitle>
              <CardDescription>
                Use the default {DEFAULT_CERTIFICATE} certificate or install your own, then bind the site URL on ports 80 and 443.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              {report.bindings.length > 0 && (
                <div className="overflow-x-auto rounded-lg border border-border">
                  <table className="w-full text-sm">
                    <thead className="bg-muted/50 text-left text-xs font-medium uppercase tracking-wide text-muted-foreground">
                      <tr className="border-b border-border">
                        <th className="px-3 py-2 font-medium">Type</th>
                        <th className="px-3 py-2 font-medium">Host name</th>
                        <th className="px-3 py-2 font-medium">Port</th>
                        <th className="px-3 py-2 font-medium">Certificate</th>
                      </tr>
                    </thead>
                    <tbody>
                      {report.bindings.map((binding) => (
                        <tr key={`${binding.protocol}-${binding.bindingInformation}`} className="border-b border-border last:border-0 hover:bg-muted/40">
                          <td className="px-3 py-2">{binding.protocol}</td>
                          <td className="px-3 py-2 font-mono text-xs">{binding.hostName ?? '*'}</td>
                          <td className="px-3 py-2">{binding.port}</td>
                          <td className="px-3 py-2 font-mono text-xs">
                            {binding.certificateThumbprint
                              ? report.certificates.find((c) => c.thumbprint === binding.certificateThumbprint)?.friendlyName
                                ?? binding.certificateThumbprint.slice(0, 12) + '…'
                              : '—'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <button
                  type="button"
                  className={`rounded-lg border p-3 text-left transition-colors ${certificateMode === 'existing' ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'border-border hover:bg-muted/40'}`}
                  onClick={() => setCertificateMode('existing')}
                  disabled={busy}
                >
                  <span className="block text-sm font-medium">Use an installed certificate</span>
                  <span className="block text-xs text-muted-foreground">Default {DEFAULT_CERTIFICATE} or another certificate on the server</span>
                </button>
                <button
                  type="button"
                  className={`rounded-lg border p-3 text-left transition-colors ${certificateMode === 'upload' ? 'border-primary bg-primary/5 ring-1 ring-primary' : 'border-border hover:bg-muted/40'}`}
                  onClick={() => setCertificateMode('upload')}
                  disabled={busy}
                >
                  <span className="block text-sm font-medium">Install my own certificate</span>
                  <span className="block text-xs text-muted-foreground">Upload a .pfx file with its private key</span>
                </button>
              </div>

              {certificateMode === 'existing' ? (
                <div className="space-y-2 sm:w-[calc(50%-0.5rem)]">
                  <Label htmlFor="certificate">Certificate *</Label>
                  <Select
                    id="certificate"
                    value={selectedThumbprint}
                    onChange={(e) => setSelectedThumbprint(e.target.value)}
                    disabled={busy}
                  >
                    <option value="">Select a certificate...</option>
                    {report.certificates.map((certificate) => (
                      <option key={certificate.thumbprint} value={certificate.thumbprint} disabled={certificate.isExpired}>
                        {(certificate.friendlyName || certificate.subject) +
                          (certificate.friendlyName === DEFAULT_CERTIFICATE ? ' (default)' : '') +
                          (certificate.notAfter ? ` · expires ${new Date(certificate.notAfter).toLocaleDateString()}` : '') +
                          (certificate.isExpired ? ' · EXPIRED' : '')}
                      </option>
                    ))}
                  </Select>
                  {report.certificates.length === 0 && (
                    <p className="text-xs text-muted-foreground">No certificates with a private key are installed. Install your own certificate.</p>
                  )}
                </div>
              ) : (
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="certificateFile">Certificate file (.pfx) *</Label>
                    <Input
                      id="certificateFile"
                      type="file"
                      accept=".pfx,.p12"
                      onChange={(e) => setCertificateFile(e.target.files?.[0] ?? null)}
                      disabled={busy}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="certificatePassword">Certificate password</Label>
                    <Input
                      id="certificatePassword"
                      type="password"
                      autoComplete="new-password"
                      value={certificatePassword}
                      onChange={(e) => setCertificatePassword(e.target.value)}
                      disabled={busy}
                    />
                  </div>
                  <div className="sm:col-span-2 flex justify-end">
                    <Button type="button" onClick={handleUploadCertificate} disabled={busy || !certificateFile}>
                      {busyAction === 'certificate' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                      Install Certificate
                    </Button>
                  </div>
                </div>
              )}

              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="bindingHost">Site URL ({report.serverType === 'Frontend' ? 'frontend' : 'backend'} host name) *</Label>
                  <Input
                    id="bindingHost"
                    placeholder={hostPlaceholder}
                    value={bindingHost}
                    onChange={(e) => setBindingHost(e.target.value)}
                    disabled={busy}
                  />
                </div>
                <label className="flex items-start gap-2 text-sm cursor-pointer sm:pt-7">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={requireSni}
                    onChange={(e) => setRequireSni(e.target.checked)}
                    disabled={busy}
                  />
                  <span>
                    Require Server Name Indication (SNI)
                    <span className="block text-xs text-muted-foreground">Needed when other sites on this server use a different certificate</span>
                  </span>
                </label>
              </div>

              <div className="flex flex-wrap items-center justify-between gap-2">
                {!siteExists && <p className="text-xs text-muted-foreground">Create the IIS site before adding bindings.</p>}
                <Button
                  type="button"
                  className="ml-auto"
                  onClick={handleApplyBindings}
                  disabled={busy || !siteExists || certificateMode !== 'existing' || !selectedThumbprint || !bindingHost.trim()}
                >
                  {busyAction === 'bindings' && <Loader2 className="h-4 w-4 animate-spin" />}
                  Apply Bindings (80 / 443)
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Database access */}
        {report && report.serverType === 'Api' && (
          <Card id="database-setup">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Database className="h-5 w-5 text-primary" />
                Database Access
              </CardTitle>
              <CardDescription>
                Optional. Gives the site&apos;s application pool identity <strong>db_owner</strong> on the database this API uses.
                Without a linked instance the server is still deployable and database migrations are skipped.
                SQL Server must run on this server; DeployVare connects with Windows authentication as the SSH user.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex flex-wrap items-end gap-3">
                <div className="space-y-2 flex-1 min-w-[240px]">
                  <Label htmlFor="databaseInstance">Database Instance *</Label>
                  <Select
                    id="databaseInstance"
                    value={selectedInstanceId}
                    onChange={(e) => setSelectedInstanceId(Number(e.target.value))}
                    disabled={busy}
                  >
                    <option value={0}>Select a database instance...</option>
                    {instances.map((instance) => (
                      <option key={instance.id} value={instance.id}>
                        {instance.instanceName}
                      </option>
                    ))}
                  </Select>
                </div>
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleLinkDatabase}
                  disabled={busy || selectedInstanceId === (report.database?.databaseInstanceId ?? 0)}
                >
                  {busyAction === 'db-link' && <Loader2 className="h-4 w-4 animate-spin" />}
                  Save
                </Button>
              </div>
              {instances.length === 0 && (
                <p className="text-xs text-muted-foreground">
                  No database instances for {server?.applicationName}. Add one on the{' '}
                  <Link href="/instances" className="underline">Instances</Link> page.
                </p>
              )}

              {report.database?.databaseInstanceId != null && (
                <div className="rounded-lg border border-border bg-muted/40 p-4 text-sm space-y-3">
                  <div className="grid gap-x-4 gap-y-1 sm:grid-cols-2">
                    <div>
                      <span className="text-muted-foreground">SQL Server:</span>{' '}
                      <span className="font-mono text-xs">{report.database.dataSource ?? '—'}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Database:</span>{' '}
                      <span className="font-mono text-xs">{report.database.databaseName ?? '—'}</span>
                    </div>
                    <div className="sm:col-span-2">
                      <span className="text-muted-foreground">Login granted:</span>{' '}
                      <span className="font-mono text-xs">{report.database.loginName}</span>
                    </div>
                    {report.database.connectedAs && (
                      <div className="sm:col-span-2">
                        <span className="text-muted-foreground">Checked as:</span>{' '}
                        <span className="font-mono text-xs">{report.database.connectedAs}</span>
                      </div>
                    )}
                  </div>
                  {report.database.configurationError && (
                    <p className="text-red-700">{report.database.configurationError}</p>
                  )}
                  {report.database.connectionError && (
                    <p className="text-red-700 break-words">{report.database.connectionError}</p>
                  )}
                  {report.database.connected != null && (
                    <div className="grid gap-1 sm:grid-cols-2">
                      {([
                        ['SQL Server on this server', report.database.sqlOnSameServer],
                        ['SSH user is sysadmin', report.database.isSysadmin],
                        ['Database exists', report.database.databaseExists],
                        ['Server login', report.database.loginExists],
                        ['Database user', report.database.userExists],
                        ['db_owner role', report.database.isDbOwner],
                      ] as [string, boolean | null | undefined][]).map(([label, ok]) => (
                        <div key={label} className="flex items-center gap-2">
                          {ok ? (
                            <CheckCircle2 className="h-4 w-4 text-emerald-600" />
                          ) : (
                            <XCircle className="h-4 w-4 text-red-500" />
                          )}
                          <span>{label}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {/* Show admin access requirement if deployvare is not sysadmin */}
              {report.database?.connected && !report.database?.isSysadmin && (
                <div className="space-y-3 rounded-lg border border-amber-200 bg-amber-50 p-4">
                  <div className="flex items-start gap-2">
                    <AlertTriangle className="h-5 w-5 text-amber-700 shrink-0 mt-0.5" />
                    <div className="space-y-2">
                      <p className="font-medium text-amber-900">
                        SQL Server Admin Access Required
                      </p>
                      <p className="text-sm text-amber-800">
                        The current Windows user <span className="font-mono">{report.database.connectedAs}</span> needs to be added to SQL Server sysadmin role to grant database access.
                      </p>
                      <div className="space-y-2 mt-3">
                        <p className="text-xs font-medium text-amber-900">Ask a DBA or Domain Admin to run in SQL Server Management Studio:</p>
                        <div className="rounded bg-white p-3 font-mono text-xs text-amber-900 break-all space-y-1 whitespace-pre-wrap">
                          <div>USE master;</div>
                          <div>CREATE LOGIN [{report.database.connectedAs}] FROM WINDOWS;</div>
                          <div>ALTER SERVER ROLE sysadmin ADD MEMBER [{report.database.connectedAs}];</div>
                        </div>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={async () => {
                            try {
                              const script = `USE master;\nCREATE LOGIN [${report.database?.connectedAs}] FROM WINDOWS;\nALTER SERVER ROLE sysadmin ADD MEMBER [${report.database?.connectedAs}];`;
                              await navigator.clipboard.writeText(script);
                              toast.success('SQL commands copied');
                            } catch {
                              toast.error('Could not copy commands');
                            }
                          }}
                          className="mt-2"
                        >
                          <Copy className="h-3 w-3" />
                          Copy Commands
                        </Button>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              <div className="flex justify-end">
                <Button
                  type="button"
                  onClick={handleGrantDatabase}
                  disabled={busy}
                >
                  {busyAction === 'db-grant' ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <ShieldCheck className="h-4 w-4" />
                  )}
                  Grant db_owner Access
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Frontend runtime settings (config.js) */}
        {server?.serverType === 'Frontend' && runtimeConfig && (
          <Card id="runtime-config-setup">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <FileCode className="h-5 w-5 text-primary" />
                Frontend Runtime Settings (config.js)
              </CardTitle>
              <CardDescription>
                Client-specific values written to <span className="font-mono">config.js</span> on every deploy, so the same
                Dev build works on this server. Leave a value empty to use the value built into the artifact.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <datalist id="runtime-setting-keys">
                {runtimeConfig.suggestedKeys.map((key) => (
                  <option key={key} value={key} />
                ))}
              </datalist>

              <div className="space-y-2">
                {runtimeRows.map((row, index) => (
                  <div key={index} className="grid gap-2 sm:grid-cols-[minmax(0,2fr)_minmax(0,3fr)_auto] items-center">
                    <Input
                      list="runtime-setting-keys"
                      placeholder="VITE_API_URL"
                      autoComplete="off"
                      aria-label="Setting name"
                      className="font-mono text-xs"
                      value={row.key}
                      onChange={(e) => updateRuntimeRow(index, { key: e.target.value.toUpperCase() })}
                      disabled={runtimeSaving || busy}
                    />
                    <Input
                      placeholder="https://..."
                      autoComplete="off"
                      aria-label={`Value for ${row.key || 'setting'}`}
                      value={row.value}
                      onChange={(e) => updateRuntimeRow(index, { value: e.target.value })}
                      disabled={runtimeSaving || busy}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon"
                      title="Remove setting"
                      onClick={() => removeRuntimeRow(index)}
                      disabled={runtimeSaving || busy}
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                ))}
              </div>

              <div className="flex flex-wrap items-center justify-between gap-2">
                <Button type="button" variant="outline" size="sm" onClick={addRuntimeRow} disabled={runtimeSaving || busy}>
                  <PlusCircle className="h-3 w-3" />
                  Add Setting
                </Button>
                <div className="flex gap-2">
                  <Button type="button" variant="outline" onClick={saveRuntimeConfig} disabled={runtimeSaving || busy}>
                    {runtimeSaving && <Loader2 className="h-4 w-4 animate-spin" />}
                    Save
                  </Button>
                  <Button type="button" onClick={handleSaveAndApplyRuntimeConfig} disabled={runtimeSaving || busy}>
                    {busyAction === 'runtime-apply' ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Upload className="h-4 w-4" />
                    )}
                    Save &amp; Apply to Server
                  </Button>
                </div>
              </div>

              {runtimeConfig.configJsPreview ? (
                <details className="rounded-lg border border-border bg-muted/40 p-3 text-xs">
                  <summary className="cursor-pointer font-medium">Preview config.js (saved settings)</summary>
                  <pre className="mt-2 overflow-x-auto whitespace-pre font-mono">{runtimeConfig.configJsPreview}</pre>
                </details>
              ) : (
                <p className="text-xs text-amber-700">
                  No settings saved yet. Until you save, deployments to this server use the values built into the artifact.
                </p>
              )}
            </CardContent>
          </Card>
        )}

        {/* SQL Server login */}
        <Card id="sql-login-setup">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <KeyRound className="h-5 w-5 text-primary" />
                  SQL Server Login
                </CardTitle>
                <CardDescription>
                  Check whether SQL Server is installed on this server, then create a SQL login with db_owner on a database.
                  Passwords are never stored by DeployVare.
                </CardDescription>
              </div>
              <Button type="button" variant="outline" onClick={handleCheckSqlServer} disabled={sqlChecking || busy}>
                {sqlChecking ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
                {sqlInfo ? 'Check Again' : 'Check SQL Server'}
              </Button>
            </div>
          </CardHeader>
          {(sqlError || sqlInfo) && (
            <CardContent className="space-y-4">
              {sqlError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{sqlError}</div>
              )}

              {sqlInfo && !sqlInfo.installed && (
                <div className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-3 text-sm">
                  <MinusCircle className="h-4 w-4 text-gray-500" />
                  SQL Server is not installed on this server. Nothing to set up.
                </div>
              )}

              {sqlInfo?.installed && (() => {
                const instance = sqlInfo.instances.find((i) => i.name === sqlForm.instanceName) ?? sqlInfo.instances[0];
                const canCreate =
                  !!instance && instance.running && instance.connected && instance.canManageLogins && instance.databases.length > 0;

                return (
                  <>
                    {sqlInfo.instances.length > 1 && (
                      <div className="space-y-2 sm:w-[calc(50%-0.5rem)]">
                        <Label htmlFor="sqlInstance">SQL Server Instance</Label>
                        <Select
                          id="sqlInstance"
                          value={sqlForm.instanceName}
                          onChange={(e) => {
                            const next = sqlInfo.instances.find((i) => i.name === e.target.value);
                            setSqlForm({ ...sqlForm, instanceName: e.target.value, databaseName: next?.databases[0] ?? '' });
                          }}
                          disabled={busy}
                        >
                          {sqlInfo.instances.map((i) => (
                            <option key={i.name} value={i.name}>{i.name}</option>
                          ))}
                        </Select>
                      </div>
                    )}

                    {instance && (
                      <div className="rounded-lg border border-border bg-muted/40 p-4 text-sm space-y-2">
                        <div className="grid gap-x-4 gap-y-1 sm:grid-cols-2">
                          <div>
                            <span className="text-muted-foreground">Instance:</span>{' '}
                            <span className="font-mono text-xs">{instance.name}</span>
                          </div>
                          <div>
                            <span className="text-muted-foreground">Version:</span>{' '}
                            <span className="text-xs">{[instance.edition, instance.version].filter(Boolean).join(' · ') || '—'}</span>
                          </div>
                          {instance.connectedAs && (
                            <div className="sm:col-span-2">
                              <span className="text-muted-foreground">Connected as:</span>{' '}
                              <span className="font-mono text-xs">{instance.connectedAs}</span>
                            </div>
                          )}
                        </div>
                        <div className="grid gap-1 sm:grid-cols-2">
                          {([
                            ['Service running', instance.running],
                            ['Connected', instance.connected],
                            ['Admin login can create logins', instance.canManageLogins],
                            ['SQL logins allowed (mixed authentication)', instance.mixedAuthentication],
                          ] as [string, boolean][]).map(([label, ok]) => (
                            <div key={label} className="flex items-center gap-2">
                              {ok ? <CheckCircle2 className="h-4 w-4 text-emerald-600" /> : <XCircle className="h-4 w-4 text-red-500" />}
                              <span>{label}</span>
                            </div>
                          ))}
                        </div>
                        {instance.connectionError && <p className="text-red-700 break-words">{instance.connectionError}</p>}
                        {!instance.mixedAuthentication && (
                          <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                            <span>This instance only allows Windows authentication, so SQL logins cannot sign in.</span>
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              onClick={() => handleEnableSqlAuthentication(instance.name)}
                              disabled={busy}
                            >
                              {busyAction === 'sql-auth' && <Loader2 className="h-3 w-3 animate-spin" />}
                              Enable SQL authentication
                            </Button>
                          </div>
                        )}
                      </div>
                    )}

                    {instance && instance.running && (
                      <div className="space-y-3 rounded-lg border border-border p-4">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="text-sm font-medium">Connect to SQL Server as *</p>
                          <label className="flex items-center gap-2 text-sm cursor-pointer">
                            <input
                              type="checkbox"
                              checked={sqlAdmin.useWindowsAuthentication}
                              onChange={(e) => setSqlAdmin({ ...sqlAdmin, useWindowsAuthentication: e.target.checked })}
                              disabled={busy || sqlChecking}
                            />
                            Use Windows authentication (SSH user) instead
                          </label>
                        </div>
                        {!sqlAdmin.useWindowsAuthentication && (
                          <div className="grid gap-4 sm:grid-cols-2">
                            <div className="space-y-2">
                              <Label htmlFor="sqlAdminLogin">SQL Admin Login *</Label>
                              <Input
                                id="sqlAdminLogin"
                                placeholder="e.g., sa"
                                autoComplete="off"
                                value={sqlAdmin.adminLogin}
                                onChange={(e) => setSqlAdmin({ ...sqlAdmin, adminLogin: e.target.value })}
                                maxLength={128}
                                disabled={busy || sqlChecking}
                              />
                            </div>
                            <div className="space-y-2">
                              <Label htmlFor="sqlAdminPassword">SQL Admin Password *</Label>
                              <Input
                                id="sqlAdminPassword"
                                type="password"
                                autoComplete="new-password"
                                value={sqlAdmin.adminPassword}
                                onChange={(e) => setSqlAdmin({ ...sqlAdmin, adminPassword: e.target.value })}
                                maxLength={128}
                                disabled={busy || sqlChecking}
                              />
                            </div>
                          </div>
                        )}
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="text-xs text-muted-foreground">
                            Needs permission to create logins (sysadmin or securityadmin). Not stored by DeployVare.
                          </p>
                          <Button
                            type="button"
                            variant="outline"
                            onClick={handleCheckSqlServer}
                            disabled={busy || sqlChecking || !sqlConnection()}
                          >
                            {sqlChecking ? <Loader2 className="h-4 w-4 animate-spin" /> : <Database className="h-4 w-4" />}
                            {instance.connected ? 'Reconnect' : 'Connect'}
                          </Button>
                        </div>
                      </div>
                    )}

                    {instance && instance.connected && instance.databases.length === 0 && (
                      <p className="text-sm text-muted-foreground">No user databases found on this instance.</p>
                    )}

                    {canCreate && (
                      <form onSubmit={handleCreateSqlLogin} className="space-y-4">
                        <div className="grid gap-4 sm:grid-cols-2">
                          <div className="space-y-2">
                            <Label htmlFor="sqlDatabase">Target Database *</Label>
                            <Select
                              id="sqlDatabase"
                              value={sqlForm.databaseName}
                              onChange={(e) => {
                                setSqlForm({ ...sqlForm, databaseName: e.target.value });
                                setSqlUsers(null);
                              }}
                              required
                              disabled={busy}
                            >
                              {instance.databases.map((database) => (
                                <option key={database} value={database}>{database}</option>
                              ))}
                            </Select>
                          </div>
                          <div className="space-y-2">
                            <Label htmlFor="sqlLogin">Login Name *</Label>
                            <Input
                              id="sqlLogin"
                              placeholder="e.g., auditvare_local"
                              autoComplete="off"
                              value={sqlForm.loginName}
                              onChange={(e) => setSqlForm({ ...sqlForm, loginName: e.target.value })}
                              required
                              maxLength={128}
                              disabled={busy}
                            />
                          </div>
                        </div>
                        <div className="grid gap-4 lg:grid-cols-2">
                        <div className="space-y-2">
                          <Label htmlFor="sqlPassword">Password *</Label>
                          <div className="flex gap-2">
                            <Input
                              id="sqlPassword"
                              type={showSqlPassword ? 'text' : 'password'}
                              autoComplete="new-password"
                              value={sqlForm.password}
                              onChange={(e) => setSqlForm({ ...sqlForm, password: e.target.value })}
                              required
                              minLength={8}
                              maxLength={128}
                              disabled={busy}
                              className="font-mono"
                            />
                            <Button type="button" variant="outline" size="icon" title={showSqlPassword ? 'Hide' : 'Show'}
                              onClick={() => setShowSqlPassword(!showSqlPassword)}>
                              {showSqlPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                            </Button>
                            <Button type="button" variant="outline" size="icon" title="Copy" onClick={copySqlPassword}
                              disabled={!sqlForm.password}>
                              <Copy className="h-4 w-4" />
                            </Button>
                            <Button type="button" variant="outline" disabled={busy}
                              onClick={() => { setSqlForm({ ...sqlForm, password: generatePassword() }); setShowSqlPassword(true); }}>
                              Generate
                            </Button>
                          </div>
                          <p className="text-xs text-muted-foreground">
                            Must meet the server&apos;s password policy. DeployVare does not store it — copy it into the application&apos;s connection string.
                          </p>
                        </div>
                        <label className="flex items-start gap-2 text-sm cursor-pointer lg:pt-7">
                          <input
                            type="checkbox"
                            className="mt-1"
                            checked={sqlForm.resetPasswordIfExists}
                            onChange={(e) => setSqlForm({ ...sqlForm, resetPasswordIfExists: e.target.checked })}
                            disabled={busy}
                          />
                          <span>
                            Reset the password if this login already exists
                            <span className="block text-xs text-muted-foreground">
                              Also unlocks and enables the login. Applications using the old password stop connecting until updated.
                            </span>
                          </span>
                        </label>
                        </div>
                        <div className="flex justify-end">
                          <Button type="submit" disabled={busy || !sqlForm.databaseName || !sqlForm.loginName || sqlForm.password.length < 8}>
                            {busyAction === 'sql-login' ? <Loader2 className="h-4 w-4 animate-spin" /> : <KeyRound className="h-4 w-4" />}
                            Create Login &amp; Grant db_owner
                          </Button>
                        </div>
                      </form>
                    )}

                    {/* Users of the selected database */}
                    {instance && instance.connected && sqlForm.databaseName && (
                      <div className="space-y-2">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <p className="text-sm font-medium flex items-center gap-2">
                            <Users className="h-4 w-4" />
                            Users in {sqlForm.databaseName}
                          </p>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            onClick={loadSqlUsers}
                            disabled={busy || sqlUsersLoading || !sqlConnection()}
                          >
                            {sqlUsersLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : <RefreshCw className="h-3 w-3" />}
                            {sqlUsers ? 'Refresh' : 'Show Users'}
                          </Button>
                        </div>
                        {sqlUsers && sqlUsers.length === 0 && (
                          <p className="text-xs text-muted-foreground">No users found in this database.</p>
                        )}
                        {sqlUsers && sqlUsers.length > 0 && (
                          <div className="overflow-x-auto rounded-lg border border-border">
                            <table className="w-full text-sm">
                              <thead className="bg-muted/50 text-left text-xs font-medium uppercase tracking-wide text-muted-foreground">
                                <tr className="border-b border-border">
                                  <th className="px-3 py-2 font-medium">User</th>
                                  <th className="px-3 py-2 font-medium">Type</th>
                                  <th className="px-3 py-2 font-medium">Login</th>
                                  <th className="px-3 py-2 font-medium">Roles</th>
                                </tr>
                              </thead>
                              <tbody>
                                {sqlUsers.map((user) => (
                                  <tr
                                    key={user.userName}
                                    className={`border-b border-border last:border-0 ${user.userName.toLowerCase() === createdLogin?.loginName.toLowerCase() ? 'bg-emerald-50' : 'hover:bg-muted/40'}`}
                                  >
                                    <td className="px-3 py-2 font-mono text-xs">{user.userName}</td>
                                    <td className="px-3 py-2 text-xs">{user.userType.replace(/_/g, ' ').toLowerCase()}</td>
                                    <td className="px-3 py-2 text-xs">
                                      {user.loginName ? (
                                        <>
                                          <span className="font-mono">{user.loginName}</span>
                                          {user.loginDisabled && <span className="ml-1 text-red-600">(disabled)</span>}
                                          {user.loginLocked && <span className="ml-1 text-red-600">(locked)</span>}
                                        </>
                                      ) : (
                                        <span className="text-amber-700">No login (orphaned)</span>
                                      )}
                                    </td>
                                    <td className="px-3 py-2 text-xs">{user.roles.length > 0 ? user.roles.join(', ') : '—'}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>
                    )}

                    {/* Add the created login as a DeployVare database instance */}
                    {createdLogin && (
                      <form onSubmit={handleAddInstance} className="space-y-4 rounded-lg border border-primary/30 bg-primary/5 p-4">
                        <div>
                          <p className="font-medium flex items-center gap-2">
                            <PlusCircle className="h-4 w-4 text-primary" />
                            Add as DeployVare Database Instance
                          </p>
                          <p className="text-xs text-muted-foreground">
                            Uses login <span className="font-mono">{createdLogin.loginName}</span> on{' '}
                            <span className="font-mono">{createdLogin.databaseName}</span>. DeployVare tests the connection from the
                            machine running the DeployVare API before saving, so the address must be reachable from there.
                          </p>
                        </div>

                        {/* Connection String */}
                        <div className="space-y-2 rounded-lg border border-blue-200 bg-blue-50 p-3">
                          <div className="flex items-center justify-between">
                            <p className="text-xs font-medium text-blue-900">Connection String</p>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={async () => {
                                try {
                                  await navigator.clipboard.writeText(buildConnectionString(false));
                                  toast.success('Connection string copied');
                                } catch {
                                  toast.error('Could not copy connection string');
                                }
                              }}
                            >
                              <Copy className="h-3 w-3" />
                            </Button>
                          </div>
                          <p className="break-all rounded bg-white p-2 font-mono text-[11px] text-blue-900">
                            {buildConnectionString(true)}
                          </p>
                        </div>

                        <div className="grid gap-4 sm:grid-cols-2">
                          <div className="space-y-2">
                            <Label htmlFor="newInstanceApplication">Application *</Label>
                            <Select
                              id="newInstanceApplication"
                              value={instanceForm.applicationId}
                              onChange={(e) => setInstanceForm({ ...instanceForm, applicationId: Number(e.target.value) })}
                              required
                              disabled={addingInstance}
                            >
                              <option value={0}>Select an application</option>
                              {applications.map((app) => (
                                <option key={app.id} value={app.id}>{app.name}</option>
                              ))}
                            </Select>
                          </div>
                          <div className="space-y-2">
                            <Label htmlFor="newInstanceName">Instance Name *</Label>
                            <Input
                              id="newInstanceName"
                              placeholder="e.g., Dev"
                              autoComplete="off"
                              value={instanceForm.instanceName}
                              onChange={(e) => setInstanceForm({ ...instanceForm, instanceName: e.target.value })}
                              required
                              disabled={addingInstance}
                            />
                          </div>
                          <div className="space-y-2">
                            <Label htmlFor="newInstanceAddress">SQL Server Address *</Label>
                            <Input
                              id="newInstanceAddress"
                              placeholder="e.g., 192.227.122.100 or 192.227.122.100,1433"
                              autoComplete="off"
                              value={instanceForm.serverAddress}
                              onChange={(e) => setInstanceForm({ ...instanceForm, serverAddress: e.target.value })}
                              required
                              disabled={addingInstance}
                            />
                            <p className="text-xs text-muted-foreground">Add ,port for a non-default port</p>
                          </div>
                          <div className="space-y-2">
                            <Label htmlFor="newInstancePatch">Current Patch Number</Label>
                            <Input
                              id="newInstancePatch"
                              type="number"
                              min="0"
                              value={instanceForm.currentPatchNumber}
                              onChange={(e) => setInstanceForm({ ...instanceForm, currentPatchNumber: parseInt(e.target.value) || 0 })}
                              disabled={addingInstance}
                            />
                          </div>
                        </div>
                        <div className="rounded-md border border-border bg-card p-2 font-mono text-xs break-all">{buildConnectionString(true)}</div>
                        <div className="flex justify-end gap-2">
                          <Button type="button" variant="outline" onClick={() => setCreatedLogin(null)} disabled={addingInstance}>
                            Dismiss
                          </Button>
                          <Button
                            type="submit"
                            disabled={
                              addingInstance ||
                              !instanceForm.applicationId ||
                              !instanceForm.instanceName.trim() ||
                              !instanceForm.serverAddress.trim()
                            }
                          >
                            {addingInstance ? <Loader2 className="h-4 w-4 animate-spin" /> : <PlusCircle className="h-4 w-4" />}
                            Add Instance
                          </Button>
                        </div>
                      </form>
                    )}
                  </>
                );
              })()}
            </CardContent>
          )}
        </Card>

        {/* Installers */}
        {installers.length > 0 && (
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Package className="h-5 w-5 text-primary" />
                Installers
              </CardTitle>
              <CardDescription>
                Files in <span className="font-mono text-xs">{installers[0].directory}</span>, copied to the server when installing
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-2 text-sm">
                {installers.map((installer) => (
                  <div key={installer.component} className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-medium">{installer.component}</span>
                    {installer.available ? (
                      <span className="font-mono text-xs">
                        {installer.fileName}
                        {installer.sizeBytes != null && ` · ${(installer.sizeBytes / 1024 / 1024).toFixed(0)} MB`}
                      </span>
                    ) : (
                      <span className="text-xs text-amber-700">Missing — add a file matching {installer.pattern}</span>
                    )}
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        )}

        {/* In progress / last action */}
        {busyAction && (
          <div className="flex items-center gap-2 rounded-lg border border-blue-200 bg-blue-50/60 p-4 text-sm">
            <Loader2 className="h-4 w-4 animate-spin" />
            Working on the server — installers can take several minutes. Keep this page open.
          </div>
        )}

        {lastResult && (
          <Card className={lastResult.result.success ? 'border-emerald-300' : 'border-red-300'}>
            <CardHeader>
              <div className="flex items-center gap-3">
                {lastResult.result.success ? (
                  <CheckCircle2 className="h-6 w-6 text-emerald-600" />
                ) : (
                  <XCircle className="h-6 w-6 text-red-600" />
                )}
                <div>
                  <CardTitle>{lastResult.title} {lastResult.result.success ? 'completed' : 'failed'}</CardTitle>
                  {!lastResult.result.success && <CardDescription>{lastResult.result.message}</CardDescription>}
                </div>
              </div>
            </CardHeader>
            {(lastResult.result.steps.length > 0 || lastResult.result.rebootRequired) && (
              <CardContent className="space-y-3">
                {lastResult.result.steps.length > 0 && (
                  <div className="rounded-lg border border-border bg-muted/40 p-3 space-y-1">
                    {lastResult.result.steps.map((step, index) => (
                      <div key={index} className="text-xs font-mono break-words">{step}</div>
                    ))}
                  </div>
                )}
                {lastResult.result.rebootRequired && (
                  <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                    The installer requires a server restart to finish.
                  </div>
                )}
              </CardContent>
            )}
          </Card>
        )}
      </div>
    </ProtectedLayout>
  );
}

export default function ServerSetupPage() {
  return (
    <Suspense>
      <ServerSetupContent />
    </Suspense>
  );
}
