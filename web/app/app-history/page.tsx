'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import api, { ApplicationServer, ApiDeployment } from '@/lib/api';
import { History, Server, Clock, User, FileCode, Download, ChevronDown, ChevronRight } from 'lucide-react';
import { toast } from 'sonner';

export default function AppHistoryPage() {
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [deployments, setDeployments] = useState<ApiDeployment[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedDeployments, setExpandedDeployments] = useState<Set<number>>(new Set());

  useEffect(() => {
    loadServers();
  }, []);

  useEffect(() => {
    if (selectedServerId) {
      loadDeployments();
    }
  }, [selectedServerId]);

  const loadServers = async () => {
    try {
      const data = await api.getApplicationServers();
      setServers(data);
      if (data.length > 0) {
        setSelectedServerId(data[0].id);
      }
    } catch (error: any) {
      console.error('Error loading servers:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    } finally {
      setLoading(false);
    }
  };

  const loadDeployments = async () => {
    try {
      const data = await api.getApiDeploymentHistory(selectedServerId, 50);
      setDeployments(data);
    } catch (error: any) {
      console.error('Error loading deployments:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    }
  };

  const toggleExpanded = (id: number) => {
    const newExpanded = new Set(expandedDeployments);
    if (newExpanded.has(id)) {
      newExpanded.delete(id);
    } else {
      newExpanded.add(id);
    }
    setExpandedDeployments(newExpanded);
  };

  const handleDownloadBackup = async (deploymentId: number, backupFileName: string) => {
    try {
      const blob = await api.downloadApiBackup(deploymentId);
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = backupFileName;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      toast.success('Backup downloaded successfully');
    } catch (error: any) {
      toast.error(`Failed to download backup: ${error.response?.data?.error || error.message}`);
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Success':
        return 'bg-green-500/10 text-green-700 ring-1 ring-green-500/20';
      case 'Failed':
        return 'bg-red-500/10 text-red-700 ring-1 ring-red-500/20';
      default:
        return 'bg-yellow-500/10 text-yellow-700 ring-1 ring-yellow-500/20';
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
            <p className="mt-4 text-muted-foreground">Loading history...</p>
          </div>
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        {/* Header */}
        <div>
          <h1 className="text-3xl font-bold tracking-tight">App Deployment History</h1>
          <p className="text-muted-foreground">
            View API and Frontend deployment history, download backups
          </p>
        </div>

        {/* Server Filter */}
        {servers.length > 0 && (
          <Card>
            <CardContent className="pt-6">
              <div className="flex items-center gap-4">
                <Server className="h-5 w-5 text-muted-foreground" />
                <select
                  className="flex-1 border rounded-md p-2"
                  value={selectedServerId}
                  onChange={(e) => setSelectedServerId(Number(e.target.value))}
                >
                  {servers.map((server) => (
                    <option key={server.id} value={server.id}>
                      [{server.serverType}] {server.serverName} — {server.applicationName}
                    </option>
                  ))}
                </select>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Deployments List */}
        <div className="space-y-4">
          {deployments.map((deployment) => {
            const isExpanded = expandedDeployments.has(deployment.id);
            return (
              <Card key={deployment.id}>
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-3 flex-1">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => toggleExpanded(deployment.id)}
                        className="p-0 h-6 w-6"
                      >
                        {isExpanded ? (
                          <ChevronDown className="h-4 w-4" />
                        ) : (
                          <ChevronRight className="h-4 w-4" />
                        )}
                      </Button>
                      <div className="flex-1">
                        <div className="flex items-center gap-2 flex-wrap">
                          <CardTitle className="text-lg">
                            Deployment #{deployment.id}
                          </CardTitle>
                          {deployment.version && (
                            <Badge variant="outline">v{deployment.version}</Badge>
                          )}
                          <Badge
                            variant="outline"
                            className={
                              deployment.deploymentType === 'Frontend'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-gray-400 text-gray-600'
                            }
                          >
                            {deployment.deploymentType}
                          </Badge>
                        </div>
                        <CardDescription className="text-xs mt-1">
                          {new Date(deployment.deploymentDate).toLocaleString()}
                        </CardDescription>
                      </div>
                    </div>
                    <Badge className={getStatusColor(deployment.status)}>
                      {deployment.status}
                    </Badge>
                  </div>
                </CardHeader>
                <CardContent className="space-y-3">
                  {/* Basic Info */}
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                    <div className="flex items-center gap-2">
                      <User className="h-4 w-4 text-muted-foreground" />
                      <div>
                        <div className="text-xs text-muted-foreground">Deployed By</div>
                        <div className="font-medium">{deployment.deployedBy}</div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <Clock className="h-4 w-4 text-muted-foreground" />
                      <div>
                        <div className="text-xs text-muted-foreground">Duration</div>
                        <div className="font-medium">
                          {deployment.deploymentDurationSeconds ? `${deployment.deploymentDurationSeconds}s` : 'N/A'}
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <FileCode className="h-4 w-4 text-muted-foreground" />
                      <div>
                        <div className="text-xs text-muted-foreground">Files</div>
                        <div className="font-medium">{deployment.files.length}</div>
                      </div>
                    </div>
                    {deployment.backupFileName && (
                      <div>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => handleDownloadBackup(deployment.id, deployment.backupFileName!)}
                          className="w-full"
                        >
                          <Download className="mr-1 h-3 w-3" />
                          Backup
                        </Button>
                      </div>
                    )}
                  </div>

                  {/* Error Message */}
                  {deployment.errorMessage && (
                    <div className="bg-red-50 ring-1 ring-red-200 rounded-lg p-3">
                      <div className="text-xs font-semibold text-red-900 mb-1">Error:</div>
                      <div className="text-xs text-red-700 font-mono">{deployment.errorMessage}</div>
                    </div>
                  )}

                  {/* Expanded Details */}
                  {isExpanded && (
                    <div className="mt-4 pt-4 border-t space-y-4">
                      {deployment.files.length > 0 && (
                        <div>
                          <div className="text-sm font-semibold mb-2">Deployed Files:</div>
                          <div className="bg-muted/50 rounded-lg p-3 max-h-48 overflow-y-auto">
                            {deployment.files.map((file) => (
                              <div key={file.id} className="flex justify-between items-center py-1 text-sm">
                                <span className="font-mono text-xs">{file.fileName}</span>
                                <span className="text-muted-foreground text-xs">{formatFileSize(file.fileSize)}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      <div className="grid grid-cols-2 gap-4 text-sm">
                        {deployment.startedAt && (
                          <div>
                            <div className="text-xs text-muted-foreground">Started At</div>
                            <div className="font-mono text-xs">{new Date(deployment.startedAt).toLocaleString()}</div>
                          </div>
                        )}
                        {deployment.completedAt && (
                          <div>
                            <div className="text-xs text-muted-foreground">Completed At</div>
                            <div className="font-mono text-xs">{new Date(deployment.completedAt).toLocaleString()}</div>
                          </div>
                        )}
                      </div>

                      {deployment.backupFileName && (
                        <div className="bg-blue-50 ring-1 ring-blue-200 rounded-lg p-3">
                          <div className="text-xs font-semibold text-blue-900 mb-1">Backup File:</div>
                          <div className="text-xs text-blue-700 font-mono">{deployment.backupFileName}</div>
                        </div>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </div>

        {/* Empty States */}
        {servers.length === 0 && (
          <Card>
            <CardContent className="flex flex-col items-center justify-center py-16">
              <History className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold mb-2">No servers configured</h3>
              <p className="text-muted-foreground mb-4">Add a server first to see deployment history</p>
              <Button onClick={() => window.location.href = '/servers'}>
                Go to Servers
              </Button>
            </CardContent>
          </Card>
        )}

        {servers.length > 0 && deployments.length === 0 && (
          <Card>
            <CardContent className="flex flex-col items-center justify-center py-16">
              <History className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold mb-2">No deployments yet</h3>
              <p className="text-muted-foreground">No deployments found for this server</p>
            </CardContent>
          </Card>
        )}
      </div>
    </ProtectedLayout>
  );
}
