# Desain UI — Serah Terima & Stok FG

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/SerahTerimaStokFG.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | Semua **Interactive Server** |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar Serah Terima FG | `/gudang/serah-terima` | `SerahTerimaFG.View` |
| Buat Serah Terima FG | `/gudang/serah-terima/baru?wo={id}` | `SerahTerimaFG.Create` |
| Detail Serah Terima FG | `/gudang/serah-terima/{id}` | `SerahTerimaFG.View` |
| Stok FG | `/gudang/stok` | `StokFG.View` |
| Kartu Stok | `/gudang/stok/kartu?fg=&color=&size=` | `StokFG.View` |
| Import Saldo Awal | `/gudang/stok/import` | `StokFG.Import` |
| Daftar Serah Terima Material | `/gudang/material` | `SerahTerimaMaterial.View` |
| Buat Serah Terima Material | `/gudang/material/baru` | `SerahTerimaMaterial.Create` |

Dialog: Void, Koreksi, Penyesuaian Stok.

---

## 2. Serah Terima FG

### 2.1 Buat
```
Buat Serah Terima FG
WO* [WO-202611-0004 · KMJ-001 ▾] (In Progress/Completed)   Tanggal* [08/11/2026]
No. Dokumen [_______]   Diterima oleh [_______] (teks bebas)   Catatan [_______]

Qty diserahterimakan:
                  Navy                       Orange
                  S     M     L     XL       S     M     L
Dapat diserahkan  60    80    40    20       —     35    15      ← Output langkah terakhir − sudah diserahterimakan
Qty              [60]  [80]  [  ]  [  ]      [  ]  [35]  [  ]
─────────────────────────────────────────────────────────────
Perkiraan alokasi otomatis ke PO-202611-0003: 140 pcs · Stok bebas (kelebihan produksi): 35 pcs
[Batal] [Simpan]
```
- Panel "Perkiraan alokasi otomatis" dihitung server sebelum simpan (sesuai FSD 2.3), memberi gambaran awal. Setelah simpan, ringkasan hasil sebenarnya ditampilkan.
- Melebihi "Dapat diserahkan" → galat sel (merah + ikon + pesan).
- Tanggal masa depan ditolak. Tanggal lebih awal dari output pertama WO: peringatan kuning.
- Setelah simpan: halaman detail dengan banner sukses: "140 pcs dialokasikan ke PO-…; 35 pcs menjadi stok bebas."

### 2.2 Daftar dan Detail
Daftar: Tanggal, No. Dokumen, WO, PO, FG, Total pcs, Status (Aktif/Void), Pembuat. Filter: WO, PO, FG, tanggal, status. Detail: header, tabel qty per Color × Size, hasil alokasi otomatis (per baris PO), rujukan antar-dokumen (Koreksi/Void), jejak. Aksi: **Void**, **Koreksi** (sesuai permission/syarat).

### 2.3 Dialog Void/Koreksi dengan pratinjau dampak
```
Void serah terima 08 Nov (WO-…0004) — 175 pcs
Dampak stok:
 FG · Color · Size   Fisik sebelum→sesudah   Teralokasi   Tersedia sesudah   Status
 KMJ-001 · Navy · M       120 → 40            30          10                 ✓
 KMJ-001 · Navy · S        60 → 0             60          −0 → 0             ✓
 KMJ-001 · Orange · M      35 → 0              0          0                  ✓
Alokasi otomatis yang akan dilepas: PO-202611-0003 · 140 pcs
Alasan* [______]
[Batal] [Void]       ← nonaktif bila ada ✗ ("Stok sudah dialokasikan ke PO lain / sudah dikirim")
```
Pesan penahan memberi langkah: "Batalkan pengiriman SJ-… terlebih dahulu" dengan tautan.

---

## 3. Stok FG

```
Stok FG                                                           [Penyesuaian] [Import Saldo Awal]
[🔍 FG]  [Color ▾] [Size ▾] [Customer ▾]  ☑ Hanya yang ada stok
Ringkasan: Fisik 2.140 · Teralokasi 640 · Tersedia 1.500   (stok bebas)
FG        Color    Size   Fisik   Teralokasi   Tersedia
KMJ-001   Navy     M       120     30           90      ⋯ (Kartu Stok · Penyesuaian)
KMJ-001   Navy     L        60     60            0
```
- Baris Tersedia = 0 tetap terlihat jika Fisik > 0 (ikon info "Seluruhnya dialokasikan").
- Pengelompokan opsional per FG (baris induk yang dapat dilipat, total per FG).

### 3.1 Kartu Stok
Header: FG · Color · Size, saldo saat ini (Fisik/Teralokasi/Tersedia). Tabel: Tanggal, Jenis (Penerimaan, Pembalikan Serah Terima, Pengiriman, Pembalikan Pengiriman, Penyesuaian, Saldo Awal), Dokumen sumber (tautan), Qty (±, minus berwarna + tanda), **Saldo berjalan**. Filter rentang tanggal. Paging di server.

### 3.2 Dialog Penyesuaian Stok
```
Penyesuaian stok
FG* [...]  Color* [...]  Size* [...]  (hanya Size Berlaku FG)
Stok saat ini: Fisik 120 · Teralokasi 30 · Tersedia 90
Qty (±)* [ -10 ]   Tanggal* [08/11/2026]   Alasan* [__________]
Hasil: Fisik 110 · Tersedia 80  ✓
[Batal] [Simpan]   ← nonaktif bila Tersedia < 0: "Lepas alokasi terlebih dahulu"
```

### 3.3 Import Saldo Awal
Wizard 3 langkah sama dengan Master Data (Unggah → Periksa → Konfirmasi). Template: Kode FG, Kode Color, Kode Size, Qty, Tanggal (opsional). Peringatan kuning bila kombinasi sudah memiliki Saldo Awal sebelumnya (AC edge E10).

---

## 4. Serah Terima Material

```
Buat Serah Terima Material
WO* [WO-… (Draft…Completed) ▾]   Tanggal* [..]   No. Dokumen [..]   Catatan [..]
Baris:
 Material*                         Qty*         Satuan   Keterangan
 [KN-001 Kain Drill Navy ▾]        [ 120,500 ]  MTR      [        ]  ✕
 [BN-004 Benang Hitam ▾]           [ 12      ]  CONE    [        ]  ✕
 [ + Tambah material ]
[Batal] [Simpan]
```
- Qty desimal format Indonesia maksimal 3 digit. Lebih ditolak.
- Satuan otomatis dari master Material (tidak dapat diubah).
- Daftar: Tanggal, No. Dokumen, WO, Jumlah item, Status. Detail dengan Void (alasan wajib).
- Catatan UI: "Ini hanya pencatatan; tidak memengaruhi stok."

---

## 5. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Buat Serah Terima FG | Skeleton grid setelah pilih WO | WO belum punya output: "Belum ada output proses terakhir yang dapat diserahterimakan" | Gagal muat batas: banner |
| Daftar serah terima | Skeleton 8 baris | "Belum ada serah terima" + [Buat] | Banner + Coba lagi |
| Stok FG | Skeleton tabel + ringkasan | "Belum ada stok" + [Import Saldo Awal] bila berhak | idem |
| Kartu Stok | Skeleton | "Belum ada mutasi pada rentang ini" | idem |
| Void/Koreksi | Skeleton pratinjau | – | Pesan penahan dengan tautan |
| Konkurensi (409) | Banner "Stok berubah. Muat ulang" | | |

---

## 6. Aksesibilitas

- Grid input dan stok: nama sel menyebut FG, Color, Size. Angka negatif dibaca "minus".
- Kolom Qty (±) pada Kartu Stok memakai tanda + / − dan warna, tidak hanya warna.
- Dialog pratinjau dampak: tabel dengan `caption`, status ✓/✗ bertulis ("Terpenuhi"/"Tertahan").
- Combobox Material dan FG memakai pola ARIA combobox.

---

## 7. Catatan untuk Frontend

- Perkiraan alokasi otomatis dan pratinjau dampak adalah **hasil hitung server**.
- Pakai ulang Grid Matriks Ukuran (baris "Dapat diserahkan") dan wizard Import dari Master Data.

---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` dan `docs/Design/_Foundation-UI.md` v2.0. Nama komponen mengikuti kontrak Design.md; implementasi di `docs/Frontend/README.md`.

- **Komponen umum:** AppShell (header + MegaMenu + CommandPalette), `PageHeader`, tabel `gms-table` + `PagerBar` (DataTable), `StatusTag`, `ProblemBanner` (Alert), `EmptyState` / `ErrorState` / `SkeletonRows`, `AntDesign.Button` (satu `Primary` per area), `IGmsToast`.
- **Khusus modul:** `QtyGrid` (qty serah terima dan alokasi), `ConfirmDialog` + `ImpactPreviewTable` (Void, penyesuaian stok), tabel Stok FG dan Kartu Stok (kolom qty rata kanan, negatif bertanda `danger-text`), `ImportWizard` saldo awal, `StatusTag` `voided`.
- **Token yang dipakai:** `danger-text` (qty negatif), `fill-alter` (baris total), `border-secondary`, `space-3`.
- **Deviasi:** tidak ada.
