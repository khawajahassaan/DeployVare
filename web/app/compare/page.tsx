'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select } from '@/components/ui/select';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription } from '@/components/ui/dialog';
import { DiffViewer } from '@/components/DiffViewer';
import api, { DatabaseInstance, SchemaComparison, SchemaObjectDiff, ProcedureDetail } from '@/lib/api';
import { GitCompare, Table, Database, FunctionSquare, ChevronDown, ChevronRight, ArrowRight, ArrowLeft, Loader2, CheckCircle, AlertCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { toast } from 'sonner';

export default function ComparePage() {
  const [instances, setInstances] = useState<DatabaseInstance[]>([]);
  const [sourceInstanceId, setSourceInstanceId] = useState<number>(0);
  const [targetInstanceId, setTargetInstanceId] = useState<number>(0);
  const [comparing, setComparing] = useState(false);
  const [comparison, setComparison] = useState<SchemaComparison | null>(null);
  const [error, setError] = useState('');
  const [activeTab, setActiveTab] = useState<'tables' | 'procedures' | 'functions'>('tables');
  const [expandedObjects, setExpandedObjects] = useState<Set<string>>(new Set());
  
  // Lazy loading state
  const [loadingDetails, setLoadingDetails] = useState<Set<string>>(new Set());
  const [procedureDetails, setProcedureDetails] = useState<Map<string, ProcedureDetail>>(new Map());
  
  // Transfer dialog state
  const [isTransferDialogOpen, setIsTransferDialogOpen] = useState(false);
  const [transferring, setTransferring] = useState(false);
  const [selectedObject, setSelectedObject] = useState<{ objectName: string; objectType: string; schema: string } | null>(null);
  const [transferDirection, setTransferDirection] = useState<'SourceToTarget' | 'TargetToSource'>('SourceToTarget');

  useEffect(() => {
    loadInstances();
  }, []);

  const loadInstances = async () => {
    try {
      const data = await api.getDatabaseInstances();
      setInstances(data);
    } catch (error) {
      console.error('Failed to load instances:', error);
    }
  };

  const handleCompare = async () => {
    if (!sourceInstanceId || !targetInstanceId) {
      setError('Please select both source and target instances');
      return;
    }

    if (sourceInstanceId === targetInstanceId) {
      setError('Please select different instances to compare');
      return;
    }

    setError('');
    setComparing(true);
    setComparison(null);

    try {
      const result = await api.compareSchemas(sourceInstanceId, targetInstanceId);
      setComparison(result);
    } catch (err: any) {
      setError(err.response?.data?.message || 'Comparison failed');
    } finally {
      setComparing(false);
    }
  };

  const toggleObject = async (objectName: string, objectType: 'procedure' | 'function') => {
    const key = `${objectType}:${objectName}`;

    if (expandedObjects.has(key)) {
      // Collapse immediately
      setExpandedObjects(prev => {
        const next = new Set(prev);
        next.delete(key);
        return next;
      });
      return;
    }

    // Expand immediately so the card body (and loading spinner) become visible
    setExpandedObjects(prev => new Set(prev).add(key));

    // Lazy load procedure/function details if not already loaded
    if (!procedureDetails.has(key) && !loadingDetails.has(key)) {
      setLoadingDetails(prev => new Set(prev).add(key));

      try {
        const schema = objectName.includes('.') ? objectName.split('.')[0] : 'dbo';
        const name = objectName.includes('.') ? objectName.split('.')[1] : objectName;

        const detail = await api.getProcedureDetail(
          sourceInstanceId,
          targetInstanceId,
          name,
          schema
        );

        // Use functional update to avoid stale closure overwriting concurrent loads
        setProcedureDetails(prev => new Map(prev).set(key, detail));
      } catch (err: any) {
        console.error('Failed to load details:', err);
        toast.error('Failed to load details: ' + (err.response?.data?.message || err.message));
      } finally {
        // Use functional update to avoid stale closure
        setLoadingDetails(prev => {
          const next = new Set(prev);
          next.delete(key);
          return next;
        });
      }
    }
  };

  const openTransferDialog = (objectName: string, objectType: string, direction: 'SourceToTarget' | 'TargetToSource') => {
    const schema = objectName.includes('.') ? objectName.split('.')[0] : 'dbo';
    const name = objectName.includes('.') ? objectName.split('.')[1] : objectName;
    
    setSelectedObject({ objectName: name, objectType, schema });
    setTransferDirection(direction);
    setIsTransferDialogOpen(true);
  };

  const handleTransfer = async () => {
    if (!selectedObject) return;
    
    setTransferring(true);
    try {
      const result = await api.transferObject(
        sourceInstanceId,
        targetInstanceId,
        selectedObject.objectType,
        selectedObject.objectName,
        selectedObject.schema,
        transferDirection
      );
      
      if (result.success) {
        toast.success(result.message);
        setIsTransferDialogOpen(false);
        
        // Optionally, refresh the comparison
        // await handleCompare();
      } else {
        toast.error(result.errorMessage || result.message);
      }
    } catch (err: any) {
      console.error('Transfer failed:', err);
      toast.error('Transfer failed: ' + (err.response?.data?.message || err.message));
    } finally {
      setTransferring(false);
    }
  };

  const getDiffTypeBadge = (diffType: string) => {
    switch (diffType) {
      case 'Added':
        return <Badge variant="success">Added</Badge>;
      case 'Removed':
        return <Badge variant="destructive">Removed</Badge>;
      case 'Modified':
        return <Badge variant="warning">Modified</Badge>;
      case 'Identical':
        return <Badge variant="secondary">Identical</Badge>;
      default:
        return <Badge>{diffType}</Badge>;
    }
  };

  const renderDiffList = (diffs: SchemaObjectDiff[], icon: React.ReactNode, objectType: 'table' | 'procedure' | 'function') => {
    if (!diffs || diffs.length === 0) {
      return <p className="text-center text-muted-foreground py-8">No differences found</p>;
    }

    const showTransferButtons = objectType === 'procedure' || objectType === 'function';

    return (
      <div className="space-y-2">
        {diffs.map((diff, idx) => {
          const key = `${objectType}:${diff.objectName}`;
          const isExpanded = expandedObjects.has(key);
          const isLoading = loadingDetails.has(key);
          const detail = procedureDetails.get(key);

          return (
            <Card key={idx}>
              <CardHeader>
                <div className="flex items-center justify-between">
                  <div 
                    className="flex items-center gap-2 flex-1 cursor-pointer hover:text-primary transition-colors"
                    onClick={() => {
                      if (objectType === 'table') {
                        // Tables have no lazy-load endpoint — just toggle expand in place
                        setExpandedObjects(prev => {
                          const next = new Set(prev);
                          if (next.has(key)) next.delete(key); else next.add(key);
                          return next;
                        });
                      } else {
                        toggleObject(diff.objectName, objectType as 'procedure' | 'function');
                      }
                    }}
                  >
                    {isExpanded ? (
                      <ChevronDown className="h-4 w-4" />
                    ) : (
                      <ChevronRight className="h-4 w-4" />
                    )}
                    {icon}
                    <CardTitle className="text-base">{diff.objectName}</CardTitle>
                  </div>
                  <div className="flex items-center gap-2">
                    {getDiffTypeBadge(diff.diffType)}
                    {showTransferButtons && (
                      <>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => openTransferDialog(diff.objectName, objectType, 'SourceToTarget')}
                          title="Move to Target"
                        >
                          <ArrowRight className="h-4 w-4" />
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => openTransferDialog(diff.objectName, objectType, 'TargetToSource')}
                          title="Move to Source"
                        >
                          <ArrowLeft className="h-4 w-4" />
                        </Button>
                      </>
                    )}
                  </div>
                </div>
              </CardHeader>
              {isExpanded && (
                <CardContent>
                  {objectType === 'table' ? (
                    // Tables: show pre-computed column diff from the comparison result
                    diff.changes && diff.changes.length > 0 ? (
                      <DiffViewer changes={diff.changes} />
                    ) : (
                      <p className="text-sm text-muted-foreground">No changes available</p>
                    )
                  ) : isLoading ? (
                    <div className="flex items-center justify-center py-8">
                      <Loader2 className="h-6 w-6 animate-spin text-primary" />
                      <span className="ml-2 text-sm text-muted-foreground">Loading details...</span>
                    </div>
                  ) : detail ? (
                    <div className="space-y-4">
                      <DiffViewer changes={detail.diff.split('\n')} />
                    </div>
                  ) : diff.changes && diff.changes.length > 0 ? (
                    <DiffViewer changes={diff.changes} />
                  ) : (
                    <p className="text-sm text-muted-foreground">No detailed changes available</p>
                  )}
                </CardContent>
              )}
            </Card>
          );
        })}
      </div>
    );
  };

  const sourceInstance = instances.find((i) => i.id === sourceInstanceId);
  const targetInstance = instances.find((i) => i.id === targetInstanceId);

  const tabs = [
    {
      key: 'tables' as const,
      label: 'Tables',
      icon: <Table className="h-4 w-4" />,
      count: comparison?.tableDifferences?.length || 0,
    },
    {
      key: 'procedures' as const,
      label: 'Procedures',
      icon: <Database className="h-4 w-4" />,
      count: comparison?.procedureDifferences?.length || 0,
    },
    {
      key: 'functions' as const,
      label: 'Functions',
      icon: <FunctionSquare className="h-4 w-4" />,
      count: comparison?.functionDifferences?.length || 0,
    },
  ];

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold">Schema Comparison</h1>
          <p className="text-muted-foreground">
            Compare database schemas between two instances
          </p>
        </div>

        {/* Selection */}
        <Card>
          <CardHeader>
            <CardTitle>Select Instances to Compare</CardTitle>
            <CardDescription>Choose source and target database instances</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid md:grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label htmlFor="source">Source Instance</Label>
                <Select
                  id="source"
                  value={sourceInstanceId}
                  onChange={(e) => setSourceInstanceId(parseInt(e.target.value))}
                  disabled={comparing}
                >
                  <option value="0">Select source instance</option>
                  {instances.map((instance) => (
                    <option key={instance.id} value={instance.id}>
                      {instance.applicationName} - {instance.instanceName} (Patch #{instance.currentPatchNumber})
                    </option>
                  ))}
                </Select>
                {sourceInstance && (
                  <div className="rounded-lg ring-1 ring-gray-200 p-3 bg-muted/50 text-sm shadow-sm">
                    <p className="font-medium">{sourceInstance.applicationName}</p>
                    <p className="text-muted-foreground">{sourceInstance.instanceName}</p>
                    <p className="text-muted-foreground">Patch #{sourceInstance.currentPatchNumber}</p>
                  </div>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="target">Target Instance</Label>
                <Select
                  id="target"
                  value={targetInstanceId}
                  onChange={(e) => setTargetInstanceId(parseInt(e.target.value))}
                  disabled={comparing}
                >
                  <option value="0">Select target instance</option>
                  {instances.map((instance) => (
                    <option key={instance.id} value={instance.id}>
                      {instance.applicationName} - {instance.instanceName} (Patch #{instance.currentPatchNumber})
                    </option>
                  ))}
                </Select>
                {targetInstance && (
                  <div className="rounded-lg ring-1 ring-gray-200 p-3 bg-muted/50 text-sm shadow-sm">
                    <p className="font-medium">{targetInstance.applicationName}</p>
                    <p className="text-muted-foreground">{targetInstance.instanceName}</p>
                    <p className="text-muted-foreground">Patch #{targetInstance.currentPatchNumber}</p>
                  </div>
                )}
              </div>
            </div>

            {error && (
              <div className="rounded-md bg-destructive/15 p-3 text-sm text-destructive">
                {error}
              </div>
            )}

            <Button
              onClick={handleCompare}
              disabled={!sourceInstanceId || !targetInstanceId || comparing}
              className="w-full"
            >
              {comparing ? (
                'Comparing...'
              ) : (
                <>
                  <GitCompare className="mr-2 h-4 w-4" />
                  Compare Schemas
                </>
              )}
            </Button>
          </CardContent>
        </Card>

        {/* Comparison Results */}
        {comparison && (
          <Card>
            <CardHeader>
              <CardTitle>Comparison Results</CardTitle>
              <CardDescription>
                {comparison.source.instanceName} vs {comparison.target.instanceName}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {/* Tabs */}
              <div className="flex">
                {tabs.map((tab) => (
                  <button
                    key={tab.key}
                    onClick={() => setActiveTab(tab.key)}
                    className={cn(
                      'flex items-center gap-2 px-4 py-2 rounded-t-lg transition-colors',
                      activeTab === tab.key
                        ? 'bg-primary/10 text-primary font-medium'
                        : 'text-muted-foreground hover:text-foreground hover:bg-muted/50'
                    )}
                  >
                    {tab.icon}
                    {tab.label}
                    {tab.count > 0 && (
                      <Badge variant="secondary" className="ml-1">
                        {tab.count}
                      </Badge>
                    )}
                  </button>
                ))}
              </div>

              {/* Tab Content */}
              <div className="mt-4">
                {activeTab === 'tables' &&
                  renderDiffList(comparison.tableDifferences, <Table className="h-4 w-4 text-primary" />, 'table')}
                {activeTab === 'procedures' &&
                  renderDiffList(comparison.procedureDifferences, <Database className="h-4 w-4 text-primary" />, 'procedure')}
                {activeTab === 'functions' &&
                  renderDiffList(comparison.functionDifferences, <FunctionSquare className="h-4 w-4 text-primary" />, 'function')}
              </div>

              {/* Legend */}
              <div className="flex items-center justify-center gap-4 pt-4 text-sm">
                <div className="flex items-center gap-2">
                  <div className="w-4 h-4 bg-green-100 dark:bg-green-950/30 ring-1 ring-green-300 dark:ring-green-700 rounded shadow-sm" />
                  <span className="text-muted-foreground">In Source only <span className="text-xs text-muted-foreground/60">(would be added to Target)</span></span>
                </div>
                <div className="flex items-center gap-2">
                  <div className="w-4 h-4 bg-red-100 dark:bg-red-950/30 ring-1 ring-red-300 dark:ring-red-700 rounded shadow-sm" />
                  <span className="text-muted-foreground">In Target only <span className="text-xs text-muted-foreground/60">(would be removed from Target)</span></span>
                </div>
                <div className="flex items-center gap-2">
                  <div className="w-4 h-4 bg-muted ring-1 ring-gray-300 rounded shadow-sm" />
                  <span className="text-muted-foreground">Unchanged</span>
                </div>
              </div>
            </CardContent>
          </Card>
        )}
      </div>

      {/* Transfer Object Dialog */}
      <Dialog open={isTransferDialogOpen} onOpenChange={setIsTransferDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Transfer Database Object</DialogTitle>
            <DialogDescription>
              Transfer {selectedObject?.schema}.{selectedObject?.objectName} between databases
            </DialogDescription>
          </DialogHeader>
          
          <div className="space-y-4 py-4">
            <div className="rounded-lg bg-muted/50 p-4 space-y-2">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">Source Database</p>
                  <p className="text-xs text-muted-foreground">
                    {transferDirection === 'SourceToTarget' 
                      ? sourceInstance?.instanceName 
                      : targetInstance?.instanceName}
                  </p>
                </div>
                <ArrowRight className="h-5 w-5 text-primary" />
                <div>
                  <p className="text-sm font-medium">Target Database</p>
                  <p className="text-xs text-muted-foreground">
                    {transferDirection === 'SourceToTarget' 
                      ? targetInstance?.instanceName 
                      : sourceInstance?.instanceName}
                  </p>
                </div>
              </div>
            </div>

            <div className="rounded-lg bg-blue-50 dark:bg-blue-950/20 p-3 text-sm">
              <div className="flex items-start gap-2">
                <CheckCircle className="h-4 w-4 text-blue-600 dark:text-blue-400 mt-0.5 flex-shrink-0" />
                <p className="text-blue-900 dark:text-blue-100">
                  The script will be transformed to <code className="font-mono bg-blue-100 dark:bg-blue-900/50 px-1 py-0.5 rounded">CREATE OR ALTER</code> to ensure idempotency.
                </p>
              </div>
            </div>

            <div className="rounded-lg bg-yellow-50 dark:bg-yellow-950/20 p-3 text-sm">
              <div className="flex items-start gap-2">
                <AlertCircle className="h-4 w-4 text-yellow-600 dark:text-yellow-400 mt-0.5 flex-shrink-0" />
                <p className="text-yellow-900 dark:text-yellow-100">
                  This action will modify the target database. This transfer will be recorded in the deployment history.
                </p>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button 
              variant="outline" 
              onClick={() => setIsTransferDialogOpen(false)}
              disabled={transferring}
            >
              Cancel
            </Button>
            <Button 
              onClick={handleTransfer}
              disabled={transferring}
            >
              {transferring ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Transferring...
                </>
              ) : (
                <>
                  <ArrowRight className="mr-2 h-4 w-4" />
                  Transfer
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </ProtectedLayout>
  );
}

