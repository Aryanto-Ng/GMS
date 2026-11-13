# Desain UI — Work Order

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/WorkOrder.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** |
| Render Mode | Semua **Interactive Server** |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar WO | `/wo` | `WorkOrder.View` |
| Buat WO | `/wo/baru?po={id}` | `WorkOrder.Create` |
| Detail WO (progres) | `/wo/{id}` (tab `?tab=`) | `WorkOrder.View` |
| Ubah Draft / Qty | `/wo/{id}/ubah` | `WorkOrder.Edit` |

Dialog: Rilis, Complete, Buka Kembali (ke In Progress), Close, Reopen, Cancel. **Ubah Qty = mode halaman** (lihat Revisi Fluent 2).

---

## 2. Wireframe

### 2.1 Daftar WO
```
Work Order (18)                                                          [ + Buat WO ]
[🔍 Kode WO / PO / Customer]  [Status ▾] [FG ▾] [Tanggal ▾]   Chip: [Terlambat 2] [Menunggu Close 1]
Kode WO         PO               Customer   FG        Status        Qty    Progres WO   Target
WO-202611-0004  PO-202611-0003   PT Maju    KMJ-001   ◔ In Progress 1.200  ▓▓▓░░ 62%    20 Nov
WO-202611-0002  PO-202611-0001   PT Jaya    CLN-004   ✓ Completed     500  ▓▓▓▓▓ 104%   10 Nov ⏰
```
Progres > 100% ditampilkan apa adanya (kelebihan produksi) dengan bar penuh + angka.

### 2.2 Buat WO
```
Buat Work Order
① Pilih PO*   [PO-202611-0003 · PT Maju ▾]  (hanya PO Confirmed/In Progress)
② Pilih FG*   [KMJ-001 Kemeja Wearpak ▾]  (FG yang ada pada PO)   Status FG: ⚠ Belum Siap — Tarif Sewing XL kosong
③ Qty WO per Color × Size
           S         M         L         XL
 Navy    [ 100 ]   [ 200 ]   [ 150 ]   [  50 ]
  sisa:     100       200       150       50       ← Sisa Rencana baris PO
 Orange  [     ]   [  80 ]   [  40 ]   [     ]
  sisa:      —         80        40       —
④ Jadwal  Mulai [__/__/____]  Target selesai [__/__/____]   Keterangan [________]
[Batal] [Simpan Draft]
```
- Draft boleh disimpan walau FG belum Siap Produksi; chip kesiapan tampil dengan alasan dan tautan ke Master Data.
- Qty melebihi "sisa" → galat sel. Baris "sisa" berasal dari server.
- Target selesai < mulai → galat.

### 2.3 Detail WO
```
← WO / WO-202611-0004          [Chip: In Progress]            PO-202611-0003 · PT Maju · KMJ-001
Aksi (kontekstual):  [Tandai Selesai]  [Ubah Qty]  [Serah Terima FG]   atau  [Rilis] [Ubah] [Batalkan]  atau  [Close]
┌ Ringkasan ─────────────────────────────────────────────────────────────────┐
│ Qty WO 1.200 · Progres 62% · Diserahterimakan 300 · Belum diserahterimakan 450 │
│ Mulai 08 Nov · Target 20 Nov │ Sisa di vendor 150                               │
└────────────────────────────────────────────────────────────────────────────┘
Tab: Progres │ Baris │ Tarif 🔒 │ Output │ Serah Terima │ Material │ Riwayat
```

#### Tab Progres (matriks progres)
```
                         │ Navy                     │ Orange            │ Total       │ %    │ Status
Langkah                  │ S    M    L    XL        │ S    M    L       │
1 Cutting     (Internal) │ 110  210  160  55        │ —    85   45      │ 665/ 660    │ 100% │ Selesai
2 Bordir      (Vendor)   │ Terkirim 400 · Diterima 250 · Sisa di vendor 150 (per vendor ▸)│ 250/660 │ 38% │ Berjalan
3 Sewing      (Internal) │ 70   100  60   0         │ —    40   20      │ 290/660     │ 44% │ Berjalan
4 QC          (Internal) │ 0    ...                                                      │ Belum Mulai
```
- Sel `output/Qty` dengan bar tipis. Output di atas Qty ditampilkan dengan tanda "+" (kelebihan produksi).
- Langkah Vendor: baris dua tingkat (Terkirim / Diterima / Sisa) dan tautan "per vendor" yang membuka rincian.
- Total dan persentase per langkah. Status: Belum Mulai, Berjalan, Selesai.
- Klik sel membuka riwayat output terfilter (langkah, Color, Size).

#### Tab Baris
Qty per Color × Size, rujukan baris PO, dan Sisa Rencana PO saat ini.

#### Tab Tarif 🔒 (hanya `WorkOrder.Tarif.View`; bila tidak berhak, tab tidak ditampilkan)
Snapshot routing dan tarif: baris = langkah Internal, kolom = Size, nilai Rp. Judul "Snapshot saat dirilis pada 08 Nov 2026 14:30 oleh …". Hanya baca.

#### Tab Output / Serah Terima / Material
Tabel ringkas dengan tautan ke modul sumber. Tab Serah Terima menampilkan total diserahterimakan per Color × Size dan sisa belum diserahterimakan. Tab Material menampilkan dokumen serah terima material dan total per Material.

#### Tab Riwayat
Linimasa status (dari → ke, alasan, pelaku, waktu) dan audit perubahan Qty.

### 2.4 Dialog
| Dialog | Isi |
|---|---|
| **Rilis** | Daftar periksa: ✓ PO aktif · ✓ FG Siap Produksi · ✓ Qty ≤ Sisa Rencana. Ringkasan snapshot (routing + jumlah tarif). Peringatan: "Routing dan tarif dibekukan dan tidak dapat diubah." Tombol [Rilis WO]. Bila ada yang gagal: butir ✗ dengan alasan dan tautan, tombol nonaktif |
| **Tandai Selesai** | Ringkasan progres per langkah, peringatan bila ada sisa di vendor atau langkah belum tercapai. "Input output akan diblokir." [Tandai Selesai] |
| **Buka Kembali (Completed → In Progress)** | Alasan wajib |
| **Close** | Ringkasan: Dalam Produksi menjadi 0, sisa kebutuhan kembali ke PO (n pcs). Peringatan tak memblokir bila ada output belum diserahterimakan (per Color × Size). [Close WO] |
| **Reopen (Closed → Completed)** | Alasan wajib. Bila Sisa Rencana PO tidak cukup: pesan penahan + tombol nonaktif |
| **Cancel** | Alasan wajib. Hanya muncul bila belum ada aktivitas |
| ~~Ubah Qty~~ | **Dipindah ke mode halaman** (`/wo/{id}/ubah`): grid Qty dengan batas minimum (output proses pertama) dan maksimum (Sisa Rencana) per sel |

---

## 3. Aturan Tampilan Aksi per Status

| Status | Aksi terlihat |
|---|---|
| Draft | Ubah, Rilis, Batalkan |
| Released | Ubah Qty, Batalkan (selama belum ada aktivitas) |
| In Progress | Ubah Qty, Tandai Selesai, Serah Terima FG |
| Completed | Buka Kembali (Konveksi), Close (Comfi), Serah Terima FG |
| Closed | Reopen (Comfi) |
| Cancelled | Tidak ada |

Aksi hanya tampil bagi pengguna yang punya permission dan status yang sesuai.

---

## 4. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Daftar WO | Skeleton 8 baris | "Belum ada Work Order" + [Buat WO] | Banner + Coba lagi |
| Buat WO | Skeleton grid setelah pilih FG | PO tanpa FG tersisa: "Semua kebutuhan PO sudah direncanakan" | Gagal memuat Sisa Rencana: banner |
| Detail WO | Skeleton ringkasan + matriks | Matriks sebelum ada output: sel "0", status "Belum Mulai" dan teks "Belum ada output" | 404 "WO tidak ditemukan" |
| Tab Tarif | Skeleton | – | 403 untuk pengguna tanpa hak: tab tidak ada |
| Rilis gagal | Spinner pada tombol | – | Dialog menampilkan butir yang gagal |
| 409 | Banner "Status WO telah berubah" + Muat ulang | | |

---

## 5. Aksesibilitas

- Matriks progres: `role="table"`, header kolom terkelompok (Color → Size) dengan `scope`, nilai sel dibaca "Sewing, Navy M: 100 dari 200".
- Status langkah memakai ikon + teks. Bar tipis tidak membawa informasi unik.
- Dialog alasan: fokus pada kolom Alasan, tombol bahaya jelas diberi label akibat ("Close WO").
- Daftar periksa Rilis: ikon ✓/✗ + teks, dibacakan sebagai "Terpenuhi"/"Belum terpenuhi".

---

## 6. Catatan untuk Frontend

- Komponen **Matriks Progres** dan **Dialog Alasan + Pratinjau Dampak** dari Fondasi.
- Dialog Rilis memanggil pemeriksaan dari server (bukan logika di klien).
- Tab Tarif dan data tarif tidak boleh dikirim ke klien tanpa `WorkOrder.Tarif.View`.

---

## Revisi Fluent 2 (v3.0, 2026-11-13)

Bagian ini **menggantikan** wireframe, pola, dan istilah lama yang bertentangan. Acuan: `docs/Design.md` v2.0 (Fluent 2) dan `docs/Design/_Foundation-UI.md` v3.0. Mock-up visual: `docs/Design/mockup/index.html`.

> Glyph pada wireframe di dokumen ini (⏰ ⚠ ✓ ◔ ⛔ 🔒 ⋯ ▾) hanyalah **notasi**. UI memakai ikon Fluent 16px + teks, tanpa emoji (Design.md bagian 12).

### Tab Manager
`wo:list` · `wo:{WO-…}` (detail dan Ubah berbagi tab) · `wo:baru`. Alert tab: **merah** bila WO terlambat; **oranye** pada WO Draft dengan FG Belum Siap. Titik belum disimpan pada form Buat/Ubah (termasuk mode Ubah Qty).

### Status dan Stepper
| Status | Badge |
|---|---|
| Draft, Released | biru `t-proc` |
| In Progress | kuning `t-prog` |
| Completed, Closed | hijau `t-ok` |
| Cancelled | abu `t-obs` |
| Terlambat | merah `t-err` |
| Langkah matriks: Belum Mulai / Berjalan / Selesai | biru / kuning / hijau |
| FG Belum Siap / Siap Produksi | oranye / hijau |

`Stepper`: Draft → Released → In Progress → Completed → Closed (status akhir hijau). Cancelled: badge abu, stepper disembunyikan.

### Daftar WO
Primary `⊕ Buat WO`. Pencarian (kode WO / PO / customer), dropdown Status/FG/Tanggal, chip dengan jumlah: **Terlambat · Menunggu Close** (`terlambat`, `menungguClose`), footer "Menampilkan X dari Y". Progres: bar **kuning** (berjalan), **hijau** (≥ 100%), **merah** (terlambat); > 100% tampil apa adanya (bar penuh + angka + "+").

### Detail WO — aksi header (satu primary)
| Status | Tombol (ikon kiri) | Primary |
|---|---|---|
| Draft | Ubah · Batalkan (bahaya) · Rilis | **Rilis** |
| Released | Ubah Qty · Batalkan (bahaya, selama belum ada aktivitas) | – |
| In Progress | Ubah Qty · Tandai Selesai · Serah Terima FG | **Serah Terima FG** |
| Completed | Buka Kembali (Konveksi) · Serah Terima FG · Close (Comfi) | **Close** bagi Comfi, **Serah Terima FG** bagi Konveksi |
| Closed | Reopen (Comfi) | – |
| Cancelled | – | – |

Ringkasan = `StatTile` (Qty WO, Progres %, Diserahterimakan, Belum diserahterimakan, Sisa di vendor). Inner tabs: Progres · Baris · Tarif (tidak dirender bila tanpa `WorkOrder.Tarif.View`) · Output · Serah Terima · Material · Riwayat.

### Matriks Progres
`ProgressMatrix`: sel `output/Qty` + bar tipis (kuning/hijau), langkah Vendor = tiga angka (Terkirim · Diterima · Sisa). Kolom pertama dan header lengket; **header dua tingkat Color → Size** (jangan memberi `position: sticky` pada kedua baris sekaligus sehingga baris Color tertutup). Status langkah = badge teks.

### Dialog (semua 480px)
Rilis (daftar periksa ✓/✗ dari server + ringkasan snapshot), Tandai Selesai (ringkasan progres per langkah sebagai **daftar**, bukan tabel lebar), Close (pratinjau sisa kebutuhan), Reopen/Buka Kembali/Cancel (alasan wajib, tombol bahaya).

**Perubahan:** **Ubah Qty bukan dialog lagi.** Grid Qty dengan batas min/maks per sel tidak muat di 480px (usulan U-1, Fondasi bagian 12), sehingga Ubah Qty = **mode Ubah di tab WO** (`/wo/{id}/ubah`), footer [Batal] [Simpan Perubahan].


---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Nama komponen mengikuti kontrak Design.md bagian 13 (pemetaan dari nama lama: Fondasi bagian 8). Pustaka UI ditentukan oleh keputusan **K-1** (Fondasi bagian 12); spesifikasi ini tidak bergantung pada pustaka.

- **Komponen umum:** `AppHeader` (pencarian, bahasa, pengguna), `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `Stepper`, `ProgressMatrix`, `StackedProgress`, `QtyGrid` (Buat WO dan Ubah Qty), `StatTile`, `Dialog` (Rilis dengan daftar periksa server, Tandai Selesai, Close, Reopen, Cancel), `StatusBadge` WO.
- **Token yang dipakai:** `--brand`, `--brand-t`, `--s3` (track progres), warna bar progres dari variabel chart (kuning berjalan, hijau selesai, merah terlambat); badge `t-proc`/`t-prog`/`t-ok`/`t-obs`/`t-err`/`t-warn`. Tidak ada hex/px mentah.
- **Deviasi:** Ubah Qty dari dialog menjadi mode halaman (akibat U-1). Tidak ada deviasi lain.
