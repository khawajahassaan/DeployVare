'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/store/authStore';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import AuroraBackdrop from '@/components/AuroraBackdrop';
import { ChevronDown, ChevronUp, AlertCircle, Eye, EyeOff, LogIn, Loader2 } from 'lucide-react';
import { getConfig, saveConfig, restartApp, type AppConfig } from '@/lib/tauri';
import { cn } from '@/lib/utils';

export default function LoginPage() {
  const router = useRouter();
  const { login, isLoading } = useAuthStore();
  const [credentials, setCredentials] = useState({ username: '', password: '' });
  const [error, setError] = useState('');
  const [showPassword, setShowPassword] = useState(false);

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

  // Same split layout as the AuditVare / TransVare sign-in: animated brand panel on the
  // left (a banner on small screens), the form on a black page to the right
  return (
    <div className="relative flex min-h-screen flex-col bg-[#1A1A1D] md:grid md:h-screen md:grid-cols-[minmax(0,55%)_minmax(0,1fr)] md:px-3">
      <div className="relative h-[220px] shrink-0 overflow-hidden sm:h-[260px] md:mt-2.5 md:h-[97vh] md:rounded-[16px]">
        <AuroraBackdrop variant="dark" />

        <div className="relative z-[1] flex h-full items-center justify-between px-5 md:absolute md:inset-x-0 md:bottom-auto md:left-[10%] md:right-0 md:top-[25%] md:h-auto md:items-start md:px-0">
          <div className="min-w-0 max-w-[min(100%,420px)] pr-2 md:pr-0">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/brand/deployvare-logo-white.png"
              alt="DeployVare"
              width={268}
              height={38}
              className="h-8 w-auto max-w-full md:h-[38px]"
            />
            <p className="mt-1.5 text-[14px] font-normal leading-snug text-white/80 sm:text-[16px] md:mt-3 md:text-[22px]">
              Client onboarding, server setup and deployments. Managed from one place.
            </p>
          </div>

          {/* Arrow pill — desktop only */}
          <div className="hidden bg-[#1A1A1D] p-1.5 md:block md:w-[173px] md:rounded-l-full">
            <div className="flex size-[85px] items-center justify-center rounded-full bg-white/90 p-4">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src="/brand/auth-arrow.png" alt="" className="w-[56px]" />
            </div>
          </div>
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-5 pt-6 sm:px-6 md:h-full md:p-4">
        <div className="flex h-full w-full flex-col items-stretch justify-start md:items-center md:justify-center">
          <div className="mx-auto w-full max-w-[420px] md:max-w-[65%]">
            <form onSubmit={handleSubmit}>
              <h1 className="mb-3 text-[20px] font-normal text-white">Welcome to DeployVare.</h1>

              {error && (
                <div className="mb-3 flex items-start gap-2 rounded-[8px] border border-[#58181C] bg-[#2A1215] px-3 py-2 text-[13px] text-[#FF7875]">
                  <AlertCircle className="mt-0.5 size-4 flex-shrink-0" />
                  <span>{error}</span>
                </div>
              )}

              <div className="mb-4">
                <Label htmlFor="username" className="mb-1 inline-block text-white/65">
                  Username<span className="ml-0.5 text-[#FF4D4F]">*</span>
                </Label>
                <Input
                  id="username"
                  type="text"
                  placeholder="Enter your username"
                  value={credentials.username}
                  onChange={(e) => setCredentials({ ...credentials, username: e.target.value })}
                  required
                  disabled={isLoading}
                  className="dv-dark-field border-[#424242] bg-[#232327] text-white/85 placeholder:text-white/30 hover:border-[#8C8C8C] focus-visible:border-[#8C8C8C] disabled:border-[#424242] disabled:bg-[#232327] disabled:text-white/85"
                />
              </div>

              <div className="mb-5">
                <Label htmlFor="password" className="mb-1 inline-block text-white/65">
                  Password<span className="ml-0.5 text-[#FF4D4F]">*</span>
                </Label>
                <div className="relative">
                  <Input
                    id="password"
                    type={showPassword ? 'text' : 'password'}
                    placeholder="Enter your password"
                    value={credentials.password}
                    onChange={(e) => setCredentials({ ...credentials, password: e.target.value })}
                    required
                    disabled={isLoading}
                    className="pr-10 dv-dark-field border-[#424242] bg-[#232327] text-white/85 placeholder:text-white/30 hover:border-[#8C8C8C] focus-visible:border-[#8C8C8C] disabled:border-[#424242] disabled:bg-[#232327] disabled:text-white/85"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((v) => !v)}
                    className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-white/45 hover:text-white/85"
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                    tabIndex={-1}
                  >
                    {showPassword ? <Eye className="size-4" /> : <EyeOff className="size-4" />}
                  </button>
                </div>
              </div>

              <Button type="submit" className="mb-1 w-full bg-brand-blue hover:bg-brand-hover" disabled={isLoading}>
                {isLoading ? <Loader2 className="size-4 animate-spin" /> : <LogIn className="size-4" />}
                {isLoading ? 'Signing in…' : 'Login'}
              </Button>
            </form>

            {/* Database settings collapsible — only shown inside Tauri */}
            {config !== null && (
              <div className="mt-5 overflow-hidden rounded-[10px] border border-[#3A3A3F]">
                <button
                  type="button"
                  className="flex w-full items-center justify-between px-4 py-3 text-[14px] text-white/85 transition-colors hover:bg-white/5"
                  onClick={() => setDbOpen((v) => !v)}
                >
                  <span>Database Settings</span>
                  {dbOpen ? <ChevronUp className="size-4 text-white/45" /> : <ChevronDown className="size-4 text-white/45" />}
                </button>

                {dbOpen && (
                  <div className="space-y-3 border-t border-[#3A3A3F] px-4 pb-4 pt-3">
                    {/* Mode switch — AuditVare segmented control */}
                    <div className="flex gap-0.5 rounded-[8px] bg-white/10 p-[2px]">
                      {['local', 'sqlserver'].map((mode) => (
                        <button
                          key={mode}
                          type="button"
                          className={cn(
                            'h-7 flex-1 rounded-[6px] text-[13px] transition-colors',
                            config.dbMode === mode
                              ? 'bg-white text-brand-blue shadow-[0px_1px_1px_rgba(23,33,51,0.05),0px_1px_4px_-1px_rgba(23,33,51,0.07)]'
                              : 'text-white/65 hover:text-white'
                          )}
                          onClick={() => setConfig({ ...config, dbMode: mode })}
                        >
                          {mode === 'local' ? 'Local (SQLite)' : 'Cloud (SQL Server)'}
                        </button>
                      ))}
                    </div>

                    {config.dbMode === 'sqlserver' && (
                      <div>
                        <Label htmlFor="connStr" className="mb-1 inline-block text-white/65">Connection String</Label>
                        <Input
                          id="connStr"
                          type="text"
                          placeholder="Server=...;Database=...;User Id=...;Password=..."
                          value={config.connectionString}
                          onChange={(e) => setConfig({ ...config, connectionString: e.target.value })}
                          className="dv-dark-field border-[#424242] bg-[#232327] text-white/85 placeholder:text-white/30 hover:border-[#8C8C8C] focus-visible:border-[#8C8C8C] disabled:border-[#424242] disabled:bg-[#232327] disabled:text-white/85"
                        />
                      </div>
                    )}

                    {saveMsg && (
                      <p className="text-[12px] text-white/65">{saveMsg}</p>
                    )}

                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="w-full border-[#4096FF] text-[#4096FF]"
                      disabled={saving}
                      onClick={handleApplyRestart}
                    >
                      {saving ? 'Saving…' : 'Apply & Restart'}
                    </Button>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
