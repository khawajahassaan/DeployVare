# Deploying to Linux with DeployVare — a guide for Windows/IIS people

This guide explains how a .NET backend and a static frontend run on Linux, what DeployVare's **Linux Deployment**
module does for you, and how to do the same by hand. Target servers: **Ubuntu 22.04/24.04 or Debian 12** (anything
with `apt-get` and `systemd`).

---

## 1. Crash course: how Linux hosting differs from IIS

On Windows, **IIS** receives every request. It runs your API inside an **application pool** (`w3wp.exe`, via the
ASP.NET Core Module) and serves the frontend's files from a site folder.

On Linux, three separate programs do those jobs:

```
Browser ──HTTPS──▶ Nginx (ports 80/443) ──┬─ /            → static files in /var/www/<app>-web   (frontend)
                                          └─ api / /api   → http://127.0.0.1:5000                (backend)
                                                              │
                                              Kestrel: your app, run by systemd as a service
                                              (dotnet App.dll), restarted on crash and at boot
```

| Windows (IIS) | Linux | Where |
|---|---|---|
| IIS website + bindings | Nginx (or Apache) "site" | `/etc/nginx/sites-available/<site>` |
| Application pool (`w3wp.exe`) | **systemd service** running Kestrel | `/etc/systemd/system/<service>.service` |
| ASP.NET Core Module | Nginx **reverse proxy** to `127.0.0.1:5000` | inside the Nginx site |
| `C:\inetpub\wwwroot\app` | `/var/www/app` | |
| App pool identity + NTFS rights | a Linux user (`www-data`) + `chown`/`chmod` | |
| `services.msc`, IIS Manager | `systemctl status / restart / enable` | |
| Event Viewer | `journalctl -u <service>` | |
| `web.config` settings | `appsettings.json` + environment in the systemd unit | |
| Windows authentication to SQL Server | **SQL login** in the connection string | Linux has no Windows auth |
| `.pfx` in the certificate store | PEM `fullchain.pem` + `privkey.pem` files | e.g. from certbot |

Key ideas:

- **Kestrel** is the web server built into every ASP.NET Core app. It listens only on `127.0.0.1:5000`, so it is
  never exposed to the internet directly.
- **systemd** is the Linux service manager (like the Windows Service Control Manager). It starts the app at boot,
  restarts it if it crashes and collects its console output into the journal.
- **Nginx** is the front door. It handles ports 80/443 and HTTPS, serves the frontend files and forwards API requests
  to Kestrel.

---

## 2. Using the Linux Deployment module

Sidebar → **Linux → Linux Deployment**.

1. **Application and environment** (Dev/UAT): uses the same Azure DevOps artifact sources as Deploy App.
2. **Architecture**
   - **Front End**: static files served by Nginx.
   - **Back End**: the .NET API as a systemd service behind Nginx.
   - **Two-Tier**: API (which also reaches the database) + frontend, on one server or two.
   - **Three-Tier**: frontend + API + a separate SQL Server. A manual download adds a third, *database* kit.
     An automated deployment runs the migrations from the API server.
3. **Deployment mode**
   - **Automated Deployment**: enter the SSH login(s), press **Deploy**. DeployVare downloads the Linux build,
     uploads it over SFTP, extracts it to `/var/www/...`, sets `chown`/`chmod`, (re)writes and restarts the
     systemd service and reloads Nginx/Apache.
   - **Manual Deployment**: downloads one `.tar.gz` **kit** per server. Each kit holds the build, `appsettings.json`
     (backend), `Deploy.sh`, `deploy-config.json` and a README with that server's exact commands.

Both modes use the **same kit and the same `Deploy.sh`**, so a manual deployment behaves exactly like an automated one.

SSH logins are used for that deployment only and never stored. The sudo password goes to `sudo` on stdin, never on
a command line.

---

## 3. Azure DevOps: building for Linux

The module picks, from the latest successful build, the artifact whose name contains **`linux`** (e.g.
`drop-linux`), preferring one that starts with the source's configured artifact name. Without one, it falls back to
the configured artifact and warns you.

**Frontend:** the built `dist/` files run on any OS, so the existing artifact is fine.

**Backend:** what matters is how the API is published:

| Publish | Runs on Linux? | How Linux starts it |
|---|---|---|
| framework-dependent, no runtime (`dotnet publish -c Release`) | ✅ yes (needs the ASP.NET Core runtime, which Deploy.sh installs) | `dotnet App.dll` |
| self-contained `-r linux-x64 --self-contained` | ✅ yes (no runtime needed) | `./App` |
| self-contained `-r win-x64` | ❌ no, rejected with a clear message | — |

Add a Linux publish next to the existing Windows one in the pipeline:

```yaml
- task: DotNetCoreCLI@2
  displayName: Publish API (linux-x64)
  inputs:
    command: publish
    projects: 'AuditVare-WebApi/AuditVare-WebApi.csproj'
    arguments: '-c Release -r linux-x64 --self-contained false -o $(Build.ArtifactStagingDirectory)/linux'
    zipAfterPublish: false
    publishWebProjects: false

- task: PublishBuildArtifacts@1
  displayName: Publish artifact drop-linux
  inputs:
    PathtoPublish: '$(Build.ArtifactStagingDirectory)/linux'
    ArtifactName: 'drop-linux'
```

Notes:

- `-r linux-x64 --self-contained false` produces a Linux build that uses the installed runtime and still restores
  Linux-native packages (e.g. SQL Client's native parts). `--self-contained true` bundles the runtime instead.
- If the Windows artifact uses a *folder inside artifact* (e.g. `AuditVare-WebApi`), publish the Linux artifact with
  the same layout. The module uses the source's folder setting for both.
- Keep publishing the **`sqlscripts`** artifact. Kits carry the scripts, and DbMigrator runs on Linux as
  `dotnet DbMigrator.dll`.
- Behind Nginx the app should honour the forwarded headers so it knows the original scheme and client IP:
  `app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });`

---

## 4. Preparing a fresh server

You need an Ubuntu/Debian server you can SSH into as a user with `sudo` (e.g. `ubuntu`), or as root.

`Deploy.sh` installs everything that is missing. This is what it does, so you know what ends up on the server:

```bash
sudo apt-get update
sudo apt-get install -y nginx redis-server           # web server / reverse proxy, cache (API)
# ASP.NET Core runtime (Microsoft's installer script; works on every Ubuntu/Debian version)
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
sudo bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
dotnet --list-runtimes                               # Microsoft.AspNetCore.App 10.0.x
```

Firewall: when `ufw` is on, Deploy.sh allows "Nginx Full" (ports 80 and 443). Cloud servers (Azure, AWS) also have
a network security group / security group: open 80/443 there. Only open 1433 on the SQL Server, and only to the API
server.

---

## 5. Where files go

| What | Path |
|---|---|
| Backend files | `/var/www/<app>-api` (owner `www-data`, folders 750, files 640) |
| Backend settings | `/var/www/<app>-api/appsettings.json` |
| Frontend files | `/var/www/<app>-web` (owner `www-data`, folders 755, files 644) |
| Frontend runtime settings | `/var/www/<app>-web/config.js` (`VITE_API_URL`, ...) |
| systemd service | `/etc/systemd/system/<app>-api.service` |
| Extra secrets for the service (optional) | `/etc/default/<app>-api` (`KEY=value` lines) |
| Nginx sites | `/etc/nginx/sites-available/<app>-api`, `<app>-web` (linked from `sites-enabled`) |
| Backups before each deployment | `/var/backups/deployvare/<site>_<date>.tar.gz` (last 5 kept) |
| App logs | `journalctl -u <app>-api` |

Why these permissions: the API runs as `www-data` and owns its folder, because it may write logs or uploads there.
Other users cannot read it (750/640), which matters because `appsettings.json` holds the database password. Nginx
only needs to *read* the frontend, so those files are 644.

---

## 6. Manual deployment, step by step

1. In DeployVare choose **Manual Deployment** → **Download deployment kits**. You get one `.tar.gz` per server:
   `*_linux-database-*` (three-tier), `*_linux-api-*`, `*_linux-frontend-*`.
2. **Copy the kit** to the server. PowerShell on Windows 10+ has `scp`; WinSCP works too:
   ```bash
   scp AuditVare_linux-api-1.0.123.tar.gz ubuntu@10.0.0.20:/tmp/
   ```
3. **Log in and extract it**:
   ```bash
   ssh ubuntu@10.0.0.20
   cd /tmp && tar -xzf AuditVare_linux-api-1.0.123.tar.gz && cd AuditVare_linux-api-1.0.123
   ls          # app/  sqlscripts/  Deploy.sh  deploy-config.json  README.md
   ```
4. **Backend: set the connection string** (next section), in `app/appsettings.json`.
5. **Run the script as root.** It prints its plan and asks before changing anything:
   ```bash
   sudo bash Deploy.sh            # --help lists the options (--run-migrations, --skip-migrations, --yes)
   ```
6. **Check it**:
   ```bash
   sudo systemctl status auditvare-api      # "active (running)"
   sudo journalctl -u auditvare-api -f      # live logs, Ctrl+C to stop
   curl -I http://api.client.com            # answer through Nginx
   ```

Order: **database kit → API kit → frontend kit**.

---

## 7. Editing appsettings.json on Linux

Every backend kit has `app/appsettings.json`. If the Azure build has none (pipelines often leave it out), DeployVare
adds a template. It copies the keys from the build's `appsettings.Production.json` when there is one, and sets every
connection string to a clear placeholder:

```json
// Generated by DeployVare: this build has no appsettings.json.
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=10.0.0.5,1433;Database=AuditVare;User Id=<SQL_LOGIN>;Password=<SQL_PASSWORD>;TrustServerCertificate=True;MultipleActiveResultSets=True"
  },
  ...
}
```

Edit it with **nano** (easiest) or vim:

```bash
nano app/appsettings.json        # arrows to move, type to edit
                                 # Ctrl+O then Enter = save, Ctrl+X = exit, Ctrl+W = search
```

(vim: `vim app/appsettings.json`, press `i` to type, `Esc` then `:wq` + Enter to save and quit.)

- Linux cannot use `Integrated Security=True`. Use a **SQL login**: `User Id=...;Password=...`.
- `Deploy.sh` refuses to migrate while any `<...>` placeholder remains, and offers to open nano if one is left.
- **An appsettings.json that is already on the server is kept.** To change settings later, edit the live file and
  restart:
  ```bash
  sudo nano /var/www/auditvare-api/appsettings.json
  sudo systemctl restart auditvare-api
  ```

The same template fix applies to **Windows** manual packages: the backend's `app\appsettings.json` is now always
present. A generated template is only copied to servers that have none yet, so a server's working config is never
overwritten by placeholders.

---

## 8. The systemd service, explained

`Deploy.sh` writes this (the values come from your settings):

```ini
[Unit]
Description=AuditVare API (auditvare-api)
After=network-online.target redis-server.service    # start after the network (and Redis)
Wants=network-online.target

[Service]
Type=simple
User=www-data                                       # like the app pool identity
Group=www-data
WorkingDirectory=/var/www/auditvare-api             # appsettings.json is read from here
ExecStart=/usr/share/dotnet/dotnet /var/www/auditvare-api/AuditVare.dll
Restart=always                                      # restart after a crash...
RestartSec=10                                       # ...10 seconds later
KillSignal=SIGINT                                   # graceful shutdown (like Ctrl+C)
SyslogIdentifier=auditvare-api                      # name in journalctl
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000   # Kestrel only on localhost
EnvironmentFile=-/etc/default/auditvare-api         # optional KEY=value secrets

[Install]
WantedBy=multi-user.target                          # "start at boot"
```

The commands you will use:

```bash
sudo systemctl status auditvare-api       # running? last log lines
sudo systemctl restart auditvare-api      # after changing appsettings.json
sudo systemctl stop|start auditvare-api
sudo systemctl enable auditvare-api       # start at boot (Deploy.sh does this)
sudo journalctl -u auditvare-api -n 100   # last 100 log lines; -f to follow
sudo systemctl edit auditvare-api         # your own overrides (kept across deployments)
```

The unit file is rewritten on every deployment. Put your own changes in `systemctl edit` overrides or
`/etc/default/auditvare-api`.

---

## 9. Nginx as the reverse proxy, explained

**Backend site** (`/etc/nginx/sites-available/auditvare-api`):

```nginx
server {
    listen 80;
    server_name api.client.com;             # the "binding" host name
    client_max_body_size 500m;              # allow large uploads
    location / {
        proxy_pass http://127.0.0.1:5000;   # → Kestrel
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;                 # WebSockets / SignalR
        proxy_set_header Connection $dv_connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;             # app knows it was HTTPS
    }
}
```

**Frontend site** (`/etc/nginx/sites-available/auditvare-web`):

```nginx
server {
    listen 80;
    server_name app.client.com;
    root /var/www/auditvare-web;
    index index.html;
    location / { try_files $uri $uri/ /index.html; }   # single-page app routing
    location = /config.js { add_header Cache-Control "no-store"; }
    location /api/ { proxy_pass http://127.0.0.1:5000; ... }   # optional: one address for both
}
```

The optional `/api` route lets the browser reach the API through the frontend's address (no CORS, one certificate).
When the API is on another server, the route points at the API's host name instead of `127.0.0.1`.

```bash
sudo nginx -t                    # test the configuration (always before reloading)
sudo systemctl reload nginx      # apply without dropping connections
ls -l /etc/nginx/sites-enabled/  # which sites are on
```

`Deploy.sh` runs `nginx -t` itself. If the test fails, it puts the previous site configuration back and stops.

**HTTPS**: put PEM files on the server and enter their paths under *Advanced*. The easiest free option is Let's
Encrypt:

```bash
sudo apt-get install -y certbot
sudo certbot certonly --webroot -w /var/www/auditvare-web -d app.client.com
# → /etc/letsencrypt/live/app.client.com/fullchain.pem and privkey.pem
```

With certificate paths set, port 80 redirects to HTTPS and port 443 serves TLS 1.2/1.3.

---

## 10. Three-tier specifics

- SQL Server runs on its own machine (Windows or SQL Server on Linux). Allow port **1433** from the API server only.
- Create a **SQL login** for the application; Windows authentication is not available from Linux.
- Manual: run the **database kit** on any Linux machine that can reach SQL Server (the API server is fine):
  `nano appsettings.json` → `sudo bash Deploy.sh`. It runs DbMigrator with the build's SQL scripts. The database
  must already exist and be managed by DbMigrator (restore the baseline first).
- Automated: tick **Run database migrations**. DbMigrator runs from the API server with the API's connection string.

---

## 11. Troubleshooting

| Symptom | Check |
|---|---|
| `502 Bad Gateway` | The app is not running: `sudo systemctl status <service>`, `sudo journalctl -u <service> -n 50` |
| Service keeps restarting | The journal shows the exception: often a wrong connection string or a missing setting |
| `Permission denied` in the journal | Files not owned by the service user: `sudo chown -R www-data:www-data /var/www/<app>-api` |
| `address already in use` | Another app uses the port: `sudo ss -ltnp \| grep 5000`; pick another Kestrel port under Advanced |
| Nginx will not reload | `sudo nginx -t` shows the line; another site may use the same `server_name` |
| Frontend loads but API calls fail | `config.js` `VITE_API_URL`, the `/api` route, or CORS on the API |
| Login loops / wrong redirects behind HTTPS | The app needs `UseForwardedHeaders` (section 3) |
| SSH test: "sudo needs a password" | Enter the sudo password, or allow passwordless sudo for the user |
| Can't reach SQL Server | `nc -vz <sql-host> 1433` from the API server; firewall/NSG on the SQL Server |

## 12. Rolling back

Every run saves the previous version first:

```bash
ls -lt /var/backups/deployvare/
sudo systemctl stop auditvare-api
sudo tar -xzf /var/backups/deployvare/auditvare-api_20261015_101500.tar.gz -C /var/www
sudo systemctl start auditvare-api
```

## Limitations

- Ubuntu/Debian only (apt + systemd). RHEL-family servers are not supported by `Deploy.sh`.
- SSH host keys are not verified (same as the Windows server tooling).
- SQL Server itself is not installed or restored on Linux; the database must exist.
- Deployments are not recorded in App History yet. The result and full `Deploy.sh` output appear on the page.
