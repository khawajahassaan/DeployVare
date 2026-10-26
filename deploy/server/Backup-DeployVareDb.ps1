<#
  Run on the old Windows DeployVare server (PowerShell as Administrator):
    powershell -ExecutionPolicy Bypass -File .\Backup-DeployVareDb.ps1
  Backs up the database DeployVare's API is connected to (read from its appsettings), copies the .bak to
  C:\DeployVareBackup, and prints row counts plus a fingerprint of the encryption key (never the key).
#>
param(
    [string]$ApiPath = 'C:\inetpub\DeployVare\api',
    [string]$OutputFolder = 'C:\DeployVareBackup'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data

function Read-Json($path) { if (Test-Path $path) { Get-Content $path -Raw | ConvertFrom-Json } }

# The connection string: appsettings.Production.json wins over appsettings.json, as in the API.
$prod = Read-Json (Join-Path $ApiPath 'appsettings.Production.json')
$base = Read-Json (Join-Path $ApiPath 'appsettings.json')
if (-not $base -and -not $prod) { throw "No appsettings found in $ApiPath. Pass -ApiPath with the DeployVare API folder." }
$connStr = $prod.ConnectionStrings.DeployVareDb
if (-not $connStr) { $connStr = $base.ConnectionStrings.DeployVareDb }
if (-not $connStr) { throw 'ConnectionStrings:DeployVareDb is not set in the appsettings files.' }

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connStr
$database = $builder.InitialCatalog
Write-Host "SQL Server : $($builder.DataSource)"
Write-Host "Database   : $database"

function Invoke-Sql([string]$sql, [string]$db = 'master') {
    $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connStr
    # Indexer, not .InitialCatalog: PowerShell treats the builder as a dictionary and rejects the property.
    $b['Initial Catalog'] = $db
    $conn = New-Object System.Data.SqlClient.SqlConnection $b.ConnectionString
    $conn.Open()
    try {
        $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 0
        $table = New-Object System.Data.DataTable
        $table.Load($cmd.ExecuteReader())
        return ,$table
    } finally { $conn.Close() }
}

# What the backup will hold, so an empty database is noticed before it is copied anywhere.
$counts = Invoke-Sql @"
SELECT (SELECT COUNT(*) FROM Users) AS Users, (SELECT COUNT(*) FROM Clients) AS Clients,
       (SELECT COUNT(*) FROM Applications) AS Apps, (SELECT COUNT(*) FROM ApplicationServers) AS Servers,
       (SELECT COUNT(*) FROM ArtifactSources) AS Sources, (SELECT COUNT(*) FROM Deployments) AS Deployments
"@ $database
Write-Host 'Rows in the database:'
$counts | Format-Table -AutoSize | Out-String | Write-Host

# SQL Server writes the file itself, so it goes to its own backup folder first, then gets copied.
$sqlBackupDir = (Invoke-Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(4000)) AS P").Rows[0].P
if (-not $sqlBackupDir) { $sqlBackupDir = 'C:\Program Files\Microsoft SQL Server' }
$fileName = "DeployVare-$(Get-Date -Format 'yyyyMMdd-HHmm').bak"
$sqlBackupFile = Join-Path $sqlBackupDir $fileName

Write-Host "Backing up to $sqlBackupFile ..."
# COPY_ONLY leaves the server's own backup chain alone. Express has no compression, so it is retried without.
try {
    Invoke-Sql "BACKUP DATABASE [$database] TO DISK = N'$sqlBackupFile' WITH COPY_ONLY, INIT, COMPRESSION" | Out-Null
} catch {
    Invoke-Sql "BACKUP DATABASE [$database] TO DISK = N'$sqlBackupFile' WITH COPY_ONLY, INIT" | Out-Null
}

New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
$target = Join-Path $OutputFolder $fileName
Copy-Item $sqlBackupFile $target -Force
Write-Host "Backup ready: $target ($([math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"

# The saved credentials only decrypt with the same key; compare this fingerprint with the new server's.
$key = $prod.EncryptionSettings.EncryptionKey
if (-not $key) { $key = $base.EncryptionSettings.EncryptionKey }
if ($key) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($key))
    Write-Host ("Encryption key fingerprint: " + (($hash[0..5] | ForEach-Object { $_.ToString('x2') }) -join ''))
} else {
    Write-Host 'Encryption key: not found in appsettings.'
}
