# Konvensi dan Kontrak API — GMS

| Atribut | Nilai |
|---|---|
| Versi | 1.0 (konvensi + peta endpoint; DTO ada di `src/Shared`) |
| Penulis | Software Architect |
| Dasar | `docs/Architecture/ADR.md`, `docs/Architecture/Database.md`, `docs/FSD/*.md` |

---

## 1. Umum

| Aspek | Aturan |
|---|---|
| Dasar rute | `/api/v1` |
| Format | JSON `application/json`, `camelCase`, UTF-8. Nilai `null` tidak ditulis (`WhenWritingNull`). |
| Tipe | Id = GUID string. Enum = string PascalCase. Tanggal bisnis = `yyyy-MM-dd`. Waktu jejak = ISO-8601 UTC. Uang/tarif = angka desimal JSON (dikonversi ke `decimal` di C#, tanpa floating point). |
| Autentikasi | `Authorization: Bearer <token sesi>` (ADR-002). Publik: `POST /auth/login`, `GET /health`. |
| Otorisasi | Setiap endpoint menyatakan permission yang dibutuhkan (kolom **Izin**). `—` = cukup login. |
| Konkurensi | Respons agregat yang dapat diedit memuat `rowVersion` (uint). Permintaan ubah/aksi menyertakannya. Ketidakcocokan → 409 `concurrency-conflict`. |
| Paging | `?page=1&pageSize=25` (maks 200). Respons `PagedResult<T> { items, page, pageSize, totalCount }`. |
| Urutan | `?sort=field:asc|desc` (field diizinkan per endpoint). |
| Pencarian/filter | `?q=` teks bebas + filter spesifik per endpoint (nama parameter di bagian 5). Parameter bersama Beranda: `terlambat`, `jatuhTempo7`, `poCustomerKosong`, `sisaRencana`, `hariIni`. |
| Pratinjau dampak | Endpoint `.../preview` mengembalikan `ImpactPreview { rows[], canProceed, violations[] }` tanpa menyimpan apa pun. Aksi sesungguhnya memvalidasi ulang di server. |
| Unduhan berkas | `application/pdf`, `.xlsx`, `text/csv` dengan `Content-Disposition`. Galat tetap Problem Details. |
| Idempotensi | Tidak dipakai pada fase 1 (ADR-015). UI mencegah kirim ganda. |
| Bahasa pesan | `title`/`detail`/pesan validasi dalam Bahasa Indonesia. `code` stabil dalam Bahasa Inggris. |

---

## 2. Standar Galat — RFC 7807 Problem Details

Semua galat: `Content-Type: application/problem+json`.

```json
{
  "type": "urn:gms:problem:rule-violation",
  "title": "Aturan kumulatif output dilanggar.",
  "status": 409,
  "detail": "Output Sewing untuk Navy M tidak boleh melebihi output Cutting.",
  "code": "output.cumulative-exceeded",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00",
  "errors": { "lines[0].qty": ["Maksimal 40."] },
  "violations": [
    { "code": "output.cumulative-exceeded", "subject": "Sewing · Navy · M", "current": 110, "limit": 100, "message": "Tertahan oleh QC (40)." }
  ]
}
```

| Anggota | Arti |
|---|---|
| `type` | URN dari katalog di bawah (stabil) |
| `title`, `detail` | Pesan manusiawi (Bahasa Indonesia) |
| `status` | Kode HTTP |
| `code` | Kode mesin yang lebih spesifik (opsional) |
| `traceId` | Wajib; ditampilkan di UI untuk pelaporan |
| `errors` | Validasi per field: kunci = jalur field (`header.customerId`, `lines[2].qty`) |
| `violations` | Penahan aturan bisnis (sel, kuantitas saat ini, batas, pesan) |

### Katalog `type` (`urn:gms:problem:<kode>`)

| Kode | HTTP | Kapan |
|---|---|---|
| `validation-failed` | 422 | Validasi field gagal (`errors`) |
| `bad-request` | 400 | Permintaan tidak dapat dipahami |
| `invalid-credentials` | 401 | Username atau password salah, atau akun nonaktif (pesan sama, AC-AM-02/04) |
| `account-locked` | 401 | Terkunci (menyertakan `lockedUntil`) |
| `session-expired` | 401 | Sesi habis (tidak aktif 30 menit) |
| `session-replaced` | 401 | Login dari perangkat lain |
| `account-disabled` | 401 | Pengguna dinonaktifkan/sesi diakhiri paksa |
| `must-change-password` | 403 | Wajib ganti password sebelum memakai API (kecuali `/auth/*`) |
| `forbidden` | 403 | Tidak memiliki permission |
| `not-found` | 404 | Data tidak ditemukan |
| `concurrency-conflict` | 409 | `rowVersion` tidak cocok |
| `duplicate` | 409 | Kode/nama/nomor sudah dipakai (`errors` menunjuk field) |
| `state-conflict` | 409 | Status dokumen tidak mengizinkan aksi |
| `rule-violation` | 409 | Aturan bisnis dilanggar; `violations` terisi (kumulatif, stok, alokasi, Sisa Rencana, tarif belum lengkap, periode, dst.) |
| `period-closed` | 409 | Tanggal kerja berada pada periode gaji tertutup (mengarahkan ke Penyesuaian) |
| `import-invalid` | 422 | Berkas impor ditolak; `rows[]` berisi galat per baris |
| `internal-error` | 500 | Galat tak terduga (tanpa detail internal) |
| `service-unavailable` | 503 | Dependensi (DB) tidak tersedia |

### Pemetaan ke UI
Lihat `docs/Design/_Foundation-UI.md` bagian 7. `401 session-*` → ke halaman Login dengan pesan sesuai tipe. `409 concurrency-conflict` → banner "Muat ulang". `409 rule-violation` → dialog/banner dengan `violations`.

---

## 3. Autentikasi (`/api/v1/auth`)

| Metode | Rute | Izin | Keterangan |
|---|---|---|---|
| POST | `/auth/login` | publik | `{ username, password, ipAddress?, userAgent? }` → `LoginResponse { token, expiresInMinutes, user, mustChangePassword, permissions[] }`. Mencabut sesi lama (`Replaced`). |
| POST | `/auth/logout` | — | Mencabut sesi saat ini |
| GET | `/auth/me` | — | Profil + `permissions[]`. Dipakai juga sebagai "tetap masuk". |
| POST | `/auth/change-password` | — | `{ currentPassword, newPassword }`. Diizinkan walau `must-change-password`. |

---

## 4. Peta Endpoint per Modul

Notasi: **R** = membaca, daftar mendukung paging dan filter. Semua rute berawalan `/api/v1`.

### 4.1 Access Management (Administrator)
| Metode | Rute | Izin |
|---|---|---|
| GET/POST | `/users` | `AccessManagement.User.View/Create` |
| GET/PUT | `/users/{id}` | `…User.View/Edit` |
| POST | `/users/{id}/reset-password` | `…User.ResetPassword` |
| POST | `/users/{id}/unlock` | `…User.Unlock` |
| POST | `/users/{id}/end-session` | `…User.EndSession` |
| POST | `/users/{id}/deactivate?rowVersion=N`, `/users/{id}/activate?rowVersion=N` (`rowVersion` sebagai query string) | `…User.Deactivate` |
| GET/POST | `/roles` | `…Role.View/Create` |
| GET/PUT/DELETE | `/roles/{id}` | `…Role.View/Edit/Delete` |
| GET | `/permissions` | `…Role.View` (katalog untuk matriks) |
| GET | `/audit-logs` | `…AuditLog.View` (filter: `from`, `to` wajib, `actorId`, `eventType`) |

### 4.2 Master Data
Pola untuk entitas `uoms`, `material-categories`, `materials`, `sizes`, `colors`, `customers`, `processes`, `vendors`, `workers`, `fgs`, di bawah `/master/{entitas}`:

| Metode | Rute | Izin |
|---|---|---|
| GET | `/master/{e}` (filter `q`, `status=active|inactive|all`) | `MasterData.{E}.View` |
| POST | `/master/{e}` | `…Create` |
| GET/PUT | `/master/{e}/{id}` | `…View/Edit` |
| POST | `/master/{e}/{id}/deactivate`, `/activate` | `…Deactivate` |
| GET | `/master/{e}/lookup?q=` | `…View` (daftar ringan untuk dropdown/combobox, hanya aktif) |

Catatan perilaku (diimplementasikan & diuji):
- Daftar: `?page&pageSize&q&sort=code|name:asc|desc&status=Active|Inactive|All` (bawaan `Active`). Daftar FG menambah `readyOnly=true` (hanya Siap Produksi; `FgListQuery` di Shared). Ukuran `Size` terurut `sortOrder` lalu kode.
- `POST .../deactivate` dan `.../activate` menerima `rowVersion` sebagai **query string**.
- `lookup`: hanya data aktif, maksimal 50 baris, urut kode.
- Kode otomatis (`customers` = `CUS-0001`, `vendors` = `VND-0001`, `workers` = `PKJ-0001`) tidak ada di body simpan. Kode manual dikirim di body dan terkunci (422 pada field `code`) bila sudah dipakai data lain.
- Penolakan nonaktif karena masih dipakai: 409 `rule-violation` dengan `code` `master.in-use` (atau `fg.has-open-documents`) dan `violations[].subject` = pemakai (mis. `FG KMJ-001`).

Khusus FG:
| Metode | Rute | Izin |
|---|---|---|
| GET/PUT | `/master/fgs/{id}/sizes` | `MasterData.FG.View/Edit` |
| GET/PUT | `/master/fgs/{id}/routing` | `MasterData.Routing.View/Edit` |
| GET/PUT | `/master/fgs/{id}/tariffs` | `MasterData.Tarif.View/Edit` |
| GET | `/master/fgs/{id}/readiness` | `MasterData.FG.View` |
| POST | `/master/fgs/{id}/copy-from/preview`, `/copy-from` | `MasterData.Routing.Edit` dan `MasterData.Tarif.Edit` |

Semantik impor (diimplementasikan & diuji):
- `dryRun` bawaan **true** (aman). Respons selalu `200` + `ImportResultDto` untuk galat **per baris** (`Committed=false`, `Rows[].RowNumber` = nomor baris berkas, judul = baris 1). Impor sungguhan (`dryRun=false`) hanya menyimpan bila SEMUA baris valid (semua-atau-tidak-sama-sekali).
- Galat **tingkat berkas** (judul kolom salah, kosong, bukan UTF-8, kutip tidak tertutup, >2.000 baris, >2 MB) = `422 import-invalid` dengan `code = import.file-invalid`.
- Isi bukan multipart = `415`; field `file` tidak ada = `400 bad-request`. Pemisah koma atau titik koma terdeteksi otomatis; BOM diterima.

Impor (Material, FG, Pekerja):
| Metode | Rute | Izin |
|---|---|---|
| GET | `/master/{materials|fgs|workers}/import-template` | `…Import` |
| POST | `/master/{materials|fgs|workers}/import?dryRun=true|false` (multipart) | `…Import` |

### 4.3 Customer PO
| Metode | Rute | Izin |
|---|---|---|
| GET | `/purchase-orders` (filter: `status`, `customerId`, `from`, `to`, `terlambat`, `jatuhTempo7`, `poCustomerKosong`, `sisaRencana`) | `CustomerPO.View` |
| POST | `/purchase-orders` | `CustomerPO.Create` |
| GET | `/purchase-orders/{id}` | `CustomerPO.View` |
| PUT | `/purchase-orders/{id}` (header + baris; revisi) | `CustomerPO.Edit` |
| PUT | `/purchase-orders/{id}/customer-po-number` | `CustomerPO.Edit` |
| POST | `/purchase-orders/{id}/confirm` | `CustomerPO.Confirm` |
| POST | `/purchase-orders/{id}/cancel` | `CustomerPO.Cancel` |
| GET | `/purchase-orders/{id}/allocations` | `CustomerPO.Alokasi.View` |
| GET | `/po-lines/{lineId}/stock-availability` | `CustomerPO.Alokasi.View` |
| POST | `/po-lines/{lineId}/allocate` | `CustomerPO.Alokasi.Allocate` |
| POST | `/po-lines/{lineId}/release-allocation` | `CustomerPO.Alokasi.Release` |

**Catatan implementasi (backend, 2026-11-08):**
- `confirm` menerima `rowVersion` sebagai **query string** (`?rowVersion=N`); `cancel` menerima body `ReasonRequest { reason (wajib, ≤500), rowVersion }`.
- Pembatalan ditolak 409 `po.cancel-blocked` bila ada pengiriman aktif, qty terkirim, atau WO yang belum Closed/Cancelled; bila lolos, seluruh alokasi dilepas otomatis (sumber `PoCancel`).
- Alokasi/pelepasan melebihi batas → 409 `allocation.exceeded` dengan `violations[0].current` (diminta) dan `limit` (batas terbaru). Revisi di bawah komitmen → 409 `po.qty-below-committed`; menghapus baris terpakai → 409 `po.line-in-use`.
- Daftar `GET /purchase-orders` mendukung `sort=code|poDate:asc|desc` (default `poDate:desc`) dan `q` (kode, nomor PO customer, nama customer).

### 4.4 Work Order
| Metode | Rute | Izin |
|---|---|---|
| GET | `/work-orders` (filter: `status`, `poId`, `customerId`, `fgId`, `from`, `to`, `terlambat`) | `WorkOrder.View` |
| GET | `/work-orders/creation-options?poId=` | `WorkOrder.Create` (FG pada PO + Sisa Rencana per sel + kesiapan FG) |
| POST | `/work-orders` | `WorkOrder.Create` |
| GET | `/work-orders/{id}` | `WorkOrder.View` |
| PUT | `/work-orders/{id}` (Draft) | `WorkOrder.Edit` |
| PUT | `/work-orders/{id}/quantities` (Released/InProgress) | `WorkOrder.Edit` |
| POST | `/work-orders/{id}/release/preview`, `/release` | `WorkOrder.Release` |
| POST | `/work-orders/{id}/complete` | `WorkOrder.Complete` |
| POST | `/work-orders/{id}/resume` (Completed → InProgress) | `WorkOrder.Complete` |
| POST | `/work-orders/{id}/close/preview`, `/close` | `WorkOrder.Close` |
| POST | `/work-orders/{id}/reopen` | `WorkOrder.Reopen` |
| POST | `/work-orders/{id}/cancel` | `WorkOrder.Cancel` |
| GET | `/work-orders/{id}/progress` | `WorkOrder.View` |
| GET | `/work-orders/{id}/tariff-snapshot` | `WorkOrder.Tarif.View` |
| GET | `/work-orders/{id}/status-history` | `WorkOrder.View` |
| PUT | `/work-orders/{id}/schedule` (Draft/Released/InProgress: tanggal + keterangan) | `WorkOrder.Edit` |

**Catatan implementasi (backend, 2026-11-09):**
- Semua aksi status (`release`, `complete`, `resume`, `close`, `reopen`, `cancel`) memakai body `WoActionRequest { reason, rowVersion }`; alasan wajib untuk `cancel`, `resume`, `reopen`. Dua penekan bersamaan: yang kalah mendapat 409 `state-conflict` atau `concurrency-conflict`.
- `PUT /work-orders/{id}/quantities` harus mengirim SELURUH baris WO (baris tidak dapat ditambah/dihapus). Galat: 409 `wo.qty-below-output` (di bawah output proses pertama), 409 `po.remaining-plan-exceeded` (kenaikan; `violations[].limit` = batas terbaru).
- Rilis ditolak 409 `fg.not-ready` (dengan `violations` per butir) bila FG belum siap; `po.remaining-plan-exceeded` bila Sisa Rencana tidak cukup. Cancel ditolak 409 `wo.has-activity` bila ada output, pengiriman vendor, atau serah terima.
- Snapshot tarif hanya lewat `/tariff-snapshot` (`WorkOrder.Tarif.View`); detail, progres, dan daftar tidak pernah memuat tarif.

### 4.5 Production Output (+ Vendor)
| Metode | Rute | Izin |
|---|---|---|
| GET | `/production/input-context?woId=&seq=&workDate=` | `ProductionOutput.Create` (batas tersedia per sel) |
| POST | `/production/outputs` (banyak pekerja, semua-atau-tidak-sama-sekali) | `ProductionOutput.Create` |
| GET | `/production/entries` (gabungan Output, Penyesuaian, Kirim, Terima; filter `woId`, `seq`, `workerId`, `vendorId`, `from`, `to`, `kind`, `status`, `hariIni`) | `ProductionOutput.View` / `VendorJob.View` |
| GET | `/production/entries/{id}` | View |
| POST | `/production/entries/{id}/void/preview`, `/void` | `ProductionOutput.Void` |
| POST | `/production/entries/{id}/correct/preview`, `/correct` | `ProductionOutput.Correct` |
| POST | `/production/adjustments` | `ProductionOutput.Adjust` |
| GET | `/production/vendor-context?woId=&seq=&vendorId=` | `VendorJob.Create` |
| POST | `/production/vendor-movements` (`kind` = Send/Receive) | `VendorJob.Create` |
| POST | `/production/vendor-movements/{id}/void/preview`, `/void` | `VendorJob.Void` |
| POST | `/production/vendor-movements/{id}/correct/preview`, `/correct` | `VendorJob.Create` dan `VendorJob.Void` |

### 4.6 Serah Terima & Stok FG
| Metode | Rute | Izin |
|---|---|---|
| GET | `/handovers/fg/context?woId=` | `SerahTerimaFG.Create` (dapat diserahkan per sel + perkiraan alokasi) |
| GET/POST | `/handovers/fg` | `SerahTerimaFG.View/Create` |
| GET | `/handovers/fg/{id}` | `SerahTerimaFG.View` |
| POST | `/handovers/fg/{id}/void/preview`, `/void` | `SerahTerimaFG.Void` |
| POST | `/handovers/fg/{id}/correct/preview`, `/correct` | `SerahTerimaFG.Correct` |
| GET/POST | `/handovers/material` | `SerahTerimaMaterial.View/Create` |
| GET | `/handovers/material/{id}` | `SerahTerimaMaterial.View` |
| POST | `/handovers/material/{id}/void` | `SerahTerimaMaterial.Void` |
| GET | `/stock` (filter `fgId`, `colorId`, `sizeId`, `customerId`, `hanyaAdaStok`) | `StokFG.View` |
| GET | `/stock/ledger?fgId=&colorId=&sizeId=&from=&to=` | `StokFG.View` |
| POST | `/stock/adjustments` | `StokFG.Adjust` |
| GET | `/stock/opening-balance/import-template` | `StokFG.Import` |
| POST | `/stock/opening-balance/import?dryRun=` (multipart) | `StokFG.Import` |

### 4.7 Pengiriman
| Metode | Rute | Izin |
|---|---|---|
| GET | `/deliveries/context?poId=` | `Pengiriman.Create` (maks kirim per baris PO) |
| GET/POST | `/deliveries` | `Pengiriman.View/Create` |
| GET | `/deliveries/{id}` | `Pengiriman.View` |
| PUT | `/deliveries/{id}/info` (non-qty) | `Pengiriman.Edit` |
| POST | `/deliveries/{id}/void/preview`, `/void` | `Pengiriman.Void` |
| POST | `/deliveries/{id}/correct/preview`, `/correct` | `Pengiriman.Correct` |
| GET | `/deliveries/{id}/pdf` | `Pengiriman.Print` (menaikkan `print_count`, mencatat audit) |

### 4.8 Payroll
| Metode | Rute | Izin |
|---|---|---|
| GET | `/payroll/periods` | `Payroll.View` |
| GET | `/payroll/periods/next-defaults` | `Payroll.CreatePeriod` (tanggal mulai terkunci + petunjuk output tertua) |
| POST | `/payroll/periods` | `Payroll.CreatePeriod` |
| GET | `/payroll/periods/{id}` | `Payroll.View` |
| PUT | `/payroll/periods/{id}` (Open: tanggal selesai, catatan) | `Payroll.CreatePeriod` |
| POST | `/payroll/periods/{id}/close/preview`, `/close` | `Payroll.ClosePeriod` |
| GET | `/payroll/periods/{id}/summary` | `Payroll.View` |
| GET | `/payroll/periods/{id}/workers/{workerId}/detail?view=wo|daily` | `Payroll.View` |
| GET | `/payroll/periods/{id}/daily` (filter tanggal, pekerja, WO, proses) | `Payroll.View` |
| GET | `/payroll/periods/{id}/matrix` | `Payroll.View` |
| GET | `/payroll/periods/{id}/adjustments` | `Payroll.View` |
| GET | `/payroll/periods/{id}/export?format=xlsx|csv-summary|csv-daily` | `Payroll.Export` |
| GET | `/payroll/periods/{id}/slips?workerId=&daily=` | `Payroll.PrintSlip` |

### 4.9 Beranda
Satu endpoint ringan per widget (dimuat independen, izin dicek per widget): `GET /dashboard/{widgetId}` dengan `widgetId` ∈ `po-late`, `due-soon`, `po-no-customer-number`, `unplanned-demand`, `wo-running`, `wo-late`, `output-today`, `vendor-balance`, `unhanded-over`, `stock-summary`, `payroll-period`, `security`.

| Widget | Izin |
|---|---|
| po-late, due-soon, po-no-customer-number, unplanned-demand | `CustomerPO.View` |
| wo-running, wo-late | `WorkOrder.View` |
| output-today | `ProductionOutput.View` |
| vendor-balance | `VendorJob.View` |
| unhanded-over | `SerahTerimaFG.View` |
| stock-summary | `StokFG.View` |
| payroll-period | `Payroll.View` |
| security | `AccessManagement.AuditLog.View` |

### 4.10 Sistem
`GET /health` (publik, tanpa data sensitif).

### 4.11 Endpoint Tambahan (selaras antarmuka klien di `Gms.Shared`)
Ditemukan saat menyusun antarmuka bertipe; melengkapi tabel di atas.

| Metode | Rute | Izin |
|---|---|---|
| PUT | `/work-orders/{id}/schedule` (tanggal mulai/target/keterangan; sebelum Closed) | `WorkOrder.Edit` |
| POST | `/handovers/fg/preview` (perkiraan alokasi otomatis vs stok bebas) | `SerahTerimaFG.Create` |
| POST | `/stock/adjustments/preview` (hasil Fisik/Tersedia sebelum simpan) | `StokFG.Adjust` |
| GET | `/deliveries/eligible-pos?q=` (PO yang masih punya kebutuhan untuk dikirim) | `Pengiriman.Create` |
| POST | `/production/outputs` ditambah penanganan konfirmasi: bila Kum langkah pertama melebihi Qty WO dan `confirmExceedWoQty = false`, server membalas 409 `rule-violation` dengan `code = output.exceeds-wo-qty-needs-confirmation`; klien meminta konfirmasi lalu mengirim ulang dengan `true`. | `ProductionOutput.Create` |
| GET | `/payroll/periods/{id}/summary?q=` (filter nama/kode pekerja) | `Payroll.View` |

Pemetaan antarmuka → rute dilakukan oleh implementasi `HttpXxxApi` di Frontend; nama metode di `Gms.Shared` adalah kontrak, bukan nama rute.

---

## 5. Aturan Respons yang Penting bagi UI

- **Daftar:** `PagedResult<T>` dengan item ringkas. Detail dimuat per id.
- **Matriks Color × Size:** selalu dikirim sebagai daftar sel `{ colorId, sizeId, ... }` (bukan struktur dua dimensi), urut Color lalu `sizes.sort_order`. Label (kode/nama) disertakan agar klien tidak perlu join.
- **Batas tersedia** (`max`) dan pratinjau dampak adalah **hasil hitung server**. Klien tidak mengimplementasikan ulang aturan kumulatif/stok.
- **Tarif dan rupiah** hanya muncul pada respons endpoint yang izinnya mensyaratkannya (`MasterData.Tarif.View`, `WorkOrder.Tarif.View`, `Payroll.View`). Respons lain tidak boleh memuatnya.
- **Status aksi** yang diizinkan: detail dokumen menyertakan `allowedActions[]` (hasil gabungan status + permission) agar UI tidak menduplikasi aturan visibilitas tombol.

---

## 6. Katalog Permission Final (di-seed)

Kunci = `Resource.Action`. Kolom **Assignable**: `Tidak` = hanya Administrator (tidak dapat diberikan ke Role lain).

| Resource | Action | Assignable |
|---|---|---|
| AccessManagement.User | View, Create, Edit, Deactivate, ResetPassword, Unlock, EndSession | Tidak |
| AccessManagement.Role | View, Create, Edit, Delete | Tidak |
| AccessManagement.AuditLog | View | Tidak |
| MasterData.UoM | View, Create, Edit, Deactivate | Ya |
| MasterData.KategoriMaterial | View, Create, Edit, Deactivate | Ya |
| MasterData.Material | View, Create, Edit, Deactivate, Import | Ya |
| MasterData.Size | View, Create, Edit, Deactivate | Ya |
| MasterData.Color | View, Create, Edit, Deactivate | Ya |
| MasterData.Customer | View, Create, Edit, Deactivate | Ya |
| MasterData.Process | View, Create, Edit, Deactivate | Ya |
| MasterData.Vendor | View, Create, Edit, Deactivate | Ya |
| MasterData.FG | View, Create, Edit, Deactivate, Import | Ya |
| MasterData.Routing | View, Edit | Ya |
| MasterData.Tarif | View, Edit | Ya |
| MasterData.Pekerja | View, Create, Edit, Deactivate, Import | Ya |
| CustomerPO | View, Create, Edit, Confirm, Cancel | Ya |
| CustomerPO.Alokasi | View, Allocate, Release | Ya |
| WorkOrder | View, Create, Edit, Release, Cancel, Complete, Close, Reopen | Ya |
| WorkOrder.Tarif | View | Ya |
| ProductionOutput | View, Create, Void, Correct, Adjust | Ya |
| VendorJob | View, Create, Void | Ya |
| SerahTerimaFG | View, Create, Void, Correct | Ya |
| SerahTerimaMaterial | View, Create, Void | Ya |
| StokFG | View, Adjust, Import | Ya |
| Pengiriman | View, Create, Edit, Void, Correct, Print | Ya |
| Payroll | View, CreatePeriod, ClosePeriod, Export, PrintSlip | Ya |

Aturan: memberi aksi selain `View` pada suatu resource **wajib** menyertakan `View` pada resource yang sama (AC-AM-18). Dipaksakan oleh API saat menyimpan Role.
*Penambahan dari FSD:* `Unlock` dan `EndSession` (AC-AM-03, AC-AM-06b).

---

## 7. Aturan Evolusi Kontrak

- Perubahan **kompatibel** (field opsional baru, endpoint baru) boleh dilakukan Backend setelah dicatat di Log CHECKPOINT.
- Perubahan **tidak kompatibel** (hapus/ubah arti field, ubah rute) hanya oleh Architect dan harus mengabari Frontend lewat Log CHECKPOINT.
- Versi API tetap `v1` selama fase 1.
