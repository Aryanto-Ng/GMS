# Panduan Backend (Gms.Api)

Panduan singkat untuk menjalankan, menguji, dan menambah modul. Arsitektur lengkap: `docs/Architecture/ADR.md`.

## Menjalankan Lokal

Prasyarat: .NET SDK 10 (dikunci `global.json`), Docker (untuk tes dan database lokal).

```bash
# 1) PostgreSQL lokal (contoh; sesuaikan kata sandi, JANGAN di-commit)
docker run -d --name gms-pg -e POSTGRES_PASSWORD=<acak> -e POSTGRES_DB=gms -p 5432:5432 postgres:17

# 2) Rahasia lewat user-secrets (tidak disimpan di repository)
cd src/Backend/Gms.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=gms;Username=postgres;Password=<acak>"
dotnet user-secrets set "Seed:AdminPassword" "<password awal admin>"

# 3) Jalankan (Development: migrasi + seeder otomatis, lihat appsettings.Development.json)
dotnet run
```
Login pertama `admin` memakai `Seed:AdminPassword`, lalu **wajib ganti password** (AC-AM-08).

Produksi/UAT: set `Database__MigrateOnStartup=false` dan jalankan migrasi secara terkendali (`dotnet tool run dotnet-ef database update`); seeder dapat dinyalakan sekali lewat `Database__SeedOnStartup=true`.

## Menjalankan Tes

```bash
dotnet test --project src/Tests/Gms.Api.Tests/Gms.Api.Tests.csproj
```
Tes membangun host yang sama dengan produksi (`ApiHost.Build`) pada port acak di atas **PostgreSQL 17 nyata** via Testcontainers (Docker harus aktif). `global.json` mengaktifkan runner Microsoft.Testing.Platform (wajib di SDK .NET 10 untuk xUnit v3).

## Struktur dan Konvensi

```
Gms.Api/
  Hosting/ApiHost.cs        perakitan host (DI, pipeline, daftar endpoint) + DatabaseInitializer + IDataSeeder
  Common/                   ProblemException/ErrorBag, ProblemWriter (RFC 7807), Paging, AppTimeZone
  Security/                 autentikasi sesi opaque, ICurrentUser, RequirePermission, opsi
  Audit/                    IAuditWriter + AuditEvents
  Features/<Modul>/<UseCase>/   Vertical Slice: handler + endpoint (+ validator di dalam handler)
```

**Menambah use case:**
1. Buat kelas publik `XxxHandler` di `Features/<Modul>/<Fitur>/`. Nama berakhiran `Handler` ⇒ terdaftar otomatis (scoped).
2. Gunakan DTO dari `Gms.Shared` (dilarang membuat DTO sendiri). Validasi manual dengan `ErrorBag` → `ThrowIfAny()` (422).
3. Galat bisnis dengan `ProblemException.*` (`NotFound`, `Duplicate`, `StateConflict`, `Rule`, `Forbidden`, dst.). Jangan membocorkan detail internal.
4. Endpoint: `Features/<Modul>/<Fitur>/XxxEndpoints.cs` dengan `Map(IEndpointRouteBuilder)`, lalu daftarkan di `ApiHost.ConfigurePipeline`. Lindungi dengan `.RequirePermission(Permissions.X.Y)` (konstanta dari Shared).
5. Perubahan data + `IAuditWriter.Add(...)` dalam **satu** `SaveChangesAsync` (atau satu transaksi bila memakai UserManager/ExecuteUpdate).
6. Pembaruan agregat berversi: `db.Entry(e).Property(x => x.RowVersion).OriginalValue = request.RowVersion;` → konflik otomatis menjadi 409 `concurrency-conflict`.
7. Tabel baru WAJIB punya seeder: implementasikan `IDataSeeder` (idempoten) dan daftarkan di `ApiHost.Build`.
8. Tes: `[Collection(ApiCollection.Name)]`, gunakan `ApiFixture.Main` (atau `CreateIsolatedAsync()` untuk database kosong) dan helper `TestHost` (`CreateRoleAsync`, `CreateReadyUserAsync`, dst.).

**Aturan yang sudah ditegakkan platform:**
- Autentikasi: `Authorization: Bearer <token sesi>`; permission dibaca dari database pada SETIAP permintaan.
- 401 membawa tipe spesifik (`session-expired`, `session-replaced`, `account-disabled`); 403 `must-change-password` untuk akun yang wajib ganti password (kecuali `/api/v1/auth/*`).
- Enum JSON sebagai string, camelCase, `null` tidak ditulis.
- Aturan konkurensi dan urutan kunci: ADR-004.

## Pola Master Data (`Features/Master`)

Entitas master memakai `MasterCrudHandler<TEntity,TList,TDetail,TSave>` (abstract) + `MasterContext`. Untuk menambah master baru cukup membuat kelas turunan yang mengisi: `Label`, `AutoCodePrefix` (kode otomatis) atau `MaxCodeLength`, `ListSelector`/`DetailSelector`, `ValidateAsync`, `Apply`, `Snapshot`, dan (opsional) `IsCodeUsedAsync` / `CheckCanDeactivateAsync`, lalu daftarkan satu baris di `MasterEndpoints.Map`. Mesin menyediakan: kode unik tanpa peka huruf besar-kecil (`citext`), kunci kode setelah dipakai, tanpa hapus (nonaktif/aktif), `rowVersion` → 409, audit, daftar/pencarian/sort/lookup.

Penomoran otomatis: `NumberSequences.NextCodeAsync("CUS")` / `NextMonthlyCodeAsync("PO")` — WAJIB di dalam transaksi bisnis (`BeginTransactionAsync`) agar rollback ikut membatalkan nomor.

Seeder demo: `Seed:DemoData=true` (Development) mengisi master + 3 FG contoh (`KMJ-001`, `CLN-001` siap produksi; `COV-001` belum lengkap).

## Pola Customer PO dan Alokasi Stok (`Features/Sales`)

Modul ini pertama kali memakai invarian konsistensi ADR-004. Pola yang WAJIB diikuti modul berikutnya (Work Order, Pengiriman, Stok FG):

1. **Kunci dulu, baru baca.** Dalam transaksi: `RowLocks.LockPoLinesAsync` (urut `id`) → `RowLocks.LockStockAsync` (urut fg, color, size) → muat entitas → cek ulang batas → tulis. Memuat sebelum mengunci menghasilkan keputusan dari data basi.
2. **Counter proyeksi + ledger append-only dalam satu transaksi** (`po_lines.allocated_qty`, `stock_balances.allocated` + `po_allocations`). CHECK database (`delivered+allocated+in_production <= qty`, `allocated <= on_hand`) adalah jaring pengaman terakhir, bukan mekanisme utama: pelanggaran seharusnya sudah ditolak sebagai 409 `allocation.exceeded` oleh handler.
3. **Status PO yang digerakkan sistem** (Confirmed→InProgress, ↔Completed) lewat `PoLifecycle` (`ExecuteUpdateAsync` atomik), jangan menulis `Status` dari handler modul lain.
4. **`PoQueries`** (bukan "Handler", didaftarkan manual) adalah satu-satunya tempat hitung metrik baris (`RemainingPlanQty`, `MinAllowedQty`, `IsLocked`, `IsLate`, `AllowedActions`). Klien tidak menghitung ulang.
5. Revisi PO: kolom terkunci menurut status; `qty` tidak boleh di bawah `delivered+allocated+in_production` (`po.qty-below-committed`); baris yang pernah dipakai (alokasi/WO/pengiriman, termasuk riwayat) tidak dapat dihapus (`po.line-in-use`).
6. Tes konkurensi memakai `TaskCompletionSource` sebagai gerbang agar semua permintaan dilepas bersamaan, lalu menegaskan: tidak ada 5xx, jumlah sukses tepat, dan `AssertStockInvariantAsync` (stok = Σ baris = Σ ledger).

Seeder demo PO (`PoSeeder`, Order 30): 3 PO + stok awal, hanya dengan `Seed:DemoData=true`.

## Pola Work Order (`Features/WorkOrders`)

- **Urutan kunci aksi WO:** `RowLocks.LockWorkOrderAsync` → `LockPoLinesAsync` (id urut) → baru memuat WO (`WoFlow.LoadLockedAsync`), lalu pasang `rowVersion` klien. Output/Serah Terima WAJIB mengunci baris WO sebelum memeriksa status (supaya Complete vs input output serial; FSD E2).
- **Dalam Produksi dihitung ulang, bukan delta:** setelah menyimpan perubahan WO/baris, panggil `PoLifecycle.RecomputeInProductionAsync(db, poLineIds, ct)` dalam transaksi yang sama (Σ max(qty − diserahterimakan, 0) untuk WO Draft/Released/InProgress/Completed). Modul Serah Terima yang mengubah `handed_over_qty` juga WAJIB memanggilnya.
- **Batas Sisa Rencana saat WO sendiri sudah terhitung:** limit = `RemainingPlanQty + kontribusi WO ini yang sudah ada` (`WoValidation.EnsureWithinRemainingPlanAsync` dengan kamus `own`).
- **Pembatalan PO memeriksa ulang penahan SETELAH mengunci baris** (WO yang dibuat bersamaan ikut mengunci baris PO yang sama).
- **Snapshot** (routing, tarif Internal × size yang dipakai WO, label) dibuat dalam satu transaksi dengan perubahan status Released dan tidak pernah diubah. Tarif tidak pernah masuk DTO selain `WoTariffSnapshotDto`.
- `WoQueries` (manual DI) menyatukan detail, progres, daftar, opsi pembuatan, dan pratinjau; `AllowedActions` = status × permission.

## Pola Production Output (`Features/Production`)

Semua perubahan ledger output/vendor melewati `StepLedger` (jangan menulis `wo_step_balances` dari tempat lain):

1. **Urutan:** `BeginAsync` (kunci periode gaji `FOR SHARE` → kunci WO `FOR UPDATE` → muat WO/routing/baris) → ubah ledger → `SaveChanges` → `RecomputeAsync` (saldo dihitung ulang dari entri AKTIF, bukan delta) → `ValidateAsync` (rantai `Output(n−1) ≥ Terkirim(n) ≥ Diterima(n) ≥ Kum(n+1)` + diterima ≤ terkirim per vendor) → `ThrowIfAny` → commit. Pelanggaran melempar sebelum commit sehingga transaksi batal; aturan hanya dicek pada KONDISI AKHIR (itulah sebabnya koreksi = void + entri baru tidak terhalang aturan sementara).
2. **Pratinjau dampak = jalur yang sama + rollback** (`dryRun`): jangan menulis logika pratinjau terpisah. `touched` (langkah yang diubah) menentukan kode galat: langkah yang diubah → `*-exceeded`, langkah hilir → `output.below-downstream`.
3. **Ledger tidak pernah diubah nilainya:** hanya `Status/VoidReason/Voided*`. Koreksi menyimpan `ReplacesEntryId`/`ReplacesId` dan alasan; alasan void koreksi diawali "Dikoreksi:".
4. **Status WO:** `RequireOpenForEntries` (Released/In Progress). Released → In Progress dilakukan `MarkInProgressAsync` (riwayat + `PoLifecycle.MarkInProgressAsync`) pada output/terima pertama.
5. **Periode gaji:** `EnsureWorkDateAllowedAsync` (≤ hari ini, bukan periode tertutup, tidak sebelum periode pertama). Modul Payroll yang menutup periode WAJIB mengunci `payroll_periods` `FOR UPDATE` (urutan kunci ke-1) agar serial dengan input output (yang memakai `FOR SHARE`).
6. Riwayat gabungan (`EntryQueries`) memakai `Concat` dua proyeksi ke kelas `Row` yang sama; pembatasan lihat per permission dilakukan sebelum union.
7. Tes konkurensi memakai `AssertLedgerInvariantAsync` (saldo proyeksi = Σ ledger aktif dan rantai terpenuhi).

## Pola Serah Terima & Stok FG (`Features/Inventory`)

- **`HandoverEngine`** adalah satu-satunya tempat yang mengubah `stock_balances` terkait serah terima. Urutan kunci: WO → `po_lines` (id urut) → `stock_balances` (fg, color, size) lewat `RowLocks.EnsureAndLockStockAsync` (membuat baris bila belum ada, baru mengunci: mengunci baris yang belum ada tidak menyerialkan dua pembuat).
- **Semua perubahan dilakukan di memori pada entitas terkunci lalu disimpan sekali** (`Apply`/`Reverse` → `CheckFinalState` → `SaveAsync`). Dengan begitu CHECK database hanya melihat kondisi akhir dan koreksi (= `Reverse` + `Apply`) tidak terhalang keadaan sementara. `SaveAsync` memetakan pelanggaran CHECK (23514) menjadi 409 sebagai jaring pengaman, bukan 500.
- **`in_production_qty` dijaga di memori** (`SetHanded`: selisih `max(qty WO − diserahterimakan, 0)`), bukan lewat `RecomputeInProductionAsync`, karena UPDATE terpisah akan menimbulkan pelanggaran CHECK sesaat `delivered + allocated + in_production ≤ qty`.
- **Alokasi otomatis** = `min(qty, max(Qty WO − sudah diserahterimakan, 0))` ke baris PO asal, 0 bila PO bukan Confirmed/In Progress (E4). Void melepas `min(AutoAllocatedQty, allocated saat ini)`.
- **Kartu stok**: urut `id` (identity), saldo berjalan dihitung dari Σ qty id ≤ baris (tidak terpengaruh filter tanggal).
- **Modul Pengiriman** nanti: kurangi `OnHand`, konsumsi alokasi (`Consume`), tambah `delivered_qty`; kunci sama (po_lines → stok) dan WAJIB memakai pola "ubah di memori → cek kondisi akhir → simpan sekali".

## Pola Pengiriman dan Dokumen PDF (`Features/Deliveries`, `Documents`)

- **`DeliveryEngine`** mengikuti pola serah terima: kunci seluruh baris PO (urut id) → `EnsureAndLockStockAsync` → ubah di memori (`Apply` / `Reverse`) → `CheckFinalState` → `SaveAsync` (pelanggaran CHECK dipetakan 409). Kunci stok WAJIB meski kunci baris PO sudah ada: stok dipakai bersama lintas PO (tes "dua PO + penyesuaian" membuktikan lost-update bila kunci dihapus).
- **Status PO** diturunkan lewat `PoLifecycle.MarkInProgressAsync` + `RecomputeCompletionAsync` setelah simpan, dalam transaksi yang sama.
- **Pratinjau koreksi** menangkap pelanggaran dari `Apply` dan mengembalikannya sebagai `canProceed: false` (bukan 409).
- **PDF:** semua dokumen memakai `PdfFonts.EnsureInitialized` (font sistem di Windows, `Regular.ttf`/`Bold.ttf` dari `App:Pdf:FontDirectory` di Linux) dan keluarga logis "Arial". Pembuat dokumen murni (`DeliveryPdfBuilder.Build(model)`) tanpa akses basis data sehingga mudah diuji; handler yang mengumpulkan data. Tes membaca hasil dengan `PdfReader` (jumlah halaman, ukuran A4, metadata); teks tidak diekstrak, jadi penanda status juga ditulis ke `Info.Subject`.
- Pembuatan PDF di dalam transaksi pencatatan (cetak gagal ⇒ `print_count` dan audit ikut batal).

## Pola Payroll (`Features/Payroll`)

- **Satu sumber baris upah:** `PayrollCalculator.LoadAsync` → Open: live dari `output_entries` aktif + `wo_tariff_snapshot`; Closed: dari `payroll_snapshot_lines`. Semua tampilan (rekap, rincian, harian, matriks, ekspor, slip) memakai daftar `PayrollLine` yang sama, jadi angka antar tampilan tidak mungkin berbeda. Desimal di seluruh perhitungan.
- **Penguncian (urutan kunci ke-1):** input output memegang `FOR SHARE` pada periode yang memuat tanggal kerjanya; tutup/ubah periode memegang `FOR UPDATE` (`RowLocks.LockPayrollPeriodAsync`). Pembuatan periode diserialkan dengan kunci penasihat; periode PERTAMA juga `LOCK TABLE output_entries IN SHARE ROW EXCLUSIVE MODE` agar pemeriksaan "tidak lebih akhir dari output tertua" tidak dapat disusul output yang disisipkan bersamaan.
- **Periode Open ganda:** dicegah handler dan, sebagai pertahanan berlapis, indeks unik parsial `ux_payroll_periods_single_open` (pelanggaran dipetakan ke kode galat yang sama). Mutasi yang hanya mematikan cek handler tidak terdeteksi tes karena perilakunya setara.
- **Tarif hilang (E14):** tidak ada upah tebakan. Pratinjau dan tutup menolak dengan daftar baris; periode tetap Open.
- **Ekspor/slip** memakai `PdfFonts` (lihat Pola Pengiriman) dan ClosedXML. Slip memformat angka gaya Indonesia dengan `NumberFormatInfo` sendiri agar tidak bergantung ICU/budaya sistem (Docker globalization-invariant).
- **Tes payroll WAJIB memakai host terisolasi** (`fixture.CreateIsolatedAsync()`): periode bersifat global (aturan satu Open, tanggal sebelum periode pertama ditolak, tanggal di periode tertutup terkunci) dan akan memengaruhi tes modul lain bila memakai database bersama.

## Jebakan yang Sudah Ditemukan (jangan diulang)

1. **Urutkan sebelum proyeksi DTO.** `OrderBy(x => x.Seq)` pada hasil `new SomeDto(...)` tidak dapat diterjemahkan EF (500). Pola yang benar: `Join → proyeksi anonim → OrderBy → Select(new Dto(...))`.
2. **Jangan memakai parameter lambda generik pada endpoint.** `RouteHandlerAnalyzer` ASP.NET mogok (`AD0001`) pada `[FromServices] THandler`. Ambil dari `http.RequestServices.GetRequiredService<THandler>()` (lihat `MasterEndpoints.MapCrud`).
3. **Ubah urutan routing tanpa menyentuh tarif:** cukup `UPDATE seq` dalam satu `SaveChanges` (unik `(fg_id, seq)` DEFERRABLE). Jangan hapus-lalu-sisipkan: FK komposit meng-CASCADE tarif.
4. **Mengganti seluruh routing/tarif (salin dari FG):** hapus dengan `ExecuteDeleteAsync` di luar change tracker lebih dulu, baru `Add`, agar tidak bentrok dengan kunci unik `(fg_id, process_id)`.

5. **Daftar khusus harus tetap memakai `ApplySort` kelas dasar.** Daftar FG pernah mengabaikan `sort` (selalu urut kode) karena override `ListAsync` menulis `OrderBy` sendiri; tertangkap saat tes lain menambah data. Gunakan `ApplySort(set, query.Sort)`.

6. **Jangan memfilter/mengurutkan di atas DTO berkonstruktor.** Query stok pertama kali 500 karena `Where(r => r.ColorSize.ColorId == ...)` pada `StockRow`. Proyeksikan ke kelas skalar privat, filter/urut/halaman di server, baru ubah ke DTO di memori (lihat `StockQueries.S`).
7. **`audit_logs.target_id` maksimal 100 karakter.** Jangan menggabungkan beberapa GUID; pakai id agregat dan taruh sisanya di nilai audit.

## Catatan Teknis

- `AccessSeeder` (Order 10) menjamin Administrator selalu memiliki SELURUH permission dari `PermissionCatalog` setiap seeder dijalankan, sehingga permission baru dari modul berikutnya otomatis tersedia bagi Administrator.
- Zona waktu aplikasi: `App:TimeZone` (default `Asia/Jakarta`). Bila image tidak punya tzdata, jatuh ke UTC+7 tetap.
- `POST /users/{id}/deactivate|activate` menerima `rowVersion` sebagai **query string** (`?rowVersion=123`).
