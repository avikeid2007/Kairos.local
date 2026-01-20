<#
.SYNOPSIS
    Builds, packages, signs, and installs the KaiROS.AI MSIX package.

.DESCRIPTION
    This script automates the complete MSIX packaging workflow:
    1. Publishes the .NET application
    2. Creates the MSIX package
    3. Signs the package with a certificate
    4. Imports the certificate and installs the package

.PARAMETER Version
    The version number for the MSIX package (e.g., 1.0.7)

.EXAMPLE
    .\Build-MSIX.ps1 -Version 1.0.8
#>

param(
    [Parameter(Mandatory = $true, HelpMessage = "Version number for the MSIX package (e.g., 1.0.7)")]
    [string]$Version
)

# Get the script's directory (KaiROS.AI folder)
$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptDir

# Define paths
$PublishDir = Join-Path $ScriptDir "msix-publish"
$ManifestSource = Join-Path $ScriptDir "Package.appxmanifest"
$ManifestDest = Join-Path $PublishDir "AppxManifest.xml"
$MsixFileName = "KaiROS.AI-$Version.msix"
$MsixPath = Join-Path $ProjectRoot $MsixFileName
$PfxPath = Join-Path $ProjectRoot "KaiROS.pfx"
$CerPath = Join-Path $ProjectRoot "KaiROS.cer"
$PfxPassword = "KaiROS123"

# Windows SDK paths
$WindowsKitPath = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$MakeAppx = Join-Path $WindowsKitPath "makeappx.exe"
$SignTool = Join-Path $WindowsKitPath "signtool.exe"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  KaiROS.AI MSIX Build Script v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Validate Windows SDK tools exist
if (-not (Test-Path $MakeAppx)) {
    Write-Error "MakeAppx.exe not found at: $MakeAppx"
    Write-Host "Please ensure Windows SDK is installed." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $SignTool)) {
    Write-Error "SignTool.exe not found at: $SignTool"
    Write-Host "Please ensure Windows SDK is installed." -ForegroundColor Yellow
    exit 1
}

# Validate certificate files exist
if (-not (Test-Path $PfxPath)) {
    Write-Error "Certificate file not found at: $PfxPath"
    exit 1
}

if (-not (Test-Path $CerPath)) {
    Write-Error "Certificate file not found at: $CerPath"
    exit 1
}

Write-Host "[1/6] Publishing .NET application..." -ForegroundColor Green
Push-Location $ScriptDir
try {
    dotnet publish -c Release -r win-x64 --self-contained true -o $PublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
        exit 1
    }
}
finally {
    Pop-Location
}

Write-Host "[2/6] Copying Package.appxmanifest..." -ForegroundColor Green
if (-not (Test-Path $ManifestSource)) {
    Write-Error "Package.appxmanifest not found at: $ManifestSource"
    exit 1
}
Copy-Item $ManifestSource -Destination $ManifestDest -Force
Write-Host "  Copied to: $ManifestDest" -ForegroundColor Gray

Write-Host "[3/6] Creating MSIX package..." -ForegroundColor Green
Write-Host "  Output: $MsixPath" -ForegroundColor Gray
& $MakeAppx pack /d $PublishDir /p $MsixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "MakeAppx failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "[4/6] Signing MSIX package..." -ForegroundColor Green
& $SignTool sign /fd SHA256 /f $PfxPath /p $PfxPassword /t http://timestamp.digicert.com $MsixPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "SignTool failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "[5/6] Importing certificate to TrustedPeople store..." -ForegroundColor Green
Write-Host "  (Requires Administrator privileges)" -ForegroundColor Yellow
try {
    Import-Certificate -FilePath $CerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -ErrorAction Stop
    Write-Host "  Certificate imported successfully." -ForegroundColor Gray
}
catch {
    Write-Warning "Failed to import certificate. You may need to run this script as Administrator."
    Write-Warning "Error: $_"
}

Write-Host "[6/6] Installing MSIX package..." -ForegroundColor Green
try {
    Add-AppxPackage -Path $MsixPath -ErrorAction Stop
    Write-Host "  Package installed successfully." -ForegroundColor Gray
}
catch {
    Write-Warning "Failed to install package."
    Write-Warning "Error: $_"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "  MSIX Package: $MsixPath" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
