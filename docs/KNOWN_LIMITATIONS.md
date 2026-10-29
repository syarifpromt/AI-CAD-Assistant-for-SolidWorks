# Batasan Yang Diketahui & Risiko (Known Limitations & Risks)

Dokumen ini mencatat batasan teknis versi saat ini (v1.0 MVP), asumsi, serta mitigasi risiko yang diterapkan dalam sistem.

---

## 1. Batasan Fitur Versi 1.0 (MVP)

1. **Pemodelan Permukaan Bebas (Surfacing / Complex Organic Shapes)**:
   - Versi 1.0 difokuskan pada komponen mekanik prismatik dan rotasional (balok, pelat, poros, lubang, fillet, chamfer). Bentuk permukaan bebas kompleks (sculpted surfaces, lofts non-panduan) belum didukung pada MVP.
2. **Keterbacaan Gambar Buram**:
   - Jika gambar teknik yang diunggah memiliki resolusi rendah atau teks dimensi buram, AI CAD Assistant dirancang untuk **bertanya (klarifikasi)** alih-alih menebak ukuran kritis secara sembarangan.
3. **Ketergantungan Internet untuk Gemini**:
   - Penguraian bahasa natural dan analisis gambar multimodal memerlukan koneksi internet aktif ke endpoint Google Gemini API (`https://generativelanguage.googleapis.com`). Pemodelan geometri lokal tetap dapat berjalan jika CadSpec sudah tersedia.
4. **Assembly Otomatis Tingkat Lanjut**:
   - Dukungan perakitan (assembly) komponen multi-part dan mate constraint lanjutan dijadwalkan pada rilis ekspansi 1.x setelah kestabilan alur pemodelan part tunggal diverifikasi.
5. **Anotasi Manufaktur & GD&T pada Drawing**:
   - Sistem tidak mengarang toleransi geometrik, simbol kekasaran permukaan, atau standar ulir khusus kecuali diberikan secara eksplisit oleh pengguna atau template.

---

## 2. Mitigasi Risiko Rekayasa

| Risiko | Dampak | Mitigasi Yang Diterapkan |
|---|---|---|
| AI menghasilkan dimensi yang keliru dari gambar teknik | Komponen gagal rakit atau salah dimensi | Sistem membedakan kategori dimensi (*Explicit*, *Inferred*, *Unknown*). Dimensi yang hilang memicu dialog klarifikasi sebelum pemodelan dimulai. |
| AI menghasilkan script atau perintah berbahaya | Kerusakan data atau celah keamanan | AI **TIDAK PERNAH** menjalankan kode C#, macro VBA, atau PowerShell bebas. Gemini hanya menghasilkan format data JSON yang divalidasi ketat terhadap whitelist operasi. |
| Kegagalan operasi CAD merusak file pengguna | Kehilangan pekerjaan desain yang sedang dibuka | Sistem membuat checkpoint/backup otomatis di `%APPDATA%\AICADAssistant\checkpoints` sebelum modifikasi model aktif. File baru disimpan sebagai dokumen terpisah secara default. |
| Kebocoran API Key di log atau repositori | Penyalahgunaan kuota atau akun cloud pengguna | API Key disimpan menggunakan enkripsi Windows DPAPI di profil lokal pengguna. File kredensial dan konfigurasi rahasia dikecualikan di `.gitignore`. Log teknis memblokir pencatatan API Key dan data biner gambar. |
| UI membeku (*freeze*) saat memanggil Gemini | Pengalaman pengguna buruk di SOLIDWORKS | Seluruh komunikasi jaringan dilakukan secara asinkron (`Task.Run`, `HttpClient.PostAsync`) di luar UI thread. |

---

## 3. Matriks Verifikasi Lingkungan & SOLIDWORKS

1. **SOLIDWORKS 2025 (Terverifikasi Siap Uji)**:
   - Terpasang secara lokal di `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\` (SP1.0).
   - Seluruh referensi COM interop (`SolidWorks.Interop.sldworks.dll`, `swconst.dll`, `swpublished.dll`) dikompilasi langsung terhadap pustaka resmi SOLIDWORKS 2025.
2. **SOLIDWORKS 2023 & 2024 (Status: Kompatibel Berdasarkan Desain, Belum Terverifikasi Nyata)**:
   - Kode integrasi dirancang untuk kompatibilitas ke belakang (*backward-compatible*) dengan hanya menggunakan antarmuka COM dasar (`ISwAddin`, `ISldWorks`, `IModelDoc2`, `IPartDoc`, `IFeatureManager`).
   - Namun, karena mesin pengujian saat ini hanya memiliki lisensi/instalasi SOLIDWORKS 2025, integrasi pada versi 2023 dan 2024 secara faktual berstatus **belum diverifikasi di lingkungan nyata**.
3. **Pendaftaran Add-In Sistem**:
   - Skrip pendaftaran `scripts/register_addin.ps1` dan pembatalan `scripts/unregister_addin.ps1` telah dipersiapkan menggunakan GUID stabil `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`.
   - Pendaftaran ke Registry Windows (`HKCU\Software\SolidWorks\AddIns`) **tidak dijalankan secara otomatis oleh asisten coding** untuk menjaga integritas sistem pengguna, dan menunggu persetujuan / eksekusi manual oleh pengguna.

---

## 4. Persyaratan & Batasan Google Gemini API

1. **Model Default**:
   - Model default ditetapkan ke `gemini-3.8-flash`. Model alternatif mencakup `gemini-3.5-flash-lite`, `gemini-3.1-pro-preview`, `gemini-2.5-pro`, `gemini-2.5-flash`, dan `gemini-flash-latest`.
   - Nama model di antarmuka Pengaturan bersifat dapat diketik bebas (*editable ComboBox*). Jika Google merilis nama model baru atau mengubah tag rilis, pengguna dapat langsung mengetikkan nama model tersebut tanpa harus mengompilasi ulang add-in.
2. **Keterbatasan Kuota & Rate Limit (RPM / TPM)**:
   - Penggunaan tier gratis Google AI Studio memiliki batasan Request Per Minute (RPM) dan Request Per Day (RPD). Jika kuota habis (HTTP 429), sistem menangani error ini secara tertib dan menampilkan pesan informatif tanpa crashing.
3. **Status Pengujian API Nyata vs Mock**:
   - Unit test otomatis saat ini menguji penanganan error, serialisasi DTO, redaksi API Key, dan logika parsing DTO menggunakan simulasi terisolasi (mock).
   - Pengujian koneksi API nyata (live HTTPS request) memerlukan Google AI Studio API Key valid yang dimasukkan oleh pengguna melalui menu Pengaturan (ikon ⚙) di Task Pane Add-in.


