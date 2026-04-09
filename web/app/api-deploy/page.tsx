'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import api, { ApplicationServer, ApiDeploymentResult } from '@/lib/api';
import { Upload, Server, CheckCircle2, XCircle, Loader2, Clock } from 'lucide-react';

export default function ApiDeployPage() {
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [version, setVersion] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [deploying, setDeploying] = useState(false);
  const [deploymentResult, setDeploymentResult] = useState<ApiDeploymentResult | null>(null);
  const [loading, setLoading] = useState(true);

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

  const selectedServer = servers.find(s => s.id === selectedServerId);

  return (
    <ProtectedLayout>
      <div className="space-y-6 max-w-4xl mx-auto">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Deploy API</h1>
          <p className="text-muted-foreground">
            Deploy DLL files and other API assets to Windows servers
          </p>
        </div>

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
              <div>
                <Label htmlFor="server">Target Server *</Label>
                <select
                  id="server"
                  className="w-full border rounded-md p-2 mt-1"
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
                </select>
              </div>

              {/* Server Info */}
              {selectedServer && (
                <div className="bg-muted/50 rounded-lg p-4 space-y-2 text-sm">
                  <div className="flex items-center gap-2 font-semibold">
                    <Server className="h-4 w-4" />
                    Server Details
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

              {/* File Upload */}
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
              </div>

              {/* Selected Files */}
              {files.length > 0 && (
                <div className="space-y-2">
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

              {/* Deploy Button */}
              <div className="pt-4">
                <Button
                  type="submit"
                  className="w-full"
                  disabled={deploying || !selectedServerId || files.length === 0}
                >
                  {deploying ? (
                    <>
                      <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                      Deploying...
                    </>
                  ) : (
                    <>
                      <Upload className="mr-2 h-4 w-4" />
                      Deploy to Server
                    </>
                  )}
                </Button>
              </div>
            </CardContent>
          </Card>
        </form>

        {/* Deployment Result */}
        {deploymentResult && (
          <Card className={deploymentResult.status === 'Success' ? 'ring-2 ring-green-500/30 shadow-green-100' : 'ring-2 ring-red-500/30 shadow-red-100'}>
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
              {/* Duration */}
              <div className="flex items-center gap-2 text-sm">
                <Clock className="h-4 w-4 text-muted-foreground" />
                <span>Duration: {deploymentResult.deploymentDurationSeconds}s</span>
              </div>

              {/* Deployment Steps */}
              {deploymentResult.deploymentSteps.length > 0 && (
                <div>
                  <Label className="text-sm">Deployment Steps:</Label>
                  <div className="mt-2 bg-muted/50 rounded-lg p-3 space-y-1">
                    {deploymentResult.deploymentSteps.map((step, index) => (
                      <div key={index} className="text-sm font-mono text-xs">
                        {step}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Error Message */}
              {deploymentResult.errorMessage && (
                <div className="bg-red-50 ring-1 ring-red-200 rounded-lg p-3 shadow-sm">
                  <Label className="text-sm text-red-900">Error:</Label>
                  <p className="text-sm text-red-700 mt-1 font-mono text-xs">
                    {deploymentResult.errorMessage}
                  </p>
                </div>
              )}

              {/* Backup Info */}
              {deploymentResult.backupFileName && (
                <div className="bg-blue-50 ring-1 ring-blue-200 rounded-lg p-3 shadow-sm">
                  <Label className="text-sm text-blue-900">Backup Created:</Label>
                  <p className="text-sm text-blue-700 mt-1 font-mono text-xs">
                    {deploymentResult.backupFileName}
                  </p>
                </div>
              )}

              {/* Deployed Files */}
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


