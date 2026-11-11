# Desain UI — Work Order

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/WorkOrder.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | Semua **Interactive Server** |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar WO | `/wo` | `WorkOrder.View` |
| Buat WO | `/wo/baru?po={id}` | `WorkOrder.Create` |
| Detail WO (progres) | `/wo/{id}` (tab `?tab=`) | `WorkOrder.View` |
| Ubah Draft / Qty | `/wo/{id}/ubah` | `WorkOrder.Edit` |

Dialog: Rilis, Complete, Buka Kembali (ke In Progress), Close, Reopen, Cancel, Ubah Qty.

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
| **Ubah Qty** | Grid Qty dengan batas minimum (output proses pertama) dan maksimum (Sisa Rencana) per sel |

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

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` dan `docs/Design/_Foundation-UI.md` v2.0. Nama komponen mengikuti kontrak Design.md; implementasi di `docs/Frontend/README.md`.

- **Komponen umum:** AppShell (header + MegaMenu + CommandPalette), `PageHeader`, tabel `gms-table` + `PagerBar` (DataTable), `StatusTag`, `ProblemBanner` (Alert), `EmptyState` / `ErrorState` / `SkeletonRows`, `AntDesign.Button` (satu `Primary` per area), `IGmsToast`.
- **Khusus modul:** `QtyGrid` untuk Qty per Color × Size, `ProgressMatrix` dan `StackedProgress` (progres per langkah), `ConfirmDialog` (Rilis dengan daftar periksa server, Cancel, Close dengan pratinjau sisa, Reopen), `StatusTag` WO.
- **Token yang dipakai:** `cyan-*` (Released), `primary` (bar progres normal), `danger-text` (terlambat), `fill-hover` (track progres), `space-4`.
- **Deviasi:** tidak ada.
