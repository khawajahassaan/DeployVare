'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import api, { Client } from '@/lib/api';
import { useClientStore } from '@/store/clientStore';
import { Building2, Plus, Trash2, Copy, KeyRound, Pencil, CheckCircle, XCircle, FolderKanban } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import { toast } from 'sonner';

export default function ClientsPage() {
  const { clients, loaded, loadClients, selectedClientId, selectClient } = useClientStore();

  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [newClient, setNewClient] = useState({ name: '', clientCode: '' });
  const [createdClient, setCreatedClient] = useState<Client | null>(null);

  const [editingClient, setEditingClient] = useState<Client | null>(null);
  const [editForm, setEditForm] = useState({ name: '', clientCode: '', isActive: true });

  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    loadClients();
  }, [loadClients]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSaving(true);

    try {
      const client = await api.createClient(newClient);
      setIsCreateOpen(false);
      setNewClient({ name: '', clientCode: '' });
      await loadClients();
      selectClient(client.id);
      setCreatedClient(client);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create client');
    } finally {
      setSaving(false);
    }
  };

  const openEditDialog = (client: Client) => {
    setError('');
    setEditingClient(client);
    setEditForm({ name: client.name, clientCode: client.clientCode, isActive: client.isActive });
  };

  const handleUpdate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingClient) return;
    setError('');
    setSaving(true);

    try {
      await api.updateClient(editingClient.id, editForm);
      setEditingClient(null);
      await loadClients();
      toast.success('Client updated');
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to update client');
    } finally {
      setSaving(false);
    }
  };

  const handleRegenerate = async (client: Client) => {
    if (!confirm(`Generate a new license key for "${client.name}"? The current key will stop working.`)) return;

    try {
      await api.regenerateClientLicense(client.id);
      await loadClients();
      toast.success('New license key generated');
    } catch (error: any) {
      toast.error(error.response?.data?.message || 'Failed to regenerate license key');
    }
  };

  const handleDelete = async (client: Client) => {
    if (!confirm(`Are you sure you want to delete client "${client.name}"?`)) return;

    try {
      await api.deleteClient(client.id);
      await loadClients();
      toast.success('Client deleted');
    } catch (error: any) {
      toast.error(error.response?.data?.message || 'Failed to delete client');
    }
  };

  const copyLicenseKey = async (licenseKey: string) => {
    try {
      await navigator.clipboard.writeText(licenseKey);
      toast.success('License key copied');
    } catch {
      toast.error('Could not copy license key');
    }
  };

  const openCreateDialog = () => {
    setError('');
    setIsCreateOpen(true);
  };

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <PageHeader
          title="Client Onboarding"
          description="Onboard clients — applications, instances, servers and deployments are managed per client"
          icon={Building2}
          actions={
            <Button onClick={openCreateDialog}>
              <Plus className="h-4 w-4" />
              Create Client
            </Button>
          }
        />

        {!loaded ? (
          <LoadingState label="Loading clients..." />
        ) : clients.length === 0 ? (
          <EmptyState
            icon={Building2}
            title="No clients yet"
            description="Create your first client to start managing its applications and deployments"
            action={
              <Button onClick={openCreateDialog}>
                <Plus className="h-4 w-4" />
                Create Client
              </Button>
            }
          />
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {clients.map((client) => {
              const isSelected = client.id === selectedClientId;
              return (
                <Card
                  key={client.id}
                  className={isSelected ? 'ring-2 ring-primary' : 'hover:shadow-md transition-shadow'}
                >
                  <CardHeader>
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex min-w-0 items-center gap-2">
                        <Building2 className="h-5 w-5 shrink-0 text-primary" />
                        <CardTitle className="truncate">{client.name}</CardTitle>
                      </div>
                      <div className="flex">
                        <Button variant="ghost" size="icon" onClick={() => openEditDialog(client)}>
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <Button variant="ghost" size="icon" onClick={() => handleDelete(client)}>
                          <Trash2 className="h-4 w-4 text-destructive" />
                        </Button>
                      </div>
                    </div>
                    <CardDescription>
                      Client ID: <span className="font-mono">{client.clientCode}</span>
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      <div className="space-y-1">
                        <span className="text-xs text-muted-foreground">License Key</span>
                        <div className="flex items-center gap-2">
                          <code className="flex-1 truncate rounded bg-muted px-2 py-1 text-xs">
                            {client.licenseKey}
                          </code>
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Copy license key"
                            onClick={() => copyLicenseKey(client.licenseKey)}
                          >
                            <Copy className="h-4 w-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            title="Regenerate license key"
                            onClick={() => handleRegenerate(client)}
                          >
                            <KeyRound className="h-4 w-4" />
                          </Button>
                        </div>
                      </div>
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-muted-foreground">Applications</span>
                        <Badge variant="secondary">
                          <FolderKanban className="h-3 w-3" />
                          {client.applicationCount}
                        </Badge>
                      </div>
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-muted-foreground">Status</span>
                        {client.isActive ? (
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
                        Created {formatDate(client.createdAt)} by {client.createdBy}
                      </div>
                      <Button
                        variant={isSelected ? 'default' : 'outline'}
                        size="sm"
                        className="w-full mt-2"
                        onClick={() => selectClient(client.id)}
                        disabled={isSelected}
                      >
                        {isSelected ? 'Currently Selected' : 'Work on this Client'}
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              );
            })}
          </div>
        )}

        {/* Create Dialog */}
        <Dialog open={isCreateOpen} onOpenChange={setIsCreateOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Create New Client</DialogTitle>
              <DialogDescription>
                A unique 32-character license key is generated automatically
              </DialogDescription>
            </DialogHeader>
            <form onSubmit={handleCreate} className="space-y-4">
              {error && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {error}
                </div>
              )}
              <div className="space-y-2">
                <Label htmlFor="clientName">Client Name *</Label>
                <Input
                  id="clientName"
                  placeholder="e.g., Acme Corporation"
                  value={newClient.name}
                  onChange={(e) => setNewClient({ ...newClient, name: e.target.value })}
                  required
                  disabled={saving}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="clientCode">Client ID *</Label>
                <Input
                  id="clientCode"
                  placeholder="e.g., ACME001"
                  value={newClient.clientCode}
                  onChange={(e) => setNewClient({ ...newClient, clientCode: e.target.value })}
                  required
                  maxLength={50}
                  disabled={saving}
                />
                <p className="text-xs text-muted-foreground">Must be unique across all clients</p>
              </div>
              <div className="flex justify-end space-x-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setIsCreateOpen(false)}
                  disabled={saving}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={saving}>
                  {saving ? 'Creating...' : 'Create Client'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>

        {/* Created Dialog – shows the generated license key */}
        <Dialog open={!!createdClient} onOpenChange={(open) => !open && setCreatedClient(null)}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Client Created</DialogTitle>
              <DialogDescription>
                {createdClient?.name} is onboarded and is now the active client.
              </DialogDescription>
            </DialogHeader>
            {createdClient && (
              <div className="space-y-4">
                <div className="space-y-1">
                  <span className="text-sm text-muted-foreground">Client ID</span>
                  <p className="font-mono">{createdClient.clientCode}</p>
                </div>
                <div className="space-y-1">
                  <span className="text-sm text-muted-foreground">License Key</span>
                  <div className="flex items-center gap-2">
                    <code className="flex-1 break-all rounded bg-muted px-3 py-2 text-sm">
                      {createdClient.licenseKey}
                    </code>
                    <Button variant="outline" size="icon" onClick={() => copyLicenseKey(createdClient.licenseKey)}>
                      <Copy className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
                <div className="flex justify-end">
                  <Button onClick={() => setCreatedClient(null)}>Done</Button>
                </div>
              </div>
            )}
          </DialogContent>
        </Dialog>

        {/* Edit Dialog */}
        <Dialog open={!!editingClient} onOpenChange={(open) => !open && setEditingClient(null)}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Edit Client</DialogTitle>
              <DialogDescription>Update client details</DialogDescription>
            </DialogHeader>
            <form onSubmit={handleUpdate} className="space-y-4">
              {error && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {error}
                </div>
              )}
              <div className="space-y-2">
                <Label htmlFor="editClientName">Client Name *</Label>
                <Input
                  id="editClientName"
                  value={editForm.name}
                  onChange={(e) => setEditForm({ ...editForm, name: e.target.value })}
                  required
                  disabled={saving}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="editClientCode">Client ID *</Label>
                <Input
                  id="editClientCode"
                  value={editForm.clientCode}
                  onChange={(e) => setEditForm({ ...editForm, clientCode: e.target.value })}
                  required
                  maxLength={50}
                  disabled={saving}
                />
              </div>
              <label className="flex items-center gap-2 text-sm cursor-pointer">
                <input
                  type="checkbox"
                  checked={editForm.isActive}
                  onChange={(e) => setEditForm({ ...editForm, isActive: e.target.checked })}
                  disabled={saving}
                />
                Active
              </label>
              <div className="flex justify-end space-x-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => setEditingClient(null)}
                  disabled={saving}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={saving}>
                  {saving ? 'Saving...' : 'Save Changes'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}
