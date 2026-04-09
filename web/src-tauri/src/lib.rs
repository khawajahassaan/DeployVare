use tauri::Manager;
use tauri_plugin_shell::process::CommandEvent;
use tauri_plugin_shell::ShellExt;

/// Holds the sidecar child process so it remains alive for the entire app
/// lifetime and is cleaned up when the Mutex is dropped on app exit.
#[allow(dead_code)]
struct ApiProcess(std::sync::Mutex<Option<tauri_plugin_shell::process::CommandChild>>);

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .setup(|app| {
            // Point the .NET API to the resource dir so it can read appsettings.json
            let resource_dir = app
                .path()
                .resource_dir()
                .expect("failed to resolve resource directory");

            let sidecar = app
                .shell()
                .sidecar("api-server")
                .expect("api-server sidecar not found – run `npm run sidecar:build` first")
                // Bind to loopback only; the frontend connects to this same port
                .env("ASPNETCORE_URLS", "http://127.0.0.1:5233")
                .env("ASPNETCORE_ENVIRONMENT", "Production")
                // Tell ASP.NET Core where to find appsettings.json (bundled as a Tauri resource)
                .env("ASPNETCORE_CONTENTROOT", resource_dir.to_string_lossy().as_ref())
                .env("DOTNET_CONTENTROOT", resource_dir.to_string_lossy().as_ref());

            let (mut rx, child) = sidecar
                .spawn()
                .expect("failed to start DeployVare API server");

            // Keep the child handle alive so the OS doesn't kill the process
            app.manage(ApiProcess(std::sync::Mutex::new(Some(child))));

            // Log API output to the Tauri console (visible in tauri dev, hidden in release)
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
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
