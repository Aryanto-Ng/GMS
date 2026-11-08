# 📍 Project Checkpoint & State

**ATURAN WAJIB UNTUK AI:** 
1. Ubah `[ ]` jadi `[x]` setelah tugas Anda selesai.
2. Lakukan `git add .` dan `git commit -m "chore: status update"` setelah mencentang kotak Anda.
3. Jika modul sudah selesai 100% (QA Pass/DevOps), pindahkan blok modul ini ke `docs/ARCHIVE_CHECKPOINT.md` dan kosongkan bagian "Modul Aktif" di sini untuk modul berikutnya.

## Modul Aktif: Fase 1 — Seluruh Modul (Alur B: desain lintas modul dulu)
- **Tipe:** Baru
- **Alur B:** Peran 2 (UI/UX) dan 3 (Architect) dikerjakan untuk **seluruh 8 modul** terlebih dahulu, karena skema DB, alokasi, stok, dan snapshot saling terkait. Setelah itu peran 4 s.d. 8 dikerjakan **per modul** sesuai urutan dependensi di bawah (dan modul yang selesai 100% diarsipkan).
- **Referensi FSD (semua v1.0 disetujui):**
  1. `docs/FSD/AccessManagement.md`
  2. `docs/FSD/MasterData.md`
  3. `docs/FSD/CustomerPO.md`
  4. `docs/FSD/WorkOrder.md`
  5. `docs/FSD/ProductionOutput.md`
  6. `docs/FSD/SerahTerimaStokFG.md`
  7. `docs/FSD/Pengiriman.md` (v1.1: cetak Surat Jalan PDF; Q6-Q8 menunggu konfirmasi)
  8. `docs/FSD/PayrollBorongan.md`
- **Dokumen desain:** `docs/Design/<NamaModul>-UI.md`

### Urutan Implementasi (peran 4 s.d. 8, per modul)
AccessManagement → MasterData → CustomerPO → WorkOrder → ProductionOutput → SerahTerimaStokFG → Pengiriman → PayrollBorongan

## Workflow Status
- [x] **1. System Analyst:** FSD disetujui (Render Mode ditetapkan) untuk 8 modul.
- [ ] **2. UI/UX Designer:** Spesifikasi Vibe, Layout, & State UI (Loading/Error) selesai ditulis (8 modul).
- [ ] **3. Architect:** `src/Shared/` (DTOs), Skema DB, dan Mock API Contract selesai (8 modul).
- [ ] **4. Backend Developer:** Minimal APIs, Handlers, Seeder, & xUnit selesai (per modul).
- [ ] **5. Frontend Developer:** Blazor WebApp (Fluent UI) & bUnit selesai (per modul).
- [x] **6. Mobile Developer:** N/A — fase 1 input output hanya via PC (admin produksi), tidak butuh MAUI.
- [ ] **7. QA Engineer:** E2E (Playwright) & Integration Test pass (per modul).
- [ ] **8. DevOps:** Dockerfile/Compose, CI/CD, Backup Script & Deployment selesai.

> **Log / Serah Terima:**
> - [SA → UI/UX/Architect] Cakupan fase 1 = 8 modul. Render Mode: **Interactive Server** untuk hampir semua halaman; **Static SSR** hanya untuk Login/Logout/Ganti Password/403 (AccessManagement). WebAssembly tidak dipakai. Mobile N/A.
> - [SA] Keputusan lintas modul: Comfi = master data, PO, WO, tarif, stok FG, pengiriman; Konveksi = output, serah terima, payroll, master pekerja. Granularitas output = WO + Proses + Pekerja + Tanggal kerja + Color + Size (Level C). Tarif per FG × Proses × Size (presisi 5 desimal, Rupiah), di-snapshot ke WO saat Released. BOM ditiadakan. Koreksi setelah periode gaji tertutup = entri Penyesuaian di periode berikutnya (tidak ada reopen periode). Ledger output/stok bersifat append-only; koreksi = void + entri baru atomik.
> - [SA → Architect] **Izin library dari user (2026-11-08):** ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`), **PDFsharp-MigraDoc** (MIT; slip gaji + surat jalan), **ClosedXML** (MIT; ekspor Excel). Daftarkan versi (verifikasi stabil dan kompatibel .NET 10) di `src/Directory.Packages.props`. **Tolak** QuestPDF (lisensi Community berbasis pendapatan) dan EPPlus (Polyform Noncommercial). Library lain tetap wajib minta izin. Gunakan satu komponen pembuat PDF bersama. Siapkan font berlisensi terbuka untuk PDF di Linux/Docker (DevOps).
> - [SA → Architect] Titik rawan konsistensi yang harus dirancang atomik: (1) invariant kumulatif output antar proses per WO (kunci per WO), (2) stok FG vs alokasi PO vs pengiriman (kunci per FG × Color × Size), (3) penutupan periode gaji vs input output (serialisasi), (4) satu sesi aktif per user dan penegakan permission di server.
> - [SA → Architect] Konfigurasi aplikasi (bukan kode): nama dan alamat Konveksi (kop slip), nama dan alamat Comfi (kop surat jalan), password Administrator awal (secret).
> - [SA → Backend] Seeder: role Administrator + katalog permission (tiap FSD mendaftarkan permission-nya), akun Administrator awal, Customer "STOK INTERNAL".
> - [UI/UX] Fondasi desain selesai: `docs/Design/_Foundation-UI.md` (vibe campuran Compact/Comfortable, terang+gelap, nav per fungsi, pola daftar/form/detail/dialog, Grid Matriks Ukuran, chip status, state UI, a11y WCAG 2.1 AA, target 1366+). Dokumen per modul menyusul: `docs/Design/<NamaModul>-UI.md`.
> - [UI/UX → Architect/Frontend] Library komponen yang direncanakan: `Microsoft.FluentUI.AspNetCore.Components` (MIT). **Belum diizinkan user**, wajib minta izin sebelum ditambahkan.
> - [SA] FSD ke-9 **Beranda** (`docs/FSD/Beranda.md`) v0.1 draft atas permintaan user, menunggu persetujuan (Q1-Q5). Desain UI Beranda ditunda sampai disetujui. Modul lain menambah filter yang dibutuhkan beranda (PO: Terlambat, Jatuh Tempo, PO Customer belum ada, Sisa Rencana > 0; Riwayat Output: hari ini; WO: Terlambat).
> - [SA] Item belum final: Pengiriman Q6-Q8 (tata letak surat jalan), Customer PO Q6-Q7 (default berlaku).
