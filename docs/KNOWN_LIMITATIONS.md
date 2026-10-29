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
