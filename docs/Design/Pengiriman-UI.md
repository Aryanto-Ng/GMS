# Desain UI — Pengiriman ke Customer

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/Pengiriman.md` v1.1 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** |
| Render Mode | Semua **Interactive Server**; PDF melalui endpoint unduhan/pratinjau |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar Surat Jalan | `/pengiriman` | `Pengiriman.View` |
| Buat Surat Jalan | `/pengiriman/baru?po={id}` | `Pengiriman.Create` |
| Detail Surat Jalan | `/pengiriman/{id}` | `Pengiriman.View` |
| Cetak PDF | `/pengiriman/{id}/cetak` (endpoint berkas) | `Pengiriman.Print` |

Dialog: Void, Edit Informasi, Alokasi Stok (dari modul PO). **Koreksi = tab halaman** (lihat Revisi Fluent 2).

---

## 2. Wireframe

### 2.1 Daftar
```
Pengiriman (27)                                                           [ + Buat Surat Jalan ]
[🔍 Kode SJ / PO / Customer]  [Customer ▾] [Tanggal ▾] [Status: Aktif ▾]
Kode SJ            Tgl Kirim   PO                No. PO Customer   Customer   Total pcs   Status   Aksi
SJ-202611-0005     08 Nov      PO-202611-0001    PO/25/118         PT Jaya     300        Aktif    ⋯ (Cetak · Detail)
SJ-202611-0003     05 Nov      PO-202610-0007    PO/25/101         PT Maju     ~~120~~    Void     ⋯
```

### 2.2 Buat Surat Jalan
```
Buat Surat Jalan                                              Kode: otomatis (SJ-202611-NNNN)
PO*  [PO-202611-0001 · PT Jaya ▾] (In Progress/Confirmed)     No. PO Customer: PO/25/118
Tanggal Kirim* [08/11/2026]  Penerima [__________]  Kendaraan/No. Polisi [B 1234 XY]  Sopir [______]
Catatan [_________________________________]

Baris yang dikirim (hanya baris yang masih ada kebutuhan)
FG        Color  Size  Tgl Kirim PO   Qty PO  Terkirim  Dialokasikan  Maks kirim   Qty kirim
KMJ-001   Navy   M     20 Nov           200     150        50            50        [ 50 ]
KMJ-001   Navy   L     20 Nov           150      50        20            20        [ 20 ]
KMJ-001   Orange M     27 Nov            80       0         0             0        [ — ]  [Alokasikan stok]
Total kirim: 70 pcs
[Batal] [Simpan Surat Jalan]
```
- **Maks kirim** = `min(Dialokasikan, Qty − Terkirim)` dari server. Input di atas maks → galat baris dengan pesan.
- Bila `Dialokasikan` = 0 dan ada stok tersedia: tombol **[Alokasikan stok]** membuka dialog alokasi (dari modul PO) tanpa meninggalkan halaman. Setelah berhasil, baris diperbarui. Tanpa hak alokasi: teks petunjuk "Minta Comfi mengalokasikan stok".
- Bila `Dialokasikan` = 0 dan stok tidak tersedia: "Belum ada stok untuk dikirim. Produksi atau serah terima dahulu."
- Pilihan PO membatasi daftar pada PO yang memiliki kebutuhan tersisa. PO Completed/Cancelled/Draft tidak muncul.
- Tanggal masa depan ditolak. Tanggal kirim sebelum Tanggal PO: peringatan kuning.
- Total kirim 0 → tombol Simpan menampilkan galat "Isi minimal satu baris".

### 2.3 Detail Surat Jalan
```
← Pengiriman / SJ-202611-0005            [Chip: Aktif]            [Cetak PDF] [Edit Info] [Koreksi] [Void]
PO-202611-0001 · PT Jaya (PO/25/118)   Tgl Kirim 08 Nov 2026
Penerima: …   Kendaraan: B 1234 XY   Sopir: …   Catatan: …
FG       Color  Size  Qty
KMJ-001  Navy   M     50
KMJ-001  Navy   L     20
Total 70
Riwayat: dibuat oleh … · rujukan "menggantikan SJ-…" bila hasil Koreksi · alasan Void bila ada
```
- **Edit Info:** hanya tanggal, penerima, kendaraan, sopir, dan catatan (dialog kecil). Qty/baris tidak dapat diubah.
- **Void/Koreksi:** dialog dengan pratinjau dampak.

### 2.4 Dialog Void/Koreksi
```
Void SJ-202611-0005 (70 pcs)
Dampak:
 Stok fisik          +70 pcs dikembalikan (KMJ-001 Navy M +50, L +20)
 Alokasi PO          Dialokasikan +70 (kembali aktif)
 Terkirim PO         −70 → PO-202611-0001: 600 → 530
 Status PO           Completed → In Progress  (bila berlaku)
Alasan* [_______________]
[Batal] [Void surat jalan]
```
Koreksi: grid baris sama seperti Buat, terisi nilai asal, batas dihitung pada kondisi akhir, alasan wajib. Tombol utama nonaktif bila melanggar batas.

---

## 3. Dokumen PDF — Surat Jalan (A4 portrait)

```
┌──────────────────────────────────────────────────────────────┐
│ [Nama Comfi]                              SURAT JALAN         │
│ [Alamat Comfi]                            No: SJ-202611-0005  │
│                                           Tanggal: 08 Nov 2026│
│ Kepada: PT Jaya                           [CETAK ULANG]       │
│ [Alamat customer]                                             │
│ PO Internal: PO-202611-0001   No. PO Customer: PO/25/118      │
│ Penerima: …   Kendaraan: B 1234 XY   Sopir: …                 │
│                                                               │
│ No  Barang (FG / Color)        S   M   L  XL  2XL  │  Total   │
│ 1   KMJ-001 Kemeja · Navy      –  50  20   –   –   │   70     │
│ 2   KMJ-001 Kemeja · Orange    …                    │  …       │
│                                          Total      │  70      │
│ Catatan: …                                                    │
│                                                               │
│  Pengirim            Sopir                Penerima             │
│  (               )   (               )    (               )    │
│                                                               │
│ Dicetak oleh [pengguna] pada 08 Nov 2026 14:30                │
└──────────────────────────────────────────────────────────────┘
```
- Kolom Size mengikuti Urutan Tampil. Hanya Size yang dipakai pada surat jalan ini yang ditampilkan.
- Watermark teks diagonal: **DIBATALKAN** (Void) atau penanda **CETAK ULANG** di kanan atas (cetakan ke-2 dst.).
- Matriks yang terlalu lebar dibagi menjadi blok, header Size diulang. Tabel panjang berlanjut ke halaman berikutnya dengan header diulang dan total di akhir.
- Cetak hitam-putih tetap terbaca (tanpa bergantung warna).
- Pratinjau: tombol Cetak membuka PDF di tab baru (atau mengunduh); peringatan bila alamat customer/kop Comfi kosong.

---

## 4. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Daftar | Skeleton 8 baris | "Belum ada surat jalan" + [Buat Surat Jalan] | Banner + Coba lagi |
| Buat | Skeleton tabel setelah pilih PO | PO tanpa kebutuhan tersisa: "Semua kebutuhan PO sudah terkirim" | Gagal muat alokasi: banner + Coba lagi |
| Detail | Skeleton | – | 404 "Surat jalan tidak ditemukan" |
| Simpan | Spinner pada tombol | – | Galat batas per baris + ringkasan. Konflik alokasi: banner "Alokasi berubah (maks 30)". |
| Cetak PDF | Spinner "Menyiapkan dokumen…" | – | "Gagal membuat PDF" + Coba lagi + traceId |

---

## 5. Aksesibilitas

- Tabel baris kirim: tiap input diberi label "Qty kirim KMJ-001 Navy M, maksimum 50".
- Dialog dampak memakai tabel dengan `caption`. Status Completed → In Progress dibacakan jelas.
- Tombol Cetak PDF menyebutkan "membuka berkas PDF di tab baru".
- Dokumen PDF: tagged PDF/teks nyata (tidak gambar) sedapat mungkin, sehingga dapat dibaca/disalin.

---

## 6. Catatan untuk Frontend

- Satu komponen pembuat PDF bersama untuk Surat Jalan dan Slip Gaji (kop, font, gaya tabel).
- Dialog Alokasi dipakai ulang dari modul Customer PO.

---

## Revisi Fluent 2 (v3.0, 2026-11-13)

Bagian ini **menggantikan** wireframe, pola, dan istilah lama yang bertentangan. Acuan: `docs/Design.md` v2.0 (Fluent 2) dan `docs/Design/_Foundation-UI.md` v3.0. Mock-up visual: `docs/Design/mockup/index.html`.

> Glyph pada wireframe di dokumen ini (⏰ ⚠ ✓ ◔ ⛔ 🔒 ⋯ ▾) hanyalah **notasi**. UI memakai ikon Fluent 16px + teks, tanpa emoji (Design.md bagian 12).

### Tab Manager
`sj:list` · `sj:{SJ-…}` (detail dan Edit Info berbagi tab) · `sj:baru` · `sj:koreksi:{SJ-…}`. Titik belum disimpan pada form Buat dan Koreksi.

### Status
**Aktif** hijau · **Voided** abu (qty dicoret, watermark DIBATALKAN pada PDF). Peringatan "Belum ada stok untuk dikirim" = `MessageBar` info/peringatan, bukan merah.

### Daftar Surat Jalan
PageHeader + **primary `⊕ Buat Surat Jalan`**. `DataList`: pencarian (kode SJ / PO / customer), dropdown Customer/Tanggal, chip **Aktif · Voided** dengan jumlah, footer "Menampilkan X dari Y". Menu `⋯`: Cetak PDF, Detail.

### Buat Surat Jalan
Kartu header form + tabel baris kirim (Compact). Kolom Maks kirim dari server; input melebihi maks = sel merah + "Maks 50". Tombol `Alokasikan stok` (default kecil) membuka Dialog Alokasi PO tanpa meninggalkan tab. Footer: [Batal] [Simpan Surat Jalan] (primary, tanpa ikon). "Isi minimal satu baris" = galat di ringkasan `MessageBar` error.

### Detail Surat Jalan — aksi header
Badge status di PageHeader. Tombol (ikon kiri): **`Cetak PDF` (primary, hanya bila Aktif)**, `Edit Info`, `Koreksi`, **`Void` (bahaya)**. Tombol **Cetak PDF** menyebut "membuka berkas PDF di tab baru" pada `aria-label`. Surat jalan Voided: tanpa Cetak/Edit/Koreksi/Void (cetak ulang dokumen Voided hanya bila FSD mengizinkan; tetap menampilkan watermark). Edit Info = Dialog 480px kecil (tanggal, penerima, kendaraan, sopir, catatan).

### Dialog Void (480px)
Daftar dampak berlabel (cocok 480px): Stok fisik · Alokasi PO · Terkirim PO · Status PO (mis. "Completed → In Progress"); alasan wajib; tombol bahaya "Void surat jalan".

### Koreksi = halaman, bukan dialog
Grid baris kirim terisi + pratinjau dampak + alasan wajib tidak muat di 480px (U-1) → tab `sj:koreksi:{SJ-…}`. Footer [Batal] [Simpan Koreksi] (primary, nonaktif bila melanggar batas).

### PDF
Dokumen cetak tidak berubah (Fondasi bagian 13); tidak memakai token UI.


---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Nama komponen mengikuti kontrak Design.md bagian 13 (pemetaan dari nama lama: Fondasi bagian 8). Pustaka UI ditentukan oleh keputusan **K-1** (Fondasi bagian 12); spesifikasi ini tidak bergantung pada pustaka.

- **Komponen umum:** `AppHeader` (pencarian, bahasa, pengguna), `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `QtyGrid`/tabel baris kirim (Compact) dengan batas dari server, `Dialog` + dialog Alokasi (dari modul PO), tombol Cetak PDF (primary pada detail Aktif) + penanda CETAK ULANG/DIBATALKAN di dokumen PDF, `StatusBadge` Aktif/Voided.
- **Token yang dipakai:** `--brand` (primary), `--s1`/`--fg3`, `--s3`, badge `t-ok`/`t-obs`/`t-err`; PDF memakai font Liberation Sans, bukan token UI. Tidak ada hex/px mentah.
- **Deviasi:** Koreksi menjadi tab halaman (akibat U-1).
