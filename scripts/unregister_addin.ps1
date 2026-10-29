<#
.SYNOPSIS
    Unregisters the AI CAD Assistant AddIn from SOLIDWORKS.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$dllPath = Join-Path $projectRoot "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$regAsmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

$guid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"

Write-Host "Menghapus registrasi Add-in dari SOLIDWORKS..." -ForegroundColor Cyan

# Unregister COM
if (Test-Path $dllPath) {
    & $regAsmPath $dllPath /unregister
}

# Remove HKCU keys
Remove-Item -Path "HKCU:\Software\SolidWorks\AddIns\$guid" -Recurse -Force
Remove-Item -Path "HKCU:\Software\SolidWorks\AddInsStartup\$guid" -Recurse -Force

# Remove HKLM keys
try {
    Remove-Item -Path "HKLM:\Software\SolidWorks\AddIns\$guid" -Recurse -Force -ErrorAction Stop
} catch {}

Write-Host "✓ AI CAD Assistant berhasil dihapus dari registrasi SOLIDWORKS." -ForegroundColor Green

