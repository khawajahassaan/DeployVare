DEPLOYVARE SERVER INSTALLATION
==============================

Installs DeployVare on one Windows server: its database on the local SQL Server, the API and
the frontend as two IIS sites with HTTPS.

Contents:
  api\          DeployVare API (runs on the .NET 10 Hosting Bundle)
  web\          DeployVare frontend (static files)
  database\     backup of the DeployVare database - an exact copy (your users, clients, servers,
                Azure DevOps connection and history)
  installers\   prerequisites and the AuditVare baseline backup, copied to the installers folder
  Install-DeployVare.ps1

Steps:
  1. Copy this folder to the server and open PowerShell as Administrator in it.
  2. Run:

       .\Install-DeployVare.ps1

  3. Press Enter to keep each default, or type a new value:
       SQL Server ............... .                       (the default instance)
       Database ................. DeployVare
       API site / pool / folder . DeployVareApi / DeployVareApi / C:\inetpub\DeployVare\api
       API host name ............ deployvare-api
       Frontend site / pool ..... DeployVare / DeployVareWeb
       Frontend folder .......... C:\inetpub\DeployVare\web
       Frontend host name ....... deployvare
       Installers folder ........ C:\DeployVare\Installers
     (-UseDefaults installs with these values without asking.)

  4. Open https://deployvare on the server and sign in with your existing DeployVare account.

From other machines:
  Add these lines to the hosts file (C:\Windows\System32\drivers\etc\hosts or /etc/hosts),
  with the server's IP, or create DNS records:
       <server IP>   deployvare
       <server IP>   deployvare-api
  Open https://deployvare-api once and accept the certificate, then use https://deployvare.

Upgrading:
  Build a new package and run Install-DeployVare.ps1 again. The existing database is kept
  (never restored over) and appsettings.Production.json keeps its JWT secret.

Notes:
  - The API logs to C:\inetpub\DeployVare\api\logs.
  - The database backup and appsettings.json (which holds the encryption key) are sensitive:
    keep this package somewhere safe and delete it from the server after installing.
