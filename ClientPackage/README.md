# DeployVare client install package

Runs DeployVare **on the client's server**, so it deploys to that server over SSH to `localhost`. No inbound ports are needed; the server only needs outbound HTTPS to `dev.azure.com`.

## 1. Build the package (your machine)

Requirements: .NET 8 SDK, Node.js/npm, and the repository with `web` dependencies installed (`npm install` in `web`).

```powershell
cd ClientPackage
.\Build-ClientPackage.ps1                     # add -IncludeInstallers to ship the Installers folder
```

Output: `ClientPackage\output\DeployVare-ClientPackage-<date>.zip`.

The build refuses to finish if the development database connection string, JWT key or encryption key end up in the package.

## 2. Install (client server)

Requirements on the server:

- IIS with Static Content
- .NET Hosting Bundle 6 or later (for the ASP.NET Core Module; the app itself is self-contained)
- SQL Server on the same server (or any SQL Server with `-UseSqlLogin`)
- OpenSSH Server running, and a local administrator account for DeployVare to sign in with
- Outbound HTTPS to `dev.azure.com` (`Test-NetConnection dev.azure.com -Port 443`)

Copy the ZIP to the server, extract it, then in **Windows PowerShell as Administrator**:

```powershell
cd <extracted folder>\DeployVare
Get-ChildItem -Recurse | Unblock-File          # only needed if the ZIP was downloaded
powershell -ExecutionPolicy Bypass -File .\Install-DeployVare.ps1
```

Options: `-Port 8090`, `-SqlServer ".\SQLEXPRESS"`, `-DatabaseName DeployVare`, `-UseSqlLogin`, `-InstallRoot`, `-DataRoot`.

The installer:

- Installs to `C:\Program Files\DeployVare` (API + web) with data in `C:\ProgramData\DeployVare`
- Creates the IIS site **DeployVare** bound to `127.0.0.1` / `[::1]` only; the API runs as its `/deployvare-api` application
- Creates the database and grants the API app pool (`IIS APPPOOL\DeployVareApi`) `db_owner` with Windows authentication, so no database password is stored
- Generates a new encryption key and JWT signing key, stored only in the API's `web.config` (readable by Administrators, SYSTEM and the API app pool)
- Asks for a new password for the `admin` user (the default `Admin@123` is replaced)

Open **http://127.0.0.1:8090** on the server (for example over Remote Desktop).

## 3. Configure DeployVare

1. **Client Onboarding**: create the client.
2. **Azure DevOps**: add the connection with a PAT limited to *Build (Read)*, then the artifact sources.
3. **Servers**: add this server with Host `localhost`, SSH Port `22` and the local administrator account.
4. **Server Setup**: run the readiness check.

Only this client's data belongs in this installation; do not copy another DeployVare database here.

## Upgrade

Build a new package, extract it on the server and run `Install-DeployVare.ps1` again. Files are replaced; keys, the database connection, data and logs are kept.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-DeployVare.ps1                          # keeps data and database
powershell -ExecutionPolicy Bypass -File .\Uninstall-DeployVare.ps1 -RemoveData -DropDatabase
```

Keep a copy of the API `web.config` if you might restore the database later: the encryption key in it is needed to read the stored passwords and tokens.
