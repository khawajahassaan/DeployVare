'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { ApplicationServer, ApiDeployment } from '@/lib/api';
import { History, Server, Clock, User, FileCode, Download, ChevronDown, ChevronRight } from 'lucide-react';
import { toast } from 'sonner';

export default function ApiHistoryPage() {
  const { selectedClientId } = useClientStore();
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [selectedServerId, setSelectedServerId] = useState<number>(0);
  const [deployments, setDeployments] = useState<ApiDeployment[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedDeployments, setExpandedDeployments] = useState<Set<number>>(new Set());

  useEffect(() => {
    if (selectedClientId) loadServers();
  }, [selectedClientId]);

  useEffect(() => {
    if (selectedServerId) {
      loadDeployments();
    }
  }, [selectedServerId]);

  const loadServers = async () => {
    try {
      const data = await api.getApplicationServers(selectedClientId);
      setServers(data);
      setSelectedServerId(data.length > 0 ? data[0].id : 0);
      if (data.length === 0) {
        setDeployments([]);
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

  const getStatusVariant = (status: string): 'success' | 'destructive' | 'warning' | 'secondary' => {
    switch (status) {
      case 'Success':
        return 'success';
      case 'Failed':
        return 'destructive';
      case 'Pending':
      case 'StoppingServices':
      case 'CreatingBackup':
      case 'UploadingFiles':
      case 'StartingServices':
      case 'Verifying':
        return 'warning';
      default:
        return 'secondary';
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  if (loading) {
    return (
      <ProtectedLayout requireClient>
        <div className="space-y-6">
          <LoadingState label="Loading history..." />
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="API Deployment History"
          description="View deployment history and download backups"
          icon={FileCode}
        />

        {/* Server Filter */}
        {servers.length > 0 && (
          <Card>
            <CardContent className="pt-6">
              <div className="flex items-center gap-3">
                <Server className="h-5 w-5 shrink-0 text-muted-foreground" />
                <Select
                  className="flex-1"
                  value={selectedServerId}
                  onChange={(e) => setSelectedServerId(Number(e.target.value))}
                >
                  {servers.map((server) => (
                    <option key={server.id} value={server.id}>
                      {server.serverName} - {server.applicationName}
                    </option>
                  ))}
                </Select>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Deployments List */}
        {deployments.length > 0 && (
          <div className="space-y-4">
            {deployments.map((deployment) => {
              const isExpanded = expandedDeployments.has(deployment.id);
              return (
                <Card key={deployment.id}>
                  <CardHeader>
                    <div className="flex items-start justify-between gap-4">
                      <div className="flex flex-1 items-center gap-3 min-w-0">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => toggleExpanded(deployment.id)}
                          className="h-6 w-6 p-0"
                        >
                          {isExpanded ? (
                            <ChevronDown className="h-4 w-4" />
                          ) : (
                            <ChevronRight className="h-4 w-4" />
                          )}
                        </Button>
                        <div className="flex-1 min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <CardTitle>Deployment #{deployment.id}</CardTitle>
                            {deployment.version && (
                              <Badge variant="outline">v{deployment.version}</Badge>
                            )}
                            {deployment.licensingMode === 'WithLicense' && (
                              <Badge variant="outline" title={deployment.licenseId ?? undefined}>
                                Licensed{deployment.licenseId ? ` · ${deployment.licenseId}` : ''}
                              </Badge>
                            )}
                          </div>
                          <CardDescription className="mt-1 text-xs">
                            {new Date(deployment.deploymentDate).toLocaleString()}
                          </CardDescription>
                        </div>
                      </div>
                      <Badge variant={getStatusVariant(deployment.status)}>
                        {deployment.status}
                      </Badge>
                    </div>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    {/* Basic Info */}
                    <div className="grid grid-cols-2 gap-4 text-sm md:grid-cols-4">
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
                            <Download className="h-3 w-3" />
                            Backup
                          </Button>
                        </div>
                      )}
                    </div>

                    {/* Error Message */}
                    {deployment.errorMessage && (
                      <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                        <div className="mb-1 text-xs font-semibold text-red-900">Error:</div>
                        <div className="font-mono text-xs break-words">{deployment.errorMessage}</div>
                      </div>
                    )}

                    {/* Expanded Details */}
                    {isExpanded && (
                      <div className="mt-4 space-y-4 border-t border-border pt-4">
                        {/* Files List */}
                        {deployment.files.length > 0 && (
                          <div>
                            <h2 className="mb-3 text-[16px] font-normal text-ink">Deployed Files</h2>
                            <div className="max-h-48 overflow-y-auto rounded-lg border border-border bg-muted/40 p-3">
                              {deployment.files.map((file) => (
                                <div key={file.id} className="flex items-center justify-between gap-4 py-1 text-sm">
                                  <span className="font-mono text-xs break-all">{file.fileName}</span>
                                  <span className="shrink-0 text-xs text-muted-foreground">{formatFileSize(file.fileSize)}</span>
                                </div>
                              ))}
                            </div>
                          </div>
                        )}

                        {/* Timestamps */}
                        <div className="grid grid-cols-2 gap-4 text-sm">
                          {deployment.startedAt && (
                            <div>
                              <div className="text-xs text-muted-foreground">Started At</div>
                              <div className="font-mono text-xs">
                                {new Date(deployment.startedAt).toLocaleString()}
                              </div>
                            </div>
                          )}
                          {deployment.completedAt && (
                            <div>
                              <div className="text-xs text-muted-foreground">Completed At</div>
                              <div className="font-mono text-xs">
                                {new Date(deployment.completedAt).toLocaleString()}
                              </div>
                            </div>
                          )}
                        </div>

                        {/* Backup Info */}
                        {deployment.backupFileName && (
                          <div className="rounded-lg border border-blue-200 bg-blue-50/60 p-4">
                            <div className="mb-1 text-xs font-semibold text-blue-900">Backup File:</div>
                            <div className="font-mono text-xs text-blue-700 break-all">
                              {deployment.backupFileName}
                            </div>
                          </div>
                        )}
                      </div>
                    )}
                  </CardContent>
                </Card>
              );
            })}
          </div>
        )}

        {/* Empty State */}
        {deployments.length === 0 && (
          <EmptyState
            icon={History}
            title="No deployment history"
            description={servers.length === 0
              ? 'Add a server to start deploying'
              : 'No deployments found for this server'}
            action={servers.length === 0 ? (
              <Button onClick={() => window.location.href = '/servers'}>
                Go to Servers
              </Button>
            ) : undefined}
          />
        )}
      </div>
    </ProtectedLayout>
  );
}
