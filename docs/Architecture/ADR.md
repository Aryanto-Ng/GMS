# Architecture Decision Records (ADR) — GMS

| Atribut | Nilai |
|---|---|
| Penulis | Software Architect |
| Tanggal | 2026-11-08 |
| Status | Disetujui user pada level stack (PostgreSQL, EF Core standar); keputusan turunan di bawah adalah rekomendasi Architect dan dapat ditinjau |
| Sumber | `docs/FSD/*.md` (9 modul), `docs/Design/*.md`, `CLAUDE.md` |

Dokumen terkait: `Database.md` (skema), `ApiConventions.md` (kontrak API dan Problem Details).

---

## ADR-001 Stack dan Struktur Solution

**Keputusan:** .NET 10 (SDK 10.0.403, dikunci `global.json`), ASP.NET Core Minimal APIs, Blazor Web App, PostgreSQL, EF Core standar. Mobile (MAUI) **tidak dibuat** (N/A fase 1).

```
src/
  Gms.slnx
  Directory.Build.props        (TFM, nullable, warnings-as-errors)
  Directory.Packages.props     (SATU-SATUNYA tempat versi package)
  Shared/Gms.Shared            kontrak: DTO, enum, katalog permission, tipe Problem Details, rute
  Backend/Gms.Domain           entitas + aturan domain murni
  Backend/Gms.Infrastructure   EF Core (Npgsql), konfigurasi Fluent API, Identity store, migrasi
  Backend/Gms.Api              host API: Minimal APIs, Vertical Slice (Features/<Modul>/<UseCase>/)
  Frontend/Gms.Web             Blazor Web App (Interactive Server + Static SSR)
```

**Aturan dependensi (ditegakkan lewat referensi proyek):**
`Domain → Shared` · `Infrastructure → Domain` · `Api → Infrastructure, Domain, Shared` · `Web → Shared`.
**Web tidak boleh** mereferensikan Domain/Infrastructure/Api. Satu-satunya kontrak antara Web dan Api adalah `Gms.Shared` dan HTTP.

**Vertical Slice di Api:** `Features/<Modul>/<UseCase>/` berisi Request/Response (dari Shared), Handler, Validator, dan Endpoint. Tanpa library mediator, mapper, atau validator pihak ketiga (menghindari package tambahan sesuai aturan proyek): handler adalah kelas biasa yang didaftarkan lewat DI, validasi dan pemetaan ditulis manual.

**Alasan memisahkan host:** instruksi peran Backend/Frontend mengharuskan DTO bersama dan HttpClient + Problem Details sehingga FE dan BE dapat berjalan paralel dengan kontrak yang sama.

---

## ADR-002 Autentikasi: Sesi Opaque di Server + ASP.NET Core Identity

**Konteks:** FSD Access Management mensyaratkan: satu sesi aktif per pengguna (AC-AM-06a), timeout tidak aktif 30 menit (AC-AM-05), pengakhiran sesi paksa oleh Administrator (AC-AM-06b), perubahan permission efektif pada aksi berikutnya (AC-AM-19), dan penonaktifan pengguna berlaku segera (E3). JWT stateless tidak cocok untuk semua ini.

**Keputusan:**
- **Identity** (UserManager, hasher password, lockout) dipakai untuk penyimpanan pengguna. Tabel Role Identity **tidak** dipakai (lihat Database.md).
- **Token sesi opaque** acak (≥ 256 bit) dibuat API saat login. Yang disimpan di DB hanya **hash SHA-256**-nya pada tabel `user_sessions`.
- Aturan **satu sesi aktif** ditegakkan di DB: indeks unik parsial `user_sessions(user_id) WHERE revoked_at IS NULL`. Login baru mencabut sesi lama (alasan `Replaced`) dalam transaksi yang sama.
- API memvalidasi `Authorization: Bearer <token>` di setiap permintaan: sesi aktif, belum melewati 30 menit sejak `last_activity_at`, pengguna aktif. `last_activity_at` diperbarui paling sering sekali per menit. Permission pengguna dimuat dari DB pada permintaan itu (tanpa cache pada fase 1, 5–6 pengguna) sehingga perubahan Role berlaku pada aksi berikutnya.
- **Alur Web (BFF):** halaman Login (Static SSR) mengirim kredensial ke API melalui server (`POST /api/v1/auth/login`). Respons berisi token. Web menyimpan token di **cookie autentikasi HttpOnly + Secure + SameSite=Lax** (dilindungi Data Protection). Circuit Blazor Server membaca token dari principal dan melampirkannya pada setiap panggilan `HttpClient` melalui `DelegatingHandler`. Browser tidak pernah memegang token secara langsung.
- Respons 401 membawa tipe Problem spesifik agar UI menampilkan pesan yang tepat: `session-expired`, `session-replaced`, `account-disabled`, `must-change-password`.
- **CSRF:** API memakai bearer (bukan cookie) sehingga tidak rentan CSRF. Form Static SSR Web memakai antiforgery.
- **Otorisasi:** permission berbentuk string `Resource.Action`. Endpoint dilindungi dengan filter `RequirePermission("WorkOrder.Release")`. Pemeriksaan di server bersifat final.
- Lockout: 5 kali gagal → terkunci 15 menit (konfigurasi). Administrator dapat membuka kunci.

**Konsekuensi:** satu query ringan tambahan per permintaan (sesi + permission). Diterima untuk skala ini.

---

## ADR-003 Data: PostgreSQL + EF Core + Konvensi

| Aspek | Keputusan |
|---|---|
| ORM | EF Core standar, **Fluent API** di kelas `IEntityTypeConfiguration<T>` per modul (`Infrastructure/Persistence/Configurations/`) |
| Penamaan | `snake_case` untuk tabel dan kolom (konvensi ditulis sendiri, tanpa package). Nama tabel jamak eksplisit via `ToTable`. |
| Kunci | `uuid` yang dibuat aplikasi (`Guid.CreateVersion7()`, urut waktu) untuk entitas bisnis. Ledger yang dibaca berurutan memakai `bigint identity`. |
| Waktu | Jejak (created_at dst.) = `timestamptz` UTC. Tanggal bisnis (tanggal kerja, tanggal kirim, tanggal PO, periode gaji) = `date`. |
| Uang/tarif | `numeric(18,5)` (tarif), `numeric(28,5)` (subtotal), `numeric(18,0)` (dibayarkan). Semua `decimal` di C#, tanpa floating point. |
| Qty | `integer` (FG), `numeric(18,3)` (material). |
| Enum | Disimpan sebagai `text` (`HasConversion<string>()`) agar terbaca saat debug/QA, dengan CHECK constraint untuk nilai yang valid. |
| Konkurensi optimistis | `xmin` PostgreSQL sebagai token versi (`row_version`, properti `uint` dengan `IsRowVersion()`) pada agregat yang dapat diedit. Konflik → 409 `concurrency-conflict`. |
| Penghapusan | **Tidak ada hard delete / soft delete untuk data bisnis** (aturan FSD): nonaktif atau void/cancel. FK memakai `ON DELETE RESTRICT`, kecuali anak agregat (baris dokumen, tarif, langkah routing) yang `CASCADE` dari induknya. |
| Case-insensitive unik | Kode dan username memakai indeks unik pada `lower(kolom)` (ekspresi), bukan collation khusus. |

Ekstensi PostgreSQL: `btree_gist` (untuk exclusion constraint). Migrasi dikelola EF Core Migrations (alat `dotnet-ef` dan paket Design akan **diminta izinnya** saat fase Backend).

---

## ADR-004 Konsistensi: Ledger + Proyeksi dalam Satu Transaksi + CHECK di Database

**Masalah:** FSD memiliki empat titik rawan yang tidak boleh lolos pada konkurensi: (1) aturan kumulatif output antar proses, (2) stok vs alokasi vs pengiriman, (3) penutupan periode gaji vs input output, (4) satu sesi aktif.

**Pola umum:**
1. **Ledger append-only** adalah sumber kebenaran (output_entries, vendor_movements, stock_ledger, po_allocations, handovers, deliveries).
2. **Proyeksi** (saldo/counter) diperbarui **dalam transaksi yang sama** dengan penulisan ledger, agar pemeriksaan invariant cepat dan dapat dijaga **CHECK constraint** di DB sebagai sabuk pengaman terakhir:
   - `stock_balances(on_hand, allocated)`: `CHECK (on_hand >= 0 AND allocated >= 0 AND allocated <= on_hand)`.
   - `po_lines(delivered_qty, allocated_qty, in_production_qty)`: `CHECK (delivered + allocated + in_production <= qty)` (artinya Sisa Rencana ≥ 0 dijaga DB).
   - `wo_step_balances(qty, sent_qty)`: `CHECK (qty >= 0 AND sent_qty >= 0)`.
3. **Urutan kunci baku (lock order)** untuk mencegah deadlock. Semua use case yang menyentuh lebih dari satu tipe baris **harus** mengunci dengan urutan ini, dan baris sejenis diurutkan menurut kunci (id) saat dikunci:

   | Urutan | Objek | Mode |
   |---|---|---|
   | 1 | `payroll_periods` yang memuat Tanggal Kerja | `FOR SHARE` (input output), `FOR UPDATE` (tutup periode) |
   | 2 | `work_orders` (per WO) | `FOR UPDATE` |
   | 3 | `po_lines` (urut id) | `FOR UPDATE` |
   | 4 | `stock_balances` (urut fg, color, size) | `FOR UPDATE` |

4. Isolation level **Read Committed** + penguncian eksplisit di atas (bukan Serializable). Operasi dibungkus `IUnitOfWork.ExecuteAsync(...)` yang menjalankan satu transaksi.

**Penerapan per titik rawan:**

| Titik rawan | Mekanisme |
|---|---|
| (1) Kumulatif output | Kunci baris `work_orders` → baca `wo_step_balances` → hitung **kondisi akhir** (setelah void/koreksi/entri baru) untuk rantai `Output(n−1) ≥ Terkirim(n) ≥ Diterima(n) = Output(n) ≥ Kum(n+1)` per sel Color × Size → tulis ledger + perbarui saldo. Pelanggaran → 409 `rule-violation` dengan daftar `violations`. |
| (2) Stok/alokasi | Kunci `po_lines` lalu `stock_balances` (urut). Perubahan `on_hand`/`allocated` + counter PO + entri `stock_ledger`/`po_allocations` dalam satu transaksi. CHECK menolak saldo negatif walau ada bug aplikasi. |
| (3) Tutup periode | Input output mengambil `FOR SHARE` pada periode yang memuat tanggal kerjanya (jika ada). Tutup periode mengambil `FOR UPDATE`, sehingga menunggu input yang sedang berjalan selesai; input sesudahnya melihat periode sudah Closed dan ditolak. Exclusion constraint mencegah periode tumpang tindih dan indeks unik parsial menjamin hanya satu periode Open. |
| (4) Sesi tunggal | Indeks unik parsial di `user_sessions` (lihat ADR-002). |

**Counter turunan:** `in_production_qty` dihitung ulang dalam transaksi (di bawah kunci `po_lines`) dari baris WO aktif: `Σ max(wo_line.qty − handed_over_qty, 0)` untuk WO berstatus Draft/Released/In Progress/Completed. Ini menghindari drift akibat delta bertingkat.

**Verifikasi:** Backend menyediakan `ConsistencyChecker` yang menghitung ulang semua proyeksi dari ledger dan membandingkannya (dipakai pada integration test dan sebagai perintah diagnostik).

---

## ADR-005 Ledger, Void, dan Koreksi

- Entri ledger **tidak pernah di-UPDATE** selain metadata status (`status`, `void_reason`, `voided_at/by`). Qty tidak pernah diubah.
- **Void** = menandai dokumen `Voided` + (untuk stok) menulis entri **pembalik** di `stock_ledger`; proyeksi disesuaikan dalam transaksi yang sama.
- **Koreksi** = void dokumen lama + dokumen baru (`replaces_id`) dalam satu transaksi; validasi invariant hanya pada kondisi akhir.
- **Penyesuaian output** (periode gaji tertutup) adalah entri dengan `kind = Adjustment`, qty bertanda, `original_date` wajib berada pada periode tertutup, `work_date` pada periode terbuka.
- Trigger database melarang `UPDATE` dan `DELETE` pada `audit_logs` (immutability).

---

## ADR-006 Snapshot Work Order

Saat WO di-Release, dalam satu transaksi disalin: routing (`wo_routing_snapshot`), tarif (`wo_tariff_snapshot`), dan label FG/Color/Size (kolom `*_code`, `*_name` di `work_orders` dan `wo_lines`). Seluruh perhitungan upah memakai tabel snapshot, **tidak pernah** tarif master. Snapshot tidak dapat diubah (tidak ada endpoint pembaruan; tidak ada kolom yang di-UPDATE).

---

## ADR-007 Penomoran Dokumen

Kode `PO-YYYYMM-NNNN`, `WO-…`, `SJ-…`, dan kode otomatis master (`CUS-`, `VND-`, `PKJ-`) dibuat dari tabel `number_sequences` (`key`, `next_value`) dengan upsert atomik di dalam transaksi bisnis:

```sql
INSERT INTO number_sequences(key, next_value) VALUES (@key, 1)
ON CONFLICT (key) DO UPDATE SET next_value = number_sequences.next_value + 1
RETURNING next_value;
```
Karena berada di transaksi yang sama, rollback ikut membatalkan kenaikan (tanpa lubang nomor). Kunci bulanan memakai bagian `YYYYMM` dari tanggal dokumen sistem (zona waktu aplikasi). Pembuatan dokumen tipe yang sama diserialkan sangat singkat; diterima untuk skala ini.

---

## ADR-008 Galat: RFC 7807 Problem Details

Semua galat API memakai `application/problem+json` dengan `type` berupa URN `urn:gms:problem:<kode>`, ekstensi `traceId`, `errors` (validasi per field), dan `violations` (penahan aturan bisnis). Katalog lengkap ada di `ApiConventions.md`. Pengecualian tidak tertangani menjadi 500 generik **tanpa** detail internal (hanya `traceId`). Web mem-parsing Problem Details menjadi state UI sesuai `docs/Design/_Foundation-UI.md` bagian 7.

---

## ADR-009 Audit

Penulisan `audit_logs` (siapa, kapan, jenis, objek, nilai sebelum/sesudah dalam `jsonb`, IP) dilakukan lewat `IAuditWriter` **di dalam transaksi bisnis** yang sama. Nilai rahasia (password, hash, token) dilarang masuk. Tabel dilindungi trigger anti-UPDATE/DELETE.

---

## ADR-010 Waktu dan Zona

`TimeProvider` (bawaan .NET) diinjeksi untuk testabilitas. "Hari ini" dan batas bulan penomoran memakai zona waktu aplikasi (`App:TimeZone`, default `Asia/Jakarta`). Tanggal bisnis dari klien dikirim sebagai `yyyy-MM-dd` tanpa zona.

---

## ADR-011 Dokumen Keluaran (PDF dan Excel)

- Dihasilkan di **Api** (bukan di Web): `Features/Pengiriman/Print`, `Features/Payroll/Slip`, `Features/Payroll/Export`.
- PDF: **PDFsharp-MigraDoc 6.2.4** dengan satu komponen `PdfDocumentFactory` bersama (kop, gaya tabel, watermark). Font berlisensi terbuka disertakan sebagai berkas aset dan didaftarkan lewat font resolver agar berjalan di Linux/Docker. Excel: **ClosedXML 0.105.1**. CSV tanpa library.
- Web menyajikan unduhan melalui endpoint tipis yang meneruskan (stream) respons API dengan token pengguna. Browser tidak berbicara langsung ke API.
- Ukuran besar: ekspor di-stream, rincian dipaging di server.

---

## ADR-012 Strategi Pengujian

| Level | Alat | Catatan |
|---|---|---|
| Unit/handler | xUnit | Aturan domain dan validator |
| Integrasi | xUnit + PostgreSQL nyata | Wajib untuk invariant konkurensi; database uji via Docker. Testcontainers adalah opsi dan **perlu izin** |
| Komponen UI | bUnit | Perlu izin package |
| E2E | Playwright | Perlu izin package |

Package pengujian **belum diizinkan**; diminta saat fase Backend/QA.

---

## ADR-013 Konfigurasi dan Rahasia

Tidak ada kredensial di repository. Connection string (tanpa password) hanya sebagai templat di `appsettings.json`; password lewat **user-secrets** (dev) atau **environment variable** `ConnectionStrings__Default` (UAT/produksi). Konfigurasi aplikasi: `App:TimeZone`, `App:Comfi:{Name,Address}`, `App:Konveksi:{Name,Address}`, password Administrator awal (`Seed:AdminPassword`, hanya dipakai seeder pada database kosong).

---

## ADR-014 Kontrak untuk Pengembangan Paralel FE/BE

- **Shared** memuat: DTO request/response per modul, enum, katalog permission, tipe Problem Details, konstanta rute, dan **antarmuka klien bertipe** per modul (mis. `IPurchaseOrdersApi`).
- **Web** memiliki dua implementasi tiap antarmuka: `HttpXxxApi` (HttpClient ke API) dan `MockXxxApi` (data dalam memori, mengikuti contoh data seeder). Konfigurasi `Api:UseMock` memilihnya. Dengan begitu Frontend dapat membangun UI sebelum endpoint Backend ada, dan perilaku galat (Problem Details, `violations`) dapat disimulasikan.
- Backend mengimplementasikan endpoint sesuai `ApiConventions.md` dan DTO Shared. Perubahan DTO/kontrak hanya melalui Architect (ditulis di Log CHECKPOINT).

---

## ADR-015 Hal yang Sengaja Ditunda

Cache permission, outbox/event bus, multi-gudang, multi-tenant, idempotency-key pada POST, dan penyegaran data real-time antar sesi. Semuanya tidak dibutuhkan FSD fase 1 dan dapat ditambahkan tanpa mengubah kontrak yang ada.
