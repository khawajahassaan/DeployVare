#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes DeployVare from this server: the IIS site, its app pools and the installed files.

.DESCRIPTION
    Data (-DataRoot) and the database are kept unless -RemoveData / -DropDatabase are given.

        powershell -ExecutionPolicy Bypass -File .\Uninstall-DeployVare.ps1
        powershell -ExecutionPolicy Bypass -File .\Uninstall-DeployVare.ps1 -RemoveData -DropDatabase
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'DeployVare'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'DeployVare'),
    [string]$SiteName = 'DeployVare',
    [string]$SqlServer = '.',
    [string]$DatabaseName = 'DeployVare',
    [switch]$RemoveData,
    [switch]$DropDatabase
)

$ErrorActionPreference = 'Stop'
$webPool = 'DeployVare'
$apiPool = 'DeployVareApi'
$apiIdentity = "IIS APPPOOL\$apiPool"

Write-Host 'This removes DeployVare from this server:' -ForegroundColor Yellow
Write-Host "  IIS site '$SiteName', app pools '$webPool' and '$apiPool', and $InstallRoot"
if ($RemoveData) { Write-Host "  Data folder $DataRoot" }
if ($DropDatabase) { Write-Host "  Database '$DatabaseName' on '$SqlServer' and the login '$apiIdentity'" }
if ((Read-Host 'Type DeployVare to continue') -cne 'DeployVare') { Write-Host 'Cancelled.'; return }

Import-Module WebAdministration

if (Get-Website -Name $SiteName) { Remove-Website -Name $SiteName }
foreach ($pool in $webPool, $apiPool) {
    if (Test-Path "IIS:\AppPools\$pool") {
        if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool; Start-Sleep -Seconds 3 }
        Remove-WebAppPool -Name $pool
    }
}

# Only delete folders that look like a DeployVare installation
if (Test-Path -LiteralPath (Join-Path $InstallRoot 'api\API.exe')) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
} elseif (Test-Path -LiteralPath $InstallRoot) {
    Write-Warning "$InstallRoot does not contain api\API.exe; not deleting it."
}

if ($RemoveData) {
    if (Test-Path -LiteralPath (Join-Path $DataRoot 'data')) {
        Remove-Item -LiteralPath $DataRoot -Recurse -Force
    } elseif (Test-Path -LiteralPath $DataRoot) {
        Write-Warning "$DataRoot does not contain a data folder; not deleting it."
    }
}

if ($DropDatabase) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $SqlServer
    $builder['Initial Catalog'] = 'master'
    $builder['Integrated Security'] = $true
    $builder['TrustServerCertificate'] = $true

    $dropSql = @'
DECLARE @sql nvarchar(max);
IF DB_ID(@database) IS NOT NULL
BEGIN
    SET @sql = N'ALTER DATABASE ' + QUOTENAME(@database) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@database);
    EXEC sp_executesql @sql;
END
IF SUSER_ID(@login) IS NOT NULL
BEGIN
    SET @sql = N'DROP LOGIN ' + QUOTENAME(@login);
    EXEC sp_executesql @sql;
END
'@
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $dropSql
        [void]$command.Parameters.AddWithValue('@database', $DatabaseName)
        [void]$command.Parameters.AddWithValue('@login', $apiIdentity)
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

Write-Host 'DeployVare was removed.' -ForegroundColor Green
