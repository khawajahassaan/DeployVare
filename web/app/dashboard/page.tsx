'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import api, { Application, DatabaseInstance, Deployment } from '@/lib/api';
import { FolderKanban, Database, Rocket, CheckCircle, XCircle, Clock } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import Link from 'next/link';
import { Button } from '@/components/ui/button';

export default function DashboardPage() {
  const [applications, setApplications] = useState<Application[]>([]);
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [deployments, setDeployments] = useState<Deployment[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [apps, insts, deploys] = await Promise.all([
        api.getApplications(),
        api.getDatabaseInstances(),
        api.getDeployments(1, 5),
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
          <h1 className="text-3xl font-bold">Dashboard</h1>
          <p className="text-muted-foreground">Overview of your deployment system</p>
        </div>

        {/* Stats */}
        <div className="grid gap-4 md:grid-cols-3">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Applications</CardTitle>
              <FolderKanban className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{applications.length}</div>
              <p className="text-xs text-muted-foreground">Total applications</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Database Instances</CardTitle>
              <Database className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{instances.length}</div>
              <p className="text-xs text-muted-foreground">Active instances</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">Deployments</CardTitle>
              <Rocket className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{deployments.length}</div>
              <p className="text-xs text-muted-foreground">Recent deployments</p>
            </CardContent>
          </Card>
        </div>

        {/* Recent Deployments */}
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
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
              <p className="text-center text-muted-foreground">Loading...</p>
            ) : deployments.length === 0 ? (
              <p className="text-center text-muted-foreground">No deployments yet</p>
            ) : (
              <div className="space-y-4">
                {deployments.map((deployment) => (
                  <div key={deployment.id} className="flex items-center justify-between pb-4">
                    <div className="space-y-1">
                      <p className="font-medium">
                        {deployment.applicationName} - {deployment.instanceName}
                      </p>
                      <p className="text-sm text-muted-foreground">
                        Patches {deployment.fromPatchNumber} → {deployment.toPatchNumber} by {deployment.deployedBy}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {formatDate(deployment.deployedAt)}
                      </p>
                    </div>
                    <div>{getStatusBadge(deployment.status)}</div>
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
                  <Rocket className="mr-2 h-4 w-4" />
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



