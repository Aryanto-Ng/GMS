# DESIGN.md — Enterprise Design System (Microsoft Fluent 2)

**Master Project Template** · Blazor Server (.NET) · Microsoft Fluent 2 · Bahasa Indonesia (+ English)
Versi 2.0 · Sumber kebenaran visual dan interaksi untuk seluruh UI aplikasi.

Dokumen ini adalah aturan desain tetap untuk seluruh proyek aplikasi bisnis kami. Gaya, layout, dan komponen di sini **tidak berubah antar proyek**. Yang berubah per proyek hanya: identitas aplikasi, isi menu, modul, dan data, yang diambil dari FSD proyek tersebut.

Referensi visual: artifact "Master Template — Interface (Fluent 2, Tab Workspace)" (board Workspace interaktif, Form masuk, Anatomi layout). Bila dokumen ini dan artifact berbeda, **dokumen ini yang berlaku** dan artifact wajib diperbarui.

Dokumen ini menggantikan versi 1.0 (Ant Design). Perubahan besar: Fluent 2 menggantikan Ant Design; tema terang saja; navigasi Top Navigation plus Tab Manager; CommandPalette `Ctrl K` digantikan pencarian di header.

---

## 0. Aturan wajib untuk agent (baca dulu)

Dokumen ini mengikat semua agent yang menghasilkan, mengubah, atau me-review UI: UI/UX Designer, Frontend/Blazor Engineer, QA, Tech Writer.

1. **Wajib membaca DESIGN.md sebelum merancang atau menulis UI apa pun.** Tidak ada layar, komponen, atau CSS yang dibuat tanpa merujuk dokumen ini.
2. **Pakai token, bukan nilai mentah.** Dilarang menulis hex, px, atau bayangan literal di luar bagian 9. Di CSS pakai `var(--brand)`, `var(--fg2)`, `var(--sh16)`, dan seterusnya.
3. **Pakai komponen yang ada (bagian 10).** Dilarang membuat varian tombol, badge status, tabel, atau dialog baru bila komponen yang ada bisa dipakai. Butuh komponen baru: ikuti bagian 15 (Proses perubahan), jangan membuatnya diam-diam.
4. **Status wajib teks, plus warna sesuai peta bagian 8.** Dilarang membedakan status hanya dengan warna, dan dilarang memakai warna di luar peta.
5. **Layout hanya Global Header + Top Navigation + Internal Tab Manager + Main Workspace.** Dilarang menambah sidebar kiri, menu samping, atau pintasan di luar katalog navigasi.
6. **Aksi yang membalik data wajib dialog konfirmasi** dengan ringkasan, dampak, dan alasan bila wajib.
7. **Yang tidak berhak, tidak dirender.** Menu, widget, tombol tanpa permission disembunyikan, bukan dinonaktifkan. Server tetap memeriksa permission; UI bukan pengaman.
8. **Semua teks UI lewat resource ID/EN** (bagian 12). Nama status dokumen mengikuti FSD (biasanya tetap Inggris).
9. **Tema terang saja.** Kontras minimum di bagian 11.
10. **Nama proyek tidak boleh tertulis keras di komponen shell** (bagian 2).
11. **Output desain wajib menyebut token dan komponen yang dipakai** (daftar singkat di akhir spesifikasi layar). Deviasi dari dokumen ini harus ditandai eksplisit dengan alasan; reviewer menolak deviasi tanpa alasan.

Checklist cepat sebelum menyerahkan UI: bagian 14.

---

## 1. Prinsip

1. **Tanpa sidebar kiri.** Seluruh navigasi lewat Global Header dan Top Navigation. Konteks kerja lewat Internal Tab Manager.
2. **Satu layout wajib** untuk semua halaman setelah login: Global Header, Top Navigation, Internal Tab Manager, Main Workspace (bagian 3).
3. **Fluent 2, tema terang saja.** Tidak ada dark mode kecuali diminta khusus oleh proyek.
4. **Status tidak pernah hanya warna.** Selalu teks (dan ikon bila perlu) bersama warna.
5. **Aman dulu, cepat kemudian.** Aksi permanen memakai dialog dan alasan; validasi tampil sebelum menyimpan, bukan sesudah.
6. **Template terpisah dari modul.** Shell (bagian 3 sampai 12) generik. Modul mengikuti FSD (bagian 13).
7. **Data dulu, dekorasi belakangan.** Permukaan netral, satu warna aksen (brand); warna lain hanya untuk status. Dekorasi (gradasi, animasi latar) hanya diizinkan di form masuk (bagian 10).
8. **Setiap angka dapat ditindaklanjuti.** Angka ringkasan membuka daftar terkait.
9. **Server pemegang aturan.** Validasi klien hanya bantuan; invariant dicek server pada kondisi akhir transaksi.

## 2. Identitas aplikasi (satu-satunya yang diganti per proyek)

| Nilai | Dipakai di | Contoh placeholder |
|---|---|---|
| Nama aplikasi | Header, form masuk, footer, judul halaman | Nama Aplikasi |
| Singkatan | Header (di sebelah logo) | App |
| Huruf logo | Kotak logo header dan form masuk | A |
| Tagline atau perusahaan | Form masuk | Nama Perusahaan |
| Tahun | Footer copyright | 2026 |

Aturan: tidak ada nama proyek, perusahaan, atau modul yang ditulis keras di komponen shell. Footer berbentuk `© {tahun} {nama aplikasi}. Hak cipta dilindungi.` (EN: `All rights reserved.`). Nama badan hukum dikonfirmasi per proyek.

Implementasi Blazor: satu `AppIdentityOptions` (bind dari `appsettings.json`), dipakai oleh layout dan halaman masuk.

## 3. Anatomi layout

Empat zona vertikal, tinggi viewport penuh. Hanya Main Workspace yang menggulir.

| Zona | Tinggi | Isi |
|---|---|---|
| 1. Global Header | 48px, latar brand `#0F6CBD`, teks putih | Tombol menu (mobile), logo dan nama, pencarian halaman dan aksi di tengah, pemilih bahasa, notifikasi, menu pengguna |
| 2. Top Navigation | 40px, putih | Satu kategori per modul utama, panel mega saat dibuka |
| 3. Internal Tab Manager | 40px, `#EBEBEB` | Tab dokumen terbuka |
| 4. Main Workspace | sisa tinggi, latar `#F5F5F5` | PageHeader, MessageBar, ringkasan, konten, aksi form, footer |

Di bawah 960px: Top Navigation disembunyikan; tombol menu di header membuka daftar vertikal. Padding workspace 24px (16px di mobile).

## 4. Global Header

- Latar brand, fokus tombol memakai outline putih.
- Kiri: logo (kotak 28px) + singkatan + nama panjang (disembunyikan di bawah 960px).
- Tengah: pencarian halaman dan aksi. Hasil maksimal 7, tiap hasil menampilkan label, jalur menu, dan tag "Aksi" bila berupa aksi.
- Kanan, urutan tetap: **bahasa, notifikasi, pengguna**.
  - **Bahasa:** ikon globe plus kode `ID` atau `EN`; menu dua pilihan dengan centang pada yang aktif.
  - **Notifikasi:** ikon lonceng. Lencana jumlah belum dibaca kecil di sudut kanan atas (maks `9+`, hilang bila nol, tinggi 12px, angka 9px, latar `#C50F1F`). Panel 380px: judul, ringkasan "N belum dibaca", "Tandai semua dibaca", daftar (belum dibaca: titik biru dan latar tint brand, teks tebal), "Lihat semua notifikasi". Klik item menandai terbaca lalu membuka dokumen terkait di tab. Label tombol memuat jumlah untuk pembaca layar.
  - **Pengguna:** avatar, nama, menu (Ganti Password, Keluar).
- Hanya satu panel header terbuka pada satu waktu; klik di luar menutup semuanya.

## 5. Top Navigation (mega menu)

- Satu tombol per kategori, ikon 16px plus label. Kategori aktif: teks tebal dan garis bawah brand 3px.
- Panel mega lebar penuh, berisi **grup** (kartu berbingkai, header grup dengan ikon di tile brand) dan **item** (tile ikon, judul, deskripsi singkat).
- Item aksi (membuat sesuatu) memakai tile ikon tint brand dan tanda `+`; item halaman memakai tile netral.
- Setiap grup dan item **wajib punya ikon** yang sesuai artinya. Ikon: garis 1.4px, kotak 16px, ujung bulat (set Fluent regular).
- Item tanpa permission tidak dirender. Prototipe menampilkan semua untuk Administrator.
- Kategori tanpa panel (mis. Beranda) berperilaku sebagai tautan langsung.

## 6. Internal Tab Manager

- Satu tab per dokumen. ID tab = layar + nomor dokumen. Membuka dokumen yang sama memfokuskan tab yang ada.
- Tab Beranda terpaku, tidak bisa ditutup.
- Maksimal **10 tab**. Saat penuh tampilkan pesan; tidak boleh menutup tab lama diam-diam.
- Tab aktif: latar kanvas, teks tebal, aksen brand 3px di atas.
- **Belum disimpan:** titik oranye plus teks tersembunyi "belum disimpan"; menutup meminta konfirmasi.
- **Alert pada tab:** ikon di depan judul bila isi tab bermasalah. Lingkaran merah berseru = error atau keterlambatan; segitiga oranye berseru = peringatan. Tooltip dan teks pembaca layar menyebut alasannya. Ikon hilang otomatis saat masalah selesai.
- Klik kanan: muat ulang, tutup, tutup lainnya, tutup semua. Tombol "Semua tab" menampilkan daftar saat meluap.
- State form, filter, pencarian, dan sub-tab dipertahankan per tab selama sesi.

## 7. Main Workspace

Urutan tetap, dari atas:

1. **PageHeader:** breadcrumb (12px), judul 20/28 semibold, tag status, tombol aksi kanan. Jarak bawah 14px agar tombol tidak menempel garis. Sub-judul opsional. Tab dalam (inner tabs) menempel ke garis bawah header.
2. **MessageBar:** info, peringatan, sukses, error. Info, peringatan, dan sukses bisa ditutup (×); **error tidak bisa ditutup**.
3. **Ringkasan angka:** tile terpisah (bingkai 1px, aksen atas 3px brand, hover terangkat), bukan satu kotak teks.
4. **Siklus status** (bila dokumen bertahap): lihat bagian 8.
5. **Konten:** kartu, tabel, form, grid.
6. **Aksi form** (kartu footer): catatan di kiri, tombol di kanan. Satu tombol primary.
7. **Footer copyright** di ujung area gulir.

Aturan tombol: **maksimal satu primary** per halaman. Tombol di header halaman memiliki **ikon 16px di kiri teks** (Buat, Ubah, Batalkan, Konfirmasi, Ekspor, dan sebagainya). Tombol footer form (Simpan, Batal) tanpa ikon. Tombol yang hanya ikon (mis. Segarkan) wajib punya `aria-label` dan tooltip. Aksi permanen (Batalkan, Void, Tutup periode) memakai dialog; alasan wajib bila FSD mengatakan begitu.

## 8. Status dan warna semantik

Satu peta warna untuk **seluruh** badge, siklus status, bar progres, dan chart:

| Makna | Warna | Contoh status |
|---|---|---|
| Draft, belum mulai, terjadwal | **Biru** | Draft, Confirmed, Released, Belum Mulai |
| Sedang berjalan | **Kuning** | In Progress, Open, Berjalan |
| Selesai | **Hijau** | Completed, Closed, Selesai, Aktif, Siap |
| Obsolete, batal, nonaktif | **Abu-abu** | Obsolete, Cancelled, Void, Nonaktif |
| Terlambat atau galat | **Merah** | Terlambat, Terkunci, Negatif |
| Perlu perhatian | **Oranye** | Jatuh tempo, Belum final, Belum siap |

Token badge (teks / latar / garis):

| Kelas | Teks | Latar | Garis |
|---|---|---|---|
| biru `t-proc` | `#0F548C` | `#EBF3FC` | `#A6C8EB` |
| kuning `t-prog` | `#6B4E00` | `#FFF6D1` | `#F0D367` |
| hijau `t-ok` | `#0E700E` | `#F1FAF1` | `#9FD89F` |
| abu `t-obs` | `#616161` | `#F0F0F0` | `#D1D1D1` |
| merah `t-err` | `#B10E1C` | `#FDF3F4` | `#EEACB2` |
| oranye `t-warn` | `#9A3B06` | `#FFF9F5` | `#FDCFB4` |

Aturan:
- Peta status per proyek diisi dari FSD (nama status berbeda), tetapi **kategori warnanya tetap** di atas.
- Merah hanya untuk keterlambatan dan galat. Batal/Void **bukan** merah.
- **Siklus status** (stepper): lingkaran di atas, label dan keterangan di bawah, garis penghubung tidak melewati teks. Langkah selesai: lingkaran putih bergaris **hijau** dengan ikon centang, garis penghubung hijau, keterangan "Selesai". Status akhir (Completed, Closed) juga hijau. Langkah aktif: Draft biru bernomor; In Progress kuning bernomor. Langkah berikutnya: abu-abu bernomor, keterangan "Belum".
- Bar progres: berjalan kuning `#AD7A00`, selesai hijau, terlambat merah.

## 9. Token Fluent 2

### Warna

| Token | Nilai | Pakai |
|---|---|---|
| brand | `#0F6CBD` | Header, tombol primary, fokus, aksen |
| brand hover / pressed | `#115EA3` / `#0F548C` | Interaksi |
| brand tint | `#EBF3FC` | Latar terpilih atau hover lembut |
| teks 1 / 2 / 3 | `#242424` / `#424242` / `#616161` | Utama, sekunder, tersier |
| kanvas | `#F5F5F5` | Latar workspace |
| permukaan | `#FFFFFF`, `#FAFAFA` | Kartu, panel |
| stroke | `#D1D1D1` (kontrol), `#E0E0E0` (pemisah), `#F0F0F0` (halus) | Garis |
| error | `#B10E1C` | Galat, kolom invalid |
| lencana | `#C50F1F` | Lencana notifikasi |

Palet chart (urut kategori): `k1` biru `#0F6CBD`, `k7` biru muda `#6CB0F0`, `k8` kuning `#D9A400`, `k5` hijau `#107C10`, `k2` teal `#038387`, `k3` oranye `#BC4B09`, `k4` abu `#8A8A8A`, `k6` merah `#B10E1C`. Warna chart status **mengikuti bagian 8**.

Nama CSS custom property (wajib dipakai, bagian 0 aturan 2): `--brand`, `--brand-h`, `--brand-p`, `--brand-t`, `--fg1`, `--fg2`, `--fg3`, `--s1` (stroke kontrol), `--s2` (pemisah), `--s3` (halus), `--bg2` (`#FAFAFA`), `--bg3` (kanvas), `--sh2`, `--sh16`, `--sh64`. Warna status memakai kelas badge bagian 8.

### Tipografi

Font: Segoe UI Variable Text, lalu Segoe UI, system-ui, Roboto. Mono (kode dokumen): Cascadia Mono, Consolas.

| Peran | Ukuran / tinggi baris | Bobot |
|---|---|---|
| Judul halaman | 20 / 28 | 600 |
| Judul kartu, area | 16 / 22 (kartu Beranda 14) | 600 |
| Teks | 14 / 20 | 400 |
| Keterangan, label kecil | 12 / 16 | 400 |
| Angka besar (ringkasan) | 24 / 32 | 600, tabular-nums |

Angka selalu `tabular-nums` dan rata kanan di tabel.

### Radius, bayangan, jarak

- Radius: kontrol dan tombol 4px; kartu, panel, dialog 8px; badge pil 10px.
- Bayangan: kartu `0 0 2px rgba(0,0,0,.12), 0 1px 2px rgba(0,0,0,.14)`; panel dan menu `0 0 2px rgba(0,0,0,.12), 0 8px 16px rgba(0,0,0,.14)`; dialog `0 0 8px rgba(0,0,0,.12), 0 32px 64px rgba(0,0,0,.14)`.
- Jarak: skala 4px (4, 8, 12, 16, 20, 24). Jarak antar kartu 12 sampai 16px. Tinggi kontrol 32px; target sentuh minimal 24px.

### Input

Border 1px `#D1D1D1`, **garis bawah `#616161`** (batas aksesibel). Fokus: garis bawah 2px brand, tanpa outline lain. Invalid: border dan garis bawah merah, latar `#FDF3F4`, pesan error 12px di bawah kolom. Kolom wajib bertanda `*` merah (aria-hidden) plus `aria-required`.

## 10. Komponen

### Tombol
Tinggi 32px, radius 4px, teks 600. Primary: latar brand, teks putih. Default: putih, border `#D1D1D1`. Bahaya (`dng`): teks merah, hanya untuk aksi merusak. Nonaktif: latar `#F0F0F0`, teks `#A0A0A0`, dengan tooltip alasan bila relevan.

### Kartu
Putih, border `#E0E0E0`, radius 8px, bayangan kartu. **Kartu interaktif** (Beranda): hover terangkat 2px dan bayangan panel; ikon area di header (tile 28px tint brand); tombol "Buka detail" di dasar bila ada tujuan. Animasi dimatikan pada `prefers-reduced-motion`.

### Tabel dan daftar
- Setiap daftar atau tabel **wajib punya pencarian**; tambahkan **filter chip** bila ada kategori bermakna (status, terlambat, dan sebagainya). Chip menampilkan jumlah. Filter dan pencarian dipertahankan per tab dan **tidak menandai tab belum disimpan**.
- Footer tabel: "Menampilkan X dari Y". Keadaan kosong punya teks jelas.
- Angka rata kanan, kolom pertama tetap (sticky) pada matriks lebar, scroll horizontal di dalam kartu.
- Status berupa badge.

### Grid input
Batas tersedia terlihat sebelum mengetik; sel yang melebihi batas ditandai merah dengan pesan "Maks N"; tombol Simpan dinonaktifkan atau diblokir dengan penjelasan.

### Chart
Jangan memakai donut sebagai default. Pilihan yang dipakai:
- **Komposisi:** bar tumpuk satu baris (stack) ditambah daftar bar horizontal per kategori (label, bar, jumlah, persen), baris bisa diklik.
- **Tren harian:** bar vertikal dengan nilai di atas bar.
- Semua chart: tooltip nilai, hover menyorot, `role="img"` dengan `aria-label`, dan angka tertulis sehingga tidak bergantung warna. Ukuran dipadatkan (tinggi bar 120px).

### Toast
Muncul di tengah atas, hilang otomatis (error lebih lama, 4 detik). Tiga jenis: **sukses** hijau (ikon centang, garis kiri hijau), **error** merah (ikon silang, `role="alert"`), **info** biru. Pemanggil mengirim jenisnya **secara eksplisit**.

### Dialog
Lebar 480px, radius 8px, bayangan dialog, latar gelap 40%. Aksi permanen: judul jelas, ringkasan dampak, kolom alasan bila wajib, tombol bahaya di kanan.

### Hero Beranda
Satu baris: sapaan, tanggal dan waktu pembaruan, lalu chip ringkasan kecil (tinggi 28px) yang bisa diklik. Tombol **Segarkan** tetap ada sebagai ikon di kanan judul.

### Form masuk
Halaman penuh, di luar layout 4 zona.
- Latar SVG bergerak bertema bisnis memenuhi layar (`position: fixed`, 100vw × 100vh): gradasi biru tua ke brand, kisi tipis, bar yang naik-turun, garis tren, titik data, gelombang, bentuk melayang. Redup agar tidak mengganggu. Wajib mati pada `prefers-reduced-motion`.
- Kartu putih 420px di **tengah layar** (horizontal dan vertikal), bayangan panel.
- Isi: logo dan nama, judul "Masuk", Nama pengguna, Kata sandi (tombol tampil/sembunyi), tombol Masuk (status memuat), tautan bantuan "Lupa kata sandi? Hubungi Administrator", toggle ID/EN di pojok kanan atas, footer copyright di dasar layar.
- Galat kredensial memakai MessageBar error `role="alert"`; pesan tidak membedakan nama pengguna atau kata sandi yang salah.

## 11. Aksesibilitas (wajib)

- Kontras teks minimal 4.5:1, komponen non-teks minimal 3:1. Badge memakai teks gelap pada latar tint.
- Semua kontrol dapat diakses keyboard; fokus terlihat (outline 2px).
- Status: teks plus warna. Ikon dekoratif `aria-hidden`; ikon bermakna punya teks alternatif.
- Peran ARIA: tab (`tablist`/`tab`), menu, `alert` untuk error, `status` untuk info dan sukses, `progressbar` untuk bar progres.
- Hormati `prefers-reduced-motion`.
- Elemen kecil (mis. lencana) tidak boleh jadi satu-satunya pembawa informasi; sertakan teks di label tombol atau panel.

## 12. Bahasa (i18n)

- Dua bahasa: Indonesia (default) dan English. Pilihan disimpan sebagai preferensi pengguna dan dipakai juga di halaman masuk.
- Prototipe hanya menerjemahkan chrome. **Versi nyata menerjemahkan semua teks** lewat `IStringLocalizer` dan berkas `.resx` (`id-ID`, `en-US`); tanggal dan angka mengikuti culture.
- Kunci resource per teks; dilarang menyusun kalimat dari potongan string.
- Keputusan per proyek: apakah data master (nama produk, proses, dan sebagainya) ikut diterjemahkan atau satu bahasa.

### Konten dan format data

- UI **sentence case**: "Buat Dokumen", "Tidak ada data terlambat".
- **Nama status dokumen tetap Inggris** bila FSD begitu (Draft, Confirmed, Released, In Progress, Completed, Closed, Cancelled, Open). Kondisi turunan memakai bahasa UI: Terlambat, Jatuh tempo, Belum final, Terkunci.
- **Tombol = kata kerja + objek**: "Simpan Draft". Dilarang "OK", "Submit", "Yes/No".
- **Pesan error menyebut penyebab dan letaknya**: "Melebihi batas tersedia: Kategori / Ukuran (maks 240)".
- **Empty state informatif**, bukan "No data".
- Tanpa emoji.

| Data | Format (ID) | Contoh |
|---|---|---|
| Angka | `id-ID`, titik ribuan | `1.240` |
| Uang | `Rp` + angka, tanpa desimal | `Rp 18.420.500` |
| Tanggal (tabel dan teks) | `D MMM YYYY` | `14 Nov 2026` |
| Tanggal (input) | `DD/MM/YYYY` | `14/11/2026` |
| Waktu | `HH:mm`, zona aplikasi | `09:42` |
| Satuan qty | sesuai FSD | `1.240 pcs` |
| Nomor dokumen | font mono, tabular-nums | `DOC-202611-0142` |

Pada English, format mengikuti culture `en-US` (`1,240`, `Nov 14, 2026`).

## 13. Cara proyek memakai template ini

**Tetap (jangan diubah):** bagian 3 sampai 12.

**Diisi dari FSD proyek:**
1. Identitas aplikasi (bagian 2).
2. Struktur menu: kategori, grup, item, ikon, permission.
3. Daftar status dan pemetaan ke kategori warna (bagian 8).
4. Widget Beranda: pilih KPI dan chart dari data FSD; tiap kartu punya tujuan klik.
5. Kode dokumen, aturan validasi, aturan "alasan wajib".
6. Kolom, filter, dan pencarian tiap daftar.
7. Kebutuhan notifikasi (peristiwa pemicu, penerima per role, retensi, real-time atau polling) menjadi FSD sendiri.

**Dalam implementasi Blazor Server:**
- Pustaka komponen Fluent 2 (`Microsoft.FluentUI.AspNetCore.Components`) membutuhkan **izin eksplisit** dahulu sebelum ditambahkan (aturan dependency proyek). Bila ditolak, bangun komponen sendiri dengan CSS token di bagian 9.
- Token dijadikan CSS custom properties (`--brand`, `--fg1`, ...), satu berkas `tokens.css`.
- Layout menjadi `MainLayout.razor` dengan komponen `AppHeader`, `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `StatTile`, `Stepper`, `DataList` (cari, filter, footer), `ToastHost`, `NotificationPanel`, `LanguageMenu`.
- `TabManager` adalah service berstatus per sesi (daftar tab, tab aktif, dirty, state per tab).

## 14. Daftar periksa sebelum rilis layar baru

- [ ] Memakai layout 4 zona; tanpa sidebar.
- [ ] Maksimal satu tombol primary; tombol header berikon.
- [ ] Semua daftar punya cari (dan filter bila relevan) serta footer "X dari Y".
- [ ] Status memakai peta warna bagian 8, selalu bersama teks.
- [ ] Error pada MessageBar tidak bisa ditutup; toast memberi jenis eksplisit.
- [ ] Tab menampilkan titik belum disimpan dan ikon alert bila perlu.
- [ ] Aksi permanen memakai dialog, alasan bila wajib.
- [ ] Tidak ada nama proyek tertulis keras di komponen shell.
- [ ] Teks lewat resource ID/EN.
- [ ] Kontras, fokus keyboard, dan reduced motion sudah diperiksa.

## 15. Proses perubahan dokumen ini

1. Ajukan perubahan (komponen baru, token baru, deviasi) sebagai usulan tertulis berisi: masalah, alasan komponen atau token yang ada tidak cukup, dan rancangan.
2. Pemilik dokumen (Ary) menyetujui. Tanpa persetujuan, agent tidak boleh menambah komponen atau token.
3. Setelah disetujui: perbarui dokumen ini, naikkan versi, lalu perbarui artifact referensi. Dokumen ini yang berlaku bila berbeda.
4. Deviasi sementara di satu layar harus ditandai "DEVIASI" di spesifikasi layar dengan alasan dan tanggal tinjau ulang.

## 16. Catatan dan hal yang belum final

- Prototipe belum dirender dan diuji visual; periksa kerapatan dan kontras di layar nyata sebelum token dibekukan.
- Angka lencana 9px berada di bawah ukuran teks minimum umum; jumlah tetap terbaca di label dan panel.
- Peta status mengikuti arahan: Confirmed/Released biru, Cancelled/Void/Nonaktif abu-abu. Ubah di bagian 8 bila FSD proyek berbeda.
- Tab dengan ikon alert baru mencakup layar yang diketahui; tab form dengan galat validasi dan tab master yang belum siap perlu status validasi yang dibagi ke tab.
- Aturan keamanan (kunci akun, kebijakan kata sandi) mengikuti FSD Access Management proyek, bukan template ini.
