<#
.SYNOPSIS
    Builds the AI CAD Assistant for SOLIDWORKS solution.
#>
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path (Split-Path -Parent $scriptDir) "AICADAssistant.sln"
$vsBuildTools = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$msBuildPath = if (Test-Path $vsBuildTools) { $vsBuildTools } else { "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" }

if (-not (Test-Path $msBuildPath)) {
    Write-Error "MSBuild not found at: $msBuildPath"
    exit 1
}

Write-Host "Membangun solusi AI CAD Assistant ($Configuration | $Platform)..." -ForegroundColor Cyan
& $msBuildPath $solutionPath /p:Configuration=$Configuration /p:Platform=$Platform /v:m

if ($LASTEXITCODE -eq 0) {
    Write-Host "[OK] Build Berhasil!" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Build Gagal!" -ForegroundColor Red
    exit $LASTEXITCODE
}
