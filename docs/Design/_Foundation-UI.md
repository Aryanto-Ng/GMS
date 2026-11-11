# Desain — Fondasi UI GMS (Berlaku untuk Semua Modul)

| Atribut | Nilai |
|---|---|
| Versi | 2.0 (2026-11-11) |
| Penulis | UI/UX Designer |
| Platform | Web (Blazor Web App, **AntDesign Blazor 1.x**, gaya Ant Design Enterprise). Mobile **N/A** pada fase 1. |
| Sumber | `docs/Design.md` (**acuan resmi**: token, komponen, navigasi), `docs/FSD/*.md`, keputusan user 2026-11-08 dan 2026-11-11 |

Dokumen ini menerapkan `docs/Design.md` pada GMS: shell aplikasi, pola halaman, dan komponen yang dipakai ulang. Bila berbeda dengan `Design.md`, **`Design.md` yang berlaku**. Versi 1.0 (Fluent UI + sidebar) tidak berlaku lagi. Dokumen per modul (`docs/Design/<NamaModul>-UI.md`) cukup merujuk pola di sini dan hanya memuat hal yang spesifik.

---

## 1. Vibe

**"Efisien, tenang, terpercaya."** Aplikasi dipakai berjam-jam oleh admin yang mengetik angka, jadi prioritasnya ketepatan, kecepatan, dan tidak melelahkan mata.

| Aspek | Keputusan |
|---|---|
| Karakter | **Campuran**: layar input dan tabel **padat** (Compact), beranda, detail, dan laporan **lapang** (Comfortable) |
| Bahasa visual | **Ant Design Enterprise** lewat AntDesign Blazor: bersih, netral, biru tunggal (`primary`), border tipis, bayangan halus, radius 6–8px, tinggi kontrol 32px. Semua warna/jarak/radius lewat **token** `Design.md` bagian 4 (bukan nilai mentah). |
| Tema | **Terang dan gelap**, mengikuti pengaturan sistem, dengan pengalih manual di menu pengguna (disimpan per pengguna) |
| Hiasan | Minim: tanpa gradien, ilustrasi, emoji, atau animasi masuk. Ikon `@ant-design/icons` (outlined) membantu pemindaian, bukan dekorasi. |
| Bahasa | Bahasa Indonesia. Nada ringkas dan jelas. Pesan galat menjelaskan apa yang terjadi dan apa yang harus dilakukan. |
| Target layar | **≥ 1280px** (desktop). Layout di bawah 1280px **belum dirancang** (Design.md bagian 14) dan tidak boleh dikarang. Ponsel tidak didukung. |

### Kerapatan (Density)
| Mode | Dipakai di | Tinggi baris tabel | Teks |
|---|---|---|---|
| **Compact** | Grid input (output, serah terima, kirim/terima vendor, tarif, PO baris), tabel riwayat | ± 32 px | 13–14 px |
| **Comfortable** | Beranda, form header, detail, dialog | ± 40 px | 14–16 px |

---

## 2. Format Data (Lokal Indonesia)

| Jenis | Format | Contoh |
|---|---|---|
| Tanggal | `dd MMM yyyy` (daftar), `dd/MM/yyyy` (input) | 08 Nov 2026 |
| Tanggal dan jam | `dd MMM yyyy HH:mm` | 08 Nov 2026 14:30 |
| Angka/qty | Pemisah ribuan titik | 12.500 |
| Rupiah | `Rp` + ribuan titik, tanpa desimal untuk total | Rp 1.250.000 |
| Tarif | Desimal koma, hingga 5 digit, tanpa nol ekor | Rp 1.250,5 |
| Persentase | 0 desimal (progres), 1 desimal bila perlu | 87% |
| Qty negatif | Tanda minus dan teks warna peringatan | −20 |

Input angka mengikuti format Indonesia (koma desimal). Input ambigu ditolak dengan petunjuk, tidak dibulatkan diam-diam.

---

## 3. Shell Aplikasi (Design.md bagian 5 dan 7)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ [G] GMS   Beranda  Penjualan▾ Produksi▾ Gudang▾ Penggajian▾ Master Data▾ Administrasi▾ │  header 56px
│                                    [🔍 Cari menu  Ctrl K]   [tema]   [Nama ▾]        │
├──────────────────────────────────────────────────────────────────────────────────────┤
│ Breadcrumb: Penjualan / Customer PO                                                  │
│ Judul Halaman (h1 20px)                       [Aksi sekunder]  [ Aksi utama ]        │  page header
│ subjudul / StatusTag                                                                 │
│ ┌──────────────────────────────────────────────────────────────────────────────────┐ │
│ │ Filter bar: pencarian · filter · reset                                           │ │
│ │ Area konten (tabel / form / tab) dalam kartu                                     │ │
│ └──────────────────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────┘
```
**Tidak ada sider.** Navigasi hanya lewat **MegaMenu** (menjelajah) dan **CommandPalette** (`Ctrl K`, mencari), keduanya dari satu katalog (`NavCatalog.cs`). Dilarang menambah menu samping atau pintasan di luar katalog.

### 3.1 Katalog navigasi (Kategori → Grup → Halaman/Aksi)
| Kategori | Grup | Halaman | Aksi | Izin |
|---|---|---|---|---|
| Beranda | (tautan langsung) | Beranda | | Semua pengguna login |
| **Penjualan** | Customer PO | Daftar PO | Buat PO | `CustomerPO.View` / `.Create` |
| | Pengiriman | Daftar Surat Jalan | Buat Surat Jalan | `Pengiriman.View` / `.Create` |
| **Produksi** | Work Order | Daftar WO | Buat WO | `WorkOrder.View` / `.Create` |
| | Production Output | Riwayat Output | Input Output · Input Penyesuaian | `ProductionOutput.View` / `.Create` / `.Adjust` |
| | Vendor | | Kirim/Terima Vendor | `VendorJob.View` |
| **Gudang** | Finished Goods | Daftar Serah Terima · Stok FG · Kartu Stok | Serah Terima FG | `SerahTerimaFG.*` · `StokFG.View` |
| | Material | Serah Terima Material | Serah Terima Material Baru | `SerahTerimaMaterial.*` |
| | Impor | | Impor Saldo Awal | `StokFG.Import` |
| **Penggajian** | Payroll Borongan | Periode Gaji | | `Payroll.View` |
| **Master Data** | Produk | Finished Goods · Material · Kategori Material | | `MasterData.<Resource>.View` |
| | Atribut | Size · Color · Satuan · Process | | |
| | Mitra & SDM | Customer · Vendor · Pekerja | | |
| | Impor | | Impor FG · Material · Pekerja | `MasterData.<Resource>.Import` |
| **Administrasi** | Hak Akses | Pengguna · Role & Hak Akses · Audit Log | | `AccessManagement.*` |

Aturan: entri tanpa izin **tidak dirender** (di MegaMenu maupun palette); grup dan kategori kosong ikut hilang; kategori berurutan seperti Beranda; ±12 entri per kategori (Master Data 13, toleransi 1). Dari URL langsung tanpa hak tampil halaman 403.

### 3.2 MegaMenu
Kategori sebagai trigger di header, dibuka dengan **klik** (bukan hover), satu panel pada satu waktu. Kolom = grup; halaman (label + deskripsi) di atas, aksi (ikon + `link`) di bawah dipisah garis putus-putus. Kategori aktif: teks `link` tebal + garis bawah 2px `primary`; item aktif: `primary-bg` + `aria-current="page"`. Tutup: Esc, klik scrim, atau klik kategori yang sama.

### 3.3 CommandPalette (`Ctrl K`)
Dialog `combobox` + `listbox`: query kosong menampilkan **Terakhir dibuka** dan **Aksi cepat**; dengan query, semua kata harus cocok ke label, deskripsi, jalur, atau kata kunci; chip Semua · Halaman · Aksi; ↑ ↓ pilih, Enter buka, Esc tutup. Penyaringan terjadi di browser (tanpa SignalR per ketikan).

### 3.4 Header
Kiri: merek "GMS". Tengah: MegaMenu. Kanan: tombol **Cari menu** (`Ctrl K`), pengalih tema (terang/gelap/ikuti sistem, disimpan di browser), dan menu pengguna (nama, role, Ganti Password, Keluar).

### 3.5 Responsif
Hanya **≥ 1280px** yang dirancang. Di bawah itu tata letak dapat menyusut seadanya (teks tombol cari disembunyikan) tetapi **belum menjadi bagian desain**; ajukan lewat `Design.md` bagian 12.

---

## 4. Pola Halaman

### 4.1 Halaman Daftar
1. Page header: judul, jumlah data, tombol aksi utama ("Tambah …") di kanan, hanya bila berhak.
2. Filter bar: kotak pencarian, filter status/tanggal/entitas, tombol Reset. Filter aktif ditampilkan sebagai chip yang dapat dihapus. Filter tersimpan di URL (dapat dibagikan dan tombol Back konsisten).
3. Tabel: header lengket, urut per kolom, paging di server (default 25/halaman), baris dapat diklik atau memakai tombol aksi di kolom terakhir.
4. State: Loading (skeleton baris), Kosong, Error (lihat bagian 7).

### 4.2 Halaman Form
- Satu kolom utama (maksimum ± 720 px untuk form), kelompok field diberi judul seksi.
- Label di atas field, penanda wajib berupa teks "wajib" (bukan hanya asterisk berwarna).
- Validasi inline saat blur dan ringkasan di atas saat Simpan gagal (fokus dipindah ke ringkasan, tiap item menautkan ke field).
- Footer aksi lengket: **Simpan** (utama), **Batal**. Saat menyimpan: tombol dinonaktifkan dengan spinner kecil untuk mencegah dobel kirim.
- **Penjaga perubahan belum tersimpan:** bila ada perubahan, meninggalkan halaman memunculkan konfirmasi.

### 4.3 Halaman Detail
- Header: judul, kode, **chip status**, aksi utama kontekstual (hanya yang diperbolehkan oleh status dan permission).
- Ringkasan kunci dalam kartu (nilai penting di atas, detail di bawah).
- Tab untuk bagian sekunder (Alokasi, WO terkait, Pengiriman, Riwayat/Audit, dst.). Tab aktif tersimpan di URL.

### 4.4 Dialog
| Jenis | Pola |
|---|---|
| Konfirmasi biasa | Judul pertanyaan, penjelasan dampak, tombol [Batal] [Ya, lanjutkan] (`ConfirmDialog` dengan `ShowReason=false`) |
| **Aksi dengan alasan wajib** (Void, Koreksi, Cancel, Reopen, Penyesuaian, Pelepasan alokasi) | Ringkasan dampak (pratinjau), kolom **Alasan** wajib, tombol utama berwarna bahaya untuk aksi destruktif, dinonaktifkan sampai alasan terisi |
| **Pratinjau dampak** (Void/Koreksi) | Tabel kecil sebelum dan sesudah (kumulatif, stok, alokasi). Bila melanggar aturan, pesan penahan ditampilkan dan tombol utama nonaktif |
| Permanen (Tutup periode gaji) | Ringkasan angka, peringatan "Tidak dapat dibuka kembali", konfirmasi dengan mengetik kode periode |

`ConfirmDialog` memakai `<Modal>` AntDesign (Design.md 8.8; nama aksesibel dan Esc dipasang modul JS, Esc diblok saat memproses, klik latar tidak menutup). Fokus berpindah ke dialog saat terbuka, terkurung di dalamnya, Esc menutup (kecuali saat memproses), fokus kembali ke pemicu setelah tutup.

---

## 5. Komponen Khusus

### 5.1 QtyGrid (matriks Color × Size)
Dipakai di PO, WO, Output, Serah Terima, Kirim/Terima Vendor, Pengiriman, Tarif.

```
              S      M      L      XL     2XL   │ Total
Navy        [ 20 ] [ 30 ] [ 10 ] [    ] [    ]  │   60
  maks:       80     75     40    ──     ──      │
Orange      [    ] [ 15 ] [    ] [    ] [    ]  │   15
  maks:       ──     60     ──    ──     ──      │
────────────────────────────────────────────────┼─────
Total          20     45     10      0      0    │   75
```
- Kolom Size mengikuti **Urutan Tampil** master Size. Baris = Color (atau FG + Color).
- Sel numerik bilangan bulat; filter karakter non-angka; sel yang tidak berlaku dinonaktifkan dan diberi tanda "—".
- Hint **"maks N"** di bawah sel menampilkan batas yang tersedia (sesuai FSD). Melebihi batas: sel bergaris merah, ikon dan teks pesan di bawah, tombol Simpan tetap bisa ditekan tetapi menampilkan ringkasan galat.
- **Keyboard:** Tab/Shift+Tab antar sel, Enter pindah ke bawah, panah ←→↑↓, Home/End, Ctrl+V menempel blok dari Excel.
- Total baris dan kolom dihitung langsung. Kolom pertama dan header tetap saat menggulir.
- Sel peringatan (melebihi Qty WO pada proses pertama) berwarna kuning dengan ikon, bukan hanya warna.

### 5.2 StatusTag
Selalu **ikon + teks** (tidak hanya warna); pemetaan tunggal di `StatusPresentation.cs`, nada dari `Design.md` 4.1.

| Status | Nada | Ikon |
|---|---|---|
| Draft | netral | edit |
| Confirmed | cyan | flag |
| Released | cyan | send |
| In Progress | biru | sync |
| Completed | hijau | check-circle |
| Closed (WO, Periode Gaji) | netral kuat | lock |
| Cancelled | merah | stop |
| Voided | netral, teks dicoret | minus-circle |
| Open (Periode Gaji) | biru | unlock |
| Aktif / Nonaktif | hijau / netral | check / minus-circle |
| Terlambat | merah | clock-circle |
| Belum Final | oranye | warning |
| Siap Produksi / Belum Siap | hijau / oranye (alasan di tooltip dan daftar) | check-circle / warning |
| Terkunci (akun) | merah | lock |

### 5.3 StatCard (Beranda dan ringkasan)
Angka besar (`stat-value`), label, deskripsi satu baris, dan tautan "Lihat". Tanpa penanda garis kiri; peringatan/bahaya = ikon + teks. Angka dapat diklik menuju daftar terfilter.

### 5.4 Matriks Progres (Detail WO)
Baris = langkah routing (urut), kolom = Color × Size. Sel menampilkan `output/Qty` dengan bar tipis. Langkah vendor menampilkan tiga angka (Terkirim, Diterima, Sisa). Penanda: Belum Mulai, Berjalan, Selesai.

---

## 6. Umpan Balik Sistem

| Peristiwa | Pola |
|---|---|
| Berhasil menyimpan | Toast sukses (`IGmsToast`, pesan AntDesign) 4 detik di bagian atas, menyebut dampaknya |
| Galat validasi | Inline per field + ringkasan di atas form |
| Galat server / bisnis | Banner di atas konten dengan pesan dan langkah solusi. Tidak hilang otomatis |
| Aksi berisiko | Dialog konfirmasi (bagian 4.4) |
| Proses lama (> 1 detik) | Spinner pada tombol dan area, area diberi `aria-busy` |
| Koneksi Blazor putus | Banner "Menyambung ulang…". Bila gagal, tombol "Muat ulang". Input yang belum tersimpan diperingatkan |

---

## 7. State UI (Wajib untuk Setiap Halaman)

| State | Pola |
|---|---|
| **Loading awal** | **Skeleton** yang meniru bentuk konten (baris tabel, kartu). Bukan spinner penuh halaman |
| **Loading ulang/refresh** | Konten lama tetap terlihat, indikator tipis di atas. Tombol terkait nonaktif |
| **Kosong (belum ada data)** | Ikon sederhana, judul, penjelasan, dan **tombol aksi** bila pengguna berhak ("Tambah PO pertama") |
| **Kosong (hasil filter)** | "Tidak ada hasil untuk filter ini" dengan tombol Reset filter |
| **Error (Problem Details)** | Judul, penjelasan manusiawi, **ID rujukan** (`traceId`) untuk dilaporkan, tombol **Coba lagi**. Detail teknis tidak ditampilkan |

### Pemetaan Status HTTP / Problem Details
| Kode | Tampilan |
|---|---|
| 400/422 | Galat validasi: tandai field dan ringkasan |
| 401 | Dialihkan ke login dengan `returnUrl` |
| 403 | Halaman "Akses ditolak" (Static SSR) |
| 404 | "Data tidak ditemukan" dengan tautan kembali ke daftar |
| 409 (konkurensi) | Banner "Data telah diubah pengguna lain" dengan tombol **Muat ulang** (input pengguna dipertahankan bila memungkinkan) |
| 409 (aturan bisnis, misal aturan kumulatif) | Banner/dialog menjelaskan **penahan** (sel, qty, dokumen) |
| 5xx | Error umum dengan traceId dan Coba lagi |

---

## 8. Aksesibilitas (a11y) — Target WCAG 2.1 AA

- **Kontras:** teks ≥ 4,5:1, komponen UI ≥ 3:1, pada tema terang dan gelap.
- **Keyboard penuh:** semua fungsi dapat dijangkau tanpa mouse. Urutan fokus logis. Indikator fokus selalu terlihat. Tautan lompat "Lewati ke konten utama".
- **Bukan hanya warna:** status, galat, dan peringatan memakai ikon dan teks.
- **Label dan nama:** setiap input punya label terhubung. Tombol ikon punya nama aksesibel.
- **Tabel:** `caption`, header kolom `scope`. Grid matriks memberi nama baris dan kolom pada tiap sel ("Navy, Size M").
- **Pembaca layar:** toast, ringkasan galat, dan hasil pencarian memakai `aria-live`. Dialog memakai `role="dialog"` dan `aria-modal`.
- **Target klik:** minimal 24×24 px (WCAG 2.2 AA) dan ideal 32 px pada mode Compact.
- **Zoom:** tata letak tetap dapat dipakai pada zoom 200%. Hormati `prefers-reduced-motion`.
- **Bahasa:** `lang="id"`.

---

## 9. Konsep Dokumen Cetak (PDF)

Slip Gaji dan Surat Jalan adalah PDF A4, bukan halaman web. Tata letaknya didefinisikan pada dokumen modul masing-masing. Prinsip bersama: font berlisensi terbuka, kop dari konfigurasi, tabel rapat dan terbaca saat dicetak hitam-putih, penanda "BELUM FINAL"/"DIBATALKAN"/"CETAK ULANG" sebagai watermark teks.

---

## 10. Komponen (pemetaan Design.md → implementasi)

- Library: **AntDesign Blazor 1.6.2** (MIT, disetujui user 2026-11-11), versi di `src/Directory.Packages.props`. Tidak ada library UI berbayar; Fluent UI sudah dihapus.
- Token: `wwwroot/css/tokens.css` (terang/gelap), diterapkan ke komponen Ant di `wwwroot/css/ant-theme.css`; komponen bersama di `wwwroot/app.css`.
- Komponen: `StatusTag`, `StatCard`/`DashboardCard`, `ConfirmDialog` (+ `ImpactPreviewTable`), `QtyGrid`, `TextField`/`LookupSelect` (FormField), `ProblemBanner` (Alert), `EmptyState`/`ErrorState`/`SkeletonRows`, `PagerBar` + tabel `gms-table` (DataTable), `ProgressMatrix`/`StackedProgress`, `ImportWizard`, `MegaMenu` + CommandPalette (kustom). Rincian dan deviasi: `docs/Frontend/README.md`.
