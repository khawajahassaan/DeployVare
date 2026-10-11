'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/store/authStore';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Rocket, ChevronDown, ChevronUp, AlertCircle } from 'lucide-react';
import { getConfig, saveConfig, restartApp, type AppConfig } from '@/lib/tauri';

export default function LoginPage() {
  const router = useRouter();
  const { login, isLoading } = useAuthStore();
  const [credentials, setCredentials] = useState({ username: '', password: '' });
  const [error, setError] = useState('');

  // DB settings state
  const [dbOpen, setDbOpen] = useState(false);
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveMsg, setSaveMsg] = useState('');

  useEffect(() => {
    getConfig().then(setConfig).catch(() => {});
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    try {
      await login(credentials);
      router.push('/dashboard');
    } catch (err: any) {
      console.error('Login error:', err);
      const errorMessage = err.response?.data?.message
        || err.response?.data
        || err.message
        || 'Login failed. Please check your credentials.';
      setError(errorMessage);
    }
  };

  const handleApplyRestart = async () => {
    if (!config) return;
    setSaving(true);
    setSaveMsg('');
    try {
      await saveConfig(config);
      setSaveMsg('Config saved — restarting…');
      await restartApp();
    } catch {
      setSaveMsg('Failed to save config.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 py-12 sm:px-6 lg:px-8">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1 text-center">
          <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-primary text-primary-foreground shadow-sm">
            <Rocket className="h-6 w-6" />
          </div>
          <CardTitle className="text-2xl font-semibold tracking-tight">DeployVare</CardTitle>
          <CardDescription>SQL Deployment Management System</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                <AlertCircle className="h-4 w-4 flex-shrink-0 mt-0.5" />
                <span>{error}</span>
              </div>
            )}
            <div className="space-y-2">
              <Label htmlFor="username">Username</Label>
              <Input
                id="username"
                type="text"
                placeholder="Enter your username"
                value={credentials.username}
                onChange={(e) => setCredentials({ ...credentials, username: e.target.value })}
                required
                disabled={isLoading}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                placeholder="Enter your password"
                value={credentials.password}
                onChange={(e) => setCredentials({ ...credentials, password: e.target.value })}
                required
                disabled={isLoading}
              />
            </div>
            <Button type="submit" className="w-full" disabled={isLoading}>
              {isLoading ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>

          {/* Database settings collapsible — only shown inside Tauri */}
          {config !== null && (
            <div className="overflow-hidden rounded-lg border border-border">
              <button
                type="button"
                className="flex w-full items-center justify-between px-4 py-3 text-sm font-medium text-foreground hover:bg-muted/50 transition-colors"
                onClick={() => setDbOpen((v) => !v)}
              >
                <span>Database Settings</span>
                {dbOpen ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
              </button>

              {dbOpen && (
                <div className="space-y-3 border-t border-border px-4 pb-4 pt-3">
                  {/* Mode tabs */}
                  <div className="flex overflow-hidden rounded-md border border-border text-sm">
                    {['local', 'sqlserver'].map((mode) => (
                      <button
                        key={mode}
                        type="button"
                        className={`flex-1 py-1.5 font-medium transition-colors ${
                          config.dbMode === mode
                            ? 'bg-primary text-primary-foreground'
                            : 'bg-card text-muted-foreground hover:bg-muted/50 hover:text-foreground'
                        }`}
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

                  {saveMsg && (
                    <p className="text-xs text-muted-foreground">{saveMsg}</p>
                  )}

                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="w-full"
                    disabled={saving}
                    onClick={handleApplyRestart}
                  >
                    {saving ? 'Saving…' : 'Apply & Restart'}
                  </Button>
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

