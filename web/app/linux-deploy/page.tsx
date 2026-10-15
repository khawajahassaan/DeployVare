'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import {
  AlertTriangle,
  BookOpen,
  Check,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Cloud,
  Database,
  Download,
  ExternalLink,
  GitCommit,
  KeyRound,
  Layers,
  Loader2,
  Minus,
  Monitor,
  Plug,
  RefreshCw,
  Rocket,
  Server,
  Terminal,
  X,
  XCircle,
  Zap,
} from 'lucide-react';
import { toast } from 'sonner';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { PageHeader } from '@/components/ui/page-header';
import api, {
  Application,
  ArtifactEnvironment,
  BackgroundOperation,
  DeploymentProgress,
  DeploymentStageState,
  LinuxBuildOption,
  LinuxBuilds,
  LinuxConnectionTest,
  LinuxDeploymentResult,
  LinuxDeploymentTarget,
  LinuxPackageRequest,
  LinuxSshTarget,
} from '@/lib/api';
import { cn, formatDate } from '@/lib/utils';

type Architecture = 'Frontend' | 'Backend' | 'Tiered';
type Mode = 'Automated' | 'Manual';

interface SshForm {
  host: string;
  port: string;
  username: string;
  auth: 'password' | 'key';
  password: string;
  privateKey: string;
  sudoPassword: string;
}

const emptySsh = (): SshForm => ({ host: '', port: '22', username: '', auth: 'password', password: '', privateKey: '', sudoPassword: '' });

const toTarget = (form: SshForm): LinuxSshTarget => ({
  host: form.host.trim(),
  port: Number(form.port) || 22,
  username: form.username.trim(),
  password: form.auth === 'password' ? form.password : undefined,
  privateKey: form.auth === 'key' ? form.privateKey : undefined,
  sudoPassword: form.sudoPassword || undefined,
});

const sshComplete = (form: SshForm) =>
  !!form.host.trim() && !!form.username.trim() && (form.auth === 'password' ? !!form.password : !!form.privateKey.trim());

const newProgressId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;

// Follows a background operation until it finishes; a failed poll is retried (the work continues on the server)
const waitForOperation = async (operationId: string, intervalMs = 2000): Promise<BackgroundOperation> => {
  let failures = 0;
  for (;;) {
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
    try {
      const operation = await api.getOperation(operationId);
      failures = 0;
      if (operation.status !== 'Running') return operation;
    } catch (err) {
      if ((err as { response?: { status?: number } }).response?.status === 404)
        throw new Error('The server no longer knows this operation (it may have restarted). Try again.');
      if (++failures >= 30) throw err;
    }
  }
};

const errorMessage = (err: unknown, fallback: string) => {
  const data = (err as { response?: { data?: { error?: string; message?: string; title?: string } } }).response?.data;
  return data?.error || data?.message || data?.title || (err as Error).message || fallback;
};

const formatSize = (bytes?: number | null) =>
  !bytes ? '' : bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

const segmentClass = (active: boolean) =>
  `inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
    active ? 'bg-card text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'
  }`;

// ---- Small building blocks -------------------------------------------------------------------------------------

function OptionTile({
  icon: Icon,
  title,
  description,
  selected,
  disabled,
  onClick,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  selected: boolean;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={selected}
      className={cn(
        'flex h-full flex-col items-start gap-2 rounded-lg border p-4 text-left transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        selected ? 'border-primary bg-accent/60 ring-1 ring-primary' : 'border-border bg-card hover:border-foreground/40'
      )}
    >
      <div className="flex w-full items-center justify-between">
        <Icon className={cn('h-5 w-5', selected ? 'text-primary' : 'text-muted-foreground')} />
        {selected && <CheckCircle2 className="h-4 w-4 text-primary" />}
      </div>
      <div className="text-sm font-semibold text-foreground">{title}</div>
      <div className="text-xs text-muted-foreground">{description}</div>
    </button>
  );
}

function Field({ label, hint, children, htmlFor }: { label: string; hint?: ReactNode; children: ReactNode; htmlFor?: string }) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={htmlFor}>{label}</Label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

function CodeBlock({ children }: { children: string }) {
  return (
    <pre className="overflow-x-auto rounded-md bg-[#1E1E1E] p-3 font-mono text-[12px] leading-relaxed text-[#D4D4D4]">
      <code>{children}</code>
    </pre>
  );
}

function StageIndicator({ state, number }: { state: DeploymentStageState; number: number }) {
  const circle = 'flex h-5 w-5 shrink-0 items-center justify-center rounded-full';
  switch (state) {
    case 'Running':
      return <Loader2 className="h-5 w-5 shrink-0 animate-spin text-primary" />;
    case 'Succeeded':
      return (
        <span className={cn(circle, 'bg-emerald-500 text-white')}>
          <Check className="h-3 w-3" strokeWidth={3} />
        </span>
      );
    case 'Warning':
      return <span className={cn(circle, 'bg-amber-500 text-[11px] font-bold text-white')}>!</span>;
    case 'Failed':
      return (
        <span className={cn(circle, 'bg-red-500 text-white')}>
          <X className="h-3 w-3" strokeWidth={3} />
        </span>
      );
    case 'Skipped':
      return (
        <span className={cn(circle, 'bg-muted text-muted-foreground')}>
          <Minus className="h-3 w-3" strokeWidth={3} />
        </span>
      );
    default:
      return <span className={cn(circle, 'bg-muted text-[11px] font-medium text-foreground')}>{number}</span>;
  }
}

function BuildRow({
  label,
  option,
  artifact,
  onArtifactChange,
  disabled,
}: {
  label: string;
  option: LinuxBuildOption;
  artifact: string;
  onArtifactChange: (name: string) => void;
  disabled: boolean;
}) {
  if (option.error) {
    return (
      <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
        <div className="font-medium">{label}</div>
        <div className="mt-1">{option.error}</div>
      </div>
    );
  }
  const build = option.build;
  if (!build) return null;
  const downloadable = build.artifacts.filter((a) => a.isDownloadable);
  const isLinux = artifact.toLowerCase().includes('linux');

  return (
    <div className="space-y-3 rounded-lg border border-border bg-muted/40 p-4 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold text-foreground">{label}</span>
        <Badge variant="secondary">#{build.buildNumber}</Badge>
        <span className="flex items-center gap-1 text-xs text-muted-foreground">
          <GitCommit className="h-3.5 w-3.5" />
          {build.sourceBranch.replace('refs/heads/', '')} · {build.sourceVersion.slice(0, 7)}
        </span>
        {build.finishTime && <span className="text-xs text-muted-foreground">{formatDate(build.finishTime)}</span>}
        {build.webUrl && (
          <a href={build.webUrl} target="_blank" rel="noreferrer" className="flex items-center gap-1 text-xs text-brand-blue hover:underline">
            Azure DevOps <ExternalLink className="h-3 w-3" />
          </a>
        )}
      </div>
      <Field label="Artifact" hint={option.note ?? undefined}>
        <div className="flex items-center gap-2">
          <Select value={artifact} onChange={(e) => onArtifactChange(e.target.value)} disabled={disabled}>
            {downloadable.map((a) => (
              <option key={a.name} value={a.name}>
                {a.name}
                {a.name === option.suggestedArtifactName ? ' (suggested)' : ''}
                {a.sizeBytes ? ` — ${formatSize(a.sizeBytes)}` : ''}
              </option>
            ))}
          </Select>
          <Badge variant={isLinux ? 'success' : 'warning'} className="shrink-0">
            {isLinux ? 'Linux build' : 'Not Linux-specific'}
          </Badge>
        </div>
      </Field>
    </div>
  );
}

function SshServerForm({
  title,
  value,
  onChange,
  test,
  testing,
  onTest,
  disabled,
}: {
  title: string;
  value: SshForm;
  onChange: (value: SshForm) => void;
  test: LinuxConnectionTest | null;
  testing: boolean;
  onTest: () => void;
  disabled: boolean;
}) {
  const set = (patch: Partial<SshForm>) => onChange({ ...value, ...patch });
  const id = title.toLowerCase().replace(/\W+/g, '-');

  return (
    <div className="space-y-4 rounded-lg border border-border p-4">
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <Server className="h-4 w-4 text-muted-foreground" />
        {title}
      </div>
      <div className="grid gap-4 sm:grid-cols-[1fr_96px]">
        <Field label="Host / IP *" htmlFor={`${id}-host`}>
          <Input id={`${id}-host`} value={value.host} onChange={(e) => set({ host: e.target.value })} placeholder="10.0.0.20 or web01.client.com" disabled={disabled} />
        </Field>
        <Field label="SSH port" htmlFor={`${id}-port`}>
          <Input id={`${id}-port`} inputMode="numeric" value={value.port} onChange={(e) => set({ port: e.target.value.replace(/\D/g, '') })} disabled={disabled} />
        </Field>
      </div>
      <Field label="Username *" htmlFor={`${id}-user`} hint="A user that can run sudo (e.g. ubuntu), or root.">
        <Input id={`${id}-user`} value={value.username} onChange={(e) => set({ username: e.target.value })} placeholder="ubuntu" autoComplete="off" disabled={disabled} />
      </Field>
      <div className="space-y-1.5">
        <Label>Sign in with</Label>
        <div className="inline-flex rounded-lg bg-muted p-1">
          <button type="button" className={segmentClass(value.auth === 'password')} onClick={() => set({ auth: 'password' })} disabled={disabled}>
            Password
          </button>
          <button type="button" className={segmentClass(value.auth === 'key')} onClick={() => set({ auth: 'key' })} disabled={disabled}>
            <KeyRound className="h-3.5 w-3.5" /> SSH private key
          </button>
        </div>
      </div>
      {value.auth === 'password' ? (
        <Field label="Password *" htmlFor={`${id}-password`} hint="Also used for sudo.">
          <Input id={`${id}-password`} type="password" value={value.password} onChange={(e) => set({ password: e.target.value })} autoComplete="new-password" disabled={disabled} />
        </Field>
      ) : (
        <>
          <Field label="Private key *" htmlFor={`${id}-key`} hint="Paste the whole key, including the BEGIN and END lines (unencrypted OpenSSH or PEM).">
            <Textarea
              id={`${id}-key`}
              value={value.privateKey}
              onChange={(e) => set({ privateKey: e.target.value })}
              placeholder={'-----BEGIN OPENSSH PRIVATE KEY-----\n...\n-----END OPENSSH PRIVATE KEY-----'}
              className="min-h-28 font-mono text-xs"
              spellCheck={false}
              disabled={disabled}
            />
          </Field>
          <Field label="sudo password" htmlFor={`${id}-sudo`} hint="Leave empty for root or when this user has passwordless sudo.">
            <Input id={`${id}-sudo`} type="password" value={value.sudoPassword} onChange={(e) => set({ sudoPassword: e.target.value })} autoComplete="new-password" disabled={disabled} />
          </Field>
        </>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="outline" size="sm" onClick={onTest} disabled={disabled || testing || !sshComplete(value)}>
          {testing ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Plug className="h-3.5 w-3.5" />}
          Test connection
        </Button>
        {test && (
          <span className={cn('flex items-center gap-1.5 text-xs', test.success ? 'text-emerald-700' : 'text-red-700')}>
            {test.success ? <CheckCircle2 className="h-3.5 w-3.5" /> : <XCircle className="h-3.5 w-3.5" />}
            {test.message}
          </span>
        )}
      </div>
      {test?.success && (
        <div className="flex flex-wrap gap-2 text-xs">
          <Badge variant="secondary">.NET: {test.dotNetRuntimes || 'not installed (installed on deploy)'}</Badge>
          <Badge variant="secondary">Nginx: {test.nginxInstalled ? 'installed' : 'not installed'}</Badge>
          {test.apacheInstalled && <Badge variant="secondary">Apache: installed</Badge>}
          {test.diskFreeMb != null && <Badge variant={test.diskFreeMb < 2048 ? 'warning' : 'secondary'}>{(test.diskFreeMb / 1024).toFixed(1)} GB free in /var</Badge>}
        </div>
      )}
    </div>
  );
}

// ---- Guides ----------------------------------------------------------------------------------------------------

const WINDOWS_TO_LINUX: [string, string][] = [
  ['IIS website + bindings', 'Nginx (or Apache) site in /etc/nginx/sites-available'],
  ['IIS application pool (w3wp.exe)', 'systemd service running Kestrel (dotnet App.dll)'],
  ['ASP.NET Core Module in IIS', 'Nginx reverse proxy → http://127.0.0.1:5000'],
  ['C:\\inetpub\\wwwroot\\app', '/var/www/app'],
  ['App pool identity + NTFS rights', 'service user www-data + chown / chmod'],
  ['services.msc / IIS Manager', 'systemctl status | restart | enable'],
  ['Event Viewer', 'journalctl -u <service> -f'],
  ['Windows authentication to SQL', 'SQL login in appsettings.json'],
  ['.pfx certificate in the store', 'PEM .crt/.key files (e.g. certbot / Let\'s Encrypt)'],
];

function WindowsLinuxTable() {
  return (
    <div className="overflow-hidden rounded-md border border-border text-xs">
      <table className="w-full">
        <thead className="bg-muted/60 text-left text-muted-foreground">
          <tr>
            <th className="px-3 py-2 font-medium">Windows (IIS)</th>
            <th className="px-3 py-2 font-medium">Linux</th>
          </tr>
        </thead>
        <tbody>
          {WINDOWS_TO_LINUX.map(([windows, linux]) => (
            <tr key={windows} className="border-t border-border align-top">
              <td className="px-3 py-2">{windows}</td>
              <td className="px-3 py-2 font-mono text-[11px]">{linux}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Collapsible({ title, icon: Icon, defaultOpen, children }: { title: string; icon: LucideIcon; defaultOpen?: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(!!defaultOpen);
  return (
    <div className="rounded-lg border border-border">
      <button type="button" onClick={() => setOpen(!open)} className="flex w-full items-center gap-2 px-4 py-3 text-left text-sm font-semibold text-foreground">
        {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        <Icon className="h-4 w-4 text-muted-foreground" />
        {title}
      </button>
      {open && <div className="space-y-3 border-t border-border px-4 py-3 text-sm text-muted-foreground">{children}</div>}
    </div>
  );
}

function ManualGuide({ target, slug, serviceName }: { target: LinuxDeploymentTarget; slug: string; serviceName: string }) {
  const order =
    target === 'ThreeTier' ? 'database kit → API kit → frontend kit' : target === 'TwoTier' ? 'API kit → frontend kit' : 'the kit';
  const hasApi = target !== 'Frontend';
  const kit = `${slug || 'app'}_linux-${target === 'Frontend' ? 'frontend' : 'api'}-1.0.123`;

  return (
    <div className="space-y-3">
      <Collapsible title="Manual deployment, step by step" icon={BookOpen} defaultOpen>
        <p>Each kit is a <code>.tar.gz</code> with the build, a ready <code>Deploy.sh</code> and a README for that server. Deploy {order}.</p>
        <ol className="list-decimal space-y-3 pl-5">
          <li>
            <span className="text-foreground">Copy the kit to the server</span> (PowerShell on Windows 10+ has <code>scp</code>; WinSCP also works):
            <CodeBlock>{`scp ${kit}.tar.gz ubuntu@SERVER_IP:/tmp/`}</CodeBlock>
          </li>
          <li>
            <span className="text-foreground">Log in and extract it:</span>
            <CodeBlock>{`ssh ubuntu@SERVER_IP\ncd /tmp && tar -xzf ${kit}.tar.gz && cd ${kit}`}</CodeBlock>
          </li>
          {hasApi && (
            <li>
              <span className="text-foreground">Set the database connection</span> in <code>app/appsettings.json</code>. Linux has no Windows authentication, so use a SQL login. In nano: Ctrl+O, Enter saves; Ctrl+X exits.
              <CodeBlock>{`nano app/appsettings.json\n\n"ConnectionStrings": {\n  "DefaultConnection": "Server=10.0.0.5,1433;Database=AuditVare;User Id=app_user;Password=...;TrustServerCertificate=True"\n}`}</CodeBlock>
            </li>
          )}
          <li>
            <span className="text-foreground">Run the script as root.</span> It shows its plan and asks before changing anything:
            <CodeBlock>{`sudo bash Deploy.sh`}</CodeBlock>
          </li>
          <li>
            <span className="text-foreground">Check it.</span>
            <CodeBlock>
              {hasApi
                ? `sudo systemctl status ${serviceName}     # running?\nsudo journalctl -u ${serviceName} -f     # live logs\ncurl -I http://YOUR_HOST                 # answers through Nginx?`
                : `curl -I http://YOUR_HOST\nsudo nginx -t && sudo systemctl reload nginx`}
            </CodeBlock>
          </li>
        </ol>
      </Collapsible>

      <Collapsible title="How Linux hosting differs from IIS" icon={Terminal}>
        <p>
          On Windows, IIS receives requests and runs the app inside an application pool. On Linux the app runs on its own as{' '}
          <span className="text-foreground">Kestrel</span> (the web server built into .NET), kept alive by{' '}
          <span className="text-foreground">systemd</span> (the Linux service manager). <span className="text-foreground">Nginx</span> listens on
          ports 80/443, handles HTTPS and forwards requests to Kestrel on <code>127.0.0.1:5000</code>. The frontend is plain files Nginx serves directly.
        </p>
        <WindowsLinuxTable />
      </Collapsible>

      <Collapsible title="What Deploy.sh does" icon={Rocket}>
        <ol className="list-decimal space-y-1 pl-5">
          <li>Installs what is missing: the ASP.NET Core runtime, Nginx/Apache, Redis (API only).</li>
          <li>Backs up the current folder to <code>/var/backups/deployvare</code> (last 5 kept).</li>
          <li>Copies <code>app/</code> to <code>/var/www/…</code>, keeping the server&apos;s own <code>appsettings.json</code>.</li>
          <li>
            <code>chown</code>/<code>chmod</code>: API files belong to <code>www-data</code> (750/640), frontend files are readable by Nginx (755/644).
          </li>
          {hasApi && <li>Writes <code>/etc/systemd/system/{serviceName}.service</code> (starts at boot, restarts on crash) and starts it.</li>}
          <li>Writes the Nginx site, runs <code>nginx -t</code> and reloads; a bad config is rolled back.</li>
        </ol>
      </Collapsible>
    </div>
  );
}

// ---- Page ------------------------------------------------------------------------------------------------------

export default function LinuxDeployPage() {
  const { selectedClientId } = useClientStore();
  const [applications, setApplications] = useState<Application[]>([]);
  const [applicationId, setApplicationId] = useState(0);
  const [environment, setEnvironment] = useState<ArtifactEnvironment>('Dev');
  const [architecture, setArchitecture] = useState<Architecture>('Backend');
  const [tierLayout, setTierLayout] = useState<'TwoTier' | 'ThreeTier'>('TwoTier');
  const [mode, setMode] = useState<Mode>('Automated');

  // Builds
  const [builds, setBuilds] = useState<LinuxBuilds | null>(null);
  const [buildsLoading, setBuildsLoading] = useState(false);
  const [buildsError, setBuildsError] = useState('');
  const [apiArtifact, setApiArtifact] = useState('');
  const [frontendArtifact, setFrontendArtifact] = useState('');
  const buildsRequestRef = useRef(0);

  // Site settings
  const [apiHost, setApiHost] = useState('');
  const [frontendHost, setFrontendHost] = useState('');
  const [webServer, setWebServer] = useState<'nginx' | 'apache'>('nginx');
  const [proxyApi, setProxyApi] = useState(true);
  const [frontendApiUrl, setFrontendApiUrl] = useState('');
  const [installRedis, setInstallRedis] = useState(true);
  const [runMigrations, setRunMigrations] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [apiPath, setApiPath] = useState('');
  const [frontendPath, setFrontendPath] = useState('');
  const [serviceName, setServiceName] = useState('');
  const [apiPort, setApiPort] = useState('5000');
  const [proxyUpstream, setProxyUpstream] = useState('');
  const [apiCert, setApiCert] = useState('');
  const [apiCertKey, setApiCertKey] = useState('');
  const [frontendCert, setFrontendCert] = useState('');
  const [frontendCertKey, setFrontendCertKey] = useState('');

  // Database (template for a generated appsettings.json)
  const [dbServer, setDbServer] = useState('');
  const [dbName, setDbName] = useState('');
  const [dbUser, setDbUser] = useState('');
  const [dbPassword, setDbPassword] = useState('');

  // Servers (automated)
  const [apiServer, setApiServer] = useState<SshForm>(emptySsh);
  const [frontendServer, setFrontendServer] = useState<SshForm>(emptySsh);
  const [sameServer, setSameServer] = useState(true);
  const [tests, setTests] = useState<Record<'api' | 'frontend', LinuxConnectionTest | null>>({ api: null, frontend: null });
  const [testing, setTesting] = useState<'api' | 'frontend' | null>(null);

  // Running operation
  const [running, setRunning] = useState(false);
  const [progress, setProgress] = useState<DeploymentProgress | null>(null);
  const [runError, setRunError] = useState('');
  const [result, setResult] = useState<LinuxDeploymentResult | null>(null);
  const [showOutput, setShowOutput] = useState(false);

  const target: LinuxDeploymentTarget = architecture === 'Tiered' ? tierLayout : architecture;
  const hasApi = target !== 'Frontend';
  const hasFrontend = target !== 'Backend';
  const tiered = hasApi && hasFrontend;
  const slug = builds?.slug ?? '';
  const effectiveService = serviceName.trim() || `${slug || 'app'}-api`;
  const frontendOnApiServer = tiered && sameServer;
  const threeTierAutomated = mode === 'Automated' && target === 'ThreeTier';

  useEffect(() => {
    let cancelled = false;
    api
      .getApplications(selectedClientId)
      .then((apps) => {
        if (cancelled) return;
        setApplications(apps);
        setApplicationId((current) => (apps.some((a) => a.id === current) ? current : apps[0]?.id ?? 0));
      })
      .catch((err) => toast.error(errorMessage(err, 'Failed to load applications')));
    return () => {
      cancelled = true;
    };
  }, [selectedClientId]);

  const loadBuilds = async () => {
    if (!applicationId) return;
    const request = ++buildsRequestRef.current;
    setBuildsLoading(true);
    setBuildsError('');
    try {
      const latest = await api.getLinuxBuilds(applicationId, environment);
      if (request !== buildsRequestRef.current) return;
      setBuilds(latest);
      setApiArtifact(latest.api.suggestedArtifactName ?? '');
      setFrontendArtifact(latest.frontend.suggestedArtifactName ?? '');
      setRunMigrations(latest.api.runDatabaseMigrations);
    } catch (err) {
      if (request !== buildsRequestRef.current) return;
      setBuilds(null);
      setBuildsError(errorMessage(err, 'Failed to load the builds'));
    } finally {
      if (request === buildsRequestRef.current) setBuildsLoading(false);
    }
  };

  useEffect(() => {
    setBuilds(null);
    void loadBuilds();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [applicationId, environment]);

  const runTest = async (which: 'api' | 'frontend') => {
    const form = which === 'api' ? apiServer : frontendServer;
    setTesting(which);
    try {
      const outcome = await api.testLinuxConnection(toTarget(form));
      setTests((previous) => ({ ...previous, [which]: outcome }));
    } catch (err) {
      setTests((previous) => ({ ...previous, [which]: { success: false, supportedOs: false, message: errorMessage(err, 'Connection test failed') } }));
    } finally {
      setTesting(null);
    }
  };

  const buildRequest = (): LinuxPackageRequest => ({
    applicationId,
    environment,
    target,
    apiBuildId: hasApi ? builds?.api.build?.id : undefined,
    apiArtifactName: hasApi ? apiArtifact || undefined : undefined,
    frontendBuildId: hasFrontend ? builds?.frontend.build?.id : undefined,
    frontendArtifactName: hasFrontend ? frontendArtifact || undefined : undefined,
    apiHostName: hasApi ? apiHost.trim() || undefined : undefined,
    frontendHostName: hasFrontend ? frontendHost.trim() || undefined : undefined,
    apiDeploymentPath: apiPath.trim() || undefined,
    frontendDeploymentPath: frontendPath.trim() || undefined,
    serviceName: serviceName.trim() || undefined,
    apiPort: Number(apiPort) || 5000,
    webServer,
    apiCertificatePath: apiCert.trim() || undefined,
    apiCertificateKeyPath: apiCertKey.trim() || undefined,
    frontendCertificatePath: frontendCert.trim() || undefined,
    frontendCertificateKeyPath: frontendCertKey.trim() || undefined,
    proxyApiThroughFrontend: hasFrontend && proxyApi,
    apiProxyUpstream: proxyUpstream.trim() || undefined,
    frontendApiUrl: hasFrontend ? frontendApiUrl.trim() || undefined : undefined,
    installRedis: hasApi && installRedis,
    runMigrations: hasApi && runMigrations,
    databaseServer: dbServer.trim() || undefined,
    databaseName: dbName.trim() || undefined,
    databaseUser: dbUser.trim() || undefined,
  });

  // Polls the stage list while a background operation runs
  const track = async (progressId: string, start: () => Promise<string>) => {
    setRunning(true);
    setRunError('');
    setProgress(null);
    setResult(null);
    const poll = async () => {
      try {
        const latest = await api.getDeploymentProgress(progressId);
        if (latest) setProgress((previous) => (previous?.isCompleted && !latest.isCompleted ? previous : latest));
      } catch {
        // Progress is best effort; the operation result is authoritative
      }
    };
    const timer = setInterval(poll, 1500);
    try {
      await start();
      const operation = await waitForOperation(progressId, 1500);
      await poll();
      return operation;
    } finally {
      clearInterval(timer);
      setRunning(false);
    }
  };

  const handleDeploy = async () => {
    const progressId = newProgressId();
    const frontendTarget = frontendOnApiServer ? apiServer : frontendServer;
    try {
      const operation = await track(progressId, () =>
        api.startLinuxDeployment({
          ...buildRequest(),
          apiServer: hasApi ? toTarget(apiServer) : undefined,
          frontendServer: hasFrontend ? toTarget(hasApi ? frontendTarget : frontendServer) : undefined,
          databasePassword: hasApi && dbPassword ? dbPassword : undefined,
          progressId,
        })
      );
      if (operation.status === 'Failed') throw new Error(operation.error || 'The deployment failed');
      const deployment = operation.result as LinuxDeploymentResult;
      setResult(deployment);
      toast.success(deployment.message);
    } catch (err) {
      const message = errorMessage(err, 'The deployment failed');
      setRunError(message);
      toast.error(message);
    }
  };

  const handleDownload = async () => {
    const progressId = newProgressId();
    try {
      const operation = await track(progressId, () => api.startLinuxPackage({ ...buildRequest(), progressId }));
      const token = operation.downloadToken;
      if (operation.status === 'Failed' || !token) throw new Error(operation.error || 'Failed to build the kits');
      for (const file of operation.files ?? []) {
        const url = api.operationDownloadUrl(progressId, token, file.index);
        const link = document.createElement('a');
        link.href = url;
        link.download = file.fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        toast.success(`Downloading ${file.fileName} (${formatSize(file.sizeBytes)})`, {
          description: (operation.files?.length ?? 0) > 1 ? 'One kit per server. Allow multiple downloads if the browser asks. Links stay valid for 2 hours.' : 'The link stays valid for 2 hours.',
          duration: 20000,
          action: { label: 'Download again', onClick: () => window.open(url, '_blank') },
        });
        // Browsers drop downloads started in the same instant
        if ((operation.files?.length ?? 0) > 1) await new Promise((resolve) => setTimeout(resolve, 1200));
      }
    } catch (err) {
      const message = errorMessage(err, 'Failed to build the kits');
      setRunError(message);
      toast.error(message);
    }
  };

  // What blocks the main button
  const missing: string[] = [];
  if (!applicationId) missing.push('an application');
  if (buildsLoading) missing.push('the builds to load');
  else if (!builds) missing.push('a build');
  else {
    if (hasApi && (builds.api.error || !apiArtifact)) missing.push('a backend build');
    if (hasFrontend && (builds.frontend.error || !frontendArtifact)) missing.push('a frontend build');
  }
  if (hasFrontend && !frontendHost.trim()) missing.push('the frontend host name');
  if (hasApi && !apiHost.trim() && !(hasFrontend && proxyApi)) missing.push('the API host name');
  if (hasFrontend && !hasApi && proxyApi && !proxyUpstream.trim()) missing.push('the /api route target (under Advanced), or untick the /api route');
  if (mode === 'Automated') {
    if (hasApi && !sshComplete(apiServer)) missing.push('the API server login');
    if (hasFrontend && !(hasApi && sameServer) && !sshComplete(frontendServer)) missing.push('the frontend server login');
  }
  const canRun = missing.length === 0 && !running;

  const stages = progress?.stages ?? [];

  const databasePanel = (
                  <div className="space-y-3 rounded-lg border border-border p-4">
                    <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
                      <Database className="h-4 w-4 text-muted-foreground" />{' '}
                      {threeTierAutomated ? 'Tier 3: Database server (SQL Server)' : 'Database connection (appsettings.json)'}
                    </div>
                    {threeTierAutomated && (
                      <p className="text-xs text-muted-foreground">
                        No SSH login is needed here: the API server connects to SQL Server (Windows or Linux) on port 1433, and database
                        migrations, when ticked, run from the API server. Allow port 1433 from the API server on the SQL Server&apos;s firewall.
                      </p>
                    )}
                    <p className="text-xs text-muted-foreground">
                      When the build has no <code>appsettings.json</code>, a template is added to the backend folder with these values in{' '}
                      <code>ConnectionStrings</code>.{' '}
                      {mode === 'Manual'
                        ? 'The password stays <SQL_PASSWORD>: edit the file before running Deploy.sh.'
                        : 'Enter the password only when the server has no appsettings.json yet; an existing one on the server is always kept.'}
                    </p>
                    <div className="grid gap-4 sm:grid-cols-3">
                      <Field label="SQL Server host" htmlFor="db-server">
                        <Input id="db-server" value={dbServer} onChange={(e) => setDbServer(e.target.value)} placeholder="10.0.0.5" disabled={running} />
                      </Field>
                      <Field label="Database" htmlFor="db-name">
                        <Input id="db-name" value={dbName} onChange={(e) => setDbName(e.target.value)} placeholder="AuditVare" disabled={running} />
                      </Field>
                      <Field label="SQL login" htmlFor="db-user">
                        <Input id="db-user" value={dbUser} onChange={(e) => setDbUser(e.target.value)} placeholder="app_user" autoComplete="off" disabled={running} />
                      </Field>
                    </div>
                    {mode === 'Automated' && (
                      <Field label="SQL password" htmlFor="db-password" hint="Written only to the server's appsettings.json; never stored by DeployVare.">
                        <Input id="db-password" type="password" value={dbPassword} onChange={(e) => setDbPassword(e.target.value)} autoComplete="new-password" disabled={running} />
                      </Field>
                    )}
                  </div>
  );

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Linux Deployment"
          description="Deploy Azure DevOps builds to Ubuntu/Debian servers: Kestrel + systemd behind Nginx or Apache"
          icon={Terminal}
        />

        <div className="grid gap-6 lg:grid-cols-3">
          <div className="space-y-6 lg:col-span-2">
            {/* 1. What */}
            <Card>
              <CardHeader>
                <CardTitle>What to deploy</CardTitle>
                <CardDescription>Choose the application, the architecture and how it reaches the server</CardDescription>
              </CardHeader>
              <CardContent className="space-y-6">
                <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
                  <Field label="Application *" htmlFor="application">
                    <Select id="application" value={applicationId} onChange={(e) => setApplicationId(Number(e.target.value))} disabled={running}>
                      {applications.length === 0 && <option value={0}>No applications for this client</option>}
                      {applications.map((application) => (
                        <option key={application.id} value={application.id}>
                          {application.name}
                        </option>
                      ))}
                    </Select>
                  </Field>
                  <div className="space-y-1.5">
                    <Label>Environment</Label>
                    <div className="inline-flex rounded-lg bg-muted p-1">
                      {(['Dev', 'UAT'] as ArtifactEnvironment[]).map((env) => (
                        <button key={env} type="button" className={segmentClass(environment === env)} onClick={() => setEnvironment(env)} disabled={running}>
                          {env}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label>Architecture</Label>
                  <div className="grid gap-3 sm:grid-cols-3">
                    <OptionTile icon={Monitor} title="Front End" description="Static frontend files served by Nginx/Apache" selected={architecture === 'Frontend'} onClick={() => setArchitecture('Frontend')} disabled={running} />
                    <OptionTile icon={Server} title="Back End" description=".NET API as a systemd service behind a reverse proxy" selected={architecture === 'Backend'} onClick={() => setArchitecture('Backend')} disabled={running} />
                    <OptionTile icon={Layers} title="Two-Tier / Three-Tier" description="API and frontend together; three-tier keeps the database separate" selected={architecture === 'Tiered'} onClick={() => setArchitecture('Tiered')} disabled={running} />
                  </div>
                  {architecture === 'Tiered' && (
                    <div className="flex flex-wrap items-center gap-3 pt-1">
                      <div className="inline-flex rounded-lg bg-muted p-1">
                        <button type="button" className={segmentClass(tierLayout === 'TwoTier')} onClick={() => setTierLayout('TwoTier')} disabled={running}>
                          Two-Tier
                        </button>
                        <button type="button" className={segmentClass(tierLayout === 'ThreeTier')} onClick={() => {
                          setTierLayout('ThreeTier');
                          setSameServer(false);
                        }} disabled={running}>
                          <Database className="h-3.5 w-3.5" /> Three-Tier
                        </button>
                      </div>
                      <span className="text-xs text-muted-foreground">
                        {tierLayout === 'TwoTier'
                          ? 'API (which also reaches the database) + frontend.'
                          : 'Frontend + API + a separate SQL Server. Manual downloads add a database kit; automated deployments migrate from the API server.'}
                      </span>
                    </div>
                  )}
                </div>

                <div className="space-y-2">
                  <Label>Deployment mode</Label>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <OptionTile icon={Zap} title="Automated Deployment" description="Connect over SSH; Deploy uploads the build and runs everything" selected={mode === 'Automated'} onClick={() => setMode('Automated')} disabled={running} />
                    <OptionTile icon={Download} title="Manual Deployment" description="Download ready kits (.tar.gz) with appsettings.json and Deploy.sh" selected={mode === 'Manual'} onClick={() => setMode('Manual')} disabled={running} />
                  </div>
                </div>
              </CardContent>
            </Card>

            {/* 2. Builds */}
            <Card>
              <CardHeader>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <CardTitle className="flex items-center gap-2">
                      <Cloud className="h-4 w-4" /> Azure DevOps build
                    </CardTitle>
                    <CardDescription>
                      The latest successful build. An artifact with &quot;linux&quot; in its name (e.g. a <code>linux-x64</code> publish) is chosen automatically.
                    </CardDescription>
                  </div>
                  <Button type="button" variant="ghost" size="sm" onClick={() => void loadBuilds()} disabled={buildsLoading || running || !applicationId}>
                    <RefreshCw className={cn('h-3.5 w-3.5', buildsLoading && 'animate-spin')} /> Refresh
                  </Button>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                {buildsLoading && (
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Loader2 className="h-4 w-4 animate-spin" /> Looking up the latest builds...
                  </div>
                )}
                {buildsError && <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">{buildsError}</div>}
                {builds && !buildsLoading && (
                  <>
                    {hasApi && <BuildRow label="Back end" option={builds.api} artifact={apiArtifact} onArtifactChange={setApiArtifact} disabled={running} />}
                    {hasFrontend && <BuildRow label="Front end" option={builds.frontend} artifact={frontendArtifact} onArtifactChange={setFrontendArtifact} disabled={running} />}
                  </>
                )}
              </CardContent>
            </Card>

            {/* 3. Settings */}
            <Card>
              <CardHeader>
                <CardTitle>Site settings</CardTitle>
                <CardDescription>Where the app answers and how the web server reaches it</CardDescription>
              </CardHeader>
              <CardContent className="space-y-5">
                <div className="grid gap-4 sm:grid-cols-2">
                  {hasFrontend && (
                    <Field label="Frontend host name *" htmlFor="frontend-host" hint="Domain or the server's IP; the Nginx server_name.">
                      <Input id="frontend-host" value={frontendHost} onChange={(e) => setFrontendHost(e.target.value)} placeholder="app.client.com" disabled={running} />
                    </Field>
                  )}
                  {hasApi && (
                    <Field
                      label={hasFrontend && proxyApi ? 'API host name (optional)' : 'API host name *'}
                      htmlFor="api-host"
                      hint={hasFrontend && proxyApi ? 'Empty: the API is reached only through the frontend\u2019s /api route.' : 'Domain or the server\u2019s IP for the API site.'}
                    >
                      <Input id="api-host" value={apiHost} onChange={(e) => setApiHost(e.target.value)} placeholder="api.client.com" disabled={running} />
                    </Field>
                  )}
                  <Field label="Web server (reverse proxy)" htmlFor="web-server">
                    <Select id="web-server" value={webServer} onChange={(e) => setWebServer(e.target.value as 'nginx' | 'apache')} disabled={running}>
                      <option value="nginx">Nginx (recommended)</option>
                      <option value="apache">Apache</option>
                    </Select>
                  </Field>
                  {hasFrontend && (
                    <Field label="API URL for the frontend (VITE_API_URL)" htmlFor="api-url" hint="Written to config.js. Empty keeps the build's config.js.">
                      <Input id="api-url" value={frontendApiUrl} onChange={(e) => setFrontendApiUrl(e.target.value)} placeholder={proxyApi && frontendHost ? `http://${frontendHost}/api` : 'https://api.client.com'} disabled={running} />
                    </Field>
                  )}
                </div>

                <div className="space-y-2">
                  {hasFrontend && (
                    <label className="flex items-start gap-2 text-sm">
                      <input type="checkbox" className="mt-0.5" checked={proxyApi} onChange={(e) => setProxyApi(e.target.checked)} disabled={running} />
                      <span>
                        Route <code>/api</code> on the frontend site to the backend
                        <span className="block text-xs text-muted-foreground">One address for both, no CORS. {hasApi ? 'Same server: goes to 127.0.0.1; otherwise to the API host name.' : 'Set the target under Advanced.'}</span>
                      </span>
                    </label>
                  )}
                  {hasApi && (
                    <>
                      <label className="flex items-start gap-2 text-sm">
                        <input type="checkbox" className="mt-0.5" checked={installRedis} onChange={(e) => setInstallRedis(e.target.checked)} disabled={running} />
                        <span>Install Redis on the API server when missing (port 6379)</span>
                      </label>
                      <label className="flex items-start gap-2 text-sm">
                        <input type="checkbox" className="mt-0.5" checked={runMigrations} onChange={(e) => setRunMigrations(e.target.checked)} disabled={running} />
                        <span>
                          Run database migrations (DbMigrator with the build&apos;s SQL scripts)
                          <span className="block text-xs text-muted-foreground">
                            {target === 'ThreeTier' && mode === 'Manual'
                              ? 'Three-tier downloads always include a separate database kit; this setting does not apply.'
                              : 'The database must already exist and be managed by DbMigrator.'}
                          </span>
                        </span>
                      </label>
                    </>
                  )}
                </div>

                {hasApi && !threeTierAutomated && databasePanel}

                <button type="button" onClick={() => setShowAdvanced(!showAdvanced)} className="flex items-center gap-1 text-sm text-brand-blue hover:underline">
                  {showAdvanced ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />} Advanced: folders, service, port, HTTPS
                </button>
                {showAdvanced && (
                  <div className="grid gap-4 sm:grid-cols-2">
                    {hasApi && (
                      <>
                        <Field label="API folder" htmlFor="api-path">
                          <Input id="api-path" value={apiPath} onChange={(e) => setApiPath(e.target.value)} placeholder={`/var/www/${slug || 'app'}-api`} disabled={running} />
                        </Field>
                        <Field label="systemd service name" htmlFor="service">
                          <Input id="service" value={serviceName} onChange={(e) => setServiceName(e.target.value)} placeholder={`${slug || 'app'}-api`} disabled={running} />
                        </Field>
                        <Field label="Kestrel port (localhost only)" htmlFor="port">
                          <Input id="port" inputMode="numeric" value={apiPort} onChange={(e) => setApiPort(e.target.value.replace(/\D/g, ''))} disabled={running} />
                        </Field>
                      </>
                    )}
                    {hasFrontend && (
                      <Field label="Frontend folder" htmlFor="frontend-path">
                        <Input id="frontend-path" value={frontendPath} onChange={(e) => setFrontendPath(e.target.value)} placeholder={`/var/www/${slug || 'app'}-web`} disabled={running} />
                      </Field>
                    )}
                    {hasFrontend && proxyApi && (
                      <Field label="/api route target" htmlFor="upstream" hint="Empty: worked out from the servers and API host name.">
                        <Input id="upstream" value={proxyUpstream} onChange={(e) => setProxyUpstream(e.target.value)} placeholder="http://127.0.0.1:5000" disabled={running} />
                      </Field>
                    )}
                    {hasApi && (
                      <>
                        <Field label="API certificate (PEM on the server)" htmlFor="api-cert" hint="Empty: HTTP only. Both files must exist on the server.">
                          <Input id="api-cert" value={apiCert} onChange={(e) => setApiCert(e.target.value)} placeholder="/etc/letsencrypt/live/api.client.com/fullchain.pem" disabled={running} />
                        </Field>
                        <Field label="API certificate key" htmlFor="api-key">
                          <Input id="api-key" value={apiCertKey} onChange={(e) => setApiCertKey(e.target.value)} placeholder="/etc/letsencrypt/live/api.client.com/privkey.pem" disabled={running} />
                        </Field>
                      </>
                    )}
                    {hasFrontend && (
                      <>
                        <Field label="Frontend certificate (PEM on the server)" htmlFor="fe-cert">
                          <Input id="fe-cert" value={frontendCert} onChange={(e) => setFrontendCert(e.target.value)} placeholder="/etc/letsencrypt/live/app.client.com/fullchain.pem" disabled={running} />
                        </Field>
                        <Field label="Frontend certificate key" htmlFor="fe-key">
                          <Input id="fe-key" value={frontendCertKey} onChange={(e) => setFrontendCertKey(e.target.value)} placeholder="/etc/letsencrypt/live/app.client.com/privkey.pem" disabled={running} />
                        </Field>
                      </>
                    )}
                  </div>
                )}
              </CardContent>
            </Card>

            {/* 4. Servers (automated) */}
            {mode === 'Automated' && (
              <Card>
                <CardHeader>
                  <CardTitle>Linux server connection</CardTitle>
                  <CardDescription>
                    SSH logins are used for this deployment only and never stored
                    {target === 'ThreeTier' ? '. Three-tier: an SSH login for the API and frontend servers, and the SQL Server address for the database tier.' : ''}
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-4">
                  {hasApi && (
                    <SshServerForm
                      title={tiered && sameServer ? 'Server (API and frontend)' : target === 'ThreeTier' ? 'Tier 2: API server' : 'API server'}
                      value={apiServer}
                      onChange={(value) => {
                        setApiServer(value);
                        setTests((previous) => ({ ...previous, api: null }));
                      }}
                      test={tests.api}
                      testing={testing === 'api'}
                      onTest={() => void runTest('api')}
                      disabled={running}
                    />
                  )}
                  {tiered && (
                    <label className="flex items-center gap-2 text-sm">
                      <input type="checkbox" checked={sameServer} onChange={(e) => setSameServer(e.target.checked)} disabled={running} />
                      The frontend runs on the same server as the API
                    </label>
                  )}
                  {hasFrontend && !(tiered && sameServer) && (
                    <SshServerForm
                      title={target === 'ThreeTier' ? 'Tier 1: Frontend server' : 'Frontend server'}
                      value={frontendServer}
                      onChange={(value) => {
                        setFrontendServer(value);
                        setTests((previous) => ({ ...previous, frontend: null }));
                      }}
                      test={tests.frontend}
                      testing={testing === 'frontend'}
                      onTest={() => void runTest('frontend')}
                      disabled={running}
                    />
                  )}
                  {threeTierAutomated && databasePanel}
                </CardContent>
              </Card>
            )}

            {/* Action */}
            <Card>
              <CardContent className="flex flex-col gap-3 pt-6 sm:flex-row sm:items-center sm:justify-between">
                <div className="text-sm text-muted-foreground">
                  {missing.length > 0 ? (
                    <>Still needed: {missing.join(', ')}.</>
                  ) : mode === 'Automated' ? (
                    <>Uploads the build over SFTP, then runs Deploy.sh: files, permissions, systemd and {webServer}.</>
                  ) : (
                    <>Builds {target === 'ThreeTier' ? 'three kits' : tiered ? 'two kits' : 'one kit'} (.tar.gz) to copy to the server(s).</>
                  )}
                </div>
                {mode === 'Automated' ? (
                  <Button type="button" size="lg" onClick={() => void handleDeploy()} disabled={!canRun}>
                    {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Rocket className="h-4 w-4" />}
                    {running ? 'Deploying...' : 'Deploy'}
                  </Button>
                ) : (
                  <Button type="button" size="lg" onClick={() => void handleDownload()} disabled={!canRun}>
                    {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
                    {running ? 'Building kits...' : 'Download deployment kits'}
                  </Button>
                )}
              </CardContent>
            </Card>
          </div>

          {/* Right column: progress and guide */}
          <div className="space-y-6">
            {(running || progress || runError || result) && (
              <Card>
                <CardHeader>
                  <CardTitle>{mode === 'Automated' ? 'Deployment progress' : 'Building the kits'}</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  {stages.length === 0 && running && (
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                      <Loader2 className="h-4 w-4 animate-spin" /> Starting...
                    </div>
                  )}
                  <ol className="space-y-3">
                    {stages.map((stage, index) => (
                      <li key={stage.key} className="flex gap-3">
                        <StageIndicator state={stage.state} number={index + 1} />
                        <div className="min-w-0 text-sm">
                          <div className={cn(stage.state === 'Pending' ? 'text-muted-foreground' : 'text-foreground')}>{stage.label}</div>
                          {stage.detail && <div className="break-words text-xs text-muted-foreground">{stage.detail}</div>}
                        </div>
                      </li>
                    ))}
                  </ol>
                  {runError && (
                    <div className="flex gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">
                      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                      <span className="break-words">{runError}</span>
                    </div>
                  )}
                  {result && (
                    <div className="space-y-2">
                      <div className="flex gap-2 rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                        <span>{result.message}</span>
                      </div>
                      <button type="button" onClick={() => setShowOutput(!showOutput)} className="flex items-center gap-1 text-xs text-brand-blue hover:underline">
                        {showOutput ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />} Deploy.sh output
                      </button>
                      {showOutput &&
                        result.kits.map((kit) => (
                          <div key={kit.tier} className="space-y-1">
                            <div className="text-xs font-medium text-foreground">
                              {kit.tier} on {kit.host} (#{kit.buildNumber})
                            </div>
                            <CodeBlock>{kit.output.join('\n')}</CodeBlock>
                          </div>
                        ))}
                    </div>
                  )}
                </CardContent>
              </Card>
            )}

            {mode === 'Manual' ? (
              <ManualGuide target={target} slug={slug} serviceName={effectiveService} />
            ) : (
              <div className="space-y-3">
                <Collapsible title="What happens when you press Deploy" icon={Rocket} defaultOpen>
                  <ol className="list-decimal space-y-1 pl-5">
                    <li>Logs in over SSH and checks the OS (Ubuntu/Debian) and sudo.</li>
                    <li>Downloads the Linux build from Azure DevOps and assembles the kit, adding <code>appsettings.json</code> when the build has none.</li>
                    <li>Uploads it over SFTP to a private folder in <code>/tmp</code>.</li>
                    <li>
                      Runs <code>sudo bash Deploy.sh --yes</code>: installs what is missing, backs up, extracts to <code>/var/www/…</code>, sets{' '}
                      <code>chown</code>/<code>chmod</code>, restarts the <code>systemd</code> service and reloads {webServer}.
                    </li>
                    <li>Checks the app answers on its port, then removes the upload.</li>
                  </ol>
                  <p>The same kit and script are used for manual deployments, so both behave the same.</p>
                </Collapsible>
                <ManualGuide target={target} slug={slug} serviceName={effectiveService} />
              </div>
            )}
          </div>
        </div>
      </div>
    </ProtectedLayout>
  );
}
