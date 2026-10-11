<#
.SYNOPSIS
    Builds a DeployVare package that can be installed on a client's Windows server.

.DESCRIPTION
    Produces ClientPackage\output\DeployVare\ and a ZIP of it containing:
      api\          the API, published self-contained for win-x64 (no .NET runtime needed on the server)
      web\          the web app as static files, calling the API at /deployvare-api/api
      Installers\   server installers (only with -IncludeInstallers)
      Install-DeployVare.ps1, Uninstall-DeployVare.ps1, README.md, package.json

    Development secrets (DeployVare database connection string, JWT signing key, encryption key),
    appsettings.*.json files, logs, uploads and backups from this machine are never included.
    The installer generates new keys on the client server.

.EXAMPLE
    .\Build-ClientPackage.ps1

.EXAMPLE
    .\Build-ClientPackage.ps1 -IncludeInstallers
#>
[CmdletBinding()]
param(
    # Defaults to ClientPackage\output
    [string]$OutputDirectory,
    [switch]$IncludeInstallers
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not set $PSScriptRoot while evaluating parameter defaults
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'output' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $OutputDirectory 'DeployVare'
$apiOut = Join-Path $packageRoot 'api'
$webOut = Join-Path $packageRoot 'web'

# The installer hosts the API as the /deployvare-api application inside the DeployVare IIS site,
# so the web app calls it on the same origin and the port is chosen at install time.
$apiUrl = '/deployvare-api/api'

function Write-Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }

foreach ($tool in 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH." }
}

if (Test-Path -LiteralPath $packageRoot) {
    Write-Step "Removing the previous package at $packageRoot"
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

# ---------------------------------------------------------------------------------------------
# 1. API
# ---------------------------------------------------------------------------------------------
Write-Step 'Publishing the API (self-contained, win-x64)'
& dotnet publish (Join-Path $repoRoot 'API\API.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o $apiOut --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (-not (Test-Path -LiteralPath (Join-Path $apiOut 'API.exe'))) { throw 'API.exe was not produced by dotnet publish.' }
if (-not (Test-Path -LiteralPath (Join-Path $apiOut 'web.config'))) { throw 'web.config was not produced by dotnet publish.' }

Write-Step 'Removing development settings, logs and local data from the API'
Get-ChildItem -LiteralPath $apiOut -Filter 'appsettings.*.json' -File | Remove-Item -Force
foreach ($folder in 'publish', 'publish-win', 'logs', 'data', 'wwwroot\uploads', 'wwwroot\backups') {
    $path = Join-Path $apiOut $folder
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$sourceSettings = Get-Content -LiteralPath (Join-Path $repoRoot 'API\appsettings.json') -Raw | ConvertFrom-Json
$settingsPath = Join-Path $apiOut 'appsettings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json

# The installer supplies these as environment variables; leaving them out makes a misconfigured
# install fail at startup instead of silently using the development values.
$settings.ConnectionStrings.DeployVareDb = ''
$settings.JwtSettings.PSObject.Properties.Remove('SecretKey')
$settings.EncryptionSettings.PSObject.Properties.Remove('EncryptionKey')
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------------------------
# 2. Web app (static export)
# ---------------------------------------------------------------------------------------------
Write-Step 'Building the web app as static files'
$webSource = Join-Path $repoRoot 'web'
$previousTauriBuild = $env:TAURI_BUILD
$previousApiUrl = $env:NEXT_PUBLIC_API_URL

Push-Location $webSource
try {
    $env:TAURI_BUILD = '1'              # next.config.ts: output 'export' (static files, no Node server)
    $env:NEXT_PUBLIC_API_URL = $apiUrl  # process environment takes precedence over web\.env.local
    # Route npm's stderr through cmd so Windows PowerShell 5.1 does not turn Next.js's harmless
    # stderr warnings into a terminating NativeCommandError under $ErrorActionPreference = 'Stop'.
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & cmd /c "npm run build 2>&1"
    $buildExit = $LASTEXITCODE
    $ErrorActionPreference = $eap
    if ($buildExit -ne 0) { throw "npm run build failed with exit code $buildExit" }
}
finally {
    Pop-Location
    $env:TAURI_BUILD = $previousTauriBuild
    $env:NEXT_PUBLIC_API_URL = $previousApiUrl
}

$exportDir = Join-Path $webSource 'out'
if (-not (Test-Path -LiteralPath (Join-Path $exportDir 'index.html'))) { throw 'The static export (web\out\index.html) was not produced.' }
Copy-Item -LiteralPath $exportDir -Destination $webOut -Recurse

# Count matches (Select-String -Quiet returns one result per file in Windows PowerShell 5.1)
$scripts = Get-ChildItem -LiteralPath (Join-Path $webOut '_next') -Recurse -Filter '*.js' -File
if (@($scripts | Select-String -SimpleMatch -Pattern $apiUrl -List).Count -eq 0) {
    throw "The web build does not use the API URL $apiUrl."
}
if (@($scripts | Select-String -SimpleMatch -Pattern 'localhost:5233' -List).Count -gt 0) {
    throw 'The web build still points at the development API (localhost:5233).'
}

$notFoundPage = if (Test-Path -LiteralPath (Join-Path $webOut '404.html')) {
@'
      <httpErrors errorMode="Custom" existingResponse="Replace">
        <remove statusCode="404" />
        <error statusCode="404" path="/404.html" responseMode="ExecuteURL" />
      </httpErrors>
'@
} else { '' }

$webConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <defaultDocument enabled="true">
        <files>
          <clear />
          <add value="index.html" />
        </files>
      </defaultDocument>
$notFoundPage    </system.webServer>
  </location>
</configuration>
"@
[IO.File]::WriteAllText((Join-Path $webOut 'web.config'), $webConfig, (New-Object Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------------------------
# 3. Installers, scripts, manifest
# ---------------------------------------------------------------------------------------------
if ($IncludeInstallers) {
    $installersSource = Join-Path $repoRoot 'Installers'
    if (Test-Path -LiteralPath $installersSource) {
        Write-Step 'Adding server installers'
        Copy-Item -LiteralPath $installersSource -Destination (Join-Path $packageRoot 'Installers') -Recurse
    } else {
        Write-Warning "No Installers folder found at $installersSource"
    }
}

foreach ($file in 'Install-DeployVare.ps1', 'Uninstall-DeployVare.ps1', 'README.md') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $packageRoot
}

$commit = try { (& git -C $repoRoot rev-parse --short HEAD 2>$null) } catch { $null }
$manifest = [ordered]@{
    product   = 'DeployVare'
    builtAt   = (Get-Date).ToString('o')
    commit    = $commit
    apiPath   = '/deployvare-api'
    runtime   = 'self-contained win-x64'
    installers = [bool]$IncludeInstallers
}
[IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

# ---------------------------------------------------------------------------------------------
# 4. Secret check and ZIP
# ---------------------------------------------------------------------------------------------
Write-Step 'Checking that no development secrets are in the package'
$secrets = @(
    $sourceSettings.ConnectionStrings.DeployVareDb,
    $sourceSettings.JwtSettings.SecretKey,
    $sourceSettings.EncryptionSettings.EncryptionKey
) | Where-Object { $_ -and $_.Length -ge 8 }

$leaks = @()
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Include '*.json', '*.config', '*.js', '*.html', '*.txt', '*.md') {
    $content = [IO.File]::ReadAllText($file.FullName)
    foreach ($secret in $secrets) {
        if ($content.Contains($secret)) { $leaks += $file.FullName; break }
    }
}
if ($leaks.Count -gt 0) { throw "Development secrets found in: $($leaks -join ', ')" }

Write-Step 'Creating the ZIP'
$zipPath = Join-Path $OutputDirectory ('DeployVare-ClientPackage-{0:yyyyMMdd-HHmm}.zip' -f (Get-Date))
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)

$sizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Write-Host ''
Write-Host "Package ready: $zipPath ($sizeMb MB)" -ForegroundColor Green
Write-Host 'Copy it to the client server, extract it, and run Install-DeployVare.ps1 as Administrator.'
