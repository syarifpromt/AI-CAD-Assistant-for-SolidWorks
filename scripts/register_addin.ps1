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

# 3. Daftarkan COM Add-in via 64-bit RegAsm
Write-Host "Mendaftarkan COM Add-in via 64-bit RegAsm..." -ForegroundColor Cyan
& $regAsmPath $dllPath /codebase /tlb
if ($LASTEXITCODE -ne 0) {
    Write-Error "Pendaftaran COM via RegAsm gagal dengan exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# 4. Tulis kunci registri SOLIDWORKS Add-in
$guid = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
$title = "AI CAD Assistant"
$desc = "AI CAD Assistant untuk SOLIDWORKS menggunakan Gemini AI"

Write-Host "Menulis kunci registri SOLIDWORKS Add-in..." -ForegroundColor Cyan

# 4a. HKCU AddIns & Startup (Wajib untuk user saat ini)
try {
    $hkcuAddIns = "HKCU:\Software\SolidWorks\AddIns\$guid"
    if (-not (Test-Path $hkcuAddIns)) { New-Item -Path $hkcuAddIns -Force | Out-Null }
    Set-ItemProperty -Path $hkcuAddIns -Name "Title" -Value $title
    Set-ItemProperty -Path $hkcuAddIns -Name "Description" -Value $desc
    Set-ItemProperty -Path $hkcuAddIns -Name "IsEnabled" -Value 1 -Type DWord

    $hkcuStartup = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
    if (-not (Test-Path $hkcuStartup)) { New-Item -Path $hkcuStartup -Force | Out-Null }
    Set-ItemProperty -Path $hkcuStartup -Name "(Default)" -Value 1 -Type DWord
    Write-Host "[OK] Terdaftar di HKCU (User-level)" -ForegroundColor Green
} catch {
    Write-Error "Gagal menulis kunci registri HKCU: $_"
    exit 1
}

# 4b. HKLM AddIns (Opsional - jika dijalankan dengan hak Administrator)
try {
    $hklmAddIns = "HKLM:\Software\SolidWorks\AddIns\$guid"
    if (-not (Test-Path $hklmAddIns)) { New-Item -Path $hklmAddIns -Force -ErrorAction Stop | Out-Null }
    Set-ItemProperty -Path $hklmAddIns -Name "(Default)" -Value 1 -Type DWord -ErrorAction Stop
    Write-Host "[OK] Terdaftar di HKLM (Machine-wide)" -ForegroundColor Green
} catch {
    Write-Host "[INFO] HKLM dilewati (memerlukan hak Administrator). Add-in tetap aktif via HKCU (User-level)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[SUCCESS] AI CAD Assistant BERHASIL didaftarkan!" -ForegroundColor Green
Write-Host "Silakan buka SOLIDWORKS -> Tools -> Add-Ins untuk mengaktifkan." -ForegroundColor Cyan
