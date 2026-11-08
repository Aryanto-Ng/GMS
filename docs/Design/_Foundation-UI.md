# Desain — Fondasi UI GMS (Berlaku untuk Semua Modul)

| Atribut | Nilai |
|---|---|
| Versi | 1.0 |
| Penulis | UI/UX Designer |
| Platform | Web (Blazor Web App, Microsoft Fluent UI 2). Mobile **N/A** pada fase 1. |
| Sumber | `docs/FSD/*.md`, keputusan user 2026-11-08 |

Dokumen ini mendefinisikan "vibe", shell aplikasi, dan pola yang dipakai ulang. Dokumen per modul (`docs/Design/<NamaModul>-UI.md`) cukup merujuk pola di sini dan hanya memuat hal yang spesifik.

---

## 1. Vibe

**"Efisien, tenang, terpercaya."** Aplikasi dipakai berjam-jam oleh admin yang mengetik angka, jadi prioritasnya ketepatan, kecepatan, dan tidak melelahkan mata.

| Aspek | Keputusan |
|---|---|
| Karakter | **Campuran**: layar input dan tabel **padat** (Compact), beranda, detail, dan laporan **lapang** (Comfortable) |
| Bahasa visual | Microsoft Fluent UI 2 apa adanya (tanpa kustomisasi tema berat). Warna merek = biru Fluent default dengan aksen netral. |
| Tema | **Terang dan gelap**, mengikuti pengaturan sistem, dengan pengalih manual di menu pengguna (disimpan per pengguna) |
| Hiasan | Minim. Tanpa ilustrasi besar. Ikon Fluent untuk membantu pemindaian, bukan dekorasi. |
| Bahasa | Bahasa Indonesia. Nada ringkas dan jelas. Pesan galat menjelaskan apa yang terjadi dan apa yang harus dilakukan. |
| Target layar | **1366×768 ke atas** sebagai prioritas. Tablet (≥ 1024 lebar) tidak boleh rusak. Ponsel tidak didukung. |

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

## 3. Shell Aplikasi

```
┌────────────────────────────────────────────────────────────────────────────┐
│ ☰  GMS · Garment Management System                      🌓  👤 Nama ▾     │  ← Top bar (48 px)
├──────────────┬─────────────────────────────────────────────────────────────┤
│ 🏠 Beranda   │ Breadcrumb: Penjualan › Customer PO › PO-202611-0001        │
│              │ ┌─────────────────────────────────────────────────────────┐ │
│ PENJUALAN    │ │ Judul Halaman                       [Aksi sekunder][Aksi │ │  ← Page header
│  Customer PO │ │ Subjudul/status chip                          utama]     │ │
│  Work Order  │ └─────────────────────────────────────────────────────────┘ │
│  Pengiriman  │ [Filter bar: pencarian · filter · reset]                    │
│ GUDANG FG    │ ┌─────────────────────────────────────────────────────────┐ │
│  Stok FG     │ │                                                         │ │
│  Serah Terima│ │              Area konten (tabel / form / tab)           │ │
│ PRODUKSI     │ │                                                         │ │
│  Input Output│ └─────────────────────────────────────────────────────────┘ │
│  Vendor      │ [Footer aksi lengket pada form: Batal · Simpan]            │
│  Riwayat     │                                                             │
│ PENGGAJIAN   │                                                             │
│  Periode Gaji│                                                             │
│ MASTER DATA  │                                                             │
│ ADMINISTRASI │                                                             │
└──────────────┴─────────────────────────────────────────────────────────────┘
```

### 3.1 Navigasi (dikelompokkan per fungsi)
| Grup | Item | Syarat tampil |
|---|---|---|
| (tanpa grup) | Beranda | Semua pengguna login |
| **Penjualan** | Customer PO · Work Order · Pengiriman | `View` masing-masing |
| **Gudang FG** | Stok FG · Serah Terima FG · Serah Terima Material | `View` masing-masing |
| **Produksi** | Input Output · Kirim/Terima Vendor · Riwayat Produksi | `ProductionOutput.View` · `VendorJob.View` |
| **Penggajian** | Periode Gaji | `Payroll.View` |
| **Master Data** | Customer · FG · Material · Kategori · Satuan · Size · Color · Process · Vendor · Pekerja | `MasterData.<Resource>.View` |
| **Administrasi** | Pengguna · Role & Hak Akses · Audit Log | `AccessManagement.*` (Administrator) |

Aturan:
- Item tanpa permission `View` **disembunyikan**. Grup tanpa item terlihat ikut disembunyikan.
- Grup Master Data dilipat secara default dan membuka grup terakhir yang dipakai.
- Item aktif diberi penanda ganda (latar + garis kiri), bukan warna saja.
- Dari URL langsung tanpa hak, tampil halaman 403 (bukan sekadar menu hilang).

### 3.2 Responsif
| Lebar | Perilaku |
|---|---|
| ≥ 1366 | Nav kiri terbuka (± 260 px), konten penuh |
| 1024–1365 | Nav menjadi **rail ikon** (± 56 px), label muncul saat hover/fokus atau saat diperluas |
| < 1024 | Nav menjadi **drawer** (overlay) dibuka tombol ☰. Tabel lebar menggulir horizontal dengan kolom kunci tetap |

### 3.3 Top bar
- Kiri: ☰ (nav) dan nama aplikasi.
- Kanan: pengalih tema (terang/gelap/ikuti sistem) dan menu pengguna (nama, role, Ganti Password, Keluar).

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
| Konfirmasi biasa | Judul pertanyaan, penjelasan dampak, tombol [Batal] [Ya, lanjutkan] |
| **Aksi dengan alasan wajib** (Void, Koreksi, Cancel, Reopen, Penyesuaian, Pelepasan alokasi) | Ringkasan dampak (pratinjau), kolom **Alasan** wajib, tombol utama berwarna bahaya untuk aksi destruktif, dinonaktifkan sampai alasan terisi |
| **Pratinjau dampak** (Void/Koreksi) | Tabel kecil sebelum dan sesudah (kumulatif, stok, alokasi). Bila melanggar aturan, pesan penahan ditampilkan dan tombol utama nonaktif |
| Permanen (Tutup periode gaji) | Ringkasan angka, peringatan "Tidak dapat dibuka kembali", konfirmasi dengan mengetik kode periode |

Fokus berpindah ke dialog saat terbuka, terkurung di dalamnya, Esc menutup (kecuali saat memproses), fokus kembali ke pemicu setelah tutup.

---

## 5. Komponen Khusus

### 5.1 Grid Matriks Ukuran (Color × Size)
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
- Baris kecil **"maks"** di bawah sel menampilkan batas yang tersedia (sesuai FSD). Melebihi batas: sel bergaris merah, ikon dan teks pesan di bawah, tombol Simpan tetap bisa ditekan tetapi menampilkan ringkasan galat.
- **Keyboard:** Tab/Shift+Tab antar sel, Enter pindah ke bawah, panah ←→↑↓, Home/End, Ctrl+V menempel blok dari Excel.
- Total baris dan kolom dihitung langsung. Kolom pertama dan header tetap saat menggulir.
- Sel peringatan (melebihi Qty WO pada proses pertama) berwarna kuning dengan ikon, bukan hanya warna.

### 5.2 Chip Status
Selalu **ikon + teks** (tidak hanya warna).

| Status | Tampilan |
|---|---|
| Draft | Netral, ikon pensil |
| Confirmed / Released | Informasi (biru), ikon centang |
| In Progress | Merek (biru solid), ikon progres |
| Completed | Sukses (hijau), ikon centang ganda |
| Closed | Netral gelap, ikon gembok |
| Cancelled | Bahaya lembut, ikon batal |
| Voided | Teks dicoret, netral, ikon batal |
| Terlambat | Bahaya, ikon jam |
| BELUM FINAL | Peringatan (kuning), ikon info |
| Open / Closed (periode) | Informasi / netral gelap |
| Siap Produksi / Belum Siap | Sukses / Peringatan, dengan alasan pada tooltip dan daftar |

### 5.3 Kartu Metrik (Beranda dan ringkasan)
Angka besar, label, deskripsi satu baris, dan tautan "Lihat". Angka dan status tidak dibedakan oleh warna saja.

### 5.4 Matriks Progres (Detail WO)
Baris = langkah routing (urut), kolom = Color × Size. Sel menampilkan `output/Qty` dengan bar tipis. Langkah vendor menampilkan tiga angka (Terkirim, Diterima, Sisa). Penanda: Belum Mulai, Berjalan, Selesai.

---

## 6. Umpan Balik Sistem

| Peristiwa | Pola |
|---|---|
| Berhasil menyimpan | Toast sukses 4 detik di kanan atas, dapat ditutup, `aria-live="polite"` |
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

## 10. Komponen yang Dibutuhkan (Catatan untuk Frontend/Architect)

- Library komponen: **Blazor Fluent UI resmi Microsoft** (`Microsoft.FluentUI.AspNetCore.Components`, open source, MIT). **Wajib minta izin user sebelum ditambahkan** dan daftarkan versinya di `src/Directory.Packages.props`. Verifikasi dulu kecocokan versi dengan .NET yang dipakai.
- Komponen kustom yang dibangun di atas Fluent: **Grid Matriks Ukuran**, **Matriks Progres**, **Dialog Alasan + Pratinjau Dampak**, **Kartu Metrik**, **Chip Status**.
- Tidak ada library UI berbayar.
