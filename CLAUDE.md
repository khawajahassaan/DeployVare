# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**DeployVare** is a comprehensive SQL Deployment Management System with:
- **Backend:** .NET 8 ASP.NET Core Web API with Entity Framework Core and Dapper
- **Frontend:** Next.js 14 (React + TypeScript) with Tailwind CSS v4
- **Desktop:** Tauri-based Linux desktop app (`.deb`) with .NET sidecar
- **Database:** SQL Server (primary) with SQLite support for local dev
- **Architecture:** Service-based with dependency injection; layered Controllers → Services → Data

---

## Common Commands

### Backend (.NET API)

```bash
cd API

# Build & run
dotnet build
dotnet run

# Run in release mode
dotnet build -c Release
dotnet run -c Release

# Run tests (all)
dotnet test

# Run a single test file
dotnet test --filter FullyQualifiedName~LicensingServiceTests

# Run tests with detailed output
dotnet test -v detailed

# Database migrations
dotnet ef database update                    # Apply latest migrations to configured DB
dotnet ef migrations add MigrationName       # Create a new migration
dotnet ef migrations remove                  # Revert the last migration (uncommitted)

# Watch mode (auto-rebuild on file change)
dotnet watch run

# Publish (self-contained, cross-platform)
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r win-x64 --self-contained true

# Code analysis
dotnet build /p:EnforceCodeStyleInBuild=true

# Restore NuGet packages
dotnet restore
```

### Frontend (Next.js)

```bash
cd web

# Development server
npm run dev              # http://localhost:3000

# Build for production
npm run build
npm start

# Lint
npm run lint

# Type check (included in build)
npm run build

# Tauri desktop app (Linux)
npm run sidecar:build                 # Publish .NET as self-contained Linux binary
npm run tauri:build                   # Build the Tauri desktop app + .deb

# Full desktop build (includes DEB patch)
npm run tauri:full-build

# Tauri dev mode (live reload)
npm run tauri:dev
```

### Docker

```bash
cd .. && pwd  # deployvare/ root

# Start services (API + frontend)
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f api
docker-compose logs -f web

# Rebuild images
docker-compose build --no-cache
```

---

## Architecture & Key Concepts

### Backend Layers

1. **Controllers** (`API/Controllers/`)
   - HTTP endpoints; validate requests, call services, return DTOs
   - One controller per domain (Auth, Deployment, Application, etc.)
   - No business logic; coordinate between request/response and services

2. **Services** (`API/Services/Implementations/`)
   - Core business logic; orchestrate repositories, external calls, validations
   - Interfaces in `Services/Interfaces/` define contracts
   - Key services:
     - **DeploymentService** — SQL patch deployment orchestration
     - **SchemaComparisonService** — Diff tables, procedures, functions across databases
     - **ServerReadinessService** — Remote server health checks via SSH; manages Windows/Linux deployment prerequisites
     - **SqlServerSetupService** — SQL Server instance setup (create DBs, logins, permissions)
     - **LicensingService** — License issuance, activation, binding to machines
     - **SshService** — SSH/SFTP for remote server communication
     - **IisManagementService** — IIS site/pool management via remote PowerShell
     - **ApiDeploymentService** — Deploy .NET apps to IIS

3. **Data Layer** (`API/Data/`)
   - **DeployVareDbContext** — Entity Framework Core DbContext; defines all entities and migrations
   - **Entities** — SQL-mapped classes (Application, Deployment, DatabaseInstance, LicenseActivation, etc.)

4. **DTOs** (`API/DTOs/Requests/`, `Responses/`)
   - Data Transfer Objects for API contracts; separate from entities to avoid exposing internal schema

5. **Models** (`API/Models/`)
   - Domain models (enums, licensing payloads, configuration classes)
   - **Enums** — ServerType, DeploymentStatus, UserRole
   - **Licensing** — LicensePayload, ActivationRequestPayload (cryptographic structures)

6. **Utilities** (`API/Utilities/`)
   - **LicenseSigner** — ECDSA-P256-SHA256 signing for license issuance and activation
   - **DiffGenerator** — Schema diff logic
   - **SqlPatchParser** — Parse and validate SQL patches from ZIP uploads
   - **ManualPackageBuilder** — Generate deployment packages for offline/manual deployment

### Database Schema

- **Users** — Credentials, roles (Admin/User)
- **Applications** — Containers for database instances and deployments
- **DatabaseInstances** — Connection strings, current patch level, backup path
- **Deployments** — Patch execution history per instance; status, error tracking
- **DeploymentObjects** — Details of procedures/functions/tables deployed
- **Licenses** — License records, customers, expiry, features
- **LicenseActivations** — Binding of licenses to machines by fingerprint (ECDSA public key hash)
- **ApplicationServers** — Remote servers for deployment (IIS sites, pools, .NET apps)
- **ServerReadinessChecks** — Most recent health check per server

### Frontend Architecture

- **App Router** — Pages in `web/app/[route]/page.tsx`; `(protected-layout)` for auth-required pages
- **State Management** — Zustand stores in `web/lib/store.ts`
- **API Client** — Axios instance in `web/lib/api.ts`; handles JWT token refresh
- **Components** — Reusable UI in `web/components/`; styled with Tailwind CSS v4
- **Type Safety** — All API responses typed; shared types with backend via Swagger

### Deployment Patterns

**Server Deployment** (Windows IIS):
1. Receive configuration in `Deploy.ps1` (remote setup script)
2. SSH to server; execute PowerShell scripts for IIS, certificates, .NET, database
3. Orchestrated by **ServerReadinessService** from DeployVare UI
4. Supports app pool identity, bindings, HTTPS, database permissions

**SQL Deployment**:
1. User uploads ZIP (patches, procedures, functions)
2. Service parses, validates sequence, backs up existing objects
3. Executes patches sequentially; rollback on error
4. Tracks history, allows download of backups

**Desktop Deployment**:
1. `npm run sidecar:build` — Publish API as self-contained Linux binary (`linux-x64`)
2. Copy binary to `src-tauri/binaries/api-server-x86_64-unknown-linux-gnu`
3. `tauri build` — Bundles frontend + binary into `.deb`
4. Desktop runs .NET sidecar as child process; frontend talks to local API

### Licensing System

- **Model:** ECDSA-P256 with SHA-256; each license is one-activation (bound to machine fingerprint)
- **Fingerprint:** `SHA256(PublicKey)[0:32]` (first 32 hex chars of SHA256 hash)
- **Flow:**
  1. Admin creates license in DeployVare UI (specifies customer, product, expiry, max activations)
  2. Client server generates keypair, sends activation request (signed by private key)
  3. Admin reviews and approves in UI
  4. DeployVare signs response (binds to machine fingerprint), client caches it locally
  5. Client validates response signature at runtime using public key stored in license file
- **Platform Support:** Fingerprinting is cross-platform (ECDSA); currently client tooling is Windows-only (PowerShell Deploy.ps1)

### Key Dependencies

- **EntityFrameworkCore** — ORM; migrations in `API/Migrations/`
- **Dapper** — Fast SQL execution for complex queries (schema comparison, deployment logic)
- **JWT** — Stateless auth; tokens include userId, username, role claims
- **SSH.NET** — Remote command execution on servers
- **Serilog** — Structured logging to console + rolling daily files
- **FluentValidation** — Request validation (DTOs validated before entering services)
- **BCrypt.Net-Next** — Password hashing
- **Tauri** — Desktop app framework (bundles .NET sidecar + Electron-like webview)

---

## Important Implementation Notes

### Licensing

The licensing system binds licenses to machine fingerprints to prevent copying between servers. The fingerprint is derived from the client's ECDSA public key (platform-independent). Currently, the client-side tooling (activation request generation) is **Windows-only** but the core fingerprinting algorithm is cross-platform. Future enhancements will support Linux deployments.

### Remote Deployment (SSH)

- **ServerReadinessService** uses SSH to check Windows server prerequisites (IIS, .NET, SQL Server, certificates)
- Executes remote **PowerShell** scripts to run installers and configure services
- All scripts are templated with safe parameter escaping (`Ps()` helper escapes for PowerShell)
- Supports custom SSH key or password auth

### Schema Comparison

- Fetches DDL from both databases using schema queries
- Builds line-by-line diffs (added/removed/modified)
- **Note:** Currently designed for SQL Server; cross-DB comparison would require provider-specific DDL queries

### Transaction & Rollback

- SQL patches run sequentially in transactions
- A failed patch stops further execution (no auto-rollback of previous patches; manual intervention required)
- Backup service creates ZIP snapshots of procedures/functions before deployment
- Future enhancement: down scripts for automated rollback

### State Management (Frontend)

- **Zustand** for auth (token, user, roles), deployments, selections
- **Axios interceptor** refreshes JWT tokens when expired
- No Redux; minimal boilerplate

---

## Testing

### Backend

- Framework: **xunit** with NSubstitute for mocking
- Location: `API.Tests/`
- Key test class: **LicensingServiceTests** — tests signing, activation, fingerprinting
- Run: `dotnet test` or `dotnet test --filter FullyQualifiedName~LicensingServiceTests`
- Pattern: Arrange (setup data), Act (call service), Assert (verify results and side effects)

### Frontend

- Linting: ESLint via `npm run lint`
- No unit/integration tests configured (Next.js build typechecks)
- Manual testing recommended for pages after changes

---

## Configuration

### Backend (`API/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DeployVareDb": "Server=localhost;Database=DeployVare;..."
  },
  "JwtSettings": {
    "SecretKey": "...",
    "Issuer": "DeployVare",
    "Audience": "DeployVare",
    "ExpirationMinutes": 60
  },
  "FileUploadSettings": {
    "UploadPath": "wwwroot/uploads",
    "BackupPath": "wwwroot/backups",
    "MaxUploadSizeMB": 500
  }
}
```

### Frontend (`.env.local`)

```
NEXT_PUBLIC_API_URL=https://localhost:7001/api
```

### Docker (`.env`)

```
DB_CONNECTION_STRING=Server=sql-server;Database=DeployVare;...
API_PORT=7001
WEB_PORT=3000
```

---

## File Structure Summary

```
deployvare/
├── API/                          # .NET 8 backend
│   ├── Controllers/              # HTTP endpoints
│   ├── Services/                 # Business logic (Implementations/ + Interfaces/)
│   ├── Data/                     # EF Core DbContext, entities
│   ├── DTOs/                     # Request/Response contracts
│   ├── Models/                   # Domain models, enums, licensing
│   ├── Utilities/                # Helpers (signer, diff, parser)
│   ├── Migrations/               # EF Core schema migrations
│   ├── DeploymentScripts/        # Deploy.ps1 template
│   └── Program.cs                # DI setup, authentication, logging
├── API.Tests/                    # xunit tests
├── web/                          # Next.js 14 frontend
│   ├── app/                      # Pages (App Router)
│   ├── components/               # Reusable React components
│   ├── lib/                      # Utilities (API client, store, types)
│   ├── styles/                   # Tailwind CSS config
│   └── src-tauri/                # Tauri desktop app config
├── Docker/                       # Dockerfiles, compose overrides
├── Installers/                   # Packaged installers (IIS, .NET, SQL Server, etc.)
├── docker-compose.yml            # Multi-container orchestration
└── README.md                     # Project overview
```

---

## Common Workflows

### Add a New API Endpoint

1. Create DTOs in `API/DTOs/Requests/` and `Responses/`
2. Add business logic to appropriate service in `API/Services/Implementations/`
3. Add controller method in `API/Controllers/` that calls the service
4. Decorate with `[Authorize]` if auth required
5. Test via Swagger at `https://localhost:7001/swagger`

### Add a Database Entity

1. Create class in `API/Models/Entities/`
2. Add DbSet property to `DeployVareDbContext`
3. Run `dotnet ef migrations add MigrationName`
4. Run `dotnet ef database update` to apply

### Deploy to Remote Server

1. Create server record via UI (Server Onboarding → Add Server)
2. Click "Check Readiness" to SSH and verify prerequisites
3. Click "Install Component" to push installers and configure
4. Click "Configure Bindings" to set HTTPS certificates

---

## Debugging Tips

- **Backend:** Set breakpoints in Visual Studio or use `dotnet run` with debug; logs go to `logs/` and console
- **Frontend:** Use browser dev tools (F12); Zustand state visible in React DevTools
- **SSH Commands:** Check `ServerReadinessService` for PowerShell scripts; errors logged in service response
- **Database Migrations:** Use `dotnet ef migrations history` to see applied migrations; SQL Server Management Studio for direct inspection
- **Licensing:** Use `LicenseSigner.Fingerprint()` to debug key hashing; test activation flow in `LicensingServiceTests`

---

## Linux Support (NEW!)

### Manual Deployment (Deploy.sh)

Bash script for offline Linux deployment on Ubuntu/Debian:

```bash
cd /path/to/package
sudo bash Deploy.sh
```

**Features:**
- Installs .NET 8, Redis 7, Nginx & Apache
- Configures SSL certificates
- Creates systemd service for auto-start
- Logs to `deployment.log`

See [LINUX_DEPLOYMENT.md](LINUX_DEPLOYMENT.md) for details.

### Docker Deployment

Self-contained .NET 8 app with Nginx reverse proxy and Redis:

```bash
docker compose -f docker-compose.linux.yml up -d
```

**Images:**
- `.NET 8` (self-contained, no framework dependency)
- **Nginx Alpine** (reverse proxy + SSL termination)
- **Redis Alpine** (cache layer)

See [LINUX_DEPLOYMENT.md](LINUX_DEPLOYMENT.md) for configuration.

### Remote Deployment via DeployVare

New **ILinuxServerService** enables SSH-based Linux server management:

```csharp
// Check prerequisites
var facts = await linuxService.GetSystemFactsAsync(host, port, user, pass, key);

// Install components
await linuxService.InstallDotNetAsync(host, port, user, pass, key, majorVersion);
await linuxService.InstallRedisAsync(host, port, user, pass, key, redisPort);
await linuxService.InstallNginxAsync(host, port, user, pass, key);

// Configure web server
await linuxService.ConfigureNginxSiteAsync(host, port, user, pass, key, siteName, hostName, path);

// Manage systemd service
await linuxService.CreateSystemdServiceAsync(host, port, user, pass, key, serviceName, appPath);
await linuxService.StartServiceAsync(host, port, user, pass, key, serviceName);
```

**Added Services:**
- `ILinuxServerService` — SSH-based Linux server operations
- `ExecuteBashCommandAsync` in ISshService — Bash command execution

**License Fingerprinting:** Cross-platform ECDSA-based machine binding (works on both Windows and Linux).

---

## Known Constraints & Future Work

- **Schema Comparison:** Supports SQL Server only; extending to PostgreSQL, MySQL would require provider-specific DDL logic.
- **Rollback:** No automated rollback (manual via backups); down scripts could enable this.
- **Real-time Progress:** Deployments use polling; SignalR could enable live progress streaming.
- **Multi-instance Deploy:** Cannot deploy to multiple instances in one operation.
- **Windows Desktop App:** Still Windows-only; Tauri Linux desktop app in development.

---

## Performance Considerations

- **Deployment Timeout:** Long-running patches configured in `appsettings.json`; SSH default is 30s per operation
- **Backup Compression:** Uses Zstandard (ZstdSharp) for faster compression than ZIP
- **Database Queries:** Dapper used for complex schema queries to avoid EF Core overhead
- **Frontend State:** Zustand keeps auth and UI state in memory; no persistent cache (localStorage only for dev convenience)
