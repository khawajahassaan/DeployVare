/**
 * Tauri invoke helpers with browser-safe guards.
 * Every function is a no-op (or returns a sensible default) when running in a
 * plain browser so that the Next.js dev-server and production web build both work.
 */

const isTauri = (): boolean =>
  typeof window !== 'undefined' && '__TAURI__' in window;

// ---------------------------------------------------------------------------
// Config shape (must match AppConfig in src-tauri/src/lib.rs)
// ---------------------------------------------------------------------------

export interface AppConfig {
  dbMode: string;           // "local" | "sqlserver"
  connectionString: string; // only used when dbMode === "sqlserver"
  b2Enabled: boolean;
  b2KeyId: string;
  b2AppKey: string;
  b2Bucket: string;
  b2Endpoint: string;
  b2Region: string;
}

const defaultConfig: AppConfig = {
  dbMode: 'local',
  connectionString: '',
  b2Enabled: false,
  b2KeyId: '',
  b2AppKey: '',
  b2Bucket: '',
  b2Endpoint: '',
  b2Region: '',
};

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

export async function getConfig(): Promise<AppConfig> {
  if (!isTauri()) return { ...defaultConfig };
  const { invoke } = await import('@tauri-apps/api/core');
  return invoke<AppConfig>('get_config');
}

export async function saveConfig(config: AppConfig): Promise<void> {
  if (!isTauri()) return;
  const { invoke } = await import('@tauri-apps/api/core');
  return invoke('save_config', { config });
}

export async function restartApp(): Promise<void> {
  if (!isTauri()) return;
  const { invoke } = await import('@tauri-apps/api/core');
  return invoke('restart_app');
}
