#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or upgrades DeployVare on this Windows server (IIS + SQL Server). DeployVare is reachable only
    from this server.

.DESCRIPTION
    Run from the extracted package folder in an elevated Windows PowerShell:

        powershell -ExecutionPolicy Bypass -File .\Install-DeployVare.ps1

    What it does:
      - Copies the API and web app to -InstallRoot; uploads, backups and working data go to -DataRoot
      - Creates the IIS site "DeployVare" bound to 127.0.0.1 and [::1] only, with the API as /deployvare-api
      - Creates the database with Windows authentication: the API's app pool identity becomes db_owner,
        so no database password is stored (use -UseSqlLogin for a SQL Server login instead)
      - Generates a new encryption key and JWT signing key, stored only in the API's web.config, which is
        readable by Administrators, SYSTEM and the API app pool
      - Asks for a new password for the default "admin" user

    Running it again upgrades the files and keeps the keys, database connection and data.

.PARAMETER Port
    Local port for DeployVare (http://127.0.0.1:<Port>).

.PARAMETER SqlServer
    SQL Server instance on this server, e.g. "." or ".\SQLEXPRESS". Windows authentication only works for a
    SQL Server on this machine; for a remote SQL Server use -UseSqlLogin.

.PARAMETER UseSqlLogin
    Ask for a full connection string (SQL Server login) instead of using Windows authentication.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 8090,
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'DeployVare'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'DeployVare'),
    [string]$SqlServer = '.',
    [string]$DatabaseName = 'DeployVare',
    [switch]$UseSqlLogin,
    [string]$SiteName = 'DeployVare'
)

$ErrorActionPreference = 'Stop'

$packageRoot = $PSScriptRoot
$apiSource = Join-Path $packageRoot 'api'
$webSource = Join-Path $packageRoot 'web'
$installersSource = Join-Path $packageRoot 'Installers'

$apiTarget = Join-Path $InstallRoot 'api'
$webTarget = Join-Path $InstallRoot 'web'
$installersTarget = Join-Path $InstallRoot 'Installers'
$apiLogs = Join-Path $apiTarget 'logs'
$dataDir = Join-Path $DataRoot 'data'
$apiWebConfig = Join-Path $apiTarget 'web.config'

$webPool = 'DeployVare'
$apiPool = 'DeployVareApi'
$apiAppName = 'deployvare-api'
$apiIdentity = "IIS APPPOOL\$apiPool"
$webIdentity = "IIS APPPOOL\$webPool"

$baseUrl = "http://127.0.0.1:$Port"
$apiBaseUrl = "$baseUrl/$apiAppName/api"
$defaultAdminPassword = 'Admin@123'

function Write-Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Write-Done([string]$message) { Write-Host "    $message" -ForegroundColor Green }

function New-Secret([int]$byteCount) {
    $bytes = New-Object byte[] $byteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function ConvertTo-PlainText([Security.SecureString]$secure) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Invoke-Robocopy([string]$source, [string]$target, [string[]]$options) {
    & robocopy $source $target @options /R:2 /W:2 /NP /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $source to $target failed (robocopy exit code $LASTEXITCODE)." }
}

# Administrators and SYSTEM get full control, plus the given grants; nothing is inherited from the parent
function Set-FolderAccess([string]$path, [string[]]$grants) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $arguments = @($path, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '/grant:r', '*S-1-5-18:(OI)(CI)F')
    foreach ($grant in $grants) { $arguments += @('/grant:r', $grant) }
    & icacls @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Setting permissions on $path failed (icacls exit code $LASTEXITCODE)." }
}

function Get-InstalledEnvironment([string]$webConfigPath) {
    $values = @{}
    if (Test-Path -LiteralPath $webConfigPath) {
        [xml]$xml = Get-Content -LiteralPath $webConfigPath -Raw
        foreach ($node in $xml.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')) {
            $values[$node.GetAttribute('name')] = $node.GetAttribute('value')
        }
    }
    return $values
}

function Set-ApiEnvironment([string]$webConfigPath, [System.Collections.Specialized.OrderedDictionary]$variables) {
    [xml]$xml = Get-Content -LiteralPath $webConfigPath -Raw
    $aspNetCore = $xml.SelectSingleNode('//aspNetCore')
    if (-not $aspNetCore) { throw "No aspNetCore element found in $webConfigPath" }

    $existingNode = $aspNetCore.SelectSingleNode('environmentVariables')
    if ($existingNode) { [void]$aspNetCore.RemoveChild($existingNode) }

    $container = $xml.CreateElement('environmentVariables')
    foreach ($name in $variables.Keys) {
        $element = $xml.CreateElement('environmentVariable')
        $element.SetAttribute('name', $name)
        $element.SetAttribute('value', [string]$variables[$name])
        [void]$container.AppendChild($element)
    }
    [void]$aspNetCore.AppendChild($container)
    $aspNetCore.SetAttribute('hostingModel', 'inprocess')
    $xml.Save($webConfigPath)
}

function Invoke-Sql([string]$connectionString, [string]$query, [hashtable]$parameters = @{}) {
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $query
        $command.CommandTimeout = 120
        foreach ($key in $parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $parameters[$key]) }
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Invoke-Api([string]$method, [string]$path, $body = $null, [string]$token = $null) {
    $request = @{ Method = $method; Uri = "$apiBaseUrl$path"; UseBasicParsing = $true; TimeoutSec = 60; Headers = @{} }
    if ($token) { $request.Headers.Authorization = "Bearer $token" }
    if ($null -ne $body) {
        $request.ContentType = 'application/json'
        $request.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Compress))
    }
    return Invoke-RestMethod @request
}

function Get-BindingPort([string]$bindingInformation) {
    if ($bindingInformation -match ':(\d+):[^:\]]*$') { return [int]$Matches[1] }
    return $null
}

# -------------------------------------------------------------------------------------------------
# Prerequisites
# -------------------------------------------------------------------------------------------------
Write-Step 'Checking prerequisites'

foreach ($required in 'api\API.exe', 'api\web.config', 'web\index.html') {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $required))) {
        throw "Package file missing: $required. Run this script from the extracted package folder."
    }
}

if (-not (Get-Service -Name W3SVC -ErrorAction SilentlyContinue)) { throw 'IIS is not installed on this server.' }
Import-Module WebAdministration

if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    throw 'The ASP.NET Core Module V2 is not installed. Install the .NET Hosting Bundle (version 6 or later), then run this script again.'
}
if (-not (Get-WebGlobalModule -Name StaticFileModule -ErrorAction SilentlyContinue)) {
    throw 'IIS Static Content is not installed (Web Server > Common HTTP Features > Static Content).'
}

$conflictingSites = @(Get-Website | Where-Object { $_.Name -ne $SiteName } | Where-Object {
    @($_.bindings.Collection | Where-Object { (Get-BindingPort $_.bindingInformation) -eq $Port }).Count -gt 0
})
if ($conflictingSites.Count -gt 0) {
    throw "Port $Port is already used by IIS site(s): $(($conflictingSites | ForEach-Object Name) -join ', '). Run again with -Port <free port>."
}

# http.sys (IIS) listens as the System process (PID 4); anything else on the port is another program
$otherListeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -ne 4 })
if ($otherListeners.Count -gt 0) {
    $processNames = ($otherListeners | ForEach-Object { (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName } | Sort-Object -Unique) -join ', '
    throw "Port $Port is already used by: $processNames. Run again with -Port <free port>."
}

$installed = Get-InstalledEnvironment $apiWebConfig
$isUpgrade = [bool]$installed['EncryptionSettings__EncryptionKey']
Write-Done $(if ($isUpgrade) { "Existing installation found in $InstallRoot - upgrading (keys and database connection are kept)" } else { 'New installation' })

# -------------------------------------------------------------------------------------------------
# Files
# -------------------------------------------------------------------------------------------------
foreach ($pool in $webPool, $apiPool) {
    if ((Test-Path "IIS:\AppPools\$pool") -and (Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') {
        Write-Step "Stopping app pool $pool"
        Stop-WebAppPool -Name $pool
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped' -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }
        Start-Sleep -Seconds 2
    }
}

Write-Step "Copying files to $InstallRoot"
New-Item -ItemType Directory -Path $apiTarget, $webTarget, $dataDir -Force | Out-Null
Invoke-Robocopy $apiSource $apiTarget @('/MIR', '/XD', 'logs')
Invoke-Robocopy $webSource $webTarget @('/MIR')
if (Test-Path -LiteralPath $installersSource) { Invoke-Robocopy $installersSource $installersTarget @('/E') }
New-Item -ItemType Directory -Path $apiLogs -Force | Out-Null
Write-Done 'Files copied'

# -------------------------------------------------------------------------------------------------
# IIS
# -------------------------------------------------------------------------------------------------
Write-Step 'Configuring IIS'
foreach ($pool in $webPool, $apiPool) {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
}
# Deployments run inside API requests; keep the API loaded and give it a user profile for temp files
Set-ItemProperty "IIS:\AppPools\$apiPool" -Name processModel.loadUserProfile -Value $true
Set-ItemProperty "IIS:\AppPools\$apiPool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$apiPool" -Name startMode -Value 'AlwaysRunning'

if (-not (Get-Website -Name $SiteName)) {
    New-Website -Name $SiteName -PhysicalPath $webTarget -ApplicationPool $webPool -IPAddress '127.0.0.1' -Port $Port | Out-Null
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $webTarget
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $webPool
# Reachable only from this server
Set-ItemProperty "IIS:\Sites\$SiteName" -Name bindings -Value @(
    @{ protocol = 'http'; bindingInformation = "127.0.0.1:${Port}:" },
    @{ protocol = 'http'; bindingInformation = "[::1]:${Port}:" }
)

if (-not (Get-WebApplication -Site $SiteName -Name $apiAppName)) {
    New-WebApplication -Site $SiteName -Name $apiAppName -PhysicalPath $apiTarget -ApplicationPool $apiPool | Out-Null
}
Set-ItemProperty "IIS:\Sites\$SiteName\$apiAppName" -Name physicalPath -Value $apiTarget
Set-ItemProperty "IIS:\Sites\$SiteName\$apiAppName" -Name applicationPool -Value $apiPool
Set-ItemProperty "IIS:\Sites\$SiteName\$apiAppName" -Name preloadEnabled -Value $true

# Anonymous requests run as the app pool identity, so only the pool accounts need file access
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName `
    -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name userName -Value ''
Write-Done "Site $SiteName on 127.0.0.1:$Port with the API at /$apiAppName"

# -------------------------------------------------------------------------------------------------
# Database
# -------------------------------------------------------------------------------------------------
Write-Step 'Configuring the database'
$connectionString = $installed['DB_CONNECTION_STRING']

if ($UseSqlLogin) {
    $secureConnection = Read-Host 'Connection string for the DeployVare database (input hidden)' -AsSecureString
    $connectionString = ConvertTo-PlainText $secureConnection
    try { Invoke-Sql $connectionString 'SELECT 1' }
    catch { throw "Could not connect with the given connection string: $($_.Exception.InnerException.Message) $($_.Exception.Message)" }
    Write-Done 'Connection string verified'
}
elseif ($connectionString) {
    Write-Done 'Keeping the existing database connection'
}
else {
    $master = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $master['Data Source'] = $SqlServer
    $master['Initial Catalog'] = 'master'
    $master['Integrated Security'] = $true
    $master['TrustServerCertificate'] = $true

    # Creates the database, a Windows login for the API app pool and makes it db_owner (idempotent)
    $setupSql = @'
DECLARE @quotedDatabase nvarchar(300) = QUOTENAME(@database);
DECLARE @quotedLogin nvarchar(300) = QUOTENAME(@login);
DECLARE @sql nvarchar(max);

IF DB_ID(@database) IS NULL
BEGIN
    SET @sql = N'CREATE DATABASE ' + @quotedDatabase;
    EXEC sp_executesql @sql;
END

IF SUSER_ID(@login) IS NULL
BEGIN
    SET @sql = N'CREATE LOGIN ' + @quotedLogin + N' FROM WINDOWS';
    EXEC sp_executesql @sql;
END

SET @sql = N'USE ' + @quotedDatabase + N';
IF USER_ID(@login) IS NULL
BEGIN
    DECLARE @createUser nvarchar(max) = N''CREATE USER '' + QUOTENAME(@login) + N'' FOR LOGIN '' + QUOTENAME(@login);
    EXEC sp_executesql @createUser;
END
IF ISNULL(IS_ROLEMEMBER(N''db_owner'', @login), 0) = 0
BEGIN
    DECLARE @addRole nvarchar(max) = N''ALTER ROLE db_owner ADD MEMBER '' + QUOTENAME(@login);
    EXEC sp_executesql @addRole;
END';
EXEC sp_executesql @sql, N'@login sysname', @login = @login;
'@
    try {
        Invoke-Sql $master.ConnectionString $setupSql @{ '@database' = $DatabaseName; '@login' = $apiIdentity }
    }
    catch {
        throw "Database setup on '$SqlServer' failed: $($_.Exception.InnerException.Message) $($_.Exception.Message) " +
            'Run this script as a Windows user with sysadmin rights on SQL Server, or use -UseSqlLogin.'
    }

    $app = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $app['Data Source'] = $SqlServer
    $app['Initial Catalog'] = $DatabaseName
    $app['Integrated Security'] = $true
    $app['TrustServerCertificate'] = $true
    $app['MultipleActiveResultSets'] = $true
    $connectionString = $app.ConnectionString
    Write-Done "Database $DatabaseName on $SqlServer, with $apiIdentity as db_owner (Windows authentication)"
}

# -------------------------------------------------------------------------------------------------
# Keys and API configuration
# -------------------------------------------------------------------------------------------------
Write-Step 'Configuring keys'
$encryptionKey = $installed['EncryptionSettings__EncryptionKey']
if (-not $encryptionKey) { $encryptionKey = New-Secret 24 }   # 32 characters (the API uses 32)
$jwtKey = $installed['JwtSettings__SecretKey']
if (-not $jwtKey) { $jwtKey = New-Secret 48 }                 # 64 characters

Set-ApiEnvironment $apiWebConfig ([ordered]@{
    ASPNETCORE_ENVIRONMENT            = 'Production'
    ASPNETCORE_CONTENTROOT            = $apiTarget
    DEPLOYVARE_DATA_DIR               = $dataDir
    DB_CONNECTION_STRING              = $connectionString
    JwtSettings__SecretKey            = $jwtKey
    EncryptionSettings__EncryptionKey = $encryptionKey
})
Write-Done $(if ($isUpgrade) { 'Existing keys kept' } else { 'New encryption key and JWT signing key generated' })

Write-Step 'Restricting file permissions'
Set-FolderAccess $apiTarget @("${apiIdentity}:(OI)(CI)RX")
Set-FolderAccess $apiLogs @("${apiIdentity}:(OI)(CI)M")
# IIS reads the site root's web.config for the /deployvare-api application too, so both pools need read access
Set-FolderAccess $webTarget @("${webIdentity}:(OI)(CI)RX", "${apiIdentity}:(OI)(CI)RX")
Set-FolderAccess $DataRoot @("${apiIdentity}:(OI)(CI)M")
if (Test-Path -LiteralPath $installersTarget) {
    & icacls $installersTarget /grant "${apiIdentity}:(OI)(CI)RX" | Out-Null
}
Write-Done 'API folder (with keys) readable only by Administrators, SYSTEM and the API app pool'

# -------------------------------------------------------------------------------------------------
# Start and verify
# -------------------------------------------------------------------------------------------------
Write-Step 'Starting DeployVare'
if ((Get-Service W3SVC).Status -ne 'Running') { Start-Service W3SVC }
foreach ($pool in $webPool, $apiPool) {
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool }
}
if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }

$adminToken = $null
$apiReady = $false
$lastError = $null
$lastBody = $null
$startedAt = Get-Date
$deadline = $startedAt.AddMinutes(3)
while ((Get-Date) -lt $deadline) {
    try {
        $login = Invoke-Api 'POST' '/auth/login' @{ username = 'admin'; password = $defaultAdminPassword }
        $adminToken = $login.token
        $apiReady = $true
        break
    }
    catch {
        $status = $null
        $lastBody = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            try {
                $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
                $lastBody = $reader.ReadToEnd()
                $reader.Dispose()
            }
            catch { }
        }
        if ($status -eq 401) { $apiReady = $true; break }   # admin password was already changed
        $lastError = $_.Exception.Message
        # IIS configuration errors and ASP.NET Core start-up failures do not go away by waiting
        if ($lastBody -match 'HTTP Error 500\.(19|3\d)|ANCM') { break }
        Start-Sleep -Seconds 5
    }
}
if (-not $apiReady) {
    Write-Host ''
    Write-Warning "DeployVare did not start: $lastError"
    if ($lastBody) {
        $text = ($lastBody -replace '(?is)<(script|style)[^>]*>.*?</\1>', ' ' -replace '<[^>]+>', ' ' -replace '&nbsp;', ' ' -replace '\s+', ' ').Trim()
        Write-Host "Server response: $($text.Substring(0, [Math]::Min(1500, $text.Length)))"
    }
    $latestLog = Get-ChildItem -LiteralPath $apiLogs -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($latestLog) {
        Write-Host "Last lines of $($latestLog.FullName):"
        Get-Content -LiteralPath $latestLog.FullName -Tail 25 | ForEach-Object { Write-Host "  $_" }
    }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $startedAt } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'AspNetCore|IIS' } | Select-Object -First 5 |
        ForEach-Object { Write-Host "Event $($_.Id) ($($_.ProviderName)): $($_.Message)" }
    throw 'DeployVare did not start. See the details above.'
}
Write-Done "DeployVare is running at $baseUrl"

if ($adminToken) {
    Write-Step 'Replacing the default admin password'
    do {
        $password = ConvertTo-PlainText (Read-Host 'New password for user "admin" (at least 10 characters)' -AsSecureString)
        $confirm = ConvertTo-PlainText (Read-Host 'Repeat the password' -AsSecureString)
        $valid = $password.Length -ge 10 -and $password -ceq $confirm -and $password -ne $defaultAdminPassword
        if (-not $valid) { Write-Warning 'The passwords must match, be at least 10 characters long and differ from the default.' }
    } until ($valid)

    $admin = @(Invoke-Api 'GET' '/users' $null $adminToken) | Where-Object { $_.username -eq 'admin' } | Select-Object -First 1
    if (-not $admin) { throw 'Could not find the admin user to change its password.' }
    Invoke-Api 'PUT' "/users/$($admin.id)" @{ newPassword = $password } $adminToken | Out-Null
    $password = $null
    $confirm = $null
    Write-Done 'Admin password changed'
}

Write-Host ''
Write-Host 'DeployVare is installed.' -ForegroundColor Green
Write-Host "  Open:      $baseUrl  (from this server only)"
Write-Host '  Sign in:   admin'
Write-Host "  Files:     $InstallRoot"
Write-Host "  Data:      $DataRoot"
Write-Host "  Logs:      $apiLogs"
Write-Host ''
Write-Host 'Next steps in DeployVare:'
Write-Host '  1. Client Onboarding: create the client'
Write-Host '  2. Azure DevOps: add the connection (read-only PAT) and artifact sources'
Write-Host '  3. Servers: add this server with Host "localhost", SSH Port 22 and a local administrator account'
