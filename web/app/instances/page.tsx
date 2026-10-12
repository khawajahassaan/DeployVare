'use client';

import { useEffect, useState, Suspense } from 'react';
import { useSearchParams } from 'next/navigation';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import api, { Application, DatabaseInstance } from '@/lib/api';
import { Database, Plus, Trash2, CheckCircle, XCircle, Play } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';

function InstancesContent() {
  const searchParams = useSearchParams();
  const appIdParam = searchParams?.get('app');
  const { selectedClientId } = useClientStore();

  const [applications, setApplications] = useState<Application[]>([]);
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [loading, setLoading] = useState(true);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newInstance, setNewInstance] = useState({
    applicationId: appIdParam ? parseInt(appIdParam) : 0,
    instanceName: '',
    connectionString: '',
    currentPatchNumber: 0,
  });
  const [creating, setCreating] = useState(false);
  const [testing, setTesting] = useState<number | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    if (selectedClientId) loadData();
  }, [selectedClientId]);

  const loadData = async () => {
    try {
      const [apps, insts] = await Promise.all([
        api.getApplications(selectedClientId),
        api.getDatabaseInstances(selectedClientId),
      ]);
      setApplications(apps);
      setInstances(insts);
    } catch (error) {
      console.error('Failed to load data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setCreating(true);

    try {
      await api.createDatabaseInstance(newInstance);
      setIsCreateOpen(false);
      setNewInstance({
        applicationId: 0,
        instanceName: '',
        connectionString: '',
        currentPatchNumber: 0,
      });
      loadData();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create instance');
    } finally {
      setCreating(false);
    }
  };

  const handleTestConnection = async (id: number) => {
    setTesting(id);
    try {
      const result = await api.testConnection(id);
      alert(result.message);
    } catch (error: any) {
      alert(error.response?.data?.message || 'Connection test failed');
    } finally {
      setTesting(null);
    }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Are you sure you want to delete instance "${name}"?`)) return;

    try {
      await api.deleteDatabaseInstance(id);
      loadData();
    } catch (error: any) {
      alert(error.response?.data?.message || 'Failed to delete instance');
    }
  };

  const groupedInstances = applications.map((app) => ({
    application: app,
    instances: instances.filter((inst) => inst.applicationId === app.id),
  }));

  const addButton = (
    <Button onClick={() => setIsCreateOpen(true)}>
      <Plus className="h-4 w-4" />
      Add Instance
    </Button>
  );

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Database Instances"
          description="Manage database instances for your applications"
          icon={Database}
          actions={addButton}
        />

        {loading ? (
          <LoadingState label="Loading instances..." />
        ) : instances.length === 0 ? (
          <EmptyState
            icon={Database}
            title="No database instances yet"
            description="Add your first database instance to start deploying"
            action={addButton}
          />
        ) : (
          <div className="space-y-6">
            {groupedInstances.map(({ application, instances }) => (
              <div key={application.id}>
                <h2 className="text-[16px] font-normal text-ink mb-3">{application.name}</h2>
                {instances.length === 0 ? (
                  <EmptyState compact title="No instances for this application" />
                ) : (
                  <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
                    {instances.map((instance) => (
                      <Card key={instance.id} className="hover:shadow-md transition-shadow">
                        <CardHeader>
                          <div className="flex items-start justify-between gap-2">
                            <div className="flex min-w-0 items-center gap-2">
                              <Database className="h-5 w-5 shrink-0 text-primary" />
                              <CardTitle className="truncate">{instance.instanceName}</CardTitle>
                            </div>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => handleDelete(instance.id, instance.instanceName)}
                            >
                              <Trash2 className="h-4 w-4 text-destructive" />
                            </Button>
                          </div>
                          <CardDescription>{application.name}</CardDescription>
                        </CardHeader>
                        <CardContent>
                          <div className="space-y-3">
                            <div className="flex items-center justify-between text-sm">
                              <span className="text-muted-foreground">Current Patch</span>
                              <Badge variant="secondary">{instance.currentPatchNumber}</Badge>
                            </div>
                            <div className="flex items-center justify-between text-sm">
                              <span className="text-muted-foreground">Status</span>
                              {instance.isActive ? (
                                <Badge variant="success">
                                  <CheckCircle className="h-3 w-3" />
                                  Active
                                </Badge>
                              ) : (
                                <Badge variant="destructive">
                                  <XCircle className="h-3 w-3" />
                                  Inactive
                                </Badge>
                              )}
                            </div>
                            <div className="text-xs text-muted-foreground">
                              Created {formatDate(instance.createdAt)}
                            </div>
                            <Button
                              variant="outline"
                              size="sm"
                              className="w-full mt-2"
                              onClick={() => handleTestConnection(instance.id)}
                              disabled={testing === instance.id}
                            >
                              {testing === instance.id ? (
                                'Testing...'
                              ) : (
                                <>
                                  <Play className="h-3 w-3" />
                                  Test Connection
                                </>
                              )}
                            </Button>
                          </div>
                        </CardContent>
                      </Card>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {/* Create Dialog */}
        <Dialog open={isCreateOpen} onOpenChange={setIsCreateOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Add Database Instance</DialogTitle>
              <DialogDescription>
                Configure a new database instance for deployment
              </DialogDescription>
            </DialogHeader>
            <form onSubmit={handleCreate} className="space-y-4">
              {error && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {error}
                </div>
              )}
              <div className="space-y-2">
                <Label htmlFor="application">Application *</Label>
                <Select
                  id="application"
                  value={newInstance.applicationId}
                  onChange={(e) =>
                    setNewInstance({ ...newInstance, applicationId: parseInt(e.target.value) })
                  }
                  required
                  disabled={creating}
                >
                  <option value="0">Select an application</option>
                  {applications.map((app) => (
                    <option key={app.id} value={app.id}>
                      {app.name}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="space-y-2">
                <Label htmlFor="instanceName">Instance Name *</Label>
                <Input
                  id="instanceName"
                  placeholder="e.g., Production, Stage, Dev"
                  autoComplete="off"
                  value={newInstance.instanceName}
                  onChange={(e) =>
                    setNewInstance({ ...newInstance, instanceName: e.target.value })
                  }
                  required
                  disabled={creating}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="connectionString">Connection String *</Label>
                <Input
                  id="connectionString"
                  type="password"
                  autoComplete="new-password"
                  placeholder="Server=...;Database=...;User Id=...;Password=..."
                  value={newInstance.connectionString}
                  onChange={(e) =>
                    setNewInstance({ ...newInstance, connectionString: e.target.value })
                  }
                  required
                  disabled={creating}
                />
                <p className="text-xs text-muted-foreground">
                  Connection string will be encrypted and stored securely
                </p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="patchNumber">Current Patch Number</Label>
                <Input
                  id="patchNumber"
                  type="number"
                  min="0"
                  placeholder="0"
                  value={newInstance.currentPatchNumber}
                  onChange={(e) =>
                    setNewInstance({
                      ...newInstance,
                      currentPatchNumber: parseInt(e.target.value) || 0,
                    })
                  }
                  disabled={creating}
                />
              </div>
              <div className="flex justify-end space-x-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setIsCreateOpen(false)}
                  disabled={creating}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={creating}>
                  {creating ? 'Adding...' : 'Add Instance'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}

export default function InstancesPage() {
  return (
    <Suspense>
      <InstancesContent />
    </Suspense>
  );
}



