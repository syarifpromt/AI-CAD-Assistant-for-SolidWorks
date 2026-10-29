<#
.SYNOPSIS
    Registers the AI CAD Assistant AddIn into SOLIDWORKS via 64-bit RegAsm.
    Run PowerShell as Administrator to register in HKLM as well as HKCU.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$dllPath = Join-Path $projectRoot "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$regAsmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if (-not (Test-Path $dllPath)) {
    Write-Host "Assembly belum dibangun. Menjalankan build..." -ForegroundColor Yellow
    & (Join-Path $scriptDir "build.ps1") -Configuration $Configuration
}

if (-not (Test-Path $regAsmPath)) {
    Write-Error "RegAsm 64-bit tidak ditemukan di: $regAsmPath"
    exit 1
}

Write-Host "Mendaftarkan COM Add-in via 64-bit RegAsm..." -ForegroundColor Cyan
& $regAsmPath $dllPath /codebase /tlb

$guid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
$title = "AI CAD Assistant"
$desc = "AI CAD Assistant untuk SOLIDWORKS menggunakan Gemini AI"

Write-Host "Menulis kunci registri SOLIDWORKS Add-in..." -ForegroundColor Cyan

# HKCU AddIns
$hkcuAddIns = "HKCU:\Software\SolidWorks\AddIns\$guid"
if (-not (Test-Path $hkcuAddIns)) { New-Item -Path $hkcuAddIns -Force | Out-Null }
Set-ItemProperty -Path $hkcuAddIns -Name "Title" -Value $title
Set-ItemProperty -Path $hkcuAddIns -Name "Description" -Value $desc
Set-ItemProperty -Path $hkcuAddIns -Name "IsEnabled" -Value 1 -Type DWord

# HKCU AddInsStartup
$hkcuStartup = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
if (-not (Test-Path $hkcuStartup)) { New-Item -Path $hkcuStartup -Force | Out-Null }
Set-ItemProperty -Path $hkcuStartup -Name "(Default)" -Value 1 -Type DWord

# Try HKLM AddIns if elevated
try {
    $hklmAddIns = "HKLM:\Software\SolidWorks\AddIns\$guid"
    if (-not (Test-Path $hklmAddIns)) { New-Item -Path $hklmAddIns -Force -ErrorAction Stop | Out-Null }
    Set-ItemProperty -Path $hklmAddIns -Name "(Default)" -Value 1 -Type DWord -ErrorAction Stop
    Write-Host "✓ Terdaftar di HKLM (Machine-wide)" -ForegroundColor Green
} catch {
    Write-Host "ℹ HKLM dilewati (memerlukan hak Administrator). Add-in tetap aktif via HKCU (User-level)." -ForegroundColor Yellow
}

Write-Host "`n✓ AI CAD Assistant BERHASIL didaftarkan!" -ForegroundColor Green
Write-Host "Silakan buka SOLIDWORKS -> Tools -> Add-Ins untuk mengaktifkan." -ForegroundColor Cyan
