# BUG-002 — Koneksi PostgreSQL habis saat uji manual (SqlState 53300)

- **Ditemukan:** 2026-11-14, uji manual Ary di lingkungan lokal (Gms.Api profil `http` :5222, Gms.Web :5263, Postgres native `localhost:5432`, DB `gms`).
- **Pelapor:** Ary. **Penguji:** QA. **Keparahan:** Tinggi (API menolak semua permintaan sampai koneksi bebas).
- **Status:** Terbuka. Akar masalah BELUM terkonfirmasi (lihat "Yang belum diketahui").

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

## Yang belum diketahui (butuh data dari Ary)
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
