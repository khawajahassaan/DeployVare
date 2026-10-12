'use client';

import { useEffect, useRef, useState } from 'react';
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
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { DeploymentStepsPanel } from '@/components/DeploymentStepsPanel';
import { getDeploymentSteps } from '@/lib/deploymentSteps';
import Link from 'next/link';
import api, {
  ApplicationServer,
  ApiDeploymentResult,
  ArtifactEnvironment,
  DeploymentProgress,
  DeploymentStageState,
  LatestBuildResult,
  License,
  ServerReadinessReport,
} from '@/lib/api';
import {
  Upload,
  Server,
  CheckCircle2,
  XCircle,
  Loader2,
  Clock,
  ChevronDown,
  ChevronRight,
  FolderArchive,
  Cloud,
  RefreshCw,
  GitCommit,
  ExternalLink,
  Rocket,
  ShieldCheck,
  AlertTriangle,
  Wrench,
  MinusCircle,
  Circle,
  ListChecks,
  Check,
  X,
  Minus,
  Download,
} from 'lucide-react';
import { cn, formatDate } from '@/lib/utils';

type DeploySource = 'azure' | 'upload';

type RequestError = {
  message?: string;
  response?: { data?: Partial<ApiDeploymentResult> & { error?: string; details?: string } };
};

// Same order and labels as API/Utilities/DeploymentStages.cs (live progress replaces this list while deploying)
const AZURE_DEPLOY_STEPS = [
  'Connect to Azure DevOps',
  'Connect to server and check readiness',
  'Verify build',
  'Download artifact',
  'Extract and validate package',
  'Check database',
  'Upload package to server',
  'Run database migrations',
  'Stop IIS services',
  'Back up existing files',
  'Deploy files',
  'Install licence',
  'Verify activation',
  'Start IIS services',
  'Verify deployment',
];

const newProgressId = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;

// Pipeline-style status: numbered while pending, spinner while running, round tick when done
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

const FRONTEND_UPLOAD_STEPS = ['Stop IIS services', 'Back up existing files', 'Extract ZIP to deployment path', 'Restart IIS services'];
const API_UPLOAD_STEPS = ['Stop IIS services', 'Back up existing files', 'Deploy new files', 'Restart IIS services'];

const segmentClass = (active: boolean) =>
  `inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
    active ? 'bg-card text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'
  }`;

const fileInputClass =
  'block w-full text-sm text-muted-foreground file:mr-4 file:rounded-[8px] file:border-0 file:bg-accent file:px-4 file:py-2 file:text-sm file:font-normal file:text-brand-blue hover:file:bg-[#D6E6F4] disabled:opacity-50';

// Step log lines are prefixed by the server with ✓ / ⚠ / ✗ / ⊘
function StepIcon({ step }: { step: string }) {
  const className = 'mt-0.5 h-3.5 w-3.5 shrink-0';
  if (step.startsWith('✓')) return <CheckCircle2 className={`${className} text-emerald-600`} />;
  if (step.startsWith('⚠')) return <AlertTriangle className={`${className} text-amber-600`} />;
  if (step.startsWith('✗')) return <XCircle className={`${className} text-red-600`} />;
  if (step.startsWith('⊘')) return <MinusCircle className={`${className} text-muted-foreground`} />;
  return <Circle className={`${className} text-muted-foreground/60`} />;
}

export default function AppDeployPage() {
  const { selectedClientId, clients } = useClientStore();
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [version, setVersion] = useState('');
  // Licensing is opt-in per deployment: the default deploys exactly as before, so existing clients,
  // UAT and the Dev server are unaffected until someone deliberately chooses a licensed deployment.
  const [licensingMode, setLicensingMode] = useState<'WithoutLicense' | 'WithLicense'>('WithoutLicense');
  const [licenseId, setLicenseId] = useState<number>(0);
  const [licenses, setLicenses] = useState<License[]>([]);
  const [files, setFiles] = useState<File[]>([]);
  const [zipFile, setZipFile] = useState<File | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [deploymentResult, setDeploymentResult] = useState<ApiDeploymentResult | null>(null);
  const [progress, setProgress] = useState<DeploymentProgress | null>(null);
  const [downloadingPackage, setDownloadingPackage] = useState(false);
  const [packageDialogOpen, setPackageDialogOpen] = useState(false);
  const [packageKind, setPackageKind] = useState<'single' | 'combined'>('single');
  const [freshDatabase, setFreshDatabase] = useState(false);
  const [certificateMode, setCertificateMode] = useState<'Default' | 'UserProvided'>('Default');
  const [siteHostName, setSiteHostName] = useState('');
  const [counterpartServerId, setCounterpartServerId] = useState(0);
  const [counterpartSiteHostName, setCounterpartSiteHostName] = useState('');
  const [certificateFile, setCertificateFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(true);
  const [showStructureGuide, setShowStructureGuide] = useState(false);

  // Azure DevOps artifact deployment
  const [deploySource, setDeploySource] = useState<DeploySource>('azure');
  const [environment, setEnvironment] = useState<ArtifactEnvironment>('Dev');
  const [latestBuild, setLatestBuild] = useState<LatestBuildResult | null>(null);
  const [buildLoading, setBuildLoading] = useState(false);
  const [buildError, setBuildError] = useState('');
  const [selectedArtifact, setSelectedArtifact] = useState('');
  const buildRequestRef = useRef(0);

  // Package building error (for manual deploy package modal)
  const [packageBuildError, setPackageBuildError] = useState('');

  // Server readiness (IIS, site, runtimes, Redis, SSL, bindings)
  const [readiness, setReadiness] = useState<ServerReadinessReport | null>(null);
  const [readinessLoading, setReadinessLoading] = useState(false);
  const [readinessError, setReadinessError] = useState('');
  const readinessRequestRef = useRef(0);

  // Frontend runtime settings written to config.js (null while unknown or for API servers)
  const [runtimeSettingsCount, setRuntimeSettingsCount] = useState<number | null>(null);

  useEffect(() => {
    if (selectedClientId) loadServers();
  }, [selectedClientId]);

  // Licences of the selected client, for the "With License" picker. Failing to load them must not block a
  // deployment: an empty list simply means there is nothing to install.
  useEffect(() => {
    setLicenses([]);
    setLicenseId(0);
    setLicensingMode('WithoutLicense');
    if (!selectedClientId) return;

    let cancelled = false;
    api.getLicenses(selectedClientId)
      .then((data) => { if (!cancelled) setLicenses(data.filter((l) => l.status !== 'Revoked')); })
      .catch(() => { if (!cancelled) setLicenses([]); });
    return () => { cancelled = true; };
  }, [selectedClientId]);

  useEffect(() => {
    if (selectedServerId) {
      loadReadiness(selectedServerId);
    } else {
      readinessRequestRef.current++;
      setReadiness(null);
      setReadinessError('');
      setReadinessLoading(false);
    }
  }, [selectedServerId]);

  useEffect(() => {
    setRuntimeSettingsCount(null);
    const server = servers.find((s) => s.id === selectedServerId);
    if (!server || server.serverType !== 'Frontend') return;

    let cancelled = false;
    api.getServerRuntimeConfig(server.id)
      .then((config) => { if (!cancelled) setRuntimeSettingsCount(config.settings.length); })
      .catch(() => { if (!cancelled) setRuntimeSettingsCount(null); });
    return () => { cancelled = true; };
  }, [selectedServerId, servers]);

  useEffect(() => {
    if (selectedServerId && deploySource === 'azure') {
      loadLatestBuild(selectedServerId, environment);
    }
  }, [selectedServerId, environment, deploySource]);

  // Live progress belongs to the server and source it was deployed with
  useEffect(() => {
    setProgress(null);
  }, [selectedServerId, deploySource]);

  useEffect(() => {
    if (!packageDialogOpen) {
      setPackageBuildError('');
      return;
    }
    if (!counterpartServerId) return;
    let cancelled = false;
    api.getServerReadiness(counterpartServerId)
      .then((report) => {
        if (cancelled) return;
        const host = report?.bindings?.find((b) => b.hostName)?.hostName ?? '';
        setCounterpartSiteHostName(host);
      })
      .catch(() => {
        if (!cancelled) setCounterpartSiteHostName('');
      });
    return () => { cancelled = true; };
  }, [packageDialogOpen, counterpartServerId]);

  const loadServers = async () => {
    try {
      const data = await api.getApplicationServers(selectedClientId);
      setServers(data.filter(s => s.isActive));
      setSelectedServerId(0);
      setLatestBuild(null);
    } catch (err) {
      const error = err as RequestError;
      console.error('Error loading servers:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    } finally {
      setLoading(false);
    }
  };

  // Shows the saved readiness report; runNow inspects the server again and saves the new result
  const loadReadiness = async (serverId: number, runNow = false) => {
    const requestId = ++readinessRequestRef.current;
    setReadinessLoading(true);
    setReadinessError('');
    setReadiness(null);

    try {
      const result = runNow ? await api.runServerReadinessCheck(serverId) : await api.getServerReadiness(serverId);
      if (requestId !== readinessRequestRef.current) return;
      setReadiness(result);
    } catch (err) {
      if (requestId !== readinessRequestRef.current) return;
      const error = err as RequestError;
      setReadinessError(error.response?.data?.message || error.message || 'The server readiness check failed');
    } finally {
      if (requestId === readinessRequestRef.current) setReadinessLoading(false);
    }
  };

  const loadLatestBuild = async (serverId: number, env: ArtifactEnvironment) => {
    // Ignore responses from requests superseded by a newer server/environment selection
    const requestId = ++buildRequestRef.current;
    setBuildLoading(true);
    setBuildError('');
    setLatestBuild(null);
    setSelectedArtifact('');

    try {
      const result = await api.getLatestAzureBuild(serverId, env);
      if (requestId !== buildRequestRef.current) return;
      setLatestBuild(result);
      setSelectedArtifact(result.selectedArtifactName ?? '');
    } catch (err) {
      if (requestId !== buildRequestRef.current) return;
      const error = err as RequestError;
      setBuildError(error.response?.data?.message || error.message || 'Failed to retrieve the latest build');
    } finally {
      if (requestId === buildRequestRef.current) setBuildLoading(false);
    }
  };

  const selectedServer = servers.find(s => s.id === selectedServerId);
  const isFrontend = selectedServer?.serverType === 'Frontend';
  const counterpartServers = selectedServer
    ? servers.filter((s) => s.applicationId === selectedServer.applicationId && s.serverType !== selectedServer.serverType)
    : [];
  const counterpartServer = counterpartServers.find((s) => s.id === counterpartServerId);
  const downloadableArtifacts = latestBuild?.build.artifacts.filter(a => a.isDownloadable) ?? [];
  const artifactLocked = !!latestBuild?.source.artifactName;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setFiles(Array.from(e.target.files));
    }
  };

  const handleZipChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      setZipFile(e.target.files[0]);
    }
  };

  const handleDeploy = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!selectedServerId) {
      alert('Please select a server');
      return;
    }

    if (!readiness?.isReady) {
      alert('This server is not ready for deployment. Complete Server Setup first.');
      return;
    }

    if (deploySource === 'azure') {
      if (!latestBuild || !selectedArtifact) {
        alert('Please select a build artifact to deploy');
        return;
      }
      const keptNote = latestBuild.source.preserveWebConfig || latestBuild.source.keepOnServerPaths
        ? ' (keeping web.config and configured files on the server)'
        : '';
      const plannedSteps = [
        `Download artifact '${selectedArtifact}' from Azure DevOps`,
        'Upload the package to the server',
        ...(latestBuild.source.runDatabaseMigrations
          ? [
              'Check the target database — migrations are skipped if it does not exist yet or is empty; stops if SQL Server is unreachable',
              'Run database migrations (DbMigrator) — stops here if they fail',
            ]
          : []),
        'Stop IIS services',
        'Backup existing files',
        `Deploy the new files${keptNote}`,
        ...(licensingMode === 'WithLicense'
          ? [
              'Install the licence on the server and mark it as a licensed installation',
              'Verify the activation — stops with IIS down if this server is not activated yet',
            ]
          : []),
        'Restart IIS services',
      ];
      if (!confirm(`Deploy ${environment} build #${latestBuild.build.buildNumber} to "${selectedServer?.serverName}"?\n\nThis will:\n${plannedSteps.map((step, i) => `${i + 1}. ${step}`).join('\n')}`)) {
        return;
      }
    } else if (isFrontend) {
      if (!zipFile) {
        alert('Please select a ZIP file to deploy');
        return;
      }
      if (!confirm(`Deploy frontend ZIP to "${selectedServer?.serverName}"?\n\nThis will:\n1. Stop IIS services\n2. Backup existing files\n3. Extract ZIP to deployment path\n4. Restart IIS services\n\nNote: web.config will be preserved.`)) {
        return;
      }
    } else {
      if (files.length === 0) {
        alert('Please select files to deploy');
        return;
      }
      if (!confirm(`Deploy ${files.length} file(s) to "${selectedServer?.serverName}"?\n\nThis will:\n1. Stop IIS services\n2. Backup existing files\n3. Deploy new files\n4. Restart IIS services`)) {
        return;
      }
    }

    setDeploying(true);
    setDeploymentResult(null);
    setProgress(null);

    // Azure DevOps deployments report live progress; the full step log still arrives with the final result
    const progressId = deploySource === 'azure' ? newProgressId() : null;
    const pollProgress = async () => {
      if (!progressId) return;
      try {
        const latest = await api.getDeploymentProgress(progressId);
        // A slow poll must not replace the final state with an older one
        if (latest) setProgress((previous) => (previous?.isCompleted && !latest.isCompleted ? previous : latest));
      } catch {
        // Progress is best effort; the deployment result is authoritative
      }
    };
    const pollTimer = progressId ? setInterval(pollProgress, 2000) : null;

    try {
      let result: ApiDeploymentResult;
      if (deploySource === 'azure') {
        result = await api.deployAzureArtifact({
          applicationServerId: selectedServerId,
          environment,
          buildId: latestBuild!.build.id,
          artifactName: selectedArtifact,
          version: version || undefined,
          progressId: progressId ?? undefined,
          licensingMode,
          licenseId: licensingMode === 'WithLicense' ? licenseId : undefined,
        });
      } else if (isFrontend) {
        result = await api.deployFrontend(selectedServerId, zipFile!, version || undefined);
      } else {
        result = await api.deployApi(selectedServerId, files, version || undefined);
      }
      setDeploymentResult(result);

      if (result.status === 'Success') {
        setFiles([]);
        setZipFile(null);
        setVersion('');
        const input = document.getElementById('files') as HTMLInputElement | null;
        if (input) input.value = '';
        const zipInput = document.getElementById('zipFile') as HTMLInputElement | null;
        if (zipInput) zipInput.value = '';
      }
    } catch (err) {
      const error = err as RequestError;
      console.error('Deployment error:', error);
      const data = error.response?.data;
      if (data?.deploymentSteps) {
        // Failed deployments return the full result (steps and error) with a 500 status
        setDeploymentResult(data as ApiDeploymentResult);
      } else {
        setDeploymentResult({
          deploymentId: 0,
          status: 'Failed',
          message: 'Deployment failed',
          errorMessage: data?.details || data?.message || data?.error || error.message,
          deploymentDurationSeconds: 0,
          deploymentSteps: [],
          startedAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          deployedFiles: [],
        });
      }
    } finally {
      if (pollTimer) clearInterval(pollTimer);
      await pollProgress();
      setDeploying(false);
    }
  };

  // Hands the build over as a ZIP so it can be deployed by hand; the server is not touched
  const openPackageDialog = () => {
    const host = readiness?.bindings?.find((b) => b.hostName)?.hostName ?? '';
    setSiteHostName(host);
    setFreshDatabase(false);
    setCertificateMode('Default');
    setCertificateFile(null);
    setPackageKind('single');
    const preferred = counterpartServers.length === 1
      ? counterpartServers[0]
      : counterpartServers.find((s) => s.hostName === selectedServer?.hostName) ?? counterpartServers[0];
    setCounterpartServerId(preferred?.id ?? 0);
    setCounterpartSiteHostName('');
    setPackageDialogOpen(true);
  };

  const handleDownloadPackage = async () => {
    if (!latestBuild || !selectedArtifact || !selectedServer) return;
    if (!siteHostName.trim()) {
      alert('Enter the HTTPS host name for this server (the Site URL from Server Setup).');
      return;
    }
    if (packageKind === 'combined') {
      if (!counterpartServerId) {
        alert('A three-tier package needs both an API server and a frontend server on this application.');
        return;
      }
      if (!counterpartSiteHostName.trim()) {
        alert('Enter the HTTPS host name for the other site.');
        return;
      }
    }
    if (certificateMode === 'UserProvided' && !certificateFile) {
      alert('Upload a .pfx certificate, or choose the default certificate option.');
      return;
    }

    setDownloadingPackage(true);
    setPackageBuildError('');
    try {
      const { blob, fileName } = await api.downloadDeploymentPackage({
        applicationServerId: selectedServerId,
        environment,
        buildId: latestBuild.build.id,
        artifactName: selectedArtifact,
        licensingMode,
        licenseId: licensingMode === 'WithLicense' ? licenseId : undefined,
        freshDatabase: (packageKind === 'combined' || selectedServer.serverType === 'Api') && freshDatabase,
        certificateMode,
        siteHostName: siteHostName.trim(),
        certificateFile: certificateMode === 'UserProvided' ? certificateFile : null,
        combined: packageKind === 'combined',
        counterpartServerId: packageKind === 'combined' ? counterpartServerId : undefined,
        counterpartSiteHostName: packageKind === 'combined' ? counterpartSiteHostName.trim() : undefined,
      });

      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
      setPackageDialogOpen(false);
    } catch (err) {
      const data = (err as { response?: { data?: unknown } }).response?.data;
      let message = (err as RequestError).message ?? 'Failed to build the package';
      if (data instanceof Blob) {
        try {
          const parsed = JSON.parse(await data.text());
          message = parsed.error || parsed.message || message;
        } catch {
          // Keep the original message
        }
      }
      setPackageBuildError(message);
    } finally {
      setDownloadingPackage(false);
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const deployDisabled =
    deploying ||
    !selectedServerId ||
    readinessLoading ||
    !readiness?.isReady ||
    (deploySource === 'azure'
      ? buildLoading || !latestBuild || !selectedArtifact
      : isFrontend ? !zipFile : files.length === 0);

  const pageHeader = (
    <PageHeader
      title="Deploy App"
      description="Deploy API or frontend builds from Azure DevOps, or upload files, to Windows IIS servers"
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

  const overviewSteps = deploySource === 'azure'
    ? AZURE_DEPLOY_STEPS
    : isFrontend ? FRONTEND_UPLOAD_STEPS : API_UPLOAD_STEPS;

  const liveStages = deploySource === 'azure' ? progress?.stages ?? null : null;
  const stageList = liveStages ?? overviewSteps.map((label) => ({ key: label, label, state: 'Pending' as DeploymentStageState, detail: null }));
  const runningStageIndex = liveStages ? liveStages.findIndex((stage) => stage.state === 'Running') : -1;
  const finishedStageCount = liveStages
    ? liveStages.filter((stage) => stage.state === 'Succeeded' || stage.state === 'Warning' || stage.state === 'Skipped').length
    : 0;

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
                    Select target server — the deployment adapts to the server type
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-6">
                  {/* Server Selection */}
                  <div className="space-y-1.5">
                    <Label htmlFor="server">Target Server *</Label>
                    <Select
                      id="server"
                      value={selectedServerId}
                      onChange={(e) => {
                        setSelectedServerId(Number(e.target.value));
                        setFiles([]);
                        setZipFile(null);
                        setDeploymentResult(null);
                        setLatestBuild(null);
                        setBuildError('');
                      }}
                      required
                      disabled={deploying}
                    >
                      <option value={0}>Select a server...</option>
                      {servers.map((server) => (
                        <option key={server.id} value={server.id}>
                          [{server.serverType}] {server.serverName} — {server.applicationName} ({server.hostName})
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
                        <Badge variant={isFrontend ? 'default' : 'secondary'}>
                          {selectedServer.serverType}
                        </Badge>
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
                        {isFrontend && runtimeSettingsCount !== null && (
                          <div className="sm:col-span-2">
                            <span className="text-muted-foreground">config.js:</span>
                            {runtimeSettingsCount > 0 ? (
                              <span className="ml-2 text-xs">{runtimeSettingsCount} runtime setting(s) will be written on deploy</span>
                            ) : (
                              <span className="ml-2 text-xs text-amber-700">
                                No runtime settings — the build&apos;s values (e.g. Dev URLs) will be used.{' '}
                                <a href={`/server-setup?serverId=${selectedServer.id}#runtime-config-setup`} className="font-medium underline">
                                  Configure
                                </a>
                              </span>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  )}

                  {/* Server Readiness */}
                  {selectedServer && (
                    <div
                      className={`rounded-lg border p-4 text-sm ${
                        readinessLoading
                          ? 'border-border bg-muted/30'
                          : readiness?.isReady
                            ? 'border-emerald-200 bg-emerald-50'
                            : 'border-amber-200 bg-amber-50'
                      }`}
                    >
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <div className="flex items-center gap-2 font-semibold text-foreground">
                          {readinessLoading ? (
                            <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
                          ) : readiness?.isReady ? (
                            <ShieldCheck className="h-4 w-4 text-emerald-600" />
                          ) : (
                            <AlertTriangle className="h-4 w-4 text-amber-600" />
                          )}
                          {readinessLoading
                            ? 'Loading server readiness...'
                            : !readiness
                              ? 'Server readiness has not been checked yet'
                              : readiness.isReady
                                ? `Server is ready for deployment (checked ${formatDate(readiness.checkedAt)})`
                                : `Server is not ready for deployment (checked ${formatDate(readiness.checkedAt)})`}
                        </div>
                        <div className="flex gap-2">
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => loadReadiness(selectedServerId, true)}
                            disabled={readinessLoading || deploying}
                          >
                            <RefreshCw className={`h-4 w-4 ${readinessLoading ? 'animate-spin' : ''}`} />
                            {readiness ? 'Re-check' : 'Run Check'}
                          </Button>
                          <Link href={`/server-setup?serverId=${selectedServerId}`}>
                            <Button type="button" variant="outline" size="sm">
                              <Wrench className="h-4 w-4" />
                              Server Setup
                            </Button>
                          </Link>
                        </div>
                      </div>
                      {readinessError && !readinessLoading && (
                        <p className="mt-3 text-red-700">{readinessError}</p>
                      )}
                      {readiness && !readiness.isReady && !readinessLoading && (
                        <ul className="mt-3 space-y-1">
                          {readiness.items
                            .filter((item) => item.required && item.status !== 'Pass')
                            .map((item) => (
                              <li key={item.key} className="flex items-start gap-2 text-amber-900">
                                {item.status === 'Fail' ? (
                                  <XCircle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-red-600" />
                                ) : (
                                  <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-600" />
                                )}
                                <span>
                                  <span className="font-medium">{item.name}:</span> {item.detail}
                                </span>
                              </li>
                            ))}
                        </ul>
                      )}
                      {readiness?.rebootPending && !readinessLoading && (
                        <p className="mt-3 text-xs text-amber-800">Windows reports a pending restart on this server.</p>
                      )}
                    </div>
                  )}

                  {/* Deployment Source */}
                  {selectedServer && (
                    <div className="space-y-2">
                      <Label>Deployment Source *</Label>
                      <div>
                        <div className="inline-flex rounded-lg border border-border bg-muted p-1">
                          <button
                            type="button"
                            className={segmentClass(deploySource === 'azure')}
                            onClick={() => { setDeploySource('azure'); setDeploymentResult(null); }}
                            disabled={deploying}
                          >
                            <Cloud className="h-4 w-4" />
                            Azure DevOps build
                          </button>
                          <button
                            type="button"
                            className={segmentClass(deploySource === 'upload')}
                            onClick={() => { setDeploySource('upload'); setDeploymentResult(null); }}
                            disabled={deploying}
                          >
                            <Upload className="h-4 w-4" />
                            Upload files
                          </button>
                        </div>
                      </div>
                      <p className="text-xs text-muted-foreground">
                        {deploySource === 'azure'
                          ? 'Latest successful Dev or UAT build'
                          : isFrontend ? 'Frontend build ZIP' : 'DLL, EXE and config files'}
                      </p>
                    </div>
                  )}

                  {/* Azure DevOps build */}
                  {selectedServer && deploySource === 'azure' && (
                    <div className="space-y-4">
                      <div className="flex flex-wrap items-end justify-between gap-3">
                        <div className="space-y-2">
                          <Label>Environment *</Label>
                          <div>
                            <div className="inline-flex rounded-lg border border-border bg-muted p-1">
                              {(['Dev', 'UAT'] as ArtifactEnvironment[]).map((env) => (
                                <button
                                  key={env}
                                  type="button"
                                  className={segmentClass(environment === env)}
                                  onClick={() => { setEnvironment(env); setDeploymentResult(null); }}
                                  disabled={deploying}
                                >
                                  {env}
                                </button>
                              ))}
                            </div>
                          </div>
                        </div>
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          onClick={() => loadLatestBuild(selectedServerId, environment)}
                          disabled={buildLoading || deploying}
                        >
                          <RefreshCw className={`h-4 w-4 ${buildLoading ? 'animate-spin' : ''}`} />
                          Refresh
                        </Button>
                      </div>

                      {buildLoading && (
                        <div className="flex items-center gap-2 rounded-lg border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                          <Loader2 className="h-4 w-4 animate-spin" />
                          Finding the latest successful {environment} build...
                        </div>
                      )}

                      {buildError && !buildLoading && (
                        <div className="space-y-1 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                          <p>{buildError}</p>
                          <p className="text-xs">
                            Check the connection and artifact sources on the{' '}
                            <a href="/azure-devops" className="font-medium underline">Azure DevOps</a> page.
                          </p>
                        </div>
                      )}

                      {latestBuild && !buildLoading && (
                        <div className="space-y-4 rounded-lg border border-border p-4 text-sm">
                          <div className="flex flex-wrap items-start justify-between gap-2">
                            <div>
                              <div className="flex items-center gap-2 font-semibold text-foreground">
                                <CheckCircle2 className="h-4 w-4 text-emerald-600" />
                                Build #{latestBuild.build.buildNumber}
                                <Badge variant={environment === 'UAT' ? 'warning' : 'default'}>{environment}</Badge>
                              </div>
                              <p className="mt-1 text-xs text-muted-foreground">
                                {latestBuild.build.pipelineName} · {latestBuild.build.result}
                              </p>
                            </div>
                            {latestBuild.build.webUrl && (
                              <a
                                href={latestBuild.build.webUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                className="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
                              >
                                View in Azure DevOps
                                <ExternalLink className="h-3 w-3" />
                              </a>
                            )}
                          </div>
                          <div className="grid grid-cols-1 gap-x-4 gap-y-2 sm:grid-cols-2">
                            <div>
                              <span className="text-muted-foreground">Repository:</span>
                              <span className="ml-2 break-all font-mono text-xs">{latestBuild.build.repository}</span>
                            </div>
                            <div>
                              <span className="text-muted-foreground">Branch:</span>
                              <span className="ml-2 break-all font-mono text-xs">{latestBuild.build.sourceBranch.replace('refs/heads/', '')}</span>
                            </div>
                            <div className="flex items-center">
                              <GitCommit className="h-3.5 w-3.5 text-muted-foreground" />
                              <span className="ml-2 font-mono text-xs">{latestBuild.build.sourceVersion.slice(0, 8)}</span>
                            </div>
                            <div>
                              <span className="text-muted-foreground">Finished:</span>
                              <span className="ml-2 text-xs">
                                {latestBuild.build.finishTime ? formatDate(latestBuild.build.finishTime) : '—'}
                              </span>
                            </div>
                            {latestBuild.build.requestedBy && (
                              <div className="sm:col-span-2">
                                <span className="text-muted-foreground">Requested by:</span>
                                <span className="ml-2 text-xs">{latestBuild.build.requestedBy}</span>
                              </div>
                            )}
                          </div>

                          <div className="space-y-2 border-t border-border pt-4">
                            <Label>Artifact *</Label>
                            <div className="space-y-2">
                              {latestBuild.build.artifacts.map((artifact) => {
                                const disabled = deploying || !artifact.isDownloadable
                                  || (artifactLocked && artifact.name.toLowerCase() !== latestBuild.source.artifactName!.toLowerCase());
                                return (
                                  <label
                                    key={artifact.name}
                                    className={`flex items-center justify-between gap-3 rounded-md border px-3 py-2 transition-colors ${
                                      selectedArtifact === artifact.name ? 'border-brand-blue bg-accent' : 'border-border'
                                    } ${disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer hover:bg-muted/40'}`}
                                  >
                                    <span className="flex items-center gap-2">
                                      <input
                                        type="radio"
                                        name="artifact"
                                        className="accent-primary"
                                        checked={selectedArtifact === artifact.name}
                                        onChange={() => setSelectedArtifact(artifact.name)}
                                        disabled={disabled}
                                      />
                                      <span className="font-mono text-xs">{artifact.name}</span>
                                      {!artifact.isDownloadable && (
                                        <span className="text-xs text-muted-foreground">(not downloadable)</span>
                                      )}
                                    </span>
                                    {artifact.sizeBytes != null && (
                                      <span className="text-xs text-muted-foreground">{formatFileSize(artifact.sizeBytes)}</span>
                                    )}
                                  </label>
                                );
                              })}
                            </div>
                            <p className="text-xs text-muted-foreground">
                              {artifactLocked
                                ? `The configured artifact '${latestBuild.source.artifactName}' is deployed`
                                : downloadableArtifacts.length > 1
                                  ? 'This build publishes several artifacts — choose the one to deploy'
                                  : 'Artifact to deploy'}
                              {latestBuild.source.artifactSubfolder && ` · folder '${latestBuild.source.artifactSubfolder}'`}
                              {latestBuild.source.preserveWebConfig && ' · server web.config is kept'}
                              {latestBuild.source.runDatabaseMigrations && ' · database migrations run before deploying'}
                            </p>
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  {/* Licensing — opt-in per deployment, so the default leaves existing servers untouched */}
                  {selectedServer && selectedServer.serverType === 'Api' && (
                    <div className="space-y-1.5">
                      <Label htmlFor="licensingMode">Licensing</Label>
                      <Select
                        id="licensingMode"
                        value={licensingMode}
                        onChange={(e) => {
                          const mode = e.target.value as 'WithoutLicense' | 'WithLicense';
                          setLicensingMode(mode);
                          if (mode === 'WithoutLicense') setLicenseId(0);
                        }}
                        disabled={deploying}
                      >
                        <option value="WithoutLicense">Without License</option>
                        <option value="WithLicense">With License</option>
                      </Select>

                      {licensingMode === 'WithLicense' ? (
                        <div className="space-y-1.5 pt-1.5">
                          <Label htmlFor="licenseId">Licence to install *</Label>
                          <Select
                            id="licenseId"
                            value={String(licenseId)}
                            onChange={(e) => setLicenseId(Number(e.target.value))}
                            disabled={deploying}
                          >
                            <option value="0">Select a licence...</option>
                            {licenses.map((licence) => (
                              <option key={licence.id} value={licence.id}>
                                {licence.licenseId} · {licence.product} · expires {formatDate(licence.expiresAt)}
                                {licence.isExpired ? ' (expired)' : ''}
                              </option>
                            ))}
                          </Select>
                          <p className="text-xs text-muted-foreground">
                            The licence is installed before IIS starts and binds to this server on activation.
                            {licenses.length === 0 && ' No licences have been issued for this client yet.'}
                          </p>
                        </div>
                      ) : (
                        <p className="text-xs text-muted-foreground">
                          Deploys exactly as before — the application does not ask for a licence
                        </p>
                      )}
                    </div>
                  )}

                  {/* Version */}
                  {selectedServer && (
                    <div className="space-y-1.5">
                      <Label htmlFor="version">Version (Optional)</Label>
                      <Input
                        id="version"
                        value={version}
                        onChange={(e) => setVersion(e.target.value)}
                        placeholder={deploySource === 'azure' && latestBuild ? latestBuild.build.buildNumber : '1.0.0'}
                        disabled={deploying}
                      />
                      {deploySource === 'azure' && (
                        <p className="text-xs text-muted-foreground">Defaults to the build number</p>
                      )}
                    </div>
                  )}

                  {/* File Upload — API mode */}
                  {deploySource === 'upload' && !isFrontend && selectedServer && (
                    <div className="space-y-1.5">
                      <Label htmlFor="files">Files to Deploy *</Label>
                      <input
                        id="files"
                        type="file"
                        onChange={handleFileChange}
                        multiple
                        accept=".dll,.exe,.json,.pdb,.config,.xml"
                        disabled={deploying}
                        className={fileInputClass}
                      />
                      <p className="text-xs text-muted-foreground">
                        Select DLL, EXE, JSON, PDB, and config files
                      </p>
                      {files.length > 0 && (
                        <div className="space-y-2 pt-2">
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
                    </div>
                  )}

                  {/* File Upload — Frontend mode */}
                  {deploySource === 'upload' && isFrontend && selectedServer && (
                    <div className="space-y-4">
                      {/* Structure Guide */}
                      <div className="overflow-hidden rounded-lg border border-border">
                        <button
                          type="button"
                          className="flex w-full items-center justify-between bg-muted/30 p-3 text-sm font-medium transition-colors hover:bg-muted/50"
                          onClick={() => setShowStructureGuide(!showStructureGuide)}
                        >
                          <div className="flex items-center gap-2">
                            <FolderArchive className="h-4 w-4 text-blue-500" />
                            <span>Expected ZIP structure</span>
                          </div>
                          {showStructureGuide ? (
                            <ChevronDown className="h-4 w-4 text-muted-foreground" />
                          ) : (
                            <ChevronRight className="h-4 w-4 text-muted-foreground" />
                          )}
                        </button>
                        {showStructureGuide && (
                          <div className="space-y-1 border-t border-border bg-muted/20 p-4 font-mono text-xs text-muted-foreground">
                            <div>frontend.zip</div>
                            <div className="ml-4">├── index.html</div>
                            <div className="ml-4">├── static/</div>
                            <div className="ml-8">├── css/</div>
                            <div className="ml-8">└── js/</div>
                            <div className="ml-4">└── (other build output files)</div>
                            <div className="mt-3 rounded-lg border border-amber-200 bg-amber-50 p-3 font-sans text-sm text-amber-800">
                              Do NOT include <strong>web.config</strong> in the ZIP — it will be preserved on the server.
                            </div>
                          </div>
                        )}
                      </div>

                      <div className="space-y-1.5">
                        <Label htmlFor="zipFile">Frontend ZIP File *</Label>
                        <input
                          id="zipFile"
                          type="file"
                          accept=".zip"
                          onChange={handleZipChange}
                          disabled={deploying}
                          className={fileInputClass}
                        />
                        <p className="text-xs text-muted-foreground">
                          Only .zip files accepted. The ZIP will be extracted directly into the deployment path.
                        </p>
                        {zipFile && (
                          <div className="flex items-center justify-between rounded-lg border border-border px-3 py-2 text-sm">
                            <span className="truncate font-mono text-xs">{zipFile.name}</span>
                            <span className="ml-2 text-xs text-muted-foreground">{formatFileSize(zipFile.size)}</span>
                          </div>
                        )}
                      </div>
                    </div>
                  )}

                  {/* Deploy Button */}
                  {selectedServer && (
                    <div className="border-t border-border pt-6">
                      <Button type="submit" className="w-full" disabled={deployDisabled}>
                        {deploying ? (
                          <>
                            <Loader2 className="h-4 w-4 animate-spin" />
                            Deploying...
                          </>
                        ) : deploySource === 'azure' ? (
                          <>
                            <Rocket className="h-4 w-4" />
                            {latestBuild ? `Deploy ${environment} Build #${latestBuild.build.buildNumber}` : 'Deploy Build'}
                          </>
                        ) : (
                          <>
                            <Upload className="h-4 w-4" />
                            {isFrontend ? 'Deploy Frontend' : 'Deploy API'}
                          </>
                        )}
                      </Button>

                      {deploySource === 'azure' && (
                        <div className="mt-3">
                          <Button
                            type="button"
                            variant="outline"
                            className="w-full"
                            onClick={openPackageDialog}
                            disabled={deploying || downloadingPackage || buildLoading || !latestBuild || !selectedArtifact}
                          >
                            {downloadingPackage ? (
                              <>
                                <Loader2 className="h-4 w-4 animate-spin" />
                                Preparing package...
                              </>
                            ) : (
                              <>
                                <Download className="h-4 w-4" />
                                Download package for manual deployment
                              </>
                            )}
                          </Button>
                          <p className="mt-2 text-xs text-muted-foreground">
                            This server only, or a three-tier zip (database, API and frontend kits). Copy it to the
                            client server(s) and run .\Deploy.ps1 as Administrator. Nothing is sent to the server from here.
                          </p>
                        </div>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>
            </form>

            {/* Deployment in progress (Azure DevOps) */}
            {deploying && deploySource === 'azure' && (
              <Card className="border-blue-200">
                <CardHeader>
                  <div className="flex items-center gap-3">
                    <Loader2 className="h-6 w-6 shrink-0 animate-spin text-primary" />
                    <div>
                      <CardTitle>Deployment in progress</CardTitle>
                      <CardDescription>
                        {liveStages && runningStageIndex >= 0
                          ? `Step ${runningStageIndex + 1} of ${liveStages.length}: ${liveStages[runningStageIndex].label}. `
                          : 'Starting the deployment. '}
                        Keep this page open — the full step log appears when it finishes.
                      </CardDescription>
                    </div>
                  </div>
                </CardHeader>
              </Card>
            )}

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
                <CardDescription>
                  {liveStages
                    ? progress?.isCompleted
                      ? progress.succeeded
                        ? `Completed: ${finishedStageCount} of ${liveStages.length} steps`
                        : 'Deployment failed'
                      : `${finishedStageCount} of ${liveStages.length} steps done`
                    : deploySource === 'azure'
                      ? 'What happens when an Azure DevOps build is deployed'
                      : isFrontend
                        ? 'What happens when a frontend ZIP is deployed'
                        : 'What happens when uploaded files are deployed'}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <ol className="space-y-2.5">
                  {stageList.map((stage, index) => (
                    <li key={stage.key} className="flex items-start gap-3 text-sm">
                      <StageIndicator state={stage.state} number={index + 1} />
                      <div className="min-w-0 pt-px">
                        <span
                          className={cn(
                            stage.state === 'Running' && 'font-medium text-foreground',
                            stage.state === 'Succeeded' && 'text-foreground',
                            stage.state === 'Warning' && 'text-amber-800',
                            stage.state === 'Failed' && 'font-medium text-red-700',
                            (stage.state === 'Pending' || stage.state === 'Skipped') && 'text-muted-foreground'
                          )}
                        >
                          {stage.label}
                        </span>
                        {stage.detail && (stage.state === 'Failed' || stage.state === 'Skipped' || stage.state === 'Warning') && (
                          <p className="mt-0.5 break-words text-xs text-muted-foreground">{stage.detail}</p>
                        )}
                      </div>
                    </li>
                  ))}
                </ol>
                <p className="mt-4 border-t border-border pt-4 text-xs text-muted-foreground">
                  Deploying is enabled once the server passes its readiness check. Use Server Setup to fix any failing items.
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
              <div className="flex items-center gap-2 text-sm text-muted-foreground sm:hidden">
                <Clock className="h-4 w-4" />
                <span>Duration: {deploymentResult.deploymentDurationSeconds}s</span>
              </div>

              {deploymentResult.errorMessage && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  <p className="font-medium text-red-800">Error:</p>
                  <p className="mt-1 break-words font-mono text-xs">{deploymentResult.errorMessage}</p>
                </div>
              )}

              {deploymentResult.backupFileName && (
                <div className="rounded-lg border border-blue-200 bg-blue-50/60 p-4 text-sm">
                  <p className="font-medium text-blue-900">Backup Created:</p>
                  <p className="mt-1 break-all font-mono text-xs text-blue-700">{deploymentResult.backupFileName}</p>
                </div>
              )}

              <div className="grid gap-4 lg:grid-cols-2">
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

                {deploymentResult.deployedFiles.length > 0 && (
                  <div className="space-y-2">
                    <p className="text-sm font-medium text-foreground">
                      Deployed Files ({deploymentResult.deployedFiles.length}):
                    </p>
                    <div className="max-h-96 overflow-auto rounded-lg border border-border">
                      <table className="dv-table text-[14px]">
                        <thead className="text-left text-[13px]">
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

      <Dialog open={packageDialogOpen} onOpenChange={(open) => { if (!downloadingPackage) setPackageDialogOpen(open); }}>
        <DialogContent className="max-w-none w-full max-h-screen h-screen p-0 gap-0 rounded-none border-0 fixed right-0 top-0 bottom-0 animate-in slide-in-from-right duration-300">
          <div className="flex w-full h-full">
            {/* Left side - Form */}
            <div className="flex-1 overflow-y-auto border-r border-border">
              <div className="flex flex-col h-full p-6">
                <DialogHeader>
                  <DialogTitle className={downloadingPackage ? 'font-bold' : ''}>
                    {downloadingPackage ? 'Building Package' : 'Manual Deploy package'}
                  </DialogTitle>
            <DialogDescription>
              {downloadingPackage
                ? 'Preparing your deployment package. Please wait...'
                : packageKind === 'combined'
                ? 'One zip with a database, an API and a frontend kit. On one server, run the root Deploy.ps1: it deploys the database, then the API, then the frontend. For separate servers, copy each kit to its own server and run its Deploy.ps1. Every script asks for the IIS and database names (Enter keeps the default). The servers do not need internet.'
                : `This package will prepare the server, configure IIS, deploy the application${selectedServer?.serverType === 'Api' ? ', configure the database, run DbMigrator' : ''}, and configure HTTPS. The client server does not need internet.`}
            </DialogDescription>
          </DialogHeader>

          {!downloadingPackage && <div className="space-y-4 text-sm">
            <div className="space-y-2">
              <Label>Package</Label>
              <label className="flex items-start gap-2 rounded-lg border border-border p-3">
                <input
                  type="radio"
                  className="mt-1 accent-primary"
                  checked={packageKind === 'single'}
                  onChange={() => setPackageKind('single')}
                  disabled={downloadingPackage}
                />
                <span>
                  <span className="font-medium">This server only</span>
                  <span className="block text-xs text-muted-foreground">
                    {selectedServer
                      ? `${selectedServer.serverType === 'Frontend' ? 'Frontend' : 'API'} — ${selectedServer.serverName}`
                      : 'The selected server'}
                  </span>
                </span>
              </label>
              <label className={`flex items-start gap-2 rounded-lg border p-3 ${counterpartServers.length === 0 ? 'border-border/70 opacity-70' : 'border-border'}`}>
                <input
                  type="radio"
                  className="mt-1 accent-primary"
                  checked={packageKind === 'combined'}
                  onChange={() => setPackageKind('combined')}
                  disabled={downloadingPackage || counterpartServers.length === 0}
                />
                <span>
                  <span className="font-medium">Three-tier: database + API + frontend</span>
                  <span className="block text-xs text-muted-foreground">
                    {counterpartServers.length === 0
                      ? 'Add the other server on this application to enable a three-tier zip. This server only still works.'
                      : 'Three kits in one download: database\\, api\\ and frontend\\. Run them on separate servers, or all on one server with the root Deploy.ps1.'}
                  </span>
                </span>
              </label>
            </div>

            <div className="rounded-lg border border-border bg-muted/30 p-3 space-y-1">
              <p><span className="text-muted-foreground">Client:</span> {clients.find((c) => c.id === selectedClientId)?.name ?? '—'}</p>
              {packageKind === 'combined' && counterpartServer ? (
                <>
                  <p><span className="text-muted-foreground">Database:</span> {freshDatabase ? 'Fresh (baseline restore)' : 'Existing'}, then DbMigrator</p>
                  <p><span className="text-muted-foreground">API:</span> {(selectedServer?.serverType === 'Api' ? selectedServer : counterpartServer).serverName}</p>
                  <p><span className="text-muted-foreground">Frontend:</span> {(selectedServer?.serverType === 'Frontend' ? selectedServer : counterpartServer).serverName}</p>
                </>
              ) : (
                <>
                  <p><span className="text-muted-foreground">Server:</span> {selectedServer?.serverName ?? '—'}</p>
                  <p><span className="text-muted-foreground">IIS Site:</span> {selectedServer?.websiteName ?? '—'}</p>
                  <p><span className="text-muted-foreground">App Pool:</span> {selectedServer?.appPoolName ?? '—'}</p>
                  <p><span className="text-muted-foreground">Physical Path:</span> {selectedServer?.deploymentPath ?? '—'}</p>
                </>
              )}
            </div>

            {packageKind === 'combined' && counterpartServers.length > 1 && (
              <div className="space-y-1.5">
                <Label htmlFor="counterpartServer">Other server</Label>
                <Select
                  id="counterpartServer"
                  value={String(counterpartServerId)}
                  onChange={(e) => setCounterpartServerId(Number(e.target.value))}
                  disabled={downloadingPackage}
                >
                  {counterpartServers.map((server) => (
                    <option key={server.id} value={server.id}>
                      [{server.serverType}] {server.serverName} ({server.hostName})
                    </option>
                  ))}
                </Select>
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="packageHost">
                {packageKind === 'combined'
                  ? `${selectedServer?.serverType === 'Frontend' ? 'Frontend' : 'API'} host name *`
                  : 'HTTPS host name *'}
              </Label>
              <Input
                id="packageHost"
                value={siteHostName}
                onChange={(e) => setSiteHostName(e.target.value)}
                placeholder={selectedServer?.serverType === 'Frontend' ? 'app.client.com' : 'api.client.com'}
                disabled={downloadingPackage}
              />
            </div>

            {packageKind === 'combined' && (
              <div className="space-y-1.5">
                <Label htmlFor="counterpartHost">
                  {selectedServer?.serverType === 'Frontend' ? 'API host name *' : 'Frontend host name *'}
                </Label>
                <Input
                  id="counterpartHost"
                  value={counterpartSiteHostName}
                  onChange={(e) => setCounterpartSiteHostName(e.target.value)}
                  placeholder={selectedServer?.serverType === 'Frontend' ? 'api.client.com' : 'app.client.com'}
                  disabled={downloadingPackage}
                />
              </div>
            )}

            {(packageKind === 'combined' || selectedServer?.serverType === 'Api') && (
              <div className="space-y-2">
                <Label>Database</Label>
                <label className="flex items-start gap-2">
                  <input
                    type="radio"
                    className="mt-1 accent-primary"
                    checked={!freshDatabase}
                    onChange={() => setFreshDatabase(false)}
                    disabled={downloadingPackage}
                  />
                  <span>
                    <span className="font-medium">Existing Database</span>
                    <span className="block text-xs text-muted-foreground">Do not restore the baseline backup. DbMigrator runs against the database that is already there.</span>
                  </span>
                </label>
                <label className="flex items-start gap-2">
                  <input
                    type="radio"
                    className="mt-1 accent-primary"
                    checked={freshDatabase}
                    onChange={() => setFreshDatabase(true)}
                    disabled={downloadingPackage}
                  />
                  <span>
                    <span className="font-medium">Fresh Database</span>
                    <span className="block text-xs text-muted-foreground">Install SQL Express if needed and restore the baseline backup. A database that already exists is never overwritten: the restore is skipped and DbMigrator updates it (an empty one stops the deploy).</span>
                  </span>
                </label>
              </div>
            )}

            <div className="space-y-2">
              <Label>Certificate</Label>
              <label className="flex items-start gap-2">
                <input
                  type="radio"
                  className="mt-1 accent-primary"
                  checked={certificateMode === 'Default'}
                  onChange={() => { setCertificateMode('Default'); setCertificateFile(null); }}
                  disabled={downloadingPackage}
                />
                <span>
                  <span className="font-medium">Use existing / default certificate</span>
                  <span className="block text-xs text-muted-foreground">
                    {packageKind === 'combined'
                      ? 'WMSVC-SHA2 or another cert already in LocalMachine\\My, used for both sites.'
                      : 'WMSVC-SHA2 or another cert already in LocalMachine\\My.'}
                  </span>
                </span>
              </label>
              <label className="flex items-start gap-2">
                <input
                  type="radio"
                  className="mt-1 accent-primary"
                  checked={certificateMode === 'UserProvided'}
                  onChange={() => setCertificateMode('UserProvided')}
                  disabled={downloadingPackage}
                />
                <span>
                  <span className="font-medium">Use my own certificate</span>
                  <span className="block text-xs text-muted-foreground">
                    {packageKind === 'combined'
                      ? 'One .pfx is packed in and used for both sites. The password is entered on the server.'
                      : 'The .pfx is packed in. The password is entered on the server, not stored here.'}
                  </span>
                </span>
              </label>
              {certificateMode === 'UserProvided' && (
                <input
                  type="file"
                  accept=".pfx,.p12"
                  disabled={downloadingPackage}
                  onChange={(e) => setCertificateFile(e.target.files?.[0] ?? null)}
                  className="block w-full text-xs"
                />
              )}
            </div>
          </div>}

                  <DialogFooter className="mt-auto pt-4 border-t border-border">
            <Button type="button" variant="outline" onClick={() => setPackageDialogOpen(false)} disabled={downloadingPackage}>
              Cancel
            </Button>
            <Button type="button" onClick={handleDownloadPackage} disabled={downloadingPackage || (packageKind === 'combined' && !counterpartServerId)}>
              {downloadingPackage ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Building package...
                </>
              ) : packageKind === 'combined' ? (
                'Generate combined package'
              ) : (
                'Generate package'
              )}
            </Button>
                  </DialogFooter>
                </div>
            </div>

            {/* Right side - Deployment Steps */}
            <DeploymentStepsPanel
              steps={getDeploymentSteps(selectedServer?.serverType as 'Api' | 'Frontend')}
              currentStepId={downloadingPackage ? 'building' : 'extract'}
              isBuilding={downloadingPackage}
              buildError={packageBuildError}
            />
          </div>
        </DialogContent>
      </Dialog>
    </ProtectedLayout>
  );
}
