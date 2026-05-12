use std::fs;
use std::path::PathBuf;
use tauri::{AppHandle, Manager, RunEvent};
use tauri_plugin_shell::process::CommandEvent;
use tauri_plugin_shell::ShellExt;

// ---------------------------------------------------------------------------
// App configuration – persisted to {app_config_dir}/deployvare-config.json
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct AppConfig {
    db_mode: String,          // "local" | "sqlserver"
    connection_string: String, // blank for local (SQLite path derived at runtime)
    b2_enabled: bool,
    b2_key_id: String,
    b2_app_key: String,
    b2_bucket: String,
    b2_endpoint: String,
    b2_region: String,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            db_mode: "local".into(),
            connection_string: String::new(),
            b2_enabled: false,
            b2_key_id: String::new(),
            b2_app_key: String::new(),
            b2_bucket: String::new(),
            b2_endpoint: String::new(),
            b2_region: String::new(),
        }
    }
}

fn config_path(app: &AppHandle) -> PathBuf {
    app.path()
        .app_config_dir()
        .expect("failed to resolve app config dir")
        .join("deployvare-config.json")
}

fn load_config(app: &AppHandle) -> AppConfig {
    let path = config_path(app);
    if path.exists() {
        fs::read_to_string(&path)
            .ok()
            .and_then(|s| serde_json::from_str(&s).ok())
            .unwrap_or_default()
    } else {
        AppConfig::default()
    }
}

fn save_config_file(app: &AppHandle, config: &AppConfig) -> Result<(), String> {
    let path = config_path(app);
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    let json = serde_json::to_string_pretty(config).map_err(|e| e.to_string())?;
    fs::write(&path, json).map_err(|e| e.to_string())
}

// ---------------------------------------------------------------------------
// Sidecar child process state
// ---------------------------------------------------------------------------

struct ApiProcess(std::sync::Mutex<Option<tauri_plugin_shell::process::CommandChild>>);

// ---------------------------------------------------------------------------
// Tauri commands
// ---------------------------------------------------------------------------

#[tauri::command]
fn get_config(app: AppHandle) -> AppConfig {
    load_config(&app)
}

#[tauri::command]
fn save_config(app: AppHandle, config: AppConfig) -> Result<(), String> {
    save_config_file(&app, &config)
}

#[tauri::command]
fn restart_app(app: AppHandle) {
    // Kill the sidecar first so the new instance can bind to port 5233.
    // app.restart() calls std::process::exit() which bypasses RunEvent::ExitRequested,
    // so the sidecar would otherwise stay alive as an orphan and block the port.
    if let Some(state) = app.try_state::<ApiProcess>() {
        if let Ok(mut guard) = state.0.lock() {
            if let Some(child) = guard.take() {
                let _ = child.kill();
            }
        }
    }
    // Brief pause so the OS can reclaim the port before the new process spawns.
    std::thread::sleep(std::time::Duration::from_millis(400));
    app.restart();
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![get_config, save_config, restart_app])
        .setup(|app| {
            let config = load_config(app.handle());

            // Derive DB env vars from the loaded config
            let (db_provider, db_conn_str) = if config.db_mode == "sqlserver" && !config.connection_string.is_empty() {
                ("sqlserver".to_string(), config.connection_string.clone())
            } else {
                let data_dir = app
                    .path()
                    .app_data_dir()
                    .expect("failed to resolve app data dir");
                fs::create_dir_all(&data_dir).ok();
                let sqlite_path = data_dir.join("deployvare.db");
                (
                    "sqlite".to_string(),
                    format!("Data Source={}", sqlite_path.to_string_lossy()),
                )
            };

            // Point the .NET API to the resource dir so it can find appsettings.json
            let resource_dir = app
                .path()
                .resource_dir()
                .expect("failed to resolve resource directory");

            let mut sidecar_cmd = app
                .shell()
                .sidecar("api-server")
                .map_err(|e| format!("api-server sidecar not found: {e}"))?
                .env("ASPNETCORE_URLS", "http://127.0.0.1:5233")
                .env("ASPNETCORE_ENVIRONMENT", "Production")
                .env("ASPNETCORE_CONTENTROOT", resource_dir.to_string_lossy().as_ref())
                .env("DOTNET_CONTENTROOT", resource_dir.to_string_lossy().as_ref())
                .env("DB_PROVIDER", &db_provider)
                .env("DB_CONNECTION_STRING", &db_conn_str);

            // Inject B2 credentials if enabled
            if config.b2_enabled {
                sidecar_cmd = sidecar_cmd
                    .env("B2_ENABLED", "true")
                    .env("B2_KEY_ID", &config.b2_key_id)
                    .env("B2_APP_KEY", &config.b2_app_key)
                    .env("B2_BUCKET", &config.b2_bucket)
                    .env("B2_ENDPOINT", &config.b2_endpoint)
                    .env("B2_REGION", &config.b2_region);
            }

            let (mut rx, child) = sidecar_cmd
                .spawn()
                .map_err(|e| format!("failed to start DeployVare API server: {e}"))?;

            app.manage(ApiProcess(std::sync::Mutex::new(Some(child))));

            tauri::async_runtime::spawn(async move {
                while let Some(event) = rx.recv().await {
                    match event {
                        CommandEvent::Stdout(bytes) => {
                            print!("[API] {}", String::from_utf8_lossy(&bytes));
                        }
                        CommandEvent::Stderr(bytes) => {
                            eprint!("[API ERR] {}", String::from_utf8_lossy(&bytes));
                        }
                        CommandEvent::Terminated(payload) => {
                            eprintln!("[API] process exited with code {:?}", payload.code);
                            break;
                        }
                        _ => {}
                    }
                }
            });

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app_handle, event| {
            if let RunEvent::ExitRequested { .. } = event {
                // Kill the sidecar API server when the app is closing
                if let Some(state) = app_handle.try_state::<ApiProcess>() {
                    if let Ok(mut guard) = state.0.lock() {
                        if let Some(child) = guard.take() {
                            let _ = child.kill();
                            eprintln!("[API] process killed on app exit");
                        }
                    }
                }
            }
        });
}
