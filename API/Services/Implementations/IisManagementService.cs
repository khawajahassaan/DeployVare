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

    public async Task<(bool Success, string Message)> DeployFrontendZipAsync(ApplicationServer server, string localZipPath)
    {
        _logger.LogInformation($"Deploying frontend ZIP to {server.HostName}:{server.DeploymentPath}");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var remoteTempDir = $"C:\\Temp\\DVFrontend_{timestamp}";
        var remoteZipPath = $"{remoteTempDir}\\upload.zip";

        var (password, privateKey) = await GetCredentialsAsync(server);

        // Upload zip to remote temp dir — create dir first
        var mkdirCmd = $"New-Item -Path '{remoteTempDir}' -ItemType Directory -Force | Out-Null";
        var (mkdirOk, _, mkdirErr) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, mkdirCmd);

        if (!mkdirOk)
        {
            return (false, $"Failed to create remote temp directory: {mkdirErr}");
        }

        // Upload the ZIP file
        var uploadOk = await _sshService.UploadFileAsync(
            server.HostName, server.Port, server.Username, password, privateKey,
            localZipPath, remoteZipPath.Replace("\\", "/"));

        if (!uploadOk)
        {
            return (false, "Failed to upload ZIP file to remote server");
        }

        // Ensure deployment path exists, then extract ZIP with -Force (web.config preserved)
        var extractCmd = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Stop';
            try {{
                if (-not (Test-Path '{server.DeploymentPath}')) {{
                    New-Item -Path '{server.DeploymentPath}' -ItemType Directory -Force | Out-Null;
                }}
                Expand-Archive -Path '{remoteZipPath}' -DestinationPath '{server.DeploymentPath}' -Force;
                Remove-Item -Path '{remoteTempDir}' -Recurse -Force;
                Write-Output 'FRONTEND_DEPLOY_SUCCESS';
            }} catch {{
                Write-Output ""FRONTEND_DEPLOY_ERROR: $($_.Exception.Message)"";
                exit 1;
            }}
        ";

        var (extractOk, extractOut, extractErr) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, extractCmd);

        if (extractOk && extractOut.Contains("FRONTEND_DEPLOY_SUCCESS"))
        {
            _logger.LogInformation("Frontend ZIP deployed successfully");
            return (true, "Frontend files extracted successfully");
        }

        var errMsg = extractOut.Contains("FRONTEND_DEPLOY_ERROR")
            ? extractOut
            : string.IsNullOrEmpty(extractErr) ? extractOut : extractErr;

        _logger.LogError($"Frontend extraction failed: {errMsg}");
        return (false, $"Failed to extract frontend ZIP: {errMsg}");
    }

    public async Task<(bool Success, string Message)> DeployPackageZipAsync(ApplicationServer server, string localZipPath, bool preserveExistingWebConfig)
    {
        var (created, createMessage, workspace) = await CreateRemoteWorkspaceAsync(server);
        if (!created)
        {
            return (false, createMessage);
        }

        try
        {
            var (extracted, extractMessage, stagedPath) = await UploadAndExtractZipAsync(server, workspace!, localZipPath, "package");
            if (!extracted)
            {
                return (false, extractMessage);
            }

            return await PublishStagedFolderAsync(server, stagedPath!, preserveExistingWebConfig, Array.Empty<string>());
        }
        finally
        {
            await RemoveRemoteWorkspaceAsync(server, workspace!);
        }
    }

    private const string RemoteWorkspaceRoot = @"C:\Temp";
    private const string RemoteWorkspacePrefix = "DVDeploy_";

    public async Task<(bool Success, string Message, string? WorkspacePath)> CreateRemoteWorkspaceAsync(ApplicationServer server)
    {
        var workspace = $"{RemoteWorkspaceRoot}\\{RemoteWorkspacePrefix}{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var (password, privateKey) = await GetCredentialsAsync(server);

        var (ok, _, error) = await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username,
            password, privateKey, $"New-Item -Path '{workspace}' -ItemType Directory -Force | Out-Null");

        return ok
            ? (true, "Workspace created", workspace)
            : (false, $"Failed to create remote temp directory: {error}", null);
    }

    public async Task<(bool Success, string Message, string? ExtractedPath)> UploadAndExtractZipAsync(
        ApplicationServer server, string workspacePath, string localZipPath, string folderName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(folderName, "^[A-Za-z0-9_-]{1,50}$"))
        {
            throw new ArgumentException("Invalid staging folder name", nameof(folderName));
        }

        var remoteZipPath = $"{workspacePath}\\{folderName}.zip";
        var extractedPath = $"{workspacePath}\\{folderName}";
        var (password, privateKey) = await GetCredentialsAsync(server);

        _logger.LogInformation("Uploading {File} ({Bytes} bytes) to {Host}:{Path}",
            Path.GetFileName(localZipPath), new FileInfo(localZipPath).Length, server.HostName, remoteZipPath);

        if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, password, privateKey,
                localZipPath, remoteZipPath.Replace("\\", "/")))
        {
            return (false, "Failed to upload the file to the server", null);
        }

        var command = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Stop';
            try {{
                Expand-Archive -LiteralPath '{remoteZipPath}' -DestinationPath '{extractedPath}' -Force;
                Remove-Item -LiteralPath '{remoteZipPath}' -Force;
                Write-Output 'DV_EXTRACTED';
                exit 0;
            }} catch {{
                Write-Output ""DV_EXTRACT_ERROR: $($_.Exception.Message)"";
                exit 1;
            }}
        ";

        var (ok, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (ok && output.Contains("DV_EXTRACTED"))
        {
            return (true, "Uploaded and extracted", extractedPath);
        }

        var errMsg = output.Contains("DV_EXTRACT_ERROR") ? output.Trim() : string.IsNullOrEmpty(error) ? output : error;
        _logger.LogError("Extracting {File} on {Host} failed: {Error}", Path.GetFileName(localZipPath), server.HostName, errMsg);
        return (false, $"Failed to extract on the server: {errMsg}", null);
    }

    public async Task<(bool Success, string Message)> PublishStagedFolderAsync(
        ApplicationServer server, string stagedFolderPath, bool preserveExistingWebConfig, IReadOnlyList<string> keepOnServerPaths)
    {
        _logger.LogInformation("Publishing {Staged} to {Host}:{DeploymentPath} (preserve web.config: {Preserve}, keep: [{Keep}])",
            stagedFolderPath, server.HostName, server.DeploymentPath, preserveExistingWebConfig, string.Join(", ", keepOnServerPaths));

        var keepList = string.Join(", ", keepOnServerPaths.Select(p => $"'{EscapePowerShellLiteral(p.Replace('/', '\\'))}'"));

        // Kept paths are removed from the staging copy only when they already exist on the server, so a first
        // deployment still gets them; robocopy then merges staging into the site (exit codes >= 8 are failures)
        var script = PublishStagedFolderScript
            .Replace("__STAGING__", EscapePowerShellLiteral(stagedFolderPath))
            .Replace("__DEPLOYMENT_PATH__", EscapePowerShellLiteral(server.DeploymentPath))
            .Replace("__KEEP_PATHS__", keepList)
            .Replace("__PRESERVE_WEBCONFIG__", preserveExistingWebConfig ? "$true" : "$false");

        var (password, privateKey) = await GetCredentialsAsync(server);
        var (ok, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, script);

        var successLine = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("DV_PUBLISH_SUCCESS:", StringComparison.Ordinal));
        if (ok && successLine != null)
        {
            var kept = successLine["DV_PUBLISH_SUCCESS:".Length..]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _logger.LogInformation("Files published to {Host}:{DeploymentPath}; kept on server: [{Kept}]",
                server.HostName, server.DeploymentPath, string.Join(", ", kept));

            return (true, kept.Length > 0 ? $"Files deployed (kept on server: {string.Join(", ", kept)})" : "Files deployed");
        }

        var errMsg = output.Contains("DV_PUBLISH_ERROR") ? output.Trim() : string.IsNullOrEmpty(error) ? output : error;
        _logger.LogError("Publishing files to {Host} failed: {Error}", server.HostName, errMsg);
        return (false, errMsg);
    }

    public async Task RemoveRemoteWorkspaceAsync(ApplicationServer server, string workspacePath)
    {
        // Only folders created by CreateRemoteWorkspaceAsync are ever deleted
        if (!workspacePath.StartsWith($"{RemoteWorkspaceRoot}\\{RemoteWorkspacePrefix}", StringComparison.OrdinalIgnoreCase)
            || workspacePath.Contains(".."))
        {
            _logger.LogWarning("Refusing to remove unexpected remote workspace path {Path}", workspacePath);
            return;
        }

        try
        {
            var (password, privateKey) = await GetCredentialsAsync(server);
            await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username, password, privateKey,
                $"Remove-Item -LiteralPath '{EscapePowerShellLiteral(workspacePath)}' -Recurse -Force -ErrorAction SilentlyContinue");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove remote workspace {Path} on {Host}", workspacePath, server.HostName);
        }
    }

    private const string PublishStagedFolderScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $staging = '__STAGING__'
            $deploymentPath = '__DEPLOYMENT_PATH__'
            $keepPaths = @(__KEEP_PATHS__)
            if (__PRESERVE_WEBCONFIG__) { $keepPaths += 'web.config' }
            $kept = @()

            if (-not (Test-Path -LiteralPath $deploymentPath)) {
                New-Item -Path $deploymentPath -ItemType Directory -Force | Out-Null
            }

            foreach ($relative in ($keepPaths | Select-Object -Unique)) {
                $target = Join-Path $deploymentPath $relative
                $staged = Join-Path $staging $relative
                if ((Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $staged)) {
                    Remove-Item -LiteralPath $staged -Recurse -Force
                    $kept += $relative
                }
            }

            & robocopy $staging $deploymentPath /E /IS /IT /R:2 /W:2 /NP /NFL /NDL /NJH /NJS | Out-Null
            if ($LASTEXITCODE -ge 8) {
                throw "robocopy failed with exit code $LASTEXITCODE"
            }

            Write-Output ('DV_PUBLISH_SUCCESS:' + ($kept -join ';'))
            exit 0
        }
        catch {
            Write-Output "DV_PUBLISH_ERROR: $($_.Exception.Message)"
            exit 1
        }
        """;

    public async Task<(bool Success, string Message)> WriteFrontendRuntimeConfigAsync(ApplicationServer server, string localConfigPath)
    {
        _logger.LogInformation("Writing config.js to {Host}:{DeploymentPath}", server.HostName, server.DeploymentPath);

        const string remoteTempDir = @"C:\DeployVare\Temp";
        var remoteTempFile = $"{remoteTempDir}\\config_{Guid.NewGuid():N}.js";
        var deploymentPath = EscapePowerShellLiteral(server.DeploymentPath);
        var siteName = EscapePowerShellLiteral(server.WebsiteName);

        var (password, privateKey) = await GetCredentialsAsync(server);

        var (mkdirOk, _, mkdirErr) = await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username,
            password, privateKey, $"New-Item -Path '{remoteTempDir}' -ItemType Directory -Force | Out-Null");
        if (!mkdirOk)
        {
            return (false, $"Failed to create remote temp directory: {mkdirErr}");
        }

        if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, password, privateKey,
                localConfigPath, remoteTempFile.Replace("\\", "/")))
        {
            return (false, "Failed to upload config.js to the server");
        }

        // The no-cache rule is set in applicationHost.config for <site>/config.js, so it applies even when the
        // server keeps its own web.config
        var command = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Stop';
            try {{
                if (-not (Test-Path -LiteralPath '{deploymentPath}')) {{
                    New-Item -Path '{deploymentPath}' -ItemType Directory -Force | Out-Null;
                }}
                Copy-Item -LiteralPath '{remoteTempFile}' -Destination (Join-Path '{deploymentPath}' 'config.js') -Force;

                Import-Module WebAdministration;
                Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location '{siteName}/config.js' `
                    -Filter 'system.webServer/staticContent/clientCache' -Name 'cacheControlMode' -Value 'DisableCache';

                Write-Output 'RUNTIME_CONFIG_WRITTEN';
                exit 0;
            }} catch {{
                Write-Output ""RUNTIME_CONFIG_ERROR: $($_.Exception.Message)"";
                exit 1;
            }} finally {{
                Remove-Item -LiteralPath '{remoteTempFile}' -Force -ErrorAction SilentlyContinue;
            }}
        ";

        var (ok, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (ok && output.Contains("RUNTIME_CONFIG_WRITTEN"))
        {
            return (true, "config.js written");
        }

        var errMsg = output.Contains("RUNTIME_CONFIG_ERROR")
            ? output.Trim()
            : string.IsNullOrEmpty(error) ? output : error;

        _logger.LogError("Writing config.js to {Host} failed: {Error}", server.HostName, errMsg);
        return (false, $"Failed to write config.js: {errMsg}");
    }

    public async Task<(bool Success, string Message)> InstallLicenseAsync(ApplicationServer server, string localLicenseFilePath)
    {
        _logger.LogInformation("Installing licence on {Host}:{DeploymentPath}", server.HostName, server.DeploymentPath);

        const string remoteTempDir = @"C:\DeployVare\Temp";
        var remoteLicenseFile = $"{remoteTempDir}\\license_{Guid.NewGuid():N}.lic";
        var deploymentPath = EscapePowerShellLiteral(server.DeploymentPath);

        var (password, privateKey) = await GetCredentialsAsync(server);

        var (mkdirOk, _, mkdirErr) = await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username,
            password, privateKey, $"New-Item -Path '{remoteTempDir}' -ItemType Directory -Force | Out-Null");
        if (!mkdirOk)
        {
            return (false, $"Failed to create remote temp directory: {mkdirErr}");
        }

        if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, password, privateKey,
                localLicenseFilePath, remoteLicenseFile.Replace("\\", "/")))
        {
            return (false, "Failed to upload the licence file to the server");
        }

        // The licence file is removed from the temp folder either way; the application copies what it needs into
        // ProgramData, outside the site folder, so later deployments never disturb it.
        var command = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Stop';
            try {{
                $exe = $null;
                $candidates = Get-ChildItem -LiteralPath '{deploymentPath}' -Filter '*.exe' -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object {{ $_.Name -ne 'DbMigrator.exe' }};

                # Identify the application by the licensing commands it answers to, rather than by name or size:
                # --license-status exits 0 or 1 on an application that understands it, and the .NET launcher
                # exits with something else entirely on one that does not.
                foreach ($candidate in $candidates) {{
                    $probe = & $candidate.FullName --license-status 2>&1;
                    if ($LASTEXITCODE -eq 0 -or ($probe -join ' ') -match 'Licensing:|State:') {{
                        $exe = $candidate.FullName;
                        break;
                    }}
                }}
                if (-not $exe) {{ throw 'No application in the deployment path supports the licensing commands' }}

                $ErrorActionPreference = 'Continue';
                $output = & $exe --license-install '{remoteLicenseFile}' 2>&1;
                $exitCode = $LASTEXITCODE;
                foreach ($line in $output) {{ Write-Output ('DV_LICENCE: ' + [string]$line) }}

                if ($exitCode -ne 0) {{
                    Write-Output ""DV_LICENCE_FAILED: the application exited with code $exitCode"";
                    exit 1;
                }}

                Write-Output 'DV_LICENCE_INSTALLED';
                exit 0;
            }} catch {{
                Write-Output ""DV_LICENCE_FAILED: $($_.Exception.Message)"";
                exit 1;
            }} finally {{
                Remove-Item -LiteralPath '{remoteLicenseFile}' -Force -ErrorAction SilentlyContinue;
            }}
        ";

        var (ok, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (ok && output.Contains("DV_LICENCE_INSTALLED"))
        {
            return (true, "Licence installed on the server");
        }

        var errMsg = output.Contains("DV_LICENCE_FAILED")
            ? output.Trim()
            : string.IsNullOrEmpty(error) ? output : error;

        _logger.LogError("Installing the licence on {Host} failed: {Error}", server.HostName, errMsg);
        return (false, $"Failed to install the licence: {errMsg}");
    }

    public async Task<(bool Success, string State, string Message)> CheckLicenseStatusAsync(ApplicationServer server)
    {
        var deploymentPath = EscapePowerShellLiteral(server.DeploymentPath);
        var (password, privateKey) = await GetCredentialsAsync(server);

        // --license-status exits 0 only when the licence and the activation are both valid on this machine
        var command = $@"
            $ProgressPreference = 'SilentlyContinue';
            $ErrorActionPreference = 'Stop';
            try {{
                $exe = $null;
                $candidates = Get-ChildItem -LiteralPath '{deploymentPath}' -Filter '*.exe' -Recurse -File -ErrorAction SilentlyContinue |
                    Where-Object {{ $_.Name -ne 'DbMigrator.exe' }};

                foreach ($candidate in $candidates) {{
                    $probe = & $candidate.FullName --license-status 2>&1;
                    if ($LASTEXITCODE -eq 0 -or ($probe -join ' ') -match 'Licensing:|State:') {{
                        $exe = $candidate.FullName;
                        break;
                    }}
                }}
                if (-not $exe) {{ throw 'No application in the deployment path supports the licensing commands' }}

                $ErrorActionPreference = 'Continue';
                $output = & $exe --license-status 2>&1;
                $exitCode = $LASTEXITCODE;
                foreach ($line in $output) {{ Write-Output ('DV_LICENCE: ' + [string]$line) }}

                Write-Output ""DV_LICENCE_EXIT: $exitCode"";
                exit 0;
            }} catch {{
                Write-Output ""DV_LICENCE_FAILED: $($_.Exception.Message)"";
                exit 1;
            }}
        ";

        var (ok, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, command);

        if (!ok || output.Contains("DV_LICENCE_FAILED"))
        {
            var errMsg = output.Contains("DV_LICENCE_FAILED")
                ? output.Trim()
                : string.IsNullOrEmpty(error) ? output : error;

            return (false, "Unknown", $"Could not read the licence status: {errMsg}");
        }

        var state = ReadReportedValue(output, "State:") ?? "Unknown";
        var detail = ReadReportedValue(output, "Detail:") ?? output.Trim();
        var valid = output.Contains("DV_LICENCE_EXIT: 0");

        return (valid, state, detail);
    }

    /// <summary>Reads one "Label: value" line out of the prefixed --license-status output.</summary>
    private static string? ReadReportedValue(string output, string label)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Replace("DV_LICENCE:", string.Empty).Trim();
            if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return trimmed[label.Length..].Trim();
        }

        return null;
    }

    /// <summary>Escapes a value for use inside a single-quoted PowerShell string.</summary>
    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");

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


