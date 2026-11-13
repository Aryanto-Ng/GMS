# DESIGN.md — Enterprise Design System

**Master Project Template** · Blazor Server (.NET) · AntDesign Blazor · Bahasa Indonesia
Versi 1.0 · Sumber kebenaran visual dan interaksi untuk seluruh UI aplikasi.

> Referensi visual yang dapat dibuka: artifact Design System (token, 13 komponen, preview terang/gelap). Bila dokumen ini dan artifact berbeda, **dokumen ini yang berlaku** dan artifact wajib diperbarui.

---

## 0. Aturan wajib untuk agent (baca dulu)

Dokumen ini mengikat semua agent yang menghasilkan, mengubah, atau me-review UI: UI/UX Designer, Frontend/Blazor Engineer, QA, Tech Writer.

1. **Wajib membaca DESIGN.md sebelum merancang atau menulis UI apa pun.** Tidak ada layar, komponen, atau CSS yang dibuat tanpa merujuk dokumen ini.
2. **Pakai token, bukan nilai mentah.** Dilarang menulis hex, px, atau bayangan literal di luar bagian Token. Tulis `primary`, `space-4`, `radius-md`. Di CSS: `var(--primary)`.
3. **Pakai komponen yang ada (bagian 8).** Dilarang membuat varian tombol, tag status, tabel, atau dialog baru bila komponen yang ada bisa dipakai. Butuh komponen baru: ikuti bagian 12 (Proses perubahan), jangan membuatnya diam-diam.
4. **Status wajib ikon + teks** lewat `StatusTag`. Dilarang membedakan status hanya dengan warna.
5. **Navigasi hanya lewat MegaMenu + CommandPalette** (bagian 7). Dilarang menambah sider, menu samping, atau pintasan di luar katalog navigasi.
6. **Aksi yang membalik data wajib `ConfirmDialog`** dengan ringkasan, dampak, dan alasan bila wajib.
7. **Yang tidak berhak, tidak dirender.** Menu, widget, tombol tanpa permission disembunyikan, bukan dinonaktifkan. Server tetap memeriksa permission; UI bukan pengaman.
8. **Semua teks UI Bahasa Indonesia** mengikuti bagian 3, kecuali nama status dokumen yang tetap Inggris.
9. **Dua tema.** Setiap layar harus benar di Light dan Dark. Kontras minimum di bagian 9.
10. **Output desain wajib menyebut token dan komponen yang dipakai** (daftar singkat di akhir spesifikasi layar). Deviasi dari dokumen ini harus ditandai eksplisit dengan alasan; reviewer menolak deviasi tanpa alasan.

Checklist cepat sebelum menyerahkan UI: bagian 13.

---

## 1. Prinsip

- **Data dulu, dekorasi belakangan.** Permukaan netral, satu warna aksen (`primary`). Warna lain hanya untuk status.
- **Padat tapi terbaca.** Input volume tinggi di jaringan internal. Prioritaskan kecepatan input dan kejelasan status.
- **Setiap angka dapat ditindaklanjuti.** Angka ringkasan adalah tautan ke daftar yang sudah terfilter.
- **Tampilkan dampak sebelum menyimpan.**
- **Server sebagai pemegang aturan.** Validasi klien hanya bantuan; invariant dicek server pada kondisi akhir transaksi.
- **Tanpa dekorasi.** Tidak ada gradien, ilustrasi, emoji, kartu dengan aksen garis kiri, atau animasi masuk.

---

## 2. Gaya visual

Mengikuti **Ant Design Enterprise**: bersih, netral, biru tunggal, border tipis, bayangan halus, sudut membulat sedang, tinggi kontrol 32px.

- Halaman di `bg-layout`; kartu, tabel, header, form di `bg-container`; modal, dropdown, MegaMenu, CommandPalette di `bg-elevated`.
- Hampir datar: kartu = `shadow-card` + border `border-secondary`; lapisan mengambang = `shadow-popup`. Tidak ada bayangan lain.
- Pemisah antar baris dan header kartu memakai `border-secondary`, bukan bayangan.
- Hover: tombol default dan input berganti border/teks ke `link`; baris tabel dan item menu ke `fill-hover`.
- Fokus: cincin solid 2px `focus-ring`, offset 1px, pada semua kontrol. Dilarang `outline: none` tanpa pengganti.
- Loading: skeleton `fill-hover` per kartu; tombol `loading` (spinner, nonaktif, `aria-busy`). Hormati `prefers-reduced-motion`.
- Motion: transisi warna 0.2s saja.

### Deviasi sengaja dari Ant Design default (aksesibilitas)

| Token | Ant default | Enterprise | Alasan |
|---|---|---|---|
| `primary` | `#1677ff` | `#1668dc` | Label putih 14px: 4.1:1 → 5.2:1 |
| `border-control` | `#d9d9d9` | `#8c8c8c` | Border input 1.4:1 → 3.4:1 (WCAG 1.4.11) |
| `danger` (fill) | `#ff4d4f` | `#d9363e` | Label putih 3.3:1 → 4.6:1 |
| `text-tertiary` | `rgba(0,0,0,.45)` | `#6b6b6b` | 3.4:1 → 5.3:1 |

Agent dilarang "mengembalikan" nilai ini ke default Ant.

---

## 3. Konten dan bahasa

- UI **Bahasa Indonesia**, sentence case: "Buat Dokumen", "Tidak ada data terlambat".
- **Nama status dokumen tetap Inggris** (sesuai FSD): Draft, Confirmed, Released, In Progress, Completed, Closed, Cancelled, Open. Kondisi turunan Indonesia: Terlambat, Jatuh tempo, Belum final, Terkunci.
- **Tombol = kata kerja + objek**: "Simpan Draft". Dilarang "OK", "Submit", "Yes/No".
- **Pesan error menyebut penyebab dan letaknya**: "Melebihi batas tersedia: Kategori / Ukuran (maks 240)".
- **Empty state informatif**, bukan "No data": "Tidak ada dokumen di vendor".
- Tanpa emoji. Kata ganti "Anda" hanya bila perlu.

**Format**

| Data | Format | Contoh |
|---|---|---|
| Angka | `id-ID`, titik ribuan | `1.240` |
| Uang | `Rp` + angka, tanpa desimal | `Rp 18.420.500` |
| Tanggal (tabel/teks) | `D MMM YYYY` | `14 Nov 2026` |
| Tanggal (input) | `DD/MM/YYYY` | `14/11/2026` |
| Waktu | `HH:mm`, zona aplikasi | `09:42` |
| Satuan qty | `pcs` | `1.240 pcs` |
| Nomor dokumen | gaya `doc-number` (mono) | `DOC-2026-0142` |

---

## 4. Token

### 4.1 Warna

| Token | Light | Dark | Dipakai untuk |
|---|---|---|---|
| `primary` | `#1668dc` | `#1668dc` | Fill tombol primary, garis bawah kategori aktif MegaMenu, bar progres normal. |
| `primary-hover` | `#0958d9` | `#1554ad` | Hover tombol primary. |
| `primary-active` | `#003eb3` | `#15417e` | Tombol primary saat ditekan. |
| `primary-bg` | `#e6f4ff` | `#111a2c` | Latar item MegaMenu aktif, baris CommandPalette terpilih, tag biru. |
| `primary-border` | `#91caff` | `#15325b` | Border tag biru dan alert info. |
| `on-primary` | `#ffffff` | `#ffffff` | Teks dan ikon di atas primary, primary-hover, danger. |
| `link` | `#1668dc` | `#3c89e8` | Tautan, angka yang dapat diklik di Beranda, teks tag biru. |
| `focus-ring` | `#1668dc` | `#3c89e8` | Cincin fokus solid 2px pada semua kontrol. |
| `text` | `#1f1f1f` | `#dbdbdb` | Teks utama di bg-container, bg-layout, bg-elevated. |
| `text-secondary` | `#595959` | `#a6a6a6` | Label kolom, metadata, deskripsi. |
| `text-tertiary` | `#6b6b6b` | `#8c8c8c` | Keterangan kecil, hint 'maks N'. |
| `text-disabled` | `#bfbfbf` | `#5a5a5a` | Teks nonaktif dan placeholder saja. |
| `border` | `#d9d9d9` | `#424242` | Border dekoratif: kartu, tabel, modal. |
| `border-secondary` | `#f0f0f0` | `#303030` | Garis baris tabel, pemisah header kartu. |
| `border-control` | `#8c8c8c` | `#6b6b6b` | Border Input, Select, tombol default. |
| `bg-layout` | `#f5f5f5` | `#000000` | Latar halaman di belakang kartu. |
| `bg-container` | `#ffffff` | `#141414` | Kartu, tabel, header, form. |
| `bg-elevated` | `#ffffff` | `#1f1f1f` | Modal, dropdown, popover. |
| `fill-alter` | `#fafafa` | `#1d1d1d` | Header tabel, tag netral. |
| `fill-hover` | `#f5f5f5` | `#262626` | Hover baris tabel dan item menu; skeleton. |
| `mask` | `rgba(0,0,0,0.45)` | `rgba(0,0,0,0.65)` | Overlay di belakang modal; MegaMenu memakai mask 35%. |
| `success` | `#52c41a` | `#49aa19` | Ikon dan bar progres sukses. |
| `success-text` | `#237804` | `#6abe39` | Teks tag Completed/Aktif. |
| `success-bg` | `#f6ffed` | `#162312` | Latar tag dan alert sukses. |
| `success-border` | `#b7eb8f` | `#274916` | Border tag dan alert sukses. |
| `warning` | `#faad14` | `#d89614` | Ikon peringatan dan bar progres peringatan. |
| `warning-text` | `#874d00` | `#e8b339` | Teks tag peringatan (Jatuh tempo/Belum final). |
| `warning-bg` | `#fffbe6` | `#2b2111` | Latar tag dan alert peringatan. |
| `warning-border` | `#ffe58f` | `#594214` | Border tag dan alert peringatan. |
| `danger` | `#d9363e` | `#c8353a` | Fill tombol danger. |
| `danger-text` | `#cf1322` | `#e86e6b` | Teks error validasi, tag Terlambat/Cancelled. |
| `danger-bg` | `#fff1f0` | `#2c1618` | Latar tag dan alert error. |
| `danger-border` | `#ffccc7` | `#5b2526` | Border tag dan alert error. |
| `cyan-text` | `#006d75` | `#33bcb7` | Teks tag Confirmed/Released. |
| `cyan-bg` | `#e6fffb` | `#112123` | Latar tag Confirmed/Released. |
| `cyan-border` | `#87e8de` | `#144848` | Border tag Confirmed/Released. |

### 4.2 Tipografi

Keluarga: `sans` = font sistem; `mono` = font monospace. Ukuran dasar 14px.

| Style | Family | Size | Weight | Dipakai untuk |
|---|---|---|---|---|
| `h1`-`h3` | sans | 24-38px| 600 | Jarang dipakai; judul besar. |
| `h4` | sans | 20px | 600 | Judul halaman (Daftar, Detail). |
| `h5` | sans | 16px | 600 | Judul kartu, modal, form. |
| `body-lg` | sans | 16px | 400 | Deskripsi Result/Empty besar. |
| `body` | sans | 14px | 400 | Default semua teks UI. |
| `body-strong` | sans | 14px | 600 | Header kolom tabel. |
| `caption` | sans | 12px | 400 | Hint, metadata, label tag. |
| `stat-value` | sans | 24px | 600 | Angka utama StatCard. tabular-nums. |
| `doc-number` | mono | 13px | 500 | Nomor Dokumen (tabular-nums). |

### 4.3 Jarak (grid 4px)

| Token | Nilai | Dipakai untuk |
|---|---|---|
| `space-1` | `4px` | Jarak ikon-teks. |
| `space-2` | `8px` | Gap antar tombol. |
| `space-3` | `12px` | Padding sel tabel (vertikal), gap form inline. |
| `space-4` | `16px` | Padding sel tabel (horizontal), gap grid kartu. |
| `space-5` | `20px` | Padding body modal. |
| `space-6` | `24px` | Padding kartu dan konten halaman. |
| `space-8` | `32px` | Jarak antar seksi halaman. |
| `space-12` | `48px` | Padding state Empty/Result. |

### 4.4 Radius & 4.5 Ukuran & 4.6 Z-index

- **Radius:** `radius-sm` (4px: tag, menu), `radius-md` (6px: kontrol form, tombol), `radius-lg` (8px: kartu, modal).
- **Tinggi Kontrol:** `control-height` (32px), `header-height` (56px).
- **Z-Index:** header (100) < MegaMenu (900) < modal (1000) < CommandPalette (1050).

---

## 5. Layout halaman

- **Tidak ada sider.** Seluruh lebar untuk konten.
- Lebar minimum yang didukung: **1280px** (aplikasi desktop).
- Header halaman: breadcrumb → judul `h4` di kiri + aksi di kanan. Maksimal 3 tombol aksi.
- Beranda: grid `repeat(auto-fill, minmax(280px, 1fr))`, gap `space-4`.

---

## 6. Ikonografi

- Gunakan **`@ant-design/icons`** (outlined, 14–16px, `currentColor`) lewat AntDesign Blazor.
- Contoh: CheckOutlined (sukses), ClockCircleOutlined (terlambat), WarningOutlined (peringatan), CloseCircleOutlined (error).

---

## 7. Navigasi: MegaMenu + CommandPalette

- **Navigasi HANYA lewat 1 katalog terpusat.**
- **MegaMenu:** Di header; dibuka klik, bukan hover. Kategori -> Grup -> Halaman/Aksi.
- **CommandPalette (`Ctrl K`):** Pencarian cepat menu (semua kata dicocokkan ke label/deskripsi).
- Tidak boleh ada rute yang tidak dirender/ditolak aksesnya di UI (Server tetap jaga).

---

## 8. Komponen Utama

### 8.1 Button
`type` (`primary` | `default` | `dashed` | `text` | `link`), `danger`, `size`, `loading`.
- `primary` untuk aksi utama; `danger` untuk aksi yang membalik data (via ConfirmDialog).

### 8.2 StatusTag
`status`, `icon`. Selalu ikon + teks.
Nada: Netral (Draft/Void), Cyan (Confirmed/Released), Biru (In Progress/Open), Hijau (Completed/Aktif), Merah (Cancelled/Error).

### 8.3 StatCard & 8.4 DataTable & 8.5 QtyGrid
- StatCard: Loading (skeleton), Error, Empty ditangani per kartu.
- DataTable: Server-side paging, toolbar (cari, filter, tombol primary kanan).

### 8.6 FormField, Input, Select, DatePicker
- Label di atas kontrol. Error menggantikan help. DateFormat `DD/MM/YYYY`.

### 8.8 ConfirmDialog
- **Wajib** untuk Void/Koreksi/Cancel. Menyebut dampak dan alasan bila perlu.

---

## 9. Aksesibilitas (wajib)

- **Kontras WCAG 2.1 AA** di Light dan Dark.
- Status dan error **tidak boleh hanya warna**: selalu ikon + teks.
- Semua kontrol dapat dioperasikan dengan keyboard.

---

## 10. Pola layar standar

| Pola | Susunan |
|---|---|
| **Daftar** | breadcrumb → judul + aksi → toolbar → tabel → paging |
| **Form** | judul + aksi → seksi header vertikal → grid/form item → total |
| **Detail** | judul + status + aksi → Tabs → isi detail |

---

## 11. Pemetaan ke AntDesign Blazor
(Sesuai implementasi teknis AntDesign Blazor). Komponen `MegaMenu` dan `CommandPalette` diimplementasikan secara kustom.
