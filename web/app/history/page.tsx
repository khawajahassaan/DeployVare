'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { Deployment, DeploymentPatch, DeploymentObject } from '@/lib/api';
import { CheckCircle, XCircle, Clock, Download, FileText, Database, FunctionSquare, History } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { toast } from 'sonner';

export default function HistoryPage() {
  const { selectedClientId } = useClientStore();
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [selectedDeployment, setSelectedDeployment] = useState<Deployment | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);

  useEffect(() => {
    if (selectedClientId) loadDeployments();
  }, [page, selectedClientId]);

  const loadDeployments = async () => {
    try {
      const result = await api.getDeployments(page, 20, selectedClientId);
      setDeployments(result.data);
      setTotalPages(result.totalPages);
    } catch (error) {
      console.error('Failed to load deployments:', error);
    } finally {
      setLoading(false);
    }
  };

  const loadDeploymentDetails = async (id: number) => {
    try {
      const details = await api.getDeployment(id);
      setSelectedDeployment(details);
      setDetailsOpen(true);
    } catch (error) {
      console.error('Failed to load deployment details:', error);
    }
  };

  const handleDownloadBackup = async (deploymentId: number) => {
    try {
      const blob = await api.downloadBackup(deploymentId);
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `backup-${deploymentId}.zip`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      toast.success('Backup downloaded successfully');
    } catch (error) {
      toast.error('Failed to download backup');
    }
  };

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Success':
        return <Badge variant="success"><CheckCircle className="h-3 w-3" />Success</Badge>;
      case 'Failed':
        return <Badge variant="destructive"><XCircle className="h-3 w-3" />Failed</Badge>;
      case 'InProgress':
        return <Badge variant="warning"><Clock className="h-3 w-3" />In Progress</Badge>;
      default:
        return <Badge>{status}</Badge>;
    }
  };

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Deployment History"
          description="View and manage past deployments"
          icon={History}
        />

        {loading ? (
          <LoadingState label="Loading deployment history..." />
        ) : deployments.length === 0 ? (
          <EmptyState
            icon={Clock}
            title="No deployments yet"
            description="Your deployment history will appear here"
          />
        ) : (
          <>
            <Card>
              <CardHeader>
                <CardTitle>Deployments</CardTitle>
                <CardDescription>
                  Page {page} of {totalPages}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="divide-y divide-border">
                  {deployments.map((deployment) => (
                    <div
                      key={deployment.id}
                      className="flex cursor-pointer items-center justify-between gap-4 rounded-lg px-3 py-3 transition-colors hover:bg-muted/40"
                      onClick={() => loadDeploymentDetails(deployment.id)}
                    >
                      <div className="min-w-0 flex-1 space-y-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="font-medium">
                            {deployment.applicationName} - {deployment.instanceName}
                          </p>
                          {getStatusBadge(deployment.status)}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          Patches {deployment.fromPatchNumber} → {deployment.toPatchNumber}
                        </p>
                        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                          <span>Deployed by {deployment.deployedBy}</span>
                          <span>{formatDate(deployment.deployedAt)}</span>
                          {deployment.durationSeconds && (
                            <span>{deployment.durationSeconds}s</span>
                          )}
                        </div>
                        {deployment.errorMessage && (
                          <p className="text-xs text-destructive mt-1">
                            {deployment.errorMessage}
                          </p>
                        )}
                      </div>
                      {deployment.backupFilePath && (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDownloadBackup(deployment.id);
                          }}
                        >
                          <Download className="h-3 w-3" />
                          Backup
                        </Button>
                      )}
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>

            {/* Pagination */}
            {totalPages > 1 && (
              <div className="flex justify-center gap-2">
                <Button
                  variant="outline"
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  disabled={page === 1}
                >
                  Previous
                </Button>
                <span className="flex items-center px-4 text-sm text-muted-foreground">
                  Page {page} of {totalPages}
                </span>
                <Button
                  variant="outline"
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                  disabled={page === totalPages}
                >
                  Next
                </Button>
              </div>
            )}
          </>
        )}

        {/* Details Dialog */}
        <Dialog open={detailsOpen} onOpenChange={setDetailsOpen}>
          <DialogContent className="max-w-3xl max-h-[80vh] overflow-y-auto">
            {selectedDeployment && (
              <>
                <DialogHeader>
                  <DialogTitle>Deployment Details</DialogTitle>
                </DialogHeader>
                <div className="space-y-6">
                  {/* Summary */}
                  <div className="grid grid-cols-2 gap-4 rounded-lg border border-border bg-muted/50 p-4 text-sm">
                    <div>
                      <span className="text-muted-foreground">Application:</span>
                      <p className="font-medium">{selectedDeployment.applicationName}</p>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Instance:</span>
                      <p className="font-medium">{selectedDeployment.instanceName}</p>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Status:</span>
                      <div className="mt-1">{getStatusBadge(selectedDeployment.status)}</div>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Deployed By:</span>
                      <p className="font-medium">{selectedDeployment.deployedBy}</p>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Deployed At:</span>
                      <p className="font-medium">{formatDate(selectedDeployment.deployedAt)}</p>
                    </div>
                    <div>
                      <span className="text-muted-foreground">Patches:</span>
                      <p className="font-medium">
                        {selectedDeployment.fromPatchNumber} → {selectedDeployment.toPatchNumber}
                      </p>
                    </div>
                  </div>

                  {/* Patches */}
                  {selectedDeployment.patches && selectedDeployment.patches.length > 0 && (
                    <div>
                      <h3 className="mb-3 flex items-center gap-2 text-[16px] font-normal text-ink">
                        <FileText className="h-4 w-4" />
                        SQL Patches ({selectedDeployment.patches.length})
                      </h3>
                      <div className="space-y-2">
                        {selectedDeployment.patches.map((patch: DeploymentPatch, idx: number) => (
                          <div
                            key={idx}
                            className="flex items-start justify-between rounded-lg border border-border p-3"
                          >
                            <div className="flex-1">
                              <p className="font-medium text-sm">
                                Patch #{patch.patchNumber}: {patch.description || patch.patchFileName}
                              </p>
                              {patch.author && (
                                <p className="text-xs text-muted-foreground">
                                  by {patch.author}
                                </p>
                              )}
                            </div>
                            {patch.status === 'Success' ? (
                              <CheckCircle className="h-4 w-4 text-green-600" />
                            ) : (
                              <XCircle className="h-4 w-4 text-destructive" />
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Objects (Procedures/Functions) */}
                  {selectedDeployment.objects && selectedDeployment.objects.length > 0 && (
                    <div>
                      <h3 className="mb-3 flex items-center gap-2 text-[16px] font-normal text-ink">
                        <Database className="h-4 w-4" />
                        Database Objects ({selectedDeployment.objects.length})
                      </h3>
                      <div className="grid gap-2">
                        {selectedDeployment.objects.map((obj: DeploymentObject, idx: number) => (
                          <div key={idx} className="flex items-center justify-between rounded-lg border border-border p-3">
                            <div className="flex items-center gap-2">
                              {obj.objectType.includes('Procedure') ? (
                                <Database className="h-4 w-4 text-primary" />
                              ) : (
                                <FunctionSquare className="h-4 w-4 text-primary" />
                              )}
                              <div>
                                <p className="font-medium text-sm">{obj.objectName}</p>
                                <p className="text-xs text-muted-foreground">{obj.objectType}</p>
                              </div>
                            </div>
                            {obj.backedUp && (
                              <Badge variant="secondary" className="text-xs">
                                Backed Up
                              </Badge>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Error Message */}
                  {selectedDeployment.errorMessage && (
                    <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                      <p className="font-semibold mb-1">Error:</p>
                      <p>{selectedDeployment.errorMessage}</p>
                    </div>
                  )}

                  {/* Actions */}
                  {selectedDeployment.backupFilePath && (
                    <Button
                      onClick={() => handleDownloadBackup(selectedDeployment.id)}
                      className="w-full"
                    >
                      <Download className="h-4 w-4" />
                      Download Backup
                    </Button>
                  )}
                </div>
              </>
            )}
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}
