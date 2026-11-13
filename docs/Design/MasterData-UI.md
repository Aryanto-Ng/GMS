# Desain UI — Master Data

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/MasterData.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** |
| Render Mode | Semua **Interactive Server** |

---

## 1. Halaman dan Rute

Grup navigasi **Master Data**. Setiap entitas memiliki daftar dan form (pola Daftar dan Form, Fondasi bagian 5.1).

| Entitas | Rute daftar | Catatan UI |
|---|---|---|
| Satuan | `/master/satuan` | Form sederhana (dialog) |
| Kategori Material | `/master/kategori-material` | Form sederhana (dialog) |
| Material | `/master/material` | Form halaman, Import |
| Size | `/master/size` | Daftar dapat diurutkan (Urutan Tampil) |
| Color | `/master/color` | Pemilih warna hex opsional |
| Customer | `/master/customer` | Form halaman |
| Process | `/master/process` | Tipe Internal/Vendor |
| Vendor | `/master/vendor` | Form halaman |
| Pekerja | `/master/pekerja` | Form halaman, Import (dikelola Konveksi) |
| **FG** | `/master/fg`, `/master/fg/{id}` | **Halaman detail bertab** (bagian 2) |
| Import | `/master/{entitas}/import` | Wizard 3 langkah (bagian 4) |

Entitas bentuk sederhana (Satuan, Kategori, Size, Color, Process) memakai **dialog form** di atas daftar, bukan halaman terpisah. Entitas lain memakai halaman form.

---

## 2. FG — Halaman Detail Bertab

```
← FG / KMJ-001 · Kemeja Wearpak Lengan Panjang           [Chip: Siap Produksi ✓]   [Salin dari FG lain] [Nonaktifkan]
Tab: Umum │ Size Berlaku (4) │ Routing (6) │ Tarif │ Kesiapan
```

### 2.1 Tab Umum
Form: Kode (terkunci bila sudah dipakai), Nama, Jenis, Customer (opsional), Keterangan, Status aktif.

### 2.2 Tab Size Berlaku
Daftar kotak centang semua Size aktif (urut Urutan Tampil). Minimal satu. Menghapus centang Size yang sudah bertarif memunculkan dialog: "Tarif size XL (3 proses) akan dihapus. Nilai tetap tercatat di audit log." [Batal] [Hapus size dan tarif].

### 2.3 Tab Routing
```
Routing (alur produksi berurutan)
 1  ⠿  Cutting   [Internal]                      ↑ ↓  ✕
 2  ⠿  Bordir    [Vendor]                        ↑ ↓  ✕
 3  ⠿  Sewing    [Internal]                      ↑ ↓  ✕
 4  ⠿  QC        [Internal]                      ↑ ↓  ✕
 [ + Tambah proses ▾ (hanya proses aktif yang belum ada) ]
 ⓘ Perubahan hanya berlaku untuk Work Order yang dirilis setelah ini.
[Batal] [Simpan routing]
```
- Urutan dapat diubah dengan seret (⠿) **dan** tombol ↑ ↓ (alternatif keyboard).
- Proses yang sudah ada tidak muncul lagi di pilihan (tidak boleh dobel).
- Menghapus langkah bertarif: dialog "Tarif langkah ini akan dihapus" (AC-MD-20).
- Menghapus semua langkah: ditolak, pesan "Minimal 1 langkah".

### 2.4 Tab Tarif (tampil bagi pengguna dengan `MasterData.Tarif.View`)
```
Tarif per unit (Rp) — Proses Internal × Size                       [Isi semua size…]
Proses              S            M            L            XL          │
1 Cutting     [ 350      ] [ 350      ] [ 350      ] [ 350,5    ]     [Isi semua ▸]
3 Sewing      [ 2.500    ] [ 2.500    ] [ 2.750    ] [          ]  ⚠  [Isi semua ▸]
4 QC          [ 400      ] [ 400      ] [ 400      ] [ 400      ]     [Isi semua ▸]
2 Bordir (Vendor) — tanpa tarif
⚠ 1 sel belum diisi (Sewing · XL)
[Batal] [Simpan tarif]               ● Ada perubahan belum disimpan
```
- Baris = langkah **Internal** sesuai urutan routing (nomor urut asli ditampilkan). Langkah Vendor tampil sebagai baris info abu-abu.
- Input desimal berformat Indonesia (koma), hingga 5 digit di belakang koma. Nilai > 5 digit atau ≤ 0 → galat sel dengan teks ("Maksimal 5 digit desimal", "Harus lebih dari 0"). Tidak dibulatkan diam-diam.
- Tab/Enter berpindah sel seperti Grid Matriks. Tempel blok dari Excel didukung.
- Tombol **Isi semua ▸** per baris mengisi semua sel baris dengan nilai sel pertama yang terisi (dengan konfirmasi bila menimpa).
- Indikator "perubahan belum disimpan" dan penjaga keluar halaman.
- Hanya pengguna dengan `MasterData.Tarif.Edit` yang dapat mengubah, lainnya hanya baca.

### 2.5 Tab Kesiapan
```
Status: ✓ Siap Produksi    atau    ⚠ Belum Siap
 ✓ FG aktif
 ✓ Minimal 1 Size Berlaku
 ✓ Routing memiliki 4 langkah, semua proses aktif
 ✗ Tarif Sewing untuk size XL belum diisi      [Buka Tarif ▸]
```
Setiap butir gagal memiliki tautan langsung ke tab/sel yang perlu diperbaiki.

### 2.6 Dialog "Salin dari FG lain" (wizard 3 langkah)
1. **Pilih FG sumber** (pencarian; hanya FG yang punya routing).
2. **Pratinjau:** routing yang akan disalin dan grid tarif hasil salinan. Sel dari Size yang tidak berlaku pada FG tujuan ditandai "dilewati" (daftar di bawah).
3. **Konfirmasi:** bila FG tujuan sudah punya routing/tarif: peringatan "Akan menimpa routing dan tarif yang ada" dan kotak centang konfirmasi. Tombol [Salin dan simpan].

---

## 3. Daftar dan Form Entitas

Kolom dan field utama (kolom pertama selalu Kode; kolom Status berupa chip Aktif/Nonaktif):

| Entitas | Kolom daftar | Field form |
|---|---|---|
| Material | Kode, Nama, Kategori, Satuan, Status | Kode, Nama, Kategori*, Satuan*, Spesifikasi |
| Size | Urutan, Kode, Nama, Status | Kode, Nama, Urutan Tampil |
| Color | Swatch, Kode, Nama, Status | Kode, Nama, Hex (opsional, pratinjau swatch) |
| Customer | Kode (otomatis), Nama, Kontak, Telepon, Status | Nama, PIC, Telepon, Alamat, Keterangan |
| Process | Kode, Nama, Tipe, Status | Kode, Nama, Tipe (terkunci bila sudah dipakai) |
| Vendor | Kode (otomatis), Nama, Layanan, Telepon, Status | Nama, Jenis Layanan, PIC, Telepon, Alamat, Keterangan |
| Pekerja | Kode (otomatis), Nama, Proses Utama, Tgl Masuk, Status | Nama, Tanggal Masuk, Proses Utama (opsional), Keterangan |
| FG | Kode, Nama, Jenis, Customer, **Kesiapan**, Status | Lihat 2.1 |

Aturan bersama:
- Kode otomatis ditampilkan sebagai "Otomatis saat disimpan". Kode terkunci menampilkan ikon gembok + tooltip "Sudah dipakai, tidak dapat diubah".
- Tidak ada tombol hapus. Aksi **Nonaktifkan/Aktifkan** dengan dialog dampak. Penolakan (mis. Process di routing aktif) menampilkan **daftar pemakai** (FG yang menahan) sebagai tautan.
- Daftar Size dapat diubah urutannya dengan seret atau mengisi Urutan Tampil.
- Daftar FG menampilkan kolom **Kesiapan** (chip) dan filter "Hanya Siap Produksi".

---

## 4. Wizard Import CSV

```
Impor Material       ① Unggah ──── ② Periksa ──── ③ Konfirmasi
```
1. **Unggah:** area seret-lepas + tombol pilih berkas, tautan **Unduh template**, ketentuan (UTF-8, koma atau titik koma, maks 2.000 baris/2 MB).
2. **Periksa:** ringkasan "148 baris: 146 valid, 2 error". Tabel hasil per baris (No, Kode, Status ✓/✗, Pesan). Filter "Hanya error". Bila ada error: tombol lanjut nonaktif dengan teks "Perbaiki berkas dan unggah ulang" (semua-atau-tidak-sama-sekali).
3. **Konfirmasi:** ringkasan jumlah yang akan ditambahkan, tombol [Impor sekarang]. Hasil: toast sukses dan tautan ke daftar.

Import FG: kolom Size Berlaku memakai pemisah `|` (contoh `S|M|L|XL`), ditampilkan pada template.

---

## 5. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Daftar entitas | Skeleton 8 baris | "Belum ada {entitas}" + [Tambah] atau [Impor] (bila berhak) | Banner Problem Details + Coba lagi |
| Hasil filter kosong | – | "Tidak ada hasil" + Reset filter | – |
| Detail FG | Skeleton header + tab | – | 404 "FG tidak ditemukan" |
| Tab Routing kosong | – | "Routing belum disusun. Tambahkan proses pertama." | – |
| Tab Tarif | Skeleton grid | "Tarif muncul setelah routing dan size berlaku diisi" + tautan ke tab terkait | Gagal simpan: banner + sel bermasalah ditandai |
| Wizard Import | Progres unggah/validasi (bar + teks) | – | Berkas ditolak (encoding/kolom/ukuran): pesan jelas + saran |
| Konflik (409) | Banner "Data telah diubah pengguna lain" + Muat ulang | | |

---

## 6. Aksesibilitas

- Grid Tarif memakai pola Grid Matriks (`role="grid"`, sel bernama "Sewing, size XL, tarif"). Galat sel dikaitkan dengan `aria-describedby`.
- Seret-lepas (routing, urutan size, unggah) **selalu** memiliki alternatif keyboard (tombol ↑ ↓, input angka, tombol pilih berkas).
- Chip Kesiapan dan Status memakai ikon + teks. Butir kesiapan gagal dibacakan sebagai "Belum terpenuhi".
- Swatch Color disertai nama warna sebagai teks.
- Wizard memakai indikator langkah dengan `aria-current="step"`.

---

## 7. Catatan untuk Frontend

- Satu komponen **Daftar Master Generik** (kolom dikonfigurasi) dan satu **Dialog Form Master** untuk entitas sederhana.
- Komponen **Grid Matriks** dari Fondasi dipakai ulang dengan mode input desimal untuk Tarif.
- Tab Tarif dan tombol Edit disembunyikan menurut permission; penegakan di server.

---

## Revisi Fluent 2 (v3.0, 2026-11-13)

Bagian ini **menggantikan** wireframe, pola, dan istilah lama yang bertentangan. Acuan: `docs/Design.md` v2.0 (Fluent 2) dan `docs/Design/_Foundation-UI.md` v3.0. Mock-up visual: `docs/Design/mockup/index.html`.

> Glyph pada wireframe di dokumen ini (⏰ ⚠ ✓ ◔ ⛔ 🔒 ⋯ ▾) hanyalah **notasi**. UI memakai ikon Fluent 16px + teks, tanpa emoji (Design.md bagian 12).

### Tab Manager
`master:{entitas}` (daftar) · `master:fg:{kode}` (detail FG) · `master:{entitas}:import`. Titik belum disimpan pada tab FG bila Umum/Routing/Tarif berubah. Alert tab **oranye** pada FG **Belum Siap**.

### Form entitas = Dialog 480px
Semua entitas (Satuan, Kategori Material, Material, Size, Color, Customer, Process, Vendor, Pekerja) memakai **dialog form satu kolom** di atas daftar. Ini mengonfirmasi keputusan fase 1 sebelumnya (form Material/Customer/Vendor/Pekerja berupa dialog) dan menyederhanakan Tab Manager (tidak ada tab "baru" per entitas). **Pengecualian:** FG = halaman detail bertab. Footer dialog: [Batal] [Simpan] (primary, tanpa ikon).

### Daftar entitas
PageHeader: judul + jumlah, primary `⊕ Tambah {entitas}`; Impor tersedia sebagai tombol default berikon `Impor` bila berhak. `DataList`: pencarian (kode/nama), **chip dengan jumlah: Semua · Aktif · Nonaktif**; Daftar FG menambah chip **Siap Produksi · Belum Siap**. Footer "Menampilkan X dari Y". Status: **Aktif** hijau, **Nonaktif** abu. Tipe proses **Internal/Vendor** = tag netral (garis `--s1`, teks `--fg2`), bukan badge status. Kode terkunci = ikon gembok + tooltip "Sudah dipakai, tidak dapat diubah". Penolakan nonaktif (mis. Process di routing aktif) = `MessageBar` error + daftar pemakai tertaut.

### Detail FG
PageHeader: kode + nama, badge **Siap Produksi** (hijau) / **Belum Siap** (oranye), tombol `Salin dari FG lain` (default) dan `Nonaktifkan` (bahaya). **Tidak ada primary di header**; tiap inner tab punya primary di footer-nya (Simpan routing / Simpan tarif / Simpan). Inner tabs: Umum · Size Berlaku · Routing · Tarif · Kesiapan (state tab dalam per tab).
- **Routing:** urutan dengan tombol ↑ ↓ (drag-drop = fase 2). Tag Internal/Vendor netral.
- **Tarif:** grid input; sel invalid = border + garis bawah merah, latar tint merah, pesan 12px di bawah sel ("Maksimal 5 digit desimal", "Harus lebih dari 0"); sel kosong = oranye + ikon. Format tarif mengikuti FSD (`Rp 1.250,5`). Titik **belum disimpan** pada tab. Pengguna tanpa `MasterData.Tarif.Edit`: hanya baca.
- **Kesiapan:** butir ✓/✗ + teks ("Terpenuhi"/"Belum terpenuhi"); butir gagal memiliki tautan ke tab/sel.

### Wizard
Impor CSV = `Stepper` (Unggah → Periksa → Konfirmasi). Hasil validasi = tabel dengan badge ✓ hijau / ✗ merah per baris. "Salin dari FG lain" (3 langkah): langkah 2 menampilkan pratinjau grid tarif; pada dialog 480px tabel tarif ditampilkan penuh di **Dialog lebar 720px** (U-1 disetujui).


---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Nama komponen mengikuti kontrak Design.md bagian 13 (pemetaan dari nama lama: Fondasi bagian 8). Pustaka UI ditentukan oleh keputusan **K-1** (Fondasi bagian 12); spesifikasi ini tidak bergantung pada pustaka.

- **Komponen umum:** `AppHeader` (pencarian, bahasa, pengguna), `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `MasterList<T>` + `Dialog` form untuk semua entitas selain FG; FG detail dengan inner tabs; grid tarif (`QtyGrid` mode desimal); `ImportWizard` + `Stepper`; `StatusBadge` Aktif/Nonaktif/Siap Produksi/Belum Siap; tag tipe proses netral.
- **Token yang dipakai:** `--brand` (primary, tab aktif), `--s1`/`--fg3` (input), tint merah (sel invalid), `--s3`, `--fg2`, badge `t-ok`/`t-obs`/`t-warn`/`t-err`. Tidak ada hex/px mentah.
- **Deviasi:** dialog Salin dari FG lain menampilkan tabel tarif dalam 480px dengan gulir horizontal (U-1 disetujui: Dialog lebar 720px). Tarif: desimal mengikuti FSD (bukan deviasi).
