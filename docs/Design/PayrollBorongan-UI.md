# Desain UI — Payroll Borongan

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/PayrollBorongan.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | **Interactive Server**; Excel/CSV/PDF via endpoint unduhan |
| Catatan | Halaman ini menampilkan **tarif dan rupiah**. Hanya `Payroll.View`. |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Daftar Periode | `/gaji` | `Payroll.View` |
| Detail Periode | `/gaji/{id}` (tab `?tab=ringkasan|rincian|harian|matriks|penyesuaian`) | `Payroll.View` |
| Unduhan Ekspor | `/gaji/{id}/ekspor?format=xlsx|csv-ringkasan|csv-harian` | `Payroll.Export` |
| Slip Gaji PDF | `/gaji/{id}/slip?pekerja={id|semua}&harian=0|1` | `Payroll.PrintSlip` |

Dialog: Buat Periode, Ubah Periode (Open), Tutup Periode, Opsi Slip.

---

## 2. Wireframe

### 2.1 Daftar Periode
```
Periode Gaji                                                        [ + Buat Periode ]
Kode                  Rentang                   Status      Pekerja   Total (Rp)      Ditutup
GJ-20261102-20261108  02 Nov – 08 Nov 2026      ◔ Open        14      18.450.000 *     –
GJ-20261026-20261101  26 Okt – 01 Nov 2026      🔒 Closed      13      17.120.500     02 Nov oleh Siti
* sementara — BELUM FINAL
```
- Open: total ditandai asterisk + chip "Belum final".
- Bila tidak ada periode Open: banner ajakan "Belum ada periode terbuka. Mulai periode berikutnya mulai 09 Nov 2026" dengan tombol [Buat Periode].

### 2.2 Dialog Buat Periode
```
Buat Periode Gaji
Tanggal Mulai   [09/11/2026]  (terkunci — hari setelah periode sebelumnya)   ← periode pertama: bebas, dengan petunjuk "Output tertua: 01 Nov 2026"
Tanggal Selesai* [15/11/2026]
Catatan [______]
Pintasan: [7 hari] [14 hari] [Akhir bulan]
[Batal] [Buat Periode]
```
- Tanggal Selesai < Mulai → galat. Ada periode Open → tombol nonaktif dengan alasan.

### 2.3 Detail Periode
```
← Periode / GJ-20261102-20261108         [Chip: Open — BELUM FINAL]
02 Nov – 08 Nov 2026 · 14 pekerja · Total Rp 18.450.000*      [Segarkan] [Ekspor ▾] [Slip Gaji ▾] [Tutup Periode] [Ubah]
Tab: Ringkasan │ Rincian │ Rincian Harian │ Matriks Harian │ Penyesuaian (2)
```
- Banner kuning pada periode Open: "Angka bersifat sementara dan berubah mengikuti input produksi."
- Periode Closed: banner netral "Periode ditutup pada … oleh …. Angka final." dan tombol Tutup/Ubah tidak ada.

#### Tab Ringkasan
```
[🔍 Pekerja]                                        Total Qty   Jumlah (sebelum bulat)   Dibayarkan (Rp)
PKJ-0007  Siti Rahayu                                 1.280      3.241.350,5            3.241.351
PKJ-0012  Budi Santoso                                  940      2.118.200,25           2.118.200
PKJ-0019  Andi ⚠ total negatif                          −20        −52.500,00             −52.500
                                                                      Total periode:     18.450.000
```
Klik baris → panel detail pekerja (Rincian).

#### Tab Rincian (dua tampilan: ◉ Per WO/Proses/Size ○ Per Hari)
**Per WO/Proses/Size**
```
PKJ-0012 Budi Santoso                                       Dibayarkan Rp 2.118.200
 ▾ WO-202611-0004 · Sewing
     Size   Qty    Tarif       Subtotal
     S      120    2.500       300.000
     M      200    2.500       500.000
     XL      60    2.750       165.000
 ▾ WO-202611-0004 · QC  …
 ▸ Penyesuaian: Sewing · M · −10 · Tgl asal 30 Okt · "Salah input" · −25.000
```
**Per Hari**
```
PKJ-0012 Budi Santoso
 Senin 02 Nov — subtotal Rp 412.500
    WO-…0004 Sewing S 40 × 2.500 = 100.000 · M 80 × 2.500 = 200.000 · …
 Selasa 03 Nov — subtotal …
```
Sumber: tautan ke entri output (bagi yang berhak di modul Produksi).

#### Tab Rincian Harian (seluruh pekerja)
Filter: rentang tanggal dalam periode, pekerja, WO, proses. Tabel: Tanggal, Pekerja, WO, Proses, Size, Jenis, Qty, Tarif, Subtotal; sub-total harian per pekerja (baris ringkas).

#### Tab Matriks Harian
```
              Sen 02   Sel 03   Rab 04   Kam 05   Jum 06   Sab 07   Min 08 │ Total
PKJ-0007 Siti   160      …                                                  │ 1.280 pcs
                Rp 400k  …                                                  │ Rp 3.241.351
PKJ-0012 Budi   110
Total / hari    …
```
Sel menampilkan qty dan upah hari itu; hari tanpa output kosong ("·"). Toggle tampilan: Qty / Rupiah / Keduanya. Kolom pekerja dan header tetap. Klik sel → rincian hari itu.

#### Tab Penyesuaian
Daftar baris jenis Penyesuaian yang masuk periode: Pekerja, WO, Proses, Size, Qty (±), Tanggal Asal, Alasan, Subtotal.

### 2.4 Dialog Tutup Periode
```
Tutup periode GJ-20261102-20261108?
 Rentang     02 Nov – 08 Nov 2026
 Pekerja     14        Total     Rp 18.450.000
 ⚠ 1 pekerja bertotal negatif (Andi)
 ⚠ Tanggal selesai periode di masa depan  (jika berlaku)
 ⓘ Penutupan PERMANEN. Input produksi pada tanggal ini akan terkunci;
   koreksi dilakukan lewat Penyesuaian pada periode berikutnya.
Ketik kode periode untuk konfirmasi:  [ GJ-20261102-20261108 ]
[Batal] [Tutup Periode]     ← aktif setelah kode sesuai
```

### 2.5 Menu Ekspor
- Excel (.xlsx) — semua sheet · CSV Ringkasan · CSV Rincian Harian.
- Periode Open: item diberi catatan "BELUM FINAL" dan berkas diberi tanda pada nama.

### 2.6 Dialog Opsi Slip
```
Slip Gaji PDF
 ◉ Semua pekerja (1 berkas)   ○ Satu pekerja [PKJ-… ▾]
 ☐ Sertakan rincian harian
[Batal] [Buat PDF]
```

---

## 3. Dokumen PDF — Slip Gaji (A4 portrait)

```
┌──────────────────────────────────────────────────────────────┐
│ [Nama Konveksi]                         SLIP UPAH BORONGAN    │
│ [Alamat Konveksi]                                 [BELUM FINAL]│
│ Periode: 02 Nov – 08 Nov 2026                                 │
│ Pekerja: PKJ-0012 · Budi Santoso                              │
│                                                               │
│ WO / Proses              Size    Qty     Tarif      Subtotal  │
│ WO-…0004 · Sewing        S       120     2.500      300.000   │
│                          M       200     2.500      500.000   │
│ …                                                              │
│ Penyesuaian (tgl asal 30 Okt) Sewing M  −10  2.500   −25.000  │
│                                                               │
│ Jumlah (sebelum pembulatan)                  2.118.200,25     │
│ DIBAYARKAN                                    Rp 2.118.200    │
│                                                               │
│ [Opsional] Rincian harian: tabel Tanggal · WO · Proses · …    │
│                                                               │
│ Diterima oleh:  (________________)   Tanggal: __/__/____      │
│ Dicetak oleh [pengguna] pada 08 Nov 2026 14:30                │
└──────────────────────────────────────────────────────────────┘
```
- Satu pekerja mulai di halaman baru pada berkas gabungan. Urut Kode Pekerja.
- Total negatif ditandai kata "NEGATIF" + tanda minus.
- Periode Open memakai watermark **BELUM FINAL**.
- Terbaca dengan cetak hitam-putih.

---

## 4. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Daftar periode | Skeleton 5 baris | "Belum ada periode gaji" + [Buat Periode] | Banner + Coba lagi |
| Detail (Open) | Skeleton ringkasan + indikator "menghitung…" | "Belum ada output pada periode ini" | Gagal hitung: banner + Coba lagi (tab lain tetap tersedia) |
| Detail (Closed) | Skeleton | – | 404 "Periode tidak ditemukan" |
| Matriks Harian | Skeleton grid | "Tidak ada output pada periode ini" | idem |
| Tutup periode | Spinner + `aria-busy`, tombol dikunci | – | Gagal (mis. baris tarif hilang): daftar baris bermasalah, periode tetap Open |
| Ekspor/Slip | "Menyiapkan berkas…" | – | "Gagal membuat berkas" + Coba lagi + traceId |
| Konflik | Banner "Periode telah ditutup oleh pengguna lain" | | |

---

## 5. Aksesibilitas

- Angka rupiah rata kanan, `font-variant-numeric: tabular-nums`. Negatif selalu bertanda minus dan ikon ⚠, tidak hanya warna.
- Matriks Harian: tabel dengan `scope` baris/kolom, sel bernama "Budi, Sen 02 Nov: 110 pcs, Rp 275.000". Tersedia alternatif tampilan daftar (Rincian Harian) untuk pembaca layar.
- Dialog Tutup Periode: `role="alertdialog"`, input konfirmasi berlabel, tombol utama dinonaktifkan dengan penjelasan terbaca.
- Chip "BELUM FINAL"/Open/Closed berupa ikon + teks.

---

## 6. Catatan untuk Frontend

- Tarif/rupiah tidak boleh dikirim ke klien tanpa `Payroll.View` (penegakan server, termasuk ekspor dan PDF).
- Rincian dibuka per kelompok dengan paging di server untuk periode besar.
- Gunakan komponen pembuat PDF bersama (dengan Surat Jalan).

---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` dan `docs/Design/_Foundation-UI.md` v2.0. Nama komponen mengikuti kontrak Design.md; implementasi di `docs/Frontend/README.md`.

- **Komponen umum:** AppShell (header + MegaMenu + CommandPalette), `PageHeader`, tabel `gms-table` + `PagerBar` (DataTable), `StatusTag`, `ProblemBanner` (Alert), `EmptyState` / `ErrorState` / `SkeletonRows`, `AntDesign.Button` (satu `Primary` per area), `IGmsToast`.
- **Khusus modul:** Tab (Rincian, Harian, Matriks) dengan tabel `gms-table` rapat (`compact`), `ConfirmDialog` dengan konfirmasi ketik kode periode (Tutup Periode, permanen), `StatusTag` `provisional`/Open/Closed, ekspor dan unduh slip lewat tombol `Default`.
- **Token yang dipakai:** `warning-*` (Belum final), `primary-bg` (Open), `border-secondary` (Closed), `danger-text` (total negatif), `fill-alter` (baris total), `space-3`.
- **Deviasi:** `ConfirmDialog` memakai `<dialog>` native (lihat `_Foundation-UI.md` 4.4). Tidak ada deviasi lain pada modul ini.
