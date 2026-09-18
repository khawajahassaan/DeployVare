# DeployVare offline manual deployment. Run as Administrator from this folder: .\Deploy.ps1
# Reads deploy-config.json. Does not call DeployVare or Azure DevOps. Does not print secrets.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$PackageRoot = $PSScriptRoot
$ConfigPath = Join-Path $PackageRoot 'deploy-config.json'
$LogLines = New-Object System.Collections.Generic.List[string]

function Write-Step([string]$Message) {
    $line = $Message
    $LogLines.Add($line)
    Write-Host $line
}

function Fail([string]$Message) {
    Write-Step "FAILED: $Message"
    try {
        if ($script:AppPoolStopped -and $script:Cfg -and $script:Cfg.appPoolName) {
            Import-Module WebAdministration -ErrorAction SilentlyContinue
            Start-WebAppPool -Name $script:Cfg.appPoolName -ErrorAction SilentlyContinue
            Write-Step "Attempted to restart application pool $($script:Cfg.appPoolName) after failure."
        }
    } catch { }
    exit 1
}

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'Administrator privileges are required. Please run Deploy.ps1 as Administrator.'
        exit 1
    }
}

function Read-Config {
    if (-not (Test-Path -LiteralPath $ConfigPath)) { Fail 'deploy-config.json was not found next to Deploy.ps1.' }
    $raw = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8
    $cfg = $raw | ConvertFrom-Json
    if (-not $cfg.websiteName -or -not $cfg.appPoolName -or -not $cfg.deploymentPath -or -not $cfg.siteHostName) {
        Fail 'deploy-config.json is missing websiteName, appPoolName, deploymentPath or siteHostName.'
    }
    if ($cfg.deploymentPath -notmatch '^[A-Za-z]:\\') {
        Fail "Deployment path '$($cfg.deploymentPath)' is not a rooted Windows path."
    }
    if ($cfg.deploymentPath -match '\.\.') { Fail 'Deployment path must not contain ..' }
    return $cfg
}

function Installer-Path([string]$Pattern) {
    $dir = Join-Path $PackageRoot 'installers'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    return Get-ChildItem -LiteralPath $dir -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

function Invoke-KnownInstaller([string]$File, [int]$TimeoutMinutes) {
    if (-not $File) { throw 'Installer path is empty.' }
    $installersRoot = [IO.Path]::GetFullPath((Join-Path $PackageRoot 'installers'))
    $full = [IO.Path]::GetFullPath($File)
    if (-not $full.StartsWith($installersRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to run installer outside the package installers folder: $File"
    }
    if (-not (Test-Path -LiteralPath $full)) { throw "Installer not found: $File" }
    $timeoutMs = [Math]::Max(1, $TimeoutMinutes) * 60000
    if ($full.EndsWith('.msi', [StringComparison]::OrdinalIgnoreCase)) {
        $msiArgs = @('/i', ('"' + $full + '"'), '/qn', '/norestart')
        if ([IO.Path]::GetFileName($full) -like 'Memurai*') { $msiArgs += @('INSTALL_SERVICE=1', 'PORT=6379') }
        $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $msiArgs -PassThru
    } else {
        $process = Start-Process -FilePath $full -ArgumentList @('/install', '/quiet', '/norestart') -PassThru
    }
    $null = $process.Handle
    if (-not $process.WaitForExit($timeoutMs)) {
        $process.Kill()
        throw "Installer did not finish within $TimeoutMinutes minutes"
    }
    $code = $process.ExitCode
    if (($code -ne 0) -and ($code -ne 3010) -and ($code -ne 1641)) {
        throw "Installer exited with code $code"
    }
}

function Ensure-Iis {
    $w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
    $installed = ($null -ne $w3svc) -and (Test-Path "$env:windir\System32\inetsrv\appcmd.exe")
    if ($installed) {
        if ($w3svc.Status -ne 'Running') { Start-Service W3SVC }
        Write-Step '[OK] IIS is installed and running'
        return
    }
    Write-Step 'Installing IIS (Web Server, WebSockets, management tools)...'
    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        $result = Install-WindowsFeature -Name Web-Server, Web-WebSockets, Web-Mgmt-Console, Web-Scripting-Tools -IncludeManagementTools
        if (-not $result.Success) { Fail 'IIS installation failed (Install-WindowsFeature).' }
    } else {
        $result = Enable-WindowsOptionalFeature -Online -All -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-WebSockets, IIS-ManagementConsole, IIS-ManagementScriptingTools
        if (-not $result) { Fail 'IIS installation failed (Enable-WindowsOptionalFeature).' }
    }
    $w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
    if (-not $w3svc) { Fail 'IIS installation finished but W3SVC was not found.' }
    if ($w3svc.Status -ne 'Running') { Start-Service W3SVC }
    Write-Step '[OK] IIS installed'
}

function Ensure-DotNet([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api') { return }
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $runtimes = @()
    $sdks = @()
    if (Test-Path $dotnet) {
        $runtimes = @(& $dotnet --list-runtimes 2>$null | Where-Object { $_ -like 'Microsoft.AspNetCore.App *' } | ForEach-Object { ($_ -split ' ')[1] })
        $sdks = @(& $dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] })
    }
    $moduleOk = $false
    if (Get-Module -ListAvailable WebAdministration) {
        Import-Module WebAdministration
        $moduleOk = ((Get-WebGlobalModule | ForEach-Object { $_.Name }) -contains 'AspNetCoreModuleV2')
    }

    $major = [int]$Cfg.requiredDotNetMajor
    $hasRuntime = $runtimes | Where-Object { ($_ -split '\.')[0] -eq "$major" }
    if (-not $hasRuntime -or -not $moduleOk) {
        $file = Installer-Path 'dotnet-hosting-*-win.exe'
        if (-not $file) { Fail ".NET $major Hosting Bundle is missing and no installer is in the package installers folder." }
        Write-Step "Installing .NET $major Hosting Bundle ($([IO.Path]::GetFileName($file)))..."
        Invoke-KnownInstaller $file 30
        net stop was /y | Out-Null
        net start w3svc | Out-Null
        Write-Step '[OK] .NET Hosting Bundle installed'
    } else {
        Write-Step "[OK] .NET $major Hosting Bundle"
    }

    $minSdk = [int]$Cfg.minimumSdkMajor
    $sdkOk = $sdks | Where-Object { [int](($_ -split '\.')[0]) -ge $minSdk }
    if (-not $sdkOk) {
        $file = Installer-Path 'dotnet-sdk-*-win-x64.exe'
        if (-not $file) { Fail "No .NET SDK $minSdk or newer is installed and no SDK installer is in the package." }
        Write-Step "Installing .NET SDK ($([IO.Path]::GetFileName($file)))..."
        Invoke-KnownInstaller $file 30
        Write-Step '[OK] .NET SDK installed'
    } else {
        Write-Step "[OK] .NET SDK ($minSdk or newer)"
    }
}

function Ensure-UrlRewrite([object]$Cfg) {
    if ($Cfg.serverType -ne 'Frontend') { return }
    $present = (Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite')
    if (-not $present) {
        Import-Module WebAdministration -ErrorAction SilentlyContinue
        $present = ((Get-WebGlobalModule | ForEach-Object { $_.Name }) -contains 'RewriteModule')
    }
    if ($present) {
        Write-Step '[OK] IIS URL Rewrite'
        return
    }
    $file = Installer-Path 'rewrite_amd64*.msi'
    if (-not $file) { Fail 'IIS URL Rewrite is not installed and rewrite_amd64*.msi is not in the package installers folder.' }
    Write-Step "Installing IIS URL Rewrite ($([IO.Path]::GetFileName($file)))..."
    Invoke-KnownInstaller $file 15
    Write-Step '[OK] IIS URL Rewrite installed'
}

function Get-PreferredRedisService {
    $memurai = Get-Service -Name Memurai -ErrorAction SilentlyContinue
    if ($memurai) { return $memurai }
    return Get-Service -Name Redis -ErrorAction SilentlyContinue
}

function Stop-ListenersOnRedisPort([int]$Port) {
    Get-Service -Name Redis,Memurai -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Status -ne 'Stopped') { Stop-Service -Name $_.Name -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 2
    $find = ':' + $Port
    netstat -ano | findstr $find
    foreach ($line in (netstat -ano | findstr $find)) {
        if ($line -notmatch 'LISTENING') { continue }
        $procId = ($line.Trim() -split '\s+')[-1]
        if ($procId -match '^\d+$' -and [int]$procId -gt 0 -and [int]$procId -ne $PID) {
            cmd /c ('taskkill /PID ' + $procId + ' /F') | Out-Null
        }
    }
    Start-Sleep -Seconds 1
}

function Ensure-Memurai([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api') { return }
    $port = [int]$Cfg.redisPort
    if ($port -le 0) { $port = 6379 }
    $memuraiExe = Join-Path $env:ProgramFiles 'Memurai\memurai.exe'
    $svc = Get-PreferredRedisService
    if ((Test-Path $memuraiExe) -or ($svc -and $svc.Name -eq 'Memurai')) {
        if ($svc -and $svc.Name -eq 'Memurai') {
            Set-Service -Name Memurai -StartupType Automatic
            if ($svc.Status -ne 'Running') { Start-Service -Name Memurai }
        }
        Write-Step "[OK] Memurai is running (Redis-compatible on port $port)"
        return
    }
    $file = Installer-Path 'Memurai*.msi'
    if (-not $file) { $file = Installer-Path 'Redis-x64-*.msi' }
    if (-not $file) { Fail 'Memurai / Redis is not installed and no Memurai*.msi installer is in the package.' }
    Write-Step "Installing Memurai (Redis $($Cfg.requiredRedisMajor) on port $port) ($([IO.Path]::GetFileName($file)))..."
    Stop-ListenersOnRedisPort $port
    Invoke-KnownInstaller $file 15
    $svc = Get-PreferredRedisService
    if (-not $svc) { Fail 'Memurai installer finished but the Redis/Memurai service was not found.' }
    Set-Service -Name $svc.Name -StartupType Automatic
    if ($svc.Status -ne 'Running') { Start-Service -Name $svc.Name }
    $redis = Get-Service -Name Redis -ErrorAction SilentlyContinue
    if ($svc.Name -eq 'Memurai' -and $redis) {
        Stop-Service -Name Redis -Force -ErrorAction SilentlyContinue
        Set-Service -Name Redis -StartupType Manual
    }
    Start-Sleep -Seconds 3
    Write-Step "[OK] $($svc.Name) installed and running on port $port"
}

function Ensure-Site([object]$Cfg) {
    Import-Module WebAdministration
    $siteName = [string]$Cfg.websiteName
    $poolName = [string]$Cfg.appPoolName
    $path = [string]$Cfg.deploymentPath
    $hostName = [string]$Cfg.siteHostName

    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -Path $path -ItemType Directory -Force | Out-Null
        Write-Step "Created folder $path"
    }

    if (-not (Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -eq $poolName })) {
        New-WebAppPool -Name $poolName | Out-Null
        Set-ItemProperty -Path "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''
        Write-Step "Created application pool $poolName (No Managed Code)"
    } else {
        Write-Step "Application pool $poolName already exists"
    }

    $site = Get-Website | Where-Object { $_.Name -eq $siteName } | Select-Object -First 1
    if (-not $site) {
        $ids = @(Get-Website | ForEach-Object { [int]$_.id })
        $id = if ($ids.Count -gt 0) { ($ids | Measure-Object -Maximum).Maximum + 1 } else { 1 }
        New-Website -Name $siteName -Id $id -PhysicalPath $path -ApplicationPool $poolName -Port 80 -HostHeader $hostName | Out-Null
        Write-Step "Created site $siteName with binding http://${hostName}:80"
    } else {
        Set-ItemProperty -Path "IIS:\Sites\$siteName" -Name applicationPool -Value $poolName
        Write-Step "Site $siteName already exists; application pool set to $poolName"
    }

    & icacls $path /grant ('IIS AppPool\' + $poolName + ':(OI)(CI)RX') /Q | Out-Null
}

function Get-DefaultThumbprint([object]$Cfg) {
    $name = [string]$Cfg.defaultCertificateName
    if (-not $name) { $name = 'WMSVC-SHA2' }
    $now = Get-Date
    $match = Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.HasPrivateKey -and $_.NotAfter -gt $now -and $_.FriendlyName -eq $name
    } | Select-Object -First 1
    if ($match) { return $match.Thumbprint }
    return Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt $now } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1 -ExpandProperty Thumbprint
}

function Ensure-Certificate([object]$Cfg) {
    $mode = [string]$Cfg.certificateMode
    if ($mode -eq 'UserProvided') {
        $relative = [string]$Cfg.certificateFile
        if (-not $relative) { Fail 'Certificate mode is UserProvided but certificateFile is not set in deploy-config.json.' }
        $file = Join-Path $PackageRoot ($relative -replace '/', '\')
        $certRoot = [IO.Path]::GetFullPath((Join-Path $PackageRoot 'certificate'))
        $full = [IO.Path]::GetFullPath($file)
        if (-not $full.StartsWith($certRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Fail 'Certificate file must be inside the package certificate folder.'
        }
        if (-not (Test-Path -LiteralPath $full)) { Fail "Certificate file not found: $relative" }
        $secure = Read-Host 'Password for the packaged .pfx (blank if none)' -AsSecureString
        $imported = Import-PfxCertificate -FilePath $full -CertStoreLocation 'Cert:\LocalMachine\My' -Password $secure
        $cert = @($imported) | Where-Object { $_.HasPrivateKey } | Select-Object -First 1
        if (-not $cert) { Fail 'The certificate file does not contain a private key.' }
        Write-Step "[OK] Imported certificate $($cert.Subject)"
        return $cert.Thumbprint
    }

    $thumb = Get-DefaultThumbprint $Cfg
    if (-not $thumb) {
        Fail "No valid certificate with a private key was found in LocalMachine\My (including $($Cfg.defaultCertificateName)). Re-generate the package with your own .pfx."
    }
    Write-Step "[OK] Using installed certificate $thumb"
    return $thumb
}

function Ensure-Bindings([object]$Cfg, [string]$Thumbprint) {
    Import-Module WebAdministration
    $siteName = [string]$Cfg.websiteName
    $hostName = [string]$Cfg.siteHostName
    $requireSni = [bool]$Cfg.requireSni
    $httpsPort = if ([int]$Cfg.httpsPort -gt 0) { [int]$Cfg.httpsPort } else { 443 }

    if (-not (Get-Website | Where-Object { $_.Name -eq $siteName })) { Fail "Site $siteName does not exist" }
    $cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) { Fail "Certificate $Thumbprint was not found in LocalMachine\My" }
    if (-not $cert.HasPrivateKey) { Fail 'The selected certificate has no private key' }

    if (-not (Get-WebBinding -Name $siteName -Protocol 'http' -Port 80 -HostHeader $hostName)) {
        New-WebBinding -Name $siteName -Protocol 'http' -Port 80 -IPAddress '*' -HostHeader $hostName
        Write-Step "Added binding http://${hostName}:80"
    } else {
        Write-Step "Binding http://${hostName}:80 already exists"
    }

    $sslFlags = if ($requireSni) { 1 } else { 0 }
    $https = Get-WebBinding -Name $siteName -Protocol 'https' -Port $httpsPort -HostHeader $hostName
    if (-not $https) {
        New-WebBinding -Name $siteName -Protocol 'https' -Port $httpsPort -IPAddress '*' -HostHeader $hostName -SslFlags $sslFlags
        $https = Get-WebBinding -Name $siteName -Protocol 'https' -Port $httpsPort -HostHeader $hostName
        Write-Step "Added binding https://${hostName}:${httpsPort}"
    } else {
        Write-Step "Binding https://${hostName}:${httpsPort} already exists"
    }

    $sslPath = if ($requireSni) { "IIS:\SslBindings\!${httpsPort}!$hostName" } else { "IIS:\SslBindings\0.0.0.0!${httpsPort}" }
    $existing = Get-Item -Path $sslPath -ErrorAction SilentlyContinue
    if ($existing -and ($existing.Thumbprint -ne $Thumbprint)) {
        if (-not $requireSni) {
            Fail "Port $httpsPort is already bound to certificate $($existing.Thumbprint) for all sites without SNI. Enable Require SNI or choose that certificate."
        }
        Remove-Item -Path $sslPath -Force
        $existing = $null
    }
    if (-not $existing) {
        $https.AddSslCertificate($Thumbprint, 'My')
        Write-Step "Bound certificate to https://${hostName}:${httpsPort}"
    }
}

function New-DvSqlConnection([string]$DataSource, [string]$Catalog) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $DataSource
    $builder['Initial Catalog'] = $Catalog
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 30
    $builder['TrustServerCertificate'] = $true
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    $connection.Open()
    return $connection
}

function Ensure-SqlServer([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api' -or -not [bool]$Cfg.freshDatabase) { return }
    $instance = [string]$Cfg.sqlInstanceName
    if (-not $instance) { $instance = 'SQLEXPRESS' }
    $installed = $null -ne (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue)
    if ($installed) {
        Write-Step '[OK] SQL Server is installed'
        return
    }
    $file = Installer-Path 'SQLEXPR_x64*.exe'
    if (-not $file) { $file = Installer-Path 'SQL*SSEI*Expr*.exe' }
    if (-not $file) {
        Fail "SQL Server is not installed. Put SQL2025-SSEI-Expr.exe or SQLEXPR_x64_ENU.exe in the package installers folder."
    }
    Write-Host 'SQL Server Express is not installed. Enter the sa password for a mixed-mode install (not logged).'
    $secure = Read-Host 'sa password' -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $sa = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    if ($sa.Length -lt 8) { Fail 'The sa password must be at least 8 characters.' }
    if ($sa -match '\s' -or $sa.Contains('"')) { Fail 'The sa password must not contain spaces or double-quotes.' }

    Write-Step "Installing SQL Server Express (instance $instance) — this can take 10-20 minutes..."
    $extract = $null
    try {
        $arguments = @(
            '/IACCEPTSQLSERVERLICENSETERMS','/Q','/ACTION=Install','/FEATURES=SQLENGINE',
            ('/INSTANCENAME=' + $instance),'/SECURITYMODE=SQL',('/SAPWD=' + $sa),
            '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators','/TCPENABLED=1','/NPENABLED=1',
            '/SQLSVCSTARTUPTYPE=Automatic','/UpdateEnabled=False'
        )
        $isSsei = [IO.Path]::GetFileName($file) -match '(?i)SSEI'
        if ($isSsei) {
            $arguments += '/HideConsole'
            $p = Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru
        } else {
            $extract = Join-Path $env:TEMP ('DeployVare_sqlexpr_' + [guid]::NewGuid().ToString('N'))
            $ex = Start-Process -FilePath $file -ArgumentList @('/q', ('/x:' + $extract)) -Wait -PassThru
            if ($ex.ExitCode -ne 0) { throw "Extracting the SQL Server installer failed with code $($ex.ExitCode)" }
            $setup = Join-Path $extract 'SETUP.EXE'
            if (-not (Test-Path $setup)) {
                $setup = Get-ChildItem -Path $extract -Filter 'setup.exe' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
            }
            if (-not $setup) { throw 'setup.exe was not found after extracting the installer.' }
            $p = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
        }
        $code = $p.ExitCode
        if (($code -ne 0) -and ($code -ne 3010)) {
            throw "SQL Server setup exited with code $code. See Summary.txt under C:\Program Files\Microsoft SQL Server\...\Setup Bootstrap\Log"
        }
        try { New-NetFirewallRule -DisplayName 'SQL Server 1433 (DeployVare)' -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow -ErrorAction SilentlyContinue | Out-Null } catch { }
        Write-Step "[OK] SQL Server Express installed (instance $instance)"
    } finally {
        if ($extract) { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue }
        $sa = $null
    }
}

function Get-BaselineBak {
    $dir = Join-Path $PackageRoot 'database'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    $bak = Get-ChildItem -LiteralPath $dir -Filter '*.bak' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($bak) { return $bak.FullName }
    $zip = Get-ChildItem -LiteralPath $dir -Filter '*.zip' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $zip) { return $null }
    $extract = Join-Path $dir '_extracted'
    if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    Expand-Archive -LiteralPath $zip.FullName -DestinationPath $extract -Force
    $found = Get-ChildItem -LiteralPath $extract -Filter '*.bak' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) { Fail "No .bak file was found inside $($zip.Name)." }
    return $found.FullName
}

function Restore-Database([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api' -or -not [bool]$Cfg.freshDatabase) { return }
    $database = [string]$Cfg.databaseName
    $dataSource = [string]$Cfg.sqlDataSource
    if (-not $database) { Fail 'Fresh Database was selected but databaseName is missing from deploy-config.json.' }
    if (-not $dataSource) { $dataSource = '.\' + $Cfg.sqlInstanceName }

    $bak = Get-BaselineBak
    if (-not $bak) { Fail 'Fresh Database was selected but the package has no .bak / backup zip under database\.' }

    Write-Step "Connecting to SQL Server $dataSource..."
    $master = $null
    try {
        $master = New-DvSqlConnection $dataSource 'master'
        $existsCmd = $master.CreateCommand()
        $existsCmd.CommandText = 'SELECT CASE WHEN DB_ID(@database) IS NULL THEN 0 ELSE 1 END'
        $null = $existsCmd.Parameters.AddWithValue('@database', $database)
        $exists = [int]$existsCmd.ExecuteScalar()
        if ($exists -eq 1) {
            Write-Step "Database '$database' already exists. Dropping for fresh restore..."
            $dropCmd = $master.CreateCommand()
            $dropCmd.CommandTimeout = 300
            $dropCmd.CommandText = 'ALTER DATABASE [' + $database.Replace(']', ']]') + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + $database.Replace(']', ']]') + ']'
            $null = $dropCmd.ExecuteNonQuery()
            Write-Step "[OK] Database dropped, will restore fresh"
        }

        $listCmd = $master.CreateCommand()
        $listCmd.CommandTimeout = 300
        $listCmd.CommandText = 'RESTORE FILELISTONLY FROM DISK = @bak'
        $null = $listCmd.Parameters.AddWithValue('@bak', $bak)
        $reader = $listCmd.ExecuteReader()
        $logical = @()
        while ($reader.Read()) {
            $logical += @{ Name = [string]$reader['LogicalName']; Type = [string]$reader['Type'] }
        }
        $reader.Close()
        if ($logical.Count -lt 2) { Fail 'RESTORE FILELISTONLY did not return data and log files.' }

        $pathCmd = $master.CreateCommand()
        $pathCmd.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))"
        $pathReader = $pathCmd.ExecuteReader()
        $dataPath = $null
        $logPath = $null
        if ($pathReader.Read()) {
            $dataPath = [string]$pathReader.GetValue(0)
            $logPath = [string]$pathReader.GetValue(1)
        }
        $pathReader.Close()
        if (-not $dataPath) { $dataPath = 'C:\Program Files\Microsoft SQL Server\MSSQL\DATA\' }
        if (-not $logPath) { $logPath = $dataPath }

        $moves = New-Object System.Collections.Generic.List[string]
        $i = 0
        foreach ($file in $logical) {
            $i++
            $isLog = ([string]$file.Type).Trim() -eq 'L'
            $folder = if ($isLog) { $logPath } else { $dataPath }
            $ext = if ($isLog) { '.ldf' } else { '.mdf' }
            if ($i -gt 2 -and -not $isLog) { $ext = '.ndf' }
            $phys = Join-Path $folder ($database + '_' + $file.Name + $ext)
            $moves.Add(('MOVE N''' + $file.Name.Replace('''', '''''') + ''' TO N''' + $phys.Replace('''', '''''') + ''''))
        }

        Write-Step "Restoring database '$database' from baseline backup (SQL Server chooses the data/log folders)..."
        $restore = $master.CreateCommand()
        $restore.CommandTimeout = 0
        $restore.CommandText = "RESTORE DATABASE [" + $database.Replace(']', ']]') + "] FROM DISK = @bak WITH " + ($moves -join ', ') + ', RECOVERY'
        $null = $restore.Parameters.AddWithValue('@bak', $bak)
        $null = $restore.ExecuteNonQuery()
        Write-Step "[OK] Database '$database' restored and online"
    } catch {
        Fail "Database restore failed: $($_.Exception.Message)"
    } finally {
        if ($master) { $master.Close() }
    }
}

function Grant-AppPoolAccess([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api' -or -not $Cfg.databaseName) { return }
    Import-Module WebAdministration
    $poolName = [string]$Cfg.appPoolName
    $login = 'IIS APPPOOL\' + $poolName
    $pool = Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -eq $poolName } | Select-Object -First 1
    if ($pool) {
        $identity = [string]$pool.processModel.identityType
        if ($identity -eq 'NetworkService') { $login = 'NT AUTHORITY\NETWORK SERVICE' }
        elseif ($identity -eq 'LocalService') { $login = 'NT AUTHORITY\LOCAL SERVICE' }
        elseif ($identity -eq 'LocalSystem') { $login = 'NT AUTHORITY\SYSTEM' }
        elseif ($identity -eq 'SpecificUser' -and $pool.processModel.userName) { $login = [string]$pool.processModel.userName }
    }
    $database = [string]$Cfg.databaseName
    $dataSource = [string]$Cfg.sqlDataSource
    Write-Step "Granting $login db_owner on '$database'..."
    $db = New-DvSqlConnection $dataSource $database
    try {
        $cmd = $db.CreateCommand()
        $cmd.CommandText = @'
SET NOCOUNT ON;
DECLARE @sql nvarchar(max);
DECLARE @userName sysname;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
BEGIN
    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' FROM WINDOWS WITH DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
    EXEC sys.sp_executesql @sql;
END
SELECT @userName = dp.name
FROM sys.database_principals dp
JOIN sys.server_principals sp ON dp.sid = sp.sid
WHERE sp.name = @login;
IF @userName IS NULL
BEGIN
    SET @userName = @login;
    SET @sql = N'CREATE USER ' + QUOTENAME(@userName) + N' FOR LOGIN ' + QUOTENAME(@login);
    EXEC sys.sp_executesql @sql;
END
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members rm
    JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
    JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
    WHERE r.name = N'db_owner' AND m.name = @userName)
BEGIN
    SET @sql = N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@userName);
    EXEC sys.sp_executesql @sql;
END
'@
        $null = $cmd.Parameters.AddWithValue('@login', $login)
        $null = $cmd.ExecuteNonQuery()
        Write-Step "[OK] $login has db_owner on '$database'"
    } finally {
        $db.Close()
    }
}

function Backup-Site([object]$Cfg) {
    $path = [string]$Cfg.deploymentPath
    $backupRoot = [string]$Cfg.backupPath
    if (-not $backupRoot) { $backupRoot = 'C:\DeployVare\Backups' }
    if (-not (Test-Path -LiteralPath $path)) { return }
    $count = @(Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue).Count
    if ($count -eq 0) { return }
    New-Item -Path $backupRoot -ItemType Directory -Force | Out-Null
    $zip = Join-Path $backupRoot ($Cfg.websiteName + '_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.zip')
    Compress-Archive -Path (Join-Path $path '*') -DestinationPath $zip -Force -CompressionLevel Fastest
    Write-Step "Backed up existing site to $zip"
}

function Publish-App([object]$Cfg) {
    $stagingRoot = Join-Path $env:TEMP ('DeployVare_stage_' + [guid]::NewGuid().ToString('N'))
    $staging = Join-Path $stagingRoot 'app'
    New-Item -Path $staging -ItemType Directory -Force | Out-Null
    $app = Join-Path $PackageRoot 'app'
    if (-not (Test-Path -LiteralPath $app)) { Fail 'app\ folder is missing from the package.' }
    Copy-Item -Path (Join-Path $app '*') -Destination $staging -Recurse -Force

    $contentRoot = Join-Path $PackageRoot 'content'
    if (Test-Path -LiteralPath $contentRoot) {
        foreach ($folder in (Get-ChildItem -LiteralPath $contentRoot -Directory)) {
            $dest = Join-Path $Cfg.deploymentPath $folder.Name
            if (Test-Path -LiteralPath $dest) {
                Write-Step "Keeping existing content folder $($folder.Name) on the server"
            } else {
                Copy-Item -Path $folder.FullName -Destination $dest -Recurse -Force
                Write-Step "Copied content folder $($folder.Name)"
            }
        }
    }

    $scriptsSrc = Join-Path $PackageRoot 'sqlscripts'
    if ((Test-Path -LiteralPath $scriptsSrc) -and $Cfg.serverType -eq 'Api') {
        $scriptsDest = Join-Path $staging 'sqlscripts'
        Copy-Item -Path $scriptsSrc -Destination $scriptsDest -Recurse -Force
    }

    $keep = @()
    if ($Cfg.keepOnServerPaths) { $keep += @($Cfg.keepOnServerPaths) }
    if ([bool]$Cfg.preserveWebConfig) { $keep += 'web.config' }
    $deploymentPath = [string]$Cfg.deploymentPath
    if (-not (Test-Path -LiteralPath $deploymentPath)) {
        New-Item -Path $deploymentPath -ItemType Directory -Force | Out-Null
    }
    foreach ($relative in ($keep | Select-Object -Unique)) {
        $target = Join-Path $deploymentPath ($relative -replace '/', '\')
        $staged = Join-Path $staging ($relative -replace '/', '\')
        if ((Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $staged)) {
            Remove-Item -LiteralPath $staged -Recurse -Force
            Write-Step "Keeping server copy of $relative"
        }
    }

    & robocopy $staging $deploymentPath /E /IS /IT /R:2 /W:2 /NP /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "robocopy failed with exit code $LASTEXITCODE" }
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Step '[OK] Application files deployed'
}

function Invoke-DbMigrator([object]$Cfg) {
    if ($Cfg.serverType -ne 'Api' -or -not [bool]$Cfg.hasScripts -or -not $Cfg.databaseName) { return }
    $deploymentPath = [string]$Cfg.deploymentPath
    $exe = Get-ChildItem -LiteralPath $deploymentPath -Filter 'DbMigrator.exe' -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $exe) { Fail 'DbMigrator.exe was not found in the deployment path.' }
    $scripts = Join-Path $deploymentPath 'sqlscripts'
    if (-not (Test-Path -LiteralPath $scripts)) { $scripts = Join-Path $PackageRoot 'sqlscripts' }
    if (-not (Test-Path -LiteralPath $scripts)) { Fail 'SQLScripts folder was not found.' }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = [string]$Cfg.sqlDataSource
    $builder['Initial Catalog'] = [string]$Cfg.databaseName
    $builder['Integrated Security'] = $true
    $builder['MultipleActiveResultSets'] = $true
    $builder['TrustServerCertificate'] = $true

    Write-Step "Running DbMigrator on '$($Cfg.databaseName)'..."
    $ErrorActionPreference = 'Continue'
    $output = & $exe $builder.ConnectionString $scripts 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    foreach ($line in $output) { Write-Host ('  ' + [string]$line) }
    if ($exitCode -ne 0) { Fail "DbMigrator exited with code $exitCode" }
    Write-Step '[OK] DbMigrator completed'
}

function Install-Licence([object]$Cfg) {
    if ([string]$Cfg.licensingMode -ne 'WithLicense') { return $false }
    $name = [string]$Cfg.licenseFileName
    if (-not $name) { Fail 'LicensingMode is WithLicense but licenseFileName is missing.' }
    $lic = Join-Path $PackageRoot (Join-Path 'licence' $name)
    if (-not (Test-Path -LiteralPath $lic)) { Fail "Licence file not found: licence\$name" }
    $deploymentPath = [string]$Cfg.deploymentPath
    $exe = $null
    $candidates = Get-ChildItem -LiteralPath $deploymentPath -Filter '*.exe' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'DbMigrator.exe' }
    foreach ($candidate in $candidates) {
        $probe = & $candidate.FullName --license-status 2>&1
        if ($LASTEXITCODE -eq 0 -or ($probe -join ' ') -match 'Licensing:|State:') {
            $exe = $candidate.FullName
            break
        }
    }
    if (-not $exe) { Fail 'No application in the deployment path supports the licensing commands.' }
    $ErrorActionPreference = 'Continue'
    $output = & $exe --license-install $lic 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    foreach ($line in $output) { Write-Host ('  ' + [string]$line) }
    if ($code -ne 0) { Fail "Licence install exited with code $code" }
    $request = 'C:\Temp\activation-request.json'
    New-Item -Path 'C:\Temp' -ItemType Directory -Force | Out-Null
    $ErrorActionPreference = 'Continue'
    $null = & $exe --license-request $request 2>&1
    $ErrorActionPreference = 'Stop'
    Write-Step "[OK] Licence installed. Activation request: $request"
    Write-Host 'Email that file to the vendor, apply the response with --license-apply, then start the site. The site will not be started now.'
    return $true
}

function Ensure-HostsEntry([string]$HostName) {
    if ($HostName -eq 'localhost' -or $HostName -like '127.0.0.1*' -or $HostName -like 'localhost*') { return }

    $hostsFile = Join-Path $env:windir 'System32\drivers\etc\hosts'
    $entry = "127.0.0.1`t$HostName"

    try {
        $content = @(Get-Content -LiteralPath $hostsFile -ErrorAction SilentlyContinue)
        if (-not ($content | Where-Object { $_ -match "^\s*127\.0\.0\.1\s+$([regex]::Escape($HostName))\s*$" })) {
            Add-Content -LiteralPath $hostsFile -Value $entry
            Write-Step "[OK] Added hosts entry: $entry"
        }
    } catch {
        Write-Step "WARNING: Could not update hosts file. Add manually: $entry"
    }
}

function Start-Site([object]$Cfg) {
    Import-Module WebAdministration
    Ensure-HostsEntry $Cfg.siteHostName
    try { Stop-Website -Name $Cfg.websiteName -ErrorAction Stop } catch { }
    Start-Sleep -Seconds 1
    try { Start-WebAppPool -Name $Cfg.appPoolName -ErrorAction Stop } catch { }
    try { Start-Website -Name $Cfg.websiteName -ErrorAction Stop } catch { }
    Start-Sleep -Seconds 2
    $poolStatus = (Get-WebAppPoolState -Name $Cfg.appPoolName).Value
    $siteStatus = (Get-Website -Name $Cfg.websiteName).State
    if ($poolStatus -ne 'Started' -or $siteStatus -ne 'Started') {
        Fail "Verification failed. App pool: $poolStatus. Site: $siteStatus."
    }
    Write-Step "[OK] App pool and site are Started"
}

# ---- main ----
Assert-Admin
$script:Cfg = Read-Config
$script:AppPoolStopped = $false
$isApi = $script:Cfg.serverType -eq 'Api'

Write-Step "DeployVare manual deployment: $($script:Cfg.serverName)"
Write-Step "[1] Administrator privileges... OK"

Ensure-Iis
Ensure-DotNet $script:Cfg
Ensure-UrlRewrite $script:Cfg
Ensure-Memurai $script:Cfg
Ensure-Site $script:Cfg
$thumb = Ensure-Certificate $script:Cfg
Ensure-Bindings $script:Cfg $thumb

if ($isApi -and [bool]$script:Cfg.freshDatabase) {
    Ensure-SqlServer $script:Cfg
    Restore-Database $script:Cfg
} elseif ($isApi -and $script:Cfg.databaseName) {
    Write-Step "Existing Database: will not restore. Validating '$($script:Cfg.databaseName)' on $($script:Cfg.sqlDataSource)..."
    $check = New-DvSqlConnection ([string]$script:Cfg.sqlDataSource) 'master'
    try {
        $cmd = $check.CreateCommand()
        $cmd.CommandText = 'SELECT CASE WHEN DB_ID(@database) IS NULL THEN 0 ELSE 1 END'
        $null = $cmd.Parameters.AddWithValue('@database', [string]$script:Cfg.databaseName)
        if ([int]$cmd.ExecuteScalar() -ne 1) { Fail "Database '$($script:Cfg.databaseName)' does not exist. Choose Fresh Database or create it first." }
        Write-Step "[OK] Database '$($script:Cfg.databaseName)' exists"
    } finally { $check.Close() }
}

Import-Module WebAdministration
Write-Step 'Stopping application pool...'
Stop-WebAppPool -Name $script:Cfg.appPoolName -ErrorAction SilentlyContinue
$script:AppPoolStopped = $true
Backup-Site $script:Cfg
Publish-App $script:Cfg
if ($isApi) { Grant-AppPoolAccess $script:Cfg }
if ($isApi) { Invoke-DbMigrator $script:Cfg }
$licensedPending = Install-Licence $script:Cfg

if ($licensedPending) {
    Write-Step 'Site left stopped until the licence is activated on this server.'
    Write-Host 'Deployment completed. Activate the licence, then start the app pool and site in IIS.'
    exit 0
}

Start-Site $script:Cfg
$script:AppPoolStopped = $false
Write-Host 'Deployment completed successfully.'
exit 0
