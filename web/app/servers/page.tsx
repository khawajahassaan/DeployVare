'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { Application, ApplicationServer } from '@/lib/api';
import { Server, Plus, Trash2, TestTube, Edit, Wrench } from 'lucide-react';
import Link from 'next/link';

type RequestError = {
  message?: string;
  response?: { data?: { error?: string; message?: string; title?: string; errors?: Record<string, string[]> } };
};

// Shows the API's reason (custom error, or ASP.NET validation messages) instead of "status code 400"
const requestErrorMessage = (err: unknown): string => {
  const error = err as RequestError;
  const data = error.response?.data;
  const validation = data?.errors ? Object.values(data.errors).flat().join('\n') : '';
  return data?.error || data?.message || validation || data?.title || error.message || 'Request failed';
};

export default function ServersPage() {
  const { selectedClientId } = useClientStore();
  const [servers, setServers] = useState<ApplicationServer[]>([]);
  const [applications, setApplications] = useState<Application[]>([]);
  const [loading, setLoading] = useState(true);
  const [showDialog, setShowDialog] = useState(false);
  const [editingServer, setEditingServer] = useState<ApplicationServer | null>(null);
  const [testingServer, setTestingServer] = useState<number | null>(null);

  const [formData, setFormData] = useState({
    applicationId: 0,
    serverName: '',
    hostName: '',
    port: 22,
    username: '',
    password: '',
    deploymentPath: '',
    appPoolName: '',
    websiteName: '',
    backupPath: 'C:\\DeployVare\\Backups',
    serverType: 'Api' as 'Api' | 'Frontend',
  });

  useEffect(() => {
    if (selectedClientId) loadData();
  }, [selectedClientId]);

  const loadData = async () => {
    try {
      const [serversData, appsData] = await Promise.all([
        api.getApplicationServers(selectedClientId),
        api.getApplications(selectedClientId),
      ]);
      setServers(serversData);
      setApplications(appsData);
    } catch (err) {
      alert(`Error: ${requestErrorMessage(err)}`);
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.applicationId) {
      alert('Select an application for this server.');
      return;
    }
    // The port has its own field; "20.203.78.63:55000" would be looked up as a host name and fail
    const hostName = formData.hostName.trim();
    if (/^[^:\s]+:\d+$/.test(hostName) || hostName.includes('://') || /\s/.test(hostName)) {
      alert('Host Name must be only the IP address or DNS name (e.g. 20.203.78.63). Put the port in the SSH Port field.');
      return;
    }
    try {
      if (editingServer) {
        await api.updateApplicationServer(editingServer.id, formData);
      } else {
        await api.createApplicationServer(formData);
      }
      setShowDialog(false);
      resetForm();
      loadData();
    } catch (err) {
      alert(`Error: ${requestErrorMessage(err)}`);
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Are you sure you want to delete this server?')) return;

    try {
      await api.deleteApplicationServer(id);
      loadData();
    } catch (err) {
      alert(`Error: ${requestErrorMessage(err)}`);
    }
  };

  const handleTestConnection = async (id: number) => {
    setTestingServer(id);
    try {
      const result = await api.testServerConnection(id);
      alert(`✓ ${result.message}`);
    } catch (err) {
      alert(`✗ Connection failed: ${requestErrorMessage(err)}`);
    } finally {
      setTestingServer(null);
    }
  };

  const resetForm = () => {
    setFormData({
      applicationId: 0,
      serverName: '',
      hostName: '',
      port: 22,
      username: '',
      password: '',
      deploymentPath: '',
      appPoolName: '',
      websiteName: '',
      backupPath: 'C:\\DeployVare\\Backups',
      serverType: 'Api',
    });
    setEditingServer(null);
  };

  const openEditDialog = (server: ApplicationServer) => {
    setEditingServer(server);
    setFormData({
      applicationId: server.applicationId,
      serverName: server.serverName,
      hostName: server.hostName,
      port: server.port,
      username: server.username,
      password: '',
      deploymentPath: server.deploymentPath,
      appPoolName: server.appPoolName,
      websiteName: server.websiteName,
      backupPath: server.backupPath,
      serverType: server.serverType,
    });
    setShowDialog(true);
  };

  if (loading) {
    return (
      <ProtectedLayout requireClient>
        <LoadingState label="Loading servers..." />
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Application Servers"
          description="Manage Windows servers for API deployments"
          icon={Server}
          actions={
            <Button onClick={() => { resetForm(); setShowDialog(true); }}>
              <Plus className="h-4 w-4" />
              Add Server
            </Button>
          }
        />

        {/* Servers Grid */}
        {servers.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {servers.map((server) => (
              <Card key={server.id} className="hover:shadow-md transition-shadow">
                <CardHeader>
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex min-w-0 items-center gap-2">
                      <Server className="h-5 w-5 shrink-0 text-primary" />
                      <div className="min-w-0">
                        <CardTitle className="truncate">{server.serverName}</CardTitle>
                        <CardDescription className="text-xs">{server.applicationName}</CardDescription>
                      </div>
                    </div>
                    <div className="flex shrink-0 flex-wrap justify-end gap-1">
                      <Badge variant={server.isActive ? 'success' : 'secondary'}>
                        {server.isActive ? 'Active' : 'Inactive'}
                      </Badge>
                      <Badge variant={server.serverType === 'Frontend' ? 'default' : 'secondary'}>
                        {server.serverType}
                      </Badge>
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="space-y-3">
                  <div className="space-y-2 text-sm">
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">Host:</span>
                      <span className="font-mono text-xs">{server.hostName}:{server.port}</span>
                    </div>
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">Website:</span>
                      <span className="font-mono text-xs">{server.websiteName}</span>
                    </div>
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">App Pool:</span>
                      <span className="font-mono text-xs">{server.appPoolName}</span>
                    </div>
                    <div className="flex justify-between gap-2">
                      <span className="text-muted-foreground">Deployments:</span>
                      <span className="font-semibold">{server.totalDeployments}</span>
                    </div>
                    {server.lastDeploymentDate && (
                      <div className="flex justify-between gap-2">
                        <span className="text-muted-foreground">Last Deploy:</span>
                        <span className="text-xs">{new Date(server.lastDeploymentDate).toLocaleDateString()}</span>
                      </div>
                    )}
                  </div>

                  <div className="flex gap-2 border-t border-border pt-3">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => handleTestConnection(server.id)}
                      disabled={testingServer === server.id}
                      className="flex-1"
                    >
                      <TestTube className="h-3 w-3" />
                      {testingServer === server.id ? 'Testing...' : 'Test'}
                    </Button>
                    <Link href={`/server-setup?serverId=${server.id}`}>
                      <Button size="sm" variant="outline" title="Server setup">
                        <Wrench className="h-3 w-3" />
                        Setup
                      </Button>
                    </Link>
                    <Button
                      size="sm"
                      variant="outline"
                      title="Edit server"
                      onClick={() => openEditDialog(server)}
                    >
                      <Edit className="h-3 w-3" />
                    </Button>
                    <Button
                      size="sm"
                      variant="destructive"
                      title="Delete server"
                      onClick={() => handleDelete(server.id)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}

        {servers.length === 0 && (
          <EmptyState
            icon={Server}
            title="No servers configured"
            description="Add your first Windows server to start deploying"
            action={
              <Button onClick={() => { resetForm(); setShowDialog(true); }}>
                <Plus className="h-4 w-4" />
                Add Server
              </Button>
            }
          />
        )}

        {/* Add/Edit Dialog */}
        {showDialog && (
          <Dialog open={showDialog} onOpenChange={setShowDialog}>
            <DialogContent className="max-w-2xl">
              <DialogHeader>
                <DialogTitle>{editingServer ? 'Edit Server' : 'Add New Server'}</DialogTitle>
              </DialogHeader>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="application">Application *</Label>
                    <Select
                      id="application"
                      value={formData.applicationId || ''}
                      onChange={(e) => setFormData({ ...formData, applicationId: Number(e.target.value) })}
                      required
                    >
                      <option value="" disabled>Select application...</option>
                      {applications.map((app) => (
                        <option key={app.id} value={app.id}>{app.name}</option>
                      ))}
                    </Select>
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="serverName">Server Name *</Label>
                    <Input
                      id="serverName"
                      value={formData.serverName}
                      onChange={(e) => setFormData({ ...formData, serverName: e.target.value })}
                      placeholder="Production Server 1"
                      required
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label>Server Type *</Label>
                  <div className="flex flex-wrap gap-x-6 gap-y-2">
                    <label className="flex cursor-pointer items-center gap-2">
                      <input
                        type="radio"
                        name="serverType"
                        value="Api"
                        checked={formData.serverType === 'Api'}
                        onChange={() => setFormData({ ...formData, serverType: 'Api' })}
                      />
                      <span className="text-sm font-medium">API Server</span>
                      <span className="text-xs text-muted-foreground">(deploy .dll / .exe files)</span>
                    </label>
                    <label className="flex cursor-pointer items-center gap-2">
                      <input
                        type="radio"
                        name="serverType"
                        value="Frontend"
                        checked={formData.serverType === 'Frontend'}
                        onChange={() => setFormData({ ...formData, serverType: 'Frontend' })}
                      />
                      <span className="text-sm font-medium">Frontend Server</span>
                      <span className="text-xs text-muted-foreground">(deploy .zip build)</span>
                    </label>
                  </div>
                </div>

                <div className="grid gap-4 sm:grid-cols-3">
                  <div className="space-y-2 sm:col-span-2">
                    <Label htmlFor="hostName">Host Name / IP *</Label>
                    <Input
                      id="hostName"
                      value={formData.hostName}
                      onChange={(e) => setFormData({ ...formData, hostName: e.target.value })}
                      placeholder="192.168.1.100 or server.domain.com"
                      required
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="port">SSH Port *</Label>
                    <Input
                      id="port"
                      type="number"
                      value={formData.port}
                      onChange={(e) => setFormData({ ...formData, port: Number(e.target.value) })}
                      required
                    />
                  </div>
                </div>

                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="username">Username *</Label>
                    <Input
                      id="username"
                      value={formData.username}
                      onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                      placeholder="Administrator"
                      required
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="password">
                      Password {editingServer && '(leave blank to keep current)'}
                    </Label>
                    <Input
                      id="password"
                      type="password"
                      value={formData.password}
                      onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                      placeholder="••••••••"
                      required={!editingServer}
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="deploymentPath">Deployment Path *</Label>
                  <Input
                    id="deploymentPath"
                    value={formData.deploymentPath}
                    onChange={(e) => setFormData({ ...formData, deploymentPath: e.target.value })}
                    placeholder="C:\inetpub\wwwroot\MyApp"
                    required
                  />
                </div>

                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-2">
                    <Label htmlFor="websiteName">IIS Website Name *</Label>
                    <Input
                      id="websiteName"
                      value={formData.websiteName}
                      onChange={(e) => setFormData({ ...formData, websiteName: e.target.value })}
                      placeholder="MyWebsite"
                      required
                    />
                  </div>
                  <div className="space-y-2">
                    <Label htmlFor="appPoolName">IIS App Pool Name *</Label>
                    <Input
                      id="appPoolName"
                      value={formData.appPoolName}
                      onChange={(e) => setFormData({ ...formData, appPoolName: e.target.value })}
                      placeholder="MyAppPool"
                      required
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="backupPath">Backup Path</Label>
                  <Input
                    id="backupPath"
                    value={formData.backupPath}
                    onChange={(e) => setFormData({ ...formData, backupPath: e.target.value })}
                    placeholder="C:\DeployVare\Backups"
                  />
                </div>

                <div className="flex justify-end gap-2 border-t border-border pt-4">
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setShowDialog(false)}
                  >
                    Cancel
                  </Button>
                  <Button type="submit">
                    {editingServer ? 'Update Server' : 'Add Server'}
                  </Button>
                </div>
              </form>
            </DialogContent>
          </Dialog>
        )}
      </div>
    </ProtectedLayout>
  );
}
