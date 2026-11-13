# Desain UI — Notifikasi

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/Notifikasi.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** |
| Render Mode | Semua **Interactive Server** |
| Mock-up | `docs/Design/mockup/index.html` (lonceng di header; tab "Semua notifikasi" dan "Pengaturan Notifikasi" dari menu Administrasi) |

---

## 1. Halaman dan Rute

| Layar | Rute | ID tab | Izin |
|---|---|---|---|
| Lonceng + panel (header) | – (komponen shell) | – | Semua pengguna login |
| Semua notifikasi | `/notifikasi` | `notif:all` | Semua pengguna login |
| Pengaturan Notifikasi | `/admin/notifikasi` | `admin:notif-settings` | `Notifikasi.Pengaturan.View` (ubah: `.Edit`) |

Menu: **Administrasi → grup Notifikasi** (ikon `Alert`) → item **Pengaturan Notifikasi** (halaman, ikon `Settings`). Semua notifikasi dibuka dari panel ("Lihat semua notifikasi") dan pencarian header; bukan item menu. Tidak punya alert tab.

---

## 2. Lonceng dan Panel (header)

Spesifikasi dasar ada di Fondasi bagian 3.1. Rincian GMS:

```
 ┌ Notifikasi ─────────────────────────── [Tandai semua dibaca] ┐   panel 380px
 │ 3 belum dibaca                                               │
 │ ● PO-202611-0001 terlambat                                   │   belum dibaca: titik --brand,
 │   Baris KMJ-001 / Navy / M melewati tanggal kirim 20 Nov.    │   latar --brand-t, judul tebal
 │   5 menit lalu                                  [Terlambat]  │
 │ ● WO-202611-0004 dirilis                                     │
 │   …                                                          │
 │   Akun siti terkunci                (sudah dibaca, normal)   │
 │ ──────────────────────────────────────────────────────────── │
 │              Lihat semua notifikasi                          │
 └──────────────────────────────────────────────────────────────┘
```
- Tombol lonceng 32px; lencana di sudut kanan atas (maks `9+`, hilang bila 0); `aria-label="Notifikasi, N belum dibaca"`, `aria-expanded`.
- Panel menampilkan **20 terbaru** (AC-NT-08). Item = tombol: titik biru (belum dibaca), judul, **satu baris** keterangan (dipotong 2 baris maks), waktu relatif ("5 menit lalu", >24 jam: tanggal `14 Nov 2026`), dan badge jenis **hanya bila bermakna** memakai peta status (lihat bagian 5).
- Klik item: tandai terbaca, tutup panel, **buka/fokus tab** dokumen tujuan. Dokumen tidak tersedia: toast info "Dokumen tidak tersedia" (tab tidak dibuka); item tetap terbaca.
- "Tandai semua dibaca": `btn` kecil di kanan header; menandai seluruh notifikasi (bukan hanya 20 yang tampak) dan mengumumkan "Semua notifikasi ditandai terbaca" (`aria-live="polite"`).
- Navigasi keyboard: Enter/Spasi membuka panel; ↑ ↓ pindah item; Enter membuka item; Esc menutup dan fokus kembali ke lonceng. Satu panel header terbuka pada satu waktu (Design.md bagian 4).
- **Polling 60 detik** hanya memperbarui lencana dan, bila panel sedang terbuka, daftar tanpa menutup/mengacak posisi. Item baru yang datang saat panel terbuka tampil di atas dengan sorotan singkat (dimatikan pada `prefers-reduced-motion`).

### State panel
| State | Tampilan |
|---|---|
| Memuat | Skeleton 3 item |
| Kosong | Ikon lonceng netral, "Tidak ada notifikasi", keterangan "Peristiwa penting akan muncul di sini." Tautan "Lihat semua" tetap ada |
| Galat | `MessageBar` error (tidak dapat ditutup): "Tidak dapat memuat notifikasi." + traceId + [Coba lagi] |
| Polling gagal (panel tertutup) | Lencana mempertahankan nilai terakhir, **tanpa** pesan (FSD bagian 7) |

---

## 3. Semua Notifikasi (`notif:all`)

```
Beranda / Notifikasi
Semua notifikasi                                         [ ✓ Tandai semua dibaca ]
┌ DataList ───────────────────────────────────────────────────────────────────────┐
│ [🔍 Cari judul atau dokumen]                                                     │
│ (Semua 128) (Belum dibaca 3) (Produksi 40) (Penjualan 52) (Gudang 22)            │
│ (Penggajian 6) (Keamanan 8)                                                      │
│ ●  PO-202611-0001 terlambat           Penjualan   [Terlambat]   5 menit lalu     │
│    Baris KMJ-001 / Navy / M melewati tanggal kirim 20 Nov 2026.                  │
│    WO-202611-0004 dirilis             Produksi    [Released]    14 Nov 2026 09:42│
│ …                                                                                │
│ Menampilkan 25 dari 128                                         ‹ 1 2 3 4 5 ›    │
└──────────────────────────────────────────────────────────────────────────────────┘
```
- PageHeader: judul + **ikon-kiri** tombol `Tandai semua dibaca` (default, **bukan primary**; halaman ini tidak punya primary).
- `DataList`: pencarian wajib (judul, kode dokumen); **chip dengan jumlah**: Semua · Belum dibaca · Produksi · Penjualan · Gudang · Penggajian · Keamanan (kategori mengikuti jenis, FSD bagian 2). Kategori tanpa notifikasi tetap tampil dengan jumlah 0 dan dapat dipilih (hasilnya state kosong filter).
- Baris: titik belum dibaca, judul (tebal bila belum dibaca), keterangan 1 baris, kolom Kategori, badge jenis, waktu. Klik baris = sama seperti klik item panel. Aksi baris `⋯`: **Tandai terbaca / belum dibaca** (opsional; bila FSD tidak menyediakan "belum dibaca", hanya Tandai terbaca — UI hanya menampilkan yang tersedia di API).
- Footer "Menampilkan X dari Y" + pagination, 25 per halaman, urut terbaru. Pencarian dan filter dipertahankan per tab dan **tidak** menandai tab belum disimpan.
- State: Memuat = skeleton 8 baris; Kosong = "Belum ada notifikasi" (tanpa tombol aksi); Filter kosong = "Tidak ada notifikasi untuk filter ini" + Reset; Galat = `MessageBar` error + Coba lagi.
- Retensi: footer kecil "Notifikasi disimpan selama 90 hari." (nilai dari pengaturan).

---

## 4. Pengaturan Notifikasi (`admin:notif-settings`)

```
Administrasi / Notifikasi / Pengaturan Notifikasi
Pengaturan Notifikasi                                   (tanpa tombol aksi di header)
[i] Perubahan berlaku untuk pemeriksaan berikutnya dan tidak menghapus notifikasi yang sudah ada.   (MessageBar info)

┌ Pengaturan umum ───────────────────────────────────────────────────────────┐
│ Ambang jatuh tempo (hari)*  [ 3 ]   Rentang 1–14. Dipakai NC-02.           │
│ Jam pemeriksaan harian*     [06:00]  Zona waktu aplikasi (WIB).            │
│ Masa simpan notifikasi (hari)* [ 90 ]  Rentang 30–365.                     │
└────────────────────────────────────────────────────────────────────────────┘
┌ Jenis notifikasi ──────────────────────────────────────────────────────────┐
│ KEJADIAN (dipicu aksi pengguna)                                            │
│ ☑ NE-01  WO dirilis                      Penerima: ProductionOutput.Create │
│ ☑ NE-02  WO selesai, menunggu Close      Penerima: WorkOrder.Close         │
│ …                                                                          │
│ KONDISI TERJADWAL (dicek harian)                                           │
│ ☑ NC-01  PO terlambat                    Penerima: CustomerPO.View         │
│ …                                                                          │
└────────────────────────────────────────────────────────────────────────────┘
Catatan kiri: "Terakhir diubah 14 Nov 2026 oleh Budi"          [Batal] [Simpan]
```
- Tiga kolom tabel jenis: **Aktif** (checkbox), **Jenis** (kode mono + nama), **Penerima** (izin; teks keterangan). Pengelompokan Kejadian/Kondisi dengan baris judul seksi. Kode `NE-xx`/`NC-xx` memakai font mono. Tidak memakai komponen toggle baru (Design.md tidak mengenalnya); checkbox + label "Aktif" sudah cukup.
- Validasi: field wajib bertanda `*` merah; di luar rentang → galat inline ("Ambang jatuh tempo harus 1 sampai 14 hari."); ringkasan `MessageBar` error di atas saat Simpan gagal.
- Menonaktifkan jenis yang punya episode aktif: tidak ada dialog (tidak merusak data); `MessageBar` info kecil di baris itu: "Notifikasi yang sudah ada tetap terlihat." (FSD AC-NT-06).
- **Titik belum disimpan** pada tab bila ada perubahan; menutup tab meminta konfirmasi (Dialog 480px).
- Footer kartu aksi: catatan kiri, [Batal] [Simpan] (primary, tanpa ikon). Toast sukses "Pengaturan notifikasi disimpan."
- Konflik 409 (`rowVersion`): `MessageBar` peringatan "Pengaturan diubah pengguna lain" + [Muat ulang].
- Tanpa `Notifikasi.Pengaturan.Edit`: semua kontrol hanya baca (nonaktif + keterangan "Anda hanya dapat melihat"), footer disembunyikan. Tanpa `.View`: menu tidak dirender, URL langsung = 403.
- Audit: perubahan dicatat (AC-NT-18); tampilkan tautan "Lihat di Audit Log" bila pengguna berhak.

---

## 5. Isi Notifikasi (pola teks) dan Badge

Teks = **kunci resource + parameter** (bukan kalimat jadi). Kalimat dirakit seluruhnya per kunci (dilarang menyambung potongan, Design.md bagian 12). Judul ≤ 60 karakter, keterangan ≤ 120 karakter, kode dokumen dalam font mono.

| Kategori | Jenis | Contoh judul / keterangan (ID) | Badge pada panel/daftar |
|---|---|---|---|
| Produksi | NE-01 | **WO-202611-0004 dirilis** / KMJ-001 · PT Maju siap dikerjakan. | Released (biru `t-proc`) |
| | NE-02 | **WO-202611-0002 selesai** / Menunggu Close. | Completed (hijau `t-ok`) |
| | NE-05 | **Entri output di-void** / Sewing · Siti · 8 Nov · 75 pcs. | Void (abu `t-obs`) |
| | NC-03 | **WO-202611-0007 terlambat** / Target selesai 10 Nov 2026. | Terlambat (merah `t-err`) |
| Penjualan | NC-01 | **PO-202611-0001 terlambat** / 2 baris melewati tanggal kirim. | Terlambat (merah) |
| | NC-02 | **PO-202611-0003 jatuh tempo 17 Nov** / 1.150 pcs belum terkirim. | Jatuh tempo (oranye `t-warn`) |
| | NE-08 | **PO-202610-0004 dibatalkan** / Alokasi 40 pcs dilepas. | Cancelled (abu) |
| | NE-12 | **Alokasi PO-… dilepas** / 140 pcs karena serah terima di-void. | Void (abu) |
| | NE-07 | **SJ-202611-0005 di-void** / PO-202611-0001 kembali In Progress. | Void (abu) |
| Gudang | NE-03 | **Serah Terima FG 175 pcs** / 140 pcs ke PO-202611-0003, 35 pcs stok bebas. | – |
| | NE-06 | **Serah terima di-void** / WO-202611-0004 · 175 pcs. | Void (abu) |
| | NE-11 | **Stok habis: KMJ-001 · Navy · M** / Tersedia kini 0. | Habis (oranye `t-warn`) |
| Penggajian | NC-04 | **Periode GJ-20261102-20261108 perlu ditutup** / Tanggal selesai sudah lewat. | Open (kuning `t-prog`) |
| | NE-04 | **Periode GJ-… ditutup** / Angka final. | Closed (hijau) |
| Keamanan | NE-09 | **Akun siti terkunci** / 5 percobaan login gagal. | Terkunci (merah `t-err`) |
| | NE-10 | **Sesi budi diakhiri paksa** / Oleh admin pada 14 Nov 2026 14:30. | – |

Aturan warna mengikuti peta status Fondasi bagian 6: **merah hanya untuk keterlambatan/galat/terkunci**; Void/Cancelled **abu**. Tidak ada tarif atau rupiah di teks notifikasi.

---

## 6. State UI Ringkas

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Panel | Skeleton 3 item | "Tidak ada notifikasi" | `MessageBar` error + Coba lagi |
| Semua notifikasi | Skeleton 8 baris | "Belum ada notifikasi" / filter kosong + Reset | `MessageBar` error + Coba lagi |
| Pengaturan | Skeleton kartu | – | 403 (tanpa izin); gagal muat: `MessageBar` error + Coba lagi, Simpan nonaktif |
| Simpan pengaturan | Spinner pada tombol | – | Ringkasan `MessageBar` error + field ditandai; 409 = peringatan Muat ulang |

---

## 7. Aksesibilitas

- Lencana: bukan satu-satunya pembawa informasi; jumlah ada di `aria-label` tombol dan di ringkasan panel ("3 belum dibaca").
- Panel: `role="dialog"` berlabel "Notifikasi", fokus masuk ke item pertama, Esc menutup. Daftar `role="list"`, item tombol dengan nama lengkap ("Belum dibaca. PO-202611-0001 terlambat. Baris KMJ-001 … 5 menit lalu").
- Belum dibaca: titik **dan** teks tersembunyi "Belum dibaca" (bukan hanya warna/tebal).
- Pembaruan polling yang menambah notifikasi diumumkan pelan: "1 notifikasi baru" (`aria-live="polite"`, maksimal sekali per siklus).
- Waktu relatif memakai `<time datetime>` dengan tanggal lengkap sebagai tooltip.
- Pengaturan: label terhubung, galat via `aria-describedby`, checkbox berlabel "Aktif {nama jenis}".
- Kontras dan fokus mengikuti Fondasi bagian 11; animasi sorotan item baru mati pada `prefers-reduced-motion`.

---

## 8. Catatan untuk Frontend/Architect

- Komponen `NotificationPanel` dan `LanguageMenu` sudah ada di daftar Design.md bagian 13; tambahkan `NotificationItem` (dipakai ulang di panel dan `notif:all`).
- Polling: satu timer di shell, dijeda saat `document.hidden`, memanggil endpoint ringan jumlah belum dibaca; daftar hanya dimuat saat panel dibuka atau halaman Semua notifikasi aktif.
- Teks dirakit klien dari kunci + parameter (`IStringLocalizer`); server tidak mengirim kalimat jadi. Parameter berisi kode dokumen/angka/tanggal Invariant; format tampilan memakai culture aktif.
- Penegakan izin di server (AC-NT-13/14): UI tidak menyaring.
- Tab Manager: membuka tujuan notifikasi memakai ID tab dari `docs/Design/_Foundation-UI.md` bagian 4.1; tambahkan `notif:all` dan `admin:notif-settings`.

---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Pustaka UI: komponen buatan sendiri dengan CSS token (K-1 disetujui).

- **Komponen umum:** `AppHeader` (+ `NotificationPanel`), `TopNav`, `TabManager`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip berjumlah, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `NotificationItem`, lencana lonceng, panel 380px, form pengaturan (field angka/jam, tabel checkbox jenis).
- **Token yang dipakai:** `--brand`, `--brand-t` (item belum dibaca), `--badge-dot` (lencana), `--fg1`/`--fg2`/`--fg3`, `--s2`/`--s3`, `--bg2`/`--bg3`, `--sh16` (panel), kelas badge `t-proc`/`t-prog`/`t-ok`/`t-obs`/`t-err`/`t-warn`. Tidak ada hex/px mentah.
- **Deviasi:** tidak ada. (Checkbox dipakai sebagai pengganti toggle karena Design.md tidak mendefinisikan komponen toggle.)
