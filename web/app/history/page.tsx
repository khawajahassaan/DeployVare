'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import api, { Deployment, DeploymentPatch, DeploymentObject } from '@/lib/api';
import { CheckCircle, XCircle, Clock, Download, FileText, Database, FunctionSquare } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { toast } from 'sonner';

export default function HistoryPage() {
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [selectedDeployment, setSelectedDeployment] = useState<Deployment | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);

  useEffect(() => {
    loadDeployments();
  }, [page]);

  const loadDeployments = async () => {
    try {
      const result = await api.getDeployments(page, 20);
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
        return <Badge variant="success"><CheckCircle className="mr-1 h-3 w-3" />Success</Badge>;
      case 'Failed':
        return <Badge variant="destructive"><XCircle className="mr-1 h-3 w-3" />Failed</Badge>;
      case 'InProgress':
        return <Badge variant="warning"><Clock className="mr-1 h-3 w-3" />In Progress</Badge>;
      default:
        return <Badge>{status}</Badge>;
    }
  };

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">Deployment History</h1>
          <p className="text-muted-foreground">View and manage past deployments</p>
        </div>

        {loading ? (
          <div className="text-center py-12">
            <p className="text-muted-foreground">Loading deployment history...</p>
          </div>
        ) : deployments.length === 0 ? (
          <Card>
            <CardContent className="py-12 text-center">
              <Clock className="mx-auto h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold mb-2">No deployments yet</h3>
              <p className="text-muted-foreground">
                Your deployment history will appear here
              </p>
            </CardContent>
          </Card>
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
                <div className="space-y-4">
                  {deployments.map((deployment) => (
                    <div
                      key={deployment.id}
                      className="flex items-center justify-between pb-4 cursor-pointer hover:bg-muted/50 p-3 rounded-lg transition-colors hover:shadow-sm"
                      onClick={() => loadDeploymentDetails(deployment.id)}
                    >
                      <div className="flex-1 space-y-1">
                        <div className="flex items-center gap-2">
                          <p className="font-medium">
                            {deployment.applicationName} - {deployment.instanceName}
                          </p>
                          {getStatusBadge(deployment.status)}
                        </div>
                        <p className="text-sm text-muted-foreground">
                          Patches {deployment.fromPatchNumber} → {deployment.toPatchNumber}
                        </p>
                        <div className="flex items-center gap-4 text-xs text-muted-foreground">
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
                          <Download className="mr-2 h-3 w-3" />
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
                  <div className="grid grid-cols-2 gap-4 text-sm">
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
                      <h3 className="font-semibold mb-3 flex items-center gap-2">
                        <FileText className="h-4 w-4" />
                        SQL Patches ({selectedDeployment.patches.length})
                      </h3>
                      <div className="space-y-2">
                        {selectedDeployment.patches.map((patch: DeploymentPatch, idx: number) => (
                          <div
                            key={idx}
                            className="flex items-start justify-between p-3 rounded-lg border"
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
                              <CheckCircle className="h-4 w-4 text-green-500" />
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
                      <h3 className="font-semibold mb-3 flex items-center gap-2">
                        <Database className="h-4 w-4" />
                        Database Objects ({selectedDeployment.objects.length})
                      </h3>
                      <div className="grid gap-2">
                        {selectedDeployment.objects.map((obj: DeploymentObject, idx: number) => (
                          <div key={idx} className="flex items-center justify-between p-3 rounded-lg border">
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
                    <div className="rounded-lg ring-1 ring-red-200 p-4 bg-red-50 shadow-sm">
                      <p className="font-semibold text-sm text-destructive mb-2">Error:</p>
                      <p className="text-sm text-destructive">{selectedDeployment.errorMessage}</p>
                    </div>
                  )}

                  {/* Actions */}
                  {selectedDeployment.backupFilePath && (
                    <Button
                      onClick={() => handleDownloadBackup(selectedDeployment.id)}
                      className="w-full"
                    >
                      <Download className="mr-2 h-4 w-4" />
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

