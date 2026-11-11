# Design.md — Enterprise Design System

**Master Project Template** · Blazor Server (.NET) · AntDesign Blazor · Bahasa Indonesia
Versi 1.1 · Sumber kebenaran visual dan interaksi untuk seluruh UI aplikasi. Dokumen ini **berdiri sendiri**: seluruh token, komponen, dan aturan ada di sini, tidak bergantung pada berkas luar.

> Bila ada artifact atau prototipe visual (token, komponen, preview terang/gelap), itu hanya **referensi**. Bila artifact dan dokumen ini berbeda, **dokumen ini yang berlaku** dan artifact wajib diperbarui.

---

## 0. Aturan wajib untuk agent (baca dulu)

Dokumen ini mengikat semua agent yang menghasilkan, mengubah, atau me-review UI: UI/UX Designer, Frontend/Blazor Engineer, QA, Tech Writer.

1. **Wajib membaca Design.md sebelum merancang atau menulis UI apa pun.** Tidak ada layar, komponen, atau CSS yang dibuat tanpa merujuk dokumen ini.
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
- **Tampilkan dampak sebelum menyimpan.** Void, Koreksi, Penyesuaian, Cancel, Close.
- **Server sebagai pemegang aturan.** Validasi klien hanya bantuan; invariant dicek server pada kondisi akhir transaksi.
- **Tanpa dekorasi.** Tidak ada gradien, ilustrasi, emoji, kartu dengan aksen garis kiri, atau animasi masuk.

---

## 2. Gaya visual

Mengikuti **Ant Design Enterprise**: bersih, netral, biru tunggal, border tipis, bayangan halus, sudut membulat sedang, tinggi kontrol 32px.

- Halaman di `bg-layout`; kartu, tabel, header, form di `bg-container`; modal, dropdown, MegaMenu, CommandPalette di `bg-elevated`.
- Hampir datar: kartu = `shadow-card` + border `border-secondary`; lapisan mengambang = `shadow-popup` (nilai di 4.7). Tidak ada bayangan lain.
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
- **Empty state informatif**, bukan "No data": "Tidak ada dokumen di vendor". Instalasi baru: arahkan ke langkah pertama ("Mulai dari Master Data").
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

"Hari ini" dan "7 hari" mengikuti zona waktu aplikasi, bukan zona browser.

---

## 4. Token

Nama token adalah kontrak; nilai hanya boleh berubah lewat bagian 12.

### 4.1 Warna

| Token | Light | Dark | Dipakai untuk |
|---|---|---|---|
| `primary` | `#1668dc` | `#1668dc` | Fill tombol primary, garis bawah kategori aktif MegaMenu, tab aktif, bar progres normal. |
| `primary-hover` | `#0958d9` | `#1554ad` | Hover tombol primary. |
| `primary-active` | `#003eb3` | `#15417e` | Tombol primary saat ditekan. |
| `primary-bg` | `#e6f4ff` | `#111a2c` | Latar item MegaMenu aktif, baris CommandPalette terpilih, tag biru, chip filter aktif. |
| `primary-bg-hover` | `#bae0ff` | `#112545` | Hover pada primary-bg. |
| `primary-border` | `#91caff` | `#15325b` | Border tag biru dan alert info. |
| `on-primary` | `#ffffff` | `#ffffff` | Teks dan ikon di atas primary, primary-hover, danger. |
| `link` | `#1668dc` | `#3c89e8` | Tautan, angka yang dapat diklik di Beranda, teks tag biru. |
| `focus-ring` | `#1668dc` | `#3c89e8` | Cincin fokus solid 2px pada semua kontrol; 3:1 lebih di semua permukaan. |
| `text` | `#1f1f1f` | `#dbdbdb` | Teks utama di bg-container, bg-layout, bg-elevated, fill-alter. |
| `text-secondary` | `#595959` | `#a6a6a6` | Label kolom, metadata, deskripsi. Di semua permukaan. |
| `text-tertiary` | `#6b6b6b` | `#8c8c8c` | Keterangan kecil, satuan, hint "maks N". Di bg-container dan fill-alter. |
| `text-disabled` | `#bfbfbf` | `#5a5a5a` | Teks nonaktif dan placeholder saja (dikecualikan dari aturan kontras). |
| `border` | `#d9d9d9` | `#424242` | Border dekoratif: kartu, tabel, modal. Bukan untuk border kontrol. |
| `border-secondary` | `#f0f0f0` | `#303030` | Garis baris tabel, pemisah header kartu, latar tag Closed. |
| `border-control` | `#8c8c8c` | `#6b6b6b` | Border Input, Select, sel QtyGrid, tombol default. 3:1 di bg-container. |
| `bg-layout` | `#f5f5f5` | `#000000` | Latar halaman di belakang kartu. |
| `bg-container` | `#ffffff` | `#141414` | Kartu, tabel, header, form. |
| `bg-elevated` | `#ffffff` | `#1f1f1f` | Modal, dropdown, popover. |
| `fill-alter` | `#fafafa` | `#1d1d1d` | Header tabel, baris total QtyGrid, tag netral. |
| `fill-hover` | `#f5f5f5` | `#262626` | Hover baris tabel dan item menu; track progres; skeleton. |
| `mask` | `rgba(0,0,0,0.45)` | `rgba(0,0,0,0.65)` | Overlay di belakang modal dan CommandPalette; MegaMenu memakai mask 35%. |
| `success` | `#52c41a` | `#49aa19` | Ikon dan bar progres sukses. Bukan untuk teks. |
| `success-text` | `#237804` | `#6abe39` | Teks tag Completed/Aktif. |
| `success-bg` | `#f6ffed` | `#162312` | Latar tag dan alert sukses. |
| `success-border` | `#b7eb8f` | `#274916` | Border tag dan alert sukses. |
| `warning` | `#faad14` | `#d89614` | Ikon peringatan dan bar progres peringatan. Bukan untuk teks. |
| `warning-text` | `#874d00` | `#e8b339` | Teks tag peringatan (Jatuh tempo/Belum final). |
| `warning-bg` | `#fffbe6` | `#2b2111` | Latar tag dan alert peringatan. |
| `warning-border` | `#ffe58f` | `#594214` | Border tag dan alert peringatan. |
| `danger` | `#d9363e` | `#c8353a` | Fill tombol danger. Label `on-primary` 4.5:1 lebih. |
| `danger-hover` | `#cf1322` | `#a61d24` | Hover tombol danger. |
| `danger-text` | `#cf1322` | `#e86e6b` | Teks error validasi, tag Terlambat/Cancelled, angka kekurangan. |
| `danger-bg` | `#fff1f0` | `#2c1618` | Latar tag dan alert error, sel QtyGrid melebihi batas. |
| `danger-border` | `#ffccc7` | `#5b2526` | Border tag dan alert error. |
| `cyan-text` | `#006d75` | `#33bcb7` | Teks tag Confirmed/Released (komitmen, belum dikerjakan). |
| `cyan-bg` | `#e6fffb` | `#112123` | Latar tag Confirmed/Released. |
| `cyan-border` | `#87e8de` | `#144848` | Border tag Confirmed/Released. |

**Pemetaan status → nada** (selalu lewat `StatusTag`, lihat 8.2):

| Nada | Token | Dipakai |
|---|---|---|
| Netral | `fill-alter` + `border` | Draft, Nonaktif, Void |
| Cyan | `cyan-*` | Confirmed, Released (komitmen, belum dikerjakan) |
| Biru | `primary-bg` + `link` | In Progress, Open (sedang berjalan) |
| Hijau | `success-*` | Completed, Aktif, Siap |
| Netral kuat | `border-secondary` + ikon kunci | Closed (terkunci) |
| Oranye | `warning-*` | Jatuh tempo, Belum final, Belum siap |
| Merah | `danger-*` | Cancelled, Terlambat, Terkunci, error validasi |

**Aturan pakai:**
- `success`, `warning`, `danger` (tanpa `-text`) hanya untuk isi bar dan ikon, **tidak untuk teks**.
- `primary` hanya untuk: tombol primary, garis bawah kategori MegaMenu aktif, tab aktif, bar progres normal. Tautan dan angka klik memakai `link`.
- `text-disabled` hanya untuk nonaktif dan placeholder.
- Border dekoratif (kartu, tabel, modal) memakai `border`/`border-secondary`; border kontrol (Input, Select, sel QtyGrid, tombol default) **wajib** `border-control`.

### 4.2 Tipografi

Keluarga: `sans` = `-apple-system, BlinkMacSystemFont, Segoe UI, Helvetica Neue, Arial, Noto Sans, sans-serif` (font sistem, tanpa file font); `mono` = `SFMono-Regular, Consolas, Liberation Mono, Menlo, monospace`. Ukuran dasar 14px.

| Style | Family | Size / line | Weight | Dipakai untuk |
|---|---|---|---|---|
| `h1` | sans | 38 / 46px | 600 | Jarang dipakai; halaman kosong/sambutan. |
| `h2` | sans | 30 / 38px | 600 | Judul halaman login. |
| `h3` | sans | 24 / 32px | 600 | Tidak dipakai di halaman kerja. |
| `h4` | sans | 20 / 28px | 600 | Judul halaman (Daftar, Detail). |
| `h5` | sans | 16 / 24px | 600 | Judul kartu, modal, seksi form. |
| `body-lg` | sans | 16 / 24px | 400 | Deskripsi Result/Empty besar. |
| `body` | sans | 14 / 22px | 400 | Default semua teks UI, sel tabel, input. |
| `body-strong` | sans | 14 / 22px | 600 | Header kolom tabel, label total. |
| `caption` | sans | 12 / 20px | 400 | Hint, metadata, pesan validasi, label tag. |
| `stat-value` | sans | 24 / 32px | 600 | Angka utama StatCard. `tabular-nums`. |
| `doc-number` | mono | 13 / 22px | 500 | Nomor dokumen di tabel dan judul detail. `tabular-nums`, `white-space: nowrap`. |

Semua angka di tabel, grid, progres: `font-variant-numeric: tabular-nums`, rata kanan. Judul halaman = `h4`; judul kartu/modal = `h5`.

### 4.3 Jarak (grid 4px)

| Token | Nilai | Dipakai untuk |
|---|---|---|
| `space-1` | `4px` | Jarak ikon-teks, gap dalam tag. |
| `space-2` | `8px` | Gap antar tombol, padding sel QtyGrid. |
| `space-3` | `12px` | Padding sel tabel (vertikal), gap form inline. |
| `space-4` | `16px` | Padding sel tabel (horizontal), gap grid kartu, jarak antar field. |
| `space-5` | `20px` | Padding body modal. |
| `space-6` | `24px` | Padding kartu dan konten halaman. |
| `space-8` | `32px` | Jarak antar seksi halaman. |
| `space-12` | `48px` | Padding state Empty/Result. |

### 4.4 Radius

| Token | Nilai | Dipakai untuk |
|---|---|---|
| `radius-xs` | `2px` | Sel QtyGrid, checkbox. |
| `radius-sm` | `4px` | Tag, tombol kecil, item menu. |
| `radius-md` | `6px` | Tombol, input, select, alert. |
| `radius-lg` | `8px` | Kartu, modal, tabel. |

### 4.5 Ukuran dan layout

| Token | Nilai | Dipakai untuk |
|---|---|---|
| `control-height-sm` | `24px` | Tombol dan input kecil di toolbar tabel. |
| `control-height` | `32px` | Default tombol, input, select, sel QtyGrid. |
| `control-height-lg` | `40px` | Form login, tombol Simpan di form panjang. |
| `header-height` | `56px` | Header aplikasi. |
| `megamenu-width` | `1200px` | Lebar maksimum isi panel MegaMenu (panel selebar layar). |
| `palette-width` | `640px` | Lebar CommandPalette. |

### 4.6 Z-index

| Token | Nilai | Dipakai untuk |
|---|---|---|
| `z-header` | `100` | Header aplikasi. |
| `z-megamenu` | `900` | Panel MegaMenu; scrim satu di bawahnya. |
| `z-modal` | `1000` | Modal biasa. |
| `z-palette` | `1050` | CommandPalette, di atas modal. |

Urutan: header < MegaMenu < modal < CommandPalette.

### 4.7 Bayangan

| Token | Light | Dark | Dipakai untuk |
|---|---|---|---|
| `shadow-card` | `0 1px 2px 0 rgba(0,0,0,0.03), 0 1px 6px -1px rgba(0,0,0,0.02), 0 2px 4px 0 rgba(0,0,0,0.02)` | `0 1px 2px 0 rgba(0,0,0,0.3)` | Kartu di bg-layout. |
| `shadow-popup` | `0 6px 16px 0 rgba(0,0,0,0.08), 0 3px 6px -4px rgba(0,0,0,0.12), 0 9px 28px 8px rgba(0,0,0,0.05)` | `0 6px 16px 0 rgba(0,0,0,0.48), 0 3px 6px -4px rgba(0,0,0,0.6), 0 9px 28px 8px rgba(0,0,0,0.3)` | Modal, dropdown, popover, MegaMenu. |

---

## 5. Layout halaman

```
┌────────────────────────────────────────────────────────────────────┐
│ [A] Nama   Beranda  Kategori▾ Kategori▾ …       [Cari menu Ctrl K] [User] │  header 56px
├────────────────────────────────────────────────────────────────────┤
│ Beranda / Kategori                                                 │  breadcrumb
│ Daftar Dokumen                     [Buat Dokumen]                  │  judul h4 + aksi
│ ┌──────────────────────────────────────────────────────────────┐   │
│ │ toolbar: pencarian · filter                      [aksi]      │   │
│ │ DataTable …                                                  │   │
│ └──────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

- **Tidak ada sider.** Seluruh lebar untuk konten.
- Lebar minimum yang didukung: **1280px** (aplikasi desktop). Layout di bawah 1280px **belum dirancang** (lihat bagian 14); jangan dikarang, ajukan lewat bagian 12.
- Header halaman: breadcrumb → judul `h4` di kiri + aksi di kanan. **Maksimal 3 tombol aksi**; `primary` paling kiri dari kelompok aksi; sisanya masuk menu "Lainnya".
- Konten dikelompokkan dalam kartu atau tabel; jarak antar blok `space-4`, antar seksi `space-8`; padding kartu dan konten halaman `space-6`.
- Beranda: grid `repeat(auto-fill, minmax(280px, 1fr))`, gap `space-4`, dikelompokkan per area; area paling relevan dengan permission pengguna tampil lebih dulu.

---

## 6. Ikonografi

- Gunakan **`@ant-design/icons`** (outlined, 14–16px, `currentColor`) lewat AntDesign Blazor.
- Padanan ikon untuk makna yang berulang:

| Makna | Ant Design | Dipakai |
|---|---|---|
| sukses | CheckOutlined / CheckCircleOutlined | Completed, Aktif |
| waktu | ClockCircleOutlined | Terlambat, Jatuh tempo |
| peringatan | WarningOutlined | Peringatan, Belum final |
| error | CloseCircleOutlined | Error |
| info | InfoCircleOutlined | Info |
| kunci | LockOutlined / UnlockOutlined | Closed, Terkunci / Open |
| ubah | EditOutlined | Draft |
| bendera | FlagOutlined | Confirmed |
| kirim | SendOutlined | Released |
| berjalan | SyncOutlined / LoadingOutlined | In Progress |
| batal | StopOutlined / MinusCircleOutlined | Cancelled / Nonaktif, Void |
| muat ulang | ReloadOutlined | Segarkan, Coba Lagi |
| tambah | PlusOutlined | Aksi Buat/Input |
| cari | SearchOutlined | Pencarian, CommandPalette |
| kosong | InboxOutlined | Empty state |

- Ikon selalu berdampingan dengan teks pada status dan tombol penting. Tombol hanya-ikon **wajib** `aria-label`.
- Tidak ada logo gambar: nama aplikasi ditulis sebagai teks dengan penanda huruf di kotak `primary`. Dilarang menggambar logo.

---

## 7. Navigasi: MegaMenu + CommandPalette

Dua jalur ke tujuan yang sama dari **satu katalog**: MegaMenu untuk menjelajah, CommandPalette (`Ctrl K` / `⌘ K`) untuk yang sudah tahu tujuannya.

### 7.1 Katalog (sumber tunggal)

Setiap entri bertipe **page** (daftar, laporan, detail) atau **action** (Buat, Input, Kirim, Terima). Format entri:

| Field | Isi |
|---|---|
| `key` | Pengenal unik, stabil |
| `label` | Teks menu |
| `desc` | Satu baris deskripsi |
| `kind` | `page` atau `action` |
| `keywords` | Sinonim sehari-hari untuk pencarian |
| izin | Permission yang dibutuhkan (`View` untuk page, `Create` untuk action) |

Hierarki: **Kategori → Grup → Halaman/Aksi**. Beranda adalah tautan langsung tanpa panel.

Aturan katalog:
- Beranda selalu pertama dan menjadi tujuan setelah login.
- Katalog **disusun server sesuai permission**. Item/grup/kategori tanpa izin tidak ada di katalog, sehingga tidak muncul di MegaMenu maupun palette. Kategori yang kosong ikut hilang.
- Maksimal ±12 item per kategori. Bila lebih, pecah kategori; jangan menambah kolom.
- Menambah halaman = menambah entri katalog (label, desc, keywords). Tidak ada jalur navigasi lain.
- Modul baru di FSD wajib menyebut kategori dan grup katalognya. Katalog konkret proyek didokumentasikan di dokumen fondasi UI proyek (`docs/Design/_Foundation-UI.md`) dan harus selaras dengan bagian ini.

### 7.2 MegaMenu (komponen kustom `MegaMenu`)

- Terletak di header; kategori sebagai trigger horizontal. Dibuka dengan **klik**, bukan hover. Satu panel terbuka pada satu waktu.
- Panel selebar layar (`bg-elevated`, `shadow-popup`, `z-megamenu`), isi dibatasi `megamenu-width`; kolom = grup; di tiap kolom: halaman (label + deskripsi) di atas, aksi (ikon + `link`) di bawah dipisah garis putus-putus.
- Kategori yang berisi halaman aktif: teks `link` tebal + garis bawah 2px `primary`. Item aktif: `primary-bg`, `aria-current="page"`.
- Tutup: Esc, klik scrim (`mask` 35%), atau klik kategori yang sama. Footer panel mengingatkan `Ctrl K`.
- ARIA: trigger memakai `aria-expanded` dan `aria-controls`; panel `role="region"` dengan label kategori. Fokus berpindah ke item pertama saat dibuka dan kembali ke trigger saat ditutup; ← → berpindah antar kategori.

### 7.3 CommandPalette (komponen kustom `CommandPalette`)

- Dibuka dari tombol "Cari menu" di header atau `Ctrl K` / `⌘ K` dari halaman mana pun; fokus langsung ke input. Lebar `palette-width`, di atas modal (`z-palette`), latar `mask`.
- Query kosong: **Terakhir dibuka** lalu **Aksi cepat**. Dengan query: semua kata harus cocok ke label, deskripsi, jalur kategori, atau `keywords`; hasil dikelompokkan per kategori; kata cocok digaris bawah.
- Filter chip: Semua · Halaman · Aksi. Baris: ikon, label, deskripsi, jalur "Kategori › Grup", tag "Aksi" untuk aksi. Baris terpilih memakai `primary-bg`.
- Keyboard: ↑ ↓ pilih, Enter buka, Esc tutup. Pola ARIA: `combobox` + `listbox` + `option`.
- Palette **tidak boleh** menjadi jalan pintas ke halaman tanpa permission; server tetap memeriksa saat halaman dibuka.
- Di luar cakupan versi ini: pencarian nomor dokumen dan riwayat "Terakhir dibuka" lintas perangkat (butuh endpoint baru dan penambahan FSD).
- Implementasi Blazor Server: pintasan global dan navigasi panah memerlukan JS interop; penyaringan katalog dilakukan di browser, bukan per ketikan lewat SignalR.

---

## 8. Komponen

Nama komponen di bawah adalah nama kontrak lintas dokumen desain dan kode.

### 8.1 Button
Props: `type` (`primary` | `default` | `dashed` | `text` | `link`), `danger`, `size` (`sm` 24px | `md` 32px | `lg` 40px), `icon`, `loading`, `disabled`, `block`.
- **Satu `primary` per area tampilan** untuk aksi utama halaman. Aksi sekunder `default`.
- `danger` hanya untuk aksi yang membalik data (Batalkan, Void, Tutup); selalu lewat `ConfirmDialog`.
- `dashed` untuk menambah baris di grid/form ("Tambah Baris"); `text` untuk aksi toolbar ringan (Segarkan); `link` untuk navigasi inline.
- Request berjalan → `loading` (cegah klik ganda pada Simpan/Rilis).
- Tombol tanpa permission **tidak dirender**.
- Blazor: `<Button Type="primary">`; `Danger`, `Loading`, `Icon`.

### 8.2 StatusTag
Props: `status`, opsional `children` (ganti label), `tone`, `icon`. Selalu ikon + teks.

| Kunci | Label | Nada | Ikon |
|---|---|---|---|
| `draft` | Draft | netral | EditOutlined |
| `confirmed` | Confirmed | cyan | FlagOutlined |
| `released` | Released | cyan | SendOutlined |
| `inProgress` | In Progress | biru | SyncOutlined |
| `completed` | Completed | hijau | CheckCircleOutlined |
| `closed` | Closed | netral kuat | LockOutlined |
| `cancelled` | Cancelled | merah | StopOutlined |
| `open` | Open | biru | UnlockOutlined |
| `active` / `inactive` | Aktif / Nonaktif | hijau / netral | CheckOutlined / MinusCircleOutlined |
| `voided` | Void (teks dicoret) | netral | MinusCircleOutlined |
| `late` | Terlambat | merah | ClockCircleOutlined |
| `dueSoon` | Jatuh tempo | oranye | ClockCircleOutlined |
| `notFinal` | Belum final | oranye | WarningOutlined |
| `ready` / `notReady` | Siap / Belum siap | hijau / oranye | CheckCircleOutlined / WarningOutlined |
| `locked` | Terkunci | merah | LockOutlined |

Dilarang membuat status baru dengan warna sendiri; tambahkan kunci baru lewat bagian 12. Cyan = komitmen belum dikerjakan; biru = berjalan; hijau = selesai; merah = dibatalkan/terlambat. Label status khusus proyek (mis. "Siap Produksi") boleh lewat `children` selama nadanya mengikuti tabel ini.
- Blazor: `<Tag>` + `<Icon>`.

### 8.3 StatCard
Props: `title`, `value`, `unit`, `meta`, `tag`, `extra`, `items` (maks 5 baris), `onValueClick`, `state` (`ready` | `loading` | `empty` | `error`), `emptyText`, `errorText`, `onRetry`, `children`.
- Dimuat **independen**: loading = skeleton; error = pesan + "Coba Lagi" hanya di kartu itu; empty informatif.
- Angka klik memakai `link` dan menuju daftar **terfilter**.
- Total sementara wajib `tag` `notFinal`. Tanpa grafik tren.
- Blazor: `<Card>` + `<Statistic>`, `<Skeleton Active>`, `<Empty>`.

### 8.4 DataTable
Props: `columns` (`key`, `title`, `align`, `width`, `render`), `rows`, `rowKey`, `pagination` (`page`, `pageSize`, `total`), `emptyText`, `caption`.
- Kolom qty/upah/harga `align: right`; angka diformat `id-ID`. Nomor dokumen = tautan `doc-number`.
- Status selalu `StatusTag`; tanggal terlambat diberi tag `late`.
- Toolbar di atas: pencarian kiri, filter `Select`, tombol `primary` kanan.
- Paging di server, default 10 baris, teks "1–10 dari 48".
- Baris Void tetap tampil dengan tag `voided`.
- Blazor: `<Table TItem DataSource Total PageSize>`.

### 8.5 QtyGrid (matriks kuantitas dua dimensi, mis. Warna × Ukuran)
Props: `rowsAxis`, `colsAxis`, `values`, `max` (`null` = sel tidak berlaku, tampil "–"), `onChange`, `readOnly`, `showTotals`.
- Hint "maks N" di tiap sel bila `max` ada.
- Melebihi batas: sel `danger-bg` + border `danger-text` + ikon, dan pesan di bawah grid menyebut sel ("Navy / L (maks 240)").
- Keyboard: Enter / ↓ ke sel bawah, ↑ ke atas, Tab ke kanan.
- Hanya bilangan bulat ≥ 0; baris dan kolom total memakai `fill-alter`. Varian qty negatif (penyesuaian) belum ada; ajukan lewat bagian 12.
- Blazor: `<InputNumber>` dalam tabel sendiri (bukan `<Table>` virtualized).

### 8.6 FormField, Input, Select, DatePicker
- `FormField`: `label`, `required` (`*` merah), `help`, `error`. Label di atas kontrol (layout vertikal). Error menggantikan help dan menyebut penyebab.
- `Input` (`multiline` untuk alasan), `Select` (`options`, `placeholder`), `DatePicker` format `DD/MM/YYYY`.
- Nonaktif hanya bila nilai terkunci oleh status; jelaskan alasannya di `help`.
- Blazor: `<Form Layout="vertical">`, `<FormItem>`, `<Input>`, `<Select>`, `<DatePicker Format="dd/MM/yyyy">`.

### 8.7 Alert
`type` (`info` | `success` | `warning` | `error`), `title`, `description`, `action`, `banner`.
- `banner` untuk koneksi Blazor terputus, di bawah header. `warning` untuk total sementara. `success` setelah simpan berdampak lintas modul (sebut dampaknya).
- Bukan untuk error validasi field; pakai `FormField error`.
- Blazor: `<Alert>`.

### 8.8 ConfirmDialog
Props: `title` (pertanyaan berisi objek), `summary`, `impact`, `reasonRequired`, `reasonLabel`, `danger`, `confirmText`, `loading`, `onConfirm(reason)`, `onCancel`.
- **Wajib** untuk Void/Koreksi/Cancel/Reopen/Close. Tombol konfirmasi menyebut aksinya ("Void Entri"), bukan "OK".
- `impact` **wajib** untuk Void/Koreksi/Penyesuaian: pratinjau dampak dihitung server sebelum simpan.
- `reasonRequired` untuk aksi yang membalik data. Pesan "Alasan wajib diisi".
- `danger` untuk aksi yang membalik data atau permanen.
- Blazor: `<Modal>` / `ModalService.ConfirmAsync`.

### 8.9 Tabs dan ProgressBar
- `Tabs`: halaman detail; `items` (`key`, `label`, `count`). Tab pertama = ringkasan; "Riwayat" terakhir. Aksi utama di header halaman, bukan di dalam tab. Blazor: `<Tabs>` + `<TabPane>`.
- `ProgressBar`: progres per langkah. `status` `exception` untuk terlambat dan teks kanan wajib menyebut "telat". Blazor: `<Progress>`.

### 8.10 AppShell
Menggabungkan header, MegaMenu, tombol pencarian (`Ctrl K` global), menu pengguna, breadcrumb, judul, dan aksi. Satu-satunya tempat pintasan global dipasang.

---

## 9. Aksesibilitas (wajib)

- **Kontras WCAG 2.1 AA** di Light dan Dark: teks 4.5:1; teks ≥24px atau tebal ≥19px, border kontrol, cincin fokus, ikon bermakna 3:1. Semua pasangan token di bagian 4 sudah lolos; **pasangan baru wajib dicek**.
- Label tombol di atas `primary`/`danger` memakai `on-primary`, bukan putih literal.
- Status, "terlambat", "melebihi batas", dan error **tidak boleh hanya warna**: ikon + teks.
- Semua kontrol dapat dioperasikan dengan keyboard; urutan tab mengikuti urutan visual; tombol hanya-ikon punya `aria-label`.
- Dialog: `role="dialog"`, `aria-modal`, fokus masuk ke dialog dan kembali ke pemicu saat ditutup, Esc menutup.
- Error validasi terhubung ke field (`aria-invalid`) dan diumumkan (`role="alert"` untuk error/peringatan penting).
- Tabel: `caption`, `scope` pada header.
- Animasi dimatikan saat `prefers-reduced-motion`.

---

## 10. Pola layar standar

| Pola | Susunan | Komponen |
|---|---|---|
| **Daftar** | breadcrumb → judul + aksi → toolbar (cari, filter) → tabel → paging | AppShell, DataTable, Input, Select, Button, StatusTag |
| **Form** | judul + Simpan/Batal → seksi header (FormField vertikal, grid 2–3 kolom) → grid QtyGrid bila perlu → ringkasan total | FormField, QtyGrid, Alert, Button |
| **Detail** | judul + status + aksi → Tabs (Ringkasan, …, Riwayat) → isi | Tabs, StatCard/kartu, DataTable, ProgressBar |
| **Aksi berdampak** | tombol → ConfirmDialog (ringkasan, dampak, alasan) → toast/Alert sukses | ConfirmDialog, Alert |
| **Beranda** | grid StatCard per area; "Diperbarui pukul HH:mm" + Segarkan | StatCard |
| **Empty / Error** | pesan informatif + tindakan berikutnya; error + "Coba Lagi" | StatCard state, DataTable `emptyText` |

---

## 11. Pemetaan ke AntDesign Blazor

| Komponen | AntDesign Blazor | Catatan |
|---|---|---|
| Button | `<Button>` | |
| StatusTag | `<Tag>` + `<Icon>` | Pertahankan ikon + teks |
| StatCard | `<Card>`, `<Statistic>`, `<Skeleton>`, `<Empty>` | |
| DataTable | `<Table>` | Paging server-side |
| QtyGrid | `<InputNumber>` dalam tabel sendiri | Jangan pakai `<Table>` virtualized |
| FormField/Input/Select | `<Form>`, `<FormItem>`, `<Input>`, `<Select>`, `<DatePicker>` | |
| Alert | `<Alert>` | `Banner` untuk koneksi |
| ConfirmDialog | `<Modal>` / `ModalService` | |
| Tabs | `<Tabs>` + `<TabPane>` | |
| ProgressBar | `<Progress>` | |
| MegaMenu | **komponen kustom** | `<SubMenu>` Ant tidak mendukung panel multi-kolom |
| CommandPalette | **komponen kustom** (`<Modal>` + daftar) | JS interop untuk `Ctrl K` dan panah |

**Penerapan token:** token bagian 4 diterapkan sebagai override tema AntDesign Blazor: `primary` → warna primary, `radius-md` → border radius dasar, 14px → font size dasar, `control-height` → tinggi kontrol. Mekanisme override (CSS variable, Less, atau theme token) bergantung pada versi AntDesign Blazor yang dipakai; **verifikasi dulu sebelum implementasi** dan catat mekanisme yang dipilih di dokumen fondasi UI proyek. Token juga diekspos sebagai CSS variable (`--primary`, `--bg-container`, dst.) untuk CSS kustom (MegaMenu, CommandPalette, QtyGrid).

**Paket:** AntDesign Blazor adalah paket NuGet baru; sesuai aturan proyek, pemasangannya **wajib dikonfirmasi user** dan versinya dipusatkan di `src/Directory.Packages.props`.

Komponen React pada artifact/prototipe (bila ada) hanya referensi visual dan perilaku, bukan kode produksi.

---

## 12. Proses perubahan desain

Agent tidak boleh mengubah dokumen ini sepihak. Untuk kebutuhan yang belum tercakup (komponen baru, token baru, status baru, layout < 1280px, QtyGrid negatif, matriks permission, halaman login, desain Mobile, dsb.):

1. Tulis **usulan** berisi: kebutuhan (FSD/AC yang mendasari), mengapa komponen/token yang ada tidak cukup, rancangan memakai token yang ada, dan perilaku Light/Dark + aksesibilitas.
2. Usulan ditinjau pemilik produk; bila disetujui, perbarui **dokumen ini** (dan artifact bila ada) dalam perubahan yang sama, naikkan versi, dan catat di Riwayat.
3. Sebelum disetujui, rancangan sementara harus diberi label **"Usulan, belum bagian dari Design.md"** dan tidak boleh dianggap final oleh agent lain.

Jangan: menambah warna, ukuran, atau bayangan di luar token; menyalin gaya dari proyek lain; menambah pustaka UI lain.

---

## 13. Checklist sebelum menyerahkan UI

- [ ] Hanya token, tanpa hex/px/bayangan literal
- [ ] Hanya komponen dari bagian 8; deviasi diberi alasan
- [ ] Semua status memakai `StatusTag` (ikon + teks)
- [ ] Navigasi lewat katalog (bagian 7); halaman baru sudah masuk katalog
- [ ] Aksi pembalik data memakai `ConfirmDialog` dengan dampak dan alasan
- [ ] Teks Indonesia, format angka/uang/tanggal sesuai bagian 3
- [ ] Empty, loading, dan error state ada untuk setiap daftar dan widget
- [ ] Tombol/menu/widget tanpa permission tidak dirender
- [ ] Benar di Light dan Dark; kontras lolos; fokus terlihat
- [ ] Keyboard: tab, Enter, Esc, panah berfungsi
- [ ] Lebar 1280px tanpa scroll horizontal pada halaman
- [ ] Spesifikasi menyebut token + komponen yang dipakai

---

## 14. Di luar cakupan versi ini

Belum dirancang dan **tidak boleh dikarang**; ajukan lewat bagian 12:
- **Layar di bawah 1280px** (tablet, ponsel) dan navigasi responsifnya.
- **Mobile (MAUI):** dokumen ini hanya untuk Web. Aplikasi Mobile membutuhkan panduan desain terpisah (target sentuh, navigasi, state offline) sebelum dirancang.
- Varian `QtyGrid` dengan qty negatif, halaman login, matriks permission.
- Pencarian nomor dokumen dan riwayat lintas perangkat pada CommandPalette.

---

## Riwayat

| Versi | Tanggal | Perubahan |
|---|---|---|
| 1.0 | 2026-11-11 | Versi awal: AntDesign Blazor, MegaMenu + CommandPalette, tanpa sider |
| 1.1 | 2026-11-11 | Melengkapi bagian yang dirujuk tetapi hilang: bagian 12 (proses perubahan) dan 13 (checklist); token bayangan, z-index, ukuran, line-height; spesifikasi MegaMenu/CommandPalette/katalog; StatusTag, QtyGrid, ConfirmDialog, Alert, Tabs/ProgressBar, AppShell; pemetaan AntDesign + penerapan token; bagian 14 (di luar cakupan). Dokumen dibuat berdiri sendiri (tanpa ketergantungan artifact) |
