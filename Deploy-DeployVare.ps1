# DeployVare One-Command Deployment Script
# Usage: .\Deploy-DeployVare.ps1
# Deploys latest DeployVare package to IIS (API + Frontend)

param(
    [string]$PackageVersion = (Get-ChildItem (Join-Path $PSScriptRoot "ClientPackage\output") -Filter "*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty BaseName),
    [string]$DeployPath = "C:\Deploy\manual-deploy"
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'ERROR: Administrator privileges are required.'
        exit 1
    }
}

function Deploy-Package {
    param([string]$Kit)

    Write-Host ""
    Write-Host "======== $Kit ========"
    $script = Join-Path $DeployPath "$Kit\Deploy.ps1"

    if (-not (Test-Path $script)) {
        Write-Host "ERROR: $script not found"
        exit 1
    }

    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) `
        -WorkingDirectory (Split-Path $script) -Wait -PassThru -NoNewWindow

    if ($p.ExitCode -ne 0) {
        Write-Host "FAILED: $Kit exited with code $($p.ExitCode)"
        exit $p.ExitCode
    }
}

# Main
Assert-Admin

Write-Host "DeployVare One-Command Deployment"
Write-Host "=================================="
Write-Host "Package: $PackageVersion"
Write-Host "Deploy Path: $DeployPath"

# Extract package
$zipPath = Join-Path (Join-Path $PSScriptRoot "ClientPackage\output") "$PackageVersion.zip"
if (-not (Test-Path $zipPath)) {
    Write-Host "ERROR: Package not found: $zipPath"
    exit 1
}

Write-Host ""
Write-Host "Extracting package..."
if (Test-Path $DeployPath) {
    Remove-Item $DeployPath -Recurse -Force
}
Expand-Archive -LiteralPath $zipPath -DestinationPath $DeployPath -Force
Write-Host "✓ Package extracted"

# Deploy
Deploy-Package 'api'
Deploy-Package 'frontend'

Write-Host ""
Write-Host "✓ Deployment completed successfully!"
Write-Host ""
Write-Host "Frontend: https://localhost"
Write-Host "API: https://localhost:44345/v1"
exit 0
