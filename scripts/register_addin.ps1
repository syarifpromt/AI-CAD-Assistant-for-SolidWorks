<#
.SYNOPSIS
    Registers the AI CAD Assistant AddIn into SOLIDWORKS via 64-bit RegAsm.
    Run PowerShell as Administrator to register into HKLM (mandatory for SOLIDWORKS Add-in discovery).
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$guid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
$title = "AI CAD Assistant"
$desc = "AI CAD Assistant untuk SOLIDWORKS menggunakan Gemini AI"

# 0. Periksa hak Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Registrasi SOLIDWORKS Add-In memerlukan hak Administrator untuk menulis ke 'HKLM:\SOFTWARE\SolidWorks\AddIns'. Harap buka PowerShell dengan opsi 'Run as Administrator'."
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$dllPath = Join-Path $projectRoot "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$regAsmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

# 1. Periksa ketersediaan assembly hasil build
if (-not (Test-Path $dllPath)) {
    Write-Host "[INFO] Assembly belum dibangun. Menjalankan build..." -ForegroundColor Yellow
    $buildScript = Join-Path $scriptDir "build.ps1"
    if (Test-Path $buildScript) {
        & $buildScript -Configuration $Configuration
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dllPath)) {
            Write-Error "Gagal membangun assembly Add-in di: $dllPath"
            exit 1
        }
    } else {
        Write-Error "Script build tidak ditemukan di: $buildScript"
        exit 1
    }
}

# 2. Periksa ketersediaan RegAsm 64-bit
if (-not (Test-Path $regAsmPath)) {
    Write-Error "RegAsm 64-bit tidak ditemukan di: $regAsmPath"
    exit 1
}

# 3. Periksa ketersediaan dependensi SOLIDWORKS interop yang dibutuhkan oleh .NET loader
$binDir = Split-Path -Parent $dllPath
$requiredDependencies = @(
    "SolidWorks.Interop.swpublished.dll",
    "SolidWorks.Interop.sldworks.dll",
    "SolidWorks.Interop.swconst.dll"
)
$knownRedistPaths = @(
    "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist",
    "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE\api\redist"
)

$missingDependencies = @()
foreach ($dep in $requiredDependencies) {
    $targetPath = Join-Path $binDir $dep
    if (-not (Test-Path $targetPath)) {
        $found = $false
        foreach ($redist in $knownRedistPaths) {
            $sourcePath = Join-Path $redist $dep
            if (Test-Path $sourcePath) {
                Write-Host "[INFO] Menyalin dependensi '$dep' dari '$redist' ke output add-in..." -ForegroundColor Yellow
                Copy-Item -Path $sourcePath -Destination $targetPath -Force
                $found = $true
                break
            }
        }
        if (-not $found) {
            $missingDependencies += $dep
        }
    }
}

if ($missingDependencies.Count -gt 0) {
    Write-Error "Dependensi SOLIDWORKS interop berikut tidak ditemukan di direktori output ($binDir) maupun redist ($($knownRedistPaths -join '; ')): $($missingDependencies -join ', '). Pendaftaran dibatalkan."
    exit 1
}

# 4. Daftarkan COM Add-in via 64-bit RegAsm
# Opsi /tlb tidak digunakan karena SOLIDWORKS berkomunikasi melalui antarmuka ISwAddin (swpublished.dll)
Write-Host "Mendaftarkan COM Add-in via 64-bit RegAsm..." -ForegroundColor Cyan
& $regAsmPath $dllPath /codebase
if ($LASTEXITCODE -ne 0) {
    Write-Error "Pendaftaran COM via RegAsm gagal dengan exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 5. Tulis kunci registri resmi SOLIDWORKS Add-in
Write-Host "Menulis konfigurasi registri resmi SOLIDWORKS Add-in..." -ForegroundColor Cyan

# 5a. HKLM AddIns (Wajib: lokasi penemuan add-in untuk dialog Tools -> Add-Ins SOLIDWORKS)
try {
    $hklmAddIns = "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
    if (-not (Test-Path $hklmAddIns)) { New-Item -Path $hklmAddIns -Force | Out-Null }
    Set-ItemProperty -Path $hklmAddIns -Name "(Default)" -Value 1 -Type DWord
    Set-ItemProperty -Path $hklmAddIns -Name "Title" -Value $title -Type String
    Set-ItemProperty -Path $hklmAddIns -Name "Description" -Value $desc -Type String
    Write-Host "[OK] Terdaftar di HKLM:\SOFTWARE\SolidWorks\AddIns\$guid (Title, Description, Default=1)" -ForegroundColor Green
} catch {
    Write-Error "Gagal menulis kunci registri HKLM: $_"
    exit 1
}

# 5b. HKCU AddInsStartup (Wajib: memuat add-in saat startup untuk user saat ini)
try {
    $hkcuStartup = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
    if (-not (Test-Path $hkcuStartup)) { New-Item -Path $hkcuStartup -Force | Out-Null }
    Set-ItemProperty -Path $hkcuStartup -Name "(Default)" -Value 1 -Type DWord
    Write-Host "[OK] Terdaftar di HKCU:\Software\SolidWorks\AddInsStartup\$guid (Default=1)" -ForegroundColor Green
} catch {
    Write-Error "Gagal menulis kunci registri HKCU AddInsStartup: $_"
    exit 1
}

# 5c. Pembersihan kunci legacy HKCU AddIns (jika ada dari versi skrip sebelumnya)
try {
    $hkcuLegacy = "HKCU:\Software\SolidWorks\AddIns\$guid"
    if (Test-Path $hkcuLegacy) {
        Remove-Item -Path $hkcuLegacy -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[INFO] Entri lama di HKCU:\Software\SolidWorks\AddIns telah dibersihkan." -ForegroundColor Yellow
    }
} catch {
    # Non-fatal jika pembersihan legacy gagal
}

# 6. Verifikasi integritas nilai registri
$hklmProps = Get-ItemProperty -Path "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid" -ErrorAction SilentlyContinue
$hkcuProps = Get-ItemProperty -Path "HKCU:\Software\SolidWorks\AddInsStartup\$guid" -ErrorAction SilentlyContinue

$verificationPassed = $true
if (-not $hklmProps -or $hklmProps.Title -ne $title -or $hklmProps.Description -ne $desc) {
    Write-Error "Verifikasi HKLM gagal: Title atau Description tidak cocok."
    $verificationPassed = $false
}

if (-not $hkcuProps -or $hkcuProps.'(Default)' -ne 1) {
    Write-Error "Verifikasi HKCU Startup gagal: Nilai Default bukan 1."
    $verificationPassed = $false
}

if (-not $verificationPassed) {
    exit 1
}

Write-Host ""
Write-Host "[SUCCESS] AI CAD Assistant BERHASIL didaftarkan ke SOLIDWORKS 2025!" -ForegroundColor Green
Write-Host "Detail Registrasi:" -ForegroundColor Cyan
Write-Host "  - GUID:        $guid"
Write-Host "  - Title:       $title"
Write-Host "  - Description: $desc"
Write-Host "  - Discovery:   HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
Write-Host "  - Startup:     HKCU:\Software\SolidWorks\AddInsStartup\$guid"
Write-Host ""
Write-Host "Silakan buka SOLIDWORKS 2025 -> Tools -> Add-Ins untuk memverifikasi." -ForegroundColor Cyan
