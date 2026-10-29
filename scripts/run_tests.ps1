<#
.SYNOPSIS
    Runs the automated unit test suite for AI CAD Assistant.
#>
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$testExe = Join-Path $projectRoot "src\AICADAssistant.Tests\bin\Debug\AICADAssistant.Tests.exe"

if (-not (Test-Path $testExe)) {
    Write-Host "Kompilasi ulang test suite..." -ForegroundColor Yellow
    & (Join-Path $scriptDir "build.ps1")
}

Write-Host "Menjalankan Test Suite..." -ForegroundColor Cyan
& $testExe
exit $LASTEXITCODE

