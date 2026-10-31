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

# 0. Periksa hak Administrator dan status SOLIDWORKS
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Registrasi SOLIDWORKS Add-In memerlukan hak Administrator untuk menulis ke 'HKLM:\SOFTWARE\SolidWorks\AddIns'. Harap buka PowerShell dengan opsi 'Run as Administrator'."
    exit 1
}

$swProcesses = Get-Process -Name "SLDWORKS" -ErrorAction SilentlyContinue
if ($swProcesses) {
    Write-Warning "SOLIDWORKS sedang berjalan (PID: $($swProcesses.Id -join ', '))."
    Write-Warning "Mohon tutup SOLIDWORKS terlebih dahulu sebelum melakukan registrasi COM."
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$dllPath = Join-Path $projectRoot "src\AICADAssistant.AddIn\bin\$Configuration\AICADAssistant.AddIn.dll"
$regAsmPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

# 1. Periksa ketersediaan assembly hasil build
if (-not (Test-Path $dllPath)) {
    Write-Error "Assembly Add-in tidak ditemukan di: $dllPath. Harap bangun solusi terlebih dahulu sebelum mendaftar."
    exit 1
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
# Selaras dengan contoh resmi SOLIDWORKS: HKLM AddIns default DWORD = 0
try {
    $hklmAddIns = "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
    if (-not (Test-Path $hklmAddIns)) { New-Item -Path $hklmAddIns -Force | Out-Null }
    Set-ItemProperty -Path $hklmAddIns -Name "(Default)" -Value 0 -Type DWord
    Set-ItemProperty -Path $hklmAddIns -Name "Title" -Value $title -Type String
    Set-ItemProperty -Path $hklmAddIns -Name "Description" -Value $desc -Type String
    Write-Host "[OK] Terdaftar di HKLM:\SOFTWARE\SolidWorks\AddIns\$guid (Title, Description, Default=0)" -ForegroundColor Green
} catch {
    Write-Error "Gagal menulis kunci registri HKLM: $_"
    exit 1
}

# 5b. HKCU AddInsStartup (Wajib: memuat add-in saat startup untuk user saat ini)
# Selaras dengan contoh resmi SOLIDWORKS: HKCU AddInsStartup default DWORD = 1
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

# 6. Verifikasi integritas nilai registri dan COM CLSID
Write-Host "`nMemverifikasi integritas registri COM dan SOLIDWORKS Add-in..." -ForegroundColor Cyan
$verificationPassed = $true

# 6a. Verifikasi HKLM AddIns
$hklmProps = Get-ItemProperty -Path "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid" -ErrorAction SilentlyContinue
if (-not $hklmProps) {
    Write-Error "Verifikasi HKLM gagal: Kunci registri HKLM:\SOFTWARE\SolidWorks\AddIns\$guid tidak ditemukan."
    $verificationPassed = $false
} else {
    if ($hklmProps.'(Default)' -ne 0) {
        Write-Error "Verifikasi HKLM AddIns gagal: Nilai Default bukan 0 (ditemukan: $($hklmProps.'(Default)'))."
        $verificationPassed = $false
    }
    if ($hklmProps.Title -ne $title) {
        Write-Error "Verifikasi HKLM AddIns gagal: Title tidak cocok (diharapkan: '$title', ditemukan: '$($hklmProps.Title)')."
        $verificationPassed = $false
    }
    if ($hklmProps.Description -ne $desc) {
        Write-Error "Verifikasi HKLM AddIns gagal: Description tidak cocok."
        $verificationPassed = $false
    }
}

# 6b. Verifikasi HKCU AddInsStartup
$hkcuProps = Get-ItemProperty -Path "HKCU:\Software\SolidWorks\AddInsStartup\$guid" -ErrorAction SilentlyContinue
if (-not $hkcuProps) {
    Write-Error "Verifikasi HKCU Startup gagal: Kunci registri HKCU:\Software\SolidWorks\AddInsStartup\$guid tidak ditemukan."
    $verificationPassed = $false
} elseif ($hkcuProps.'(Default)' -ne 1) {
    Write-Error "Verifikasi HKCU Startup gagal: Nilai Default bukan 1 (ditemukan: $($hkcuProps.'(Default)'))."
    $verificationPassed = $false
}

# 6c. Verifikasi COM CLSID & InprocServer32
$clsidPath = "HKLM:\SOFTWARE\Classes\CLSID\$guid\InprocServer32"
if (-not (Test-Path $clsidPath)) {
    $clsidPath = "HKCR:\CLSID\$guid\InprocServer32"
}

if (-not (Test-Path $clsidPath)) {
    Write-Error "Verifikasi COM CLSID gagal: Kunci InprocServer32 tidak ditemukan untuk CLSID $guid."
    $verificationPassed = $false
} else {
    $inprocProps = Get-ItemProperty -Path $clsidPath -ErrorAction SilentlyContinue
    $expectedVer = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()

    # Verifikasi Assembly Identity memuat versi yang benar
    if (-not $inprocProps.Assembly -or $inprocProps.Assembly -notlike "*Version=$expectedVer*") {
        Write-Error "Verifikasi Assembly Identity gagal: Assembly registri ($($inprocProps.Assembly)) tidak memuat versi $expectedVer."
        $verificationPassed = $false
    } else {
        Write-Host "  [OK] Assembly Identity cocok: $($inprocProps.Assembly)" -ForegroundColor Green
    }

    # Verifikasi CodeBase menunjuk tepat ke AddIn DLL hasil build
    if (-not $inprocProps.CodeBase) {
        Write-Error "Verifikasi CodeBase gagal: Properti CodeBase kosong pada $clsidPath."
        $verificationPassed = $false
    } else {
        $rawCodeBase = $inprocProps.CodeBase
        $normalizedCodeBase = [System.Uri]::UnescapeDataString($rawCodeBase).Replace("file:///", "").Replace("/", "\")
        $expectedDllPath = (Resolve-Path $dllPath).Path
        if ($normalizedCodeBase -ne $expectedDllPath) {
            Write-Error "Verifikasi CodeBase gagal: CodeBase ($normalizedCodeBase) tidak menunjuk tepat ke file build ($expectedDllPath)."
            $verificationPassed = $false
        } else {
            Write-Host "  [OK] CodeBase menunjuk tepat ke: $expectedDllPath" -ForegroundColor Green
        }
    }
}

if (-not $verificationPassed) {
    Write-Error "Registrasi Add-in GAGAL pada tahap verifikasi integritas."
    exit 1
}

Write-Host ""
Write-Host "[SUCCESS] AI CAD Assistant BERHASIL didaftarkan ke SOLIDWORKS 2025!" -ForegroundColor Green
Write-Host "Detail Registrasi:" -ForegroundColor Cyan
Write-Host "  - GUID:        $guid"
Write-Host "  - Title:       $title"
Write-Host "  - Description: $desc"
Write-Host "  - Discovery:   HKLM:\SOFTWARE\SolidWorks\AddIns\$guid (Default=0)"
Write-Host "  - Startup:     HKCU:\Software\SolidWorks\AddInsStartup\$guid (Default=1)"
Write-Host ""
Write-Host "Silakan buka SOLIDWORKS 2025 -> Tools -> Add-Ins untuk memverifikasi." -ForegroundColor Cyan
