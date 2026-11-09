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

## Jebakan yang Sudah Ditemukan (jangan diulang)

1. **Urutkan sebelum proyeksi DTO.** `OrderBy(x => x.Seq)` pada hasil `new SomeDto(...)` tidak dapat diterjemahkan EF (500). Pola yang benar: `Join → proyeksi anonim → OrderBy → Select(new Dto(...))`.
2. **Jangan memakai parameter lambda generik pada endpoint.** `RouteHandlerAnalyzer` ASP.NET mogok (`AD0001`) pada `[FromServices] THandler`. Ambil dari `http.RequestServices.GetRequiredService<THandler>()` (lihat `MasterEndpoints.MapCrud`).
3. **Ubah urutan routing tanpa menyentuh tarif:** cukup `UPDATE seq` dalam satu `SaveChanges` (unik `(fg_id, seq)` DEFERRABLE). Jangan hapus-lalu-sisipkan: FK komposit meng-CASCADE tarif.
4. **Mengganti seluruh routing/tarif (salin dari FG):** hapus dengan `ExecuteDeleteAsync` di luar change tracker lebih dulu, baru `Add`, agar tidak bentrok dengan kunci unik `(fg_id, process_id)`.

5. **Daftar khusus harus tetap memakai `ApplySort` kelas dasar.** Daftar FG pernah mengabaikan `sort` (selalu urut kode) karena override `ListAsync` menulis `OrderBy` sendiri; tertangkap saat tes lain menambah data. Gunakan `ApplySort(set, query.Sort)`.

## Catatan Teknis

- `AccessSeeder` (Order 10) menjamin Administrator selalu memiliki SELURUH permission dari `PermissionCatalog` setiap seeder dijalankan, sehingga permission baru dari modul berikutnya otomatis tersedia bagi Administrator.
- Zona waktu aplikasi: `App:TimeZone` (default `Asia/Jakarta`). Bila image tidak punya tzdata, jatuh ke UTC+7 tetap.
- `POST /users/{id}/deactivate|activate` menerima `rowVersion` sebagai **query string** (`?rowVersion=123`).
