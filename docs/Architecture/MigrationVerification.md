# Verifikasi Migrasi terhadap PostgreSQL Nyata

| Atribut | Nilai |
|---|---|
| Tanggal | 2026-11-08 |
| Pelaksana | Software Architect |
| Lingkungan | PostgreSQL 17.11 (Docker `postgres:17`, container sementara tanpa volume, dibuang setelah uji) |
| Migrasi diuji | `20261108141554_Initial`, `20261108141600_AdvancedConstraints` |
| Artefak | `verification/constraint-tests.sql`, `verification/EfRuntimeSmoke.cs.txt` |

## Hasil Ringkas

| Uji | Hasil |
|---|---|
| `database update` (Initial + AdvancedConstraints) | Berhasil. 47 tabel + `__EFMigrationsHistory`. Ekstensi `citext` dan `btree_gist` terpasang. |
| Skrip constraint SQL (`constraint-tests.sql`) | **64 uji: 64 lulus.** (Satu kegagalan awal #63 adalah cacat setup tes, bukan skema; setelah setup diperbaiki cascade terbukti bekerja.) |
| Unik DEFERRABLE routing (transaksi nyata dengan COMMIT) | Tukar urutan dalam satu transaksi **berhasil**; duplikasi seq **ditolak saat COMMIT** dan data kembali utuh; tarif tidak hilang. |
| Uji runtime EF Core (`EfRuntimeSmoke.cs.txt`) | **12 uji: 12 lulus** |
| `database update 0` (rollback penuh) lalu naik lagi | Bersih: 0 tabel dan 0 fungsi tersisa setelah rollback; 48 tabel setelah naik ulang |
| `migrations has-pending-model-changes` | "No changes have been made to the model" (tidak ada drift) |

## Yang Terbukti

- **Sesi tunggal:** sesi aktif kedua per pengguna ditolak (23505), sesi yang sudah dicabut tidak dibatasi, pengguna lain tidak terpengaruh, `token_hash` ganda ditolak.
- **Periode gaji:** tumpang tindih ditolak (exclusion 23P01, termasuk berimpit di batas hari), berurutan tanpa celah diterima, periode Open kedua ditolak, `end < start` dan status tidak valid ditolak.
- **Kode unik tanpa peka huruf besar-kecil** (`citext`) pada master; runtime EF: pencarian `smkxxxx` menemukan `SMKXXXX`.
- **Routing/tarif:** proses ganda per FG ditolak; tarif untuk langkah/size yang tidak sah ditolak (FK komposit); tarif 0 ditolak; tarif 5 desimal kembali persis (`1250.12345`); hapus langkah/size menghapus tarif terkait saja.
- **Sisa Rencana ≥ 0 dijaga database:** `allocated + in_production + delivered > qty` ditolak (juga lewat EF, 23514); tepat nol diterima.
- **Nomor PO customer:** unik per customer, tidak peka huruf besar-kecil, boleh kosong ganda, bebas dipakai ulang dari PO Cancelled.
- **Stok:** negatif dan teralokasi > fisik ditolak.
- **Output:** langkah di luar snapshot routing ditolak (FK komposit); Adjustment wajib `original_date` dan `reason`; Voided wajib `void_reason`; qty 0 ditolak. Qty negatif **diterima oleh DB** (aturan "hanya Adjustment" ditegakkan aplikasi).
- **Audit log:** `UPDATE`, `DELETE`, dan `TRUNCATE` ditolak trigger (23000); `INSERT` diterima.
- **Integritas referensial:** pelanggan yang dipakai PO tidak dapat dihapus (RESTRICT); WO yang sudah punya output tidak dapat dihapus (RESTRICT pada snapshot routing); hapus PO tanpa WO menghapus baris PO (CASCADE).
- **Konkurensi optimistis:** `xmin` terisi setelah simpan dan berubah setelah UPDATE; penyimpan kedua pada baris yang sama menghasilkan `DbUpdateConcurrencyException`.
- **Enum** tersimpan sebagai teks (`Vendor`) dan dibaca kembali sebagai enum; **DateOnly** kembali persis.

## Keterbatasan (jujur)

- Yang diuji adalah **skema dan pemetaan**, bukan logika bisnis. Aturan seperti kumulatif output antar proses, alokasi otomatis, dan penutupan periode belum ada kodenya; itu tugas Backend dan masing-masing wajib punya integration test.
- **Uji konkurensi dua transaksi paralel pada level penguncian** (urutan kunci ADR-004) belum dilakukan karena belum ada use case. Uji yang dilakukan hanya konkurensi optimistis `xmin`.
- Tabel Identity (`users`) diuji lewat insert langsung; alur `UserManager`/lockout belum diuji (tugas Backend).
- Satu cacat sudah diperbaiki sebelum verifikasi ini: kolom `xmin` pada kode migrasi (bukan pada DDL SQL yang dihasilkan).

## Untuk Backend

- Porting `verification/constraint-tests.sql` menjadi integration test xUnit + Testcontainers (`Testcontainers.PostgreSql` sudah diizinkan). Jadikan setiap baris tabel hasil sebagai satu `[Fact]`/`[Theory]`.
- `EfRuntimeSmoke.cs.txt` dapat dipakai sebagai kerangka tes pemetaan EF.
- Tambahkan tes konkurensi penguncian setelah use case Output/Stok/Pengiriman tersedia: dua transaksi paralel pada WO/baris PO/stok yang sama harus menghasilkan satu sukses dan satu 409 tanpa deadlock.

## Cara Mengulang

```bash
docker run -d --name gms-pg-test -e POSTGRES_PASSWORD=<acak> -e POSTGRES_DB=gms_design -p 54329:5432 postgres:17
export GMS_DESIGN_CONNECTION="Host=localhost;Port=54329;Database=gms_design;Username=postgres;Password=<acak>"
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Backend/Gms.Infrastructure --startup-project src/Backend/Gms.Api
docker exec -i gms-pg-test psql -U postgres -d gms_design < docs/Architecture/verification/constraint-tests.sql
docker rm -f gms-pg-test
```
Jangan menaruh kata sandi di berkas yang di-commit.
