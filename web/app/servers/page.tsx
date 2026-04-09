'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Dialog } from '@/components/ui/dialog';
import api, { Application, ApplicationServer } from '@/lib/api';
import { Server, Plus, Trash2, TestTube, Edit } from 'lucide-react';

export default function ServersPage() {
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
  });

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [serversData, appsData] = await Promise.all([
        api.getApplicationServers(),
        api.getApplications(),
      ]);
      setServers(serversData);
      setApplications(appsData);
    } catch (error: any) {
      console.error('Error loading data:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingServer) {
        await api.updateApplicationServer(editingServer.id, formData);
      } else {
        await api.createApplicationServer(formData);
      }
      setShowDialog(false);
      resetForm();
      loadData();
    } catch (error: any) {
      console.error('Error saving server:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Are you sure you want to delete this server?')) return;
    
    try {
      await api.deleteApplicationServer(id);
      loadData();
    } catch (error: any) {
      console.error('Error deleting server:', error);
      alert(`Error: ${error.response?.data?.error || error.message}`);
    }
  };

  const handleTestConnection = async (id: number) => {
    setTestingServer(id);
    try {
      const result = await api.testServerConnection(id);
      alert(`✓ ${result.message}`);
    } catch (error: any) {
      alert(`✗ Connection failed: ${error.response?.data?.error || error.message}`);
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
    });
    setShowDialog(true);
  };

  if (loading) {
    return (
      <ProtectedLayout>
        <div className="flex items-center justify-center min-h-screen">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto"></div>
            <p className="mt-4 text-muted-foreground">Loading servers...</p>
          </div>
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold tracking-tight">Application Servers</h1>
            <p className="text-muted-foreground">
              Manage Windows servers for API deployments
            </p>
          </div>
          <Button onClick={() => { resetForm(); setShowDialog(true); }}>
            <Plus className="mr-2 h-4 w-4" />
            Add Server
          </Button>
        </div>

        {/* Servers Grid */}
        <div className="grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          {servers.map((server) => (
            <Card key={server.id}>
              <CardHeader>
                <div className="flex items-start justify-between">
                  <div className="flex items-center gap-2">
                    <Server className="h-5 w-5 text-primary" />
                    <div>
                      <CardTitle className="text-lg">{server.serverName}</CardTitle>
                      <CardDescription className="text-xs">{server.applicationName}</CardDescription>
                    </div>
                  </div>
                  <Badge variant={server.isActive ? 'default' : 'secondary'}>
                    {server.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Host:</span>
                    <span className="font-mono text-xs">{server.hostName}:{server.port}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Website:</span>
                    <span className="font-mono text-xs">{server.websiteName}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">App Pool:</span>
                    <span className="font-mono text-xs">{server.appPoolName}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-muted-foreground">Deployments:</span>
                    <span className="font-semibold">{server.totalDeployments}</span>
                  </div>
                  {server.lastDeploymentDate && (
                    <div className="flex justify-between">
                      <span className="text-muted-foreground">Last Deploy:</span>
                      <span className="text-xs">{new Date(server.lastDeploymentDate).toLocaleDateString()}</span>
                    </div>
                  )}
                </div>

                <div className="flex gap-2 pt-2">
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => handleTestConnection(server.id)}
                    disabled={testingServer === server.id}
                    className="flex-1"
                  >
                    <TestTube className="mr-1 h-3 w-3" />
                    {testingServer === server.id ? 'Testing...' : 'Test'}
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => openEditDialog(server)}
                  >
                    <Edit className="h-3 w-3" />
                  </Button>
                  <Button
                    size="sm"
                    variant="destructive"
                    onClick={() => handleDelete(server.id)}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>

        {servers.length === 0 && (
          <Card>
            <CardContent className="flex flex-col items-center justify-center py-16">
              <Server className="h-12 w-12 text-muted-foreground mb-4" />
              <h3 className="text-lg font-semibold mb-2">No servers configured</h3>
              <p className="text-muted-foreground mb-4">Add your first Windows server to start deploying</p>
              <Button onClick={() => { resetForm(); setShowDialog(true); }}>
                <Plus className="mr-2 h-4 w-4" />
                Add Server
              </Button>
            </CardContent>
          </Card>
        )}

        {/* Add/Edit Dialog */}
        {showDialog && (
          <Dialog open={showDialog} onOpenChange={setShowDialog}>
            <div className="fixed inset-0 bg-black/50 flex items-center justify-center p-4 z-50">
              <div className="bg-white rounded-lg max-w-2xl w-full max-h-[90vh] overflow-y-auto">
                <div className="p-6">
                  <h2 className="text-2xl font-bold mb-6">
                    {editingServer ? 'Edit Server' : 'Add New Server'}
                  </h2>
                  <form onSubmit={handleSubmit} className="space-y-4">
                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <Label htmlFor="application">Application *</Label>
                        <select
                          id="application"
                          className="w-full border rounded-md p-2"
                          value={formData.applicationId}
                          onChange={(e) => setFormData({ ...formData, applicationId: Number(e.target.value) })}
                          required
                        >
                          <option value={0}>Select application...</option>
                          {applications.map((app) => (
                            <option key={app.id} value={app.id}>{app.name}</option>
                          ))}
                        </select>
                      </div>
                      <div>
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

                    <div className="grid grid-cols-3 gap-4">
                      <div className="col-span-2">
                        <Label htmlFor="hostName">Host Name / IP *</Label>
                        <Input
                          id="hostName"
                          value={formData.hostName}
                          onChange={(e) => setFormData({ ...formData, hostName: e.target.value })}
                          placeholder="192.168.1.100 or server.domain.com"
                          required
                        />
                      </div>
                      <div>
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

                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <Label htmlFor="username">Username *</Label>
                        <Input
                          id="username"
                          value={formData.username}
                          onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                          placeholder="Administrator"
                          required
                        />
                      </div>
                      <div>
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

                    <div>
                      <Label htmlFor="deploymentPath">Deployment Path *</Label>
                      <Input
                        id="deploymentPath"
                        value={formData.deploymentPath}
                        onChange={(e) => setFormData({ ...formData, deploymentPath: e.target.value })}
                        placeholder="C:\inetpub\wwwroot\MyApp"
                        required
                      />
                    </div>

                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <Label htmlFor="websiteName">IIS Website Name *</Label>
                        <Input
                          id="websiteName"
                          value={formData.websiteName}
                          onChange={(e) => setFormData({ ...formData, websiteName: e.target.value })}
                          placeholder="MyWebsite"
                          required
                        />
                      </div>
                      <div>
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

                    <div>
                      <Label htmlFor="backupPath">Backup Path</Label>
                      <Input
                        id="backupPath"
                        value={formData.backupPath}
                        onChange={(e) => setFormData({ ...formData, backupPath: e.target.value })}
                        placeholder="C:\DeployVare\Backups"
                      />
                    </div>

                    <div className="flex gap-3 pt-4">
                      <Button type="submit" className="flex-1">
                        {editingServer ? 'Update Server' : 'Add Server'}
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => setShowDialog(false)}
                        className="flex-1"
                      >
                        Cancel
                      </Button>
                    </div>
                  </form>
                </div>
              </div>
            </div>
          </Dialog>
        )}
      </div>
    </ProtectedLayout>
  );
}





