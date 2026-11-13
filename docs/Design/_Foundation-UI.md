# Desain — Fondasi UI GMS (Berlaku untuk Semua Modul)

| Atribut | Nilai |
|---|---|
| Versi | **3.0 (2026-11-13)** — menggantikan v2.0 (AntDesign + MegaMenu/CommandPalette + tema gelap) |
| Penulis | UI/UX Designer |
| Platform | Web (Blazor Web App). Mobile **N/A** pada fase 1. |
| Sumber | `docs/Design.md` **v2.0 (Microsoft Fluent 2)** — acuan tunggal: token, komponen, layout, navigasi. `docs/FSD/*.md`. |
| Mock-up visual | `docs/Design/mockup/index.html` (shell + Beranda + Daftar PO + Detail PO) dan `docs/Design/mockup/login.html` (Form masuk). Dibuka langsung di browser, tanpa server. |

Dokumen ini menerapkan `docs/Design.md` pada GMS. **Bila berbeda dengan `Design.md`, `Design.md` yang berlaku.** Dokumen per modul (`docs/Design/<NamaModul>-UI.md`) merujuk pola di sini dan hanya memuat hal yang spesifik modul.

Dokumen ini **tidak menyebut library UI**. Daftar komponen mengikuti `Design.md` bagian 13. Keputusan library (Fluent UI Blazor atau komponen sendiri) ada di bagian 12.

---

## 0. Ringkasan perubahan dari v2.0

| Aspek | v2.0 (dicabut) | v3.0 (berlaku) |
|---|---|---|
| Gaya | Ant Design Enterprise, biru `primary` | **Fluent 2**, brand `--brand` |
| Tema | Terang + gelap + ikuti sistem | **Terang saja** (tidak ada pengalih tema) |
| Navigasi | MegaMenu di header + CommandPalette `Ctrl K` | **Global Header** (pencarian di tengah) + **Top Navigation** (panel mega) + **Internal Tab Manager** |
| Layout | Header 56px, halaman tunggal | 4 zona: Header 48px, Top Nav 40px, Tab Manager 40px, Workspace |
| Konteks kerja | Satu halaman per navigasi | Satu **tab** per dokumen (maks 10), state dipertahankan per tab |
| Status | Cyan/biru/hijau/merah per komponen | **Satu peta 6 warna** (bagian 4). Cancelled/Void **abu-abu, bukan merah** |
| Bahasa | Indonesia tetap | **ID/EN** lewat resource, pemilih di header dan form masuk |
| Notifikasi | Tidak ada | Lonceng di header + panel 380px, **berlaku di fase 1** (butuh FSD Notifikasi, bagian 12) |
| Kerapatan | Compact/Comfortable bebas | Tinggi kontrol 32px, jarak skala 4px; Compact hanya untuk grid input |
| Form masuk | Kartu polos | Halaman penuh bergaris latar SVG bergerak + kartu 420px di tengah |

Nama komponen berubah (bagian 8): `StatusTag→StatusBadge`, `StatCard→StatTile`, `ProblemBanner→MessageBar`, `ConfirmDialog→Dialog`, `MegaMenu→TopNav`, `CommandPalette→pencarian header`.

---

## 1. Vibe dan prinsip

**"Efisien, tenang, terpercaya."** Admin mengetik angka berjam-jam, jadi prioritasnya ketepatan, kecepatan, dan mata yang tidak lelah.

- **Data dulu, dekorasi belakangan.** Permukaan netral, satu warna aksen (`--brand`); warna lain **hanya** untuk status. Dekorasi bergerak hanya di form masuk.
- **Tanpa sidebar kiri.** Navigasi hanya Header + Top Navigation; konteks kerja lewat Tab Manager.
- **Status tidak pernah hanya warna.** Selalu teks (dan ikon bila perlu).
- **Aman dulu, cepat kemudian.** Aksi permanen = dialog + alasan; validasi tampil **sebelum** menyimpan.
- **Setiap angka dapat ditindaklanjuti.** Angka ringkasan membuka daftar terkait (di tab).
- **Server pemegang aturan.** Validasi klien hanya bantuan; batas, kumulatif, stok, dan pratinjau dampak adalah **hasil server**.
- **Yang tidak berhak, tidak dirender** (menu, widget, tombol). Server tetap memeriksa.

| Aspek | Keputusan |
|---|---|
| Target layar | **≥ 1366px** dirancang penuh. 960–1365px: layout yang sama, konten menggulir. **< 960px:** Top Navigation disembunyikan, tombol menu di header membuka daftar vertikal; padding workspace 16px (Design.md bagian 3). Ponsel bukan target penggunaan fase 1. |
| Kerapatan | Tinggi kontrol dan tombol **32px**. Tabel: baris 40px (Comfortable) atau **32px (Compact)**. Compact dipakai hanya pada grid input dan tabel riwayat padat. |
| Jarak | Skala 4px (4, 8, 12, 16, 20, 24). Antar kartu 12–16px. Padding workspace 24px. |
| Ikon | Set Fluent regular, garis 1.4px, kotak 16px, ujung bulat. Ikon dekoratif `aria-hidden`. |
| Bahasa | ID (default) dan EN. Kalimat pendek; pesan galat menyebut penyebab dan letaknya. Tanpa emoji. |

---

## 2. Identitas aplikasi

Satu-satunya yang diganti per proyek (Design.md bagian 2). Diisi di `AppIdentityOptions` (bind dari `appsettings.json`); tidak ada nama tertulis keras di komponen shell.

| Nilai | GMS |
|---|---|
| Nama aplikasi | Garment Management System |
| Singkatan (header) | GMS |
| Huruf logo | G |
| Tagline / perusahaan | Manajemen produksi, stok, dan pengiriman *(nama badan hukum dikonfirmasi pemilik proyek, mengisi `AppIdentity:Company`)* |
| Tahun | 2026 |
| Footer | `© 2026 Garment Management System. Hak cipta dilindungi.` (EN: `All rights reserved.`) |

---

## 3. Shell — empat zona

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ [G] GMS  Garment Management System    [🔍 Cari halaman atau aksi…]    [ID▾] [🔔] [Budi▾] │ Header 48px  (latar --brand)
├──────────────────────────────────────────────────────────────────────────────────────┤
│ ⌂ Beranda   Penjualan▾   Produksi▾   Gudang▾   Penggajian▾   Master Data▾   Administrasi▾ │ Top Nav 40px (putih)
├──────────────────────────────────────────────────────────────────────────────────────┤
│ [⌂ Beranda] [PO-202611-0001 ⊗ ●] [⚠ WO-202611-0004 ⊗]                      [Semua tab ▾] │ Tab Manager 40px (--bg3 gelap)
├──────────────────────────────────────────────────────────────────────────────────────┤
│  Breadcrumb 12px                                                                       │
│  Judul 20/28 semibold  [Badge status]                    [⊕ Aksi sekunder] [⊕ Utama]   │ Main Workspace
│  MessageBar · Ringkasan (tile) · Stepper · Konten (kartu/tabel/form) · Aksi form        │ (latar --bg3, gulir)
│  © 2026 Garment Management System. Hak cipta dilindungi.                               │
└──────────────────────────────────────────────────────────────────────────────────────┘
```
Hanya Main Workspace yang menggulir. Satu layout untuk semua halaman setelah login; **dilarang** sidebar kiri, menu samping, atau pintasan di luar katalog.

### 3.1 Global Header
- Latar `--brand`, teks putih; fokus = outline putih.
- Kiri: kotak logo 28px + "GMS" + nama panjang (disembunyikan < 960px).
- Tengah: **pencarian halaman dan aksi** (menggantikan CommandPalette). Maks **7** hasil; tiap hasil: label, jalur menu, tag "Aksi" bila berupa aksi. Sumber: katalog navigasi yang sama dengan Top Navigation (disaring izin). Panah ↑↓ memilih, Enter membuka (di tab), Esc menutup.
- Kanan, urutan tetap: **Bahasa → Notifikasi → Pengguna**.
  - **Bahasa:** ikon globe + `ID`/`EN`; menu dua pilihan + centang.
  - **Notifikasi (fase 1, butuh FSD Notifikasi):** ikon lonceng; lencana jumlah belum dibaca di sudut kanan atas (maks `9+`, hilang bila nol, tinggi 12px, angka 9px, latar `--badge-dot`). Label tombol memuat jumlah ("Notifikasi, 3 belum dibaca"). Panel 380px: judul, ringkasan "N belum dibaca", "Tandai semua dibaca", daftar (belum dibaca: titik biru + latar `--brand-t` + teks tebal; tiap item: judul, satu baris keterangan, waktu relatif, badge jenis bila perlu), "Lihat semua notifikasi". Klik item = tandai terbaca lalu **buka dokumen terkait di tab** (fokus ke tab bila sudah terbuka). Keadaan: memuat (skeleton 3 baris), kosong ("Tidak ada notifikasi"), galat (MessageBar + Coba lagi). Item hanya untuk peristiwa yang boleh dilihat pengguna (izin dicek server).
  - **Pengguna:** avatar, nama, menu (**Ganti Password**, **Keluar**). Peran ditampilkan di baris pertama menu.
- Satu panel header terbuka pada satu waktu; klik di luar menutup.

### 3.2 Top Navigation (panel mega)
Satu tombol per kategori, ikon 16px + label; aktif: teks tebal + garis bawah `--brand` 3px. Panel lebar penuh: **grup** (kartu berbingkai, ikon di tile `--brand`) dan **item** (tile ikon, judul, deskripsi). **Item aksi** (membuat sesuatu): tile tint `--brand-t` + tanda `+`; **item halaman**: tile netral. Setiap grup dan item **wajib berikon**. Entri tanpa izin tidak dirender; grup/kategori kosong ikut hilang. Beranda = tautan langsung. Satu katalog (`NavCatalog`) dipakai Top Navigation **dan** pencarian header.

| Kategori (ikon) | Grup (ikon) | Halaman (ikon) | Aksi (ikon `+`) | Izin |
|---|---|---|---|---|
| **Beranda** (Home) | tautan langsung | Beranda | | Semua pengguna login |
| **Penjualan** (Cart) | Customer PO (DocumentBulletList) | Daftar PO (List) | Buat PO (DocumentAdd) | `CustomerPO.View` / `.Create` |
| | Pengiriman (VehicleTruck) | Daftar Surat Jalan (DocumentArrowRight) | Buat Surat Jalan | `Pengiriman.View` / `.Create` |
| **Produksi** (Building) | Work Order (ClipboardTask) | Daftar WO | Buat WO | `WorkOrder.View` / `.Create` |
| | Production Output (DataBarVertical) | Riwayat Output | Input Output · Input Penyesuaian | `ProductionOutput.View` / `.Create` / `.Adjust` |
| | Vendor (PeopleTeam) | | Kirim/Terima Vendor | `VendorJob.View` / `.Create` |
| **Gudang** (Box) | Finished Goods (BoxMultiple) | Daftar Serah Terima · Stok FG · Kartu Stok | Serah Terima FG | `SerahTerimaFG.*` · `StokFG.View` |
| | Material (Layer) | Serah Terima Material | Serah Terima Material Baru | `SerahTerimaMaterial.*` |
| | Impor (ArrowUpload) | | Impor Saldo Awal | `StokFG.Import` |
| **Penggajian** (Money) | Payroll Borongan (Wallet) | Periode Gaji | Buat Periode | `Payroll.View` / `.Create` |
| **Master Data** (Database) | Produk (Tag) | Finished Goods · Material · Kategori Material | | `MasterData.<Resource>.View` |
| | Atribut (Options) | Size · Color · Satuan · Process | | |
| | Mitra dan SDM (People) | Customer · Vendor · Pekerja | | |
| | Impor (ArrowUpload) | | Impor FG · Material · Pekerja | `MasterData.<Resource>.Import` |
| **Administrasi** (ShieldKeyhole) | Hak Akses (LockClosed) | Pengguna · Role dan Hak Akses · Audit Log | Tambah Pengguna · Tambah Role | `AccessManagement.*` |
| | Notifikasi (Alert) | Pengaturan Notifikasi (Settings) | | `Notifikasi.Pengaturan.View` |

Nama ikon mengikuti set Fluent regular (nama generik; Frontend memetakannya). Dari URL langsung tanpa hak: halaman 403 (Static SSR).

---

## 4. Internal Tab Manager (penerapan GMS)

Aturan dasar mengikuti Design.md bagian 6. Keputusan khusus GMS:

| Aturan | Keputusan GMS |
|---|---|
| ID tab | `layar:nomor-dokumen`. Membuka dokumen yang sama **memfokuskan** tab yang ada. |
| Tab Beranda | Terpaku, tidak bisa ditutup. |
| Maksimal | 10 tab. Saat penuh, membuka dokumen baru ditolak dengan **MessageBar peringatan** di workspace aktif ("Batas 10 tab tercapai. Tutup satu tab untuk membuka dokumen baru."). Tidak ada penutupan diam-diam. |
| Mode detail/ubah | **Satu tab per dokumen**: detail dan mode Ubah berbagi tab yang sama (route berganti, ID tab tetap). |
| Dokumen baru | Satu tab "baru" per layar (`po:baru`, `wo:baru`, `sj:baru`...). Membuka lagi memfokuskan tab itu. |
| Belum disimpan | Titik oranye + teks tersembunyi "belum disimpan". Menutup tab meminta konfirmasi (Dialog 480px: [Tetap di sini] [Tutup tanpa menyimpan]). Berlaku untuk semua form, grid input (Output, Serah Terima, Kirim/Terima, Tarif), editor Role. Pencarian dan filter **tidak** menandai tab belum disimpan. |
| Alert pada tab | Ikon di depan judul. **Merah (lingkaran berseru)** = error atau keterlambatan; **oranye (segitiga berseru)** = peringatan. Hilang otomatis saat masalah selesai. Tooltip dan teks pembaca layar menyebut alasannya. |
| State per tab | Pencarian, filter chip, urutan, halaman, sub-tab (inner tab), dan isi form dipertahankan per tab selama sesi. URL tetap mendukung tautan langsung (`?tab=`), tetapi sumber kebenarannya service Tab Manager. |
| Klik kanan | Muat ulang · Tutup · Tutup lainnya · Tutup semua. "Semua tab" menampilkan daftar saat meluap. |

### 4.1 ID tab dan alert per layar

| Layar | ID tab | Alert (usulan, tinjau ulang saat QA) |
|---|---|---|
| Beranda | `home` | – |
| Daftar PO / PO | `po:list` · `po:{PO-…}` · `po:baru` | Merah: ada baris PO terlambat |
| Daftar WO / WO | `wo:list` · `wo:{WO-…}` · `wo:baru` | Merah: WO terlambat. Oranye: FG belum siap pada WO Draft |
| Input Output | `output:input:{WO-…}` | Oranye: sel melebihi Qty WO (peringatan) · Merah: sel melebihi batas |
| Penyesuaian Output | `output:adjust` | – |
| Kirim/Terima Vendor | `output:vendor:{WO-…}` | – |
| Riwayat Produksi | `output:history` | – |
| Serah Terima FG | `fg:list` · `fg:{id}` · `fg:baru:{WO-…}` | – |
| Stok FG / Kartu Stok | `stock` · `stock:ledger:{FG}:{Color}:{Size}` | – |
| Serah Terima Material | `mat:list` · `mat:{id}` · `mat:baru` | – |
| Surat Jalan | `sj:list` · `sj:{SJ-…}` · `sj:baru` | – |
| Periode Gaji | `pay:list` · `pay:{GJ-…}` | Oranye: periode Open yang tanggal selesainya sudah lewat |
| Master Data | `master:{entitas}` · `master:fg:{kode}` · `master:{entitas}:import` | Oranye: FG **Belum Siap** |
| Notifikasi | `notif:all` · `admin:notif-settings` | – |
| Pengguna / Role / Audit | `admin:users` · `admin:user:{username}` · `admin:roles` · `admin:role:{nama}` · `admin:audit` | – |

---

## 5. Main Workspace

Urutan tetap dari atas (Design.md bagian 7):

1. **PageHeader** — breadcrumb 12px, judul 20/28 semibold, **badge status**, tombol aksi di kanan; jarak bawah 14px; tab dalam menempel ke garis bawah header.
2. **MessageBar** — info, peringatan, sukses, error. Info/peringatan/sukses dapat ditutup (×); **error tidak dapat ditutup**.
3. **Ringkasan angka** — **StatTile** terpisah: bingkai 1px, aksen atas 3px `--brand`, hover terangkat; angka 24/32 semibold `tabular-nums`; bila dapat ditindaklanjuti, seluruh tile membuka daftar.
4. **Siklus status** (Stepper) bila dokumen bertahap.
5. **Konten** — kartu, tabel, form, grid.
6. **Aksi form** (kartu footer) — catatan di kiri, tombol di kanan; satu tombol primary.
7. **Footer copyright.**

**Aturan tombol:**
- **Maksimal satu primary per halaman.** Halaman tanpa aksi dominan (mis. Beranda) tidak punya primary.
- Tombol di PageHeader: **ikon 16px di kiri teks** (Buat, Ubah, Batalkan, Konfirmasi, Cetak, Ekspor...). Tombol footer form (Simpan, Batal): **tanpa ikon**.
- Tombol hanya-ikon (mis. Segarkan): wajib `aria-label` dan tooltip.
- **Bahaya (`dng`)** hanya untuk aksi merusak (Void, Batalkan dokumen); nonaktif: latar `--s3`, teks abu, dengan tooltip alasan.
- Aksi permanen (Batalkan, Void, Tutup periode) = Dialog; alasan wajib bila FSD menyatakannya.
- Teks tombol = **kata kerja + objek** ("Simpan Draft", "Buat PO"). Dilarang "OK", "Submit", "Ya/Tidak".

### 5.1 Pola halaman

**Daftar** (`DataList`): PageHeader (judul + jumlah, aksi utama bila berhak) → MessageBar bila perlu → **kartu**: baris atas **pencarian (wajib)** + filter dropdown + tombol Reset → baris **filter chip** dengan jumlah (hanya bila ada kategori bermakna: status, terlambat, ...) → tabel → footer **"Menampilkan X dari Y"** + pagination. Filter dan pencarian dipertahankan per tab. Server-side paging (default 25). Header tabel lengket; angka rata kanan `tabular-nums`; kolom pertama lengket pada matriks lebar (gulir horizontal di dalam kartu).

**Form:** satu kolom (maks ± 720px) dalam kartu, seksi diberi judul. Label di atas field; kolom wajib bertanda `*` merah (`aria-hidden`) + `aria-required`. Validasi: saat blur + **ringkasan** MessageBar error di atas saat Simpan gagal (fokus pindah ke ringkasan; tiap butir menaut ke field). Footer aksi (kartu): catatan kiri, [Batal] [Simpan] kanan; saat menyimpan, tombol nonaktif + spinner (cegah dobel kirim).

**Detail:** PageHeader (kode, badge status, aksi kontekstual sesuai status dan izin) → Ringkasan (StatTile) → Stepper → inner tabs (Alokasi, WO, Riwayat...). Tab dalam tersimpan per tab.

**Input (Design.md "Grid input"):** batas tersedia terlihat **sebelum** mengetik; sel melebihi batas bergaris merah + pesan "Maks N"; Simpan dinonaktifkan atau diblokir dengan penjelasan.

### 5.2 Dialog

Lebar **480px** (varian **lebar 720px** hanya untuk dialog pratinjau dampak, disetujui 2026-11-13), radius 8px, bayangan `--sh64`, latar gelap 40%. Fokus terkurung; Esc menutup (kecuali saat memproses); fokus kembali ke pemicu.

| Jenis | Pola |
|---|---|
| Konfirmasi biasa | Judul pertanyaan, penjelasan dampak, [Batal] [kata kerja + objek] |
| **Aksi dengan alasan wajib** (Void, Koreksi, Cancel, Reopen, Penyesuaian, Lepas alokasi) | Ringkasan dampak, kolom **Alasan** wajib, tombol **bahaya** di kanan (nonaktif sampai alasan terisi) |
| **Pratinjau dampak** | Tabel sebelum/sesudah. Bila ada pelanggaran: pesan penahan + tombol utama nonaktif. *Lihat DEVIASI-1 (lebar).* |
| Permanen (Tutup periode gaji) | Ringkasan angka, peringatan "tidak dapat dibuka kembali", konfirmasi dengan mengetik kode periode |

---

## 6. Status dan warna semantik (peta tunggal, Design.md bagian 8)

Satu peta untuk **seluruh** badge, stepper, bar progres, dan chart. **Merah hanya untuk keterlambatan dan galat. Cancelled/Void/Nonaktif bukan merah.**

| Kelas | Makna | Status GMS (badge **selalu berteks**) |
|---|---|---|
| biru `t-proc` | Draft, belum mulai, terjadwal | Draft · Confirmed · Released · Belum Mulai |
| kuning `t-prog` | Sedang berjalan | In Progress · Open (Periode Gaji) · Berjalan |
| hijau `t-ok` | Selesai | Completed · Closed (WO, Periode Gaji) · Selesai · Aktif · Siap Produksi · Aktif (dokumen tidak void) |
| abu `t-obs` | Obsolete, batal, nonaktif | Cancelled · Voided · Nonaktif |
| merah `t-err` | Terlambat atau galat | Terlambat · Terkunci (akun) · Negatif |
| oranye `t-warn` | Perlu perhatian | Jatuh tempo · Belum Final · Belum Siap · "Belum ada" (No. PO Customer) |

Nama status dokumen tetap Inggris (FSD). Kondisi turunan memakai bahasa UI. Badge: teks gelap pada latar tint (kontras ≥ 4.5:1), pil radius 10px, ikon opsional 12px di depan teks.

**Stepper (siklus status):** lingkaran di atas; label dan keterangan di bawah; garis penghubung tidak melewati teks. Selesai = lingkaran putih garis **hijau** + centang + keterangan "Selesai", garis hijau. Aktif: Draft/Released = biru bernomor; In Progress/Open = kuning bernomor; status akhir = hijau. Berikutnya = abu bernomor + "Belum".

| Dokumen | Langkah stepper |
|---|---|
| Customer PO | Draft → Confirmed → In Progress → Completed *(Cancelled = badge abu di PageHeader, stepper disembunyikan)* |
| Work Order | Draft → Released → In Progress → Completed → Closed *(Cancelled idem)* |
| Periode Gaji | Open → Closed |
| Wizard Impor | Unggah → Periksa → Konfirmasi |

**Bar progres:** berjalan = kuning, selesai (≥100%) = hijau, terlambat = merah; `role="progressbar"` + nilai teks. **Bar PO bertumpuk:** Terkirim = hijau, Dialokasikan = biru, Dalam Produksi = kuning, sisa = `--s3`; legenda + angka tertulis.

---

## 7. Format data dan konten (Design.md bagian 12)

| Data | ID | EN | Catatan GMS |
|---|---|---|---|
| Angka / qty | `1.240` | `1,240` | `id-ID` / `en-US` eksplisit di UI; konversi ke Invariant sebelum ke API |
| Satuan qty | `1.240 pcs` | | Qty material: hingga 3 desimal (`120,500 MTR`) |
| Uang | `Rp 18.420.500` | | Total tanpa desimal |
| **Tarif per unit** | `Rp 1.250,5` | | Jumlah desimal **mengikuti FSD** (saat ini hingga 5 digit, tanpa nol ekor). Aturan umum Design.md "uang tanpa desimal" berlaku untuk total |
| Tanggal (teks) | `14 Nov 2026` | `Nov 14, 2026` | **Berubah dari v2.0** (sebelumnya `08 Nov 2026`: tanpa nol di depan) |
| Tanggal (input) | `DD/MM/YYYY` | | |
| Waktu | `HH:mm` | | Zona aplikasi (WIB) |
| Nomor dokumen | `PO-202611-0142` | | Font mono, `tabular-nums` |
| Qty negatif | `−20` | | Tanda minus + teks merah (Negatif) |

Sentence case ("Buat Dokumen"). Empty state informatif, bukan "No data". Pesan galat: penyebab + letak ("Melebihi batas tersedia: Navy / M (maks 110)").

---

## 8. Komponen (kontrak Design.md bagian 13) dan pemetaan dari nama lama

| Komponen baru (Design.md) | Menggantikan | Catatan |
|---|---|---|
| `AppHeader` (+ `LanguageMenu`, `NotificationPanel`) | header lama + `CommandPalette` | Pencarian di tengah |
| `TopNav` | `MegaMenu` | Panel mega, katalog sama |
| `TabManager` (service berstatus per sesi) | – (baru) | Daftar tab, aktif, dirty, state per tab |
| `Workspace`, `PageHeader` | `PageHeader` | Breadcrumb + status + aksi |
| `MessageBar` | `ProblemBanner` | Error tidak dapat ditutup |
| `StatusBadge` | `StatusTag` | Peta 6 warna |
| `StatTile` | `StatCard` | Aksen atas 3px |
| `Stepper` | – (baru) | Siklus status |
| `DataList` | `gms-table` + `PagerBar` | Cari, filter chip, footer X dari Y |
| `ToastHost` | `IGmsToast` | Sukses/error/info eksplisit |
| `Dialog` | `ConfirmDialog` | 480px |
| `QtyGrid` | `QtyGrid` | Pola "Grid input"; tetap |
| `StackedProgress`, `ProgressMatrix` | sama | Warna menurut bagian 6 |
| `ImportWizard`, `MasterList<T>`, `TextField`/`LookupSelect` | sama | Memakai token baru |
| Chart (`ChartStack`, `ChartBars`) | – (baru) | **Tanpa donut** |

**Chart:** komposisi = bar tumpuk satu baris + daftar bar horizontal (label, bar, jumlah, persen; baris dapat diklik); tren harian = bar vertikal bernilai di atas bar. Semua chart: tooltip, hover menyorot, `role="img"` + `aria-label`, **angka tertulis**; tinggi bar 120px.

**Toast:** tengah atas, hilang otomatis (error 4 detik, lebih lama). Sukses hijau (ikon centang, garis kiri), error merah (ikon silang, `role="alert"`), info biru. **Pemanggil mengirim jenis secara eksplisit.**

### 8.1 QtyGrid (matriks Color × Size) — penerapan "Grid input"

```
              S      M      L      XL     2XL   │ Total
Navy        [ 20 ] [ 30 ] [ 10 ] [    ] [    ]  │   60
  maks:       80     75     40    ──     ──      │
Orange      [    ] [ 15 ] [    ] [    ] [    ]  │   15
  maks:       ──     60     ──    ──     ──      │
Total          20     45     10      0      0    │   75
```
Kolom Size mengikuti Urutan Tampil. Hint **"maks N"** di bawah sel; melebihi batas → sel merah + pesan "Maks N" (ikon + teks). Sel tidak berlaku = "—" nonaktif. Peringatan (melebihi Qty WO proses pertama) = oranye + ikon. Keyboard: Tab/Shift+Tab, Enter ke bawah, panah, Home/End, Ctrl+V tempel blok *(fase 2 untuk sebagian grid; lihat CHECKPOINT)*. Kolom pertama dan header lengket. Nama sel aksesibel ("Navy, Size M, qty").

### 8.2 Input (Design.md bagian 9)
Border 1px `--s1`, garis bawah `--fg3` (batas aksesibel). Fokus: garis bawah 2px `--brand`. Invalid: border + garis bawah merah, latar tint merah, pesan 12px di bawah kolom. Tinggi 32px.

---

## 9. State UI (wajib setiap halaman)

| State | Pola |
|---|---|
| **Loading awal** | **Skeleton** meniru bentuk konten. Bukan spinner penuh halaman |
| **Loading ulang** | Konten lama tetap, indikator tipis di atas, tombol terkait nonaktif |
| **Kosong (belum ada data)** | Judul + penjelasan + **tombol aksi** bila berhak ("Buat PO pertama") |
| **Kosong (hasil filter)** | "Tidak ada hasil untuk filter ini" + Reset |
| **Error** | MessageBar error (tidak dapat ditutup): penyebab, **ID rujukan** (`traceId`), tombol **Coba lagi** |

| Kode | Tampilan |
|---|---|
| 400/422 | Galat validasi: tandai field + ringkasan |
| 401 | Dialihkan ke login dengan `returnUrl` |
| 403 | Halaman "Akses ditolak" (Static SSR, layout kartu tengah) |
| 404 | "Data tidak ditemukan" + tautan ke daftar. Tab tetap terbuka dan dapat ditutup |
| 409 (konkurensi) | MessageBar peringatan "Data telah diubah pengguna lain" + [Muat ulang]; input dipertahankan |
| 409 (aturan bisnis) | MessageBar/dialog menjelaskan **penahan** (sel, qty, dokumen) |
| 5xx | Error umum + traceId + Coba lagi |
| Koneksi Blazor putus | MessageBar "Menyambung ulang…"; gagal → tombol Muat ulang; input belum tersimpan diperingatkan |

---

## 10. Form masuk dan halaman Static SSR (Design.md bagian 10)

Halaman penuh **di luar** layout 4 zona; dipakai Login, Ganti Password, Akses Ditolak, Sesi Berakhir.
- Latar **SVG bergerak bertema bisnis** memenuhi layar (`position: fixed`, 100vw × 100vh): gradasi biru tua → `--brand`, kisi tipis, bar naik-turun, garis tren, titik data, gelombang, bentuk melayang. Redup. **Mati pada `prefers-reduced-motion`.**
- Kartu putih **420px** di tengah layar (horizontal dan vertikal), bayangan `--sh16`/panel.
- Isi (Login): logo + nama, judul "Masuk", Nama pengguna, Kata sandi (tombol tampil/sembunyi), tombol Masuk (status memuat), tautan "Lupa kata sandi? Hubungi Administrator", toggle ID/EN di pojok kanan atas, footer copyright di dasar layar.
- Galat kredensial: MessageBar error `role="alert"`; pesan **tidak membedakan** nama pengguna atau kata sandi yang salah.
- Mock-up: `docs/Design/mockup/login.html`.

---

## 11. Aksesibilitas (WCAG 2.1 AA, Design.md bagian 11)

- Kontras teks ≥ 4.5:1, non-teks ≥ 3:1. Badge: teks gelap pada latar tint.
- Semua kontrol dapat diakses keyboard; fokus terlihat (outline 2px); tautan lompat "Lewati ke konten utama".
- Status: teks + warna. Ikon dekoratif `aria-hidden`; ikon bermakna punya teks alternatif.
- ARIA: `tablist`/`tab` (Tab Manager), `menu`, `alert` (error), `status` (info/sukses), `progressbar`. Tab dengan titik belum disimpan dan ikon alert membawa teks tersembunyi.
- Lencana notifikasi dan elemen kecil lain bukan satu-satunya pembawa informasi (jumlah ada di label tombol).
- Grid: `role="grid"`; nama sel menyebut baris dan kolom. Seret-lepas **selalu** punya alternatif keyboard.
- Target klik ≥ 24px. Zoom 200% dapat dipakai. Hormati `prefers-reduced-motion` (animasi kartu, latar login).
- `lang` mengikuti bahasa aktif (`id` / `en`).

---

## 12. Keputusan terbuka, usulan perubahan Design.md, dan deviasi

Sesuai Design.md bagian 15, status keputusan. K-1, K-2, U-1 **disetujui 2026-11-13**; N-1 menunggu FSD Notifikasi.

| # | Jenis | Masalah | Usulan | Status |
|---|---|---|---|---|
| **K-1** | Keputusan | Design.md 13: pustaka Fluent 2 (`Microsoft.FluentUI.AspNetCore.Components`) butuh **izin eksplisit**; bila ditolak, komponen dibangun sendiri dengan token. Aplikasi saat ini memakai AntDesign Blazor (CLAUDE.md dan CHECKPOINT menyebutnya). | **Bangun komponen sendiri** dengan CSS token (`tokens.css`) agar identik dengan mock-up dan tidak mewarisi gaya Ant. Fluent UI Blazor boleh dipakai sebagai alternatif bila Ary memberi izin. | **DISETUJUI Ary 2026-11-13:** komponen dibangun sendiri dengan CSS token; tanpa paket UI baru (tidak perlu izin NuGet). |
| **K-2** | Keputusan | i18n ID/EN (Design.md 12) berarti seluruh teks lewat `.resx`. Aplikasi sekarang berbahasa Indonesia tertulis di komponen. | Fase 1: seluruh chrome (header, nav, tombol, status, pesan sistem) dua bahasa; teks per-halaman ID dulu dengan resource key siap diisi EN. Perlu keputusan apakah master data diterjemahkan. | **DISETUJUI Ary 2026-11-13:** infrastruktur resource ID/EN penuh; English diterjemahkan untuk chrome (header, menu, tombol umum, status, pesan sistem, form masuk, notifikasi); teks halaman modul ID dulu dengan kunci resource; data master tidak diterjemahkan. Ini deviasi yang disetujui dari Design.md bagian 12. |
| **U-1 / DEVIASI-1** | Usulan komponen | Dialog **480px** terlalu sempit untuk pratinjau dampak (tabel 5–6 kolom: Void/Koreksi Output, Serah Terima, Pengiriman) dan grid qty. | Tambah varian **Dialog lebar 720px** khusus pratinjau dampak. **Sampai disetujui:** tetap 480px dengan tabel dampak bergulir horizontal di dalam dialog; grid qty tidak dimasukkan ke dialog (halaman/tab). | **DISETUJUI Ary 2026-11-13:** varian Dialog lebar 720px khusus pratinjau dampak (bukan grid input). Koreksi Output/Pengiriman dan Ubah Qty WO tetap halaman. |
| ~~DEVIASI-2~~ | Diputuskan | Presisi desimal tarif. | **Keputusan Ary (2026-11-13):** tarif mengikuti FSD; Design.md akan mengikuti jumlah desimal yang ditetapkan FSD. Bukan deviasi. | **Selesai** |
| **N-1** | Keputusan | Lonceng notifikasi harus berlaku di fase 1, tetapi belum ada FSD, API, maupun penyimpanan notifikasi. | **Keputusan Ary (2026-11-13): diterapkan langsung di fase 1.** Prasyarat: System Analyst menulis `docs/FSD/Notifikasi.md` (peristiwa pemicu, penerima per role/izin, retensi, polling vs real-time), lalu Architect/Backend menyediakan kontrak dan penyimpanan. UI sudah dirancang (bagian 3.1) dan ada di mock-up. | Menunggu FSD |
| **U-2** | Usulan | Alert ikon pada tab (bagian 4.1) baru mencakup layar yang diketahui (Design.md 16). | Daftar di bagian 4.1 dipakai sebagai awal. | Berlaku sementara |
| ~~I-1~~ | Selesai | CLAUDE.md | Sudah diperbaiki oleh Ary (netral-framework, merujuk Design.md). | **Selesai** |

---

## 13. Dokumen cetak (PDF)

Slip Gaji dan Surat Jalan adalah PDF A4, bukan halaman web; token UI **tidak berlaku** (Design.md mengatur UI). Prinsip bersama: font berlisensi terbuka, kop dari konfigurasi, tabel rapat dan terbaca saat dicetak hitam-putih, penanda "BELUM FINAL"/"DIBATALKAN"/"CETAK ULANG" sebagai watermark teks. Tata letak tiap dokumen ada di dokumen modulnya (tidak berubah dari v2.0).

---

## 14. Daftar periksa rilis layar (Design.md bagian 14, versi GMS)

- [ ] Layout 4 zona; tanpa sidebar.
- [ ] Maksimal satu tombol primary; tombol header berikon; tombol footer tanpa ikon.
- [ ] Semua daftar: pencarian (+ filter chip bila relevan) dan footer "Menampilkan X dari Y".
- [ ] Status memakai peta bagian 6, selalu bersama teks. Cancelled/Void bukan merah.
- [ ] Error pada MessageBar tidak dapat ditutup; toast memberi jenis eksplisit.
- [ ] Tab punya ID yang benar, titik belum disimpan, dan ikon alert bila perlu.
- [ ] Aksi permanen = Dialog 480px, alasan bila wajib.
- [ ] Tidak ada nama proyek tertulis keras di komponen shell.
- [ ] Teks lewat resource ID/EN.
- [ ] Kontras, fokus keyboard, dan reduced motion diperiksa.
- [ ] Hanya token (`--brand`, `--fg1`, ...); tidak ada hex/px mentah di luar `tokens.css`.
