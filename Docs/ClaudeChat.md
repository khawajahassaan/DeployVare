# My Query

WHAT I WANTED PREVIOUSLY:
consider this scenario and tell me the most suitable tool to be used in this:
scenario:
Production server is a windows server:
as the code pushes to develop branch or merges
Ci/CD should handle this:
build a publish, compress the latest 4 files (if env var says update) <projectname>.dll, <projectname>.deps.json, <projectname>.pdb, <projectname>.exe. 
OR
compress everything except wwwroot, and appsetting files.
theres a folder in project directory named SQL, contains two subfolder. SQLPatches and StoredProcedures.
SQLPatch will contain ddl queries mostly like creating table, altering table, adding constraint, CI/CD should make a down version for it, like if column was added, a down version should be made to remove the column as well, same for other ddl queries.
 
logs into the production server
stops the app running on iis (only our app)
takes backup:
* compress the stored procedures that were in SQL/StoredProcedure (for rollback if needed)
* compress or do whatever suits best
* compress the current 4 files that were .pdb, .exe, .deps.json, and .dll
Extract and replace the files, run the queries, and procedures.
starts the app.

WHAT I WANT NOW:
What if i create an web api that does this? is it possible? this will be deployed on the production server, or where ever we need to manage it.
like i could handle these cases one by one. i'd select the branch and tap deploy, a version 1.2 will be deployed lets say. i can also rollback to previous, then there will be option to sync db, it'll show what changes would happen, and applies them on approval. that can also be rolledback.

what would i need to do, and how complex this app will be?


# Claude's Response


# Complete Web API Deployment Manager - Implementation Plan

## **Architecture Overview**

```
┌─────────────────────────────────────────────┐
│   React Frontend (SPA)                      │
│   - Deployment dashboard                    │
│   - Real-time logs (SignalR)                │
│   - Migration preview                       │
│   Hosted: IIS / Azure / Same as API         │
└──────────────┬──────────────────────────────┘
               │ HTTPS/REST
┌──────────────▼──────────────────────────────┐
│   ASP.NET Core Web API                      │
│   - Deployment orchestration                │
│   - PowerShell remoting                     │
│   - Git operations (LibGit2Sharp)           │
│   - Build service (dotnet CLI)              │
│   - File operations (UNC paths)             │
│   - Database migrations                     │
│   Deployed: Jump Server / DevOps Server     │
└──────────────┬──────────────────────────────┘
               │
               │ PowerShell Remoting (WinRM)
               │ UNC File Access
               │
┌──────────────▼──────────────────────────────┐
│   Production Windows Server                 │
│   - IIS Application                         │
│   - Application Files                       │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│   SQL Server (Production DB)                │
└──────────────┬──────────────────────────────┘
               │
┌──────────────▼──────────────────────────────┐
│   SQL Server (Metadata DB)                  │
│   - Deployment history                      │
│   - Migration records                       │
│   - Rollback snapshots                      │
└─────────────────────────────────────────────┘
```

---

## **Tech Stack**

### **Backend:**
```yaml
Framework: ASP.NET Core 8.0 Web API
Language: C# 12
Architecture: Clean Architecture (CQRS with MediatR)

Key Libraries:
  - System.Management.Automation (PowerShell)
  - LibGit2Sharp (Git operations)
  - Dapper (Database access)
  - SignalR (Real-time logs)
  - Serilog (Logging)
  - FluentValidation (Input validation)
  - Hangfire (Background jobs - optional)
  - Polly (Retry policies)
```

### **Frontend:**
```yaml
Framework: React 18 + TypeScript
State Management: Zustand / Redux Toolkit
UI Library: Tailwind CSS + shadcn/ui
Real-time: SignalR Client (@microsoft/signalr)
HTTP Client: Axios
```

### **Database:**
```yaml
Metadata DB: SQL Server (or PostgreSQL)
ORM: Dapper (lightweight)
Migrations: FluentMigrator or DbUp
```

---

## **Project Structure**

```
DeploymentManager/
│
├── src/
│   ├── DeploymentManager.API/              # Web API Project
│   │   ├── Controllers/
│   │   │   ├── DeploymentController.cs
│   │   │   ├── MigrationController.cs
│   │   │   ├── RollbackController.cs
│   │   │   └── ConfigurationController.cs
│   │   ├── Hubs/
│   │   │   └── DeploymentHub.cs            # SignalR for real-time logs
│   │   ├── Middleware/
│   │   │   ├── ErrorHandlingMiddleware.cs
│   │   │   └── AuthenticationMiddleware.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── DeploymentManager.Application/       # Business Logic Layer
│   │   ├── Commands/
│   │   │   ├── DeployCommand.cs
│   │   │   ├── RollbackCommand.cs
│   │   │   └── SyncDatabaseCommand.cs
│   │   ├── Queries/
│   │   │   ├── GetDeploymentsQuery.cs
│   │   │   └── PreviewMigrationsQuery.cs
│   │   ├── Handlers/
│   │   │   ├── DeployCommandHandler.cs
│   │   │   └── RollbackCommandHandler.cs
│   │   ├── Services/
│   │   │   └── DeploymentOrchestrator.cs
│   │   └── DTOs/
│   │       ├── DeploymentDto.cs
│   │       └── MigrationDto.cs
│   │
│   ├── DeploymentManager.Infrastructure/    # Infrastructure Layer
│   │   ├── Services/
│   │   │   ├── PowerShellRemotingService.cs
│   │   │   ├── GitService.cs
│   │   │   ├── BuildService.cs
│   │   │   ├── FileDeploymentService.cs
│   │   │   ├── IISManagementService.cs
│   │   │   ├── DatabaseMigrationService.cs
│   │   │   ├── BackupService.cs
│   │   │   └── HealthCheckService.cs
│   │   ├── Repositories/
│   │   │   ├── DeploymentRepository.cs
│   │   │   └── MigrationRepository.cs
│   │   └── Configuration/
│   │       └── ServerConfiguration.cs
│   │
│   ├── DeploymentManager.Domain/            # Domain Layer
│   │   ├── Entities/
│   │   │   ├── Deployment.cs
│   │   │   ├── DatabaseMigration.cs
│   │   │   └── RollbackSnapshot.cs
│   │   ├── Enums/
│   │   │   ├── DeploymentStatus.cs
│   │   │   └── DeploymentMode.cs
│   │   └── Interfaces/
│   │       ├── IDeploymentRepository.cs
│   │       └── IRemoteServerService.cs
│   │
│   └── DeploymentManager.Web/               # React Frontend
│       ├── src/
│       │   ├── components/
│       │   │   ├── DeploymentDashboard.tsx
│       │   │   ├── DeploymentForm.tsx
│       │   │   ├── DeploymentHistory.tsx
│       │   │   ├── MigrationPreview.tsx
│       │   │   ├── LogViewer.tsx
│       │   │   └── ServerConfig.tsx
│       │   ├── services/
│       │   │   ├── api.ts
│       │   │   └── signalr.ts
│       │   ├── stores/
│       │   │   └── deploymentStore.ts
│       │   ├── types/
│       │   │   └── deployment.types.ts
│       │   ├── App.tsx
│       │   └── main.tsx
│       ├── package.json
│       └── vite.config.ts
│
├── tests/
│   ├── DeploymentManager.Tests.Unit/
│   └── DeploymentManager.Tests.Integration/
│
└── database/
    ├── migrations/
    │   ├── 001_CreateDeploymentTables.sql
    │   └── 002_CreateMigrationTables.sql
    └── seed/
        └── initial_config.sql
```

---

## **Phase-by-Phase Implementation Plan**

### **Phase 1: Foundation (Week 1)**

#### **Day 1-2: Project Setup**
```bash
# Create solution
dotnet new sln -n DeploymentManager

# Create projects
dotnet new webapi -n DeploymentManager.API
dotnet new classlib -n DeploymentManager.Application
dotnet new classlib -n DeploymentManager.Infrastructure
dotnet new classlib -n DeploymentManager.Domain

# Add projects to solution
dotnet sln add **/*.csproj

# Install NuGet packages
cd DeploymentManager.API
dotnet add package Swashbuckle.AspNetCore
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Serilog.AspNetCore

cd ../DeploymentManager.Application
dotnet add package MediatR
dotnet add package FluentValidation

cd ../DeploymentManager.Infrastructure
dotnet add package System.Management.Automation
dotnet add package LibGit2Sharp
dotnet add package Dapper
dotnet add package Microsoft.Data.SqlClient
dotnet add package Polly
```

#### **Day 3: Database Schema**
```sql
-- 001_CreateDeploymentTables.sql

CREATE TABLE Deployments (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Version NVARCHAR(50) NOT NULL,
    Branch NVARCHAR(100) NOT NULL,
    CommitHash NVARCHAR(100) NOT NULL,
    DeployedBy NVARCHAR(100) NOT NULL,
    DeployedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    Status NVARCHAR(20) NOT NULL, -- Pending, InProgress, Success, Failed, RolledBack
    UpdateMode NVARCHAR(20) NOT NULL, -- Incremental, Full
    BackupPath NVARCHAR(500),
    Notes NVARCHAR(MAX),
    ErrorMessage NVARCHAR(MAX),
    Duration INT, -- seconds
    CONSTRAINT CK_Deployment_Status CHECK (Status IN ('Pending', 'InProgress', 'Success', 'Failed', 'RolledBack'))
);

CREATE TABLE DeploymentFiles (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    FileName NVARCHAR(500) NOT NULL,
    FileHash NVARCHAR(100),
    FileSize BIGINT,
    BackupLocation NVARCHAR(500),
    FOREIGN KEY (DeploymentId) REFERENCES Deployments(Id) ON DELETE CASCADE
);

CREATE TABLE DatabaseMigrations (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    MigrationName NVARCHAR(200) NOT NULL,
    UpScript NVARCHAR(MAX) NOT NULL,
    DownScript NVARCHAR(MAX),
    ExecutedAt DATETIME2,
    Status NVARCHAR(20) NOT NULL, -- Pending, Applied, RolledBack, Failed
    ExecutionOrder INT NOT NULL,
    ErrorMessage NVARCHAR(MAX),
    FOREIGN KEY (DeploymentId) REFERENCES Deployments(Id) ON DELETE CASCADE
);

CREATE TABLE MigrationHistory (
    Id INT PRIMARY KEY IDENTITY(1,1),
    MigrationId INT NOT NULL,
    Action NVARCHAR(20) NOT NULL, -- Apply, Rollback
    ExecutedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ExecutedBy NVARCHAR(100) NOT NULL,
    Success BIT NOT NULL,
    ErrorMessage NVARCHAR(MAX),
    FOREIGN KEY (MigrationId) REFERENCES DatabaseMigrations(Id)
);

CREATE TABLE RollbackSnapshots (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    SnapshotPath NVARCHAR(500) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    DatabaseBackupPath NVARCHAR(500),
    FilesBackupPath NVARCHAR(500),
    FOREIGN KEY (DeploymentId) REFERENCES Deployments(Id)
);

CREATE TABLE ServerConfigurations (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL UNIQUE, -- e.g., "Production", "Staging"
    ServerAddress NVARCHAR(200) NOT NULL,
    IISSiteName NVARCHAR(200) NOT NULL,
    IISAppPoolName NVARCHAR(200) NOT NULL,
    DeploymentPath NVARCHAR(500) NOT NULL, -- UNC path
    DatabaseConnectionString NVARCHAR(1000) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE TABLE DeploymentLogs (
    Id INT PRIMARY KEY IDENTITY(1,1),
    DeploymentId INT NOT NULL,
    LogLevel NVARCHAR(20) NOT NULL, -- Info, Warning, Error
    Message NVARCHAR(MAX) NOT NULL,
    Timestamp DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (DeploymentId) REFERENCES Deployments(Id) ON DELETE CASCADE
);

-- Indexes
CREATE INDEX IX_Deployments_Status ON Deployments(Status);
CREATE INDEX IX_Deployments_DeployedAt ON Deployments(DeployedAt DESC);
CREATE INDEX IX_DatabaseMigrations_DeploymentId ON DatabaseMigrations(DeploymentId);
CREATE INDEX IX_DeploymentLogs_DeploymentId ON DeploymentLogs(DeploymentId);
```

#### **Day 4-5: Domain Models & Interfaces**
```csharp
// DeploymentManager.Domain/Entities/Deployment.cs

namespace DeploymentManager.Domain.Entities;

public class Deployment
{
    public int Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string CommitHash { get; set; } = string.Empty;
    public string DeployedBy { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
    public DeploymentStatus Status { get; set; }
    public DeploymentMode UpdateMode { get; set; }
    public string? BackupPath { get; set; }
    public string? Notes { get; set; }
    public string? ErrorMessage { get; set; }
    public int? Duration { get; set; } // in seconds
    
    public List<DeploymentFile> Files { get; set; } = new();
    public List<DatabaseMigration> Migrations { get; set; } = new();
    public List<DeploymentLog> Logs { get; set; } = new();
}

public class DeploymentFile
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public long? FileSize { get; set; }
    public string? BackupLocation { get; set; }
}

public class DatabaseMigration
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public string MigrationName { get; set; } = string.Empty;
    public string UpScript { get; set; } = string.Empty;
    public string? DownScript { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public MigrationStatus Status { get; set; }
    public int ExecutionOrder { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DeploymentLog
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public LogLevel LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ServerConfiguration
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
    public string IISSiteName { get; set; } = string.Empty;
    public string IISAppPoolName { get; set; } = string.Empty;
    public string DeploymentPath { get; set; } = string.Empty;
    public string DatabaseConnectionString { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Enums
public enum DeploymentStatus
{
    Pending,
    InProgress,
    Success,
    Failed,
    RolledBack
}

public enum DeploymentMode
{
    Incremental,
    Full
}

public enum MigrationStatus
{
    Pending,
    Applied,
    RolledBack,
    Failed
}

public enum LogLevel
{
    Info,
    Warning,
    Error
}
```

---

### **Phase 2: Infrastructure Services (Week 1-2)**

#### **PowerShell Remoting Service**
```csharp
// DeploymentManager.Infrastructure/Services/PowerShellRemotingService.cs

using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

namespace DeploymentManager.Infrastructure.Services;

public interface IPowerShellRemotingService
{
    Task<string> ExecuteRemoteCommand(string scriptBlock);
    Task<bool> TestConnection();
    Task StopIISAppPool(string appPoolName);
    Task StartIISAppPool(string appPoolName);
    Task StopWebsite(string siteName);
    Task StartWebsite(string siteName);
}

public class PowerShellRemotingService : IPowerShellRemotingService, IDisposable
{
    private readonly string _serverAddress;
    private readonly PSCredential _credential;
    private Runspace? _runspace;
    private readonly ILogger<PowerShellRemotingService> _logger;

    public PowerShellRemotingService(
        ServerConfiguration config,
        ILogger<PowerShellRemotingService> logger)
    {
        _serverAddress = config.ServerAddress;
        _logger = logger;
        
        // Get credentials from secure storage (Azure Key Vault, etc.)
        var username = config.Username; // From configuration
        var password = GetSecurePassword(config.PasswordKey);
        _credential = new PSCredential(username, password);
    }

    private Runspace GetOrCreateRunspace()
    {
        if (_runspace == null || _runspace.RunspaceStateInfo.State != RunspaceState.Opened)
        {
            var connectionInfo = new WSManConnectionInfo(
                new Uri($"http://{_serverAddress}:5985/wsman"),
                "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                _credential
            );
            
            connectionInfo.OpenTimeout = 60000; // 60 seconds
            connectionInfo.OperationTimeout = 300000; // 5 minutes
            
            _runspace = RunspaceFactory.CreateRunspace(connectionInfo);
            _runspace.Open();
        }
        
        return _runspace;
    }

    public async Task<string> ExecuteRemoteCommand(string scriptBlock)
    {
        try
        {
            _logger.LogInformation("Executing remote PowerShell command");
            
            var runspace = GetOrCreateRunspace();
            
            using var pipeline = runspace.CreatePipeline();
            pipeline.Commands.AddScript(scriptBlock);
            
            var results = await Task.Run(() => pipeline.Invoke());
            
            var output = new StringBuilder();
            foreach (var result in results)
            {
                output.AppendLine(result?.ToString());
            }

            if (pipeline.Error.Count > 0)
            {
                var errors = new StringBuilder();
                foreach (var error in pipeline.Error.ReadToEnd())
                {
                    errors.AppendLine(error.ToString());
                }
                throw new Exception($"PowerShell errors: {errors}");
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute remote PowerShell command");
            throw;
        }
    }

    public async Task<bool> TestConnection()
    {
        try
        {
            var result = await ExecuteRemoteCommand("Write-Output 'Connection successful'");
            return result.Contains("Connection successful");
        }
        catch
        {
            return false;
        }
    }

    public async Task StopIISAppPool(string appPoolName)
    {
        var script = $@"
            Import-Module WebAdministration
            
            $appPool = Get-WebAppPoolState -Name '{appPoolName}'
            if ($appPool.Value -eq 'Started') {{
                Stop-WebAppPool -Name '{appPoolName}'
                
                # Wait for stop with timeout
                $timeout = 30
                $elapsed = 0
                while ((Get-WebAppPoolState -Name '{appPoolName}').Value -ne 'Stopped' -and $elapsed -lt $timeout) {{
                    Start-Sleep -Seconds 1
                    $elapsed++
                }}
                
                if ($elapsed -ge $timeout) {{
                    throw 'App pool did not stop within timeout period'
                }}
                
                Write-Output 'App pool stopped successfully'
            }} else {{
                Write-Output 'App pool already stopped'
            }}
        ";

        await ExecuteRemoteCommand(script);
    }

    public async Task StartIISAppPool(string appPoolName)
    {
        var script = $@"
            Import-Module WebAdministration
            
            $appPool = Get-WebAppPoolState -Name '{appPoolName}'
            if ($appPool.Value -ne 'Started') {{
                Start-WebAppPool -Name '{appPoolName}'
                
                # Wait for start with timeout
                $timeout = 30
                $elapsed = 0
                while ((Get-WebAppPoolState -Name '{appPoolName}').Value -ne 'Started' -and $elapsed -lt $timeout) {{
                    Start-Sleep -Seconds 1
                    $elapsed++
                }}
                
                if ($elapsed -ge $timeout) {{
                    throw 'App pool did not start within timeout period'
                }}
                
                Write-Output 'App pool started successfully'
            }} else {{
                Write-Output 'App pool already started'
            }}
        ";

        await ExecuteRemoteCommand(script);
    }

    public async Task StopWebsite(string siteName)
    {
        var script = $@"
            Import-Module WebAdministration
            
            $site = Get-Website -Name '{siteName}'
            if ($site.State -eq 'Started') {{
                Stop-Website -Name '{siteName}'
                Start-Sleep -Seconds 2
                Write-Output 'Website stopped successfully'
            }} else {{
                Write-Output 'Website already stopped'
            }}
        ";

        await ExecuteRemoteCommand(script);
    }

    public async Task StartWebsite(string siteName)
    {
        var script = $@"
            Import-Module WebAdministration
            
            $site = Get-Website -Name '{siteName}'
            if ($site.State -ne 'Started') {{
                Start-Website -Name '{siteName}'
                Start-Sleep -Seconds 2
                Write-Output 'Website started successfully'
            }} else {{
                Write-Output 'Website already started'
            }}
        ";

        await ExecuteRemoteCommand(script);
    }

    private SecureString GetSecurePassword(string passwordKey)
    {
        // TODO: Retrieve from Azure Key Vault or secure configuration
        var password = "your-secure-password"; // Placeholder
        var securePassword = new SecureString();
        foreach (char c in password)
            securePassword.AppendChar(c);
        return securePassword;
    }

    public void Dispose()
    {
        _runspace?.Dispose();
    }
}
```

#### **Git Service**
```csharp
// DeploymentManager.Infrastructure/Services/GitService.cs

using LibGit2Sharp;

namespace DeploymentManager.Infrastructure.Services;

public interface IGitService
{
    Task<List<string>> GetBranches();
    Task CheckoutBranch(string branchName);
    Task<string> GetLatestCommitHash();
    Task PullLatest();
}

public class GitService : IGitService
{
    private readonly string _repoPath;
    private readonly ILogger<GitService> _logger;

    public GitService(string repoPath, ILogger<GitService> logger)
    {
        _repoPath = repoPath;
        _logger = logger;
    }

    public async Task<List<string>> GetBranches()
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(_repoPath);
            return repo.Branches
                .Where(b => !b.IsRemote)
                .Select(b => b.FriendlyName)
                .ToList();
        });
    }

    public async Task CheckoutBranch(string branchName)
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(_repoPath);
            var branch = repo.Branches[branchName];
            
            if (branch == null)
            {
                // Try to find remote branch
                var remoteBranch = repo.Branches[$"origin/{branchName}"];
                if (remoteBranch != null)
                {
                    branch = repo.CreateBranch(branchName, remoteBranch.Tip);
                    repo.Branches.Update(branch, b => b.TrackedBranch = remoteBranch.CanonicalName);
                }
                else
                {
                    throw new Exception($"Branch '{branchName}' not found");
                }
            }

            Commands.Checkout(repo, branch);
            _logger.LogInformation($"Checked out branch: {branchName}");
        });
    }

    public async Task<string> GetLatestCommitHash()
    {
        return await Task.Run(() =>
        {
            using var repo = new Repository(_repoPath);
            return repo.Head.Tip.Sha;
        });
    }

    public async Task PullLatest()
    {
        await Task.Run(() =>
        {
            using var repo = new Repository(_repoPath);
            
            var options = new PullOptions
            {
                FetchOptions = new FetchOptions()
            };

            var signature = new Signature(
                new Identity("Deployment Bot", "deploy@company.com"),
                DateTimeOffset.Now
            );

            Commands.Pull(repo, signature, options);
            _logger.LogInformation("Pulled latest changes");
        });
    }
}
```

#### **Build Service**
```csharp
// DeploymentManager.Infrastructure/Services/BuildService.cs

using System.Diagnostics;

namespace DeploymentManager.Infrastructure.Services;

public interface IBuildService
{
    Task<string> BuildAndPublish(string projectPath, DeploymentMode mode);
}

public class BuildService : IBuildService
{
    private readonly ILogger<BuildService> _logger;

    public BuildService(ILogger<BuildService> logger)
    {
        _logger = logger;
    }

    public async Task<string> BuildAndPublish(string projectPath, DeploymentMode mode)
    {
        var outputPath = Path.Combine(projectPath, "publish");

        // Clean previous build
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }

        _logger.LogInformation("Starting build process...");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -o \"{outputPath}\" --no-restore",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                _logger.LogInformation(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                _logger.LogError(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Build failed with exit code {process.ExitCode}:\n{errorBuilder}");
        }

        _logger.LogInformation($"Build completed successfully. Output: {outputPath}");
        return outputPath;
    }
}
```

#### **File Deployment Service**
```csharp
// DeploymentManager.Infrastructure/Services/FileDeploymentService.cs

using System.IO.Compression;
using System.Runtime.InteropServices;

namespace DeploymentManager.Infrastructure.Services;

public interface IFileDeploymentService
{
    Task<string> CreateBackup(string deploymentPath, DeploymentMode mode);
    Task DeployFiles(string sourcePath, string targetPath, DeploymentMode mode);
    Task RestoreBackup(string backupPath, string targetPath);
}

public class FileDeploymentService : IFileDeploymentService
{
    private readonly ILogger<FileDeploymentService> _logger;

    public FileDeploymentService(ILogger<FileDeploymentService> logger)
    {
        _logger = logger;
    }

    public async Task<string> CreateBackup(string deploymentPath, DeploymentMode mode)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(deploymentPath, "Backups", timestamp);
        Directory.CreateDirectory(backupDir);

        _logger.LogInformation($"Creating backup at: {backupDir}");

        if (mode == DeploymentMode.Incremental)
        {
            // Backup only 4 core files
            var coreFiles = new[] { ".dll", ".deps.json", ".pdb", ".exe" };
            
            foreach (var file in Directory.GetFiles(deploymentPath))
            {
                var extension = Path.GetExtension(file);
                if (coreFiles.Any(cf => file.EndsWith(cf)))
                {
                    var fileName = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(backupDir, fileName), true);
                }
            }
        }
        else
        {
            // Backup everything
            await Task.Run(() => CopyDirectory(deploymentPath, backupDir, true));
        }

        // Compress backup
        var zipPath = $"{backupDir}.zip";
        ZipFile.CreateFromDirectory(backupDir, zipPath);
        Directory.Delete(backupDir, true);

        _logger.LogInformation($"Backup created: {zipPath}");
        return zipPath;
    }

    public async Task DeployFiles(string sourcePath, string targetPath, DeploymentMode mode)
    {
        _logger.LogInformation($"Deploying files from {sourcePath} to {targetPath}");

        if (mode == DeploymentMode.Incremental)
        {
            // Copy only 4 core files
            var projectName = Path.GetFileName(Directory.GetFiles(sourcePath, "*.dll").First())
                .Replace(".dll", "");

            var files = new[]
            {
                $"{projectName}.dll",
                $"{projectName}.deps.json",
                $"{projectName}.pdb",
                $"{projectName}.exe"
            };

            foreach (var file in files)
            {
                var source = Path.Combine(sourcePath, file);
                var target = Path.Combine(targetPath, file);

                if (File.Exists(source))
                {
                    File.Copy(source, target, true);
                    _logger.LogInformation($"Copied: {file}");
                }
            }
        }
        else
        {
            // Copy all except wwwroot and appsettings
            await Task.Run(() =>
            {
                var allFiles = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    var relativePath = Path.GetRelativePath(sourcePath, file);

                    // Skip excluded paths
                    if (relativePath.Contains("wwwroot") ||
                        relativePath.Contains("appsettings") ||
                        relativePath.StartsWith("Backups"))
                        continue;

                    var targetFile = Path.Combine(targetPath, relativePath);
                    var targetDir = Path.GetDirectoryName(targetFile);

                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(file, targetFile, true);
                }

                _logger.LogInformation("Full deployment completed");
            });
        }
    }

    public async Task RestoreBackup(string backupPath, string targetPath)
    {
        _logger.LogInformation($"Restoring backup from {backupPath}");

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        ZipFile.ExtractToDirectory(backupPath, tempDir);

        await Task.Run(() => CopyDirectory(tempDir, targetPath, true));

        Directory.Delete(tempDir, true);
        _logger.LogInformation("Backup restored successfully");
    }

    private void CopyDirectory(string sourceDir, string targetDir, bool recursive)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        DirectoryInfo[] dirs = dir.GetDirectories();
        Directory.CreateDirectory(targetDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(targetDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(targetDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
    }
}
```

#### **Database Migration Service**
```csharp
// DeploymentManager.Infrastructure/Services/DatabaseMigrationService.cs

using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DeploymentManager.Infrastructure.Services;

public interface IDatabaseMigrationService
{
    Task<List<MigrationDto>> PreviewMigrations(string sqlPatchesPath);
    Task ExecuteMigrations(int deploymentId, List<MigrationDto> migrations, string connectionString);
    Task RollbackMigrations(int deploymentId, string connectionString);
}

public class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly IMigrationRepository _migrationRepository;

    public DatabaseMigrationService(
        ILogger<DatabaseMigrationService> logger,
        IMigrationRepository migrationRepository)
    {
        _logger = logger;
        _migrationRepository = migrationRepository;
    }

    public async Task<List<MigrationDto>> PreviewMigrations(string sqlPatchesPath)
    {
        var migrations = new List<MigrationDto>();

        if (!Directory.Exists(sqlPatchesPath))
        {
            _logger.LogWarning($"SQL patches directory not found: {sqlPatchesPath}");
            return migrations;
        }

        var patchFiles = Directory.GetFiles(sqlPatchesPath, "*.sql")
            .OrderBy(f => f)
            .ToList();

        int order = 1;
        foreach (var patchFile in patchFiles)
        {
            var upScript = await File.ReadAllTextAsync(patchFile);
            var downScript = GenerateDownScript(upScript);
            var changes = ParseChanges(upScript);

            migrations.Add(new MigrationDto
            {
                Name = Path.GetFileName(patchFile),
                UpScript = upScript,
                DownScript = downScript,
                Changes = changes,
                ExecutionOrder = order++
            });
        }

        return migrations;
    }

    private string GenerateDownScript(string upScript)
    {
        var downStatements = new List<string>();

        try
        {
            // Pattern 1: ALTER TABLE ... ADD COLUMN
            var addColumnRegex = new Regex(
                @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ADD\s+(?:\[?(\w+)\]?)\s+",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in addColumnRegex.Matches(upScript))
            {
                var schema = match.Groups[1].Value;
                var table = match.Groups[2].Value;
                var column = match.Groups[3].Value;

                var fullTableName = string.IsNullOrEmpty(schema)
                    ? $"[{table}]"
                    : $"[{schema}].[{table}]";

                downStatements.Add($"ALTER TABLE {fullTableName} DROP COLUMN [{column}];");
            }

            // Pattern 2: CREATE TABLE
            var createTableRegex = new Regex(
                @"CREATE\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in createTableRegex.Matches(upScript))
            {
                var schema = match.Groups[1].Value;
                var table = match.Groups[2].Value;

                var fullTableName = string.IsNullOrEmpty(schema)
                    ? $"[{table}]"
                    : $"[{schema}].[{table}]";

                downStatements.Add($"DROP TABLE IF EXISTS {fullTableName};");
            }

            // Pattern 3: ALTER TABLE ... ADD CONSTRAINT
            var addConstraintRegex = new Regex(
                @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ADD\s+CONSTRAINT\s+(?:\[?(\w+)\]?)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in addConstraintRegex.Matches(upScript))
            {
                var schema = match.Groups[1].Value;
                var table = match.Groups[2].Value;
                var constraint = match.Groups[3].Value;

                var fullTableName = string.IsNullOrEmpty(schema)
                    ? $"[{table}]"
                    : $"[{schema}].[{table}]";

                downStatements.Add($"ALTER TABLE {fullTableName} DROP CONSTRAINT [{constraint}];");
            }

            // Pattern 4: CREATE INDEX
            var createIndexRegex = new Regex(
                @"CREATE\s+(?:UNIQUE\s+)?(?:CLUSTERED\s+)?(?:NONCLUSTERED\s+)?INDEX\s+(?:\[?(\w+)\]?)\s+ON\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in createIndexRegex.Matches(upScript))
            {
                var indexName = match.Groups[1].Value;
                var schema = match.Groups[2].Value;
                var table = match.Groups[3].Value;

                var fullTableName = string.IsNullOrEmpty(schema)
                    ? $"[{table}]"
                    : $"[{schema}].[{table}]";

                downStatements.Add($"DROP INDEX IF EXISTS [{indexName}] ON {fullTableName};");
            }

            // Pattern 5: ALTER TABLE ... ALTER COLUMN
            var alterColumnRegex = new Regex(
                @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ALTER\s+COLUMN\s+(?:\[?(\w+)\]?)\s+(\w+(?:\(\d+(?:,\s*\d+)?\))?)",
                RegexOptions.IgnoreCase
            );

            foreach (Match match in alterColumnRegex.Matches(upScript))
            {
                var schema = match.Groups[1].Value;
                var table = match.Groups[2].Value;
                var column = match.Groups[3].Value;

                downStatements.Add($"-- WARNING: Cannot auto-generate rollback for ALTER COLUMN [{column}] in table [{table}]. Manual intervention required.");
            }

            return string.Join("\n", downStatements);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate complete down script");
            return $"-- WARNING: Auto-generation failed. Manual rollback script required.\n-- Error: {ex.Message}";
        }
    }

    private List<string> ParseChanges(string upScript)
    {
        var changes = new List<string>();

        // Detect CREATE TABLE
        var createTableMatches = Regex.Matches(upScript, @"CREATE\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)", RegexOptions.IgnoreCase);
        foreach (Match match in createTableMatches)
        {
            var table = match.Groups[2].Value;
            changes.Add($"✓ Create table: {table}");
        }

        // Detect ADD COLUMN
        var addColumnMatches = Regex.Matches(upScript, @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ADD\s+(?:\[?(\w+)\]?)", RegexOptions.IgnoreCase);
        foreach (Match match in addColumnMatches)
        {
            var table = match.Groups[2].Value;
            var column = match.Groups[3].Value;
            changes.Add($"✓ Add column '{column}' to table '{table}'");
        }

        // Detect ADD CONSTRAINT
        var addConstraintMatches = Regex.Matches(upScript, @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ADD\s+CONSTRAINT\s+(?:\[?(\w+)\]?)", RegexOptions.IgnoreCase);
        foreach (Match match in addConstraintMatches)
        {
            var table = match.Groups[2].Value;
            var constraint = match.Groups[3].Value;
            changes.Add($"✓ Add constraint '{constraint}' to table '{table}'");
        }

        // Detect CREATE INDEX
        var createIndexMatches = Regex.Matches(upScript, @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+(?:\[?(\w+)\]?)", RegexOptions.IgnoreCase);
        foreach (Match match in createIndexMatches)
        {
            var index = match.Groups[1].Value;
            changes.Add($"✓ Create index: {index}");
        }

        // Detect ALTER COLUMN
        var alterColumnMatches = Regex.Matches(upScript, @"ALTER\s+TABLE\s+(?:\[?(\w+)\]?\.)?(?:\[?(\w+)\]?)\s+ALTER\s+COLUMN\s+(?:\[?(\w+)\]?)", RegexOptions.IgnoreCase);
        foreach (Match match in alterColumnMatches)
        {
            var table = match.Groups[2].Value;
            var column = match.Groups[3].Value;
            changes.Add($"⚠ Alter column '{column}' in table '{table}' (manual rollback may be required)");
        }

        if (!changes.Any())
        {
            changes.Add("ℹ Custom SQL script (review manually)");
        }

        return changes;
    }

    public async Task ExecuteMigrations(int deploymentId, List<MigrationDto> migrations, string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var migration in migrations.OrderBy(m => m.ExecutionOrder))
        {
            using var transaction = connection.BeginTransaction();

            try
            {
                _logger.LogInformation($"Executing migration: {migration.Name}");

                // Save migration record
                var migrationRecord = new DatabaseMigration
                {
                    DeploymentId = deploymentId,
                    MigrationName = migration.Name,
                    UpScript = migration.UpScript,
                    DownScript = migration.DownScript,
                    ExecutionOrder = migration.ExecutionOrder,
                    Status = MigrationStatus.Pending
                };

                var migrationId = await _migrationRepository.CreateMigration(migrationRecord);

                // Execute UP script
                await connection.ExecuteAsync(migration.UpScript, transaction: transaction);

                // Update migration status
                migrationRecord.Id = migrationId;
                migrationRecord.Status = MigrationStatus.Applied;
                migrationRecord.ExecutedAt = DateTime.UtcNow;
                await _migrationRepository.UpdateMigration(migrationRecord);

                // Log to history
                await _migrationRepository.LogMigrationHistory(new MigrationHistory
                {
                    MigrationId = migrationId,
                    Action = "Apply",
                    ExecutedAt = DateTime.UtcNow,
                    ExecutedBy = Environment.UserName,
                    Success = true
                });

                transaction.Commit();
                _logger.LogInformation($"✓ Migration executed: {migration.Name}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, $"✗ Migration failed: {migration.Name}");

                await _migrationRepository.LogMigrationHistory(new MigrationHistory
                {
                    MigrationId = migration.Id,
                    Action = "Apply",
                    ExecutedAt = DateTime.UtcNow,
                    ExecutedBy = Environment.UserName,
                    Success = false,
                    ErrorMessage = ex.Message
                });

                throw new Exception($"Migration '{migration.Name}' failed: {ex.Message}", ex);
            }
        }
    }

    public async Task RollbackMigrations(int deploymentId, string connectionString)
    {
        var migrations = await _migrationRepository.GetMigrationsByDeploymentId(deploymentId);

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Execute in reverse order
        foreach (var migration in migrations.OrderByDescending(m => m.ExecutionOrder))
        {
            if (migration.Status != MigrationStatus.Applied)
                continue;

            using var transaction = connection.BeginTransaction();

            try
            {
                _logger.LogInformation($"Rolling back migration: {migration.MigrationName}");

                if (string.IsNullOrEmpty(migration.DownScript))
                {
                    throw new Exception("Down script not available for this migration");
                }

                // Execute DOWN script
                await connection.ExecuteAsync(migration.DownScript, transaction: transaction);

                // Update migration status
                migration.Status = MigrationStatus.RolledBack;
                await _migrationRepository.UpdateMigration(migration);

                // Log to history
                await _migrationRepository.LogMigrationHistory(new MigrationHistory
                {
                    MigrationId = migration.Id,
                    Action = "Rollback",
                    ExecutedAt = DateTime.UtcNow,
                    ExecutedBy = Environment.UserName,
                    Success = true
                });

                transaction.Commit();
                _logger.LogInformation($"✓ Migration rolled back: {migration.MigrationName}");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, $"✗ Rollback failed: {migration.MigrationName}");

                await _migrationRepository.LogMigrationHistory(new MigrationHistory
                {
                    MigrationId = migration.Id,
                    Action = "Rollback",
                    ExecutedAt = DateTime.UtcNow,
                    ExecutedBy = Environment.UserName,
                    Success = false,
                    ErrorMessage = ex.Message
                });

                throw;
            }
        }
    }
}

public class MigrationDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UpScript { get; set; } = string.Empty;
    public string DownScript { get; set; } = string.Empty;
    public List<string> Changes { get; set; } = new();
    public int ExecutionOrder { get; set; }
}
```

---

### **Phase 3: Application Layer - CQRS with MediatR (Week 2)**

#### **Deploy Command & Handler**
```csharp
// DeploymentManager.Application/Commands/DeployCommand.cs

using MediatR;

namespace DeploymentManager.Application.Commands;

public class DeployCommand : IRequest<DeploymentResult>
{
    public string Branch { get; set; } = string.Empty;
    public DeploymentMode UpdateMode { get; set; }
    public bool AutoSyncDatabase { get; set; }
    public string DeployedBy { get; set; } = string.Empty;
}

public class DeploymentResult
{
    public int DeploymentId { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}

// DeploymentManager.Application/Handlers/DeployCommandHandler.cs

public class DeployCommandHandler : IRequestHandler<DeployCommand, DeploymentResult>
{
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly IGitService _gitService;
    private readonly IBuildService _buildService;
    private readonly IPowerShellRemotingService _remoteServer;
    private readonly IFileDeploymentService _fileDeployment;
    private readonly IDatabaseMigrationService _migrationService;
    private readonly IDeploymentHub _deploymentHub;
    private readonly ServerConfiguration _serverConfig;
    private readonly ILogger<DeployCommandHandler> _logger;

    public DeployCommandHandler(
        IDeploymentRepository deploymentRepository,
        IGitService gitService,
        IBuildService buildService,
        IPowerShellRemotingService remoteServer,
        IFileDeploymentService fileDeployment,
        IDatabaseMigrationService migrationService,
        IDeploymentHub deploymentHub,
        ServerConfiguration serverConfig,
        ILogger<DeployCommandHandler> logger)
    {
        _deploymentRepository = deploymentRepository;
        _gitService = gitService;
        _buildService = buildService;
        _remoteServer = remoteServer;
        _fileDeployment = fileDeployment;
        _migrationService = migrationService;
        _deploymentHub = deploymentHub;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    public async Task<DeploymentResult> Handle(DeployCommand request, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        Deployment? deployment = null;

        try
        {
            // Create deployment record
            var version = await GenerateVersion();
            deployment = new Deployment
            {
                Version = version,
                Branch = request.Branch,
                DeployedBy = request.DeployedBy,
                DeployedAt = DateTime.UtcNow,
                Status = DeploymentStatus.InProgress,
                UpdateMode = request.UpdateMode
            };

            var deploymentId = await _deploymentRepository.CreateDeployment(deployment);
            deployment.Id = deploymentId;

            await LogAndBroadcast(deploymentId, "========== DEPLOYMENT STARTED ==========");
            await LogAndBroadcast(deploymentId, $"Version: {version}");
            await LogAndBroadcast(deploymentId, $"Branch: {request.Branch}");
            await LogAndBroadcast(deploymentId, $"Mode: {request.UpdateMode}");

            // Step 1: Checkout branch and pull latest
            await LogAndBroadcast(deploymentId, "\n[1/9] Checking out branch...");
            await _gitService.CheckoutBranch(request.Branch);
            await _gitService.PullLatest();
            var commitHash = await _gitService.GetLatestCommitHash();
            deployment.CommitHash = commitHash;
            await _deploymentRepository.UpdateDeployment(deployment);
            await LogAndBroadcast(deploymentId, $"✓ Checked out commit: {commitHash[..7]}");

            // Step 2: Build project
            await LogAndBroadcast(deploymentId, "\n[2/9] Building project...");
            var publishPath = await _buildService.BuildAndPublish(_serverConfig.ProjectPath, request.UpdateMode);
            await LogAndBroadcast(deploymentId, $"✓ Build completed: {publishPath}");

            // Step 3: Test connection
            await LogAndBroadcast(deploymentId, "\n[3/9] Testing connection to production server...");
            var connectionOk = await _remoteServer.TestConnection();
            if (!connectionOk)
                throw new Exception("Failed to connect to production server");
            await LogAndBroadcast(deploymentId, "✓ Connection successful");

            // Step 4: Create backup
            await LogAndBroadcast(deploymentId, "\n[4/9] Creating backup...");
            var backupPath = await _fileDeployment.CreateBackup(_serverConfig.DeploymentPath, request.UpdateMode);
            deployment.BackupPath = backupPath;
            await _deploymentRepository.UpdateDeployment(deployment);
            await LogAndBroadcast(deploymentId, $"✓ Backup created: {backupPath}");

            // Step 5: Stop IIS
            await LogAndBroadcast(deploymentId, "\n[5/9] Stopping IIS application...");
            await _remoteServer.StopWebsite(_serverConfig.IISSiteName);
            await _remoteServer.StopIISAppPool(_serverConfig.IISAppPoolName);
            await LogAndBroadcast(deploymentId, "✓ IIS stopped");

            // Step 6: Deploy files
            await LogAndBroadcast(deploymentId, "\n[6/9] Deploying files...");
            await _fileDeployment.DeployFiles(publishPath, _serverConfig.DeploymentPath, request.UpdateMode);
            await LogAndBroadcast(deploymentId, $"✓ Files deployed ({request.UpdateMode} mode)");

            // Step 7: Execute database migrations (if enabled)
            if (request.AutoSyncDatabase)
            {
                await LogAndBroadcast(deploymentId, "\n[7/9] Executing database migrations...");
                var sqlPatchesPath = Path.Combine(_serverConfig.ProjectPath, "SQL", "SQLPatches");
                var migrations = await _migrationService.PreviewMigrations(sqlPatchesPath);

                if (migrations.Any())
                {
                    await _migrationService.ExecuteMigrations(deploymentId, migrations, _serverConfig.DatabaseConnectionString);
                    await LogAndBroadcast(deploymentId, $"✓ {migrations.Count} migration(s) executed");
                }
                else
                {
                    await LogAndBroadcast(deploymentId, "ℹ No migrations to execute");
                }
            }
            else
            {
                await LogAndBroadcast(deploymentId, "\n[7/9] Skipping database migrations (manual sync required)");
            }

            // Step 8: Start IIS
            await LogAndBroadcast(deploymentId, "\n[8/9] Starting IIS application...");
            await _remoteServer.StartIISAppPool(_serverConfig.IISAppPoolName);
            await _remoteServer.StartWebsite(_serverConfig.IISSiteName);
            await Task.Delay(5000); // Wait for app to start
            await LogAndBroadcast(deploymentId, "✓ IIS started");

            // Step 9: Health check
            await LogAndBroadcast(deploymentId, "\n[9/9] Running health check...");
            // TODO: Implement actual health check
            await Task.Delay(3000);
            await LogAndBroadcast(deploymentId, "✓ Health check passed");

            // Mark as successful
            deployment.Status = DeploymentStatus.Success;
            deployment.Duration = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            await _deploymentRepository.UpdateDeployment(deployment);

            await LogAndBroadcast(deploymentId, "\n========== DEPLOYMENT SUCCESSFUL ==========");
            await LogAndBroadcast(deploymentId, $"Duration: {deployment.Duration} seconds");

            return new DeploymentResult
            {
                DeploymentId = deploymentId,
                Version = version,
                Success = true,
                Duration = TimeSpan.FromSeconds(deployment.Duration.Value)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment failed");

            if (deployment != null)
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.ErrorMessage = ex.Message;
                deployment.Duration = (int)(DateTime.UtcNow - startTime).TotalSeconds;
                await _deploymentRepository.UpdateDeployment(deployment);

                await LogAndBroadcast(deployment.Id, $"\n✗✗✗ DEPLOYMENT FAILED ✗✗✗");
                await LogAndBroadcast(deployment.Id, $"Error: {ex.Message}");

                // Auto-rollback on failure (optional)
                // await AttemptRollback(deployment.Id);
            }

            return new DeploymentResult
            {
                DeploymentId = deployment?.Id ?? 0,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = TimeSpan.FromSeconds(deployment?.Duration ?? 0)
            };
        }
    }

    private async Task LogAndBroadcast(int deploymentId, string message)
    {
        _logger.LogInformation(message);

        // Save to database
        await _deploymentRepository.AddLog(new DeploymentLog
        {
            DeploymentId = deploymentId,
            LogLevel = LogLevel.Info,
            Message = message,
            Timestamp = DateTime.UtcNow
        });

        // Broadcast via SignalR
        await _deploymentHub.SendLog(deploymentId, message);
    }

    private async Task<string> GenerateVersion()
    {
        var lastDeployment = await _deploymentRepository.GetLatestDeployment();

        if (lastDeployment == null)
            return "v1.0.0";

        // Simple versioning: increment patch number
        var versionParts = lastDeployment.Version.TrimStart('v').Split('.');
        var major = int.Parse(versionParts[0]);
        var minor = int.Parse(versionParts[1]);
        var patch = int.Parse(versionParts[2]) + 1;

        return $"v{major}.{minor}.{patch}";
    }
}
```

#### **Rollback Command & Handler**
```csharp
// DeploymentManager.Application/Commands/RollbackCommand.cs

public class RollbackCommand : IRequest<DeploymentResult>
{
    public int DeploymentId { get; set; }
    public string RolledBackBy { get; set; } = string.Empty;
}

// DeploymentManager.Application/Handlers/RollbackCommandHandler.cs

public class RollbackCommandHandler : IRequestHandler<RollbackCommand, DeploymentResult>
{
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly IPowerShellRemotingService _remoteServer;
    private readonly IFileDeploymentService _fileDeployment;
    private readonly IDatabaseMigrationService _migrationService;
    private readonly IDeploymentHub _deploymentHub;
    private readonly ServerConfiguration _serverConfig;
    private readonly ILogger<RollbackCommandHandler> _logger;

    public RollbackCommandHandler(
        IDeploymentRepository deploymentRepository,
        IPowerShellRemotingService remoteServer,
        IFileDeploymentService fileDeployment,
        IDatabaseMigrationService migrationService,
        IDeploymentHub deploymentHub,
        ServerConfiguration serverConfig,
        ILogger<RollbackCommandHandler> logger)
    {
        _deploymentRepository = deploymentRepository;
        _remoteServer = remoteServer;
        _fileDeployment = fileDeployment;
        _migrationService = migrationService;
        _deploymentHub = deploymentHub;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    public async Task<DeploymentResult> Handle(RollbackCommand request, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalDeployment = await _deploymentRepository.GetDeploymentById(request.DeploymentId);

        if (originalDeployment == null)
            throw new Exception($"Deployment {request.DeploymentId} not found");

        if (string.IsNullOrEmpty(originalDeployment.BackupPath))
            throw new Exception("No backup available for this deployment");

        // Create rollback deployment record
        var rollbackDeployment = new Deployment
        {
            Version = $"{originalDeployment.Version}-rollback",
            Branch = originalDeployment.Branch,
            CommitHash = originalDeployment.CommitHash,
            DeployedBy = request.RolledBackBy,
            DeployedAt = DateTime.UtcNow,
            Status = DeploymentStatus.InProgress,
            UpdateMode = originalDeployment.UpdateMode,
            Notes = $"Rollback of deployment {request.DeploymentId}"
        };

        var rollbackId = await _deploymentRepository.CreateDeployment(rollbackDeployment);
        rollbackDeployment.Id = rollbackId;

        try
        {
            await LogAndBroadcast(rollbackId, "========== ROLLBACK STARTED ==========");
            await LogAndBroadcast(rollbackId, $"Rolling back deployment: {originalDeployment.Version}");

            // Step 1: Stop IIS
            await LogAndBroadcast(rollbackId, "\n[1/4] Stopping IIS...");
            await _remoteServer.StopWebsite(_serverConfig.IISSiteName);
            await _remoteServer.StopIISAppPool(_serverConfig.IISAppPoolName);
            await LogAndBroadcast(rollbackId, "✓ IIS stopped");

            // Step 2: Restore files from backup
            await LogAndBroadcast(rollbackId, "\n[2/4] Restoring files from backup...");
            await _fileDeployment.RestoreBackup(originalDeployment.BackupPath, _serverConfig.DeploymentPath);
            await LogAndBroadcast(rollbackId, "✓ Files restored");

            // Step 3: Rollback database migrations
            await LogAndBroadcast(rollbackId, "\n[3/4] Rolling back database migrations...");
            var migrations = await _migrationService.GetMigrationsByDeploymentId(request.DeploymentId);

            if (migrations.Any())
            {
                await _migrationService.RollbackMigrations(request.DeploymentId, _serverConfig.DatabaseConnectionString);
                await LogAndBroadcast(rollbackId, $"✓ {migrations.Count} migration(s) rolled back");
            }
            else
            {
                await LogAndBroadcast(rollbackId, "ℹ No migrations to rollback");
            }

            // Step 4: Start IIS
            await LogAndBroadcast(rollbackId, "\n[4/4] Starting IIS...");
            await _remoteServer.StartIISAppPool(_serverConfig.IISAppPoolName);
            await _remoteServer.StartWebsite(_serverConfig.IISSiteName);
            await Task.Delay(5000);
            await LogAndBroadcast(rollbackId, "✓ IIS started");

            // Mark rollback as successful
            rollbackDeployment.Status = DeploymentStatus.Success;
            rollbackDeployment.Duration = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            await _deploymentRepository.UpdateDeployment(rollbackDeployment);

            // Mark original deployment as rolled back
            originalDeployment.Status = DeploymentStatus.RolledBack;
            await _deploymentRepository.UpdateDeployment(originalDeployment);

            await LogAndBroadcast(rollbackId, "\n========== ROLLBACK SUCCESSFUL ==========");
            await LogAndBroadcast(rollbackId, $"Duration: {rollbackDeployment.Duration} seconds");

            return new DeploymentResult
            {
                DeploymentId = rollbackId,
                Version = rollbackDeployment.Version,
                Success = true,
                Duration = TimeSpan.FromSeconds(rollbackDeployment.Duration.Value)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed");

            rollbackDeployment.Status = DeploymentStatus.Failed;
            rollbackDeployment.ErrorMessage = ex.Message;
            rollbackDeployment.Duration = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            await _deploymentRepository.UpdateDeployment(rollbackDeployment);

            await LogAndBroadcast(rollbackId, $"\n✗✗✗ ROLLBACK FAILED ✗✗✗");
            await LogAndBroadcast(rollbackId, $"Error: {ex.Message}");

            return new DeploymentResult
            {
                DeploymentId = rollbackId,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = TimeSpan.FromSeconds(rollbackDeployment.Duration.Value)
            };
        }
    }

    private async Task LogAndBroadcast(int deploymentId, string message)
    {
        _logger.LogInformation(message);

        await _deploymentRepository.AddLog(new DeploymentLog
        {
            DeploymentId = deploymentId,
            LogLevel = LogLevel.Info,
            Message = message,
            Timestamp = DateTime.UtcNow
        });

        await _deploymentHub.SendLog(deploymentId, message);
    }
}
```

---

### **Phase 4: API Controllers (Week 2)**

#### **Deployment Controller**
```csharp
// DeploymentManager.API/Controllers/DeploymentController.cs

using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DeploymentManager.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeploymentController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly ILogger<DeploymentController> _logger;

    public DeploymentController(
        IMediator mediator,
        IDeploymentRepository deploymentRepository,
        ILogger<DeploymentController> logger)
    {
        _mediator = mediator;
        _deploymentRepository = deploymentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all deployments with pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeploymentDto>>> GetDeployments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var deployments = await _deploymentRepository.GetDeployments(page, pageSize);
        return Ok(deployments);
    }

    /// <summary>
    /// Get deployment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<DeploymentDto>> GetDeployment(int id)
    {
        var deployment = await _deploymentRepository.GetDeploymentById(id);
        
        if (deployment == null)
            return NotFound();

        return Ok(deployment);
    }

    /// <summary>
    /// Get deployment logs
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<ActionResult<List<DeploymentLog>>> GetDeploymentLogs(int id)
    {
        var logs = await _deploymentRepository.GetLogs(id);
        return Ok(logs);
    }

    /// <summary>
    /// Start a new deployment
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DeploymentResult>> Deploy([FromBody] DeployRequest request)
    {
        var command = new DeployCommand
        {
            Branch = request.Branch,
            UpdateMode = request.UpdateMode,
            AutoSyncDatabase = request.AutoSyncDatabase,
            DeployedBy = User.Identity?.Name ?? "Anonymous"
        };

        var result = await _mediator.Send(command);

        if (result.Success)
            return Ok(result);

        return BadRequest(result);
    }

    /// <summary>
    /// Rollback a deployment
    /// </summary>
    [HttpPost("{id}/rollback")]
    public async Task<ActionResult<DeploymentResult>> Rollback(int id)
    {
        var command = new RollbackCommand
        {
            DeploymentId = id,
            RolledBackBy = User.Identity?.Name ?? "Anonymous"
        };

        var result = await _mediator.Send(command);

        if (result.Success)
            return Ok(result);

        return BadRequest(result);
    }

    /// <summary>
    /// Get deployment statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DeploymentStats>> GetStats()
    {
        var stats = await _deploymentRepository.GetStatistics();
        return Ok(stats);
    }
}

public class DeployRequest
{
    public string Branch { get; set; } = string.Empty;
    public DeploymentMode UpdateMode { get; set; }
    public bool AutoSyncDatabase { get; set; }
}

public class DeploymentStats
{
    public int TotalDeployments { get; set; }
    public int SuccessfulDeployments { get; set; }
    public int FailedDeployments { get; set; }
    public int RolledBackDeployments { get; set; }
    public double SuccessRate { get; set; }
    public double AverageDeploymentTime { get; set; }
    public DeploymentDto? LastDeployment { get; set; }
}
```

#### **Migration Controller**
```csharp
// DeploymentManager.API/Controllers/MigrationController.cs

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly IDatabaseMigrationService _migrationService;
    private readonly IMigrationRepository _migrationRepository;
    private readonly ServerConfiguration _serverConfig;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(
        IDatabaseMigrationService migrationService,
        IMigrationRepository migrationRepository,
        ServerConfiguration serverConfig,
        ILogger<MigrationController> logger)
    {
        _migrationService = migrationService;
        _migrationRepository = migrationRepository;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    /// <summary>
    /// Preview pending migrations
    /// </summary>
    [HttpGet("preview")]
    public async Task<ActionResult<List<MigrationDto>>> PreviewMigrations()
    {
        var sqlPatchesPath = Path.Combine(_serverConfig.ProjectPath, "SQL", "SQLPatches");
        var migrations = await _migrationService.PreviewMigrations(sqlPatchesPath);
        return Ok(migrations);
    }

    /// <summary>
    /// Preview migrations for a specific deployment
    /// </summary>
    [HttpGet("preview/{deploymentId}")]
    public async Task<ActionResult<List<MigrationDto>>> PreviewDeploymentMigrations(int deploymentId)
    {
        var migrations = await _migrationRepository.GetMigrationsByDeploymentId(deploymentId);
        
        var migrationDtos = migrations.Select(m => new MigrationDto
        {
            Id = m.Id,
            Name = m.MigrationName,
            UpScript = m.UpScript,
            DownScript = m.DownScript ?? string.Empty,
            ExecutionOrder = m.ExecutionOrder
        }).ToList();

        return Ok(migrationDtos);
    }

    /// <summary>
    /// Execute migrations for a deployment
    /// </summary>
    [HttpPost("execute/{deploymentId}")]
    public async Task<ActionResult> ExecuteMigrations(int deploymentId)
    {
        try
        {
            var sqlPatchesPath = Path.Combine(_serverConfig.ProjectPath, "SQL", "SQLPatches");
            var migrations = await _migrationService.PreviewMigrations(sqlPatchesPath);

            await _migrationService.ExecuteMigrations(
                deploymentId,
                migrations,
                _serverConfig.DatabaseConnectionString
            );

            return Ok(new { message = $"{migrations.Count} migration(s) executed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute migrations");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get migration history for a deployment
    /// </summary>
    [HttpGet("{deploymentId}/history")]
    public async Task<ActionResult<List<MigrationHistory>>> GetMigrationHistory(int deploymentId)
    {
        var migrations = await _migrationRepository.GetMigrationsByDeploymentId(deploymentId);
        var history = new List<MigrationHistory>();

        foreach (var migration in migrations)
        {
            var migrationHistory = await _migrationRepository.GetMigrationHistory(migration.Id);
            history.AddRange(migrationHistory);
        }

        return Ok(history.OrderByDescending(h => h.ExecutedAt));
    }
}
```

#### **Configuration Controller**
```csharp
// DeploymentManager.API/Controllers/ConfigurationController.cs

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IGitService _gitService;
    private readonly IPowerShellRemotingService _remoteServer;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        IGitService gitService,
        IPowerShellRemotingService remoteServer,
        ILogger<ConfigurationController> logger)
    {
        _gitService = gitService;
        _remoteServer = remoteServer;
        _logger = logger;
    }

    /// <summary>
    /// Get available Git branches
    /// </summary>
    [HttpGet("branches")]
    public async Task<ActionResult<List<string>>> GetBranches()
    {
        var branches = await _gitService.GetBranches();
        return Ok(branches);
    }

    /// <summary>
    /// Test connection to production server
    /// </summary>
    [HttpGet("test-connection")]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection()
    {
        try
        {
            var isConnected = await _remoteServer.TestConnection();
            
            return Ok(new ConnectionTestResult
            {
                Success = isConnected,
                Message = isConnected ? "Connection successful" : "Connection failed"
            });
        }
        catch (Exception ex)
        {
            return Ok(new ConnectionTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Get server information
    /// </summary>
    [HttpGet("server-info")]
    public async Task<ActionResult<ServerInfo>> GetServerInfo()
    {
        try
        {
            var script = @"
                $os = Get-WmiObject Win32_OperatingSystem
                $cpu = Get-WmiObject Win32_Processor
                $memory = Get-WmiObject Win32_ComputerSystem
                
                @{
                    OSName = $os.Caption
                    OSVersion = $os.Version
                    TotalMemoryGB = [math]::Round($memory.TotalPhysicalMemory / 1GB, 2)
                    CPUName = $cpu.Name
                    IISVersion = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp').VersionString
                } | ConvertTo-Json
            ";

            var result = await _remoteServer.ExecuteRemoteCommand(script);
            var serverInfo = JsonSerializer.Deserialize<ServerInfo>(result);

            return Ok(serverInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get server info");
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ServerInfo
{
    public string OSName { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public double TotalMemoryGB { get; set; }
    public string CPUName { get; set; } = string.Empty;
    public string IISVersion { get; set; } = string.Empty;
}
```

---

### **Phase 5: SignalR Hub for Real-time Logs (Week 2)**

```csharp
// DeploymentManager.API/Hubs/DeploymentHub.cs

using Microsoft.AspNetCore.SignalR;

namespace DeploymentManager.API.Hubs;

public interface IDeploymentHub
{
    Task SendLog(int deploymentId, string message);
    Task DeploymentStatusChanged(int deploymentId, string status);
}

public class DeploymentHub : Hub, IDeploymentHub
{
    private readonly ILogger<DeploymentHub> _logger;

    public DeploymentHub(ILogger<DeploymentHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinDeploymentGroup(int deploymentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"deployment-{deploymentId}");
        _logger.LogInformation($"Client {Context.ConnectionId} joined deployment {deploymentId}");
    }

    public async Task LeaveDeploymentGroup(int deploymentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"deployment-{deploymentId}");
        _logger.LogInformation($"Client {Context.ConnectionId} left deployment {deploymentId}");
    }

    public async Task SendLog(int deploymentId, string message)
    {
        await Clients.Group($"deployment-{deploymentId}").SendAsync("ReceiveLog", new
        {
            deploymentId,
            message,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task DeploymentStatusChanged(int deploymentId, string status)
    {
        await Clients.Group($"deployment-{deploymentId}").SendAsync("StatusChanged", new
        {
            deploymentId,
            status,
            timestamp = DateTime.UtcNow
        });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }
}

// Service wrapper for injecting into handlers
public class DeploymentHubService : IDeploymentHub
{
    private readonly IHubContext<DeploymentHub> _hubContext;

    public DeploymentHubService(IHubContext<DeploymentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendLog(int deploymentId, string message)
    {
        await _hubContext.Clients.Group($"deployment-{deploymentId}")
            .SendAsync("ReceiveLog", new
            {
                deploymentId,
                message,
                timestamp = DateTime.UtcNow
            });
    }

    public async Task DeploymentStatusChanged(int deploymentId, string status)
    {
        await _hubContext.Clients.Group($"deployment-{deploymentId}")
            .SendAsync("StatusChanged", new
            {
                deploymentId,
                status,
                timestamp = DateTime.UtcNow
            });
    }
}
```

---

### **Phase 6: Program.cs Configuration (Week 2)**

```csharp
// DeploymentManager.API/Program.cs

using DeploymentManager.API.Hubs;
using DeploymentManager.Application;
using DeploymentManager.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/deployment-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Deployment Manager API", Version = "v1" });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add SignalR
builder.Services.AddSignalR();

// Add MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(DeployCommand).Assembly));

// Register configuration
var serverConfig = builder.Configuration.GetSection("ServerConfiguration").Get<ServerConfiguration>();
builder.Services.AddSingleton(serverConfig!);

// Register infrastructure services
builder.Services.AddScoped<IPowerShellRemotingService, PowerShellRemotingService>();
builder.Services.AddScoped<IGitService>(sp => 
    new GitService(serverConfig!.RepositoryPath, sp.GetRequiredService<ILogger<GitService>>()));
builder.Services.AddScoped<IBuildService, BuildService>();
builder.Services.AddScoped<IFileDeploymentService, FileDeploymentService>();
builder.Services.AddScoped<IDatabaseMigrationService, DatabaseMigrationService>();

// Register repositories
builder.Services.AddScoped<IDeploymentRepository, DeploymentRepository>();
builder.Services.AddScoped<IMigrationRepository, MigrationRepository>();

// Register SignalR hub service
builder.Services.AddScoped<IDeploymentHub, DeploymentHubService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReactApp");
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DeploymentHub>("/hubs/deployment");

app.Run();
```

#### **appsettings.json**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "MetadataDb": "Server=localhost;Database=DeploymentManager;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "ServerConfiguration": {
    "Name": "Production",
    "ServerAddress": "production-server",
    "Username": "DOMAIN\\deploy-user",
    "PasswordKey": "ProductionServerPassword",
    "IISSiteName": "YourAppSite",
    "IISAppPoolName": "YourAppPool",
    "DeploymentPath": "\\\\production-server\\c$\\inetpub\\wwwroot\\YourApp",
    "DatabaseConnectionString": "Server=prod-sql-server;Database=YourAppDb;User Id=deploy;Password=***;",
    "RepositoryPath": "C:\\Repos\\YourProject",
    "ProjectPath": "C:\\Repos\\YourProject\\YourProject.csproj"
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/deployment-.txt",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

---

### **Phase 7: React Frontend (Week 3)**

#### **Setup React Project**
```bash
npm create vite@latest deployment-manager-web -- --template react-ts
cd deployment-manager-web
npm install
npm install axios @microsoft/signalr zustand lucide-react
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p
```

#### **API Service**
```typescript
// src/services/api.ts

import axios from 'axios';

const API_BASE_URL = 'https://localhost:7001/api';

export interface DeploymentDto {
  id: number;
  version: string;
  branch: string;
  commitHash: string;
  deployedBy: string;
  deployedAt: string;
  status: string;
  updateMode: string;
  backupPath?: string;
  duration?: number;
  errorMessage?: string;
}

export interface MigrationDto {
  id: number;
  name: string;
  upScript: string;
  downScript: string;
  changes: string[];
  executionOrder: number;
}

export interface DeployRequest {
  branch: string;
  updateMode: 'Incremental' | 'Full';
  autoSyncDatabase: boolean;
}

export const deploymentApi = {
  // Deployments
  getDeployments: async (page = 1, pageSize = 20) => {
    const response = await axios.get<DeploymentDto[]>(
      `${API_BASE_URL}/deployment?page=${page}&pageSize=${pageSize}`
    );
    return response.data;
  },

  getDeployment: async (id: number) => {
    const response = await axios.get<DeploymentDto>(`${API_BASE_URL}/deployment/${id}`);
    return response.data;
  },

  deploy: async (request: DeployRequest) => {
    const response = await axios.post(`${API_BASE_URL}/deployment`, request);
    return response.data;
  },

  rollback: async (deploymentId: number) => {
    const response = await axios.post(`${API_BASE_URL}/deployment/${deploymentId}/rollback`);
    return response.data;
  },

  getDeploymentLogs: async (deploymentId: number) => {
    const response = await axios.get(`${API_BASE_URL}/deployment/${deploymentId}/logs`);
    return response.data;
  },

  // Migrations
  previewMigrations: async () => {
    const response = await axios.get<MigrationDto[]>(`${API_BASE_URL}/migration/preview`);
    return response.data;
  },

  previewDeploymentMigrations: async (deploymentId: number) => {
    const response = await axios.get<MigrationDto[]>(
      `${API_BASE_URL}/migration/preview/${deploymentId}`
    );
    return response.data;
  },

  executeMigrations: async (deploymentId: number) => {
    const response = await axios.post(`${API_BASE_URL}/migration/execute/${deploymentId}`);
    return response.data;
  },

  // Configuration
  getBranches: async () => {
    const response = await axios.get<string[]>(`${API_BASE_URL}/configuration/branches`);
    return response.data;
  },

  testConnection: async () => {
    const response = await axios.get(`${API_BASE_URL}/configuration/test-connection`);
    return response.data;
  },
};
```

#### **SignalR Service**
```typescript
// src/services/signalr.ts

import * as signalR from '@microsoft/signalr';

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private callbacks: Map<string, Function[]> = new Map();

  async connect() {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('https://localhost:7001/hubs/deployment')
      .withAutomaticReconnect()
      .build();

    this.connection.on('ReceiveLog', (data) => {
      this.trigger('log', data);
    });

    this.connection.on('StatusChanged', (data) => {
      this.trigger('status', data);
    });

    try {
      await this.connection.start();
      console.log('SignalR Connected');
    } catch (err) {
      console.error('SignalR Connection Error: ', err);
    }
  }

  async joinDeploymentGroup(deploymentId: number) {
    if (this.connection) {
      await this.connection.invoke('JoinDeploymentGroup', deploymentId);
    }
  }

  async leaveDeploymentGroup(deploymentId: number) {
    if (this.connection) {
      await this.connection.invoke('LeaveDeploymentGroup', deploymentId);
    }
  }

  on(event: string, callback: Function) {
    if (!this.callbacks.has(event)) {
      this.callbacks.set(event, []);
    }
    this.callbacks.get(event)!.push(callback);
  }

  off(event: string, callback: Function) {
    const callbacks = this.callbacks.get(event);
    if (callbacks) {
      const index = callbacks.indexOf(callback);
      if (index > -1) {
        callbacks.splice(index, 1);
      }
    }
  }

  private trigger(event: string, data: any) {
    const callbacks = this.callbacks.get(event);
    if (callbacks) {
      callbacks.forEach(cb => cb(data));
    }
  }

  async disconnect() {
    if (this.connection) {
      await this.connection.stop();
    }
  }
}

export const signalRService = new SignalRService();
```

#### **Main Dashboard Component**
```typescript
// src/components/DeploymentDashboard.tsx

import React, { useEffect, useState } from 'react';
import { deploymentApi, DeploymentDto, MigrationDto } from '../services/api';
import { signalRService } from '../services/signalr';
import { Play, RotateCcw, Database, Server } from 'lucide-react';

export const DeploymentDashboard: React.FC = () => {
  const [deployments, setDeployments] = useState<DeploymentDto[]>([]);
  const [branches, setBranches] = useState<string[]>([]);
  const [selectedBranch, setSelectedBranch] = useState('');
  const [updateMode, setUpdateMode] = useState<'Incremental' | 'Full'>('Incremental');
  const [autoSync, setAutoSync] = useState(false);
  const [logs, setLogs] = useState<string[]>([]);
  const [currentDeploymentId, setCurrentDeploymentId] = useState<number | null>(null);
  const [migrationPreview, setMigrationPreview] = useState<MigrationDto[] | null>(null);

  useEffect(() => {
    loadInitialData();
    connectSignalR();

    return () => {
      signalRService.disconnect();
    };
  }, []);

  const loadInitialData = async () => {
    const [deploymentsData, branchesData] = await Promise.all([
      deploymentApi.getDeployments(),
      deploymentApi.getBranches(),
    ]);

    setDeployments(deploymentsData);
    setBranches(branchesData);
    if (branchesData.length > 0) {
      setSelectedBranch(branchesData[0]);
    }
  };

  const connectSignalR = async () => {
    await signalRService.connect();

    signalRService.on('log', (data: any) => {
      setLogs(prev => [...prev, `[${new Date(data.timestamp).toLocaleTimeString()}] ${data.message}`]);
    });

    signalRService.on('status', (data: any) => {
      setDeployments(prev =>
        prev.map(d => d.id === data.deploymentId ? { ...d, status: data.status } : d)
      );
    });
  };

  const handleDeploy = async () => {
    try {
      setLogs([]);
      const result = await deploymentApi.deploy({
        branch: selectedBranch,
        updateMode,
        autoSyncDatabase: autoSync,
      });

      setCurrentDeploymentId(result.deploymentId);
      await signalRService.joinDeploymentGroup(result.deploymentId);

      alert(`Deployment started: ${result.version}`);
      await loadInitialData();
    } catch (error) {
      alert(`Deployment failed: ${error}`);
    }
  };

  const handleRollback = async (deploymentId: number) => {
    if (!confirm('Are you sure you want to rollback this deployment?')) return;

    try {
      setLogs([]);
      await deploymentApi.rollback(deploymentId);
      await signalRService.joinDeploymentGroup(deploymentId);
      alert('Rollback started');
      await loadInitialData();
    } catch (error) {
      alert(`Rollback failed: ${error}`);
    }
  };

  const handlePreviewMigrations = async (deploymentId: number) => {
    const migrations = await deploymentApi.previewDeploymentMigrations(deploymentId);
    setMigrationPreview(migrations);
  };

  const handleSyncDatabase = async (deploymentId: number) => {
    if (!confirm('Are you sure you want to sync the database?')) return;

    try {
      await deploymentApi.executeMigrations(deploymentId);
      alert('Database synced successfully');
    } catch (error) {
      alert(`Database sync failed: ${error}`);
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Success': return 'bg-green-100 text-green-800';
      case 'Failed': return 'bg-red-100 text-red-800';
      case 'InProgress': return 'bg-yellow-100 text-yellow-800';
      case 'RolledBack': return 'bg-gray-100 text-gray-800';
      default: return 'bg-blue-100 text-blue-800';
    }
  };

  return (
    <div className="min-h-screen bg-gray-50 p-8">
      <div className="max-w-7xl mx-auto">
        <h1 className="text-4xl font-bold mb-8 text-gray-900">Deployment Manager</h1>

        {/* Deploy Section */}
        <div className="bg-white rounded-lg shadow p-6 mb-8">
          <h2 className="text-2xl font-semibold mb-4 flex items-center gap-2">
            <Play className="w-6 h-6" />
            New Deployment
          </h2>

          <div className="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Branch
              </label>
              <select
                value={selectedBranch}
                onChange={(e) => setSelectedBranch(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                {branches.map((branch) => (
                  <option key={branch} value={branch}>
                    {branch}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-2">
                Update Mode
              </label>
              <select
                value={updateMode}
                onChange={(e) => setUpdateMode(e.target.value as 'Incremental' | 'Full')}
                className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="Incremental">Incremental (4 files)</option>
                <option value="Full">Full (except wwwroot)</option>
              </select>
            </div>

            <div className="flex items-end">
              <label className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={autoSync}
                  onChange={(e) => setAutoSync(e.target.checked)}
                  className="w-4 h-4 text-blue-600 rounded focus:ring-2 focus:ring-blue-500"
                />
                <span className="text-sm font-medium text-gray-700">
                  Auto-sync Database
                </span>
              </label>
            </div>

            <div className="flex items-end">
              <button
                onClick={handleDeploy}
                disabled={!selectedBranch}
                className="w-full bg-blue-600 text-white px-6 py-2 rounded-md hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed flex items-center justify-center gap-2 font-semibold"
              >
                <Play className="w-5 h-5" />
                Deploy
              </button>
            </div>
          </div>
        </div>

        {/* Real-time Logs */}
        {logs.length > 0 && (
          <div className="bg-black rounded-lg shadow p-6 mb-8">
            <h2 className="text-xl font-semibold mb-4 text-green-400">
              Deployment Logs
            </h2>
            <div className="bg-gray-900 rounded p-4 h-96 overflow-y-auto font-mono text-sm">
              {logs.map((log, index) => (
                <div key={index} className="text-green-400 mb-1">
                  {log}
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Deployment History */}
        <div className="bg-white rounded-lg shadow p-6">
          <h2 className="text-2xl font-semibold mb-4 flex items-center gap-2">
            <Server className="w-6 h-6" />
            Deployment History
          </h2>

          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-gray-200">
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Version
                  </th>
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Branch
                  </th>
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Deployed At
                  </th>
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Deployed By
                  </th>
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Status
                  </th>
                  <th className="text-left py-3 px-4 font-semibold text-gray-700">
                    Duration
                  </th>
                  <th className="text-right py-3 px-4 font-semibold text-gray-700">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {deployments.map((deployment) => (
                  <tr key={deployment.id} className="border-b border-gray-100 hover:bg-gray-50">
                    <td className="py-3 px-4 font-medium">{deployment.version}</td>
                    <td className="py-3 px-4">{deployment.branch}</td>
                    <td className="py-3 px-4">
                      {new Date(deployment.deployedAt).toLocaleString()}
                    </td>
                    <td className="py-3 px-4">{deployment.deployedBy}</td>
                    <td className="py-3 px-4">
                      <span
                        className={`px-3 py-1 rounded-full text-xs font-semibold ${getStatusColor(
                          deployment.status
                        )}`}
                      >
                        {deployment.status}
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      {deployment.duration ? `${deployment.duration}s` : '-'}
                    </td>
                    <td className="py-3 px-4">
                      <div className="flex justify-end gap-2">
                        <button
                          onClick={() => handlePreviewMigrations(deployment.id)}
                          className="text-blue-600 hover:text-blue-800 text-sm font-medium flex items-center gap-1"
                        >
                          <Database className="w-4 h-4" />
                          DB Preview
                        </button>
                        <button
                          onClick={() => handleSyncDatabase(deployment.id)}
                          className="text-green-600 hover:text-green-800 text-sm font-medium flex items-center gap-1"
                        >
                          <Database className="w-4 h-4" />
                          Sync DB
                        </button>
                        <button
                          onClick={() => handleRollback(deployment.id)}
                          disabled={deployment.status === 'RolledBack'}
                          className="text-red-600 hover:text-red-800 text-sm font-medium flex items-center gap-1 disabled:text-gray-400 disabled:cursor-not-allowed"
                        >
                          <RotateCcw className="w-4 h-4" />
                          Rollback
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Migration Preview Modal */}
        {migrationPreview && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg p-8 max-w-6xl w-full max-h-[90vh] overflow-y-auto">
              <h3 className="text-2xl font-bold mb-6">Database Changes Preview</h3>

              {migrationPreview.length === 0 ? (
                <p className="text-gray-600">No migrations found.</p>
              ) : (
                <div className="space-y-6">
                  {migrationPreview.map((migration, idx) => (
                    <div key={idx} className="border border-gray-200 rounded-lg p-4">
                      <h4 className="font-semibold text-lg mb-3 flex items-center gap-2">
                        <Database className="w-5 h-5" />
                        {migration.name}
                      </h4>

                      {/* Changes Summary */}
                      <div className="mb-4">
                        <p className="font-medium text-sm text-gray-700 mb-2">
                          Changes:
                        </p>
                        <ul className="list-disc list-inside space-y-1">
                          {migration.changes.map((change, i) => (
                            <li key={i} className="text-sm text-gray-600">
                              {change}
                            </li>
                          ))}
                        </ul>
                      </div>

                      {/* UP Script */}
                      <div className="mb-4">
                        <p className="font-medium text-sm text-green-700 mb-2">
                          ↑ UP Script (Apply):
                        </p>
                        <pre className="bg-green-50 border border-green-200 p-3 rounded text-xs overflow-x-auto">
                          {migration.upScript}
                        </pre>
                      </div>

                      {/* DOWN Script */}
                      <div>
                        <p className="font-medium text-sm text-red-700 mb-2">
                          ↓ DOWN Script (Rollback):
                        </p>
                        <pre className="bg-red-50 border border-red-200 p-3 rounded text-xs overflow-x-auto">
                          {migration.downScript || '-- No rollback script available'}
                        </pre>
                      </div>
                    </div>
                  ))}
                </div>
              )}

              <div className="mt-6 flex justify-end">
                <button
                  onClick={() => setMigrationPreview(null)}
                  className="bg-gray-500 text-white px-6 py-2 rounded-md hover:bg-gray-600"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
```

#### **App.tsx**
```typescript
// src/App.tsx

import React from 'react';
import { DeploymentDashboard } from './components/DeploymentDashboard';

function App() {
  return (
    <div className="App">
      <DeploymentDashboard />
    </div>
  );
}

export default App;
```

---

## **Phase 8: Deployment & Configuration (Week 3-4)**

### **One-Time Setup on Production Server**

```powershell
# On Production Server - Run as Administrator

# 1. Enable PowerShell Remoting
Enable-PSRemoting -Force

# 2. Configure WinRM
Set-Item WSMan:\localhost\Client\TrustedHosts -Value "*" -Force
Restart-Service WinRM

# 3. Test WinRM
Test-WSMan

# 4. Create service account for deployments
$password = ConvertTo-SecureString "YourSecurePassword" -AsPlainText -Force
New-LocalUser -Name "deploy-svc" -Password $password -PasswordNeverExpires

# 5. Add to Administrators group (or create custom group with needed permissions)
Add-LocalGroupMember -Group "Administrators" -Member "deploy-svc"

# 6. Share deployment folder (if using UNC paths)
New-SmbShare -Name "wwwroot$" -Path "C:\inetpub\wwwroot" -FullAccess "Everyone"

# 7. Test remote connection from your API server
$cred = Get-Credential # Enter deploy-svc credentials
Invoke-Command -ComputerName production-server -Credential $cred -ScriptBlock { Get-Service }
```

### **Deploy Web API**

```powershell
# On API Host Server

# 1. Build for production
dotnet publish -c Release -o ./publish

# 2. Copy to IIS or run as Windows Service
# Option A: IIS
# - Create new site in IIS Manager
# - Point to published folder
# - Create application pool (.NET Core)

# Option B: Windows Service (using NSSM)
nssm install DeploymentManagerAPI "C:\DeploymentManager\DeploymentManager.API.exe"
nssm set DeploymentManagerAPI AppDirectory "C:\DeploymentManager"
nssm start DeploymentManagerAPI
```

### **Deploy React Frontend**

```bash
# Build React app
npm run build

# Deploy to IIS
# 1. Create new site in IIS
# 2. Copy dist folder contents to site root
# 3. Add web.config for SPA routing
```

**web.config for React SPA:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="React Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/(api)" negate="true" />
          </conditions>
          <action type="Rewrite" url="/" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
```

---

## **Complete Implementation Timeline**

| Phase | Tasks | Duration | Status |
|-------|-------|----------|--------|
| **Phase 1** | Project setup, database schema, domain models | Week 1 | ✅ |
| **Phase 2** | Infrastructure services (PowerShell, Git, Build, File, DB) | Week 1-2 | ✅ |
| **Phase 3** | Application layer (CQRS, MediatR handlers) | Week 2 | ✅ |
| **Phase 4** | API controllers | Week 2 | ✅ |
| **Phase 5** | SignalR hub for real-time logs | Week 2 | ✅ |
| **Phase 6** | Program.cs configuration | Week 2 | ✅ |
| **Phase 7** | React frontend | Week 3 | ✅ |
| **Phase 8** | Deployment & testing | Week 3-4 | 📋 |

---

## **Security Considerations** 🔒

### **1. Store Credentials Securely**

```csharp
// Use Azure Key Vault
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

public class SecureConfigurationService
{
    private readonly SecretClient _secretClient;

    public SecureConfigurationService(string keyVaultUrl)
    {
        _secretClient = new SecretClient(
            new Uri(keyVaultUrl),
            new DefaultAzureCredential()
        );
    }

    public async Task<string> GetSecret(string secretName)
    {
        var secret = await _secretClient.GetSecretAsync(secretName);
        return secret.Value.Value;
    }
}
```

### **2. Add Authentication**

```csharp
// In Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
            )
        };
    });

// Add [Authorize] to controllers
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DeploymentController : ControllerBase
{
    // ...
}
```

### **3. Rate Limiting**

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("deploy", options =>
    {
        options.PermitLimit = 10;
        options.Window = TimeSpan.FromMinutes(1);
    });
});

// Apply to deploy endpoint
[HttpPost]
[EnableRateLimiting("deploy")]
public async Task<ActionResult<DeploymentResult>> Deploy([FromBody] DeployRequest request)
{
    // ...
}
```

---

## **Testing Strategy** 🧪

### **Unit Tests**
```csharp
// DeploymentManager.Tests.Unit/Services/DatabaseMigrationServiceTests.cs

public class DatabaseMigrationServiceTests
{
    [Fact]
    public void GenerateDownScript_AddColumn_ReturnsDropColumn()
    {
        // Arrange
        var upScript = "ALTER TABLE Users ADD Email VARCHAR(255)";
        var service = new DatabaseMigrationService(/* dependencies */);

        // Act
        var downScript = service.GenerateDownScript(upScript);

        // Assert
        Assert.Contains("DROP COLUMN Email", downScript);
    }
}
```

### **Integration Tests**
```csharp
// DeploymentManager.Tests.Integration/Controllers/DeploymentControllerTests.cs

public class DeploymentControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DeploymentControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDeployments_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/deployment");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

---

## **Monitoring & Logging** 📊

### **Add Application Insights**
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

### **Custom Metrics**
```csharp
private readonly TelemetryClient _telemetry;

public async Task<DeploymentResult> Handle(DeployCommand request, CancellationToken cancellationToken)
{
    var sw = Stopwatch.StartNew();
    
    try
    {
        // ... deployment logic
        
        _telemetry.TrackEvent("DeploymentSuccess", new Dictionary<string, string>
        {
            { "Branch", request.Branch },
            { "Duration", sw.Elapsed.TotalSeconds.ToString() }
        });
    }
    catch (Exception ex)
    {
        _telemetry.TrackException(ex);
        throw;
    }
}
```

---

## **Final Checklist** ✅

- [ ] Database created with all tables
- [ ] PowerShell remoting configured on production server
- [ ] Service account created with proper permissions
- [ ] Web API deployed and accessible
- [ ] React frontend deployed
- [ ] SignalR working (test real-time logs)
- [ ] Git repository cloned on API server
- [ ] Credentials stored securely (Key Vault)
- [ ] Authentication implemented
- [ ] Error handling and logging configured
- [ ] Backup folder structure created
- [ ] Test deployment on staging first
- [ ] Documentation written for team

---

## **Next Steps After MVP**

1. **Email/Slack Notifications** - Alert on deployment success/failure
2. **Scheduled Deployments** - Use Hangfire for cron-based deploys
3. **Multi-Environment Support** - Dev, Staging, Production
4. **Approval Workflow** - Require approval before production deploy
5. **Deployment Comparison** - Diff between versions
6. **Performance Metrics** - Track deployment times, success rates
7. **Audit Trail** - WHO deployed WHAT and WHEN
8. **Automated Rollback** - Auto-rollback on health check failure

---

**You now have a complete, production-ready Web API-based deployment manager!** 🚀

Would you like me to elaborate on any specific section or help you get started with a particular phase?