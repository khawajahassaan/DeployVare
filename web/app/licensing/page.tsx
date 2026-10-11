'use client';

import { useCallback, useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { LoadingState } from '@/components/ui/loading-state';
import api, { ActivationReview, License, LicensingFile, SigningKey } from '@/lib/api';
import { useClientStore } from '@/store/clientStore';
import { KeyRound, ShieldCheck, Download, Upload, Plus, ArrowRightLeft, Ban, Copy, Server, AlertTriangle } from 'lucide-react';
import { formatDate } from '@/lib/utils';
import { toast } from 'sonner';

const errorMessage = (err: unknown, fallback: string): string => {
  const data = (err as { response?: { data?: { message?: string; error?: string } } })?.response?.data;
  return data?.message || data?.error || fallback;
};

const downloadFile = (file: LicensingFile) => {
  const blob = new Blob([file.content], { type: 'application/json' });
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

const statusVariant = (status: string, isExpired: boolean) => {
  if (status === 'Revoked') return 'destructive' as const;
  if (isExpired) return 'warning' as const;
  if (status === 'Active') return 'success' as const;
  return 'secondary' as const;
};

export default function LicensingPage() {
  const { clients, selectedClientId } = useClientStore();

  const [signingKey, setSigningKey] = useState<SigningKey | null>(null);
  const [licenses, setLicenses] = useState<License[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const [isIssueOpen, setIsIssueOpen] = useState(false);
  const [issueForm, setIssueForm] = useState({ clientId: 0, product: 'AuditVare', expiresAt: '', notes: '' });
  const [issueError, setIssueError] = useState('');

  const [requestFile, setRequestFile] = useState('');
  const [review, setReview] = useState<ActivationReview | null>(null);
  const [activationError, setActivationError] = useState('');

  const load = useCallback(async () => {
    try {
      const [key, list] = await Promise.all([api.getSigningKey(), api.getLicenses(selectedClientId)]);
      setSigningKey(key);
      setLicenses(list);
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to load licences'));
    } finally {
      setLoading(false);
    }
  }, [selectedClientId]);

  useEffect(() => {
    load();
  }, [load]);

  const handleCreateKey = async () => {
    if (!confirm('Create the signing key on this machine? Keep an offline backup: licences cannot be re-signed without it.')) return;
    setBusy(true);
    try {
      setSigningKey(await api.createSigningKey());
      toast.success('Signing key created');
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to create the signing key'));
    } finally {
      setBusy(false);
    }
  };

  const openIssue = () => {
    const oneYear = new Date();
    oneYear.setFullYear(oneYear.getFullYear() + 1);
    setIssueForm({
      clientId: selectedClientId ?? clients[0]?.id ?? 0,
      product: 'AuditVare',
      expiresAt: oneYear.toISOString().slice(0, 10),
      notes: '',
    });
    setIssueError('');
    setIsIssueOpen(true);
  };

  const handleIssue = async (e: React.FormEvent) => {
    e.preventDefault();
    setIssueError('');
    setBusy(true);
    try {
      const license = await api.issueLicense({
        clientId: issueForm.clientId,
        product: issueForm.product.trim(),
        expiresAt: new Date(`${issueForm.expiresAt}T23:59:59Z`).toISOString(),
        notes: issueForm.notes.trim() || undefined,
      });
      setIsIssueOpen(false);
      await load();
      toast.success(`Licence ${license.licenseId} issued`);
      downloadFile(await api.getLicenseFile(license.id));
    } catch (err) {
      setIssueError(errorMessage(err, 'Failed to issue the licence'));
    } finally {
      setBusy(false);
    }
  };

  const handleReview = async () => {
    setActivationError('');
    setReview(null);
    setBusy(true);
    try {
      setReview(await api.reviewActivation(requestFile));
    } catch (err) {
      setActivationError(errorMessage(err, 'Could not read the activation request'));
    } finally {
      setBusy(false);
    }
  };

  const handleApprove = async () => {
    setActivationError('');
    setBusy(true);
    try {
      const approval = await api.approveActivation(requestFile);
      downloadFile(approval.responseFile);
      setRequestFile('');
      setReview(null);
      await load();
      toast.success(`${approval.license.licenseId} activated — send the response file back`);
    } catch (err) {
      setActivationError(errorMessage(err, 'Failed to approve the activation'));
    } finally {
      setBusy(false);
    }
  };

  const handleTransfer = async (license: License) => {
    const reason = prompt(`Release ${license.licenseId} from its current server so another can activate?\n\nReason (shown in the licence history):`);
    if (reason === null) return;
    try {
      await api.transferLicense(license.id, reason || undefined);
      await load();
      toast.success('Licence released — the new server can now activate');
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to transfer the licence'));
    }
  };

  const handleRevoke = async (license: License) => {
    const reason = prompt(`Revoke ${license.licenseId}? It can no longer be activated anywhere.\n\nReason:`);
    if (reason === null) return;
    try {
      await api.revokeLicense(license.id, reason || undefined);
      await load();
      toast.success('Licence revoked');
    } catch (err) {
      toast.error(errorMessage(err, 'Failed to revoke the licence'));
    }
  };

  const copyPublicKey = async () => {
    if (!signingKey?.publicKey) return;
    try {
      await navigator.clipboard.writeText(signingKey.publicKey);
      toast.success('Public key copied');
    } catch {
      toast.error('Could not copy the public key');
    }
  };

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <PageHeader
          title="Licensing"
          description="Issue licences for clients and approve activation requests from their servers"
          icon={ShieldCheck}
          actions={
            <Button onClick={openIssue} disabled={!signingKey?.exists || clients.length === 0}>
              <Plus className="h-4 w-4" />
              Issue Licence
            </Button>
          }
        />

        {loading ? (
          <LoadingState label="Loading licences..." />
        ) : (
          <div className="grid gap-6 lg:grid-cols-3">
            <div className="space-y-6 lg:col-span-2">
              {/* Activation requests */}
              <Card>
                <CardHeader>
                  <div className="flex items-center gap-2">
                    <Upload className="h-4 w-4 text-primary" />
                    <CardTitle>Activation request</CardTitle>
                  </div>
                  <CardDescription>
                    Paste the activation request file a client server produced. Check it first, then approve to get the
                    response file to email back.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-3">
                  <Textarea
                    value={requestFile}
                    onChange={(e) => setRequestFile(e.target.value)}
                    placeholder='{ "kind": "activation-request", ... }'
                    className="min-h-32 font-mono text-xs"
                    disabled={busy}
                  />
                  <div className="flex flex-wrap gap-2">
                    <Button variant="outline" onClick={handleReview} disabled={busy || !requestFile.trim()}>
                      Check request
                    </Button>
                    <Button onClick={handleApprove} disabled={busy || !review?.canApprove}>
                      <ShieldCheck className="h-4 w-4" />
                      Approve and download response
                    </Button>
                  </div>

                  {activationError && (
                    <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{activationError}</div>
                  )}

                  {review && (
                    <div
                      className={
                        review.canApprove
                          ? 'rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800'
                          : 'rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800'
                      }
                    >
                      <p className="font-medium">{review.message}</p>
                      <div className="mt-2 grid gap-x-4 gap-y-1 sm:grid-cols-2">
                        <div>Licence: <span className="font-mono text-xs">{review.licenseId}</span></div>
                        <div>Product: {review.product}</div>
                        <div>Client: {review.clientName ?? review.customerId}</div>
                        <div>Server: {review.machineName}</div>
                        <div className="sm:col-span-2">
                          Machine: <span className="font-mono text-xs">{review.machineFingerprint}</span>
                        </div>
                        {review.serverIdentity && (
                          <div className="sm:col-span-2 font-mono text-xs">{review.serverIdentity}</div>
                        )}
                      </div>
                    </div>
                  )}
                </CardContent>
              </Card>

              {/* Licences */}
              {licenses.length === 0 ? (
                <EmptyState
                  icon={KeyRound}
                  title="No licences issued yet"
                  description="Issue a licence for a client, then send the .lic file with the installer"
                  action={
                    <Button onClick={openIssue} disabled={!signingKey?.exists || clients.length === 0}>
                      <Plus className="h-4 w-4" />
                      Issue Licence
                    </Button>
                  }
                />
              ) : (
                <div className="space-y-4">
                  {licenses.map((license) => (
                    <Card key={license.id}>
                      <CardHeader>
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div className="min-w-0">
                            <CardTitle className="font-mono">{license.licenseId}</CardTitle>
                            <CardDescription>
                              {license.clientName} · {license.product} · expires {formatDate(license.expiresAt)}
                            </CardDescription>
                          </div>
                          <Badge variant={statusVariant(license.status, license.isExpired)}>
                            {license.isExpired ? 'Expired' : license.status}
                          </Badge>
                        </div>
                      </CardHeader>
                      <CardContent className="space-y-3">
                        {license.currentActivation ? (
                          <div className="rounded-lg border border-border p-3 text-sm">
                            <div className="flex items-center gap-2 font-medium">
                              <Server className="h-4 w-4 text-primary" />
                              {license.currentActivation.machineName ?? 'Activated server'}
                            </div>
                            <p className="mt-1 font-mono text-xs text-muted-foreground">
                              {license.currentActivation.machineFingerprint}
                            </p>
                            <p className="text-xs text-muted-foreground">
                              Activated {formatDate(license.currentActivation.activatedAt)} by {license.currentActivation.activatedBy}
                            </p>
                          </div>
                        ) : (
                          <p className="text-sm text-muted-foreground">Not activated on any server yet.</p>
                        )}

                        {license.previousActivations.length > 0 && (
                          <details className="text-xs text-muted-foreground">
                            <summary className="cursor-pointer">Previous servers ({license.previousActivations.length})</summary>
                            <ul className="mt-2 space-y-1">
                              {license.previousActivations.map((activation) => (
                                <li key={activation.id}>
                                  <span className="font-mono">{activation.machineFingerprint}</span>
                                  {activation.machineName ? ` · ${activation.machineName}` : ''} ·{' '}
                                  {activation.deactivationReason ?? 'released'}
                                </li>
                              ))}
                            </ul>
                          </details>
                        )}

                        <div className="flex flex-wrap gap-2">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={async () => {
                              try {
                                downloadFile(await api.getLicenseFile(license.id));
                              } catch (err) {
                                toast.error(errorMessage(err, 'Failed to build the licence file'));
                              }
                            }}
                          >
                            <Download className="h-4 w-4" />
                            Licence file
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => handleTransfer(license)}
                            disabled={!license.currentActivation}
                          >
                            <ArrowRightLeft className="h-4 w-4" />
                            Transfer
                          </Button>
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => handleRevoke(license)}
                            disabled={license.status === 'Revoked'}
                          >
                            <Ban className="h-4 w-4" />
                            Revoke
                          </Button>
                        </div>
                      </CardContent>
                    </Card>
                  ))}
                </div>
              )}
            </div>

            {/* Signing key */}
            <div className="space-y-6 self-start lg:sticky lg:top-24">
              <Card>
                <CardHeader>
                  <div className="flex items-center gap-2">
                    <KeyRound className="h-4 w-4 text-primary" />
                    <CardTitle>Signing key</CardTitle>
                  </div>
                  <CardDescription>
                    Licences and activations are signed with this key. It stays on this machine and is never part of any
                    installation package.
                  </CardDescription>
                </CardHeader>
                <CardContent className="space-y-3 text-sm">
                  {signingKey?.exists ? (
                    <>
                      <div>
                        <span className="text-muted-foreground">Key ID</span>
                        <p className="font-mono text-xs">{signingKey.keyId}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Algorithm</span>
                        <p className="font-mono text-xs">{signingKey.algorithm}</p>
                      </div>
                      <div>
                        <span className="text-muted-foreground">Public key (embed in AuditVare)</span>
                        <p className="mt-1 break-all rounded bg-muted p-2 font-mono text-[11px]">{signingKey.publicKey}</p>
                        <Button variant="outline" size="sm" className="mt-2" onClick={copyPublicKey}>
                          <Copy className="h-4 w-4" />
                          Copy public key
                        </Button>
                      </div>
                      <p className="text-xs text-muted-foreground">
                        Stored at <span className="font-mono">{signingKey.location}</span>. Restrict that folder to
                        administrators and keep an offline backup.
                      </p>
                    </>
                  ) : (
                    <>
                      <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-amber-800">
                        <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                        <p>
                          This installation has no signing key, so it can only view licences. Create the key here on your
                          own machine.
                        </p>
                      </div>
                      <p className="text-xs text-muted-foreground">
                        Expected at <span className="font-mono">{signingKey?.location}</span>
                      </p>
                      <Button onClick={handleCreateKey} disabled={busy}>
                        <KeyRound className="h-4 w-4" />
                        Create signing key
                      </Button>
                    </>
                  )}
                </CardContent>
              </Card>
            </div>
          </div>
        )}

        {/* Issue dialog */}
        <Dialog open={isIssueOpen} onOpenChange={setIsIssueOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Issue Licence</DialogTitle>
              <DialogDescription>One licence per client and module. The file downloads when it is issued.</DialogDescription>
            </DialogHeader>
            <form onSubmit={handleIssue} className="space-y-4">
              {issueError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{issueError}</div>
              )}
              <div className="space-y-2">
                <Label htmlFor="licenseClient">Client *</Label>
                <Select
                  id="licenseClient"
                  value={issueForm.clientId || ''}
                  onChange={(e) => setIssueForm({ ...issueForm, clientId: Number(e.target.value) })}
                  required
                  disabled={busy}
                >
                  <option value="" disabled>
                    Select a client...
                  </option>
                  {clients.map((client) => (
                    <option key={client.id} value={client.id}>
                      {client.name} ({client.clientCode})
                    </option>
                  ))}
                </Select>
              </div>
              <div className="space-y-2">
                <Label htmlFor="licenseProduct">Module *</Label>
                <Input
                  id="licenseProduct"
                  value={issueForm.product}
                  onChange={(e) => setIssueForm({ ...issueForm, product: e.target.value })}
                  placeholder="AuditVare"
                  required
                  disabled={busy}
                />
                <p className="text-xs text-muted-foreground">Each module is licensed separately, e.g. AuditVare.</p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="licenseExpiry">Expires on *</Label>
                <Input
                  id="licenseExpiry"
                  type="date"
                  value={issueForm.expiresAt}
                  onChange={(e) => setIssueForm({ ...issueForm, expiresAt: e.target.value })}
                  required
                  disabled={busy}
                />
                <p className="text-xs text-muted-foreground">After this date the server needs a renewed licence file.</p>
              </div>
              <div className="space-y-2">
                <Label htmlFor="licenseNotes">Notes</Label>
                <Input
                  id="licenseNotes"
                  value={issueForm.notes}
                  onChange={(e) => setIssueForm({ ...issueForm, notes: e.target.value })}
                  placeholder="e.g. partner name, contract reference"
                  disabled={busy}
                />
              </div>
              <div className="flex justify-end gap-2">
                <Button type="button" variant="outline" onClick={() => setIsIssueOpen(false)} disabled={busy}>
                  Cancel
                </Button>
                <Button type="submit" disabled={busy || !issueForm.clientId}>
                  {busy ? 'Issuing...' : 'Issue Licence'}
                </Button>
              </div>
            </form>
          </DialogContent>
        </Dialog>
      </div>
    </ProtectedLayout>
  );
}
