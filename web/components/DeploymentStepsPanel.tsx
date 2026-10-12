'use client';

import { Copy, CheckCircle2, Loader2, Circle, AlertTriangle, MinusCircle, XCircle } from 'lucide-react';
import type { DeploymentProgress, DeploymentStageState } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { toast } from 'sonner';
import { useState, useEffect } from 'react';

export interface DeploymentStep {
  id: string;
  name: string;
  status: 'pending' | 'running' | 'completed' | 'error';
  estimatedMinutes: number;
  folder: string;
  command: string;
  instructions?: string;
}

interface DeploymentStepsPanelProps {
  steps: DeploymentStep[];
  currentStepId?: string;
  isBuilding?: boolean;
  buildError?: string;
  /** Stages the server reports while it builds the package */
  packageProgress?: DeploymentProgress | null;
  /** Bytes of the finished zip received by the browser */
  packageTransfer?: { loaded: number; total?: number } | null;
}

const formatBytes = (bytes: number) =>
  bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(2)} GB`
  : bytes >= 1024 ** 2 ? `${(bytes / 1024 ** 2).toFixed(1)} MB`
  : `${Math.max(1, Math.round(bytes / 1024))} KB`;

const formatDuration = (seconds: number) =>
  `${String(Math.floor(seconds / 60)).padStart(2, '0')}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

function StageIcon({ state }: { state: DeploymentStageState }) {
  switch (state) {
    case 'Running': return <Loader2 className="h-5 w-5 animate-spin text-brand-blue" />;
    case 'Succeeded': return <CheckCircle2 className="h-5 w-5 text-emerald-600" />;
    case 'Warning': return <AlertTriangle className="h-5 w-5 text-amber-600" />;
    case 'Skipped': return <MinusCircle className="h-5 w-5 text-muted-foreground" />;
    case 'Failed': return <XCircle className="h-5 w-5 text-red-600" />;
    default: return <Circle className="h-5 w-5 text-muted-foreground/60" />;
  }
}

/** Real build progress: the server's stages, then the zip arriving in the browser. */
function BuildingProgress({ progress, transfer }: { progress?: DeploymentProgress | null; transfer?: { loaded: number; total?: number } | null }) {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  // Ticks once a second, so a running stage's timer moves between polls
  const [now, setNow] = useState(() => new Date().getTime());

  useEffect(() => {
    const interval = setInterval(() => {
      setElapsedSeconds((s) => s + 1);
      setNow(new Date().getTime());
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  // The last stage is the browser's own: it starts once the server has the zip ready
  const serverDone = Boolean(progress?.isCompleted && progress.succeeded);
  const sendState: DeploymentStageState = transfer && transfer.total && transfer.loaded >= transfer.total
    ? 'Succeeded'
    : serverDone || (transfer && transfer.loaded > 0) ? 'Running' : 'Pending';
  const sendDetail = transfer && transfer.loaded > 0
    ? transfer.total
      ? `${formatBytes(transfer.loaded)} of ${formatBytes(transfer.total)} (${Math.floor((transfer.loaded * 100) / transfer.total)}%)`
      : `${formatBytes(transfer.loaded)} received`
    : serverDone ? 'Starting the download…' : null;

  const stages: { key: string; label: string; state: DeploymentStageState; detail?: string | null; startedAt?: string | null; completedAt?: string | null }[] = [
    ...(progress?.stages ?? []),
    { key: 'send', label: 'Send the package to your browser', state: sendState, detail: sendDetail },
  ];

  return (
    <div className="w-96 bg-slate-50 border-l border-border overflow-y-auto flex flex-col">
      <div className="p-6 space-y-5 flex-1">
        <div>
          <h3 className="mb-1 text-[18px] font-normal text-ink">Building Package</h3>
          <p className="text-xs text-muted-foreground">
            {progress ? 'Live progress from the server' : 'Starting… the stages appear as soon as the server begins'}
          </p>
        </div>

        <div className="space-y-2.5">
          {stages.map((stage) => {
            const seconds = stage.startedAt
              ? Math.max(0, ((stage.completedAt ? new Date(stage.completedAt).getTime() : now) - new Date(stage.startedAt).getTime()) / 1000)
              : null;
            return (
              <div
                key={stage.key}
                className={`rounded-[8px] border p-3 transition-colors ${
                  stage.state === 'Running'
                    ? 'border-brand-blue/50 bg-accent ring-1 ring-brand-blue/20'
                    : stage.state === 'Succeeded'
                      ? 'border-emerald-200 bg-emerald-50'
                      : stage.state === 'Failed'
                        ? 'border-red-200 bg-red-50'
                        : 'border-border bg-white'
                }`}
              >
                <div className="flex items-start gap-3">
                  <div className="mt-0.5 flex-shrink-0"><StageIcon state={stage.state} /></div>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-2">
                      <p className={`text-[13px] ${stage.state === 'Pending' || stage.state === 'Skipped' ? 'text-muted-foreground' : 'text-ink'}`}>
                        {stage.label}
                      </p>
                      {seconds !== null && seconds >= 1 && (
                        <span className="shrink-0 font-mono text-[11px] text-muted-foreground">{formatDuration(seconds)}</span>
                      )}
                    </div>
                    {stage.detail && <p className="mt-0.5 break-words text-[12px] text-label">{stage.detail}</p>}
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <div className="rounded-[8px] bg-slate-100 p-4">
          <p className="mb-1 text-xs text-muted-foreground">Elapsed time</p>
          <p className="font-mono text-2xl">{formatDuration(elapsedSeconds)}</p>
        </div>
      </div>
    </div>
  );
}

function BuildingError({ error }: { error: string }) {
  return (
    <div className="w-96 bg-slate-50 border-l border-border overflow-y-auto flex flex-col">
      <div className="p-6 space-y-6 flex-1">
        <div>
          <h3 className="mb-1 flex items-center gap-2 text-[18px] font-normal text-ink">
            <AlertTriangle className="h-5 w-5 text-red-600" />
            Build Failed
          </h3>
          <p className="text-xs text-muted-foreground">Unable to build package</p>
        </div>

        <div className="rounded-lg border border-red-200 bg-red-50 p-4 space-y-3">
          <p className="font-medium text-red-900 text-sm">Error:</p>
          <p className="text-xs text-red-800 font-mono break-all">{error}</p>
          <p className="text-xs text-red-700 mt-3">
            <strong>Solution:</strong> Click &quot;Generate package&quot; again to retry, or check your Azure DevOps connection.
          </p>
        </div>
      </div>
    </div>
  );
}

export function DeploymentStepsPanel({ steps, currentStepId, isBuilding, buildError, packageProgress, packageTransfer }: DeploymentStepsPanelProps) {
  const copyCommand = (command: string) => {
    navigator.clipboard.writeText(command);
    toast.success('Command copied to clipboard');
  };

  // Show building progress while building
  if (isBuilding) {
    return <BuildingProgress progress={packageProgress} transfer={packageTransfer} />;
  }

  // Show error if build failed
  if (buildError) {
    return <BuildingError error={buildError} />;
  }

  // Show deployment steps after build completes
  return (
    <div className="w-96 bg-slate-50 border-l border-border overflow-y-auto flex flex-col">
      <div className="p-6 space-y-6 flex-1">
        <div>
          <h3 className="mb-1 text-[18px] font-normal text-ink">Deployment Steps</h3>
          <p className="text-xs text-muted-foreground">Follow the steps to deploy your application</p>
        </div>

        <div className="space-y-4">
          {steps.map((step, index) => {
            const isCurrent = step.id === currentStepId;
            const isCompleted = step.status === 'completed';
            const isError = step.status === 'error';

            return (
              <div
                key={step.id}
                className={`rounded-lg border p-4 transition-colors ${
                  isCurrent
                    ? 'border-brand-blue/50 bg-accent ring-1 ring-brand-blue/20'
                    : isCompleted
                      ? 'border-emerald-200 bg-emerald-50'
                      : isError
                        ? 'border-red-200 bg-red-50'
                        : 'border-border bg-white'
                }`}
              >
                <div className="flex items-start gap-3">
                  <div className="mt-1">
                    {step.status === 'running' && <Loader2 className="h-5 w-5 animate-spin text-primary" />}
                    {step.status === 'completed' && <CheckCircle2 className="h-5 w-5 text-emerald-600" />}
                    {step.status === 'error' && <CheckCircle2 className="h-5 w-5 text-red-600" />}
                    {step.status === 'pending' && <Circle className="h-5 w-5 text-muted-foreground" />}
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between gap-2">
                      <p className={`font-medium text-sm ${isCurrent ? 'text-primary' : ''}`}>
                        {index + 1}. {step.name}
                      </p>
                      <span className="text-xs text-muted-foreground whitespace-nowrap">
                        {step.estimatedMinutes} min
                      </span>
                    </div>

                    {(isCurrent || isCompleted) && (
                      <div className="mt-3 space-y-3 text-xs">
                        {/* Folder path */}
                        <div>
                          <p className="text-muted-foreground mb-1">Work in folder:</p>
                          <div className="bg-white p-2 rounded border border-border font-mono text-[11px] break-all">
                            📁 {step.folder}
                          </div>
                        </div>

                        {/* Command */}
                        <div>
                          <p className="text-muted-foreground mb-1">Run command:</p>
                          <div className="flex gap-2">
                            <div className="flex-1 bg-slate-900 text-slate-100 p-2 rounded border border-border font-mono text-[11px] overflow-x-auto">
                              {step.command}
                            </div>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => copyCommand(step.command)}
                              className="shrink-0"
                            >
                              <Copy className="h-3 w-3" />
                            </Button>
                          </div>
                        </div>

                        {/* Instructions */}
                        {step.instructions && (
                          <div>
                            <p className="text-muted-foreground mb-1">Next step:</p>
                            <p className="text-muted-foreground leading-relaxed">{step.instructions}</p>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <div className="pt-4 border-t border-border">
          <p className="text-xs text-muted-foreground text-center">
            Total estimated time: <span className="font-semibold">{steps.reduce((sum, s) => sum + s.estimatedMinutes, 0)} min</span>
          </p>
        </div>
      </div>
    </div>
  );
}
