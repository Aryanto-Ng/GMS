# BUG-001 — Master Data: filter `status` huruf kecil ditolak (400) sehingga semua daftar master gagal dimuat

| Atribut | Nilai |
|---|---|
| Ditemukan oleh | QA (E2E Playwright terhadap tumpukan nyata: PostgreSQL + Gms.Api + Gms.Web `UseMock=false`) |
| Tanggal | 2026-11-10 |
| Tingkat | **Tinggi** (memblokir seluruh layar Master Data dan form yang memuat daftar master: Buat/Ubah PO, tab Size Berlaku FG, dsb.) |
| Pemilik | **Backend** (`src/Backend/Gms.Api/Features/Master/MasterEndpoints.cs`, parameter `ActiveFilter? status`) |
| Bukti uji | `Gms.E2E.Tests.ContractE2ETests.Filter_status_master_menerima_nilai_huruf_kecil_sesuai_kontrak` (3 kasus gagal) |

## Langkah Reproduksi
1. Jalankan API + PostgreSQL (mis. lewat `dotnet test --project src/Tests/Gms.E2E.Tests`), login sebagai Administrator.
2. `GET /api/v1/master/uoms?status=active` (juga `inactive`, `all`) dengan header `Authorization: Bearer <token>`.
3. Atau buka di UI (UseMock=false): `/master/uoms`, `/master/sizes`, `/master/fgs`, `/purchase-orders/new`, dst.

## Expected
`docs/Architecture/ApiConventions.md` bagian 4.2: *"GET `/master/{e}` (filter `q`, `status=active|inactive|all`)"* → respons **200** dengan daftar yang tersaring. Nilai huruf kecil adalah bentuk yang didokumentasikan kontrak (dan yang dikirim klien web).

## Actual
**400 Bad Request**:
```json
{"type":"urn:gms:problem:bad-request","title":"Permintaan tidak valid.","status":400,"detail":"Isi permintaan tidak dapat dipahami."}
```
Nilai berhuruf besar (`status=Active`, `All`) diterima (200) — hanya nama enum persis yang dikenali. Tes integrasi backend selama ini memakai nama enum persis sehingga tidak menangkapnya.

## Dampak
- UI: daftar Satuan, Kategori Material, Material, Size, Color, Customer, Process, Vendor, Pekerja, FG menampilkan state galat "Data belum valid"; form PO baru/ubah gagal memuat; tab Size FG gagal.
- Tidak ada jalan pintas dari sisi klien yang sesuai kontrak.

## Saran Perbaikan (untuk Backend)
Ikat `status` tanpa membedakan huruf besar/kecil (mis. parameter `string? status` + `Enum.TryParse(ignoreCase: true)` atau model binder/`ApiHost` yang menerima enum case-insensitive pada query), dan tambahkan kasus huruf kecil pada tes integrasi API. Periksa parameter enum query lain (`UserListQuery.Status`, `EntryListQuery.Kind/Status`, `PoListQuery.Status`) — uji kontrak QA menunjukkan nama enum persis diterima, huruf kecil belum diuji untuk endpoint selain `/master`.

## Catatan QA
Halaman terdampak dikecualikan dari `SmokeE2ETests` (diberi komentar `BUG-001`) agar halaman lain tetap terverifikasi; kembalikan setelah perbaikan.

## Resolusi (2026-11-10)
Diperbaiki oleh Backend: parameter `string? status` di-parse `ignoreCase`; nilai tak valid → 400. Terverifikasi QA: API 504/504, E2E 36/36. **CLOSED.**
