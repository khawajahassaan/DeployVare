'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useClientStore } from '@/store/clientStore';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { Application, DatabaseInstance, Deployment } from '@/lib/api';
import { FolderKanban, Database, Rocket, CheckCircle, XCircle, Clock, Home } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import Link from 'next/link';
import { Button } from '@/components/ui/button';

export default function DashboardPage() {
  const { clients, selectedClientId } = useClientStore();
  const selectedClient = clients.find((c) => c.id === selectedClientId);
  const [applications, setApplications] = useState<Application[]>([]);
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (selectedClientId) loadData();
  }, [selectedClientId]);

  const loadData = async () => {
    try {
      const [apps, insts, deploys] = await Promise.all([
        api.getApplications(selectedClientId),
        api.getDatabaseInstances(selectedClientId),
        api.getDeployments(1, 5, selectedClientId),
      ]);
      setApplications(apps);
      setInstances(insts);
      setDeployments(deploys.data);
    } catch (error) {
      console.error('Failed to load dashboard data:', error);
    } finally {
      setLoading(false);
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

  const stats = [
    { title: 'Applications', value: applications.length, caption: 'Total applications', icon: FolderKanban },
    { title: 'Database Instances', value: instances.length, caption: 'Active instances', icon: Database },
    { title: 'Deployments', value: deployments.length, caption: 'Recent deployments', icon: Rocket },
  ];

  return (
    <ProtectedLayout requireClient>
      <div className="space-y-6">
        <PageHeader
          title="Dashboard"
          description={`Overview of your deployment system${selectedClient ? ` for ${selectedClient.name}` : ''}`}
          icon={Home}
        />

        {/* Stats */}
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {stats.map(({ title, value, caption, icon: Icon }) => (
            <Card key={title}>
              <CardContent className="flex items-center gap-4 p-5">
                <div className="rounded-[8px] bg-accent p-2 text-brand-blue">
                  <Icon className="h-5 w-5" />
                </div>
                <div className="min-w-0">
                  <p className="text-sm font-medium text-muted-foreground">{title}</p>
                  <div className="text-[24px] font-normal leading-tight text-ink">{value}</div>
                  <p className="text-xs text-muted-foreground">{caption}</p>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>

        {/* Recent Deployments */}
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between gap-4">
              <div>
                <CardTitle>Recent Deployments</CardTitle>
                <CardDescription>Latest deployment activities</CardDescription>
              </div>
              <Link href="/history">
                <Button variant="outline" size="sm">View All</Button>
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            {loading ? (
              <LoadingState className="py-8" label="Loading..." />
            ) : deployments.length === 0 ? (
              <EmptyState compact icon={Rocket} title="No deployments yet" />
            ) : (
              <div className="divide-y divide-border">
                {deployments.map((deployment) => (
                  <div
                    key={deployment.id}
                    className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
                  >
                    <div className="min-w-0 space-y-1">
                      <p className="font-medium text-foreground">
                        {deployment.applicationName} - {deployment.instanceName}
                      </p>
                      <p className="text-sm text-muted-foreground">
                        Patches {deployment.fromPatchNumber} → {deployment.toPatchNumber} by {deployment.deployedBy}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {formatDate(deployment.deployedAt)}
                      </p>
                    </div>
                    <div className="shrink-0">{getStatusBadge(deployment.status)}</div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Quick Actions */}
        <div className="grid gap-4 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Quick Deploy</CardTitle>
              <CardDescription>Deploy SQL scripts to your databases</CardDescription>
            </CardHeader>
            <CardContent>
              <Link href="/deploy">
                <Button className="w-full">
                  <Rocket className="h-4 w-4" />
                  Start Deployment
                </Button>
              </Link>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Compare Schemas</CardTitle>
              <CardDescription>Compare database schemas side by side</CardDescription>
            </CardHeader>
            <CardContent>
              <Link href="/compare">
                <Button variant="outline" className="w-full">
                  Compare Databases
                </Button>
              </Link>
            </CardContent>
          </Card>
        </div>
      </div>
    </ProtectedLayout>
  );
}
