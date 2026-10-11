'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import ProtectedLayout from '@/components/ProtectedLayout';
import { useAuthStore } from '@/store/authStore';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { PageHeader } from '@/components/ui/page-header';
import { EmptyState } from '@/components/ui/empty-state';
import { Settings, Monitor, CheckCircle, AlertCircle } from 'lucide-react';
import { getConfig, saveConfig, restartApp, type AppConfig } from '@/lib/tauri';
import { cn } from '@/lib/utils';

export default function SettingsPage() {
  const router = useRouter();
  const { user } = useAuthStore();
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  // Redirect non-admins
  useEffect(() => {
    if (user && user.role !== 'Admin') router.replace('/dashboard');
  }, [user, router]);

  useEffect(() => {
    getConfig().then(setConfig).catch(() => {});
  }, []);

  const handleSave = async () => {
    if (!config) return;
    setSaving(true);
    setMsg(null);
    try {
      await saveConfig(config);
      setMsg({ text: 'Settings saved. Restart the app to apply changes.', ok: true });
    } catch {
      setMsg({ text: 'Failed to save settings.', ok: false });
    } finally {
      setSaving(false);
    }
  };

  const handleSaveRestart = async () => {
    if (!config) return;
    setSaving(true);
    setMsg(null);
    try {
      await saveConfig(config);
      setMsg({ text: 'Config saved — restarting…', ok: true });
      await restartApp();
    } catch {
      setMsg({ text: 'Failed to save or restart.', ok: false });
    } finally {
      setSaving(false);
    }
  };

  const header = (
    <PageHeader
      title="Settings"
      description="Configure data storage and backup options for this installation"
      icon={Settings}
    />
  );

  if (!config) {
    return (
      <ProtectedLayout>
        <div className="space-y-6">
          {header}
          <EmptyState
            icon={Monitor}
            title="Desktop only"
            description="Settings are only available inside the desktop application."
          />
        </div>
      </ProtectedLayout>
    );
  }

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        {header}

        <div className="grid gap-6 lg:grid-cols-2">
          {/* ── Database ─────────────────────────────────────── */}
          <Card className="self-start">
            <CardHeader>
              <CardTitle>Database</CardTitle>
              <CardDescription>
                Choose where DeployVare stores its data.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex overflow-hidden rounded-md border border-border text-sm">
                {['local', 'sqlserver'].map((mode) => (
                  <button
                    key={mode}
                    type="button"
                    className={cn(
                      'flex-1 py-2 font-medium transition-colors',
                      config.dbMode === mode
                        ? 'bg-primary text-primary-foreground'
                        : 'bg-card text-muted-foreground hover:bg-muted/50 hover:text-foreground'
                    )}
                    onClick={() => setConfig({ ...config, dbMode: mode })}
                  >
                    {mode === 'local' ? 'Local (SQLite)' : 'Cloud (SQL Server)'}
                  </button>
                ))}
              </div>

              {config.dbMode === 'sqlserver' && (
                <div className="space-y-2">
                  <Label htmlFor="connStr">Connection String</Label>
                  <Input
                    id="connStr"
                    type="text"
                    placeholder="Server=...;Database=...;User Id=...;Password=..."
                    value={config.connectionString}
                    onChange={(e) => setConfig({ ...config, connectionString: e.target.value })}
                  />
                </div>
              )}
            </CardContent>
          </Card>

          {/* ── Backblaze B2 ─────────────────────────────────── */}
          <Card className="self-start">
            <CardHeader>
              <CardTitle>Backblaze B2 Backup Storage</CardTitle>
              <CardDescription>
                When enabled, deployment backups are uploaded to B2 instead of the local filesystem.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex items-center gap-3">
                <input
                  id="b2Enabled"
                  type="checkbox"
                  className="h-4 w-4 accent-primary"
                  checked={config.b2Enabled}
                  onChange={(e) => setConfig({ ...config, b2Enabled: e.target.checked })}
                />
                <Label htmlFor="b2Enabled">Enable Backblaze B2</Label>
              </div>

              {config.b2Enabled && (
                <div className="space-y-3">
                  {[
                    { id: 'b2KeyId',   label: 'Key ID',         field: 'b2KeyId'   as const, placeholder: 'xxxxxxxxxxxxx' },
                    { id: 'b2AppKey',  label: 'Application Key', field: 'b2AppKey'  as const, placeholder: 'K000...' },
                    { id: 'b2Bucket',  label: 'Bucket Name',     field: 'b2Bucket'  as const, placeholder: 'my-bucket' },
                    { id: 'b2Endpoint', label: 'Endpoint URL',   field: 'b2Endpoint' as const, placeholder: 'https://s3.us-west-004.backblazeb2.com' },
                    { id: 'b2Region',  label: 'Region',          field: 'b2Region'  as const, placeholder: 'us-west-004' },
                  ].map(({ id, label, field, placeholder }) => (
                    <div key={id} className="space-y-1">
                      <Label htmlFor={id}>{label}</Label>
                      <Input
                        id={id}
                        type={field === 'b2AppKey' ? 'password' : 'text'}
                        placeholder={placeholder}
                        value={config[field]}
                        onChange={(e) => setConfig({ ...config, [field]: e.target.value })}
                      />
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        {/* ── Actions ──────────────────────────────────────── */}
        {msg && (
          <div
            className={cn(
              'flex items-start gap-2 rounded-lg border p-3 text-sm',
              msg.ok
                ? 'border-green-200 bg-green-50 text-green-700'
                : 'border-red-200 bg-red-50 text-red-700'
            )}
          >
            {msg.ok ? (
              <CheckCircle className="h-4 w-4 flex-shrink-0 mt-0.5" />
            ) : (
              <AlertCircle className="h-4 w-4 flex-shrink-0 mt-0.5" />
            )}
            <span>{msg.text}</span>
          </div>
        )}

        <div className="flex flex-wrap gap-3">
          <Button onClick={handleSave} disabled={saving} variant="outline">
            Save
          </Button>
          <Button onClick={handleSaveRestart} disabled={saving}>
            {saving ? 'Saving…' : 'Save & Restart'}
          </Button>
        </div>
      </div>
    </ProtectedLayout>
  );
}
