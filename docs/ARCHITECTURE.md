# Arsitektur Aplikasi AI CAD Assistant

## 1. Ikhtisar Arsitektur

AI CAD Assistant dirancang dengan pola modular dan pemisahan tanggung jawab (*Separation of Concerns*). Komponen logika bisnis murni (*Core*) dipisahkan sepenuhnya dari dependensi COM SOLIDWORKS untuk menjamin kemampuan uji otomatis (*testability*) tanpa memerlukan sesi SOLIDWORKS aktif.

```
┌────────────────────────────────────────────────────────┐
│                   SOLIDWORKS Process                   │
│                                                        │
│  ┌─────────────────────────┐  ┌─────────────────────┐  │
│  │   SwAddIn (ISwAddin)    │  │  ElementHost Bridge │  │
│  │  COM Entry & Lifecycle  │──│  WinForms <-> WPF   │  │
│  └───────────┬─────────────┘  └──────────┬──────────┘  │
│              │                           │             │
│              ▼                           ▼             │
│  ┌─────────────────────────┐  ┌─────────────────────┐  │
│  │  SolidWorksAutomation   │  │ MainTaskPaneControl │  │
│  │    Service (COM API)    │  │     (WPF XAML)      │  │
│  └───────────▲─────────────┘  └──────────┬──────────┘  │
│              │                           │             │
│              │                ┌──────────▼──────────┐  │
│              │                │    MainViewModel    │  │
│              │                └──────────┬──────────┘  │
└──────────────┼───────────────────────────┼─────────────┘
               │                           │
               │  ┌────────────────────────┴──────────┐
               │  │       AICADAssistant.Core         │
               │  │                                   │
               │  │  ┌───────────────┐ ┌───────────┐  │
               │  │  │   CadPlanner  │ │ Gemini    │  │
               │  │  │ (Prompt/JSON) │ │  Client   │──┼──► Google Gemini API
               │  │  └───────┬───────┘ └───────────┘  │      (HTTPS / JSON)
               │  │          │                        │
               │  │  ┌───────▼───────┐                │
               │  │  │CadPlanValidator               │
               │  │  │(Semantic Check)                │
               └──┼──│  [CadSpec]    │                │
                  │  └───────────────┘                │
                  │  ┌───────────────┐ ┌───────────┐  │
                  │  │ UnitConverter │ │ Credential│  │
                  │  │ (mm -> meters)│ │  (DPAPI)  │  │
                  │  └───────────────┘ └───────────┘  │
                  └───────────────────────────────────┘
```

---

## 2. Tanggung Jawab Modul

### 2.1 Modul `AICADAssistant.Core`
Berisi seluruh logika bisnis independen yang tidak memiliki referensi ke COM DLL SOLIDWORKS:
- **`Models`**:
  - `CadSpec`: Kontrak spesifikasi rencana CAD terstruktur (RequestId, Units, Dimensions, Operations, Unknowns, Notes).
  - `CadOperation`: Definisi operasi atomik geometris dari whitelist yang diizinkan.
  - `OperationResult` & `ExecutionResult`: Status hasil eksekusi, bounding box, solid body count, dan error diagnostics.
  - `AppSettings`: Pengaturan model, timeout, retry, dan direktori keluaran.
  - `ConversationMessage`: Riwayat pesan percakapan dan peran pengguna/asisten.
- **`Validation`**:
  - `CadPlanValidator`: Memvalidasi whitelist operasi, ketergantungan fitur (anti-circular dependency), keberadaan parameter dimensi positif, serta mendeteksi dimensi penting yang belum diketahui (*missing dimensions*).
  - `ValidationResult`: Penampung pesan kesalahan, peringatan, dan daftar klarifikasi.
- **`CAD`**:
  - `UnitConverter`: Titik sentral konversi satuan (mm, cm, in, m) ke meter internal SOLIDWORKS.
  - `CadPlanner`: Ekstraksi JSON dari teks Gemini, pembuatan prompt instruksi terstruktur berbasis skema CadSpec, penanganan revisi model, dan penyusunan pesan klarifikasi.
  - `SketchBuilder` & `FeatureBuilder`: Pembungkus definisi entitas sketch dan fitur 3D.
- **`Gemini`**:
  - `GeminiClient`: Klien HTTPS asinkron ke Google Gemini API dengan dukungan multimodal (teks, gambar, PDF), penanganan timeout, dan retry terbatas untuk HTTP 429 / 5xx.
  - `GeminiRequest` & `GeminiResponse`: Model DTO payload JSON REST API.
  - `GeminiException`: Penanganan error terstruktur (RateLimit, Timeout, Auth, Network).
- **`Services`**:
  - `CredentialService`: Pengamanan API Key menggunakan enkripsi Windows DPAPI pada profil pengguna lokal (`%APPDATA%\AICADAssistant\credential.dat`).
  - `LoggingService`: Pencatatan log teknis berputar (7 hari) tanpa pernah mencatat API Key atau payload biner gambar.
  - `ImageService`: Validasi file lokal (ekstensi, ukuran maks 20 MB, orientasi, estimasi halaman PDF).
  - `BackupService`: Pencadangan file aktif (checkpoint) sebelum perubahan CAD dan pemulihan jika gagal.

### 2.2 Modul `AICADAssistant.AddIn`
Menangani interaksi antarmuka pengguna di dalam SOLIDWORKS dan pemanggilan SOLIDWORKS API COM:
- **`AddInEntryPoint` (`SwAddIn`)**:
  - Mengimplementasikan antarmuka `ISwAddin`.
  - Pendaftaran COM GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` ke registri SOLIDWORKS.
  - Pembuatan Task Pane View dengan Windows Forms `ElementHost` yang menampung kontrol WPF.
  - Penanganan event `ActiveDocChangeNotify` dan pembersihan COM objects pada `DisconnectFromSW`.
- **`SolidWorksAutomationService`**:
  - Menjalankan operasi deterministik: `CreateNewPart`, `OpenSketchOnPlane`, `DrawCenterRectangle`, `DrawCircle`, `ExtrudeBoss`, `ExtrudeCut`, `AddFillet`, `AddChamfer`, `AddSimpleHole`, `RebuildModel`.
  - Mengukur ukuran bounding box (`GetBoundingBoxMm`) dan jumlah solid body (`GetSolidBodyCount`).
  - Menyimpan part native `.SLDPRT` dan ekspor `.STEP` / `.STL`.
- **`UI`**:
  - `MainTaskPaneControl.xaml`: Tampilan panel Task Pane dengan tema Merah-Putih SOLIDWORKS, riwayat chat percakapan, pratinjau lampiran, tombol aksi kontekstual, indikator status berwarna, dan input teks multiline.
  - `SettingsWindow.xaml`: Dialog konfigurasi API Key terenkripsi, pemilihan model Gemini, satuan default, dan pengujian koneksi.

---

## 3. Alur Kerja Eksekusi Model

```text
Pengguna mengetik teks atau melampirkan gambar teknik
  │
  ▼
[Pengguna menekan Kirim / Analisis] (Tidak ada pengiriman otomatis)
  │
  ▼
GeminiClient mengirim HTTPS REST Request ke Google Gemini API
  │
  ▼
Respons JSON diterima -> CadPlanner mengekstrak & mem-parsing CadSpec
  │
  ▼
CadPlanValidator memvalidasi parameter:
  ├─ Jika ada dimensi kritis hilang -> Sistem bertanya (Klarifikasi)
  └─ Jika valid -> Status "Rencana Siap Dieksekusi"
       │
       ▼
[Pengguna menekan "Buat Model"]
  │
  ▼
BackupService membuat checkpoint file sumber (jika memodifikasi dokumen aktif)
  │
  ▼
SolidWorksAutomationService menjalankan urutan operasi deterministik via COM:
  ├─ Buat Part / Buka Sketch pada Plane
  ├─ Gambar entitas geometri dengan dimensi dalam meter (hasil konversi UnitConverter)
  ├─ Eksekusi Extrude Boss / Cut / Fillet / Chamfer / Hole
  └─ ForceRebuild3 & Verifikasi Solid Body & Bounding Box
       │
       ▼
Simpan part native .SLDPRT dan ekspor STEP / STL yang diminta
  │
  ▼
Laporan hasil pemodelan ditampilkan secara jujur di panel chat
```

