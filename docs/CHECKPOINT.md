# 📍 Project Checkpoint & State

**ATURAN WAJIB UNTUK AI:** 
1. Ubah `[ ]` jadi `[x]` setelah tugas Anda selesai.
2. Lakukan `git add .` dan `git commit -m "chore: status update"` setelah mencentang kotak Anda.
3. Jika modul sudah selesai 100% (QA Pass/DevOps), pindahkan blok modul ini ke `docs/ARCHIVE_CHECKPOINT.md` dan kosongkan bagian "Modul Aktif" di sini untuk modul berikutnya.

## Modul Aktif: Access Management
- **Tipe:** Baru
- **Referensi:** `docs/FSD/AccessManagement.md` | `docs/Design/...`

## Workflow Status
- [x] **1. System Analyst:** FSD disetujui (Render Mode ditetapkan).
- [ ] **2. UI/UX Designer:** Spesifikasi Vibe, Layout, & State UI (Loading/Error) selesai ditulis.
- [ ] **3. Architect:** `src/Shared/` (DTOs), Skema DB, dan Mock API Contract selesai.
- [ ] **4. Backend Developer:** Minimal APIs, Handlers, Seeder, & xUnit selesai.
- [ ] **5. Frontend Developer:** Blazor WebApp (Fluent UI) & bUnit selesai.
- [x] **6. Mobile Developer:** N/A — fase 1 input output hanya via PC (admin produksi), tidak butuh MAUI.
- [ ] **7. QA Engineer:** E2E (Playwright) & Integration Test pass.
- [ ] **8. DevOps:** Dockerfile/Compose, CI/CD, Backup Script & Deployment selesai.

> **Log / Serah Terima:**
> - [SA → UI/UX/Architect] FSD Access Management v1.0 disetujui. Render Mode: Login/Logout/Ganti Password/403 = Static SSR; halaman User, Role+Matriks, Audit Log = Interactive Server. Mobile N/A (fase 1).
> - [SA → Architect] User **sudah memberi izin** memakai ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`). Daftarkan versinya di `src/Directory.Packages.props` saat solution dibuat (belum ada solution). Satu sesi aktif per user (AC-AM-06a) dan penegakan permission di server wajib dirancang sejak awal.
> - [SA] Status FSD fase 1: AccessManagement v1.0, CustomerPO v1.0, MasterData v1.0, WorkOrder v1.0 (semua disetujui). Serah terima material dicatat oleh Admin Produksi (Konveksi) yang diberi hak akses.
> - [SA] (arsip) Status sebelumnya: AccessManagement v1.0, CustomerPO v1.0, MasterData v0.1 draft. Ditambahkan ke fase 1 atas permintaan user: Customer PO, Serah Terima & Stok FG (termasuk serah terima material sederhana tanpa stok), Pengiriman ke Customer. BOM ditiadakan. Urutan FSD: Work Order → Production Output → Serah Terima & Stok FG → Pengiriman → Payroll.
> - [SA] Keputusan lintas modul: Comfi = master data/PO/WO/tarif; Konveksi = output & payroll & master pekerja. Granularitas output = WO + Proses + Pekerja + Tanggal kerja + Size + Color (Level C). Tarif per FG × Proses × Size, di-snapshot ke WO saat Released. Koreksi setelah tutup periode = entri penyesuaian di periode berikutnya. FSD berikutnya: Master Data → Work Order → Production Output (+ vendor job) → Payroll Borongan.
