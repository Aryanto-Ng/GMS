# 📍 Project Checkpoint & State

**ATURAN WAJIB UNTUK AI:** 
1. Ubah `[ ]` jadi `[x]` setelah tugas Anda selesai.
2. Lakukan `git add .` dan `git commit -m "chore: status update"` setelah mencentang kotak Anda.
3. Jika modul sudah selesai 100% (QA Pass/DevOps), pindahkan blok modul ini ke `docs/ARCHIVE_CHECKPOINT.md` dan kosongkan bagian "Modul Aktif" di sini untuk modul berikutnya.

## Modul Aktif: Fase 1 — Seluruh Modul (Alur B: desain lintas modul dulu)
- **Tipe:** Baru
- **Alur B:** Peran 2 (UI/UX) dan 3 (Architect) dikerjakan untuk **seluruh 8 modul** terlebih dahulu, karena skema DB, alokasi, stok, dan snapshot saling terkait. Setelah itu peran 4 s.d. 8 dikerjakan **per modul** sesuai urutan dependensi di bawah (dan modul yang selesai 100% diarsipkan).
- **Referensi FSD (semua v1.0 disetujui; ditambah `docs/FSD/Beranda.md` v1.0 sebagai modul ke-9):**
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
AccessManagement → MasterData → CustomerPO → WorkOrder → ProductionOutput → SerahTerimaStokFG → Pengiriman → PayrollBorongan → Beranda (terakhir, karena membaca data semua modul)

## Workflow Status
- [x] **1. System Analyst:** FSD disetujui (Render Mode ditetapkan) untuk 8 modul.
- [x] **2. UI/UX Designer:** Spesifikasi Vibe, Layout, & State UI (Loading/Error) selesai ditulis (9 modul + fondasi: `docs/Design/`).
- [x] **3. Architect:** `src/Shared/` (DTOs), Skema DB, dan Mock API Contract selesai (9 modul). Implementasi mock = tugas Frontend (ADR-014).
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
> - [UI/UX → Architect/Frontend] **Izin user diberikan (2026-11-08):** `Microsoft.FluentUI.AspNetCore.Components` (MIT). Daftarkan versi di `src/Directory.Packages.props`, verifikasi kompatibilitas .NET. Total izin library saat ini: ASP.NET Core Identity, PDFsharp-MigraDoc, ClosedXML, Microsoft.FluentUI.AspNetCore.Components.
> - [UI/UX → Frontend/Architect] 9 dokumen desain + fondasi selesai di `docs/Design/`. Komponen bersama yang harus dibangun: Grid Matriks Ukuran (kolom bertingkat, baris "batas", tempel blok), Matriks Progres, Dialog Alasan + Pratinjau Dampak, Kartu Metrik, Chip Status, Daftar Master Generik, Wizard Import CSV, komponen pembuat PDF bersama. Aturan kumulatif/stok/pratinjau dampak adalah **hasil hitung server** (jangan diimplementasikan ulang di klien).
> - [UI/UX → Semua FSD] Parameter query filter bersama (Beranda dan chip cepat): `terlambat`, `jatuhTempo7`, `poCustomerKosong`, `sisaRencana`, `hariIni`. Modul terkait wajib menyediakannya.
> - [SA] FSD ke-9 **Beranda** (`docs/FSD/Beranda.md`) v1.0 disetujui.
> - [Architect → Backend/Frontend] **Tahap 3 SELESAI — Backend dan Frontend dapat bekerja PARALEL.** Hasil: kerangka solution `src/Gms.slnx` (build 0 warning/0 error); `docs/Architecture/{ADR,Database,ApiConventions}.md`; **kontrak** di `src/Shared/Gms.Shared` (DTO, enum, `Permissions` + `PermissionCatalog`, `ProblemTypes`/`ProblemCodes`/`GmsProblemDetails`/`ApiException`, dan antarmuka klien bertipe: `IAuthApi, IUsersApi, IRolesApi, IAuditApi, IUomApi, IMaterialCategoryApi, IMaterialApi, ISizeApi, IColorApi, ICustomerApi, IProcessApi, IVendorApi, IWorkerApi, IFgApi, IImportApi, IPurchaseOrdersApi, IWorkOrdersApi, IProductionApi, IInventoryApi, IDeliveriesApi, IPayrollApi, IDashboardApi`); entitas di `Gms.Domain`; konfigurasi Fluent API + `GmsDbContext` + migrasi `Initial` dan `AdvancedConstraints` di `Gms.Infrastructure` (47 tabel).
> - [Architect → Backend] **Migrasi SUDAH diverifikasi di PostgreSQL 17 nyata (2026-11-08):** 64/64 uji constraint SQL, 12/12 uji runtime EF Core, rollback penuh + naik ulang bersih, tanpa drift model. Laporan: `docs/Architecture/MigrationVerification.md`; skrip: `docs/Architecture/verification/`. **Tugas pertama Backend:** (1) `dotnet tool restore`; (2) porting skrip verifikasi menjadi integration test xUnit + Testcontainers; (3) tes yang memastikan setiap konstanta `Permissions.*` ada di `PermissionCatalog.All`; (4) Seeder idempoten (permission, role Administrator, admin awal, Customer "STOK INTERNAL", data demo); (5) tes penguncian paralel untuk Output/Stok/Pengiriman begitu use case-nya ada (satu sukses + satu 409, tanpa deadlock). Kontrak endpoint: `ApiConventions.md` (termasuk bagian 4.11). Ikuti urutan kunci baku di ADR-004.
> - [Architect → Frontend] Tulis `HttpXxxApi` dan `MockXxxApi` untuk setiap antarmuka di `Gms.Shared` (ADR-014); mock wajib melempar `ApiException` dengan `GmsProblemDetails` seperti API asli. Konfigurasi `Api:UseMock`. Login/otorisasi: token sesi opaque di cookie server (ADR-002). Aturan kumulatif/stok/pratinjau dampak = hasil server, jangan dihitung ulang di klien.
> - [Architect] **Izin tooling dari user (2026-11-08)** dan sudah terdaftar di CPM: `Microsoft.EntityFrameworkCore.Design` + tool `dotnet-ef` (tool lokal, `dotnet-tools.json`), `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `bunit`, `Microsoft.Playwright`, `Testcontainers.PostgreSql`. **Masih wajib minta izin:** `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Playwright.Xunit`, dan package lain.
> - [Architect] Keputusan arsitektur: dua host (Gms.Api + Gms.Web, Web hanya mereferensi Shared), sesi opaque + Identity (satu sesi aktif, timeout 30 menit), PostgreSQL + EF Core Fluent API snake_case, ledger append-only + proyeksi dalam satu transaksi + CHECK DB + urutan kunci baku, snapshot WO, penomoran lewat tabel `number_sequences`. Detail: `docs/Architecture/ADR.md`.
> - [Architect] **Izin library:** user menetapkan PostgreSQL + EF Core standar (2026-11-08); provider `Npgsql.EntityFrameworkCore.PostgreSQL` diperlakukan termasuk dalam izin tersebut. Versi terpasang di CPM: EF Core 10.0.14, Npgsql EF 10.0.3, Identity EF 10.0.14, Fluent UI 4.14.5, PDFsharp-MigraDoc 6.2.4, ClosedXML 0.105.1 (+ tooling pengujian di atas).
> - [Architect → DevOps] Repository tidak punya `.gitignore` sebelumnya; sudah dibuat. PostgreSQL membutuhkan ekstensi `citext` dan `btree_gist` (keduanya trusted extension; dibuat oleh migrasi awal, user DB perlu hak CREATE pada database). Font berlisensi terbuka harus disertakan untuk PDF di Linux/Docker. Backup: `pg_dump` terjadwal.
> - [SA] Item belum final: Pengiriman Q6-Q8 (tata letak surat jalan), Customer PO Q6-Q7 (default berlaku).
