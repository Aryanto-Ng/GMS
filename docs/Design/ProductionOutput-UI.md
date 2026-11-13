# Desain UI — Production Output

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/ProductionOutput.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** |
| Render Mode | Semua **Interactive Server** |
| Pengguna utama | Admin Produksi, input volume tinggi di PC → mode **Compact**, keyboard-first |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| **Input Output** | `/produksi/input` | `ProductionOutput.Create` |
| Penyesuaian Output | `/produksi/penyesuaian` | `ProductionOutput.Adjust` |
| Kirim/Terima Vendor | `/produksi/vendor` (tab Kirim · Terima) | `VendorJob.Create` |
| Riwayat Produksi | `/produksi/riwayat` | `ProductionOutput.View` / `VendorJob.View` |
| Detail Entri | `/produksi/riwayat/{id}` (**baris yang diperluas inline**, bukan panel samping) | View |

Dialog: Void, Konfirmasi Peringatan. **Koreksi = tab halaman** (lihat Revisi Fluent 2).

---

## 2. Input Output (Layar Inti)

```
Input Output
WO*   [WO-202611-0004 · KMJ-001 · PT Maju ▾]   Proses* [3 Sewing ▾]   Tanggal Kerja* [08/11/2026]
Info: Output proses sebelumnya (Cutting) 665 · Sudah Sewing 290 · Tersedia 375         [Filter Color: ☑Navy ☑Orange]

                    │ Navy                            │ Orange              │
Pekerja             │ S      M      L      XL        │ S      M      L      │ Total
Batas tersedia      │ 40     110    100    55         │ 0      45     25     │   
───────────────────┼─────────────────────────────────┼──────────────────────┼───────
[PKJ-0012 Budi ▾] ✕ │ [20 ]  [30 ]  [   ]  [   ]      │ [  ]   [  ]   [  ]   │   50
[PKJ-0007 Siti ▾] ✕ │ [   ]  [40 ]  [25 ]  [   ]      │ [  ]   [10 ]  [  ]   │   75
[ + Tambah pekerja ]                                                            
Total               │ 20     70     25     0          │ 0      10     0       │  125
[Simpan]                                    ● Belum disimpan   2 pekerja · 125 pcs
```

### Perilaku
- **Selector atas:** WO (hanya Released/In Progress), Proses (langkah Internal pada routing snapshot, berurut), Tanggal Kerja (default hari ini; tanggal masa depan dan tanggal pada periode tertutup ditolak dengan pesan).
- **Baris info:** menampilkan output proses sebelumnya, kumulatif proses ini, dan tersedia.
- **Baris "Batas tersedia"** per kolom Color × Size: `Output proses sebelumnya − Kum proses ini`. Untuk proses pertama: "Bebas" dengan angka sisa terhadap Qty WO sebagai rujukan.
- **Kolom** = Color (kelompok) → Size. Bila kolom lebih lebar dari layar: kolom Pekerja dan header tetap, gulir horizontal. Filter Color mempersempit kolom yang ditampilkan (data di luar filter tetap tersimpan).
- **Tambah pekerja:** combobox bersifat pencarian kode/nama, hanya pekerja aktif. Pekerja yang sudah ada di tabel tidak muncul lagi. Fokus langsung ke sel pertama baris baru.
- **Keyboard:** Tab/Shift+Tab antar sel, Enter pindah ke bawah, panah, Ctrl+V tempel blok, Ctrl+Enter = Simpan, Alt+N = tambah pekerja.
- **Validasi langsung:** sel yang melebihi batas → bergaris merah + ikon + pesan "Maks 40". Total di bawah memperbarui secara langsung.
- **Proses pertama melebihi Qty WO:** sel kuning + ikon dan pesan "Melebihi Qty WO (kumulatif 1.050 dari 1.000)". Saat Simpan: dialog konfirmasi (AC-PR-05).
- **Simpan:** semua-atau-tidak-sama-sekali. Bila ada baris bermasalah: ringkasan galat di atas, baris/sel ditandai, fokus ke ringkasan.
- **Setelah berhasil:** toast "2 entri tersimpan (125 pcs)". WO, Proses, dan Tanggal **tetap**, baris pekerja dikosongkan, fokus ke tombol Tambah pekerja.
- **Penjaga keluar:** peringatan bila ada input belum tersimpan.
- Tidak ada nilai rupiah/tarif di layar ini.

---

## 3. Kirim / Terima Vendor

```
Vendor    Tab: Kirim │ Terima
WO* [WO-202611-0004 ▾]   Proses vendor* [2 Bordir ▾]   Vendor* [CV Bordir Indah ▾]
Tanggal* [08/11/2026]   No. Dokumen [_________]   Keterangan [_________]
                 Navy                         Orange
                 S     M     L     XL         S     M     L
Tersedia dikirim 110   210   160   55         —     85    45     ← (Tab Kirim)
Qty kirim       [100] [200] [   ] [   ]       [  ]  [ 80] [  ]
────────────────────────────────────────────────────────────
Sisa di vendor (CV Bordir Indah)  S 0 · M 0 · ...                    ← (Tab Terima: batas = terkirim − diterima)
[Simpan]
```
- Tab **Terima** menampilkan "Sisa di vendor" per Color × Size untuk vendor terpilih sebagai batas. Terima sebagian diperbolehkan.
- Panel samping "Ringkasan Vendor": per vendor Terkirim / Diterima / Sisa untuk langkah ini.
- Semua aturan validasi, konfirmasi, dan pesan mengikuti pola Input Output.

---

## 4. Penyesuaian Output

```
Penyesuaian Output (untuk periode gaji yang sudah ditutup)
WO* [...]  Proses* [...]  Pekerja* [PKJ-0012 Budi ▾ (termasuk nonaktif)]
Tanggal Asal* [01/11/2026]  (harus dalam periode tertutup GJ-20261026-20261101)
Tanggal Kerja penyesuaian* [08/11/2026]  (periode terbuka)
Alasan* [______________________________]
Qty (boleh negatif):   Navy S [ -5 ]  M [  ]  ...
[Simpan penyesuaian]
```
- Peringatan info: "Upah penyesuaian dihitung dengan tarif snapshot WO dan masuk ke periode yang memuat Tanggal Kerja penyesuaian."
- Tanggal Asal di periode terbuka ditolak dengan arahan "Gunakan Void atau Koreksi dari Riwayat".
- Sel menerima tanda minus. Pelanggaran aturan kumulatif ditampilkan sebagai galat dengan rincian sel.

---

## 5. Riwayat Produksi

```
Riwayat Produksi
[WO ▾][Proses ▾][Pekerja ▾][Vendor ▾][Tanggal kerja ▾][Jenis ▾][Status: Aktif ▾]   ☐ tampilkan Void
Waktu input       WO         Proses   Pekerja/Vendor  Tgl kerja   Jenis        Qty    Status   Aksi
08 Nov 10:02      WO-…0004   Sewing   Budi            08 Nov      Output       50     Aktif    ⋯
08 Nov 09:40      WO-…0004   Sewing   Siti            08 Nov      Output       ~~75~~ Void     ⋯ (↳ digantikan oleh #…)
07 Nov 16:10      WO-…0004   Bordir   CV Bordir       07 Nov      Terima       250    Aktif    ⋯
```
- Baris Voided: teks dicoret + chip "Void" + alasan di tooltip. Tautan antar entri (digantikan oleh / menggantikan).
- Klik baris memperluas **detail inline**: qty per Color × Size, alasan, rujukan, jejak pembuat. Aksi: Void, Koreksi (sesuai permission dan syarat).
- Aksi Void/Koreksi dinonaktifkan dengan tooltip alasan bila WO Completed/Closed atau tanggal kerja berada di periode tertutup (arahkan ke Penyesuaian).
- Ekspor tidak ada di fase 1.

### Dialog Void
```
Batalkan entri ini?  Sewing · Siti · 08 Nov · 75 pcs
Dampak pada kumulatif:
 Langkah   Sel         Sebelum  Sesudah  Batas    Status
 3 Sewing  Navy · M     100      60       ≥ 4 QC 40  ✓
 3 Sewing  Navy · L      60      35       ≥ 4 QC 40  ✗ Tertahan oleh QC (40)
Alasan* [______________________]
[Batal] [Void entri]      ← nonaktif bila ada ✗
```
### Dialog Koreksi
Grid yang sama dengan Input Output (satu baris), terisi nilai entri asal; boleh mengubah pekerja, tanggal, dan qty. Panel pratinjau dampak (kondisi akhir) di bawah, alasan wajib. [Simpan koreksi] nonaktif bila ada ✗.

---

## 6. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Input Output | Skeleton selector + grid setelah pilih WO | Belum pilih WO: "Pilih WO dan proses untuk mulai". WO tanpa proses Internal: pesan. | Gagal muat batas: banner + Coba lagi, Simpan nonaktif |
| Simpan | Spinner pada tombol, grid `aria-busy` | – | Ringkasan galat + sel ditandai. Konflik batas berubah: banner "Batas diperbarui" dan nilai input dipertahankan |
| Kirim/Terima | Skeleton | Tab Terima tanpa kiriman: "Belum ada barang di vendor" | idem |
| Riwayat | Skeleton 10 baris | "Belum ada catatan produksi" / hasil filter kosong + Reset | Banner + Coba lagi |
| WO tidak boleh diinput | – | Pesan di atas halaman: "WO ini berstatus Completed. Buka kembali untuk menginput." (+ tautan ke WO) | – |
| Koneksi terputus | Banner "Menyambung ulang…" | – | Bila putus, peringatan data belum tersimpan hilang |

---

## 7. Aksesibilitas

- Grid input: `role="grid"`, nama sel "Budi, Navy M, qty", batas dibacakan ("maksimum 110"). Galat melalui `aria-describedby` dan pengumuman `aria-live="assertive"` untuk ringkasan.
- Tidak ada informasi yang hanya di warna: kuning/merah selalu disertai ikon dan teks.
- Combobox pekerja: pola ARIA combobox, daftar dapat dinavigasi panah, Enter memilih.
- Tombol aksi Void/Koreksi dengan label jelas ("Void entri Siti 08 Nov").
- Mode Compact tetap menjaga target klik ≥ 24 px dan jarak fokus yang jelas.

---

## 8. Catatan untuk Frontend

- Komponen **Grid Matriks Ukuran** harus mendukung: kolom bertingkat (Color → Size), baris "batas", baris pekerja dinamis, tempel blok, virtualisasi bila kolom banyak.
- Batas tersedia dan pratinjau dampak berasal dari server (aturan kumulatif tidak diimplementasikan ulang di klien).
- Kunci keputusan: penyimpanan tunggal semua-atau-tidak-sama-sekali, dan state input dipertahankan saat penolakan.

---

## Revisi Fluent 2 (v3.0, 2026-11-13)

Bagian ini **menggantikan** wireframe, pola, dan istilah lama yang bertentangan. Acuan: `docs/Design.md` v2.0 (Fluent 2) dan `docs/Design/_Foundation-UI.md` v3.0. Mock-up visual: `docs/Design/mockup/index.html`.

> Glyph pada wireframe di dokumen ini (⏰ ⚠ ✓ ◔ ⛔ 🔒 ⋯ ▾) hanyalah **notasi**. UI memakai ikon Fluent 16px + teks, tanpa emoji (Design.md bagian 12).

### Tab Manager
| Layar | ID tab |
|---|---|
| Input Output | `output:input:{WO-…}` (satu tab per WO; membuka WO yang sama memfokuskan tab itu) |
| Penyesuaian Output | `output:adjust` |
| Kirim/Terima Vendor | `output:vendor:{WO-…}` |
| Riwayat Produksi | `output:history` |
| Koreksi entri | `output:koreksi:{id}` |

Titik **belum disimpan** muncul segera setelah ada sel terisi dan menahan penutupan tab (konfirmasi Dialog 480px). Alert tab: **oranye** bila ada sel melebihi Qty WO (peringatan), **merah** bila ada sel melebihi batas (galat); hilang otomatis saat dibereskan.

### Input Output (Grid input, mode Compact, keyboard-first)
Selector WO / Proses / Tanggal Kerja di kartu atas (tinggi kontrol 32px). Baris "Batas tersedia" terlihat **sebelum** mengetik; sel melebihi → border + garis bawah merah, ikon + pesan "Maks 40"; peringatan Qty WO = oranye + ikon + teks. Pesan galat menyebut letaknya ("Melebihi batas tersedia: Navy / M (maks 110)"). Ringkasan galat di atas = `MessageBar` error (tidak dapat ditutup), fokus pindah ke sana. Footer kartu aksi: catatan kiri (`● Belum disimpan · 2 pekerja · 125 pcs`), kanan **[Simpan]** primary tanpa ikon (nonaktif/diblokir dengan penjelasan bila ada galat). Toast sukses eksplisit: "2 entri tersimpan (125 pcs)". WO tidak bisa diinput = `MessageBar` peringatan di atas halaman + tautan ke WO.

### Kirim/Terima Vendor
Inner tabs **Kirim · Terima**; aturan grid sama. Ringkasan Vendor (Terkirim/Diterima/Sisa) tampil sebagai `StatTile` di atas grid (menggantikan "panel samping" — Design.md tidak mengenal panel samping).

### Riwayat Produksi
PageHeader "Riwayat Produksi" (tanpa primary). `DataList`: **pencarian** (WO, pekerja, vendor, no. dokumen), dropdown WO/Proses/Pekerja/Vendor/Tanggal kerja, **chip jenis dengan jumlah**: Output · Terima · Kirim · Penyesuaian · Void (+ chip `hariIni`). Footer "Menampilkan X dari Y". Status: **Aktif** hijau, **Void** abu + teks dicoret + tautan antar entri; qty negatif = teks merah dengan tanda minus (badge **Negatif** merah pada Penyesuaian bernilai negatif). **Detail entri = baris yang dapat diperluas (inline)**, menggantikan "panel samping": qty per Color × Size, alasan, rujukan, jejak pembuat, dan aksi **Void** (bahaya) / **Koreksi** (default). Aksi nonaktif dengan tooltip bila WO Completed/Closed atau tanggal di periode tertutup (arahkan ke Penyesuaian).

### Dialog Void (480px)
Judul "Void entri ini?" + ringkasan satu baris. Pratinjau dampak: daftar kartu per sel (Langkah · Sel · Sebelum → Sesudah · Batas · status ✓ "Terpenuhi" / ✗ "Tertahan oleh QC (40)"), bukan tabel lebar; bila perlu tabel penuh, gulir horizontal di dalam dialog (menunggu **U-1**). Alasan wajib, tombol bahaya "Void entri" nonaktif bila ada ✗.

### Koreksi = halaman, bukan dialog
Grid terisi + pratinjau dampak + alasan tidak muat di 480px. Koreksi dibuka sebagai **tab** (`output:koreksi:{id}`) berisi grid satu baris pekerja (nilai asal terisi), panel pratinjau dampak (kondisi akhir, dari server) dan alasan wajib; footer [Batal] [Simpan Koreksi] (primary, nonaktif bila ada ✗).

### Penyesuaian
Form kartu: WO, Proses, Pekerja, Tanggal Asal, Tanggal Kerja penyesuaian, Alasan (wajib), grid qty (boleh negatif). `MessageBar` info (dapat ditutup) menjelaskan penghitungan upah. Tanggal asal di periode terbuka ditolak dengan arahan Void/Koreksi.


---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Nama komponen mengikuti kontrak Design.md bagian 13 (pemetaan dari nama lama: Fondasi bagian 8). Pustaka UI ditentukan oleh keputusan **K-1** (Fondasi bagian 12); spesifikasi ini tidak bergantung pada pustaka.

- **Komponen umum:** `AppHeader` (pencarian, bahasa, pengguna), `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `QtyGrid` (grid output per Color × Size, hint "Maks N", galat/peringatan per sel), `StatTile` (ringkasan vendor), baris tabel yang dapat diperluas (detail entri), `ImpactPreview` (daftar dampak dari server) di dalam `Dialog`, inner tabs Kirim/Terima, `MessageBar` untuk penahan server, `StatusBadge` Aktif/Void.
- **Token yang dipakai:** `--brand` (primary, fokus sel), `--s1`/`--fg3` (input), tint merah (sel invalid), `--s3`, badge `t-ok` (Aktif), `t-obs` (Void), `t-err` (Negatif/galat), `t-warn` (peringatan Qty WO). Tidak ada hex/px mentah.
- **Deviasi:** Koreksi menjadi tab halaman (akibat U-1). Dialog Void memakai daftar dampak di 480px (menunggu U-1). Grid output tetap tanpa tempel blok, Ctrl+Enter, dan kolom bertingkat penuh sampai fase 2 (diterima user 2026-11-10).
