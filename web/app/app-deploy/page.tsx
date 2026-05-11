'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import api, { ApplicationServer, ApiDeploymentResult } from '@/lib/api';
import { Upload, Server, CheckCircle2, XCircle, Loader2, Clock, ChevronDown, ChevronRight, FolderArchive } from 'lucide-react';

export default function AppDeployPage() {
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [version, setVersion] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [zipFile, setZipFile] = useState<File | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [deploymentResult, setDeploymentResult] = useState<ApiDeploymentResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [showStructureGuide, setShowStructureGuide] = useState(false);

  useEffect(() => {
    loadServers();
  }, []);

  const loadServers = async () => {
    try {
      const data = await api.getApplicationServers();
      setServers(data.filter(s => s.isActive));
    } catch (error: any) {
      console.error('Error loading servers:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    } finally {
      setLoading(false);
    }
  };

  const selectedServer = servers.find(s => s.id === selectedServerId);
  const isFrontend = selectedServer?.serverType === 'Frontend';

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

    if (isFrontend) {
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

    try {
      let result: ApiDeploymentResult;
      if (isFrontend) {
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

  if (loading) {
    return (
      <ProtectedLayout>
        <div className="flex items-center justify-center min-h-screen">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto"></div>
            <p className="mt-4 text-muted-foreground">Loading...</p>
          </div>
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout>
      <div className="space-y-6 max-w-4xl mx-auto">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Deploy App</h1>
          <p className="text-muted-foreground">
            Deploy API files or frontend builds to Windows IIS servers
          </p>
        </div>

        {/* Deployment Form */}
        <form onSubmit={handleDeploy}>
          <Card>
            <CardHeader>
              <CardTitle>Deployment Configuration</CardTitle>
              <CardDescription>
                Select target server — the upload form adapts to the server type
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-6">
              {/* Server Selection */}
              <div>
                <Label htmlFor="server">Target Server *</Label>
                <select
                  id="server"
                  className="w-full border rounded-md p-2 mt-1"
                  value={selectedServerId}
                  onChange={(e) => {
                    setSelectedServerId(Number(e.target.value));
                    setFiles([]);
                    setZipFile(null);
                    setDeploymentResult(null);
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
                </select>
              </div>

              {/* Server Info */}
              {selectedServer && (
                <div className="bg-muted/50 rounded-lg p-4 space-y-2 text-sm">
                  <div className="flex items-center gap-2 font-semibold">
                    <Server className="h-4 w-4" />
                    Server Details
                    <Badge variant={isFrontend ? 'outline' : 'secondary'}
                      className={isFrontend ? 'border-blue-500 text-blue-600 ml-2' : 'ml-2'}>
                      {selectedServer.serverType}
                    </Badge>
                  </div>
                  <div className="grid grid-cols-2 gap-x-4 gap-y-2 mt-2">
                    <div>
                      <span className="text-muted-foreground">Website:</span>
                      <span className="ml-2 font-mono text-xs">{selectedServer.websiteName}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">App Pool:</span>
                      <span className="ml-2 font-mono text-xs">{selectedServer.appPoolName}</span>
                    </div>
                    <div className="col-span-2">
                      <span className="text-muted-foreground">Path:</span>
                      <span className="ml-2 font-mono text-xs">{selectedServer.deploymentPath}</span>
                    </div>
                  </div>
                </div>
              )}

              {/* Version */}
              <div>
                <Label htmlFor="version">Version (Optional)</Label>
                <Input
                  id="version"
                  value={version}
                  onChange={(e) => setVersion(e.target.value)}
                  placeholder="1.0.0"
                  disabled={deploying}
                />
              </div>

              {/* File Upload — API mode */}
              {!isFrontend && selectedServer && (
                <div>
                  <Label htmlFor="files">Files to Deploy *</Label>
                  <Input
                    id="files"
                    type="file"
                    onChange={handleFileChange}
                    multiple
                    accept=".dll,.exe,.json,.pdb,.config,.xml"
                    disabled={deploying}
                    className="mt-1"
                  />
                  <p className="text-xs text-muted-foreground mt-2">
                    Select DLL, EXE, JSON, PDB, and config files
                  </p>
                  {files.length > 0 && (
                    <div className="mt-3 space-y-2">
                      <Label>Selected Files ({files.length})</Label>
                      <div className="bg-muted/50 rounded-lg p-3 max-h-48 overflow-y-auto">
                        {files.map((file, index) => (
                          <div key={index} className="flex justify-between items-center py-1 text-sm">
                            <span className="font-mono text-xs truncate flex-1">{file.name}</span>
                            <span className="text-muted-foreground text-xs ml-2">{formatFileSize(file.size)}</span>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* File Upload — Frontend mode */}
              {isFrontend && selectedServer && (
                <div className="space-y-3">
                  {/* Structure Guide */}
                  <div className="border rounded-lg overflow-hidden">
                    <button
                      type="button"
                      className="w-full flex items-center justify-between p-3 text-sm font-medium bg-muted/30 hover:bg-muted/50 transition-colors"
                      onClick={() => setShowStructureGuide(!showStructureGuide)}
                    >
                      <div className="flex items-center gap-2">
                        <FolderArchive className="h-4 w-4 text-blue-500" />
                        <span>Expected ZIP structure</span>
                      </div>
                      {showStructureGuide ? (
                        <ChevronDown className="h-4 w-4" />
                      ) : (
                        <ChevronRight className="h-4 w-4" />
                      )}
                    </button>
                    {showStructureGuide && (
                      <div className="p-4 bg-muted/20 border-t text-xs font-mono space-y-1 text-muted-foreground">
                        <div>frontend.zip</div>
                        <div className="ml-4">├── index.html</div>
                        <div className="ml-4">├── static/</div>
                        <div className="ml-8">├── css/</div>
                        <div className="ml-8">└── js/</div>
                        <div className="ml-4">└── (other build output files)</div>
                        <div className="mt-3 text-yellow-700 bg-yellow-50 rounded p-2 not-italic font-sans">
                          Do NOT include <strong>web.config</strong> in the ZIP — it will be preserved on the server.
                        </div>
                      </div>
                    )}
                  </div>

                  <div>
                    <Label htmlFor="zipFile">Frontend ZIP File *</Label>
                    <Input
                      id="zipFile"
                      type="file"
                      accept=".zip"
                      onChange={handleZipChange}
                      disabled={deploying}
                      className="mt-1"
                    />
                    <p className="text-xs text-muted-foreground mt-2">
                      Only .zip files accepted. The ZIP will be extracted directly into the deployment path.
                    </p>
                    {zipFile && (
                      <div className="mt-2 bg-muted/50 rounded-lg p-3 flex justify-between items-center text-sm">
                        <span className="font-mono text-xs truncate">{zipFile.name}</span>
                        <span className="text-muted-foreground text-xs ml-2">{formatFileSize(zipFile.size)}</span>
                      </div>
                    )}
                  </div>
                </div>
              )}

              {/* Deploy Button */}
              {selectedServer && (
                <div className="pt-4">
                  <Button
                    type="submit"
                    className="w-full"
                    disabled={
                      deploying ||
                      !selectedServerId ||
                      (isFrontend ? !zipFile : files.length === 0)
                    }
                  >
                    {deploying ? (
                      <>
                        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                        Deploying...
                      </>
                    ) : (
                      <>
                        <Upload className="mr-2 h-4 w-4" />
                        {isFrontend ? 'Deploy Frontend' : 'Deploy API'}
                      </>
                    )}
                  </Button>
                </div>
              )}
            </CardContent>
          </Card>
        </form>

        {/* Deployment Result */}
        {deploymentResult && (
          <Card className={deploymentResult.status === 'Success' ? 'ring-2 ring-green-500/30' : 'ring-2 ring-red-500/30'}>
            <CardHeader>
              <div className="flex items-center gap-3">
                {deploymentResult.status === 'Success' ? (
                  <CheckCircle2 className="h-6 w-6 text-green-600" />
                ) : (
                  <XCircle className="h-6 w-6 text-red-600" />
                )}
                <div className="flex-1">
                  <CardTitle>
                    {deploymentResult.status === 'Success' ? 'Deployment Successful' : 'Deployment Failed'}
                  </CardTitle>
                  <CardDescription>{deploymentResult.message}</CardDescription>
                </div>
                <Badge variant={deploymentResult.status === 'Success' ? 'default' : 'destructive'}>
                  {deploymentResult.status}
                </Badge>
              </div>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex items-center gap-2 text-sm">
                <Clock className="h-4 w-4 text-muted-foreground" />
                <span>Duration: {deploymentResult.deploymentDurationSeconds}s</span>
              </div>

              {deploymentResult.deploymentSteps.length > 0 && (
                <div>
                  <Label className="text-sm">Deployment Steps:</Label>
                  <div className="mt-2 bg-muted/50 rounded-lg p-3 space-y-1">
                    {deploymentResult.deploymentSteps.map((step, index) => (
                      <div key={index} className="text-xs font-mono">{step}</div>
                    ))}
                  </div>
                </div>
              )}

              {deploymentResult.errorMessage && (
                <div className="bg-red-50 ring-1 ring-red-200 rounded-lg p-3">
                  <Label className="text-sm text-red-900">Error:</Label>
                  <p className="text-xs text-red-700 mt-1 font-mono">{deploymentResult.errorMessage}</p>
                </div>
              )}

              {deploymentResult.backupFileName && (
                <div className="bg-blue-50 ring-1 ring-blue-200 rounded-lg p-3">
                  <Label className="text-sm text-blue-900">Backup Created:</Label>
                  <p className="text-xs text-blue-700 mt-1 font-mono">{deploymentResult.backupFileName}</p>
                </div>
              )}

              {deploymentResult.deployedFiles.length > 0 && (
                <div>
                  <Label className="text-sm">Deployed Files ({deploymentResult.deployedFiles.length}):</Label>
                  <div className="mt-2 bg-muted/50 rounded-lg p-3 max-h-32 overflow-y-auto">
                    {deploymentResult.deployedFiles.map((file) => (
                      <div key={file.id} className="flex justify-between items-center py-1 text-sm">
                        <span className="font-mono text-xs">{file.fileName}</span>
                        <span className="text-muted-foreground text-xs">{formatFileSize(file.fileSize)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </CardContent>
          </Card>
        )}

        {servers.length === 0 && (
          <Card>
            <CardContent className="flex flex-col items-center justify-center py-16">
              <Server className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold mb-2">No servers configured</h3>
              <p className="text-muted-foreground mb-4">Add a server first to start deploying</p>
              <Button onClick={() => window.location.href = '/servers'}>
                Go to Servers
              </Button>
            </CardContent>
          </Card>
        )}
      </div>
    </ProtectedLayout>
  );
}
