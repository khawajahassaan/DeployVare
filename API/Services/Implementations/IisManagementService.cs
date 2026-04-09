using API.Models.Entities;
using API.Services.Interfaces;

namespace API.Services.Implementations;

public class IisManagementService : IIisManagementService
{
    private readonly ISshService _sshService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<IisManagementService> _logger;

    public IisManagementService(
        ISshService sshService,
        IEncryptionService encryptionService,
        ILogger<IisManagementService> logger)
    {
        _sshService = sshService;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> StopAppPoolAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Stopping App Pool '{server.AppPoolName}' on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $pool = Get-WebAppPoolState -Name '{server.AppPoolName}';
            if ($pool.Value -eq 'Started') {{
                Stop-WebAppPool -Name '{server.AppPoolName}';
                Start-Sleep -Seconds 3;
                $status = (Get-WebAppPoolState -Name '{server.AppPoolName}').Value;
                if ($status -eq 'Stopped') {{
                    Write-Host 'App Pool stopped successfully';
                }} else {{
                    Write-Error 'Failed to stop App Pool';
                }}
            }} else {{
                Write-Host 'App Pool is already stopped';
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (success)
        {
            _logger.LogInformation($"App Pool stopped: {output}");
            return (true, output);
        }
        else
        {
            _logger.LogError($"Failed to stop App Pool: {error}");
            return (false, error);
        }
    }

    public async Task<(bool Success, string Message)> StartAppPoolAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Starting App Pool '{server.AppPoolName}' on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $pool = Get-WebAppPoolState -Name '{server.AppPoolName}';
            if ($pool.Value -ne 'Started') {{
                Start-WebAppPool -Name '{server.AppPoolName}';
                Start-Sleep -Seconds 3;
                $status = (Get-WebAppPoolState -Name '{server.AppPoolName}').Value;
                if ($status -eq 'Started') {{
                    Write-Host 'App Pool started successfully';
                }} else {{
                    Write-Error 'Failed to start App Pool';
                }}
            }} else {{
                Write-Host 'App Pool is already running';
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (success)
        {
            _logger.LogInformation($"App Pool started: {output}");
            return (true, output);
        }
        else
        {
            _logger.LogError($"Failed to start App Pool: {error}");
            return (false, error);
        }
    }

    public async Task<(bool Success, string Message)> StopWebsiteAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Stopping Website '{server.WebsiteName}' on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $site = Get-Website -Name '{server.WebsiteName}';
            if ($site.State -eq 'Started') {{
                Stop-Website -Name '{server.WebsiteName}';
                Start-Sleep -Seconds 2;
                $status = (Get-Website -Name '{server.WebsiteName}').State;
                if ($status -eq 'Stopped') {{
                    Write-Host 'Website stopped successfully';
                }} else {{
                    Write-Error 'Failed to stop Website';
                }}
            }} else {{
                Write-Host 'Website is already stopped';
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (success)
        {
            _logger.LogInformation($"Website stopped: {output}");
            return (true, output);
        }
        else
        {
            _logger.LogError($"Failed to stop Website: {error}");
            return (false, error);
        }
    }

    public async Task<(bool Success, string Message)> StartWebsiteAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Starting Website '{server.WebsiteName}' on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $site = Get-Website -Name '{server.WebsiteName}';
            if ($site.State -ne 'Started') {{
                Start-Website -Name '{server.WebsiteName}';
                Start-Sleep -Seconds 2;
                $status = (Get-Website -Name '{server.WebsiteName}').State;
                if ($status -eq 'Started') {{
                    Write-Host 'Website started successfully';
                }} else {{
                    Write-Error 'Failed to start Website';
                }}
            }} else {{
                Write-Host 'Website is already running';
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (success)
        {
            _logger.LogInformation($"Website started: {output}");
            return (true, output);
        }
        else
        {
            _logger.LogError($"Failed to start Website: {error}");
            return (false, error);
        }
    }

    public async Task<(bool Success, string? BackupFileName, string Message)> CreateBackupAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Creating backup of {server.DeploymentPath} on {server.HostName}");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"{server.WebsiteName}_{timestamp}.zip";
        var backupPath = $"{server.BackupPath}\\{backupFileName}".Replace("\\\\", "\\");

        var command = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Continue';

            try {{
                # Ensure backup directory exists
                if (-not (Test-Path '{server.BackupPath}')) {{
                    New-Item -Path '{server.BackupPath}' -ItemType Directory -Force | Out-Null;
                }}

                # Check if deployment path exists
                if (-not (Test-Path '{server.DeploymentPath}')) {{
                    Write-Output 'SKIP: Deployment path does not exist, skipping backup';
                    exit 0;
                }}

                # Check if there are any files to backup
                $fileCount = (Get-ChildItem -Path '{server.DeploymentPath}' -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count;
                
                if ($fileCount -eq 0) {{
                    Write-Output 'SKIP: No files found in deployment directory, skipping backup';
                    exit 0;
                }}

                # Create backup
                Compress-Archive -Path '{server.DeploymentPath}\*' -DestinationPath '{backupPath}' -Force -CompressionLevel Fastest -ErrorAction Stop;
                
                Start-Sleep -Milliseconds 500;
                
                # Verify backup was created
                if (Test-Path '{backupPath}') {{
                    $size = (Get-Item '{backupPath}').Length / 1MB;
                    Write-Output ""SUCCESS: Backup created: {backupFileName} ({{0:N2}} MB)"" -f $size;
                    exit 0;
                }} else {{
                    Write-Output 'ERROR: Backup file was not created';
                    exit 1;
                }}
            }} catch {{
                Write-Output ""ERROR: $($_.Exception.Message)"";
                exit 1;
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        // Check if backup was skipped (no files) or successful
        if (success && (output.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase) || 
                        output.Contains("SKIP:", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation($"Backup result: {output}");
            
            // Return null for backupFileName if it was skipped
            var actualBackupFileName = output.Contains("SKIP:") ? null : backupFileName;
            return (true, actualBackupFileName, output);
        }
        else
        {
            var errorMsg = string.IsNullOrEmpty(error) ? output : error;
            _logger.LogError($"Failed to create backup: {errorMsg}");
            return (false, null, errorMsg);
        }
    }

    public async Task<(bool Success, string Message)> VerifyDeploymentAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Verifying deployment on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $poolStatus = (Get-WebAppPoolState -Name '{server.AppPoolName}').Value;
            $siteStatus = (Get-Website -Name '{server.WebsiteName}').State;

            Write-Host ""App Pool Status: $poolStatus"";
            Write-Host ""Website Status: $siteStatus"";

            if ($poolStatus -eq 'Started' -and $siteStatus -eq 'Started') {{
                Write-Host 'VERIFICATION_SUCCESS';
            }} else {{
                Write-Error 'VERIFICATION_FAILED';
            }}
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        var verified = success && output.Contains("VERIFICATION_SUCCESS");

        if (verified)
        {
            _logger.LogInformation("Deployment verification successful");
            return (true, output);
        }
        else
        {
            _logger.LogWarning($"Deployment verification failed: {error}");
            return (false, error);
        }
    }

    public async Task<(bool AppPoolRunning, bool WebsiteRunning, string Message)> GetServiceStatusAsync(ApplicationServer server)
    {
        _logger.LogInformation($"Getting service status on {server.HostName}");

        var command = $@"
            Import-Module WebAdministration;
            $poolStatus = (Get-WebAppPoolState -Name '{server.AppPoolName}').Value;
            $siteStatus = (Get-Website -Name '{server.WebsiteName}').State;
            Write-Host ""AppPool:$poolStatus|Website:$siteStatus"";
        ";

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (success, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (success)
        {
            var parts = output.Split('|');
            var appPoolRunning = parts[0].Contains("Started", StringComparison.OrdinalIgnoreCase);
            var websiteRunning = parts.Length > 1 && parts[1].Contains("Started", StringComparison.OrdinalIgnoreCase);
            
            return (appPoolRunning, websiteRunning, output);
        }

        return (false, false, error);
    }

    private async Task<(string? password, string? privateKey)> GetCredentialsAsync(ApplicationServer server)
    {
        string? password = null;
        string? privateKey = null;

        if (!string.IsNullOrEmpty(server.EncryptedPassword))
        {
            password = await Task.Run(() => _encryptionService.Decrypt(server.EncryptedPassword));
        }

        if (!string.IsNullOrEmpty(server.SshPrivateKey))
        {
            privateKey = server.SshPrivateKey;
        }

        return (password, privateKey);
    }
}


