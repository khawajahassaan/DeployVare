'use client';

import { Copy, CheckCircle2, Loader2, Circle, AlertTriangle } from 'lucide-react';
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
  buildStep?: 'frontend' | 'backend' | 'combining';
}

interface BuildingProgressProps {
  currentStep?: 'frontend' | 'backend' | 'combining';
}

function BuildingProgress({ currentStep = 'frontend' }: BuildingProgressProps) {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  useEffect(() => {
    const interval = setInterval(() => {
      setElapsedSeconds(s => s + 1);
    }, 1000);
    return () => clearInterval(interval);
  }, []);

  const minutes = Math.floor(elapsedSeconds / 60);
  const seconds = elapsedSeconds % 60;

  const steps = [
    { id: 'frontend', label: 'Download Frontend', time: '2-5 min' },
    { id: 'backend', label: 'Download Backend', time: '5-10 min' },
    { id: 'combining', label: 'Combining Package', time: '1-2 min' },
  ];

  return (
    <div className="w-96 bg-slate-50 border-l border-border overflow-y-auto flex flex-col">
      <div className="p-6 space-y-6 flex-1">
        <div>
          <h3 className="font-semibold text-lg mb-1">Building Package</h3>
          <p className="text-xs text-muted-foreground">Preparing your deployment package</p>
        </div>

        <div className="space-y-4">
          {steps.map((step) => {
            const isActive = currentStep === step.id;
            const isCompleted = ['frontend', 'backend'].indexOf(currentStep) > ['frontend', 'backend'].indexOf(step.id);

            return (
              <div
                key={step.id}
                className={`rounded-lg border p-4 transition-colors ${
                  isActive
                    ? 'border-primary/50 bg-primary/5 ring-1 ring-primary/20'
                    : isCompleted
                      ? 'border-emerald-200 bg-emerald-50'
                      : 'border-border bg-white'
                }`}
              >
                <div className="flex items-center gap-3">
                  <div className="flex-shrink-0">
                    {isActive && <Loader2 className="h-5 w-5 animate-spin text-primary" />}
                    {isCompleted && <CheckCircle2 className="h-5 w-5 text-emerald-600" />}
                    {!isActive && !isCompleted && <Circle className="h-5 w-5 text-muted-foreground" />}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className={`font-medium text-sm ${isActive ? 'text-primary' : isCompleted ? 'text-emerald-700' : 'text-muted-foreground'}`}>
                      {step.label}
                    </p>
                    <p className="text-xs text-muted-foreground">{step.time}</p>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        <div className="bg-slate-100 rounded-lg p-4">
          <p className="text-xs text-muted-foreground mb-2">Elapsed time:</p>
          <p className="font-mono text-2xl font-bold">
            {String(minutes).padStart(2, '0')}:{String(seconds).padStart(2, '0')}
          </p>
          <p className="text-xs text-muted-foreground mt-2">Estimated total: 8-17 minutes</p>
        </div>
      </div>

      <div className="p-4 border-t border-border bg-white">
        <p className="text-xs text-muted-foreground text-center">
          Downloading in steps to avoid timeouts
        </p>
      </div>
    </div>
  );
}

function BuildingError({ error }: { error: string }) {
  return (
    <div className="w-96 bg-slate-50 border-l border-border overflow-y-auto flex flex-col">
      <div className="p-6 space-y-6 flex-1">
        <div>
          <h3 className="font-semibold text-lg mb-1 flex items-center gap-2">
            <AlertTriangle className="h-5 w-5 text-red-600" />
            Build Failed
          </h3>
          <p className="text-xs text-muted-foreground">Unable to build package</p>
        </div>

        <div className="rounded-lg border border-red-200 bg-red-50 p-4 space-y-3">
          <p className="font-medium text-red-900 text-sm">Error:</p>
          <p className="text-xs text-red-800 font-mono break-all">{error}</p>
          <p className="text-xs text-red-700 mt-3">
            <strong>Solution:</strong> Click "Generate package" again to retry, or check your Azure DevOps connection.
          </p>
        </div>
      </div>
    </div>
  );
}

export function DeploymentStepsPanel({ steps, currentStepId, isBuilding, buildError, buildStep }: DeploymentStepsPanelProps) {
  const copyCommand = (command: string) => {
    navigator.clipboard.writeText(command);
    toast.success('Command copied to clipboard');
  };

  // Show building progress while building
  if (isBuilding) {
    return <BuildingProgress currentStep={buildStep} />;
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
          <h3 className="font-semibold text-lg mb-1">Deployment Steps</h3>
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
                    ? 'border-primary/50 bg-primary/5 ring-1 ring-primary/20'
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
