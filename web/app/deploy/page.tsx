'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { PageHeader } from '@/components/ui/page-header';
import api, { DatabaseInstance } from '@/lib/api';
import { Upload, Rocket, FileArchive, CheckCircle, AlertCircle, Download, FolderTree } from 'lucide-react';
import { toast } from 'sonner';

export default function DeployPage() {
  const { selectedClientId } = useClientStore();
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [selectedInstanceId, setSelectedInstanceId] = useState<number>(0);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [deployResult, setDeployResult] = useState<any>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    if (selectedClientId) loadInstances();
  }, [selectedClientId]);

  const loadInstances = async () => {
    try {
      const data = await api.getDatabaseInstances(selectedClientId);
      setInstances(data);
      setSelectedInstanceId(0);
    } catch (error) {
      console.error('Failed to load instances:', error);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      if (!file.name.endsWith('.zip')) {
        setError('Please select a ZIP file');
        return;
      }
      setSelectedFile(file);
      setError('');
    }
  };

  const handleDeploy = async () => {
    if (!selectedInstanceId) {
      setError('Please select a database instance');
      return;
    }
    if (!selectedFile) {
      setError('Please select a ZIP file');
      return;
    }

    setError('');
    setDeploying(true);
    setDeployResult(null);

    try {
      const result = await api.deploy(selectedInstanceId, selectedFile);
      setDeployResult(result);
      setSelectedFile(null);

      // Reset file input
      const fileInput = document.getElementById('file-upload') as HTMLInputElement;
      if (fileInput) fileInput.value = '';
    } catch (err: any) {
      setError(err.response?.data?.message || 'Deployment failed');
      setDeployResult(err.response?.data || null);
    } finally {
      setDeploying(false);
    }
  };

  const handleDownloadBackup = async () => {
    if (!deployResult?.deploymentId) return;

    try {
      const blob = await api.downloadBackup(deployResult.deploymentId);
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `backup-${deployResult.deploymentId}.zip`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      toast.success('Backup downloaded successfully');
    } catch (error) {
      toast.error('Failed to download backup');
    }
  };

  const selectedInstance = instances.find((i) => i.id === selectedInstanceId);

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Deploy SQL Scripts"
          description="Upload and deploy SQL patches, procedures, and functions"
          icon={Rocket}
        />

        <div className="grid gap-6 lg:grid-cols-3">
          <div className="space-y-6 lg:col-span-2">
            {/* Selection */}
            <Card>
              <CardHeader>
                <CardTitle>1. Select Database Instance</CardTitle>
                <CardDescription>Choose the target database for deployment</CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="instance">Database Instance</Label>
                  <Select
                    id="instance"
                    value={selectedInstanceId}
                    onChange={(e) => setSelectedInstanceId(parseInt(e.target.value))}
                    disabled={deploying}
                  >
                    <option value="0">Select an instance</option>
                    {instances.map((instance) => (
                      <option key={instance.id} value={instance.id}>
                        {instance.applicationName} - {instance.instanceName} (Patch #{instance.currentPatchNumber})
                      </option>
                    ))}
                  </Select>
                </div>

                {selectedInstance && (
                  <div className="rounded-lg border border-border bg-muted/50 p-4">
                    <div className="grid grid-cols-2 gap-4 text-sm">
                      <div>
                        <span className="text-muted-foreground">Application:</span>
                        <p className="font-medium">{selectedInstance.applicationName}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Instance:</span>
                        <p className="font-medium">{selectedInstance.instanceName}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Current Patch:</span>
                        <p className="font-medium">#{selectedInstance.currentPatchNumber}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Status:</span>
                        <div>
                          <Badge variant={selectedInstance.isActive ? 'success' : 'destructive'}>
                            {selectedInstance.isActive ? 'Active' : 'Inactive'}
                          </Badge>
                        </div>
                      </div>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>

            {/* File Upload */}
            <Card>
              <CardHeader>
                <CardTitle>2. Upload SQL Scripts</CardTitle>
                <CardDescription>
                  Upload a ZIP file containing SQLPatch, StoreProcedure, and SQLFunction folders
                </CardDescription>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="file-upload">ZIP File</Label>
                  <input
                    id="file-upload"
                    type="file"
                    accept=".zip"
                    onChange={handleFileChange}
                    disabled={deploying}
                    className="block w-full text-sm text-muted-foreground file:mr-4 file:rounded-[8px] file:border-0 file:bg-accent file:px-4 file:py-2 file:text-sm file:font-normal file:text-brand-blue hover:file:bg-[#D6E6F4] disabled:opacity-50"
                  />
                </div>

                {selectedFile && (
                  <div className="flex items-center gap-3 rounded-lg border border-border bg-muted/50 p-3">
                    <FileArchive className="h-5 w-5 text-primary" />
                    <div className="flex-1">
                      <p className="font-medium text-sm">{selectedFile.name}</p>
                      <p className="text-xs text-muted-foreground">
                        {(selectedFile.size / 1024 / 1024).toFixed(2)} MB
                      </p>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>

            {/* Deploy Button */}
            {error && (
              <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                <AlertCircle className="h-4 w-4 flex-shrink-0 mt-0.5" />
                <span>{error}</span>
              </div>
            )}

            <Card>
              <CardContent className="pt-6">
                <Button
                  onClick={handleDeploy}
                  disabled={!selectedInstanceId || !selectedFile || deploying}
                  size="lg"
                  className="w-full"
                >
                  {deploying ? (
                    <>
                      <Upload className="h-5 w-5 animate-pulse" />
                      Deploying...
                    </>
                  ) : (
                    <>
                      <Rocket className="h-5 w-5" />
                      Deploy Now
                    </>
                  )}
                </Button>
              </CardContent>
            </Card>

            {/* Deployment Result */}
            {deployResult && (
              <Card className={deployResult.success ? 'border-green-200 ring-1 ring-green-500/20' : 'border-red-200 ring-1 ring-red-500/20'}>
                <CardHeader>
                  <div className="flex items-center gap-2">
                    {deployResult.success ? (
                      <CheckCircle className="h-5 w-5 text-green-600" />
                    ) : (
                      <AlertCircle className="h-5 w-5 text-destructive" />
                    )}
                    <CardTitle>
                      {deployResult.success ? 'Deployment Successful!' : 'Deployment Failed'}
                    </CardTitle>
                  </div>
                </CardHeader>
                <CardContent className="space-y-4">
                  {deployResult.message && (
                    <p className="text-sm">{deployResult.message}</p>
                  )}

                  {deployResult.patchesApplied !== undefined && (
                    <div className="grid grid-cols-2 gap-4 text-sm">
                      <div>
                        <span className="text-muted-foreground">Patches Applied:</span>
                        <p className="font-medium">{deployResult.patchesApplied}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Objects Deployed:</span>
                        <p className="font-medium">{deployResult.objectsDeployed || 0}</p>
                      </div>
                      {deployResult.fromPatch !== undefined && (
                        <>
                          <div>
                            <span className="text-muted-foreground">From Patch:</span>
                            <p className="font-medium">#{deployResult.fromPatch}</p>
                          </div>
                          <div>
                            <span className="text-muted-foreground">To Patch:</span>
                            <p className="font-medium">#{deployResult.toPatch}</p>
                          </div>
                        </>
                      )}
                    </div>
                  )}

                  {deployResult.backupCreated && deployResult.deploymentId && (
                    <Button
                      onClick={handleDownloadBackup}
                      variant="outline"
                      className="w-full"
                    >
                      <Download className="h-4 w-4" />
                      Download Backup
                    </Button>
                  )}

                  {deployResult.errors && deployResult.errors.length > 0 && (
                    <div className="space-y-2">
                      <p className="text-sm font-medium">Errors:</p>
                      <div className="rounded-lg border border-red-200 bg-red-50 p-3">
                        {deployResult.errors.map((err: string, idx: number) => (
                          <p key={idx} className="text-sm text-red-700">
                            • {err}
                          </p>
                        ))}
                      </div>
                    </div>
                  )}
                </CardContent>
              </Card>
            )}
          </div>

          {/* Help */}
          <Card className="self-start lg:sticky lg:top-24">
            <CardHeader>
              <div className="flex items-center gap-2">
                <FolderTree className="h-4 w-4 text-primary" />
                <CardTitle>Expected ZIP Structure</CardTitle>
              </div>
              <CardDescription>
                Patches run in numeric order; procedures and functions are deployed after patches.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto rounded-lg border border-blue-200 bg-blue-50/60 p-4">
                <pre className="text-xs text-muted-foreground font-mono">
{`UploadedFile.zip
├── SQLPatch/
│   ├── 106_Add_Column_UserName.sql
│   ├── 107_Create_Table_Audit.sql
├── StoreProcedure/
│   ├── usp_GetExpenses.sql
└── SQLFunction/
    ├── fn_CalculateTotal.sql`}
                </pre>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </ProtectedLayout>
  );
}
