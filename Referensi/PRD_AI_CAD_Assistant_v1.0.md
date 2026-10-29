# Product Requirements Document (PRD)

## AI CAD Assistant for SolidWorks

**Versi:** 1.0  
**Tanggal:** 29 Oktober 2026  
**Status:** Draft spesifikasi untuk implementasi  
**Pemilik proyek:** Pengguna  
**Platform target:** Windows + SOLIDWORKS 2023, 2024, dan 2025  
**Bahasa antarmuka:** Bahasa Indonesia (utama)  

---

## 1. Ringkasan Produk

AI CAD Assistant adalah add-in berbasis AI yang tampil langsung di dalam SOLIDWORKS. Pengguna dapat menjelaskan komponen mekanik menggunakan teks atau mengunggah gambar teknik, kemudian meminta AI menganalisis kebutuhan, menyusun rencana CAD yang terstruktur, dan membangun model menggunakan SOLIDWORKS API.

Gemini digunakan untuk memahami bahasa natural, menafsirkan gambar, mengidentifikasi dimensi yang terbaca, serta mengusulkan urutan pemodelan. Geometri tidak dibuat langsung oleh Gemini. Geometri dibuat oleh mesin CAD deterministik di dalam add-in melalui operasi SOLIDWORKS API yang diizinkan dan telah divalidasi.

Produk harus membantu pengguna membuat serta merevisi part, merakit komponen, menghasilkan gambar kerja dasar, dan mengekspor model ke STEP/STL. Ketelitian geometri merupakan prioritas. Jika dimensi, hubungan antartampak, atau maksud desain tidak cukup jelas, sistem harus bertanya sebelum menjalankan perubahan yang berisiko.

### 1.1 Prinsip inti

1. **AI merencanakan; SOLIDWORKS membangun geometri.**
2. **Tidak mengarang dimensi teknik.** Nilai yang tidak tersedia harus dikonfirmasi atau diberi label sebagai asumsi eksplisit.
3. **Model harus bisa diedit.** Utamakan sketch, dimensi, dan feature tree native, bukan hanya mesh atau bentuk impor tanpa riwayat fitur.
4. **Perubahan aman dan dapat dipulihkan.** File sumber tidak boleh ditimpa secara default.
5. **Cloud hanya setelah tindakan pengguna.** Berkas tidak dikirim ke Gemini sebelum tombol **Analisis** ditekan.
6. **AI tidak menjalankan kode bebas.** Respons Gemini dipetakan ke schema dan daftar operasi CAD yang diizinkan.

---

## 2. Latar Belakang dan Masalah

Pemodelan mekanik manual di SOLIDWORKS memerlukan waktu untuk mengubah deskripsi, sketsa, atau gambar teknik menjadi sketch, feature, assembly, dan drawing. Interpretasi dimensi dari gambar juga dapat menimbulkan kesalahan jika garis, angka, atau tampak tidak jelas.

AI CAD Assistant ditujukan untuk mempercepat proses dari ide ke model CAD sambil mempertahankan kendali teknik kepada pengguna. Aplikasi harus lebih dari sekadar menghasilkan gambar visual: ia perlu membangun dokumen CAD native, memeriksa hasil rebuild, melaporkan fitur yang gagal, dan mendukung revisi melalui percakapan.

---

## 3. Tujuan Produk

### 3.1 Tujuan utama

- Menyediakan panel AI di dalam jendela SOLIDWORKS.
- Mengubah instruksi teks menjadi rencana model CAD terstruktur.
- Menganalisis gambar teknik JPG, PNG, WebP, dan PDF setelah pengguna menekan **Analisis**.
- Membuat komponen mekanik native yang dapat diedit melalui SOLIDWORKS API.
- Memungkinkan revisi model melalui chat secara bertahap.
- Membuat assembly dari komponen yang dibuat AI maupun dari file part yang telah tersedia.
- Membuat gambar kerja dasar dan mengekspor hasil ke format yang ditetapkan.
- Mendukung SOLIDWORKS 2023, 2024, dan 2025 setelah pengujian per versi.
- Menjaga file asli, API key, dan data pengguna.

### 3.2 Bukan tujuan versi awal

- Menjamin bahwa setiap sketsa tangan dapat diterjemahkan menjadi model manufaktur yang benar tanpa klarifikasi.
- Menghasilkan rancangan tersertifikasi untuk komponen keselamatan kritis, pressure vessel, atau penggunaan regulated tanpa pemeriksaan insinyur.
- Menghasilkan toleransi, GD&T, material, heat treatment, atau kekasaran permukaan yang tidak diberikan pengguna atau tidak berasal dari standar/template yang dipilih.
- Menggantikan penilaian engineer atau validasi manufaktur.
- Menjalankan macro, script, atau kode arbitrer hasil AI.
- Pemrosesan AI sepenuhnya offline. Analisis Gemini memerlukan internet.

---

## 4. Target Pengguna dan Skenario Penggunaan

### 4.1 Target pengguna

Pengguna pribadi yang menggunakan SOLIDWORKS pada komputer Windows, terutama untuk desain dan pembelajaran komponen mekanik.

### 4.2 Skenario utama

**A. Desain dari teks**  
Pengguna mengetik: “Buat pelat 100 × 60 mm dengan ketebalan 5 mm dan empat lubang diameter 8 mm, masing-masing berjarak 10 mm dari tepi.” AI mengidentifikasi dimensi, memvalidasi konsistensi, membuat rencana fitur, lalu menjalankan pembuatan part.

**B. Desain dari gambar teknik**  
Pengguna memilih gambar JPG/PNG/WebP/PDF, menekan **Analisis**, dan menerima interpretasi tampak, dimensi terbaca, fitur, serta daftar ketidakpastian. Jika diperlukan, AI bertanya. Setelah spesifikasi cukup lengkap, model dibuat.

**C. Revisi melalui chat**  
Pengguna mengetik “Ubah ketebalan pelat menjadi 6 mm” atau “Tambahkan fillet 2 mm pada empat tepi luar.” Aplikasi memetakan permintaan ke feature/dimensi yang relevan, membuat checkpoint, menerapkan perubahan, lalu memeriksa rebuild.

**D. Membuat assembly**  
Pengguna meminta komponen baru dan/atau memilih part yang telah tersedia. AI mengusulkan susunan komponen serta mate. Jika hubungan atau orientasi belum jelas, sistem meminta konfirmasi.

**E. Membuat gambar kerja dan ekspor**  
Pengguna meminta drawing dengan tampak standar, dimensi yang telah diketahui, dan template yang dipilih. Setelah diperiksa, pengguna dapat menyimpan drawing serta mengekspor STEP/STL.

---

## 5. Cakupan Fitur

### 5.1 Versi 1.0 — MVP yang dapat diuji

MVP harus memprioritaskan satu alur lengkap yang benar-benar bekerja di SOLIDWORKS, lalu memperluasnya secara bertahap.

- Add-in COM native yang dapat dimuat melalui menu Add-Ins SOLIDWORKS.
- Panel task pane berbahasa Indonesia dengan tema merah-putih-abu-abu.
- Chat teks dengan riwayat percakapan untuk sesi aktif.
- Pengaturan Gemini API key dan pemilihan model melalui konfigurasi.
- Pengiriman teks/gambar ke Gemini hanya sesudah tindakan **Analisis** atau **Kirim** yang jelas dari pengguna.
- Input gambar JPG, PNG, WebP, dan PDF; file diproses lokal untuk validasi jenis, ukuran, orientasi, dan pratinjau sebelum dikirim.
- Structured CAD Plan dalam JSON dengan schema terdefinisi.
- Validasi parameter geometri sebelum operasi SOLIDWORKS dimulai.
- Pembuatan part mekanik dasar: sketch garis/persegi/persegi panjang/lingkaran, dimensi, extruded boss/base, cut-extrude, hole sederhana, fillet, chamfer, dan pattern sederhana sesuai dukungan API yang telah diuji.
- Pembuatan model dengan feature tree native; sistem tidak boleh mengklaim model sepenuhnya parametrik jika sebenarnya menggunakan geometri impor atau mesh.
- Rebuild dan pemeriksaan status fitur setelah operasi.
- Revisi bertahap melalui chat pada fitur yang dapat diidentifikasi dengan jelas.
- Backup/checkpoint sebelum perubahan yang mengubah model.
- Simpan sebagai file baru secara default; jangan menimpa file sumber secara otomatis.
- Ekspor STEP dan STL untuk model yang berhasil divalidasi.
- Log teknis tanpa menyimpan API key, isi file gambar, atau payload sensitif secara default.

### 5.2 Versi 1.x — Perluasan setelah MVP stabil

- Assembly dari komponen yang baru dibuat maupun file part yang sudah ada.
- Penempatan komponen dan mate standar: coincident, concentric, parallel, perpendicular, distance, angle, dan mate lain yang telah diuji.
- Drawing dasar `.SLDDRW`: tampak depan/atas/samping/isometrik, skala, dan dimensi yang tervalidasi.
- Pilihan template drawing dan standar gambar.
- Ekspor PDF drawing bila diaktifkan pengguna.
- Dukungan lebih luas untuk pattern, shell, revolved features, sweep/loft sederhana, dan fitur mekanik lain setelah implementasi serta pengujian.
- Mode Concept untuk bentuk yang belum terdefinisi lengkap dan Engineering Mode untuk model yang menuntut dimensi eksplisit.

### 5.3 Pengembangan lanjutan

- Gambar manufaktur dengan toleransi, GD&T, simbol ulir, kekasaran, dan anotasi lain hanya bila dasar nilainya jelas.
- Pustaka template komponen dan operasi yang dapat diperluas.
- Pemeriksaan interferensi assembly dan laporan masalah geometri.
- Ekspor format tambahan bila kebutuhan dan API mendukungnya.

### 5.4 Format hasil

| Format | Tujuan | Target tahap |
|---|---|---|
| `.SLDPRT` | Part native yang dapat diedit | MVP |
| `.SLDASM` | Assembly native | Perluasan 1.x |
| `.SLDDRW` | Gambar kerja SOLIDWORKS | Perluasan 1.x |
| `.STEP` / `.STP` | Pertukaran model solid/surface | MVP untuk model yang tervalidasi |
| `.STL` | Mesh untuk pencetakan 3D atau alur kerja mesh | MVP untuk model yang tervalidasi |
| `.PDF` | Hasil ekspor drawing | Perluasan 1.x |

---

## 6. Pengalaman Pengguna dan UI/UX

### 6.1 Penempatan panel

Panel ditampilkan melalui Task Pane di dalam SOLIDWORKS dan dapat dibuka/ditutup lewat tombol pada CommandManager atau tab yang disediakan add-in.

### 6.2 Arah visual

- Tema merah, putih, dan abu-abu yang senada dengan lingkungan SOLIDWORKS.
- Merah digunakan sebagai warna aksen untuk tombol utama, status aktif, dan tindakan penting; bukan untuk semua latar.
- Putih/abu-abu untuk permukaan, riwayat chat, area input, dan kartu informasi.
- Ikon sederhana, tipografi mudah dibaca, jarak antarelemen rapi, dan status operasi terlihat jelas.
- Semua teks UI utama dalam Bahasa Indonesia.
- Warna harus dapat disesuaikan lewat resource/theme agar tidak bergantung pada satu kode warna tetap.

### 6.3 Komponen panel

1. Header: nama aplikasi, indikator koneksi SOLIDWORKS, dan status Gemini.
2. Mode: **Desain dari Teks**, **Analisis Gambar**, **Revisi Model**.
3. Area percakapan: permintaan, respons AI, klarifikasi, status operasi.
4. Area lampiran: nama file, jenis file, pratinjau, tombol hapus.
5. Tombol utama: **Analisis**, **Buat Model**, **Terapkan Revisi**, **Batalkan**, dan **Ekspor**; tampil sesuai konteks.
6. Kartu spesifikasi: dimensi, satuan, fitur, asumsi, serta bagian yang belum diketahui.
7. Panel hasil: operasi yang dijalankan, status rebuild, peringatan, dan lokasi file.
8. Pengaturan: API key, model Gemini, unit default, direktori output, dan preferensi konfirmasi.

### 6.4 Status operasi

Gunakan status yang jelas: Siap, Menganalisis, Menunggu Klarifikasi, Menyusun Rencana, Memvalidasi, Membuat Model, Membangun Ulang, Berhasil, Perlu Perhatian, Gagal, dan Dibatalkan. UI tidak boleh terlihat membeku ketika Gemini sedang dipanggil.

---

## 7. Alur Kerja Fungsional

### 7.1 Instalasi dan pemuatan add-in

1. Pengguna menjalankan installer atau mengikuti prosedur pengembangan lokal.
2. Add-in diregistrasikan dan muncul di daftar Add-Ins SOLIDWORKS.
3. Saat dimuat, add-in memperoleh objek aplikasi SOLIDWORKS, menambahkan tombol/panel, dan memeriksa status koneksi.
4. Jika versi SOLIDWORKS tidak didukung atau interop gagal, add-in menampilkan pesan yang dapat ditindaklanjuti dan tidak melakukan modifikasi dokumen.

### 7.2 Alur teks ke model

1. Pengguna menulis permintaan.
2. Gemini menyusun spesifikasi CAD terstruktur; tidak ada operasi CAD yang langsung dijalankan dari teks bebas.
3. CAD Plan Validator memeriksa schema, satuan, nilai positif, batas parameter, dependensi fitur, dan data yang hilang.
4. Jika ada dimensi atau hubungan penting yang ambigu, sistem bertanya dan menunggu jawaban.
5. Jika plan valid, sistem menjalankan operasi dalam urutan deterministik.
6. Setiap operasi memeriksa hasil dan status rebuild.
7. Jika berhasil, sistem menampilkan ringkasan dan menyimpan model ke lokasi yang dipilih.

### 7.3 Alur gambar ke model

1. Pengguna memilih JPG, PNG, WebP, atau PDF.
2. Add-in menampilkan nama file, pratinjau, ukuran, jumlah halaman (PDF), dan status validasi lokal.
3. File **tidak dikirim** ke Gemini sebelum pengguna menekan **Analisis**.
4. Add-in mengirim file yang dipilih bersama instruksi ekstraksi dimensi dan bentuk ke Gemini API.
5. Gemini mengembalikan geometri yang teridentifikasi, dimensi terbaca, satuan yang diperkirakan, tampak yang dikenali, dan daftar ketidakpastian dalam struktur terdefinisi.
6. Validator membedakan dimensi eksplisit, hasil interpretasi, dan informasi yang belum diketahui.
7. Sistem meminta klarifikasi untuk ukuran kritis yang hilang, angka yang tidak terbaca, konflik antartampak, atau orientasi yang tidak pasti.
8. Setelah spesifikasi lengkap/terkonfirmasi, CAD Plan dibuat dan dieksekusi.
9. Sistem menampilkan peringatan bila hasil merupakan perkiraan konsep, bukan desain manufaktur yang terverifikasi.

### 7.4 Alur revisi chat

1. Pengguna memilih dokumen aktif dan memberikan permintaan revisi.
2. Sistem mengidentifikasi komponen, sketch, dimensi, atau feature target melalui nama/ID internal yang stabil jika tersedia.
3. Sistem menyimpan checkpoint sebelum perubahan.
4. Revisi sederhana yang tidak ambigu dijalankan; perubahan yang berpotensi mengubah geometri utama atau merusak referensi harus diklarifikasi.
5. Model dibangun ulang dan divalidasi.
6. Jika gagal, sistem melakukan paling banyak dua percobaan pemulihan otomatis yang dibatasi pada operasi yang diizinkan.
7. Jika masih gagal, sistem menghentikan proses, mempertahankan checkpoint, dan melaporkan operasi yang gagal serta langkah pemulihan yang disarankan.

### 7.5 Alur assembly

1. Pengguna memilih file part yang ada dan/atau meminta komponen baru.
2. Sistem memeriksa file yang dipilih dan membuat daftar komponen.
3. Gemini mengusulkan relasi penempatan berdasarkan instruksi, geometri, dan informasi yang tersedia.
4. Mate yang ambigu—misalnya arah pemasangan, referensi permukaan, atau jarak—harus dikonfirmasi.
5. Add-in menambahkan komponen dan mate melalui SOLIDWORKS API.
6. Sistem rebuild assembly, mendeteksi mate yang gagal, dan melaporkan konflik.

### 7.6 Alur gambar kerja dan ekspor

1. Pengguna memilih part/assembly dan template drawing.
2. Sistem menghasilkan tampak standar serta menempatkan dimensi yang dapat dirujuk ke model.
3. Sistem tidak mengarang toleransi, GD&T, material, atau simbol manufaktur.
4. Pengguna memeriksa drawing sebelum menyimpan.
5. STEP/STL/PDF diekspor melalui mekanisme ekspor SOLIDWORKS yang sesuai dan diverifikasi dengan keberhasilan penyimpanan.

---

## 8. Arsitektur Teknis

### 8.1 Komponen

**A. SOLIDWORKS Add-in (C#/.NET)**  
Menangani lifecycle add-in, CommandManager, Task Pane, dokumen aktif, serta koordinasi operasi CAD.

**B. UI Layer (WPF)**  
Menampilkan chat, lampiran, spesifikasi, status, pengaturan, dan dialog klarifikasi. Logika geometri tidak boleh berada di code-behind UI.

**C. Gemini Client**  
Mengirim request melalui HTTPS menggunakan API key dari penyimpanan lokal yang terlindungi. Endpoint/model harus dapat dikonfigurasi agar aplikasi tidak bergantung pada satu nama model yang mungkin berubah.

**D. Image/PDF Intake**  
Memvalidasi jenis file, ukuran, orientasi, halaman, dan pratinjau. Format gambar dapat dinormalisasi ke format input yang kompatibel tanpa mengubah gambar asli di disk.

**E. CAD Planner**  
Memetakan percakapan dan hasil analisis menjadi `CadSpec` dan daftar operasi yang diketahui. Respons Gemini harus diparse sebagai data, bukan kode.

**F. CAD Plan Validator**  
Memeriksa tipe operasi, parameter, satuan, batas nilai, urutan feature, dimensi yang hilang, serta dependensi.

**G. SOLIDWORKS Operation Engine**  
Menjalankan daftar operasi CAD yang diizinkan melalui SOLIDWORKS API. Semua operasi COM harus dijalankan pada thread/apartment yang sesuai dengan SOLIDWORKS; pemanggilan jaringan tidak boleh memblokir UI.

**H. Model Verification**  
Memeriksa keberhasilan operasi, rebuild, error/warning feature, jumlah body, ukuran bounding box, volume, dan properti geometris yang relevan.

**I. Recovery and Logging**  
Menyimpan checkpoint, mencatat metadata operasi, waktu, status, serta kode error. Isi gambar dan API key tidak ditulis ke log.

### 8.2 Diagram alur

```text
Pengguna
  -> WPF Task Pane
  -> Gemini Client (hanya setelah tindakan pengguna)
  -> CadSpec JSON
  -> Schema + semantic validation
  -> CadPlan (whitelist operasi)
  -> SOLIDWORKS Operation Engine
  -> Rebuild + Geometry Verification
  -> Hasil / klarifikasi / recovery
  -> SLDPRT / SLDASM / SLDDRW / STEP / STL
```

### 8.3 Pola data CAD

CAD Plan sekurang-kurangnya memuat:

- `request_id`: pengenal operasi.
- `document_type`: part, assembly, atau drawing.
- `units`: satuan sumber dan satuan internal.
- `source`: text atau image/document.
- `explicit_dimensions`: dimensi yang secara eksplisit diberikan pengguna atau terbaca pada gambar.
- `inferred_values`: interpretasi AI yang harus ditandai dan dikonfirmasi bila memengaruhi ukuran kritis.
- `unknowns`: informasi yang perlu ditanyakan.
- `operations`: daftar operasi dari whitelist dengan ID, parameter, referensi, dan dependensi.
- `validation_rules`: batas dan kondisi yang harus terpenuhi.
- `export_targets`: format output yang diminta.

Contoh struktur konseptual (bukan schema produksi final):

```json
{
  "documentType": "part",
  "units": "mm",
  "operations": [
    {
      "id": "base_extrude",
      "type": "ExtrudedBoss",
      "profile": "base_rectangle",
      "depth": 5
    }
  ],
  "unknowns": [],
  "requiresConfirmation": false
}
```

Schema produksi harus membatasi `type` hanya ke operasi yang benar-benar sudah diimplementasikan, memakai `additionalProperties: false` bila sesuai, serta menjalankan validasi semantik di aplikasi. JSON valid bukan bukti bahwa rencana geometri benar.

### 8.4 Satuan dan sistem koordinat

- Unit antarmuka default: millimeter (mm).
- Simpan satuan sumber dan lakukan konversi di satu modul terpusat.
- Konversi ke unit internal SOLIDWORKS dilakukan secara eksplisit; jangan menyebarkan konstanta konversi di setiap feature builder.
- Sudut, koordinat, arah sketch, normal bidang, dan orientasi komponen harus memiliki definisi konsisten.
- Sistem tidak boleh mengasumsikan semua gambar menggunakan skala 1:1. Skala gambar bukan pengganti dimensi eksplisit.

---

## 9. Integrasi Gemini

### 9.1 Fungsi Gemini

- Memahami instruksi desain dalam Bahasa Indonesia.
- Menginterpretasikan gambar dan dokumen PDF.
- Mengidentifikasi fitur dan dimensi yang terlihat.
- Menghasilkan pertanyaan klarifikasi.
- Menghasilkan struktur data CAD sesuai schema.
- Membantu menyusun revisi atas spesifikasi model yang ada.

### 9.2 Prinsip integrasi

- Gunakan Gemini API melalui HTTPS.
- Gunakan structured output/JSON schema jika tersedia untuk model/endpoint yang dipilih.
- Validasi output lagi di sisi aplikasi; jangan menganggap output terstruktur otomatis benar secara teknis.
- Nama model disimpan di pengaturan dan tidak di-hard-code pada banyak file.
- Tangani timeout, rate limit, masalah jaringan, respons kosong, data tidak sesuai schema, dan error autentikasi.
- Terapkan retry terbatas hanya untuk error sementara; jangan mengulang tanpa batas.
- Tampilkan pesan yang jelas jika internet atau API tidak tersedia.

### 9.3 Pemilihan model dan biaya

Pemilihan model harus dapat dikonfigurasi. Model cepat dapat digunakan untuk ekstraksi awal atau permintaan sederhana, sementara model yang lebih kuat dapat digunakan untuk gambar kompleks atau revisi yang memerlukan reasoning lebih tinggi, bila tersedia dan biaya/latensinya sesuai.

Aplikasi sebaiknya menampilkan indikasi bahwa pemrosesan cloud mungkin memerlukan biaya atau memiliki kuota sesuai akun/API pengguna. Jangan menjanjikan waktu respons yang tetap karena waktu bergantung pada model, ukuran file, jaringan, kuota, dan kompleksitas permintaan.

---

## 10. Akurasi Geometri dan Validasi

### 10.1 Aturan wajib

- Dimensi eksplisit harus dipertahankan tanpa perubahan diam-diam.
- Dimensi yang hilang untuk bentuk atau fungsi penting harus ditanyakan sebelum pembuatan model presisi.
- Konflik dimensi harus dilaporkan; sistem tidak boleh memilih nilai secara diam-diam.
- Informasi yang hanya diperkirakan dari sketsa harus diberi label sebagai hasil interpretasi.
- Model harus menjalani rebuild sebelum dinyatakan berhasil.
- Feature yang gagal, suppressed tanpa rencana, sketch terbuka yang tidak sesuai, atau solid body kosong harus menghasilkan status gagal/perlu perhatian.
- Ukuran bounding box, volume, jumlah body, serta dimensi penting harus diperiksa jika relevan.
- Validasi API tidak dapat menjamin semua aspek manufaktur, kekuatan, fungsi, atau kepatuhan standar.

### 10.2 Kategori kepastian

1. **Eksplisit:** diberikan teks pengguna atau tercantum jelas pada gambar.
2. **Terinterpretasi:** dibaca/ditafsirkan AI dan perlu diperiksa.
3. **Tidak diketahui:** tidak ada dasar yang cukup; wajib ditanyakan bila memengaruhi geometri penting.

### 10.3 Pengujian geometri

Test suite minimal meliputi:

- Balok dengan dimensi diketahui.
- Silinder/poros dengan diameter dan panjang tertentu.
- Pelat dengan lubang berposisi dan berdiameter diketahui.
- Bracket sederhana dengan beberapa feature.
- Revisi satu dimensi dan rebuild.
- Input gambar dengan dimensi lengkap.
- Input gambar buram/ambigu yang harus memicu klarifikasi.
- Feature gagal yang harus memicu recovery dan menjaga checkpoint.

---

## 11. Keamanan, Privasi, dan Integritas File

### 11.1 API key

- Jangan menaruh API key di source code, file project yang dikomit, atau log.
- Gunakan Windows Credential Manager atau penyimpanan lokal yang dilindungi DPAPI untuk API key.
- Berikan tombol uji koneksi dan hapus/ganti API key.
- `.gitignore` harus mengecualikan file konfigurasi lokal yang berisi rahasia.

### 11.2 Pengiriman data ke Gemini

- File hanya dikirim setelah pengguna menekan tombol **Analisis**.
- Sebelum dikirim, tampilkan file yang akan dikirim dan tujuan pemrosesan cloud.
- Tidak boleh ada upload otomatis saat file baru dipilih atau di-drag ke panel.
- Jangan menyimpan gambar atau payload request di log secara default.
- Pengguna bertanggung jawab untuk tidak mengirim gambar rahasia yang tidak boleh diproses oleh layanan cloud.

### 11.3 Keselamatan perubahan CAD

- Simpan dokumen baru atau checkpoint sebelum perubahan signifikan.
- Jangan menimpa file asli secara default.
- Batasi operasi ke whitelist CAD yang disetujui.
- Jangan menjalankan kode shell, macro, script, file executable, atau ekspresi arbitrer yang dikirim Gemini.
- Sediakan tombol **Batalkan**; pembatalan dilakukan di batas operasi yang aman.
- Batasi pemulihan otomatis maksimal dua percobaan per operasi gagal pada versi awal.
- Jika pemulihan gagal, berhenti dan pertahankan model/checkpoint yang valid terakhir.

---

## 12. Kompatibilitas dan Lingkungan Pengembangan

### 12.1 Target

- SOLIDWORKS 2023
- SOLIDWORKS 2024
- SOLIDWORKS 2025
- Windows 64-bit dengan SOLIDWORKS terpasang dan memiliki izin menjalankan add-in.
- Koneksi internet diperlukan untuk Gemini API.

### 12.2 Bahasa dan framework

- C#/.NET untuk COM Add-in.
- WPF untuk Task Pane.
- SOLIDWORKS interop assemblies/API dari instalasi atau SDK resmi yang sesuai.
- `HttpClient` untuk komunikasi HTTPS dengan Gemini REST API, kecuali SDK C# resmi yang sesuai tersedia dan dipilih secara eksplisit.

Target framework .NET harus diputuskan dan diuji pada lingkungan SOLIDWORKS tertua yang didukung. Jangan mengasumsikan semua versi SOLIDWORKS memuat add-in dengan target framework yang sama tanpa pengujian. Untuk kompatibilitas 2023–2025, baseline API dan interop perlu dipilih dengan sengaja dan diuji pada tiap versi.

### 12.3 VS Code dan Antigravity

VS Code dan Antigravity dipakai untuk menulis, meninjau, menguji, dan memperbaiki kode. Keduanya tidak menggantikan kebutuhan untuk membangun/registrasi COM Add-in serta menjalankan pengujian pada komputer Windows dengan SOLIDWORKS yang benar-benar terpasang.

### 12.4 Strategi kompatibilitas

- Gunakan hanya API yang tersedia pada baseline yang ditetapkan untuk semua target versi, kecuali fitur diberi guard berdasarkan versi.
- Hindari ketergantungan pada method/property baru tanpa pengecekan versi.
- Jalankan test instalasi, load/unload add-in, model sederhana, drawing, dan ekspor pada masing-masing versi target.
- Simpan hasil uji per versi dan service pack yang dipakai.
- Kompatibilitas dinyatakan **terverifikasi** hanya untuk kombinasi versi yang benar-benar diuji.

---

## 13. Kebutuhan Nonfungsional

### 13.1 Responsivitas

- Pemanggilan Gemini tidak boleh membekukan seluruh UI.
- Status proses dan tombol pembatalan harus tersedia saat request atau operasi sedang berlangsung.
- Operasi COM harus dijalankan pada thread/apartment yang sesuai; hasil jaringan dikembalikan dengan mekanisme marshalling yang aman.

### 13.2 Keandalan

- Semua operasi CAD harus menghasilkan status berhasil/gagal yang dapat ditelusuri.
- Tidak boleh menyatakan model berhasil bila rebuild gagal atau file tidak tersimpan.
- Retry dan recovery harus memiliki batas.
- Error log harus menyertakan kode operasi dan tahap yang gagal, tetapi tidak menyimpan rahasia atau payload sensitif.

### 13.3 Kemudahan pemeliharaan

- Pisahkan UI, Gemini client, CAD planner, validator, SolidWorks operation engine, export, serta logging.
- Gunakan schema versi untuk CAD Plan.
- Semua operasi CAD harus memiliki unit test untuk validasi input dan integration test di SOLIDWORKS.
- Konfigurasi model, unit, warna tema, dan direktori output harus dapat diubah tanpa mengubah kode utama.

### 13.4 Performa sebagai target uji

Target awal berikut adalah sasaran pengujian pada komputer referensi dan jaringan yang stabil, bukan jaminan universal:

- Prompt teks sederhana dengan spesifikasi lengkap: pembuatan model dasar ditargetkan selesai dalam 30 detik setelah respons AI diterima.
- Gambar teknik 1–3 halaman yang jelas: analisis awal ditargetkan selesai dalam 60 detik pada kondisi jaringan/model yang wajar.
- Waktu setiap tahap ditampilkan sehingga keterlambatan dapat dibedakan antara Gemini, operasi CAD, rebuild, dan ekspor.
- Uji performa dilakukan pada kumpulan prompt dan gambar referensi yang sama, bukan hanya demonstrasi tunggal.

---

## 14. Risiko dan Mitigasi

| Risiko | Dampak | Mitigasi |
|---|---|---|
| Dimensi salah dibaca Gemini | Model tidak akurat | Klarifikasi, kategori kepastian, validasi, dan perbandingan ke gambar sumber |
| Dimensi gambar tidak lengkap | Geometri tidak dapat ditentukan unik | Tanya pengguna; jangan menebak ukuran kritis |
| API SOLIDWORKS berbeda menurut versi | Add-in gagal dimuat atau fitur berbeda | Baseline API, pengecekan versi, dan test matrix 2023–2025 |
| COM/threading tidak benar | SOLIDWORKS freeze/crash | Pisahkan pekerjaan jaringan dari operasi COM; marshal operasi ke thread yang tepat |
| Response Gemini bukan JSON valid atau salah secara semantik | CAD plan gagal | Structured output, parser, schema validation, semantic validation, bounded retry |
| API/network/quota bermasalah | Analisis tidak selesai | Pesan error jelas, retry terbatas, opsi mencoba ulang manual |
| Perubahan merusak model aktif | Kehilangan pekerjaan | Checkpoint, save-as default, recovery terbatas |
| API key bocor | Penyalahgunaan akun/biaya | Credential Manager/DPAPI, redaksi log, tidak commit rahasia |
| Ekspektasi semua desain otomatis 100% akurat | Kesalahan manufaktur | Nyatakan batas kemampuan, minta pemeriksaan pengguna, jangan mengklaim validasi manufaktur penuh |
| Cakupan terlalu besar untuk versi pertama | Jadwal dan debugging membengkak | Implementasi bertahap dengan MVP dan acceptance criteria yang ketat |

---

## 15. Kriteria Penerimaan (Acceptance Criteria)

MVP dianggap dapat diuji ketika seluruh kriteria berikut terpenuhi:

1. Add-in dapat dimuat, menampilkan panel WPF, lalu di-unload tanpa meninggalkan proses/background task yang tidak perlu pada versi SOLIDWORKS yang diuji.
2. Pengguna dapat memasukkan API key secara lokal; key tidak muncul di source code, log, atau file repository.
3. Pemilihan gambar tidak mengirim file ke Gemini sampai pengguna menekan **Analisis**.
4. Pengguna dapat meminta pembuatan balok/pelat dengan dimensi eksplisit dan memperoleh part native `.SLDPRT`.
5. Model dasar memiliki feature tree yang sesuai rencana dan berhasil rebuild.
6. Sistem menolak nilai tidak valid, misalnya panjang nol/negatif atau parameter wajib yang hilang.
7. Input dengan dimensi penting yang tidak terbaca menyebabkan sistem meminta klarifikasi, bukan membuat ukuran secara diam-diam.
8. Revisi dimensi yang didukung melalui chat mengubah model, melakukan rebuild, dan melaporkan hasil.
9. Saat feature gagal, sistem berhenti dalam batas percobaan yang ditetapkan, mempertahankan checkpoint, dan memberi pesan error yang dapat ditindaklanjuti.
10. Model yang berhasil dapat diekspor ke STEP/STL dan file hasil benar-benar ada di lokasi tujuan.
11. UI tetap responsif selama request Gemini berlangsung serta menampilkan status proses.
12. Tema panel mengikuti arah visual merah-putih-abu-abu dan teks utama berbahasa Indonesia.
13. Pengujian dasar tercatat untuk SOLIDWORKS 2023, 2024, dan 2025 sebelum kompatibilitas versi tersebut diumumkan.

Fitur assembly serta drawing dasar memiliki acceptance test tambahan pada fase implementasinya dan tidak boleh ditandai selesai hanya berdasarkan mockup UI.

---

## 16. Rencana Implementasi

### Fase 0 — Validasi lingkungan

- Konfirmasi instalasi SOLIDWORKS, versi/service pack, dan akses API/interop.
- Pastikan toolchain .NET di Windows dapat membangun COM Add-in.
- Buat proof of concept yang bisa load/unload dan menampilkan panel kosong.

**Keluaran:** add-in minimal berjalan pada satu versi SOLIDWORKS yang tersedia.

### Fase 1 — CAD engine deterministik

- Buat satu part sederhana tanpa Gemini.
- Implementasikan unit conversion, operasi extrude/cut dasar, rebuild, save, dan export.
- Tambahkan geometry validator serta integration test.

**Keluaran:** pembuatan balok/pelat yang berulang dan dapat divalidasi.

### Fase 2 — Gemini dan chat teks

- Tambahkan Gemini client, pengelolaan API key, timeout, dan pesan error.
- Definisikan `CadSpec` dan JSON schema.
- Tambahkan validasi sebelum eksekusi dan revisi dasar melalui chat.

**Keluaran:** prompt teks menjadi part dasar yang benar pada kumpulan test yang ditentukan.

### Fase 3 — Gambar teknik

- Tambahkan intake/pratinjau gambar dan PDF.
- Tambahkan ekstraksi dimensi/fitur, ketidakpastian, dan dialog klarifikasi.
- Buat evaluasi menggunakan kumpulan gambar referensi berlabel dimensi.

**Keluaran:** gambar yang tidak jelas memicu klarifikasi, dan gambar yang cukup jelas dapat membangun bentuk dalam cakupan fitur yang didukung.

### Fase 4 — Assembly dan drawing dasar

- Tambahkan pembuatan/penempatan komponen dan mate.
- Tambahkan drawing, tampak standar, dimensi yang diketahui, template, dan ekspor.

**Keluaran:** assembly/drawing dasar yang berhasil direbuild dan diverifikasi.

### Fase 5 — Kompatibilitas, packaging, dan rilis

- Pengujian pada SOLIDWORKS 2023, 2024, 2025.
- Pengujian error/recovery dan UI pada tiap versi target.
- Buat installer, petunjuk API key, backup, log, serta troubleshooting.

**Keluaran:** paket instalasi pribadi dengan dokumentasi dan batas kompatibilitas yang terverifikasi.

---

## 17. Pertanyaan Teknis yang Masih Menjadi Prasyarat

Keputusan produk utama telah ditetapkan. Hal berikut tidak perlu ditanyakan ulang untuk menyusun PRD, tetapi harus diverifikasi ketika implementasi dimulai:

1. Service pack dan instalasi aktual SOLIDWORKS pada komputer pengembangan.
2. Target .NET yang benar-benar kompatibel dengan baseline SOLIDWORKS 2023–2025.
3. Ketersediaan interop assemblies/API SDK untuk instalasi tersebut.
4. Model Gemini yang dipilih dan kuota/biaya API pada akun pengguna.
5. Template part, assembly, dan drawing default; bila belum ada, aplikasi perlu menyediakan pilihan template lokal dan meminta pengguna memilih template yang tersedia.
6. Performa pada perangkat sebenarnya; target waktu di dokumen ini harus divalidasi secara empiris.

---

## 18. Asumsi dan Keputusan Default

- Penggunaan awal bersifat pribadi pada satu komputer Windows.
- Panel dan kontrol utama menggunakan Bahasa Indonesia.
- Satuan default adalah mm, dengan konversi terpusat ke satuan internal SOLIDWORKS.
- Tema UI menggunakan aksen merah, latar putih, dan permukaan abu-abu; warna dapat diubah.
- Analisis gambar memerlukan tindakan manual pengguna untuk mengirim file ke Gemini.
- File sumber tidak ditimpa secara otomatis.
- Fitur manufaktur tingkat lanjut ditunda sampai pemodelan dasar, validasi, recovery, dan kompatibilitas stabil.
- Tidak ada klaim akurasi absolut. Akurasi dievaluasi melalui test suite geometri dan pemeriksaan model.

---

## 19. Referensi Teknis Resmi

Referensi berikut menjadi titik awal verifikasi saat implementasi. Detail API dan model Gemini perlu diperiksa kembali pada saat coding karena dapat berubah.

1. SOLIDWORKS API 2025 — Standalone dan Add-in Applications Overview: https://help.solidworks.com/2025/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Standalone_and_Add-in_Applications_Overview.htm
2. SOLIDWORKS API 2023 — API Getting Started Overview: https://help.solidworks.com/2023/english/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Getting_Started_Overview.htm
3. SOLIDWORKS API 2025 — ISldWorks interface: https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks.html
4. SOLIDWORKS API 2023 — IAssemblyDoc interface: https://help.solidworks.com/2023/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IAssemblyDoc.html
5. SOLIDWORKS API 2025 — SaveAs2 method: https://help.solidworks.com/2025/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~saveas2.html
6. Gemini API — Image understanding: https://ai.google.dev/gemini-api/docs/image-understanding
7. Gemini API — Document/PDF processing: https://ai.google.dev/gemini-api/docs/document-processing
8. Gemini API — Structured outputs: https://ai.google.dev/gemini-api/docs/structured-output

---

## 20. Definisi Selesai untuk PRD

PRD ini menjadi dasar implementasi setelah struktur solution, baseline .NET, daftar operasi CAD yang didukung, schema `CadSpec`, dan test suite pertama disetujui. Keberhasilan versi awal ditentukan oleh pembuatan part sederhana yang terukur, dapat diedit, lolos rebuild, aman terhadap kegagalan, dan dapat diekspor—bukan berdasarkan kemampuan menghasilkan bentuk yang tampak meyakinkan saja.
