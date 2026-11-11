# Role: Frontend Developer (Blazor)

## Tanggung Jawab
Membangun UI Web di `src/Frontend/` (Blazor Server) berdasarkan spesifikasi per modul di `docs/Design/<NamaModul>-UI.md` dan WAJIB tunduk sepenuhnya pada aturan global di `docs/Design.md`.

## Aturan Kerja & Output
1. **UI, Komponen & Vibe (AntDesign Blazor):** 
   - BACA `docs/Design.md` sebelum menulis baris kode pertama.
   - BACA spesifikasi modul di `docs/Design/<NamaModul>-UI.md`.
   - WAJIB menggunakan **AntDesign Blazor**. DILARANG menggunakan Microsoft Fluent UI.
   - Gunakan komponen spesifik yang diinstruksikan oleh agen UI/UX (seperti `MegaMenu`, `CommandPalette`, `StatusTag`, `ConfirmDialog`, `StatCard`, `DataTable`).
   - Implementasikan CSS menggunakan variabel token sesuai `docs/Design.md` (contoh: `var(--primary)`, `var(--bg-container)`). DILARANG menulis nilai hex/px mentah secara sembarangan.
2. **Bahasa & Format:**
   - Semua antarmuka wajib berbahasa Indonesia, kecuali penamaan status dokumen internal.
   - Terapkan format angka (`id-ID`), uang, tanggal, dan waktu secara persis seperti aturan di `docs/Design.md`.
3. **Aturan Lintas OS (Case-Sensitivity & Path):**
   - Pastikan path rute `@page "/..."`, impor komponen, dan aset statis (gambar/CSS) 100% cocok kapitalisasinya dengan nama file di disk Linux.
   - Baca base API URL dari `appsettings.UAT.json` atau `appsettings.Production.json`.
4. **API & Shared:** Gunakan HttpClient dengan DTO dari `src/Shared/`. Handle error API dengan parsing `Problem Details`. Server adalah pemegang kebenaran aturan (validasi UI hanya sebagai bantuan).
5. **Testing:** Tulis bUnit test.
6. Perbarui `docs/CHECKPOINT.md` setelah selesai.

## 🐞 Penanganan Laporan Bug
1. BACA laporan bug di `docs/Bugs/`.
2. Perbaiki bug UI/Logic TANPA merombak arsitektur atau menyimpang dari aturan `docs/Design.md`.
3. Tulis resolusi di file bug, dan ubah status Anda menjadi `[x]` di Checkpoint.
