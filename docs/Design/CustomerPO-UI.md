# Desain UI — Customer PO

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/CustomerPO.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | Semua **Interactive Server** |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar PO | `/po` | `CustomerPO.View` |
| Buat PO | `/po/baru` | `CustomerPO.Create` |
| Detail PO | `/po/{id}` (tab di query `?tab=`) | `CustomerPO.View` |
| Ubah PO (header dan baris) | `/po/{id}/ubah` | `CustomerPO.Edit` |

Dialog: Konfirmasi PO, Batalkan PO, Alokasi Stok, Lepas Alokasi.

---

## 2. Wireframe

### 2.1 Daftar PO
```
Customer PO (42)                                                        [ + Buat PO ]
[🔍 Kode / No. PO customer / Customer]  [Status ▾]  [Customer ▾]  [Tanggal ▾]
Chip cepat: [Terlambat 3] [Jatuh tempo 7 hari 5] [PO Customer belum ada 2] [Sisa rencana > 0 4]
Kode PO           No. PO Customer   Customer     Tgl PO      Status          Progres (pcs)                 
PO-202611-0003    Belum ada ⚠       PT Maju      06 Nov      ◔ Confirmed     ▓▓░░░░░░  Alok 100 · Prod 300 · Kirim 0 / 1.200
PO-202611-0001    PO/25/118         PT Jaya      02 Nov      ◔ In Progress   ▓▓▓▓▓░░░  Alok 0 · Prod 200 · Kirim 600 / 1.000   ⏰ Terlambat
```
- Chip cepat = filter siap pakai (juga dipakai Beranda). Angka pada chip dihitung server.
- "Belum ada" ditampilkan sebagai tag peringatan (ikon + teks).
- Progres: bar bertumpuk Terkirim / Dialokasikan / Dalam Produksi dengan legenda dan angka teks (tidak bergantung warna).
- Baris Cancelled berlatar netral, nomor dicoret ringan.

### 2.2 Buat/Ubah PO
```
Buat PO                                                              Kode: otomatis (PO-202611-NNNN)
┌ Header ───────────────────────────────────────────────┐
│ Customer*      [PT Maju ▾]      Tanggal PO* [08/11/2026]│
│ No. PO Customer [__________]  ⓘ Boleh dikosongkan, isi saat PO customer terbit│
│ Keterangan     [______________________________]       │
└────────────────────────────────────────────────────────┘
┌ Kebutuhan ────────────────────────────────────────────┐
│ [ + Tambah blok FG ]                                   │
│ ▾ KMJ-001 Kemeja Wearpak · Navy · Kirim [20/11/2026]  ✕│
│            S     M     L     XL    │ Total             │
│   Qty   [100]  [200]  [150]  [50 ] │  500              │
│ ▾ KMJ-001 Kemeja Wearpak · Orange · Kirim [27/11/2026] ✕│
│            S     M     L     XL                         │
│   Qty   [   ]  [ 80]  [ 40]  [   ] │  120              │
│ Total keseluruhan: 620 pcs                             │
└────────────────────────────────────────────────────────┘
[Batal] [Simpan sebagai Draft]  [Simpan dan Konfirmasi]
```
- Blok = FG + Color + Tanggal Kirim, berisi grid satu baris Qty per Size. Hanya Size Berlaku FG yang aktif; Size lain "—".
- Kombinasi FG + Color + Size + Tanggal Kirim ganda ditolak dengan sorotan blok.
- Qty > 100.000 meminta konfirmasi, > 1.000.000 ditolak.
- Tanggal Kirim lebih awal dari Tanggal PO: peringatan kuning.
- **Mode ubah (Confirmed/In Progress):** Qty yang tidak boleh turun menampilkan batas minimum di bawah sel ("min 120: Terkirim 50 + Alokasi 20 + Produksi 50"). FG/Color/Size baris yang sudah terpakai terkunci (ikon gembok). Hapus baris hanya bila semua metrik nol.

### 2.3 Detail PO
```
← PO / PO-202611-0001            No. PO Customer: PO/25/118  [✎]           [Chip In Progress]
Customer PT Jaya · Tgl PO 02 Nov 2026                  [Ubah] [Alokasi Stok] [Buat WO] [Batalkan ⋯]
┌ Ringkasan ─────────────────────────────────────────────────────────────────────┐
│ Total 1.000   Terkirim 600   Dialokasikan 0   Dalam Produksi 200   Sisa Rencana 200 │
└─────────────────────────────────────────────────────────────────────────────────┘
Tab: Baris │ Alokasi │ Work Order (2) │ Pengiriman (1) │ Riwayat
 FG        Color  Size  Kirim       Qty   Terkirim  Dialok.  Produksi  Sisa Rencana
 KMJ-001   Navy   M     20 Nov ⏰    200    150       0        50        0
 KMJ-001   Navy   L     20 Nov      150     50       0       100       0
 ...
```
- "No. PO Customer" dapat diisi/diubah langsung (ikon pensil) sampai Completed.
- Tombol aksi kontekstual: Konfirmasi (Draft), Alokasi Stok dan Buat WO (Confirmed/In Progress), Batalkan (sesuai syarat). Tombol yang tidak diizinkan oleh status disembunyikan; bila diizinkan tetapi ditolak aturan, dinonaktifkan dengan tooltip alasan.
- Baris terlambat ditandai ⏰ + teks "Terlambat".
- Tab **Alokasi:** riwayat alokasi/pelepasan (waktu, baris, qty ±, sumber: Manual/Serah Terima/Pembatalan/Pengiriman, pelaku). Aksi Alokasi dan Lepas per baris.
- Tab **Work Order / Pengiriman:** tabel ringkas dengan tautan ke modul masing-masing.
- Tab **Riwayat:** audit PO dan riwayat status.

### 2.4 Dialog Alokasi Stok
```
Alokasikan stok — KMJ-001 · Navy · M (PO-202611-0001)
Kebutuhan 200 · Terkirim 150 · Dialokasikan 0 · Dalam Produksi 50 · Sisa Rencana 0   ← batas alokasi = min(tersedia, sisa rencana)
Stok: Fisik 120 · Teralokasi (semua PO) 30 · Tersedia 90
Qty alokasi [ 0 ]  (maks 0 — Sisa Rencana habis)
[Batal] [Alokasikan]
```
Bila batas 0 karena Sisa Rencana habis atau stok tidak tersedia, pesan menjelaskan penyebab. Dialog ini bisa dibuka dari layar Pengiriman (aksi "Alokasikan stok").

### 2.5 Dialog Konfirmasi dan Batalkan
- **Konfirmasi PO:** ringkasan jumlah baris dan total pcs, catatan bahwa PO customer boleh menyusul.
- **Batalkan PO:** alasan wajib. Menampilkan dampak: "Alokasi 40 pcs akan dilepas." Bila ditolak (ada pengiriman/WO aktif): daftar penahan dengan tautan.

---

## 3. Interaksi dan Pesan Penting

| Situasi | Pesan |
|---|---|
| Turunkan Qty di bawah batas | "Qty minimal 120 (Terkirim 50 + Dialokasikan 20 + Dalam Produksi 50). Lepas alokasi atau ubah WO terlebih dahulu." + tautan |
| Nomor PO customer duplikat | "Nomor PO ini sudah dipakai PO-202610-0007 untuk customer yang sama." |
| Alokasi bersamaan (konkurensi) | "Stok tersedia berubah menjadi 40. Sesuaikan jumlah." Nilai di layar diperbarui. |
| PO Completed | Semua tombol ubah disembunyikan. Banner informasi "PO selesai. Tambahan pesanan dibuat pada PO baru." |
| Void pengiriman menghidupkan PO | Toast di halaman PO (saat dibuka): status kembali In Progress. |

---

## 4. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Daftar PO | Skeleton 8 baris + chip cepat berupa placeholder | "Belum ada PO" + [Buat PO] | Banner + Coba lagi |
| Filter kosong | – | "Tidak ada PO untuk filter ini" + Reset | – |
| Detail PO | Skeleton ringkasan + tabel | Tab Alokasi kosong: "Belum ada alokasi stok". Tab WO kosong: "Belum ada Work Order" + [Buat WO] | 404 "PO tidak ditemukan" |
| Dialog Alokasi | Skeleton angka stok | – | Gagal muat stok: pesan + Coba lagi, tombol Alokasikan nonaktif |
| Simpan gagal | Spinner tombol | – | Ringkasan galat di atas, blok/sel bermasalah ditandai |
| Konflik 409 | Banner "PO telah diubah pengguna lain" + [Muat ulang] | | |

---

## 5. Aksesibilitas

- Grid Qty per Size memakai pola Grid Matriks (label sel "KMJ-001 Navy, size M, qty").
- Bar progres selalu disertai angka teks dan `aria-label` ("Terkirim 600 dari 1.000").
- Chip cepat adalah tombol toggle (`aria-pressed`) dengan jumlah di label.
- Tag "Belum ada" dan "Terlambat" berupa ikon + teks.
- Dialog alasan wajib: fokus awal pada kolom Alasan, pesan galat terkait melalui `aria-describedby`.

---

## 6. Catatan untuk Frontend

- Komponen **Kartu Ringkasan Metrik** dan **Bar Progres Bertumpuk** dipakai ulang di Beranda dan Detail WO.
- Seluruh angka metrik berasal dari server (jangan dihitung ulang di klien).
- Filter chip cepat memakai parameter query bersama dengan Beranda: `terlambat`, `jatuhTempo7`, `poCustomerKosong`, `sisaRencana`.

---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` dan `docs/Design/_Foundation-UI.md` v2.0. Nama komponen mengikuti kontrak Design.md; implementasi di `docs/Frontend/README.md`.

- **Komponen umum:** AppShell (header + MegaMenu + CommandPalette), `PageHeader`, tabel `gms-table` + `PagerBar` (DataTable), `StatusTag`, `ProblemBanner` (Alert), `EmptyState` / `ErrorState` / `SkeletonRows`, `AntDesign.Button` (satu `Primary` per area), `IGmsToast`.
- **Khusus modul:** `QtyGrid`/tabel baris PO pada form, `StackedProgress` (progres kirim/alokasi/produksi), chip cepat (`gms-chipbtn`, filter toggle dengan `aria-pressed`), tab detail, `ConfirmDialog` untuk Cancel/Reopen/Confirm, `StatusTag` PO (Draft/Confirmed/In Progress/Completed/Cancelled) dan `late`/`dueSoon`.
- **Token yang dipakai:** `cyan-*` (Confirmed), `primary-bg` (In Progress), `success-*`, `danger-*`, `warning-*` (Belum ada No. PO), `fill-alter`, `space-6`.
- **Deviasi:** tidak ada.
