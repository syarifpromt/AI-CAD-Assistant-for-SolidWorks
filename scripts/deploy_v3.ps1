<#
.SYNOPSIS
    Builds, tests, validates, and registers AI CAD Assistant v3.0.2 for SOLIDWORKS.
    Run this script when SOLIDWORKS is closed so that AddIn.dll can be updated.
#>
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$solutionPath = Join-Path $rootDir "AICADAssistant.sln"
$msBuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

$coreDll = Join-Path $rootDir "src\AICADAssistant.Core\bin\$Configuration\AICADAssistant.Core.dll"
$addinDll = Join-Path $rootDir "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$testsExe = Join-Path $rootDir "src\AICADAssistant.Tests\bin\$Configuration\AICADAssistant.Tests.exe"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  AI CAD Assistant v3.0.2 — Deployment & Build Script" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 0. Pastikan SOLIDWORKS tertutup sebelum build dan registrasi
$swProcesses = Get-Process -Name "SLDWORKS" -ErrorAction SilentlyContinue
if ($swProcesses) {
    Write-Warning "SOLIDWORKS sedang berjalan (PID: $($swProcesses.Id -join ', '))."
    Write-Warning "Mohon tutup SOLIDWORKS terlebih dahulu sebelum menjalankan deployment."
    exit 1
}

# 1. Periksa keberadaan MSBuild Visual Studio Build Tools
if (-not (Test-Path $msBuildPath)) {
    Write-Error "MSBuild Visual Studio Build Tools tidak ditemukan di: $msBuildPath"
    exit 1
}

Write-Host "`n1. Membangun Solusi (Target: Rebuild | $Configuration | $Platform)..." -ForegroundColor Yellow
& $msBuildPath $solutionPath /t:Rebuild /p:Configuration=$Configuration /p:Platform=$Platform /v:m

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build gagal dengan exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
Write-Host "[OK] Solusi berhasil dibangun!" -ForegroundColor Green

# 2. Verifikasi ketersediaan ketiga file output build
Write-Host "`n2. Memverifikasi File Output Build..." -ForegroundColor Yellow
$requiredFiles = @(
    @{ Name = "AICADAssistant.Core.dll"; Path = $coreDll },
    @{ Name = "AICADAssistant.AddIn.dll"; Path = $addinDll },
    @{ Name = "AICADAssistant.Tests.exe"; Path = $testsExe }
)

foreach ($f in $requiredFiles) {
    if (-not (Test-Path $f.Path)) {
        Write-Error "File output wajib tidak ditemukan: $($f.Name) di $($f.Path). Deployment dihentikan."
        exit 1
    }
    Write-Host "  [OK] $($f.Name) tersedia." -ForegroundColor Green
}

# 3. Validasi versi aktual via AssemblyName
Write-Host "`n3. Memvalidasi AssemblyVersion..." -ForegroundColor Yellow
$coreVer = [System.Reflection.AssemblyName]::GetAssemblyName($coreDll).Version.ToString()
$addinVer = [System.Reflection.AssemblyName]::GetAssemblyName($addinDll).Version.ToString()

Write-Host "  - Core AssemblyVersion:  $coreVer" -ForegroundColor Cyan
Write-Host "  - AddIn AssemblyVersion: $addinVer" -ForegroundColor Cyan

if ($coreVer -ne "3.0.2.0") {
    Write-Error "Core AssemblyVersion ($coreVer) tidak sesuai target (3.0.2.0)!"
    exit 1
}
if ($addinVer -ne "3.0.2.0") {
    Write-Error "AddIn AssemblyVersion ($addinVer) tidak sesuai target (3.0.2.0)!"
    exit 1
}
Write-Host "  [OK] Seluruh assembly memenuhi target versi v3.0.2.0!" -ForegroundColor Green

# 4. Menjalankan Suite Unit Test
Write-Host "`n4. Menjalankan Suite Pengujian Otomatis..." -ForegroundColor Yellow
& $testsExe
if ($LASTEXITCODE -ne 0) {
    Write-Error "Unit test gagal dengan exit code $LASTEXITCODE. Deployment dihentikan."
    exit $LASTEXITCODE
}
Write-Host "[OK] Seluruh unit test lolos tanpa kegagalan!" -ForegroundColor Green

# 5. Panggil script registrasi Add-in
Write-Host "`n5. Mendaftarkan Add-in ke SOLIDWORKS..." -ForegroundColor Yellow
$regScript = Join-Path $scriptDir "register_addin.ps1"
if (-not (Test-Path $regScript)) {
    Write-Error "Script registrasi tidak ditemukan di: $regScript"
    exit 1
}

& $regScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Registrasi Add-in gagal dengan exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  AI CAD Assistant v3.0.2 Berhasil Dideploy ke SOLIDWORKS! " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green

