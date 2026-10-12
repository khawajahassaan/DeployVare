# Installs DeployVare itself on one Windows server: database (SQL Server), API (IIS) and frontend (IIS).
# Run as Administrator from the unzipped package folder:  .\Install-DeployVare.ps1
#
# Shows every name it will use (IIS sites, app pools, folders, host names, SQL Server, database) and lets you keep
# each with Enter or type a new value. -UseDefaults installs with the defaults without asking.
# Safe to run again for an upgrade: an existing database is never restored over, and an existing
# appsettings.Production.json keeps its JWT secret (only the connection string, CORS origin and installers
# folder are refreshed).
param([switch]$UseDefaults)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PackageRoot = $PSScriptRoot

function Write-Step([string]$Message) { Write-Host $Message }
function Fail([string]$Message) { Write-Host "FAILED: $Message"; exit 1 }

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'Administrator privileges are required. Run Install-DeployVare.ps1 as Administrator.'
        exit 1
    }
}

function Read-Setting([string]$Label, [string]$Default, [scriptblock]$Validate, [string]$Hint) {
    $ask = (-not $UseDefaults) -and [Environment]::UserInteractive
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        $value = $Default
        if ($ask) {
            $answer = Read-Host "  $Label [$Default]"
            if (-not [string]::IsNullOrWhiteSpace($answer)) { $value = $answer.Trim() }
        }
        if (-not $Validate -or (& $Validate $value)) { return $value }
        if (-not $ask) { Fail "$Label '$value' is not valid. $Hint" }
        Write-Host "  '$value' is not valid. $Hint"
    }
    Fail "$Label was not valid after several attempts."
}

$IisName = { param($v) $v -and ($v -notmatch '[\\/:*?"<>|]') }
$WinPath = { param($v) $v -and ($v -match '^[A-Za-z]:\\') -and ($v -notmatch '\.\.') }
$HostName = { param($v) $v -and ($v -match '^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$') }
$DbName = { param($v) $v -and ($v.Length -le 128) -and ($v -notmatch '[\[\];''"]') }

function Installer-Path([string]$Pattern) {
    $dir = Join-Path $PackageRoot 'installers'
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    return Get-ChildItem -LiteralPath $dir -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}

function Ensure-Prerequisites {
    $w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
    if (-not $w3svc) {
        Write-Step 'Installing IIS...'
        if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
            $result = Install-WindowsFeature -Name Web-Server, Web-WebSockets, Web-Mgmt-Console, Web-Scripting-Tools -IncludeManagementTools
            if (-not $result.Success) { Fail 'IIS installation failed.' }
        } else {
            $null = Enable-WindowsOptionalFeature -Online -All -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-WebSockets, IIS-ManagementConsole, IIS-ManagementScriptingTools
        }
        $w3svc = Get-Service -Name W3SVC
    }
    if ($w3svc.Status -ne 'Running') { Start-Service W3SVC }
    Import-Module WebAdministration
    Write-Step '[OK] IIS'

    $hasModule = (Get-WebGlobalModule | ForEach-Object { $_.Name }) -contains 'AspNetCoreModuleV2'
    if (-not $hasModule) {
        $file = Installer-Path 'dotnet-hosting-*-win.exe'
        if (-not $file) { Fail 'The .NET Hosting Bundle is not installed and dotnet-hosting-*-win.exe is not in the installers folder.' }
        Write-Step "Installing the .NET Hosting Bundle ($([IO.Path]::GetFileName($file)))..."
        $p = Start-Process -FilePath $file -ArgumentList @('/install', '/quiet', '/norestart') -Wait -PassThru
        if (($p.ExitCode -ne 0) -and ($p.ExitCode -ne 3010)) { Fail "Hosting Bundle installer exited with code $($p.ExitCode)" }
        net stop was /y | Out-Null
        net start w3svc | Out-Null
    }
    Write-Step '[OK] .NET Hosting Bundle (ASP.NET Core Module)'
}

function New-SqlConnection([string]$DataSource, [string]$Catalog) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $DataSource
    $builder['Initial Catalog'] = $Catalog
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 30
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    $connection.Open()
    return $connection
}

function Ensure-Database([string]$DataSource, [string]$Database) {
    $master = New-SqlConnection $DataSource 'master'
    try {
        $cmd = $master.CreateCommand()
        $cmd.CommandText = 'SELECT CASE WHEN DB_ID(@d) IS NULL THEN 0 ELSE 1 END'
        $null = $cmd.Parameters.AddWithValue('@d', $Database)
        if ([int]$cmd.ExecuteScalar() -eq 1) {
            Write-Step "[OK] Database '$Database' already exists on $DataSource; it is kept as it is (no restore)"
            return
        }

        $bak = Get-ChildItem -LiteralPath (Join-Path $PackageRoot 'database') -Filter '*.bak' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $bak) { Fail "Database '$Database' does not exist and the package has no database\*.bak to restore." }

        $list = $master.CreateCommand()
        $list.CommandText = 'RESTORE FILELISTONLY FROM DISK = @bak'
        $null = $list.Parameters.AddWithValue('@bak', $bak.FullName)
        $reader = $list.ExecuteReader()
        $files = @()
        while ($reader.Read()) { $files += @{ Name = [string]$reader['LogicalName']; Type = [string]$reader['Type'] } }
        $reader.Close()

        $paths = $master.CreateCommand()
        $paths.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))"
        $pr = $paths.ExecuteReader(); $null = $pr.Read()
        $dataPath = [string]$pr.GetValue(0); $logPath = [string]$pr.GetValue(1); $pr.Close()
        if (-not $logPath) { $logPath = $dataPath }

        $moves = foreach ($f in $files) {
            $isLog = $f.Type.Trim() -eq 'L'
            $target = Join-Path $(if ($isLog) { $logPath } else { $dataPath }) ($Database + '_' + $f.Name + $(if ($isLog) { '.ldf' } else { '.mdf' }))
            'MOVE N''' + $f.Name.Replace('''', '''''') + ''' TO N''' + $target.Replace('''', '''''') + ''''
        }

        Write-Step "Restoring '$Database' from $($bak.Name)..."
        $restore = $master.CreateCommand()
        $restore.CommandTimeout = 0
        $restore.CommandText = 'RESTORE DATABASE [' + $Database.Replace(']', ']]') + '] FROM DISK = @bak WITH ' + ($moves -join ', ') + ', RECOVERY'
        $null = $restore.Parameters.AddWithValue('@bak', $bak.FullName)
        $null = $restore.ExecuteNonQuery()
        Write-Step "[OK] Database '$Database' restored"
    } finally { $master.Close() }
}

function Grant-DbOwner([string]$DataSource, [string]$Database, [string]$Login) {
    $db = New-SqlConnection $DataSource $Database
    try {
        $cmd = $db.CreateCommand()
        $cmd.CommandText = @'
SET NOCOUNT ON;
DECLARE @sql nvarchar(max), @userName sysname;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
BEGIN
    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' FROM WINDOWS WITH DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
    EXEC sys.sp_executesql @sql;
END
SELECT @userName = dp.name FROM sys.database_principals dp JOIN sys.server_principals sp ON dp.sid = sp.sid WHERE sp.name = @login;
IF @userName IS NULL
BEGIN
    SET @userName = @login;
    SET @sql = N'CREATE USER ' + QUOTENAME(@userName) + N' FOR LOGIN ' + QUOTENAME(@login);
    EXEC sys.sp_executesql @sql;
END
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
    JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
    JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
    WHERE r.name = N'db_owner' AND m.name = @userName)
BEGIN
    SET @sql = N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@userName);
    EXEC sys.sp_executesql @sql;
END
'@
        $null = $cmd.Parameters.AddWithValue('@login', $Login)
        $null = $cmd.ExecuteNonQuery()
        Write-Step "[OK] $Login has db_owner on '$Database'"
    } finally { $db.Close() }
}

function Get-DefaultThumbprint {
    $now = Get-Date
    $match = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt $now -and $_.FriendlyName -eq 'WMSVC-SHA2' } | Select-Object -First 1
    if ($match) { return $match.Thumbprint }
    return Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt $now } |
        Sort-Object NotAfter -Descending | Select-Object -First 1 -ExpandProperty Thumbprint
}

function Ensure-Site([string]$Site, [string]$Pool, [string]$Path, [string]$HostHeader, [string]$Thumbprint) {
    New-Item -Path $Path -ItemType Directory -Force | Out-Null
    if (-not (Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -eq $Pool })) {
        New-WebAppPool -Name $Pool | Out-Null
        Write-Step "Created application pool $Pool"
    }
    Set-ItemProperty -Path "IIS:\AppPools\$Pool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty -Path "IIS:\AppPools\$Pool" -Name processModel.loadUserProfile -Value $true

    $existing = Get-Website | Where-Object { $_.Name -eq $Site } | Select-Object -First 1
    if (-not $existing) {
        $ids = @(Get-Website | ForEach-Object { [int]$_.id })
        $id = if ($ids.Count -gt 0) { ($ids | Measure-Object -Maximum).Maximum + 1 } else { 1 }
        New-Website -Name $Site -Id $id -PhysicalPath $Path -ApplicationPool $Pool -Port 80 -HostHeader $HostHeader | Out-Null
        Write-Step "Created site $Site (http://$HostHeader)"
    } else {
        Set-ItemProperty -Path "IIS:\Sites\$Site" -Name physicalPath -Value $Path
        Set-ItemProperty -Path "IIS:\Sites\$Site" -Name applicationPool -Value $Pool
        Write-Step "Site $Site already exists; path and application pool updated"
    }

    if (-not (Get-WebBinding -Name $Site -Protocol 'http' -Port 80 -HostHeader $HostHeader)) {
        New-WebBinding -Name $Site -Protocol 'http' -Port 80 -IPAddress '*' -HostHeader $HostHeader
    }
    $https = Get-WebBinding -Name $Site -Protocol 'https' -Port 443 -HostHeader $HostHeader
    if (-not $https) {
        New-WebBinding -Name $Site -Protocol 'https' -Port 443 -IPAddress '*' -HostHeader $HostHeader -SslFlags 1
        $https = Get-WebBinding -Name $Site -Protocol 'https' -Port 443 -HostHeader $HostHeader
    }
    # A certificate already bound to this host name (e.g. a real one from win-acme / Let's Encrypt) is kept;
    # the default certificate only goes on a host name that has none yet
    $sslPath = "IIS:\SslBindings\!443!$HostHeader"
    $current = Get-Item -Path $sslPath -ErrorAction SilentlyContinue
    if ($current) {
        Write-Step "[OK] https://$HostHeader keeps its certificate ($($current.Thumbprint))"
    } else {
        $https.AddSslCertificate($Thumbprint, 'My')
        Write-Step "[OK] https://$HostHeader bound to the default certificate (SNI)"
    }
}

function Grant-Folder([string]$Path, [string]$Pool, [string]$Rights) {
    New-Item -Path $Path -ItemType Directory -Force | Out-Null
    & icacls $Path /grant ('IIS AppPool\' + $Pool + ':(OI)(CI)' + $Rights) /Q | Out-Null
}

function Copy-Folder([string]$Source, [string]$Destination, [string[]]$KeepFiles, [switch]$Mirror) {
    $options = @('/E', '/R:2', '/W:2', '/NP', '/NFL', '/NDL', '/NJH', '/NJS')
    if ($Mirror) { $options[0] = '/MIR' }
    if ($KeepFiles) { $options += '/XF'; $options += $KeepFiles }
    & robocopy $Source $Destination @options | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying $Source to $Destination failed (robocopy $LASTEXITCODE)" }
}

function Write-ApiSettings([string]$ApiPath, [string]$ConnectionString, [string]$FrontendOrigin, [string]$InstallersPath) {
    $file = Join-Path $ApiPath 'appsettings.Production.json'
    $settings = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }

    $settings | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue ([pscustomobject]@{ DeployVareDb = $ConnectionString }) -Force
    $settings | Add-Member -NotePropertyName Cors -NotePropertyValue ([pscustomobject]@{ AllowedOrigins = $FrontendOrigin }) -Force
    $settings | Add-Member -NotePropertyName ServerSetup -NotePropertyValue ([pscustomobject]@{ InstallersPath = $InstallersPath }) -Force

    # A new installation gets its own JWT secret; an upgrade keeps the existing one (signed-in users stay signed in)
    $newSecret = -not ($settings.JwtSettings -and $settings.JwtSettings.SecretKey)
    if ($newSecret) {
        $bytes = New-Object byte[] 48
        [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $settings | Add-Member -NotePropertyName JwtSettings -NotePropertyValue ([pscustomobject]@{ SecretKey = [Convert]::ToBase64String($bytes) }) -Force
    }
    # EncryptionSettings stay in appsettings.json: the copied database was encrypted with that key

    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $file -Encoding UTF8
    Write-Step "[OK] appsettings.Production.json written$(if ($newSecret) { ' with a new JWT secret' } else { '; existing JWT secret kept' })"
}

function Write-FrontendFiles([string]$WebPath, [string]$ApiUrl) {
    Set-Content -LiteralPath (Join-Path $WebPath 'config.js') -Encoding UTF8 -Value @"
// Written by Install-DeployVare.ps1
window.__DEPLOYVARE_CONFIG__ = { apiUrl: '$ApiUrl' };
"@
    # Static Next.js export: folder/index.html per page, .txt data files, woff2 fonts; config.js never cached
    Set-Content -LiteralPath (Join-Path $WebPath 'web.config') -Encoding UTF8 -Value @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <defaultDocument enabled="true">
      <files><clear /><add value="index.html" /></files>
    </defaultDocument>
    <staticContent>
      <remove fileExtension=".woff2" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <remove fileExtension=".txt" />
      <mimeMap fileExtension=".txt" mimeType="text/plain; charset=utf-8" />
    </staticContent>
    <httpErrors errorMode="Custom" existingResponse="Replace">
      <remove statusCode="404" />
      <error statusCode="404" path="/404.html" responseMode="ExecuteURL" />
    </httpErrors>
  </system.webServer>
  <location path="config.js">
    <system.webServer>
      <staticContent><clientCache cacheControlMode="DisableCache" /></staticContent>
    </system.webServer>
  </location>
</configuration>
'@
}

function Ensure-HostsEntry([string]$HostHeader) {
    $hostsFile = Join-Path $env:windir 'System32\drivers\etc\hosts'
    $content = @(Get-Content -LiteralPath $hostsFile -ErrorAction SilentlyContinue)
    if (-not ($content | Where-Object { $_ -match "^\s*127\.0\.0\.1\s+$([regex]::Escape($HostHeader))\s*$" })) {
        Add-Content -LiteralPath $hostsFile -Value "127.0.0.1`t$HostHeader"
        Write-Step "[OK] hosts entry 127.0.0.1 $HostHeader (this server only)"
    }
}

function Test-Url([string]$Url) {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 60
        return [int]$response.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
        return 0
    } finally {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = $null
    }
}

# ---- main ----
Assert-Admin
Write-Host 'DeployVare server installation (database + API + frontend on this server)'
foreach ($folder in 'api', 'web') {
    if (-not (Test-Path -LiteralPath (Join-Path $PackageRoot $folder))) { Fail "The package has no $folder\ folder. Run this from the unzipped package." }
}

if ((-not $UseDefaults) -and [Environment]::UserInteractive) { Write-Host ''; Write-Host 'Settings (press Enter to keep the value in brackets):' }
$sqlServer = Read-Setting 'SQL Server' '.' { param($v) [bool]$v } 'e.g. . for the default instance or .\SQLEXPRESS'
$database = Read-Setting 'Database name' 'DeployVare' $DbName 'Use letters, digits, underscores or dashes.'
$apiSite = Read-Setting 'API IIS site' 'DeployVareApi' $IisName 'No \ / : * ? " < > |'
$apiPool = Read-Setting 'API application pool' 'DeployVareApi' $IisName 'No \ / : * ? " < > |'
$apiPath = Read-Setting 'API folder' 'C:\inetpub\DeployVare\api' $WinPath 'A full Windows path, e.g. C:\inetpub\DeployVare\api'
$apiHost = Read-Setting 'API host name' 'deployvare-api' $HostName 'e.g. deployvare-api or api.yourdomain.com'
$webSite = Read-Setting 'Frontend IIS site' 'DeployVare' $IisName 'No \ / : * ? " < > |'
$webPool = Read-Setting 'Frontend application pool' 'DeployVareWeb' $IisName 'No \ / : * ? " < > |'
$webPath = Read-Setting 'Frontend folder' 'C:\inetpub\DeployVare\web' $WinPath 'A full Windows path, e.g. C:\inetpub\DeployVare\web'
$webHost = Read-Setting 'Frontend host name' 'deployvare' $HostName 'e.g. deployvare or deployvare.yourdomain.com'
$installersPath = Read-Setting 'Installers folder (for server setup and manual packages)' 'C:\DeployVare\Installers' $WinPath 'A full Windows path'
if ($apiHost -eq $webHost) { Fail 'The API and frontend host names must be different.' }

Write-Host ''
Ensure-Prerequisites
$thumb = Get-DefaultThumbprint
if (-not $thumb) { Fail 'No certificate with a private key was found in LocalMachine\My (the default is WMSVC-SHA2).' }

# 1. Database
Ensure-Database $sqlServer $database

# 2. API
Ensure-Site $apiSite $apiPool $apiPath $apiHost $thumb
Stop-WebAppPool -Name $apiPool -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Copy-Folder (Join-Path $PackageRoot 'api') $apiPath -KeepFiles @('appsettings.Production.json')
Write-Step '[OK] API files copied'
Write-ApiSettings $apiPath "Server=$sqlServer;Database=$database;Integrated Security=True;MultipleActiveResultSets=True;TrustServerCertificate=True" "https://$webHost" $installersPath
& icacls $apiPath /grant ('IIS AppPool\' + $apiPool + ':(OI)(CI)RX') /Q | Out-Null
Grant-Folder (Join-Path $apiPath 'wwwroot') $apiPool 'M'
Grant-Folder (Join-Path $apiPath 'logs') $apiPool 'M'
Grant-Folder (Join-Path $env:ProgramData 'DeployVare') $apiPool 'M'
Grant-DbOwner $sqlServer $database ('IIS APPPOOL\' + $apiPool)

$packagedInstallers = Join-Path $PackageRoot 'installers'
if (Test-Path -LiteralPath $packagedInstallers) {
    Copy-Folder $packagedInstallers $installersPath
    Write-Step "[OK] Installers copied to $installersPath"
}
Grant-Folder $installersPath $apiPool 'RX'

# 3. Frontend
Ensure-Site $webSite $webPool $webPath $webHost $thumb
Copy-Folder (Join-Path $PackageRoot 'web') $webPath -Mirror
Write-FrontendFiles $webPath "https://$apiHost/api"
& icacls $webPath /grant ('IIS AppPool\' + $webPool + ':(OI)(CI)RX') /Q | Out-Null
Write-Step '[OK] Frontend files copied'

# 4. Start and check
Ensure-HostsEntry $apiHost
Ensure-HostsEntry $webHost
foreach ($pool in $apiPool, $webPool) { Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue }
foreach ($site in $apiSite, $webSite) { Start-Website -Name $site -ErrorAction SilentlyContinue }

$apiStatus = Test-Url "https://$apiHost/api/clients"
$webStatus = Test-Url "https://$webHost/login/"
if ($apiStatus -eq 401 -or $apiStatus -eq 200) { Write-Step "[OK] API answers at https://$apiHost/api ($apiStatus)" }
else { Write-Step "WARNING: the API returned $apiStatus. Check $apiPath\logs and the Windows Application event log." }
if ($webStatus -eq 200) { Write-Step "[OK] Frontend answers at https://$webHost" }
else { Write-Step "WARNING: the frontend returned $webStatus." }

Write-Host ''
Write-Host 'DeployVare is installed.'
Write-Host "  App:  https://$webHost"
Write-Host "  API:  https://$apiHost/api"
Write-Host 'Sign in with your existing DeployVare account (the database was copied as it is).'
Write-Host "Other machines: add '<this server IP>  $webHost' and '<this server IP>  $apiHost' to their hosts file (or DNS),"
Write-Host "then open https://$apiHost once and accept the certificate before using the app."
exit 0
