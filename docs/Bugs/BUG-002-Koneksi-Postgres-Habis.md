# BUG-002 — Koneksi PostgreSQL habis saat uji manual (SqlState 53300)

- **Ditemukan:** 2026-11-14, uji manual Ary di lingkungan lokal (Gms.Api profil `http` :5222, Gms.Web :5263, Postgres native `localhost:5432`, DB `gms`).
- **Pelapor:** Ary. **Penguji:** QA. **Keparahan:** Tinggi (API menolak semua permintaan sampai koneksi bebas).
- **Status:** Diperbaiki (batas pool), MENUNGGU UJI ULANG Ary di mesin lokal. **Akar masalah BELUM terkonfirmasi.** Dugaan `max_connections=7` DICABUT (2026-11-14): Ary memastikan `postgresql.conf` = 100, dan angka "7" di hasil query tidak terverifikasi sebagai `max_connections`.

## Gejala
```
Severity: FATAL  SqlState: 53300
MessageText: remaining connection slots are reserved for roles with the SUPERUSER attribute
Routine: InitPostgres
```
Artinya jumlah sesi ke server Postgres sudah mencapai `max_connections` dikurangi `superuser_reserved_connections`; role `gms` ditolak.

## Langkah mereproduksi
Belum ada langkah pasti. Terjadi "waktu manual test" (rincian layar/aksi belum dicatat). Mohon Ary mencatat layar yang dibuka dan jumlah tab terbuka saat error muncul.

## Temuan pemeriksaan kode (baca saja, tidak ada kode diubah)
1. **Tidak ada batas pool.** `appsettings.json`: `Host=localhost;Port=5432;Database=gms;Username=gms` tanpa `Maximum Pool Size`. Npgsql memakai bawaan 100 per proses, sedangkan `max_connections` bawaan Postgres juga 100. Satu proses API saja berpotensi menghabiskan seluruh slot bila beban paralel tinggi.
2. **Server Postgres dipakai bersama proyek lain.** Pada saat pemeriksaan, hanya ada 2 koneksi ke :5432 dan keduanya milik proses `python` (bukan Gms). Sesi dari aplikasi lain di mesin yang sama ikut memakai jatah.
3. **Beban paralel dari UI.** `TabHost` merender SEMUA tab terbuka (maks. 10) dan tiap halaman memuat data sendiri; ditambah lonceng (polling 60 dtk), chip berjumlah, dan kartu Beranda. Saat banyak tab dibuka/dipulihkan, ada lonjakan permintaan API paralel, masing-masing satu koneksi DB.
4. **Dua koneksi per simpan pada kasus tertentu.** `NotificationSideEffectsInterceptor` membuka scope + DbContext kedua (koneksi kedua) saat stok habis (NE-11) atau alokasi dilepas (NE-12), selagi koneksi transaksi utama masih terbuka. Hanya pada jalur itu, bukan pada setiap simpan.
5. **Koneksi khusus pekerja latar.** `NotificationJob` menahan satu koneksi selama pemeriksaan (advisory lock sesi), dibuka/ditutup dengan `try/finally`; tidak ditemukan kebocoran. Berjalan paling cepat tiap 5 menit.
6. Tidak ditemukan `DbContext`/koneksi yang dibuat tanpa dibuang (semua lewat DI scoped atau `await using`).

## Hasil diagnosis sementara (data dari Ary, 2026-11-14) — TIDAK KONKLUSIF
- Baris pertama hasil = `7` (tidak jelas apa; BUKAN dipastikan `max_connections` karena `postgresql.conf` = 100).
- `pg_stat_activity`: 3 sesi pgAdmin (role `postgres`: DB postgres idle, CONN aktif, DB gms idle) + 1 proses latar (tanpa usename). Tidak ada sesi `gms` saat dicek.
- (DICABUT, hanya berlaku bila batas efektif memang 7) Dengan `superuser_reserved_connections` bawaan 3, role non-superuser seperti `gms` hanya boleh memakai **7 − 3 = 4** slot, dan sesi pgAdmin (tiap tab/DB membuka sesi sendiri) memakan sebagian jatah itu. Pool Npgsql bawaan (hingga 100) dan beberapa permintaan paralel dari UI (banyak tab, lonceng, chip) melewati batas ini dengan cepat. Jadi penyebab utamanya adalah konfigurasi server, bukan kebocoran koneksi di kode; kode sudah diperiksa dan tidak ada kebocoran.
- Pada saat dicek hanya 4 sesi aktif, jadi server tidak sedang penuh; error bersifat sesaat (lonjakan) atau batas efektif berbeda dari isi `postgresql.conf`.
- **Data lanjutan yang diminta** (batas efektif bisa berbeda dari file: `postgresql.auto.conf`/`ALTER SYSTEM`, perubahan belum direstart, atau ada lebih dari satu instance Postgres):
```sql
SELECT name, setting, source, sourcefile, pending_restart FROM pg_settings
WHERE name IN ('max_connections','superuser_reserved_connections');
SELECT version(), inet_server_port(), current_setting('data_directory');
SELECT rolname, rolconnlimit FROM pg_roles WHERE rolname='gms';
```
Batas pool aplikasi tetap disarankan (lihat Saran).

## (Arsip) Data yang diminta sebelumnya
Tanpa kredensial DB, QA tidak dapat melihat `pg_stat_activity`. Jalankan saat error terulang (atau sekarang) sebagai superuser:
```sql
SHOW max_connections;
SELECT usename, application_name, client_addr, state, count(*)
FROM pg_stat_activity GROUP BY 1,2,3,4 ORDER BY 5 DESC;
```
Hasilnya menentukan: (a) apakah sesi `gms` yang menumpuk (state `idle`/`idle in transaction` → kebocoran/pool terlalu besar), atau (b) sesi aplikasi lain (python/pcms/dll) yang memakan slot.

## Dugaan penyebab (urut kemungkinan, belum dibuktikan)
1. Pool 100 + `max_connections` 100 + aplikasi lain di server yang sama.
2. Lonjakan permintaan paralel dari banyak tab terbuka/dipulihkan.
3. Sesi `idle in transaction` dari jalur yang gagal di tengah transaksi (belum ditemukan di kode).

## Saran perbaikan untuk Developer (QA tidak memperbaiki kode)
- Backend/DevOps: set `Maximum Pool Size` (mis. 20–30) dan `Minimum Pool Size` kecil di connection string Dev/UAT/Prod lewat konfigurasi (Tier 2/3), dan sesuaikan `max_connections` Postgres.
- Frontend: batasi pemuatan data tab tidak aktif (tunda sampai tab pertama kali diaktifkan) agar tidak ada lonjakan saat banyak tab.
- Tambah tes beban ringan/E2E: buka 10 tab bersamaan dan pastikan jumlah sesi `gms` tetap di bawah batas.

## Kriteria lulus uji ulang
Membuka 10 tab dan menjalankan alur PO→WO→Output tanpa error 53300; `pg_stat_activity` untuk role `gms` tidak melebihi batas pool yang ditetapkan.

## Pengukuran QA (2026-11-14)
- `max_connections` = 100 (dikonfirmasi Ary lewat `pg_settings`); slot untuk non-superuser = 100 − `superuser_reserved_connections` (bawaan 3) = 97. Pada saat error sekitar 97 sesi terpakai; sampel `pg_stat_activity` Ary diambil setelah lonjakan lewat (hanya 4 sesi pgAdmin).
- Uji: seluruh E2E (48 tes, 1 pengguna berurutan, Postgres 17 Testcontainers, API tanpa batas pool) dengan sampling `pg_stat_activity` tiap ~0,5–1,5 dtk: **puncak 28 sesi klien bersamaan** (terukur minimal; lonjakan singkat bisa lebih tinggi). Satu pengguna saja sudah memakai sepertiga jatah; pool Npgsql menyimpan koneksi menganggur hingga ±5 menit sehingga angka ini menumpuk.
- Kesimpulan sementara: error terjadi ketika beberapa tab/sesi terbuka bersamaan memicu banyak permintaan paralel dan pool tak terbatas (bawaan 100 > 97 slot) menghabiskan slot. Belum dibuktikan secara langsung pada mesin Ary; akan terbukti bila uji ulang dengan batas pool tidak lagi memunculkan 53300.

## Perbaikan (Backend, 2026-11-14)
- `ApiHost.WithPoolLimit`: `Maximum Pool Size` diterapkan ke connection string dari konfigurasi `Database:MaxPoolSize` (bawaan 20). Nilai yang sudah tertulis di connection string tidak ditimpa. Tanpa paket baru.
- Frontend: usulan "tunda muat tab tidak aktif" DIBATALKAN. Tab baru dirender saat dibuka dan langsung aktif, jadi tidak ada lonjakan dari tab tidak aktif.
- Tes: `ConnectionPoolTests` (4). API 535/535 lulus; E2E 48/48 lulus. Puncak sesi bersamaan pada E2E turun dari 28 menjadi 21 (batas 20 + sesi sampling).
- **Uji ulang Ary:** restart Gms.Api, buka banyak tab, jalankan alur PO→WO→Output; error 53300 tidak boleh muncul. Bila permintaan berlebih, tampilannya menunggu giliran (bukan galat server).
