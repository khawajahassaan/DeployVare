'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { Application } from '@/lib/api';
import { FolderKanban, Plus, Trash2, Database } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import Link from 'next/link';

export default function ApplicationsPage() {
  const { clients, selectedClientId } = useClientStore();
  const selectedClient = clients.find((c) => c.id === selectedClientId);
  const [applications, setApplications] = useState<Application[]>([]);
  const [loading, setLoading] = useState(true);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newApp, setNewApp] = useState({ name: '', description: '' });
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (selectedClientId) loadApplications();
  }, [selectedClientId]);

  const loadApplications = async () => {
    try {
      const data = await api.getApplications(selectedClientId);
      setApplications(data);
    } catch (error) {
      console.error('Failed to load applications:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setCreating(true);

    try {
      await api.createApplication({ ...newApp, clientId: selectedClientId! });
      setIsCreateOpen(false);
      setNewApp({ name: '', description: '' });
      loadApplications();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create application');
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`Are you sure you want to delete "${name}"?`)) return;

    try {
      await api.deleteApplication(id);
      loadApplications();
    } catch (error: any) {
      alert(error.response?.data?.message || 'Failed to delete application');
    }
  };

  const createButton = (
    <Button onClick={() => setIsCreateOpen(true)}>
      <Plus className="h-4 w-4" />
      Create Application
    </Button>
  );

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Applications"
          description={`Manage applications and their database instances${selectedClient ? ` for ${selectedClient.name}` : ''}`}
          icon={FolderKanban}
          actions={createButton}
        />

        {loading ? (
          <LoadingState label="Loading applications..." />
        ) : applications.length === 0 ? (
          <EmptyState
            icon={FolderKanban}
            title="No applications yet"
            description="Get started by creating your first application"
            action={createButton}
          />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {applications.map((app) => (
              <Card key={app.id} className="hover:shadow-md transition-shadow">
                <CardHeader>
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex min-w-0 items-center gap-2">
                      <FolderKanban className="h-5 w-5 shrink-0 text-primary" />
                      <CardTitle className="truncate">{app.name}</CardTitle>
                    </div>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleDelete(app.id, app.name)}
                    >
                      <Trash2 className="h-4 w-4 text-destructive" />
                    </Button>
                  </div>
                  <CardDescription>
                    {app.description || 'No description'}
                  </CardDescription>
                </CardHeader>
                <CardContent>
                  <div className="space-y-2">
                    <div className="flex items-center justify-between text-sm">
                      <span className="text-muted-foreground">Instances</span>
                      <Badge variant="secondary">
                        <Database className="h-3 w-3" />
                        {app.instanceCount}
                      </Badge>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      Created {formatDate(app.createdAt)}
                    </div>
                    <Link href={`/instances?app=${app.id}`}>
                      <Button variant="outline" size="sm" className="w-full mt-2">
                        View Instances
                      </Button>
                    </Link>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}

        {/* Create Dialog */}
        <Dialog open={isCreateOpen} onOpenChange={setIsCreateOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Create New Application</DialogTitle>
              <DialogDescription>
                Add a new application for client <strong>{selectedClient?.name}</strong>
              </DialogDescription>
            </DialogHeader>
            <form onSubmit={handleCreate} className="space-y-4">
              {error && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {error}
                </div>
              )}
              <div className="space-y-2">
                <Label htmlFor="name">Application Name *</Label>
                <Input
                  id="name"
                  placeholder="e.g., AuditVare"
                  value={newApp.name}
                  onChange={(e) => setNewApp({ ...newApp, name: e.target.value })}
                  required
                  disabled={creating}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="description">Description</Label>
                <Input
                  id="description"
                  placeholder="Brief description of the application"
                  value={newApp.description}
                  onChange={(e) => setNewApp({ ...newApp, description: e.target.value })}
                  disabled={creating}
                />
              </div>
              <div className="flex justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setIsCreateOpen(false)}
                  disabled={creating}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={creating}>
                  {creating ? 'Creating...' : 'Create Application'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}
