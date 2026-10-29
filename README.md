# AI CAD Assistant for SOLIDWORKS

Add-in desktop berbasis kecerdasan buatan (AI) yang terintegrasi langsung di dalam **SOLIDWORKS** (2023, 2024, dan 2025) ditenagai oleh **Google Gemini API**.

---

## 🌟 Prinsip Inti

1. **AI Merencanakan, SOLIDWORKS Membangun Geometri**: Geometri dibangun secara deterministik melalui SOLIDWORKS API COM native, bukan kode bebas atau macro tanpa validasi.
2. **Tidak Mengarang Dimensi Teknik**: Nilai yang tidak tersedia atau ambigu wajib diklarifikasi atau diberi label asumsi eksplisit.
3. **Model Native & Editable**: Menghasilkan sketch, dimensi, dan feature tree native SOLIDWORKS, bukan sekadar import mesh (.stl) tanpa riwayat fitur.
4. **Keamanan & Pemulihan Dokumen**: Checkpoint otomatis sebelum modifikasi; file pengguna tidak ditimpa tanpa izin.
5. **Privasi & Keamanan Kredensial**: File tidak dikirim ke Google Gemini sebelum tombol **Analisis** ditekan secara manual. Kredensial API Key dienkripsi menggunakan Windows DPAPI (*Data Protection API*) di profil lokal pengguna.

---

## 🏗️ Struktur Solusi

```text
├── AICADAssistant.sln
├── Referensi/
│   └── PRD_AI_CAD_Assistant_v1.0.md      # Sumber kebenaran spesifikasi
├── docs/
│   ├── ARCHITECTURE.md                  # Desain arsitektur & tanggung jawab modul
│   ├── IMPLEMENTATION_LOG.md            # Log implementasi & verifikasi bertahap
│   ├── COMPATIBILITY_MATRIX.md          # Matriks dukungan SOLIDWORKS 2023-2025
│   └── KNOWN_LIMITATIONS.md             # Batasan fitur & langkah mitigasi
├── scripts/
│   ├── build.ps1                        # Script build solusi via MSBuild
│   ├── run_tests.ps1                    # Script eksekusi test suite otomatis
│   ├── register_addin.ps1               # Script registrasi COM Add-in ke SOLIDWORKS
│   └── unregister_addin.ps1             # Script pembatalan registrasi Add-in
└── src/
    ├── AICADAssistant.Core/             # Pustaka logika murni (non-COM)
    │   ├── CAD/                         # UnitConverter, CadPlanner, SketchBuilder, FeatureBuilder
    │   ├── Gemini/                      # GeminiClient, Request/Response, Exception
    │   ├── Models/                      # CadSpec, CadOperation, AppSettings, ConversationMessage
    │   ├── Services/                    # CredentialService (DPAPI), LoggingService, ImageService, BackupService
    │   └── Validation/                  # CadPlanValidator, ValidationResult
    ├── AICADAssistant.AddIn/            # Add-in SOLIDWORKS & UI Task Pane (WPF)
    │   ├── AddInEntryPoint.cs           # ISwAddin COM entry point & TaskPane lifecycle
    │   ├── SolidWorksAutomationService.cs # Eksekutor geometri deterministik SOLIDWORKS COM API
    │   └── UI/                          # Task Pane WPF Control, Settings Window, ViewModels, Themes
    └── AICADAssistant.Tests/            # Suite unit test otomatis
```

---

## 🚀 Panduan Membangun (Build)

### Prasyarat
- **Windows 10 / 11 64-bit**
- **SOLIDWORKS 2023, 2024, atau 2025**
- **.NET Framework 4.8** (dilengkapi compiler C# bawaan Windows di `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`)

### Langkah Kompilasi
Buka terminal PowerShell di folder proyek dan jalankan:
```powershell
.\scripts\build.ps1
```

### Menjalankan Unit Test
Suite unit test berjalan secara terisolasi tanpa memerlukan sesi live SOLIDWORKS aktif:
```powershell
.\scripts\run_tests.ps1
```
*Hasil: 39 unit test lolos (100% passed).*

---

## 🔌 Pendaftaran Add-in ke SOLIDWORKS

Untuk mendaftarkan add-in agar muncul pada daftar Add-Ins SOLIDWORKS:
1. Buka PowerShell (sebagai Administrator jika ingin registrasi sistem HKLM).
2. Jalankan perintah:
   ```powershell
   .\scripts\register_addin.ps1
   ```
3. Buka **SOLIDWORKS** -> Buka menu **Tools** -> **Add-Ins...** -> Pastikan **AI CAD Assistant** tercentang.
4. Panel akan muncul di Task Pane sebelah kanan dengan tema Merah-Putih-Abu-abu khas SOLIDWORKS.

Untuk menghapus registrasi:
```powershell
.\scripts\unregister_addin.ps1
```

---

## ⚙️ Konfigurasi Gemini API Key

1. Klik tombol roda gigi (⚙) di header panel Task Pane AI CAD Assistant.
2. Masukkan API Key Google Gemini Anda di kolom input yang aman.
3. Klik **Simpan API Key**. Key disimpan secara terenkripsi menggunakan Windows DPAPI di profil pengguna Windows Anda.
4. Klik **Uji Koneksi** untuk memverifikasi komunikasi ke server Google Gemini.
