# Log Implementasi (Implementation Log)

**Proyek:** AI CAD Assistant for SOLIDWORKS  
**Tanggal Mulai:** 29 Oktober 2026  
**Status Solusi:** Berhasil dikompilasi (0 Error), 75/75 Unit Test Berhasil (100% Passed) — v1.1.5.0  

---

## Tahap 1: Inspeksi Lingkungan & Dokumen Persyaratan (PRD)

1. Menemukan dan membaca dokumen `Referensi/PRD_AI_CAD_Assistant_v1.0.md`.
2. Memeriksa lingkungan sistem pengembang:
   - OS: Windows 10/11 64-bit.
   - SOLIDWORKS: **SOLIDWORKS 2025** terpasang di `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\`.
   - Interop DLLs: Tersedia di `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\` (`SolidWorks.Interop.sldworks.dll`, `swconst.dll`, `swpublished.dll`).
   - .NET Framework: 4.8.10008 terpasang dengan compiler MSBuild 64-bit di `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`.
   - Menemukan batasan compiler: C# compiler bawaan .NET Framework 4.8 mendukung versi bahasa C# 5 / Default (bukan C# 7+), sehingga konstruksi kode disesuaikan menggunakan sintaks C# 5 yang murni dan kompatibel.

---

## Tahap 2: Pembentukan Solusi & Arsitektur

1. Menginisialisasi repositori Git dan membuat `.gitignore` untuk melindungi API Key, rahasia lokal, file build, dan log.
2. Membuat file solusi `AICADAssistant.sln` yang memuat 3 proyek:
   - `AICADAssistant.Core` (.NET Framework 4.8 class library, tanpa dependensi COM).
   - `AICADAssistant.AddIn` (.NET Framework 4.8 class library dengan referensi COM SOLIDWORKS dan antarmuka WPF).
   - `AICADAssistant.Tests` (.NET Framework 4.8 executable console test runner).

---

## Tahap 3: Implementasi Pustaka Logika Bisnis (`AICADAssistant.Core`)

1. **Model Data CAD**:
   - `CadSpec`: Spesifikasi data rencana CAD terstruktur (RequestId, DocumentType, Units, ExplicitDimensions, InferredValues, Unknowns, Operations, ExportTargets, Notes).
   - `CadOperation`: Representasi operasi atomik geometri dari whitelist operasi resmi (`AllowedOperations.Whitelist`).
   - `OperationResult` & `ExecutionResult`: Pelaporan hasil eksekusi, bounding box, solid body count, dan diagnosa kegagalan.
   - `AppSettings`: Pengaturan model Gemini, timeout, retry, dan direktori keluaran.
   - `ConversationMessage`: Entitas riwayat chat.
2. **Validasi Geometris**:
   - `CadPlanValidator`: Memeriksa keabsahan satuan, tipe operasi dari whitelist, keberadaan parameter bernilai positif, dependensi antarfitur, dan mendeteksi dimensi yang hilang (`MissingDimensions`).
   - `ValidationResult`: Mengumpulkan error, warning, dan daftar klarifikasi dimensi.
3. **Konversi Satuan & Perencanaan**:
   - `UnitConverter`: Titik sentral konversi satuan (mm, cm, in, ft) ke meter internal SOLIDWORKS.
   - `CadPlanner`: Ekstraksi JSON dari teks Gemini, pembuatan prompt instruksi terstruktur berbasis skema CadSpec, penanganan revisi model, dan penyusunan pesan klarifikasi.
4. **Integrasi Google Gemini**:
   - `GeminiClient`: Komunikasi REST API HTTPS dengan model yang dapat dikonfigurasi, dukungan multimodal teks dan gambar/PDF (Base64), penanganan timeout, dan retry terbatas untuk error sementara.
   - `GeminiRequest` & `GeminiResponse`: DTO serialisasi JSON payload Gemini.
   - `GeminiException`: Penanganan error terstruktur (RateLimit, Timeout, Auth, Network).
5. **Layanan Keamanan & Berkas**:
   - `CredentialService`: Pengamanan API Key menggunakan enkripsi Windows DPAPI pada profil pengguna lokal (`%APPDATA%\AICADAssistant\credential.dat`).
   - `LoggingService`: Pencatatan log teknis dengan rotasi berkas (7 hari) tanpa menyimpan API Key atau data biner gambar.
   - `ImageService`: Validasi file lokal (ekstensi, ukuran maks 20 MB, estimasi halaman PDF).
   - `BackupService`: Pencadangan file aktif (checkpoint) sebelum modifikasi dan pemulihan jika gagal.

---

## Tahap 4: Implementasi Test Suite Otomatis (`AICADAssistant.Tests`)

1. Membuat `TestFramework.cs` dan `Program.cs` yang menyediakan test runner mandiri berbasis refleksi.
2. Mengimplementasikan 5 kelompok pengujian:
   - `CadPlanValidatorTests`: 11 test kasus validasi (null spec, empty ops, negative dims, missing hole diameter, circular dependency, unknown ops, valid block).
   - `UnitConverterTests`: 14 test kasus konversi satuan (mm ke meter, meter ke mm, inch, cm, invalid unit, normalisasi string).
   - `CadSpecParsingTests`: 5 test kasus parsing JSON (markdown fence extraction, invalid json handling, clarification formatting).
   - `GeminiClientTests`: 5 test kasus kredensial DPAPI dan tipe GeminiException.
   - `BackupServiceTests`: 4 test kasus pembuatan checkpoint, pemulihan data, dan listing.
3. **Hasil Eksekusi**: Seluruh 39 unit test berhasil (0 gagal, waktu: 107 ms).

---

## Tahap 5: Implementasi Add-in SOLIDWORKS & UI WPF (`AICADAssistant.AddIn`)

1. **`AddInEntryPoint.cs` (`SwAddIn`)**:
   - Mengimplementasikan `ISwAddin` dengan GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.
   - Pendaftaran COM pada `HKCU\SOFTWARE\SolidWorks\AddIns` dan `AddInsStartup`.
   - Pembuatan Task Pane View dengan Windows Forms `ElementHost` yang menampung kontrol WPF `MainTaskPaneControl`.
   - Pengecekan versi SOLIDWORKS (minimal versi 2023).
   - Pembersihan referensi COM saat disconnect.
2. **`SolidWorksAutomationService.cs`**:
   - Menjalankan operasi deterministik via SOLIDWORKS API:
     - Pembuatan part baru menggunakan template default.
     - Pembukaan sketch pada Front/Top/Right plane.
     - Penggambaran center rectangle dan lingkaran.
     - Extrude Boss (`FeatureExtrusion3`) dan Extrude Cut (`FeatureCut4`).
     - Pembuatan Fillet (`FeatureFillet`) dan Chamfer (`InsertFeatureChamfer`).
     - Pembuatan lubang terstruktur.
     - Rebuild model (`ForceRebuild3`), penghitungan solid body (`GetBodies2`), dan pengukuran bounding box (`GetPartBox`).
     - Penyimpanan native `.SLDPRT` dan ekspor `.STEP` / `.STL`.
3. **Antarmuka Pengguna (WPF)**:
   - Menggunakan tema visual Merah-Putih SOLIDWORKS (`RedWhiteTheme.xaml`).
   - Task pane responsif berbahasa Indonesia dengan riwayat chat percakapan, pratinjau lampiran, tombol kontekstual ("Analisis Gambar", "Buat Model", "Batalkan", "Ekspor STEP/STL"), indikator status LED, dan input teks multiline.
   - Jendela Pengaturan (`SettingsWindow.xaml`) untuk API Key Gemini, pemilihan model, satuan default, dan tombol pengujian koneksi.

---

## Tahap 6: Skrip Pembantu & Verifikasi Awal

1. `scripts/build.ps1`: Otomasi kompilasi solusi via MSBuild.
2. `scripts/run_tests.ps1`: Otomasi eksekusi test suite.
3. `scripts/register_addin.ps1`: Pendaftaran COM add-in ke registri SOLIDWORKS.
4. `scripts/unregister_addin.ps1`: Pembersihan registri add-in.
5. Verifikasi build: Solusi terkompilasi 100% sukses tanpa error.

---

## Tahap 7: Peningkatan Konfigurasi Gemini & Validasi Model Terverifikasi

1. **Evaluasi & Pemilihan Model Gemini**:
   - Berdasarkan dokumentasi resmi Google Gemini API (`https://ai.google.dev/gemini-api/docs/models`), model lama yang telah dihentikan/usang (`gemini-1.5-pro`, `gemini-1.5-flash`, `gemini-2.0-flash`) dihapus dari konfigurasi default.
   - Mengevaluasi dan menetapkan `gemini-3.8-flash` sebagai model utama default (seimbang antara latensi cepat, penalaran arsitektur perangkat lunak jangka panjang, dan dukungan structured JSON output).
   - Menyediakan pilihan model aktif terverifikasi:
     - `gemini-3.8-flash` (Default)
     - `gemini-3.5-flash-lite` (Latensi ultra-rendah untuk pengenalan dokumen/sketsa gambar)
     - `gemini-3.1-pro-preview` (Penalaran multi-langkah dan agen tingkat lanjut)
     - `gemini-2.5-pro` (Model penalaran stabil berakurasi tinggi)
     - `gemini-2.5-flash` (Model hybrid cepat dan serbaguna)
     - `gemini-flash-latest` (Penunjuk alias otomatis ke rilis Flash terbaru)
2. **Pemisahan Konfigurasi Model Tanpa Mengubah CAD Engine**:
   - `SettingsWindow.xaml`: Mengubah `ComboBox` pemilihan model menjadi `IsEditable="True"` dengan two-way binding ke `GeminiModel`, sehingga pengguna dapat memilih dari daftar rekomendasi atau mengetik nama model custom apa pun tanpa perlu mengubah kode sumber C# CAD Engine.
   - `AppSettings.cs`: Menetapkan `DefaultGeminiModel = "gemini-3.8-flash"` serta logika auto-migrasi saat membaca konfigurasi lama agar otomatis berpindah dari model usang ke `gemini-3.8-flash`.
   - `GeminiClient.cs`: Menjadikan `CurrentModel` dinamis membaca konfigurasi aktif `_settings.GeminiModel` setiap kali request dipersiapkan.
3. **Penyempurnaan Payload & Penanganan Error REST API**:
   - Memperbaiki serialisasi request JSON agar mengecualikan field bernilai `null` (`nullValueHandling = Ignore`), mencegah penolakan payload oleh endpoint `v1beta`.
   - Memperkaya `GeminiException` dengan penanganan terstruktur:
     - HTTP 404 / `NOT_FOUND`: `ModelNotFoundException` dengan pesan jelas bahwa model tidak tersedia atau salah ketik.
     - HTTP 401 / 403 / `UNAUTHENTICATED`: `AuthException` dengan panduan periksa API Key.
     - HTTP 429 / `RESOURCE_EXHAUSTED`: `RateLimitException` dengan petunjuk kuota habis.
     - Timeout: `TimeoutException` dengan detail durasi timeout.
   - Menambahkan metode `TestConnectionDetailedAsync()` pada `GeminiClient` dan `SettingsViewModel` untuk pelaporan status koneksi yang informatif langsung di antarmuka pengguna.
4. **Pencegahan Kebocoran Kredensial**:
   - `LoggingService.cs`: Mengimplementasikan metode `RedactApiKey()` yang memindai dan menyamarkan parameter URL (`?key=...`) dan string API key Google (`AIzaSy...`) menjadi `[REDACTED_API_KEY]` sebelum ditulis ke file log atau dipaparkan ke antarmuka pengguna.
5. **Ekspansi Test Suite Otomatis**:
   - Menambahkan 5 pengujian baru di `AICADAssistant.Tests/GeminiClientTests.cs`:
     - Verifikasi model default `gemini-3.8-flash`.
     - Pengujian pergantian model dinamis.
     - Pengujian penanganan error HTTP 404 Model Not Found.
     - Pengujian fungsi `TestConnectionDetailedAsync` dan respons status.
     - Pengujian sanitasi redaksi API key pada `LoggingService`.
   - **Hasil Akhir Test**: 44/44 unit test lolos (100% passed, 0 failed, waktu eksekusi: 220 ms).

---

## Tahap 8: Resolusi Dependensi COM Interop SOLIDWORKS & Validasi Loadability RegAsm

1. **Analisis Kegagalan Registrasi COM (`RegAsm error RA0000`)**:
   - Gejala: `Could not load file or assembly 'SolidWorks.Interop.swpublished, Version=33.1.1.1, Culture=neutral, PublicKeyToken=89a97bdc5284e6d8' or one of its dependencies.`
   - Akar masalah:
     - `AICADAssistant.AddIn.csproj` sebelumnya dikonfigurasi dengan `<Private>false</Private>` (Copy Local = False) untuk referensi interop SOLIDWORKS.
     - SOLIDWORKS tidak menginstal DLL interop ke GAC (*Global Assembly Cache*). Interop DLLs berada di `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`.
     - Saat utilitas pendaftaran COM (`RegAsm.exe`) dijalankan di luar SOLIDWORKS pada assembly `AICADAssistant.AddIn.dll`, .NET assembly loader tidak dapat menemukan `SolidWorks.Interop.swpublished.dll` di folder output (`bin\Debug\`) maupun di GAC, sehingga memicu error RA0000.
2. **Perbaikan Referensi Proyek (`AICADAssistant.AddIn.csproj`)**:
   - Memperbarui ketiga referensi interop SOLIDWORKS:
     - `SolidWorks.Interop.sldworks` (Version 33.1.1.1 / SOLIDWORKS 2025 SP1)
     - `SolidWorks.Interop.swconst` (Version 33.1.1.1)
     - `SolidWorks.Interop.swpublished` (Version 33.1.1.1)
   - Mengatur `<Private>True</Private>` agar MSBuild secara deterministik menyalin ketiga DLL interop tersebut ke direktori output (`bin\Debug\` dan `bin\Release\`).
   - Mempertahankan `<EmbedInteropTypes>False</EmbedInteropTypes>` sesuai panduan resmi SOLIDWORKS API (mencegah inkonsistensi identitas antarmuka COM `ISwAddin`).
3. **Penyempurnaan Skrip Registrasi (`scripts/register_addin.ps1`)**:
   - Menambahkan mekanisme verifikasi pra-registrasi: sebelum `RegAsm.exe` dipanggil, skrip memeriksa keberadaan ketiga DLL interop di direktori output add-in.
   - Menyediakan fallback penyalinan otomatis dari lokasi redist instalasi SOLIDWORKS (`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist`) jika berkas belum ada di direktori build.
   - Membatalkan proses dengan pesan error deskriptif jika dependensi tidak ditemukan di lokasi mana pun, mencegah eksekusi RegAsm dalam kondisi dependensi rusak.
4. **Verifikasi & Pengujian Loadability**:
   - **Kompilasi MSBuild**: Berhasil 100% (0 error). Ketiga DLL interop kini tersalin lengkap di `bin\Debug\`.
   - **Unit Test Suite**: 44/44 unit test lolos (100% passed, 0 error).
   - **Pemeriksaan Refleksi .NET**: `Assembly.LoadFrom` berhasil memuat tipe `AICADAssistant.AddIn.SwAddIn`, antarmuka `ISwAddin`, GUID `{a1b2c3d4-e5f6-7890-abcd-ef1234567890}`, dan `ComVisible = True`.
   - **Pengujian Loadability RegAsm (Dry-run)**: Pengujian menggunakan `RegAsm.exe /regfile` berhasil dieksekusi dengan **ExitCode 0 (Sukses)** tanpa error RA0000, memverifikasi bahwa utilitas COM loader dapat memuat seluruh dependensi tanpa perlu mengubah Windows Registry.

---

## Tahap 9: Resolusi Kegagalan Type Library Exporter (TlbExp "Element not found")

1. **Analisis Kegagalan Ekspor Type Library**:
   - Gejala: `Types registered successfully`, diikuti oleh `RegAsm : error RA0000 : Type library exporter encountered an error while processing 'AICADAssistant.AddIn.SwAddIn, AICADAssistant.AddIn'. Error: Element not found.`
   - Akar masalah:
     - **Opsi `/tlb` yang Tidak Diperlukan:** `scripts/register_addin.ps1` memanggil `RegAsm.exe` dengan flag `/tlb`. Add-in SOLIDWORKS berbasis .NET sama sekali tidak memerlukan berkas Type Library (`.tlb`) karena SOLIDWORKS berkomunikasi menggunakan antarmuka COM `ISwAddin` yang telah didefinisikan dan diketahui secara native melalui `SolidWorks.Interop.swpublished.dll`.
     - **Duplikasi GUID (LIBID vs CLSID):** Berkas `AssemblyInfo.cs` memiliki `[assembly: Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]` yang nilainya persis sama dengan GUID kelas `SwAddIn` (`CLSID = a1b2c3d4-e5f6-7890-abcd-ef1234567890`). Dalam COM, identitas pustaka tipe (LIBID) dan identitas kelas (CLSID) tidak boleh identik karena memicu konflik dependensi sirkular pada subsistem TypeLib Windows.
     - **Visibilitas COM Global (`ComVisible(true)` di AssemblyInfo):** Atribut tingkat perakitan `[assembly: ComVisible(true)]` mengekspos semua kelas publik di assembly (termasuk kelas WPF ViewModels, `RelayCommand`, dan `IValueConverter`) ke eksportir COM. Karena tipe WPF tersebut tidak memiliki deskripsi antarmuka COM, eksportir type library mengalami kegagalan resolusi metadata (`TYPE_E_ELEMENTNOTFOUND`).
2. **Perbaikan Kode & Metadata Assembly**:
   - `src/AICADAssistant.AddIn/Properties/AssemblyInfo.cs`:
     - Mengubah visibilitas default perakitan menjadi `[assembly: ComVisible(false)]`. Dengan demikian, hanya tipe yang didekorasi eksplisit dengan `[ComVisible(true)]` (`SwAddIn`) yang diekspos ke subsistem COM.
     - Mengganti GUID perakitan (LIBID) dengan pengenal unik terpisah: `[assembly: Guid("f2c1b0a9-8e7d-6c5b-4a32-10fedcba9876")]`, menghilangkan tabrakan identitas dengan CLSID kelas.
   - `src/AICADAssistant.AddIn/AddInEntryPoint.cs`:
     - Mempertahankan CLSID add-in resmi: `[Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]` dan implementasi `ISwAddin` dengan `[ClassInterface(ClassInterfaceType.None)]`.
   - `scripts/register_addin.ps1`:
     - Menghapus opsi `/tlb` dari pemanggilan `RegAsm.exe`, sehingga perintah pendaftaran menjadi:
       `& $regAsmPath $dllPath /codebase`
3. **Verifikasi & Validasi**:
   - **Kompilasi MSBuild**: Berhasil 100% (0 error).
   - **Unit Tests**: 44/44 unit test lolos (100% passed).
   - **Validasi RegAsm Dry-run**: Pengujian simulasi pembuatan registri via `RegAsm.exe $dllPath /codebase /regfile:...` menghasilkan berkas registri CLSID murni untuk `SwAddIn` dengan **ExitCode 0 (Sukses)** tanpa memanggil TlbExp dan tanpa error RA0000.

---

## Tahap 10: Standardisasi Registrasi SOLIDWORKS Add-In Discovery (HKLM Title & Description)

1. **Analisis Masalah Visibilitas Add-in di SOLIDWORKS**:
   - Gejala: AI CAD Assistant tidak muncul pada dialog *Tools -> Add-Ins* di SOLIDWORKS 2025 meskipun assembly COM telah terdaftar di CLSID.
   - Pemeriksaan registri aktual:
     - `HKLM\SOFTWARE\SolidWorks\AddIns\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`: Hanya memuat nilai `(Default) = 1`, sedangkan nilai `Title` dan `Description` tidak ditemukan.
     - `HKCU\Software\SolidWorks\AddIns\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`: Memuat `Title` dan `Description`.
   - Akar masalah: Sesuai spesifikasi API resmi SOLIDWORKS, proses penemuan add-in (*add-in discovery*) untuk menampilkan entri di dialog *Tools -> Add-Ins* secara eksklusif membaca dari `HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}`. Tanpa string `Title` dan `Description` pada kunci HKLM tersebut, SOLIDWORKS mengabaikan add-in dari daftar dialog antarmuka.
2. **Penyelarasan Pola Registrasi Resmi**:
   - `src/AICADAssistant.AddIn/AddInEntryPoint.cs`:
     - Memperbarui `[ComRegisterFunction]` agar menulis `(Default) = 1` (DWORD), `Title = "AI CAD Assistant"` (String), dan `Description = "AI CAD Assistant untuk SOLIDWORKS menggunakan Gemini AI"` (String) langsung ke `HKLM\SOFTWARE\SolidWorks\AddIns\{GUID}`.
     - Mempertahankan registrasi `HKCU\Software\SolidWorks\AddInsStartup\{GUID}` dengan `(Default) = 1` untuk status startup per pengguna.
     - Membersihkan subkunci legacy di `HKCU\Software\SolidWorks\AddIns\{GUID}`.
   - `scripts/register_addin.ps1`:
     - Menambahkan validasi hak akses Administrator di awal skrip.
     - Menulis konfigurasi lengkap ke `HKLM:\SOFTWARE\SolidWorks\AddIns\$guid` (`(Default)=1`, `Title`, `Description`).
     - Menulis `HKCU:\Software\SolidWorks\AddInsStartup\$guid` (`(Default)=1`).
     - Membersihkan entri legacy di `HKCU:\Software\SolidWorks\AddIns\$guid`.
     - Memverifikasi keberadaan dan kebenaran seluruh nilai registri HKLM dan HKCU sebelum melaporkan pesan `[SUCCESS]`.
   - `scripts/unregister_addin.ps1`:
     - Memastikan pembersihan registrasi terisolasi secara ketat hanya pada GUID proyek `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` di HKLM, HKCU Startup, dan legacy HKCU tanpa menyentuh add-in lainnya.
3. **Verifikasi**:
   - **Kompilasi Solusi**: 100% Berhasil (0 Error).
   - **Unit Tests**: 44/44 lolos (100% passed).
   - **Validasi Sintaks Skrip**: Seluruh skrip PowerShell di `scripts/` lolos pemeriksaan AST Parser (0 Error).

---

## Tahap 11: Resolusi InvalidCastException pada ConnectToSW (ClassInterfaceType.AutoDispatch & Taskpane DisplayWindowFromHandlex64)

1. **Analisis Error Runtime**:
   - Gejala: Saat kotak centang add-in dicentang di *Tools -> Add-Ins*, muncul dialog `Gagal menghubungkan AI CAD Assistant: Specified cast is not valid`.
   - Jejak log faktual di `%APPDATA%\AICADAssistant\addin.log`:
     `[2026-10-29 21:03:23] CheckSolidWorksVersion: Revision=33.1.1, Estimated Year=2025`
     `[2026-10-29 21:03:23] ConnectToSW ERROR: Specified cast is not valid.`
   - Bukti: Casting parameter `ThisSW` ke `ISldWorks` berhasil sempurna dan pemanggilan `CheckSolidWorksVersion()` menghasilkan nilai `true`. Kegagalan terjadi persis pada baris berikutnya: `_swApp.SetAddinCallbackInfo2(0, this, cookie)`.
2. **Akar Masalah yang Terverifikasi**:
   - **Ketiadaan Implementasi IDispatch pada SwAddIn:**
     Parameter kedua pada `SetAddinCallbackInfo2` didefinisikan dengan atribut marshaling `[MarshalAs(UnmanagedType.IDispatch)] object AddinCallbacks`.
     Kelas `SwAddIn` sebelumnya disetel dengan `[ClassInterface(ClassInterfaceType.None)]` dan hanya mengimplementasikan antarmuka `ISwAddin` (`InterfaceIsIUnknown`).
     Akibatnya, wrapper COM (`CCW`) tidak menyediakan antarmuka `IDispatch`. Saat runtime .NET mencoba melakukan marshaling pointer `this` ke `IDispatch`, pemanggilan internal `QueryInterface(IID_IDispatch)` menghasilkan `E_NOINTERFACE` (0x80004002), yang diangkat oleh CLR sebagai `System.InvalidCastException: Specified cast is not valid`.
   - **Metode Penyematan Task Pane yang Keliru:**
     Pada metode `AddTaskPane()`, implementasi lama memanggil `_taskpaneView.AddControl(hostForm.Handle.ToString(), ADDIN_TITLE)` yang mengharapkan ActiveX ProgID/CLSID. Di API SOLIDWORKS 64-bit, metode resmi untuk menyematkan kontrol Windows Forms/WPF ke dalam task pane adalah `_taskpaneView.DisplayWindowFromHandlex64(handle.ToInt64())`.
3. **Perbaikan Kode (`src/AICADAssistant.AddIn/AddInEntryPoint.cs`)**:
   - Mengubah deklarasi kelas menjadi `[ClassInterface(ClassInterfaceType.AutoDispatch)]`, sehingga CCW secara otomatis menyediakan antarmuka `IDispatch` yang dibutuhkan oleh `SetAddinCallbackInfo2`.
   - Mengirim argumen `ModuleHandle` sebagai Int64 `0L` secara eksplisit dan menambahkan blok fallback ke `SetAddinCallbackInfo` legacy jika terjadi anomali API.
   - Memperbaiki `AddTaskPane()` agar menggunakan `_taskpaneView.DisplayWindowFromHandlex64(handle.ToInt64())`.
   - Menambahkan instrumentasi logging bertahap (`[Stage 1/6]` hingga `[Stage 6/6]`) serta pencatatan stack trace lengkap (`ex.ToString()`) yang otomatis ditulis ke `%APPDATA%\AICADAssistant\addin.log` dan `%LOCALAPPDATA%\AICADAssistant\logs\addin.log`.
4. **Verifikasi**:
   - **Kompilasi MSBuild**: 100% Berhasil (0 Error).
   - **Unit Tests**: 44/44 lolos (100% passed).
   - **Pemeriksaan Refleksi**: `ClassInterfaceType` terverifikasi `AutoDispatch` dan antarmuka `ISwAddin` terverifikasi utuh.

---

## Tahap 12: Implementasi v1.1 — Direct Chat-to-CAD Execution, Geometri Setengah Bola (Hemisphere & Revolve), Perbaikan Input Keyboard, dan Verifikasi Geometri Lengkap

1. **Diagnosis & Perbaikan Geometri Setengah Bola (Hemisphere)**:
   - Kasus aktual: Perintah pengguna "buat bentuk setengah bola diameter 100 mm" sebelumnya menghasilkan nilai 0 atau tidak membentuk geometri.
   - Akar masalah:
     - `OperationType.RevolvedBoss` sebelumnya belum ditangani dalam blok `switch (op.Type)` pada `SolidWorksAutomationService.ExecuteSpec` (jatuh ke `default:` tanpa mengeksekusi API SOLIDWORKS).
     - Tidak ada validasi jumlah solid body (`SolidBodyCount == 0` dilaporkan sukses palsu).
   - Solusi Geometris yang Diterapkan:
     - Menambahkan operasi resmi `CreateHemisphere = 17` ke dalam `OperationType` dan `AllowedOperations.Whitelist`.
     - Implementasi algoritma pemodelan native di `SolidWorksAutomationService.CreateHemisphere`:
       - Membuka sketch pada bidang yang dipilih (default `Front Plane`).
       - Menggambar busur seperempat lingkaran (`CreateArc`) berpusat di origin $(0,0,0)$ dari $(R,0,0)$ ke $(0,R,0)$ dengan arah berlawanan jarum jam (CCW, direction = +1).
       - Menggambar garis dasar horizontal (`CreateLine`) dari $(0,0,0)$ ke $(R,0,0)$.
       - Menggambar garis sumbu putar vertikal (`CreateLine`) dari $(0,R,0)$ ke $(0,0,0)$.
       - Memilih garis sumbu putar dengan Mark = 4 (mark seleksi resmi API SOLIDWORKS untuk *Axis of Revolution*).
       - Mengeksekusi `FeatureRevolve2` 360° ($2\pi$ radian) untuk membentuk benda padat sempurna (1 solid body, diameter dasar 100 mm, tinggi 50 mm, radius 50 mm, dasar lingkaran datar).
     - Menghitung volume analitik dan terbaca via `GetMassProperties2`:
       $$V = \frac{2}{3} \pi R^3 = \frac{2}{3} \pi (50)^3 \approx 261.799,39\text{ mm}^3$$
     - Menolak pelaporan sukses jika `SolidBodyCount <= 0` dengan pesan error diagnostik yang jelas.

2. **Direct Chat-to-CAD Execution**:
   - Menghilangkan keharusan pengguna mengklik tombol manual "Buat Model".
   - Alur baru di `MainViewModel.SendMessageAsync`:
     - Pengguna mengirim instruksi pemodelan/revisi melalui chat.
     - Respons Gemini di-parsing ke `CadSpec` dan divalidasi keamanannya via `CadPlanValidator`.
     - Jika ada dimensi wajib yang hilang/ambigu, asisten langsung menanyakan klarifikasi spesifik di chat tanpa mengeksekusi.
     - Jika spesifikasi valid, sistem langsung mengeksekusi pemodelan di SOLIDWORKS secara otomatis (`ExecuteSpecDirectlyAsync`).
     - Hasil geometri aktual dilaporkan lengkap di riwayat percakapan: nama dokumen, nama fitur yang terbentuk, status rebuild, jumlah solid body, dimensi bounding box ($X \times Y \times Z\text{ mm}$), volume benda kerja ($\text{mm}^3$), waktu eksekusi, serta path berkas tersimpan dan ekspor.

3. **Perbaikan Input Keyboard pada Chat TextBox**:
   - Gejala: Input teks langsung di `InputTextBox` pada Task Pane tidak merespons ketikan tombol (hanya paste Ctrl+V yang berfungsi) karena pintasan keyboard SOLIDWORKS (akselerator seperti 'S', 'F', 'L') mencegat pesan keyboard Win32 sebelum diteruskan ke visual tree WPF di dalam unmanaged window.
   - Solusi Interop yang Diterapkan:
     - Membuat kelas `SafeElementHost : ElementHost` dan `TaskPaneWinFormsHost : UserControl` di `AddInEntryPoint.cs`.
     - Menangani pesan Win32 `WM_GETDLGCODE` (0x0087) dengan mengembalikan `DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB` (0x0087).
     - Meng-override `IsInputKey` dan `IsInputChar` agar bernilai `true` untuk semua input karakter.
     - Pada WPF `MainTaskPaneControl.xaml`: Menambahkan `Focusable="True"`, `IsTabStop="True"`, `PreviewMouseDown`, dan `PreviewKeyDown`.
     - Pada `MainTaskPaneControl.xaml.cs`:
       - Event `Loaded` memanggil `Dispatcher.BeginInvoke` dengan prioritas `Input` untuk merebut keyboard focus secara andal.
       - Event `PreviewMouseDown` pada `InputTextBox` memastikan klaim `Keyboard.Focus(InputTextBox)`.
       - Event `PreviewKeyDown`: Menekan `Enter` mengirim pesan langsung, `Shift+Enter` menyisipkan baris baru (*newline*), dan `Ctrl+Enter` mengirim pesan.

4. **Keandalan & Pengujian Regresi**:
   - Menambahkan 8 unit test baru (Total: 52 unit test):
     - `Validate_CreateHemisphere_ValidDiameter_ReturnsValid`
     - `Validate_CreateHemisphere_ValidRadius_ReturnsValid`
     - `Validate_CreateHemisphere_ZeroOrNegativeDiameter_ReturnsInvalid`
     - `Validate_CreateHemisphere_MissingDimensions_ReturnsInvalid`
     - `Validate_RevolvedBoss_ValidAngle_ReturnsValid`
     - `Validate_RevolvedBoss_NegativeAngle_ReturnsInvalid`
     - `ParseCadSpec_HemisphereJson_ParsesCorrectly`
     - `Hemisphere_VolumeCalculation_MatchesAnalyticalFormula`
   - Hasil pengujian: **52/52 Lolos (100% Passed, 0 Gagal)** dalam 79 ms.
   - Kompilasi solusi x64: **100% Berhasil (0 Error)**.









---

## Tahap 13: Critical Fix v1.1.1 — SOLIDWORKS Runtime, Direct Execution, Interop Keyboard Input, & Settings UI (2026-10-29)

### 1. Perbaikan Input Keyboard Melalui Win32 HwndSource Hook & ComVisible
- **Akar Masalah**: SOLIDWORKS MFC main message pump menjalankan `::IsDialogMessage` pada HWND anak yang sedang memiliki fokus sebelum memproses akselerator keyboard aplikasi. Kontrol WPF di dalam WinForms `ElementHost` berada di HWND terpisah (`HwndSource`). Jika HWND anak yang fokus tidak secara eksplisit menangkap dan merespons `WM_GETDLGCODE` (0x0087), `::IsDialogMessage` mengabaikan tombol ketik biasa (seperti 's', 'f', 'l') dan SOLIDWORKS menginterpretasikannya sebagai hotkey/akselerator CAD.
- **Implementasi Solusi**:
  - Membuat `InteropTextBox` (`src/AICADAssistant.AddIn/UI/Controls/InteropTextBox.cs`) yang merupakan turunan dari WPF `TextBox` dengan atribut `[ComVisible(true)]`.
  - Mengaitkan `HwndSourceHook` pada `HwndSource` milik TextBox dan `MainTaskPaneControl` untuk menangani pesan `WM_GETDLGCODE` (0x0087) secara native: mengembalikan `DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB` (0x0087).
  - Memberi atribut `[ComVisible(true)]` dan `[ClassInterface(ClassInterfaceType.AutoDispatch)]` pada `SafeElementHost`, `TaskPaneWinFormsHost`, dan `MainTaskPaneControl` sesuai panduan resmi interop SOLIDWORKS / CodeStack.
  - Meng-override `ProcessCmdKey` pada `TaskPaneWinFormsHost` untuk mencegah WinForms menelan atau mengalihkan pesan keyboard.

### 2. Direct Chat-to-CAD Execution & STA Thread Safety
- **Akar Masalah**: Panggilan API SOLIDWORKS COM (`doc.FeatureManager`, `doc.SketchManager`, dsb.) harus dieksekusi pada Single-Threaded Apartment (STA) main thread SOLIDWORKS. Menjalankan eksekusi CAD di dalam `await Task.Run(...)` menyebabkan pemanggilan COM lintas apartemen (cross-apartment marshaling) yang memicu kegagalan COM atau eksekusi tidak stabil.
- **Implementasi Solusi**:
  - Pada `MainViewModel.SendMessageAsync`, setelah respons Gemini di-parse menjadi `CadSpec` dan divalidasi keamanannya oleh `CadPlanValidator`, sistem langsung memanggil `ExecuteSpecDirectly(spec)` secara sinkron pada thread STA SOLIDWORKS UI.
  - Menghilangkan keharusan mengklik tombol "Buat Model". Tombol kontekstual diubah menjadi fungsi sekunder opsional: "⚙ Buat Ulang Model".
  - Hasil geometri aktual (nama dokumen, fitur, status rebuild, jumlah solid body, bounding box, volume, waktu eksekusi) langsung dilaporkan ke bubble chat asisten.

### 3. Penyederhanaan UI Pengaturan (Settings)
- Menghapus input "Batas Waktu (detik)" dan "Maksimal Percobaan" dari antarmuka dialog pengaturan sesuai instruksi pengguna. Nilai default 120 detik dan 3 kali retry tetap dipertahankan secara internal di `AppSettings.cs`.
- Menghapus tombol "Uji Koneksi" dari `SettingsWindow.xaml`.
- Mengatur tinggi jendela pengaturan menjadi ringkas (420 px).

### 4. Dynamic Version Indicator & Startup Diagnostics
- Memperbarui versi assembly ke `1.1.1.0` / `1.1.1` pada:
  - `src/AICADAssistant.AddIn/Properties/AssemblyInfo.cs`
  - `src/AICADAssistant.Core/Properties/AssemblyInfo.cs`
  - `src/AICADAssistant.Tests/Properties/AssemblyInfo.cs`
- Menambahkan properti dinamis `AppVersionDisplay` di `MainViewModel.cs` yang membaca versi langsung dari assembly runtime (`v1.1.1`).
- Menampilkan badge versi dinamis di Header bar (`<Border Background="#C62828">...`) dan Footer status bar di `MainTaskPaneControl.xaml`.
- Menambahkan pencatatan log diagnostik saat inisialisasi add-in di `AddInEntryPoint.cs` yang mencatat path absolut file DLL dan timestamp waktu kompilasi file DLL ke `aicad_addin_startup.log`.

### 5. Hasil Pengujian & Kompilasi
- **Unit Test**: 53 unit test (termasuk tes verifikasi default timeout) lolos 100% (**53/53 PASSED, 0 GAGAL**).
- **Kompilasi MSBuild**: Berhasil 100% (0 Error, 0 Warning kode).
- **COM Activation Test**: Berhasil menginstansiasi class `AICADAssistant.AddIn.SwAddIn` secara native melalui COM runtime.


---

## Tahap 14: Critical Fix v1.1.2 — Penanganan Timeout Gemini, Diagnostic Logging, Input Win32 Native, & Direct Execution (2026-10-30)

### 1. Investigasi & Solusi Timeout Gemini (Pesan 9000 Detik Terpecahkan)
- **Akar Masalah**:
  1. Pada berkas `%APPDATA%\AICADAssistant\settings.json`, nilai `RequestTimeoutSeconds` tersimpan `9000` dan `MaxRetryAttempts` tersimpan `10`.
  2. Model `gemini-3.8-flash` pada server publik Google secara berkala mengalami lonjakan beban tinggi (HTTP `503 Server Unavailable`: *"This model is currently experiencing high demand"*).
  3. Saat menerima error 503, `GeminiClient` lama melakukan retry hingga 10 kali dengan backoff kumulatif > 170 detik.
  4. Karena default `HttpClient.Timeout` pada .NET Framework adalah 100 detik, `HttpClient` memutus koneksi di detik ke-100 dan melempar `TaskCanceledException`.
  5. Blok penanganan exception menangkap pembatalan tersebut dan melempar `GeminiException.TimeoutException(_settings.RequestTimeoutSeconds)` yang menyisipkan angka `9000` detik ke pesan error di UI!
- **Implementasi Solusi**:
  - Menetapkan batas aman pada `AppSettings.Load()`: jika `RequestTimeoutSeconds < 30` atau `> 300`, di-clamp ke `120` detik. Jika `MaxRetryAttempts < 0` atau `> 3`, di-clamp ke `2`. Berkas `settings.json` di disk langsung diperbarui secara otomatis.
  - Menyetel `HttpClient.Timeout = TimeSpan.FromSeconds(180)` agar per-request `CancellationToken` (120 detik) mengendalikan timeout tanpa interupsi premature dari `HttpClient`.
  - Menambahkan penanganan khusus HTTP 503 (`UNAVAILABLE`): jika model aktif (`gemini-3.8-flash`) terkena 503, `GeminiClient` otomatis beralih mencoba model fallback `gemini-3.5-flash-lite` yang terbukti stabil dan cepat merespons (sukses dalam 15-20 detik).
  - Jika 503 berlanjut, sistem melempar `GeminiException.ServerUnavailableException` (bukan timeout generik) dengan pesan jelas: *"Server Gemini sedang mengalami lonjakan beban sementara (503 High Demand). Silakan coba lagi beberapa saat atau pilih model alternatif."*

### 2. Penambahan Diagnostic Logging Lengkap di Sepanjang Pipeline Chat-to-CAD
- Menambahkan metode `LoggingService.Instance.LogPipeline` yang mencatat setiap tahap secara detail dan terstruktur ke `%APPDATA%\AICADAssistant\logs\aicad_{yyyyMMdd}.log` dan menduplikasikannya ke berkas utama `%APPDATA%\AICADAssistant\addin.log`:
  1. `STAGE_1_CHAT_RECEIVED`: Waktu terima pesan, panjang karakter, dan preview teks aman.
  2. `STAGE_2_DIRECT_EXECUTION`: Inisiasi pipeline direct execution dan model Gemini aktif.
  3. `STAGE_3_GEMINI_REQUEST`: Pengiriman request ke Gemini, panjang prompt, dan riwayat chat.
  4. `STAGE_4_GEMINI_RESPONSE`: Waktu respons diterima (dalam ms), status HTTP, dan panjang respons.
  5. `STAGE_5_CAD_PARSE`: Hasil parsing respons ke `CadSpec` (RequestId, jumlah operasi).
  6. `STAGE_6_CAD_VALIDATE`: Validasi semantik geometri via `CadPlanValidator` (valid / daftar dimensi yang hilang).
  7. `STAGE_7_CAD_EXECUTOR`: Pemanggilan `SolidWorksAutomationService.ExecuteSpec` sinkron pada thread STA SOLIDWORKS.
  8. `CAD_RESULT`: Dokumen aktif, nama fitur, status rebuild, jumlah solid body, dimensi bounding box ($X \times Y \times Z\text{ mm}$), volume benda kerja ($\text{mm}^3$), dan durasi eksekusi (ms).
  9. `STAGE_COMPLETE`: Pelaporan status akhir ke pengguna di chat.

### 3. Solusi Native Win32 Keyboard Input Interop (`WinFormsChatTextBox`)
- **Akar Masalah**: SOLIDWORKS adalah aplikasi native C++/MFC. Message pump utamanya memeriksa `::GetFocus()`. Jika kelas jendela yang sedang aktif bukan kontrol `"EDIT"`, MFC menjalankan `::TranslateAccelerator` pada seluruh penekanan tombol karakter (`S`, `L`, `F`, spasi, angka), sehingga karakter ditelan menjadi hotkey CAD dan `WM_CHAR` tidak pernah sampai ke visual tree WPF. Kontrol WPF murni tidak memiliki HWND Win32 sendiri (hanya digambar di atas surface `HwndSource`).
- **Implementasi Solusi**:
  - Dibuat kontrol native Win32 `WinFormsChatTextBox : System.Windows.Forms.TextBox` (`src/AICADAssistant.AddIn/UI/Controls/WinFormsChatTextBox.cs`) dengan atribut `[ComVisible(true)]`.
  - Kontrol ini memiliki Win32 HWND sejati turunan kelas Win32 `"EDIT"`, sehingga MFC SOLIDWORKS mengenalinya sebagai kontrol input teks dan **tidak pernah membajak akselerator keyboard**.
  - Dihosting di WPF melalui `<wfi:WindowsFormsHost x:Name="InputHost">` pada `MainTaskPaneControl.xaml`.
  - Menangani pesan `WM_GETDLGCODE` mengembalikan `0x0087` (`DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB`).
  - Mengatur `ProcessCmdKey`: tombol `Enter` memicu pengiriman pesan chat, `Shift+Enter` menyisipkan baris baru (*newline*), dan `Ctrl+Enter` memicu pengiriman chat.
  - Sinkronisasi dua arah (*two-way*) penuh dengan `ViewModel.UserInput` dan tampilan placeholder teks.

### 4. Hasil Kompilasi & Pengujian
- **Unit Tests**: **56/56 Lolos (100% Passed, 0 Gagal)** dalam 139 ms.
- **Kompilasi Solusi MSBuild x64**: **100% Berhasil (0 Error)**.
- **Aktivasi COM**: Berhasil memuat assembly versi `1.1.2.0` dari path absolut `bin\Debug\AICADAssistant.AddIn.dll`.

---

## Tahap 15: Critical Fix v1.1.3 — Enter Submission, Deterministic Fast Path, Zero Timeout, & Latency Acceleration (2026-10-30)

### 1. Perbaikan Pengiriman Pesan Menggunakan Tombol Enter
- **Akar Masalah**:
  - Pada kontrol multiline `System.Windows.Forms.TextBox`, implementasi default `IsInputKey` mengembalikan `true` untuk tombol `Keys.Return` agar TextBox dapat menyisipkan baris baru (`\r\n`). Akibatnya, `ProcessCmdKey` sering dilewati oleh sistem kontrol WinForms sebelum event dapat dicegat.
  - Selain itu, event `OnKeyPress` bawaan tetap menyisipkan karakter newline ke dalam teks input jika tidak ditekan dengan penanganan tombol yang ketat.
- **Implementasi Solusi**:
  - Pada `WinFormsChatTextBox.cs`, menambahkan override eksplisit pada:
    1. `OnKeyDown`: jika `e.KeyCode == Keys.Enter && !e.Shift`, atur `e.SuppressKeyPress = true`, `e.Handled = true`, dan panggil `TriggerSubmit()`. Jika `e.Shift == true`, teruskan ke `base.OnKeyDown` untuk menyisipkan newline normal.
    2. `OnKeyPress`: cegat tombol `(char)Keys.Enter` saat `!ModifierKeys.HasFlag(Keys.Shift)` dengan `e.Handled = true` untuk mencegah bunyi beep sistem dan mencegah penambahan karakter liar.
    3. `WndProc`: tangani pesan `WM_GETDLGCODE` (0x0087) dengan mengembalikan `DLGC_WANTALLKEYS | DLGC_WANTCHARS | DLGC_WANTARROWS | DLGC_WANTTAB`. Tangani pula pesan langsung `WM_KEYDOWN` (0x0100) untuk kode `VK_RETURN` (0x0D) tanpa tombol Shift sebagai pengaman sekunder.
    4. Menyertakan mekanisme debounce 250 ms (`_lastSubmitTime`) untuk mencegah pengiriman pesan ganda saat penekanan tombol cepat.
  - Pada `MainTaskPaneControl.xaml.cs`:
    - Event handler `OnChatBoxSubmitRequested` menyinkronkan teks yang telah di-trim ke `ViewModel.UserInput`, memeriksa keberadaan teks, lalu mengeksekusi `ViewModel.SendMessageCommand.Execute(null)`.
    - Jalur eksekusi penekanan tombol `Enter` identik 100% dengan klik tombol `Kirim`.

### 2. Penghapusan Batas Waktu Artifisial (Artificial 120s Timeout)
- **Akar Masalah**:
  - Pembuatan model CAD kompleks atau respons AI dengan thinking process dapat memerlukan waktu yang bervariasi.
  - Adanya timeout tetap 120 detik pada `CancellationTokenSource(TimeSpan.FromSeconds(120))` di dalam `GeminiClient` memotong permintaan pengguna secara sepihak dan menimbulkan pesan error yang membingungkan.
- **Implementasi Solusi**:
  - Mengatur `HttpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan`.
  - Menghapus pembuatan `timeoutCts` berdurasi 120 detik di dalam `GeminiClient.ExecuteRequestAsync`. Pembatalan permintaan HTTP kini dikendalikan sepenuhnya oleh `CancellationToken` dari aksi manual pengguna (tombol "✖ Batalkan").
  - Menambahkan timer visual responsif di `MainViewModel.cs` menggunakan `DispatcherTimer`: selama proses pembuatan CAD berjalan (`IsBusy`), pesan status memperlihatkan detik berjalan secara langsung: `Menyusun Rencana CAD... (X dtk)`.

### 3. Akselerasi Latensi Gemini & Fallback Kuota
- **Optimasi Model & Token**:
  - Mengubah model default ke `gemini-3.5-flash-lite` dengan konfigurasi `thinkingConfig: { thinkingLevel: "minimal" }` yang terbukti secara empiris merespons dalam ~7 detik.
  - Untuk model `gemini-3.8-flash`, menyetel `thinkingLevel: "low"`.
  - Melakukan pruning pada riwayat percakapan (`SendTextAsync`): hanya 4 pesan terakhir yang disertakan dalam payload untuk mencegah token bloat.
  - Menangani error HTTP 429 (`ResourceExhausted`): jika model aktif kehabisan kuota harian (seperti limit 20 req/day pada tier gratis), sistem otomatis fallback ke `gemini-3.5-flash-lite`.

### 4. Deterministic Fast Path Engine (`CadFastPathEngine.cs`)
- **Fitur Baru**:
  - Membuat engine parser deterministik berbasis regex untuk perintah geometri standar eksplisit:
    - Setengah bola / hemisphere padat: dimensi diameter/radius terdeteksi -> menghasilkan operasi `CreateHemisphere` (Type 17).
    - Pelat / balok / kubus padat: dimensi $P \times L \times T$ terdeteksi -> menghasilkan `NewSketch`, `AddRectangle`, `ExtrudeBoss`.
    - Silinder padat: dimensi diameter dan tinggi terdeteksi -> menghasilkan `NewSketch`, `AddCircle`, `ExtrudeBoss`.
    - Pelat dengan lubang tembus: dimensi pelat dan diameter lubang terdeteksi -> menghasilkan `NewSketch`, `AddRectangle`, `ExtrudeBoss`, `SimpleHole`.
  - Jika perintah eksplisit dan lengkap: langsung dibuat `CadSpec` secara lokal (< 10 milidetik, 0 panggilan API, 0 kuota terpakai).
  - Jika perintah memuat kata tentatif/revisi ("mungkin", "kira-kira", "atau", "ubah", "revisi"), Fast Path menolak dan menyerahkan ke Gemini AI.

### 5. Hasil Kompilasi & Pengujian
- **Unit Tests**: **64/64 Lolos (100% Passed, 0 Gagal)** dalam 235 ms:
  - `CadPlanValidatorTests`: 17 passed.
  - `CadFastPathEngineTests`: 8 passed.
  - `UnitConverterTests`: 15 passed.
  - `CadSpecParsingTests`: 6 passed.
  - `GeminiClientTests`: 14 passed.
  - `BackupServiceTests`: 4 passed.
- **Kompilasi Solusi MSBuild x64**: **100% Berhasil (0 Error, 0 Warning kode)**.
- **Aktivasi COM**: Berhasil memuat assembly versi `1.1.3.0` dari path absolut `bin\Debug\AICADAssistant.AddIn.dll` (`AICADAssistant.AddIn.SwAddIn`).

---

## Tahap 16: Critical Fix v1.1.4 — Hemispherical Primitive Fast Path, Contextual Bypass Fix, & Clean Single Solid Execution (2026-10-30)

### 1. Investigasi & Akar Masalah Geometri Setengah Bola
- **Akar Masalah Utama (Contextual Bypass)**:
  - Pada `MainViewModel.cs` baris 243 sebelumnya, kondisi pemanggilan Fast Path dibatasi oleh `_currentCadSpec == null`:
    `if (_currentCadSpec == null && CadFastPathEngine.TryCreateSpec(text, out fastSpec))`
  - Ketika pengguna berhasil menjalankan pengujian pertama (Uji 1 berhasil), `_currentCadSpec` terisi dengan model sebelumnya.
  - Saat pengguna mengirimkan perintah berikutnya: *"Buat setengah bola pejal dengan diameter 100 mm."*, pengecekan `_currentCadSpec == null` bernilai `false`, sehingga sistem **melewatkan Fast Path** dan mengalirkan permintaan ke `BuildPromptForRevision` menuju Gemini AI.
  - Prompt revisi lama tidak memuat daftar whitelist operasi dan instruksi khusus `CreateHemisphere` (Type 17), sehingga Gemini mencoba menyusun sendiri operasi berupa `AddRectangle` (`profile_hemi`) dan `ExtrudeCut` (`rev1`) tanpa parameter numerik `width`, `height`, dan `depth`.
  - `CadPlanValidator` kemudian menolak CAD plan tersebut dengan pesan:
    - `profile_hemi (AddRectangle): parameter width tidak ditemukan atau bukan angka valid.`
    - `profile_hemi (AddRectangle): parameter height tidak ditemukan atau bukan angka valid.`
    - `rev1 (ExtrudeCut): parameter depth tidak ditemukan atau bukan angka valid.`
- **Akar Masalah Kedua (Word Boundary Filter & Pattern Matching)**:
  - Filter pengaman lama memeriksa kata `"ubah"` dengan `clean.Contains("ubah")`. Kata `"kubah"` secara tidak sengaja memicu filter tersebut karena mengandung substring `"ubah"`.
  - Pola ekstraksi diameter lama belum memanfaatkan boundary kata `\b` secara menyeluruh, sehingga kata seperti *"dengan"* atau *"dan"* dapat berpotensi memengaruhi deteksi huruf `d`.

### 2. Implementasi Perbaikan
- **`CadFastPathEngine.cs`**:
  - Memperbaiki `TryCreateSpec` agar memfilter kata tentatif/revisi menggunakan word boundary regex:
    `\b(?:kira-kira|mungkin|sekitar|atau|revisi|ubah|tambahkan|hapus|ganti)\b` sehingga kata *"kubah"* tetap diizinkan.
  - Memperbarui `TryMatchHemisphere`:
    - Mengenali kata kunci: `setengah bola`, `setengah-bola`, `setengahbola`, `hemisphere`, `semi sphere`, `semisphere`, `kubah`, `half sphere`.
    - Pola Diameter: `(?:(?:\b(?:diameter|diam|dia)\b|[øØ]|\bd\b)\s*[:=]?\s*|\bd(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?`
    - Pola Radius: `(?:(?:\b(?:radius|jari-jari|jari|rad)\b|\br\b)\s*[:=]?\s*|\br(?=\d))(\d+(?:[.,]\d+)?)\s*(mm|cm|m|in(?:ch)?)?`
    - Normalisasi otomatis satuan (`cm`, `m`, `in`) ke milimeter internal.
    - Menghasilkan operasi `OperationType.CreateHemisphere` (Type 17) dengan parameter `diameter` dan `plane: Front`.
- **`MainViewModel.cs`**:
  - Menghapus pembatas `_currentCadSpec == null` pada pemanggilan `CadFastPathEngine.TryCreateSpec`. Seluruh perintah primitif eksplisit langsung dieksekusi secara instan (< 10 ms) tanpa memandang status riwayat model sebelumnya.
- **`CadPlanner.cs`**:
  - Menambahkan whitelist lengkap `OperationType` dan instruksi tegas larangan menggunakan `AddRectangle`/`ExtrudeCut` kosong untuk setengah bola pada `BuildPromptForRevision` dan `BuildPromptForTextRequest`.
- **`SolidWorksAutomationService.cs`**:
  - Pada eksekusi `CreateHemisphere`: jika dokumen aktif telah memiliki solid body (`GetSolidBodyCount(activeDoc) > 0`), otomatis dibuat part document baru (`CreateNewPartDocument`) agar setengah bola terbentuk sebagai **satu solid body pejal mandiri** tanpa risiko tabrakan atau penggabungan geometri dari model sebelumnya.
  - Pada `OpenSketchOnPlane`: memastikan sketch aktif sebelumnya ditutup terlebih dahulu sebelum membuka sketch pada bidang baru.

### 3. Pengujian & Verifikasi
- **Unit Tests**: **68/68 Lolos (100% Passed, 0 Gagal)** dalam 583 ms:
  - Tes frasa persis yang gagal: *"Buat setengah bola pejal dengan diameter 100 mm."* (dengan tanda titik di akhir).
  - Tes 10 variasi frasa, kapitalisasi, tanda baca, simbol diameter (Ø), radius, dan kubah.
  - Tes konversi satuan (cm ke mm).
  - Tes penolakan diameter 0 atau negatif.
  - Tes verifikasi bahwa tidak ada `AddRectangle` atau `ExtrudeCut` liar dalam CAD plan.
- **Kompilasi Solusi MSBuild x64**: **100% Berhasil (0 Error, 0 Warning kode)**.
- **Aktivasi COM**: Assembly versi **`1.1.4.0`** (`AICADAssistant.AddIn.SwAddIn`) terverifikasi aktif melalui CLSID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.

---

## Tahap 17: Bugfix v1.1.5 — Investigasi Kegagalan CAD Executor, Diagnostic Logging, Strict Dependency Control, & Integritas Status Pipeline (2026-10-30)

### 1. Investigasi & Akar Masalah CAD_RESULT FAILED [0 ms] dan STAGE_COMPLETE -> SUCCESS
- **Akar Masalah Durasi 0 ms dan Informasi Error Hilang**:
  - Pada `SolidWorksAutomationService.cs` (`ExecuteSpec`), ketika salah satu operasi gagal (`!opRes.Success`), kode langsung melakukan `return execResult;`.
  - Pada blok return dini tersebut, `stopwatch.Stop()` tidak dipanggil dan `execResult.DurationMs` tidak disetel (tetap default `0`).
  - Properti `execResult.DocumentTitle`, `execResult.SolidBodyCount`, `execResult.RebuildSuccess`, dan `execResult.VolumeMm3` tidak diisi.
  - Di `MainViewModel.cs`, pemanggilan `LogPipeline("CAD_RESULT", ...)` hanya memformat properti-properti di atas tanpa menyertakan `result.ErrorMessage` atau `result.FailedOperation`. Akibatnya log hanya mencatat:
    `CAD_RESULT -> FAILED [0 ms]: Doc='', Feature='', Bodies=0, Rebuild=False, Vol=0.00 mm3` sehingga pesan error spesifik dari operasi tersembunyi.
- **Akar Masalah Kontradiksi Status Pipeline (`STAGE_COMPLETE -> SUCCESS`)**:
  - Di `MainViewModel.cs` (`SendMessageAsync`), setelah `ExecuteSpecDirectly(spec)` dijalankan, log secara tidak bersyarat mencatat:
    `LoggingService.Instance.LogPipeline("STAGE_COMPLETE", "SUCCESS", "Pipeline Chat-to-CAD selesai.", ...);`
  - Hal ini menyebabkan sistem melaporkan pipeline `SUCCESS` meskipun `CAD_RESULT` berstatus `FAILED`!
- **Akar Masalah Kegagalan Pemodelan Kunci Pas & Tangkai pada Setengah Bola**:
  1. *Operasi Tidak Didukung dalam Switch*:
     Operasi seperti `AddLine` (Type 2) ada dalam whitelist validator, tetapi pada `SolidWorksAutomationService.ExecuteSpec` tidak memiliki case penanganan. Operasi tersebut jatuh ke blok `default:` yang secara salah mengembalikan `opRes = OperationResult.Succeeded(op.Id)` tanpa menggambar garis apa pun. Saat operasi `ExtrudeBoss` dipanggil, sketsa kosong/terbuka sehingga `FeatureExtrusion3` menghasilkan `null` dan gagal.
  2. *Parameter Pusat (x, y) Diabaikan*:
     Pada `AddCircle` dan `AddRectangle`, nilai `x` dan `y` diabaikan dan titik pusat di-hardcode ke `(0, 0)`. Pada model kunci pas (gagang + kepala), persegi panjang dan lingkaran digambar bertumpukan di titik pusat yang sama di dalam satu sketsa, sehingga `FeatureExtrusion3` gagal mengekstrusi kontur yang saling berpotongan (self-intersecting contours).
  3. *Ketiadaan Pengecekan Dependensi (`DependsOn`)*:
     `ExecuteSpec` lama tidak pernah memverifikasi apakah operasi yang didefinisikan dalam `op.DependsOn` berhasil dieksekusi atau tidak.
  4. *Ketiadaan Sketsa Aktif*:
     Operasi pemotongan atau penambahan fitur kedua dipanggil tanpa memeriksa apakah sketsa aktif telah terbuka.

### 2. Implementasi Perbaikan
- **`SolidWorksAutomationService.cs`**:
  - Menambahkan *per-operation diagnostic logging* sebelum (`[OP_START]`) dan sesudah (`[OP_SUCCESS]` / `[OP_FAILED]`) setiap operasi dieksekusi, memuat RequestId, indeks operasi, tipe, nama, parameter lengkap, dependensi, dan feature yang dihasilkan.
  - Menambahkan penanganan blok `catch (Exception opEx)` per operasi yang mencatat tipe exception, pesan, dan stack trace ke log dengan level `[OP_EXCEPTION]`.
  - Mengimplementasikan pengecekan rantai dependensi (`DependsOn`): jika ada dependensi yang belum selesai atau gagal, operasi berikutnya otomatis dihentikan dengan status `[OP_DEPENDENCY_FAILED]`.
  - Mengimplementasikan `DrawLine` (`doc.SketchManager.CreateLine`), overload `DrawCenterRectangle` dengan koordinat pusat `centerXm, centerYm`, serta membaca parameter `x` dan `y` pada `AddCircle` dan `AddRectangle`.
  - Menangani pembukaan sketsa otomatis jika operasi entitas sketsa (`AddLine`, `AddRectangle`, `AddCircle`) dipanggil tanpa sketsa aktif.
  - Memperbaiki `OpenSketchOnPlane` agar memvalidasi hasil pemilihan bidang (`selected`) dan memastikan `ActiveSketch != null`.
  - Pada blok `default:` di switch operasi, operasi yang belum didukung tidak lagi ditelan sebagai sukses, melainkan dilaporkan secara eksplisit: *"Operasi 'X' dengan tipe 'Y' belum didukung oleh CAD executor."*
  - Saat terjadi kegagalan pada operasi apa pun: `stopwatch.Stop()` dipanggil, `DurationMs` dihitung, metadata dokumen diisi, dan pesan error spesifik dimasukkan ke `execResult.ErrorMessage`.
  - Memastikan *Strict Solid Body Check*: jika semua operasi selesai tetapi `SolidBodyCount <= 0`, eksekusi ditandai gagal.
- **`MainViewModel.cs`**:
  - Mengubah `ExecuteSpecDirectly` agar mengembalikan nilai `bool` (sukses/gagal).
  - Menyertakan detail `result.ErrorMessage` ke dalam log `CAD_RESULT`.
  - Memperbaiki pencatatan `STAGE_COMPLETE`: hanya mencatat `SUCCESS` jika eksekusi geometri benar-benar sukses dan menghasilkan solid body. Jika gagal, mencatat `STAGE_COMPLETE -> FAILED` dengan durasi aktual.
  - Menampilkan pesan error spesifik yang dapat ditindaklanjuti pada panel UI dan riwayat chat.
- **`CadPlanner.cs`**:
  - Menambahkan pedoman pemodelan SOLIDWORKS pada prompt teks dan revisi: wajib `NewSketch` sebelum fitur 3D, larangan kontur bertumpukan di sketsa yang sama tanpa kontur terpisah, dan penggunaan parameter `x` serta `y`.
- **`LoggingService.cs`**:
  - Menambahkan alias metode `Warn(string message)` untuk kompatibilitas.
- **`AICADAssistant.Tests`**:
  - Menambahkan `CadExecutorTests.cs` (7 unit test baru): pengujian kegagalan executor, pengujian zero solid bodies, pengujian abort dependensi gagal, pengujian operasi tidak didukung, pengujian fast path pelat dan setengah bola, serta logging pipeline error.

### 3. Hasil Kompilasi & Pengujian
- **Unit Tests**: **75/75 Lolos (100% Passed, 0 Gagal)** dalam 210 ms.
- **Kompilasi MSBuild x64**: **100% Berhasil (0 Error, 0 Warning kode)**.
- **Aktivasi COM**: Assembly versi **`1.1.5.0`** (`AICADAssistant.AddIn.SwAddIn`) terverifikasi aktif melalui CLSID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.

---

## Tahap 18: Bugfix v1.1.6 — Context-Aware CAD Revision, Base Feature Preservation, Planar Face Selection, & Add Feature Robustness (2026-10-30)

### 1. Investigasi & Akar Masalah Kegagalan Penambahan Tangkai pada Setengah Bola
- **Bukti Kegagalan**:
  - Pengguna membuat setengah bola diameter 100 mm (`Buat setengah bola pejal dengan diameter 100 mm.`) -> Sukses (Part1, 1 body, volume 261.799,39 mm³).
  - Pengguna kemudian meminta: `Tambahkan tangkai silinder diameter 20 mm panjang 100 mm di tengah bola.`
  - Eksekusi gagal pada operasi 1: `hemi1 (CreateHemisphere): Gagal menginisialisasi dokumen untuk pembuatan setengah bola.`
- **Akar Masalah Utama**:
  1. *Prompt Revisi Menginstruksikan Gemini untuk Tidak Menghapus Fitur Lama*:
     Prompt `CadPlanner.BuildPromptForRevision` menginstruksikan *"Jangan menghapus fitur yang sudah ada"*, sehingga Gemini menyertakan ulang `hemi1 (CreateHemisphere)` sebagai operasi pertama dalam rencana revisi.
  2. *CreateHemisphere Mencoba Membuat Dokumen Baru pada Part Aktif*:
     Pada `SolidWorksAutomationService.cs`, `case OperationType.CreateHemisphere` memiliki logika:
     `if (activeDoc == null || GetSolidBodyCount(activeDoc) > 0) activeDoc = CreateNewPartDocument();`
     Karena part aktif sudah memiliki 1 solid body setengah bola, executor mencoba membuat dokumen part baru. Pembuatan dokumen baru gagal atau tidak diinginkan, menyebabkan kegagalan operasi pertama dan membatalkan seluruh operasi berikutnya.
  3. *Ketiadaan Pemahaman Konteks Model Aktif*:
     Sistem sebelumnya tidak membedakan antara **Create New Model** dan **Modify Existing Model**. Tidak ada mekanisme rekonsiliasi yang menandai bahwa `hemi1` telah selesai dieksekusi pada model aktif, sehingga operasi penambahan tangkai yang bergantung pada `hemi1` tidak dapat dieksekusi secara independen.
  4. *Ketiadaan Dukungan Pemilihan Planar Face pada Geometri 3D*:
     Untuk menambahkan tangkai pada dasar setengah bola, sketsa harus dibuka pada permukaan datar (*planar face*) dasar setengah bola di $Y=0$. `OpenSketchOnPlane` sebelumnya hanya mendukung nama bidang standar (*Front*, *Top*, *Right*) dan tidak memiliki fungsi inspeksi `Face2.GetSurface().IsPlane()` untuk memilih permukaan benda secara otomatis.

### 2. Implementasi Perbaikan
- **`CadContextReconciler.cs` (Baru)**:
  - Mengimplementasikan `IsModificationRequest(text, currentSpec, activeBodyCount)`: membedakan secara tegas permintaan modifikasi/penambahan fitur (`tambah`, `tambahkan`, `pasang`, `revisi`, `ubah`, `di tengah`, `pada permukaan`) vs permintaan model baru eksplisit (`buat model baru`, `part baru`, `mulai dari awal`).
  - Mengimplementasikan `TryReconcileRevisionSpec(...)`: mengidentifikasi operasi duplikasi fitur dasar (`CreateHemisphere`, `NewPart`) pada model yang sudah memiliki solid body, mendaftarkannya ke dalam `existingCompletedOpIds`, serta memastikan seluruh rantai dependensi downstream tetap utuh dan valid.
  - Mengimplementasikan `MergeSpecs(...)`: menggabungkan operasi lama dan operasi baru ke dalam `CurrentCadSpec` kumulatif agar riwayat model tetap lengkap untuk revisi berikutnya.
- **`CadFastPathEngine.cs`**:
  - Mengimplementasikan `TryCreateRevisionSpec(text, currentSpec, out revSpec)`:
    - Mendeteksi penambahan tangkai/batang silinder (*rod/shaft*) secara deterministik (< 1 ms latency, 0 kuota API).
    - Mengekstrak diameter dan panjang (misal: diameter 20 mm, panjang 100 mm).
    - Menghasilkan 3 operasi CAD presisi: `NewSketch` pada `PlanarFace`, `AddCircle` diameter 20 mm di (0,0), dan `ExtrudeBoss` depth 100 mm dengan `Merge Result`.
    - Mendeteksi penambahan lubang (*hole*) dengan kedalaman dan diameter spesifik.
- **`CadPlanner.cs`**:
  - Memperbarui `BuildPromptForRevision` dengan instruksi ketat: geometri dasar sudah terbentuk di SOLIDWORKS, larangan mengulangi `CreateHemisphere`, dan panduan penambahan fitur pada permukaan menggunakan `plane: 'PlanarFace'`, `AddCircle`, dan `ExtrudeBoss`.
- **`CadPlanValidator.cs`**:
  - Menambahkan overload `Validate(CadSpec spec, IEnumerable<string> existingOperationIds)`: memungkinkan operasi baru memvalidasi dependensinya ke operasi yang sudah selesai pada model aktif tanpa memicu error "dependensi tidak ditemukan".
- **`SolidWorksAutomationService.cs`**:
  - Menambahkan parameter `HashSet<string> existingCompletedOpIds` pada `ExecuteSpec`. Operasi yang sudah ada dilompati eksekusi geometrinya (`[OP_EXISTING]`), ditandai sukses, dan dependensinya diteruskan ke operasi berikutnya.
  - Mengimplementasikan `SelectPlanarFace(ModelDoc2 doc)`: mengiterasi seluruh `Face2` dari solid body aktif, memfilter menggunakan `Surface.IsPlane()`, dan memilih permukaan datar terluas (dasar setengah bola) menggunakan `Entity.Select4(false, null)`.
  - Memperbarui `OpenSketchOnPlane`: mendukung pemilihan `PlanarFace` secara eksplisit dan fallback otomatis ke `SelectPlanarFace` jika dokumen aktif memiliki solid body.
  - Memperbarui `CreateNewPartDocument()`: menambahkan fallback ke `_swApp.GetDocumentTemplate((int)swDocumentTypes_e.swDocPART, "", 0, 0, 0)` dan logging diagnostik template.
  - Memastikan `ExtrudeBoss` menggunakan `Merge = true` sehingga tangkai silinder menyatu sempurna dengan setengah bola menjadi **1 solid body**.
- **`MainViewModel.cs`**:
  - Menghubungkan `CadContextReconciler` ke dalam `SendMessageAsync`.
  - Memisahkan jalur `isModification` dan `New Model`: modifikasi mengecek `TryCreateRevisionSpec` lalu Gemini revision prompt, sedangkan model baru mengecek `TryCreateSpec` lalu Gemini new model prompt.
  - Merekonsiliasi spesifikasi sebelum eksekusi dan meneruskan `existingCompletedOpIds` ke `ExecuteSpecDirectly`.
  - Memperbarui `CurrentCadSpec` menggunakan `CadContextReconciler.MergeSpecs` setelah eksekusi berhasil.
- **`AICADAssistant.Tests`**:
  - Menambahkan `CadRevisionTests.cs` (11 unit test baru): pengujian model setengah bola baru, penambahan tangkai tanpa CreateHemisphere ulang, deteksi kata kunci revisi, pengecualian model baru eksplisit, rekonsiliasi dependensi, penanganan part kosong, penggabungan spek kumulatif, Fast Path penambahan tangkai dan lubang, serta penolakan dependensi invalid.

### 3. Hasil Kompilasi & Pengujian
- **Unit Tests**: **86/86 Lolos (100% Passed, 0 Gagal)** dalam 225 ms.
- **Kompilasi MSBuild x64**: **100% Berhasil (0 Error, 0 Warning kode)**.
- **Aktivasi COM**: Assembly versi **`1.1.6.0`** (`AICADAssistant.AddIn.SwAddIn`) terverifikasi aktif melalui CLSID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.

---

## Tahap 19: Master Engineering Upgrade v2.0 — Complete CAD Automation, Single Prompt to Mechanical Model, Smart Face Selection, & Geometry Verification (2026-10-30)

### 1. Tujuan & Filosofi Desain v2.0
- **Filosofi Utama**: **ONE PROMPT -> COMPLETE CAD MODEL**. Pengguna dapat mendeskripsikan komponen mekanik nyata (seperti kunci pas 10 mm, L-bracket dengan lubang baut, puli alur V, poros bertingkat) dalam satu pesan bahasa alami, dan sistem langsung menghasilkan model 3D solid yang lengkap, akurat, dan dapat direbuild di SOLIDWORKS tanpa langkah perantara.
- **Eliminasi Stale-Plan Execution**: Pertanyaan percakapan murni, penjelasan, atau kegagalan parsing AI tidak boleh mengeksekusi rencana CAD lama atau mengubah status spesifikasi aktif.

### 2. Single Source of Truth & Katalog Operasi (`OperationCatalog.cs`)
- **Ekspansi `OperationType` (0–32)**:
  - Tipe 0–17: `NewPart`, `NewSketch`, `AddLine`, `AddRectangle`, `AddCircle`, `AddDimension`, `ExtrudeBoss`, `ExtrudeCut`, `RevolvedBoss`, `Fillet`, `Chamfer`, `SimpleHole`, `LinearPattern`, `CircularPattern`, `SavePart`, `ExportStep`, `ExportStl`, `CreateHemisphere`.
  - Tipe 18–32 (Baru di v2.0): `EndSketch`, `AddPolyline`, `AddArc`, `AddCenterRectangle`, `AddSlot`, `AddPolygon`, `AddPoint`, `RevolvedCut`, `SweepBoss`, `Shell`, `Mirror`, `CreatePlane`, `ActivateDocument`, `Rebuild`, `AddConstraint`.
- **`OperationCatalog.cs`**:
  - Menyediakan metadata terpadu per operasi: kategori (`Document`, `Sketch`, `SolidFeature`, `ReferenceGeometry`), deskripsi, parameter wajib, parameter opsional, kebutuhan sketsa aktif, dan status implementasi di executor.
- **`CadPlanValidator.cs`**:
  - Diperbarui untuk memvalidasi seluruh 33 operasi, termasuk array titik koordinat `AddPolyline`, radius dan sudut `AddArc`, dimensi `AddSlot`, ketebalan `Shell`, dan opsi `throughAll` pada `ExtrudeCut`.

### 3. Klasifikasi Intent Presisi (`IntentClassifier.cs`)
- Mengklasifikasikan pesan pengguna secara deterministik sebelum menyentuh AI atau CAD Executor:
  1. `Conversational`: Pertanyaan teori, penjelasan, atau sapaan ("Apa fungsi fitur revolve?", "Berapa volume part ini?"). Dijawab langsung di chat tanpa memanggil CAD Executor dan tanpa memodifikasi `CurrentCadSpec`.
  2. `Export`: Perintah ekspor ("Ekspor ke STEP", "Simpan sebagai STL"). Mengekspor model aktif secara instan ke direktori tujuan.
  3. `CreateNewModel`: Perintah pembuatan benda baru dari awal.
  4. `ModifyExistingModel`: Perintah penambahan fitur pada model aktif (tangkai, lubang, fillet, chamfer).
- Setiap siklus pemodelan CAD diberikan `activeRequestId` unik (`req_yyyyMMdd_HHmmss`). Eksekusi diblokir jika ID spesifikasi tidak cocok dengan request aktif.

### 4. Otomasi Eksekusi CAD di SOLIDWORKS (`SolidWorksAutomationService.cs`)
- **Fitur Baru & Penyempurnaan**:
  1. `DrawPolyline`: Menggambar rantai garis bersambung dari array koordinat `points` dengan penutupan kontur otomatis (`closed: true`).
  2. `DrawArc`: Menggambar busur lingkaran menggunakan koordinat pusat, radius, sudut awal, dan sudut akhir.
  3. `DrawSlot`: Menggambar slot lonjong menggunakan `SketchManager.CreateSketchSlot` dengan fallback garis sejajar dan dua busur setengah lingkaran.
  4. `ExtrudeCut` yang Tangguh:
     - Mendukung parameter `throughAll` (`swEndCondThroughAllBoth`).
     - *Automatic Flip Fallback*: Jika `FeatureCut4` dengan `flip = false` gagal, otomatis mencoba kembali dengan `flip = true` untuk mengatasi normal face terbalik.
  5. `AddSimpleHole`: Mendukung mode lubang tembus (`throughAll`).
  6. `RevolvedCut`: Mengimplementasikan pemotongan putar menggunakan `FeatureManager.FeatureRevolveCut2`.
  7. `AddShell`: Mengimplementasikan pengosongan benda padat menggunakan `InsertFeatureShell`.
  8. *Smart Planar Face Selection*:
     - Menilai planar face pada solid bodies berdasarkan kecocokan normal face dengan vektor arah target dan jarak terdekat ke `targetPoint`.
     - Memberikan bobot prioritas luas permukaan.

### 5. Fast Path Model Mekanik Kompleks (`CadFastPathEngine.cs`)
- **Kunci Pas 10 mm (`TryMatchWrench`)**:
  - Prompt: *"Buatkan kunci pas ukuran 10 mm dengan panjang 120 mm, ketebalan 5 mm, lubang gantung diameter 5 mm di ujung gagang, radius pada sudut kepala, dan bentuk gagang yang mengecil secara bertahap."*
  - Kontur Polyline 12 titik simetris pada Front Plane membentuk kepala mulut terbuka 10 mm (kedalaman rahang 15 mm, lebar kepala 24 mm) dan gagang yang mengecil dari 16 mm ke 12 mm.
  - ExtrudeBoss tebal 5 mm.
  - Lubang gantung lingkaran diameter 5 mm pada $X = 112$ mm (8 mm dari ujung gagang), diekstrusi potong tembus (`throughAll = true`).
  - Fillet 1 mm pada tepi.
  - Menghasilkan 1 solid body presisi dengan bounding box $\approx 24 \times 120 \times 5$ mm.
- **L-Bracket (`TryMatchBracket`)**:
  - Polyline penampang siku $80 \times 100$ mm tebal 5 mm, diekstrusi lebar 40 mm.
- **Puli V-Groove (`TryMatchPulley`)**:
  - Sketsa penampang puli dengan alur V, di-revolve 360 derajat.
- **Poros Bertingkat (`TryMatchShaft`)**:
  - Sketsa penampang poros bertingkat 3 seksi, di-revolve 360 derajat.
- **Fast Path Revisi Tingkat Lanjut**:
  - Penempatan lubang peka jarak (misal: "8 mm dari ujung" otomatis menghitung $X = \text{panjang} - 8$).
  - Penambahan Fillet langsung (`TryMatchFilletRevision`).
  - Penambahan Chamfer langsung (`TryMatchChamferRevision`).

### 6. Strict Geometric Verification Engine (`GeometryVerifier.cs`)
- Memvalidasi kualitas geometri setelah eksekusi CAD di SOLIDWORKS:
  1. *Solid Body Count Check*: Wajib $> 0$. Jika bernilai 0, eksekusi dinyatakan gagal.
  2. *Bounding Box Check*: Memverifikasi dimensi bodi $X, Y, Z > 0$.
  3. *Force Rebuild Integrity*: Memeriksa apakah terjadi error pada feature tree.
  4. *Volume Delta Verification*: Pada operasi revisi, memverifikasi bahwa operasi penambahan material (Boss) meningkatkan volume dan operasi pemotongan (Cut) menurunkan volume.

### 7. Hasil Kompilasi & Pengujian
- **Unit Tests**: **97/97 Lolos (100% Passed, 0 Gagal)** dalam 212 ms:
  - `CadPlanValidatorTests`: 17 passed.
  - `CadFastPathEngineTests`: 12 passed.
  - `CadRevisionTests`: 11 passed.
  - `CadEngineeringTests`: 11 passed (Wrench 10 mm, L-Bracket, Pulley, Stepped Shaft, IntentClassifier, Offset Hole, Fillet/Chamfer revisions, GeometryVerifier body/volume/rebuild, OperationCatalog 33 operations).
  - `CadExecutorTests`: 7 passed.
  - `UnitConverterTests`: 15 passed.
  - `CadSpecParsingTests`: 6 passed.
  - `GeminiClientTests`: 14 passed.
  - `BackupServiceTests`: 4 passed.
- **Kompilasi Solusi MSBuild x64**: **100% Berhasil (0 Error, 0 Warning kode)**.
- **Versi Assembly**: Diperbarui ke **`2.0.0.0`** pada seluruh assembly (`AICADAssistant.Core.dll`, `AICADAssistant.AddIn.dll`, `AICADAssistant.Tests.exe`).
- **Aktivasi COM**: Objek COM `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` berhasil diinisialisasi secara native di Windows x64.





---

# Catatan Implementasi — AI CAD ASSISTANT v3.0
**Tanggal:** 30 Oktober 2026  
**Versi Target:** `3.0.0.0` (Informational: `3.0.0`)  
**Lingkungan CAD:** SOLIDWORKS 2025 SP1.1 (x64)  
**Lingkungan Framework:** .NET Framework 4.8 / C# 5 / COM Interop  
**Penyedia AI:** Google Gemini API (dpapi-protected)

## 1. Ringkasan Rekonstruksi Arsitektur v3.0
Sesuai MASTER PROMPT v3.0, arsitektur pemodelan CAD berbasis AI direkonstruksi secara komprehensif mengadopsi pola-pola komputasi parametrik modern (Adze, SolidWorks-MCP-Server, Multi-Agent CAD, dan CadQuery/build123d):
1. **Pemisahan Kontrak Bertipe (Typed Contracts)**:
   - `CADBrief`: Mendefinisikan maksud desain teknis, tipe komponen (`WrenchTool`, `Bracket`, `Shaft`, `Hemisphere`, `Prismatic`, dll.), dimensi target, toleransi, dan target volume.
   - `ArchitectPlan`: Rencana pembagian bidang sketsa (datum), orientasi sumbu, strategi simetri, dan dependency DAG fitur.
   - `CADProgram`: Representasi program CAD deterministik dengan kompatibilitas dua arah (`ToCadSpec` / `FromCadSpec`).
   - `GeometryReference`: Pelacakan referensi geometri hybrid (`TopFace`, `BottomFace`, `LargestFace`, `NamedFace`, `PersistentId`).
   - `VerificationResult`: Laporan verifikasi fisik komprehensif (Solid Body Count $> 0$, Deviasi Volume, Bounding Box, Rebuild Errors).
   - `CADDiagnostic`: Struktur diagnostik bertingkat (`Info`, `Warning`, `Error`, `Fatal`) dengan proposal remediasi otomatis.

2. **Native Tool Registry (`ICadTool` & `CadToolRegistry`)**:
   - Pendaftaran seluruh 33 operasi CAD dengan status dukungan (`FullySupported`, `PartiallySupported`, `Experimental`, `Unsupported`).
   - Validasi pra-eksekusi terhadap skema parameter, batas minimum/maksimum nilai geometris, dan prakondisi status dokumen.

3. **Parametric Sketch Engine**:
   - `SketchLifecycleManager`: State machine 7 tahap (`Idle` -> `SketchStarted` -> `GeometryCreated` -> `GeometryValidated` -> `SketchClosed` -> `ProfileSelected` -> `FeatureExecuted`) yang menjamin tidak ada desinkronisasi sketsa aktif COM.
   - `CoordinateTransformer`: Transformasi koordinat 2D bidang lokal $(u, v)$ ke koordinat ruang 3D $(x, y, z)$ menggunakan basis ortonormal normal dan tangen bidang.
   - `ProfileValidator`: Validasi kontinuitas rantai segmen, deteksi celah diskontinuitas, eliminasi segmen nol, dan perhitungan luas poligon via formula Shoelace ($A > 0$).

4. **Perbaikan Keandalan Fitur (Feature Reliability)**:
   - **Root Cause Fix pada ExtrudeCut / ThroughHole**:
     - Parameter kedua `FeatureCut4` pada SOLIDWORKS API adalah `Flip side to cut` (bukan arah ekstrusi). Pada implementasi lama, saat pemotongan gagal parameter ini diubah menjadi `true` yang memerintahkan SOLIDWORKS memotong bagian luar profil (menghapus seluruh bodi), menyebabkan error "Pastikan terdapat sketsa tertutup aktif".
     - Pada v3.0, `Flip` selalu dijaga `false` (potong material di dalam profil).
     - Untuk lubang tembus (`throughAll`), arah ekstrusi diatur dua arah (`Sd = false`, `T1 = ThroughAll`, `T2 = ThroughAll`).
     - Jika directional cut satu arah diperlukan, pembalikan arah dilakukan via parameter ketiga `Dir = true`.
   - **AddSimpleHole**:
     - Mendukung penempatan koordinat spesifik $X, Y$ dan planar face target secara otomatis.
   - **Fillet & Chamfer**:
     - Dilengkapi seleksi otomatis fitur target (`BODYFEATURE`) dan validasi radius maksimum terhadap ketebalan minimum bounding box ($r < 0.5 \times \min(t)$).

5. **Multi-Stage Orchestrator (`CadExecutionEngine`)**:
   - Mengintegrasikan Tahap 1 (Normalisasi Input) hingga Tahap 8 (Auto-Repair Loop) secara kohesif.
   - Eksekusi Fast Path deterministik (< 10 ms) untuk bentuk standar mekanik, dengan fallback AI Gemini API dan loop perbaikan diagnostik hingga 2 iterasi.

6. **Hasil Pengujian Otomatis**:
   - **Total Tests:** **113 / 113 Lolos (100% Passed, 0 Failed)**:
     - 97 tes baseline (Validator, FastPath, Revision, Engineering, Executor, UnitConverter, Parsing, GeminiClient, Backup).
     - 16 tes arsitektur v3.0 baru (`CadArchitectureV3Tests`).
   - Seluruh assembly (`Core`, `AddIn`, `Tests`) dinaikkan versinya menjadi **`3.0.0.0`**.

---

## Tahap 21: Targeted Bugfix v3.0.2 Follow-Up — API Key Redaction, Credential Isolation, In-Flight Cancellation, & Post-HTTP Deadline Check (2026-10-31)

### 1. Keamanan API Key pada Pesan Error (API Key Redaction)
- **Akar Masalah**:
  - `GeminiClient.ExecuteRequestAsync` sebelumnya membaca `errorContent` dan `serverMessage` secara mentah dan menyalurkannya langsung ke `GeminiException` serta `GeminiConnectionService.ReportError`.
  - Jika Gemini API atau reverse proxy mengembalikan pesan error yang memuat parameter `key=...` atau string API key mentah, `GeminiException.Message` berisiko membocorkan key ke UI atau penanganan log eksternal.
  - Test sebelumnya memanggil `LoggingService.RedactApiKey()` di dalam assertion pengujian, bukan menguji integritas sanitasi internal kode produksi.
- **Implementasi Perbaikan**:
  - `LoggingService.RedactApiKey`:
    - Diperluas untuk mencakup URL query parameter `([?&]key=)[^&"'\s]+`, header/JSON parameter `(api_?key\s*[:=]\s*["']?)[^"'\s,}]+`, dan pola Google API key `AIza[A-Za-z0-9_\-]{16,}`.
  - `GeminiException`:
    - Konstruktor utama memanggil `base(LoggingService.RedactApiKey(message), inner)` sehingga string pesan pada level exception paling mendasar dijamin 100% terbebas dari API key mentah.
    - Semua metode factory (`RateLimitException`, `TimeoutException`, `ServerUnavailableException`, `AuthException`, `ModelNotFoundException`, `NetworkException`, `InvalidResponseException`) menyaring detail server melalui `RedactApiKey`.
  - `GeminiClient`:
    - Menambahkan metode `RedactSensitiveData()` yang menggabungkan `RedactApiKey` dengan penghapusan substring API key aktif yang terkonfigurasi pada `CredentialService`.
    - Di dalam `ExecuteRequestAsync`, `errorContent` dan `serverMessage` disaring seketika sebelum diteruskan ke `LoggingService.Instance.Warning()`, `GeminiConnectionService.Instance.ReportError()`, dan instansiasi `GeminiException`.
  - `GeminiHttpMockTests.Test08`:
    - Mengirimkan respons error HTTP 500 yang memuat dummy API key.
    - Melakukan assertion langsung terhadap `GeminiException.Message` dan `GeminiConnectionService.LastErrorMessage` tanpa pemanggilan fungsi sanitasi di dalam unit test assertion.

### 2. Isolasi Kredensial untuk Pengujian Unit (Credential Isolation)
- **Akar Masalah**:
  - `GeminiHttpMockTests` sebelumnya memanggil konstruktor default `new CredentialService()`, yang mengakses `%APPDATA%\AICADAssistant\credential.dat` milik profil Windows pengguna aktif.
- **Implementasi Perbaikan**:
  - `CredentialService`:
    - Konstruktor berparameter `public CredentialService(string customCredentialPath)` disempurnakan untuk memastikan pembuatan direktori induk secara otomatis sebelum enkripsi DPAPI dieksekusi.
  - `GeminiHttpMockTests`:
    - Menggunakan file temporer unik di folder `%TEMP%` (`aicad_test_cred_<guid>.dat`) dengan dummy key terisolasi.
    - Menambahkan `[TestCleanup]` untuk menghapus file kredensial temporer setelah setiap pengujian selesai.
  - File riil `%APPDATA%\AICADAssistant\credential.dat` milik pengguna sama sekali tidak disentuh atau didekripsi/dienkripsi ulang.

### 3. Pengujian In-Flight User Cancellation
- **Implementasi**:
  - `Test03_UserCancellation_DistinctFromTimeout` diperbarui menggunakan `TaskCompletionSource<bool>` yang memberi sinyal dari dalam `MockHttpMessageHandler` saat request telah aktif diterima dan sedang diproses di server.
  - Pembatalan `CancellationTokenSource.Cancel()` dipicu saat request berstatus in-flight.
  - Terverifikasi: menghasilkan `OperationCanceledException`, tidak diklasifikasikan sebagai `Timeout`, tidak memicu mekanisme retry (`CallCount == 1`).

### 4. Pengujian Timeout & Retry Robustness
- **Implementasi**:
  - `Test04_Timeout_DoesNotCauseRetry`:
    - Menguji batas waktu logis (1 detik).
    - Memverifikasi exception berjenis `GeminiException` dengan properti `IsTimeout == true`.
    - Memverifikasi tidak terjadi fallback model dan tidak terjadi retry (`CallCount == 1`, request URL tetap menargetkan model awal).
  - `Test05` dan `Test06`:
    - Memverifikasi retry HTTP 503 dan HTTP 429 beralih ke model fallback yang tepat (`gemini-3.5-flash-lite` sesuai `AppSettings.DefaultGeminiModel`).

### 5. Pemeriksaan Deadline Pasca-HTTP Request (Post-HTTP Deadline Check)
- **Akar Masalah**:
  - Bila respons HTTP diterima persis di batas waktu (boundary deadline), transmisi atau deserialisasi dapat selesai setelah timer deadline kadaluwarsa.
- **Implementasi Perbaikan**:
  - Pemeriksaan `ct.IsCancellationRequested` dan `timeoutCts.IsCancellationRequested` disisipkan:
    1. Segera setelah `await _httpClient.PostAsync()` selesai.
    2. Segera setelah `await response.Content.ReadAsStringAsync()` selesai.
  - Jika deadline kadaluwarsa pada fase tersebut: proses seketika dibatalkan, dicatat sebagai timeout (`408`), respons tidak dikembalikan ke caller, dan tidak dilakukan retry.
  - Ditambahkan `Test09_PostHttpDeadlineCheck_ThrowsTimeoutAndDoesNotReturnResponse` dan `Test10_PostHttpCancellation_ThrowsOperationCanceledException`.

### 6. Hasil Pengujian & Kompilasi
- **Total Tests**: **137 / 137 Lolos (100% Passed, 0 Failed)**.
- **Build Status**: **x64 Debug Succeeded (0 Errors, 0 Warnings)** across `Core`, `AddIn`, and `Tests`.
- **Integritas Konfigurasi**: COM GUID tetap dipertahankan `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`, kredensial pengguna 278 bytes terjaga utuh tanpa modifikasi, tidak ada git push atau deployment tak berizin.

---

## Tahap 22: Final Targeted Patch — Network Exception Cancellation Classification & Strict Prioritization (2026-10-31)

### 1. Klasifikasi Pembatalan Jaringan pada `HttpRequestException`
- **Akar Masalah**:
  - Pada lingkungan .NET Framework, ketika socket atau koneksi HTTP dibatalkan oleh pengguna saat pengiriman data berlangsung, `HttpClient` sering kali melempar `HttpRequestException` (misal: "The underlying connection was closed: The request was canceled").
  - Blok `catch (HttpRequestException ex)` sebelumnya hanya memeriksa `timeoutCts.IsCancellationRequested` sebelum masuk ke fallback/retry atau melempar `GeminiException.NetworkException`.
  - Hal ini menyebabkan pembatalan oleh pengguna dapat keliru diklasifikasikan sebagai Network Error (`[GEMINI_NETWORK_ERROR]`) atau bahkan memicu retry yang tidak diinginkan.
- **Implementasi Perbaikan**:
  - Pada `GeminiClient.ExecuteRequestAsync()`, blok `catch (HttpRequestException ex)` sekarang menerapkan prioritas ketat:
    1. **User Cancellation (`ct.IsCancellationRequested`)**:
       - Mencatat log `[GEMINI_CANCEL]`.
       - Melempar `OperationCanceledException(ct)`.
       - Tidak melempar NetworkException, tidak melaporkan timeout (408), tidak memicu retry.
    2. **Request Timeout (`timeoutCts.IsCancellationRequested`)**:
       - Mencatat log `[GEMINI_TIMEOUT]`.
       - Melaporkan error 408 ke `GeminiConnectionService`.
       - Melempar `GeminiException.TimeoutException`.
    3. **Network Retry / Network Failure**:
       - Jika percobaan masih dalam batas dan tidak dibatalkan, menjadwalkan retry dengan delay eksponensial.
       - Jika retries habis, mencatat `[GEMINI_NETWORK_ERROR]` dan melempar `GeminiException.NetworkException`.
  - Penanganan status code HTTP 429 dan 503 tetap terjaga di blok evaluasi status respons HTTP.

### 2. Pengujian Regresi (`Test11`)
- Menambahkan `Test11_HttpRequestException_WhenUserCancelled_ClassifiedAsUserCancellation` pada `GeminiHttpMockTests.cs`:
  - Request dimulai secara asinkron.
  - Handler menahan request hingga pengguna membatalkan `CancellationTokenSource`.
  - Handler melempar `HttpRequestException` untuk mensimulasikan socket connection abort.
  - Assert membuktikan:
    - `OperationCanceledException` tertangkap.
    - `IsTimeout == false`.
    - `IsNetworkError == false`.
    - Tidak ada retry (`mockHandler.CallCount == 1`).

### 3. Hasil Validasi
- **Total Tests**: **137 / 137 Lolos (100% Passed, 0 Failed)**.
- **Solusi MSBuild x64 Debug**: Succeeded (**0 Errors, 0 Warnings**).
- **Integritas `credential.dat`**: 278 bytes, DPAPI CurrentUser, tidak tersentuh.

---

## Tahap 23: Deployment Script & Registration Fix — VS BuildTools MSBuild, Strict Verification, & Official Registry Alignment (2026-10-31)

### 1. Perbaikan Script Deployment (`scripts/deploy_v3.ps1`)
- Mengalihkan path MSBuild ke executable Visual Studio 2022/BuildTools yang terverifikasi:
  `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe`.
- Menambahkan validasi eksistensi MSBuild sebelum proses build dijalankan.
- Konfigurasi build menggunakan target `/t:Rebuild`, platform `x64`, dan konfigurasi `Debug`.
- Menambahkan pemeriksaan ketat keberadaan ketiga file output wajib setelah build:
  - `AICADAssistant.Core.dll`
  - `AICADAssistant.AddIn.dll`
  - `AICADAssistant.Tests.exe`
- Menghentikan proses deployment jika `AICADAssistant.Tests.exe` tidak ditemukan (tidak ada bypass pengujian).
- Validasi `AssemblyVersion` aktual via `[System.Reflection.AssemblyName]::GetAssemblyName($path).Version` untuk memastikan target `3.0.2.0`.
- Menjalankan seluruh 137 unit test secara wajib; menghentikan deployment jika terdapat kegagalan.

### 2. Perbaikan Script Registrasi (`scripts/register_addin.ps1`)
- Mempertahankan COM GUID resmi: `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.
- Memeriksa proses SOLIDWORKS sebelum registrasi dijalankan.
- Menggunakan 64-bit RegAsm (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe /codebase`).
- Menyelaraskan registri dengan standar resmi SOLIDWORKS AddIn:
  - `HKLM:\SOFTWARE\SolidWorks\AddIns\$guid` default DWORD = `0`.
  - `HKCU:\Software\SolidWorks\AddInsStartup\$guid` default DWORD = `1`.
- Menambahkan verifikasi integritas 5 titik:
  1. HKLM Default = 0, Title, Description.
  2. HKCU AddInsStartup Default = 1.
  3. Eksistensi COM CLSID pada `HKLM:\SOFTWARE\Classes\CLSID\$guid\InprocServer32`.
  4. Kecocokan Assembly Identity memuat versi aktual (`3.0.2.0`).
  5. Kecocokan path CodeBase menunjuk tepat ke file AddIn DLL hasil build.
- Menghentikan dan melaporkan error jika salah satu verifikasi gagal.

### 3. Integritas Kredensial dan Sistem
- File `credential.dat` (278 bytes) dan `settings.json` (226 bytes) terjaga utuh.
- Tidak ada deployment atau registrasi otomatis yang dijalankan selama task berlangsung.

---

## Tahap 24: Critical Bugfix v3.0.2 — CAD Intent Classification, Document Context Isolation & Geometry Verification (2026-10-31)

### 1. Investigasi & Akar Masalah False Success Stepped Shaft
- **Akar Masalah Klasifikasi Intent (Intent Hijack)**:
  - Pada `IntentClassifier.Classify(string text, CadSpec currentSpec, int activeBodyCount)`:
    Kondisi `if (activeBodyCount > 0 || currentSpec != null)` bernilai `true` karena `_currentCadSpec` milik model pelat sebelumnya masih tersimpan di memori `MainViewModel`, meskipun dokumen aktif di SOLIDWORKS baru/kosong (`activeBodyCount == 0`).
  - Pola modifikasi mencocokkan kata `"tambahkan"` pada klausa subordinat *"dan tambahkan lubang aksial"*, sehingga permintaan pembuatan poros baru keliru diklasifikasikan sebagai `ModifyExistingModel`.
- **Akar Masalah Kebocoran Konteks Dokumen (Document Context Leak)**:
  - `_currentCadSpec` pada `MainViewModel` bersifat global tunggal dan tidak terikat dengan identitas dokumen SOLIDWORKS (`GetTitle()` / `GetPathName()`). Saat pengguna membuka dokumen baru, `_currentCadSpec` tidak direset atau diisolasi.
- **Akar Masalah Pembajakan Fast Path Revision (Fast Path False Match)**:
  - `CadFastPathEngine.TryMatchRodRevision` mencocokkan kata kunci `"shaft"`, mengekstrak angka pertama diameter (20) dan panjang (20), lalu menghasilkan spesifikasi penambahan silinder sederhana Ø20 × 20 mm, membypass Gemini CAD Planner secara keliru.
- **Akar Masalah False Success pada GeometryVerifier**:
  - `GeometryVerifier` sebelumnya hanya memeriksa `SolidBodyCount > 0` dan rebuild status tanpa memvalidasi kesesuaian bounding box terhadap dimensi target (panjang 120 mm) dan kelengkapan fitur 3D yang direncanakan.

### 2. Implementasi Perbaikan
- **`IntentClassifier.cs` (P0 Intent Classification)**:
  - Menambahkan overload dengan parameter `out string reason` untuk logging diagnostik terperinci.
  - Menetapkan aturan ketat: `activeBodyCount <= 0` (dokumen kosong) **TIDAK PERNAH** diklasifikasikan sebagai `ModifyExistingModel`.
  - Frasa eksplisit pembuatan komponen utama (`buat/buatkan/rancang/modelkan/bikin` + root component noun: poros, puli, pelat, bracket, dll.) **mengungguli** kata tambahan fitur subordinat ("tambahkan lubang").
- **`MainViewModel.cs` (P0 Document Context Isolation)**:
  - Mengimplementasikan `_documentCadSpecs` (`Dictionary<string, CadSpec>`) yang memetakan spesifikasi CAD per identitas dokumen aktif (`GetDocumentIdentifier`).
  - Pada `OnActiveDocumentChanged()` dan setiap pesan masuk di `SendMessageAsync()`: memeriksa identitas dokumen aktif dan jumlah solid body (`activeBodyCount`). Jika `activeBodyCount == 0` atau dokumen berbeda, konteks CAD sebelumnya tidak digunakan (`activeDocSpec = null`).
  - Menambahkan logging diagnostik terperinci: active doc identity, body count, context match, intent + reasoning, serta fast path decision.
- **`CadFastPathEngine.cs` (P0 Fast Path Revision Guard & Dynamic Stepped Shaft)**:
  - Menambahkan `ComplexGeometryInhibitorPattern` pada `TryCreateRevisionSpec`: frasa seperti `poros bertingkat`, `stepped shaft`, `lima bagian`, `segmen`, `multiple diameter`, `revolve`, atau prompt dengan $\ge 5$ angka dimensi langsung menghambat Fast Path Revision dan dialihkan ke Gemini CAD Planner.
  - Memperketat `TryMatchRodRevision`: menghapus kata lepas `"shaft"`, mewajibkan frasa aksi penambahan eksplisit (`tambahkan tangkai`, `pasangkan silinder`, dll.).
  - Mengembangkan `TryMatchShaft` pada `TryCreateSpec` agar mampu mengekstraksi poros bertingkat multi-segmen secara dinamis (5 segmen aksial + Revolve 360° + lubang aksial Through All).
- **`GeometryVerifier.cs` (P0 Prevent False Success)**:
  - Memperkenalkan enum `VerificationStatus` (`CompleteSuccess`, `PartialSuccess`, `Failed`, `NeedsClarification`).
  - Menambahkan validasi Bounding Box terhadap dimensi target (panjang 120 mm): jika deviasi melebihi toleransi (misal aktual 20 mm vs target 120 mm), diverifikasi sebagai `Failed`.
  - Menambahkan validasi kelengkapan operasi 3D: jika spesifikasi meminta kombinasi multi-fitur (Boss + Cut) tetapi operasi pemotongan/aksial belum terbentuk, diklasifikasikan sebagai `PartialSuccess` atau `Failed`, bukan `CompleteSuccess`.
  - Memvalidasi operasi tak didukung: langsung menghasilkan status `Failed`.
- **`SolidWorksAutomationService.cs` (P0 Revolve & Cut Support)**:
  - Menyempurnakan `RevolvedBoss`: secara otomatis memeriksa dan menandai segmen garis sumbu putar pada bidang sketsa aktif dengan `Mark = 4` sebelum memanggil `FeatureRevolve2`, memastikan API Revolve SOLIDWORKS COM berjalan stabil.

### 3. Pengujian Regresi & Validasi
- **Unit Tests**: **145 / 145 Lolos (100% Passed, 0 Failed)**:
  - Menambahkan `CadContextIsolationTests.cs` (8 pengujian regresi spesifik Section F):
    1. `Test01`: Stepped shaft request dengan existing CadSpec tetapi bodyCount=0 $\to$ `CreateNewModel`.
    2. `Test02`: Stepped shaft request tidak masuk `FastPathRevision`.
    3. `Test03`: "Tambahkan lubang pada pelat aktif" tetap `ModifyExistingModel` saat context valid.
    4. `Test04`: Berpindah ke dokumen baru tidak menggunakan CadSpec dokumen sebelumnya.
    5. `Test05`: Prompt kompleks tidak menghasilkan satu primitif sederhana.
    6. `Test06`: Incomplete operation graph tidak boleh `CompleteSuccess` (menghasilkan `PartialSuccess`).
    7. `Test07`: Bounding box mismatch (20 mm vs 120 mm) tidak boleh `CompleteSuccess` (menghasilkan `Failed`).
    8. `Test08`: Unsupported operation tidak boleh dilaporkan berhasil.
- **Kompilasi Solusi MSBuild x64 Debug**: **100% Succeeded (0 Error, 0 Warning)**.
- **Integritas Sistem**: `credential.dat` (278 bytes), `settings.json`, COM GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` terjaga utuh. Tidak ada push ke GitHub atau auto-deploy.

---

## [2026-10-31] — CRITICAL FIX: SOLIDWORKS Native Revolve Selection (v3.0.2)

### 1. Root Cause Analysis
- Pada `SolidWorksAutomationService.cs`, `RevolvedBoss()` mencari axis melalui `SketchManager.ActiveSketch`.
- Ketika CAD Executor menjalankan `EndSketch` sebelum `RevolvedBoss`, `ActiveSketch` menjadi `null`, menyebabkan `RevolvedBoss` gagal atau menghasilkan seleksi kosong.
- `RevolvedBoss` tidak memilih profile sketch secara eksplisit sebelum `FeatureRevolve2` dan mengandalkan `UseAutoSelect=true`, padahal SOLIDWORKS mensyaratkan Mark 0 untuk profile sketch dan Mark 4/16 untuk axis of revolution.

### 2. Implementasi Perbaikan
- **Kontrak RevolvedBoss**: Memperbarui signature menjadi `RevolvedBoss(ModelDoc2 doc, double angleRad, string sketchName, SketchSegment axisSegment, out string featureName)`.
- **Pelacakan Sketch & Axis State**: Menambahkan state tracking internal `_lastCreatedSketchName`, `_lastCreatedSketchFeature`, `_lastTrackedAxisSegment`, dll.
- **Alur Seleksi Native Eksplisit**:
  - Profile sketch diseleksi dengan `SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0)` (Mark = 0).
  - Axis diseleksi dengan `Select4(true, selData)` di mana `selData.Mark = 4` (Mark = 4).
  - Memanggil `FeatureRevolve2` dengan `UseFeatScope=true` dan `UseAutoSelect=false`.
- **Pengujian**: Menambahkan `SolidWorksRevolveTests.cs` (7 test scenarios). Total lulus: 152 / 152 unit tests.

---

## [2026-10-31] — CRITICAL BUGFIX: Revision Context Persistence & Operation ID (Level 3A Pipeline v3.0.2)

### 1. Root Cause Analysis
- **Document Identifier Mismatch**: Pada `MainViewModel.cs`, dokumen aktif diperiksa di awal `SendMessageAsync` sebelum eksekusi part baru. Setelah pembuatan part baru (misal `Part_20261031_111838`), `CurrentCadSpec` didaftarkan menggunakan identitas lama/kosong, sehingga saat revisi diminta, pencarian `Part_20261031_111838` menghasilkan `null` (`ContextMatch = False`, `CurrentOps = 0`).
- **Empty Operation ID Collision**: Reconciler menambahkan `op.Id` kosong ke `existingCompletedOpIds`. Operasi berikutnya yang juga ber-ID kosong cocok dengan string kosong di `HashSet`, menyebabkan rekonsiliasi mencatat `Melompati duplikasi NewPart []` dan menolak seluruh rencana sebagai "semua operasi sudah ada".
- **Strict Validation Failure**: `CadPlanValidator` menolak operasi ber-ID kosong dengan pesan `Terdapat operasi tanpa Id.`.
- **Intent Misclassification on Follow-up**: Setelah kegagalan validasi, instruksi lanjutan pengguna seperti `"kerjakan"` diklasifikasikan sebagai `CreateNewModel` dengan 0 operasi karena tidak adanya mekanisme resume pending task.

### 2. Implementasi Perbaikan
- **P0 Document Context Persistence (`MainViewModel.cs`)**:
  - Menambahkan mapping alias dokumen `_documentAliases` (`Part1` $\leftrightarrow$ `C:\...\Part1.SLDPRT`) yang diperbarui saat Save As dan deteksi dokumen aktif.
  - Mendaftarkan `CurrentCadSpec` menggunakan identitas aktual dokumen pasca-eksekusi (`postDocId`, `postDocTitle`).
  - Menambahkan fallback inspeksi pohon fitur SOLIDWORKS (`ModelContextSnapshot`) jika dokumen aktif memiliki solid body tetapi CadSpec tidak ada di memori.
- **P0 Operation ID Normalization (`CadPlanNormalizer.cs`)**:
  - Menghasilkan ID unik deterministik `${spec.RequestId ?? "op"}_{i + 1}_{op.Type.ToString().ToLowerInvariant()}` untuk setiap operasi yang memiliki ID kosong atau null.
  - Menandai operasi dengan `op.IsIdGenerated = true`.
  - Mendeteksi dan menolak intra-spec duplicate IDs sebelum validasi.
  - Mempertahankan kontrak `CadPlanValidator` yang tetap ketat menolak ID kosong jika sampai ke validator.
- **P0 Context Reconciliation Contract (`CadContextReconciler.cs`)**:
  - String kosong atau whitespace tidak pernah dimasukkan ke dalam `existingCompletedOpIds`.
  - Mengeliminasi/menghapus operasi `NewPart` dari rencana revisi jika model aktif sudah memiliki solid body.
  - Jika `priorOpMap.Count == 0`, rekonsiliasi tidak pernah menyimpulkan "semua operasi sudah ada".
- **P0 Model Context Snapshot (`ModelContextSnapshot.cs`, `SolidWorksAutomationService.cs`)**:
  - Menginspeksi pohon fitur SOLIDWORKS (`FirstFeature()`, `GetNextFeature()`), mengidentifikasi sketches, revolves, cuts, bosses, bounding box (`GetPartBox`), dan volume (`CreateMassProperty`).
  - Menyusun `ToPromptContextString()` yang secara eksplisit menginstruksikan Gemini mengenai fitur yang sudah ada (`Revolve1`, `Cut-Extrude1`), melarang regenerasi dasar, dan melarang `NewPart`.
- **P0 Pending CAD Task Continuity (`PendingCadTask.cs`, `IntentClassifier.cs`, `MainViewModel.cs`)**:
  - Menyimpan `_pendingCadTask` ketika terjadi kegagalan validasi, perencanaan, atau eksekusi.
  - Menambahkan `UserIntent.ResumePendingTask` dan regex matching untuk frasa lanjutan (`kerjakan`, `coba lagi`, `lanjutkan`, `perbaiki`, `ulang`, `eksekusi`, `gas`, dll.).
  - Jika pengguna mengirim frasa lanjutan dan ada pending task pada dokumen yang sama, pipeline memulihkan prompt asli dan konteks model aktif tanpa membuat part kosong baru.
  - Jika tidak ada pending task, frasa lanjutan ditangani secara percakapan (`Conversational`), bukan `CreateNewModel`.
- **P1 Gemini Plan Diagnostics**:
  - Logging terstruktur `[GEMINI_PLAN_DIAGNOSTICS]` (RequestId, raw length, parsed ops count, generated IDs count, context match status) yang aman dan bersih tanpa memuat API key.

### 3. Pengujian Regresi & Validasi
- **Unit Tests**: **164 / 164 Lolos (100% Passed, 0 Failed)**.
- **Kompilasi MSBuild x64 Debug**: **100% Succeeded (0 Error, 0 Warning)**.
- **Integritas Sistem**: `credential.dat` (278 bytes), `settings.json`, COM GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` terjaga utuh tanpa modifikasi. Tidak ada push ke GitHub atau auto-deploy.

---

## [2026-10-31] — CRITICAL BUGFIX: Revision Operation Type Mapping & Sketch Execution (Level 3A Keyway v3.0.2)

### 1. Root Cause Analysis
- **Enum Default Zero Collision**:
  - Pada C#, `default(OperationType)` bernilai `0` (`NewPart`).
  - Ketika respons Gemini menggunakan penamaan alternatif seperti `"operation": "NewSketch"`, `"operation_type": "extrude_cut"`, atau string snake_case, `JavaScriptSerializer.Deserialize<CadSpec>` tidak memetakan properti `Type` (atau melempar exception jika format string tidak cocok persis dengan nama enum PascalCase).
  - Akibatnya, `CadOperation.Type` secara diam-diam bernilai `NewPart` (`0`) untuk semua operasi revisi (`rev_sk_keyway`, `rev_rect_keyway`, `rev_cut_keyway`).
- **Context Reconciler False Strip**:
  - `CadContextReconciler` mendeteksi bahwa dokumen aktif sudah memiliki solid body (`activeBodyCount = 1`).
  - Karena ketiga operasi revisi salah terbaca sebagai `NewPart`, reconciler menghapus ketiganya:
    `Menghapus operasi NewPart [rev_cut_keyway] ...`
    `Menghapus operasi NewPart [rev_rect_keyway] ...`
    `Menghapus operasi NewPart [rev_sk_keyway] ...`
  - Hasil akhirnya: `Operations = 0`, sehingga `CadPlanValidator` menolak dengan status `INVALID`.
- **Eksekutor SolidWorksAutomationService**:
  - `CreatePlane` sebelumnya diimplementasikan sebagai dummy no-op (`opRes = OperationResult.Succeeded(op.Id)`).
  - `OpenSketchOnPlane` memiliki bias di mana nama bidang referensi non-standar langsung dialihkan ke "Front Plane" secara keliru.
  - `ExtrudeCut` tidak menutup sketch aktif (`ActiveSketch`) dan tidak melakukan seleksi sketch eksplisit sebelum memanggil `FeatureCut4`.
  - `DrawSlot` memiliki bug pemetaan parameter di mana titik pusat `(xCenterM, yCenterM)` diteruskan ke `CreateSketchSlot` alih-alih titik awal busur `(x1, y1)`.

### 2. Implementasi Perbaikan
- **P0 Operation Type Contract (`CadOperation.cs`)**:
  - Menambahkan nilai `Unknown = -1` pada `OperationType` enum.
  - Menambahkan properti `public string RawTypeString { get; set; }` pada `CadOperation`.
  - Di dalam konstruktor `CadOperation()`, menginisialisasi `Type = OperationType.Unknown;`.
  - Memastikan `AllowedOperations.Whitelist` tidak memuat `Unknown`.
- **P0 Operation Type Mapper (`OperationTypeMapper.cs`)**:
  - Memetakan secara aman seluruh 33 nilai enum (0 hingga 32), format integer, string nama enum, dan bentuk normalisasi snake_case / kebab-case (misal `new_sketch`, `extrude_cut`, `add_slot`, `create_plane`).
  - Mendukung alias umum: `slot`, `cut`, `boss`, `plane`, `sketch`, `revolve`, `round`, dll.
  - Mengembalikan `OperationType.Unknown` jika properti tidak ada atau tidak dikenali.
  - Dilarang keras menebak tipe dari `op.Id` (nama ID seperti `rev_cut` tidak boleh menggantikan atau mendahului properti tipe sebenarnya).
- **P0 Parser Robustness & Diagnostics (`CadPlanner.cs`)**:
  - Mem-parsing JSON respons melalui kamus terstruktur (`Dictionary<string, object>`) terlebih dahulu agar kebal terhadap exception deserialisasi enum.
  - Memeriksa seluruh variasi kunci tipe operasi: `Type`, `type`, `operation`, `Operation`, `operation_type`, `op_type`, `action`, `Action`.
  - Menghasilkan log diagnostik `[PARSER_OP_DIAGNOSTIC]` untuk setiap operasi (Index, Id, RawType, ParsedType, Desc, ParamsCount, DependsOn).
- **P0 Structured Revision Schema Prompt (`CadPlanner.cs`)**:
  - Menyediakan `CadSpecRevisionJsonSchemaExample` dalam `BuildPromptForRevision` dengan contoh skema terstruktur: `CreatePlane` (Type: 30), `NewSketch` (Type: 1), `AddSlot` (Type: 22) / `AddRectangle` (Type: 3), dan `ExtrudeCut` (Type: 7).
  - Menginstruksikan konteks model stepped shaft (segmen tengah Ø25 mm, panjang 64 mm; keyway lebar 8 mm, kedalaman 4 mm, panjang 50 mm terpusat dengan sisa margin 7 mm di kedua ujung; lubang aksial Ø8 mm dipertahankan; solid bodies = 1).
  - Melarang keras menghasilkan `NewPart` (Type: 0) dan melarang regenerasi geometri dasar.
- **P0 Validation & Reconciler Safety (`CadPlanValidator.cs`, `CadContextReconciler.cs`)**:
  - `CadPlanValidator` secara eksplisit menolak operasi berstatus `OperationType.Unknown` dengan pesan diagnostik yang memuat `op.RawTypeString` dan `op.Id`.
  - `CadContextReconciler` hanya mengeliminasi operasi yang benar-benar bernilai `OperationType.NewPart`. Operasi valid dan `Unknown` tetap dipertahankan.
- **P0 SOLIDWORKS Plane & Sketch/Cut Lifecycle (`SolidWorksAutomationService.cs`)**:
  - Mengimplementasikan `CreateOffsetPlane(ModelDoc2 doc, string refPlaneName, double offsetM, bool flip, out string featName)` menggunakan `FeatureManager.InsertRefPlane((int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance, ...)`.
  - Melacak nama bidang referensi yang dibuat dalam kamus `_createdPlanes` dan `_lastCreatedPlaneName`.
  - Memperbarui `OpenSketchOnPlane` agar memprioritaskan lookup pada `_createdPlanes` dan pencocokan nama bidang eksak sebelum fallback ke bidang standar.
  - Pada `ExtrudeCut`: menutup sketch aktif jika terbuka (`InsertSketch(true)`), menyeleksi sketch secara eksplisit dengan `SelectByID2(skName, "SKETCH", ...)` (Mark 0), mencoba arah pemotongan normal, arah terbalik (`Dir = !flip`), dan pemotongan dua arah, serta menyediakan fallback ke `FeatureCut3`.
  - Pada `DrawSlot`: memperbaiki koordinat `(x1, y1)` dan `(x2, y2)` pada `CreateSketchSlot`.

### 3. Pengujian Regresi & Validasi
- **Unit Tests**: **176 / 176 Lolos (100% Passed, 0 Failed)**:
  - Menambahkan `CadOperationTypeMappingTests.cs` (12 skenario pengujian baru):
    1. `Test01`: Pemetaan string snake_case (`new_sketch`, `extrude_cut`).
    2. `Test02`: Pemetaan nilai numerik integer (1, 3, 7, 30).
    3. `Test03`: Pengenalan alias kunci (`operation`, `operation_type`, `action`).
    4. `Test04`: Tipe operasi yang hilang otomatis menjadi `OperationType.Unknown` (bukan `NewPart`).
    5. `Test05`: Tipe operasi tak dikenal menghasilkan `Unknown` dan menyimpan `RawTypeString`.
    6. `Test06`: Tipe operasi tidak pernah ditebak dari `op.Id`.
    7. `Test07`: `CadPlanValidator` menolak `OperationType.Unknown` dengan pesan diagnostik.
    8. `Test08`: `CadPlanValidator` menerima rangkaian operasi revisi valid (`CreatePlane`, `NewSketch`, `AddSlot`, `ExtrudeCut`).
    9. `Test09`: `CadContextReconciler` mempertahankan operasi revisi dan `Unknown`, hanya menghapus `NewPart`.
    10. `Test10`: Prompt revisi memuat skema terstruktur dengan `CreatePlane`, `NewSketch`, `AddSlot`/`AddRectangle`, `ExtrudeCut`, dan melarang `NewPart`.
    11. `Test11`: `OperationTypeMapper` mencakup seluruh 33 nilai enum `OperationType`.
    12. `Test12`: `OperationTypeMapper` menangani alias umum (`slot`, `cut`, `boss`, `plane`, dll.).
- **Kompilasi MSBuild x64 Debug**: **100% Berhasil (0 Warning, 0 Error)**.
- **Integritas Sistem**: `credential.dat` (278 bytes), `settings.json` (231 bytes), COM GUID `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` terjaga utuh. Tidak ada push ke GitHub atau auto-deploy.






