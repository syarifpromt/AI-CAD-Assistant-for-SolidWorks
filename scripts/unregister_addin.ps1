<#
.SYNOPSIS
    Unregisters the AI CAD Assistant AddIn from SOLIDWORKS.
    Run PowerShell as Administrator to remove HKLM registration entries.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$guid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

# 0. Periksa status SOLIDWORKS
$swProcesses = Get-Process -Name "SLDWORKS" -ErrorAction SilentlyContinue
if ($swProcesses) {
    Write-Warning "SOLIDWORKS sedang berjalan (PID: $($swProcesses.Id -join ', '))."
    Write-Warning "Mohon tutup SOLIDWORKS terlebih dahulu sebelum menghapus registrasi Add-in."
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$dllPath = Join-Path $projectRoot "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$regAsmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

Write-Host "Menghapus registrasi AI CAD Assistant dari SOLIDWORKS..." -ForegroundColor Cyan

# 1. Unregister COM class via RegAsm
if ((Test-Path $dllPath) -and (Test-Path $regAsmPath)) {
    Write-Host "Membatalkan pendaftaran COM via 64-bit RegAsm..." -ForegroundColor Cyan
    & $regAsmPath $dllPath /unregister
}

# 2. Hapus kunci HKLM AddIns untuk GUID proyek ini saja
try {
    $hklmPath = "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
    if (Test-Path $hklmPath) {
        Remove-Item -Path $hklmPath -Recurse -Force -ErrorAction Stop
        Write-Host "[OK] Kunci registri HKLM AddIns dihapus ($guid)" -ForegroundColor Green
    }
} catch {
    Write-Warning "Gagal menghapus kunci HKLM (memerlukan hak Administrator): $_"
}

# 3. Hapus kunci HKCU AddInsStartup untuk GUID proyek ini saja
try {
    $hkcuStartup = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
    if (Test-Path $hkcuStartup) {
        Remove-Item -Path $hkcuStartup -Recurse -Force -ErrorAction Stop
        Write-Host "[OK] Kunci registri HKCU AddInsStartup dihapus ($guid)" -ForegroundColor Green
    }
} catch {
    Write-Warning "Gagal menghapus kunci HKCU AddInsStartup: $_"
}

# 4. Hapus kunci legacy HKCU AddIns jika masih ada
try {
    $hkcuLegacy = "HKCU:\Software\SolidWorks\AddIns\$guid"
    if (Test-Path $hkcuLegacy) {
        Remove-Item -Path $hkcuLegacy -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "[OK] Kunci legacy HKCU AddIns dibersihkan ($guid)" -ForegroundColor Green
    }
} catch {
    # Non-fatal
}

Write-Host ""
Write-Host "[SUCCESS] AI CAD Assistant berhasil dihapus dari registrasi SOLIDWORKS." -ForegroundColor Green
