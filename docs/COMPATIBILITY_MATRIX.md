# Matriks Kompatibilitas SOLIDWORKS (Compatibility Matrix)

Dokumen ini mencatat status pengujian dan dukungan versi SOLIDWORKS untuk AI CAD Assistant.

---

## 1. Matriks Dukungan Versi

| Versi SOLIDWORKS | Target .NET Framework | Interop Assembly | Status Verifikasi | Catatan |
|---|---|---|---|---|
| **SOLIDWORKS 2023** | .NET Framework 4.8 | 2023 Interop | Direncanakan (Unverified) | Kompatibel dengan baseline API yang dipilih (`ISwAddin`, `FeatureExtrusion3`, `FeatureCut4`, `SaveAs`). Memerlukan pengujian manual di mesin dengan SW 2023. |
| **SOLIDWORKS 2024** | .NET Framework 4.8 | 2024 Interop | Direncanakan (Unverified) | Kompatibel dengan baseline API yang dipilih. Memerlukan pengujian manual di mesin dengan SW 2024. |
| **SOLIDWORKS 2025** | .NET Framework 4.8 | 2025 Interop (SP1.0) | **Terverifikasi (Build & Interop Valid)** | Dikompilasi langsung menggunakan Interop DLL resmi dari `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\`. |

---

## 2. Baseline API Yang Digunakan

Untuk memastikan kompatibilitas di ketiga versi (2023–2025), implementasi sengaja membatasi pemanggilan API ke metode yang tersedia dan stabil di seluruh versi:

1. **Add-in Lifecycle**:
   - `SolidWorks.Interop.swpublished.ISwAddin` (`ConnectToSW`, `DisconnectFromSW`)
   - `ISldWorks.SetAddinCallbackInfo2`
   - `ISldWorks.CreateTaskpaneView2`
   - `ITaskpaneView.AddControl`
2. **Dokumen & Template**:
   - `ISldWorks.NewDocument`
   - `ISldWorks.GetUserPreferenceStringValue(swDefaultTemplatePart)`
   - `IModelDocExtension.SelectByID2`
3. **Sketch & Fitur 3D**:
   - `ISketchManager.InsertSketch`
   - `ISketchManager.CreateCenterRectangle`
   - `ISketchManager.CreateCircle`
   - `IFeatureManager.FeatureExtrusion3`
   - `IFeatureManager.FeatureCut4`
   - `IFeatureManager.FeatureFillet`
   - `IFeatureManager.InsertFeatureChamfer`
4. **Verifikasi & Ekspor**:
   - `IModelDoc2.ForceRebuild3`
   - `IPartDoc.GetBodies2` (menghitung solid bodies)
   - `IPartDoc.GetPartBox` (mengukur dimensi bounding box)
   - `IModelDocExtension.SaveAs` (menyimpan `.SLDPRT`, mengekspor `.STEP` dan `.STL`)

---

## 3. Prosedur Validasi Manual Per Versi

Bagi lingkungan dengan SOLIDWORKS 2023 atau 2024:
1. Jalankan `scripts\build.ps1` untuk menghasilkan `AICADAssistant.AddIn.dll`.
2. Jalankan `scripts\register_addin.ps1` dengan hak Administrator.
3. Buka SOLIDWORKS -> Buka menu **Tools** -> **Add-Ins...**.
4. Verifikasi bahwa **AI CAD Assistant** muncul dalam daftar Add-Ins dan dapat dicentang tanpa pesan error COM.
5. Periksa Task Pane sebelah kanan: pastikan tab dengan logo merah AI CAD Assistant dapat dibuka dan menampilkan antarmuka WPF.
6. Buat part sederhana dengan mengetikkan: *"Buat balok 100x50x20 mm"*.
7. Tekan tombol **Kirim**, periksa rencana CAD yang dihasilkan, lalu tekan **Buat Model**.
8. Pastikan model balok terbentuk pada layar grafis SOLIDWORKS dan status rebuild berhasil (tanpa error di Feature Tree).
