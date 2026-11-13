# Desain UI — Access Management

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/AccessManagement.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` **v3.0 (Fluent 2)** (pola daftar/form/detail/dialog, state, a11y berlaku) |
| Platform | Web (Fluent 2, Design.md v2.0). Mobile N/A. |
| Render Mode | Login, Logout, Ganti Password, 403 = **Static SSR**. Lainnya = **Interactive Server**. |

---

## 1. Halaman dan Rute

| Halaman | Rute | Mode | Permission |
|---|---|---|---|
| Login | `/masuk` | Static SSR | Publik |
| Ganti Password (wajib/sukarela) | `/akun/ganti-password` | Static SSR | Login |
| Akses Ditolak | `/akses-ditolak` | Static SSR | – |
| Daftar Pengguna | `/admin/pengguna` | Interactive | Administrator |
| Tambah Pengguna | `/admin/pengguna/baru` | Interactive | Administrator |
| Detail/Ubah Pengguna | `/admin/pengguna/{id}` | Interactive | Administrator |
| Daftar Role | `/admin/role` | Interactive | Administrator |
| Editor Role + Matriks | `/admin/role/{id}` dan `/admin/role/baru` | Interactive | Administrator |
| Audit Log | `/admin/audit` | Interactive | Administrator |

---

## 2. Wireframe

### 2.1 Login (SUDAH DIGANTI: lihat "Form masuk" di Revisi Fluent 2, di bawah)
```
              ┌────────────────────────────────────┐
              │  GMS                               │
              │  Garment Management System         │
              │                                    │
              │  Masuk                             │
              │  [!] Pesan galat/info (jika ada)   │
              │  Username   [__________________]   │
              │  Password   [__________________]   │
              │  [        Masuk        ]           │
              └────────────────────────────────────┘
```
- Pesan: "Username atau password salah." (generik, termasuk akun nonaktif). Akun terkunci: "Terlalu banyak percobaan. Coba lagi dalam 15 menit atau hubungi Administrator."
- Info sesi digantikan: "Akun Anda digunakan di perangkat lain. Silakan masuk kembali."
- Fokus awal pada Username. Enter mengirim form. `returnUrl` dipertahankan.

### 2.2 Ganti Password
```
Ganti Password                             (wajib: "Anda harus mengganti password sebelum melanjutkan")
Password lama   [________]
Password baru   [________]   ✓ Min. 8 karakter   ✓ Ada huruf   ✗ Ada angka
Ulangi password [________]
[ Simpan password ]   [ Batal ] (hanya jika sukarela)
```
Mode wajib: tanpa navigasi dan tanpa tombol Batal; hanya tombol Keluar.

### 2.3 Daftar Pengguna
```
Pengguna (6)                                                   [ + Tambah Pengguna ]
[🔍 Cari username/nama]  [Role ▾]  [Status ▾]
Username    Nama           Role            Status            Login terakhir   Aksi
budi        Budi S.        Admin Produksi  ● Aktif           08 Nov 08:12     ⋯
siti        Siti R.        Admin Gaji      ⛔ Terkunci        07 Nov 16:40     ⋯
andi        Andi P.        Comfi           ○ Nonaktif         01 Nov 10:02     ⋯
```
Menu `⋯`: Lihat/Ubah, Reset Password, Buka Kunci (hanya bila terkunci), Akhiri Sesi (hanya bila aktif login), Nonaktifkan/Aktifkan.

### 2.4 Form/Detail Pengguna
```
← Pengguna / budi                                    [Status ● Aktif]
┌ Data Pengguna ─────────────────────────────┐ ┌ Status Akun ─────────────────────┐
│ Username    budi            (terkunci)     │ │ Login terakhir  08 Nov 08:12     │
│ Nama lengkap [Budi Santoso]                │ │ Sesi aktif      Ya · 10.0.0.14   │
│ Role        [Admin Produksi ▾]             │ │ Gagal login     0                │
│ (baru) Password awal [____] [Buat otomatis]│ │ [Reset Password] [Akhiri Sesi]   │
└────────────────────────────────────────────┘ │ [Nonaktifkan]                    │
[Batal] [Simpan]                                └──────────────────────────────────┘
```
- Pada mode **Tambah**: field Password awal dengan daftar ketentuan dan tombol **Buat otomatis** (tampil sekali dengan tombol Salin). Info: "Pengguna wajib mengganti password saat login pertama."
- Pada mode **Ubah**: Username terkunci. Role dapat diubah. Menurunkan Administrator terakhir ditolak dengan pesan dari server.

### 2.5 Dialog Reset Password
Judul "Reset password budi". Pilih: [Buat otomatis] atau ketik manual. Menampilkan password sementara **sekali** dengan tombol Salin dan peringatan "Catat sekarang, tidak ditampilkan lagi". Tombol: [Batal] [Reset].

### 2.6 Editor Role + Matriks Permission
```
← Role / Admin Produksi                                  [Dipakai oleh 2 pengguna]
Nama [Admin Produksi]   Deskripsi [Input output dan serah terima di Konveksi]
[🔍 Cari resource]                                   Terpilih: 12 permission
                              View  Create  Edit  Void  Correct  Adjust  Print ...
▾ PRODUKSI
  ProductionOutput             ☑     ☑      –     ☑     ☑       ☐       –
  VendorJob                    ☑     ☑      –     ☑     –       –       –
▾ GUDANG FG
  SerahTerimaFG                ☑     ☑      –     ☑     ☑       –       –
  StokFG                       ☑     –      –     –     –       ☐       –
▾ MASTER DATA
  MasterData.Pekerja           ☑     ☑      ☑     –     –       –       –
▾ ADMINISTRASI  🔒 Hanya Administrator (tidak dapat diberikan)
[Batal] [Simpan]
```
Aturan UI:
- Kolom = gabungan semua Action. Sel tanpa Action untuk resource itu menampilkan "–" (tidak fokus).
- Mencentang Create/Edit/Void/dst. **otomatis mencentang View** pada baris itu (AC-AM-18). Menghapus centang View yang membuat aksi lain otomatis lepas, dengan toast "Permission lain pada baris ini ikut dilepas".
- Pintasan: centang header kolom (semua resource), centang awal baris (semua aksi baris), dan tombol "Hanya baca" per grup.
- Grup Administrasi ditampilkan terkunci (disabled) dengan ikon gembok dan penjelasan.
- Role **Administrator** (sistem): halaman hanya baca, banner "Role sistem tidak dapat diubah".
- Saat Simpan: dialog ringkasan "Tambah 3, lepas 1 permission. Berlaku untuk 2 pengguna pada aksi berikutnya."
- Hapus Role: tombol di menu, dinonaktifkan dengan tooltip bila dipakai pengguna ("Dipakai oleh N pengguna").

### 2.7 Audit Log
```
Audit Log
Tanggal [01 Nov – 08 Nov 2026]  Pelaku [Semua ▾]  Peristiwa [Semua ▾]  [Terapkan]
Waktu             Pelaku  Peristiwa              Objek            IP
08 Nov 08:12:03   budi    Login berhasil         –                10.0.0.14
08 Nov 07:55:41   siti    Role diubah            Role: Admin Gaji 10.0.0.9   ▸
 └ Sebelum/Sesudah: tabel dua kolom perubahan (tanpa data rahasia)
```
Rentang tanggal wajib (default 7 hari). Baris dapat diperluas untuk melihat nilai sebelum/sesudah. Tidak ada tombol ubah/hapus.

### 2.8 Akses Ditolak (403)
Judul "Anda tidak memiliki akses ke halaman ini", penjelasan singkat, tombol [Kembali ke Beranda]. Tidak membocorkan nama permission.

---

## 3. Interaksi dan Aturan UI Khusus

| Hal | Aturan |
|---|---|
| Peringatan sesi habis | Usulan desain: 2 menit sebelum timeout 30 menit, dialog "Sesi akan berakhir. Tetap masuk?" dengan hitung mundur. Melanjutkan memperpanjang sesi. Tidak mengubah FSD. |
| Sesi digantikan | Sesi lama pada interaksi berikutnya diarahkan ke Login dengan pesan khusus (AC-AM-06a). |
| Pengakhiran sesi paksa | Dialog konfirmasi "Akhiri sesi budi? Pengguna akan keluar pada aksi berikutnya." |
| Nonaktifkan pengguna | Dialog konfirmasi dampak. Bila Administrator terakhir atau diri sendiri: tombol nonaktif dengan penjelasan. |
| Konkurensi (409) | Banner "Data telah diubah pengguna lain" + [Muat ulang]. |

---

## 4. State UI

| Halaman | Loading | Kosong | Error |
|---|---|---|---|
| Daftar Pengguna | Skeleton 6 baris | "Belum ada pengguna" (hampir mustahil karena ada Administrator) | Banner Problem Details + Coba lagi |
| Daftar Role | Skeleton 4 baris | "Belum ada role selain Administrator" + [Tambah Role] | idem |
| Matriks Role | Skeleton matriks (grup dan baris) | – | Gagal muat katalog: banner + Coba lagi, Simpan nonaktif |
| Audit Log | Skeleton 10 baris | "Tidak ada kejadian pada rentang ini" + ubah rentang | idem. Rentang terlalu lebar: pesan agar dipersempit |
| Login/Ganti Password | Tombol berstatus memproses | – | Pesan di atas form (lihat 2.1/2.2) |

---

## 5. Aksesibilitas

- Form login/ganti password: label terhubung, `autocomplete` benar (`username`, `current-password`, `new-password`), galat dibacakan (`role="alert"`).
- Daftar ketentuan password memakai ikon + teks (✓/✗) dan `aria-live="polite"` saat berubah.
- Matriks permission: `role="grid"`, tiap sel diberi nama "ProductionOutput, Void" dan status centang. Panah untuk berpindah sel, Spasi untuk mencentang. Header kolom dan baris terhubung.
- Status pengguna (Aktif/Terkunci/Nonaktif) berupa ikon + teks.
- Password sementara: dapat dibaca pembaca layar, tombol Salin memberi umpan balik `aria-live`.
- Dialog hitung mundur sesi menggunakan `role="alertdialog"`.

---

## 6. Catatan untuk Frontend

- Halaman Static SSR memakai layout terpisah tanpa shell. Anti-forgery wajib.
- Matriks permission dibangun dari katalog permission yang dikirim server (jangan hardcode), dikelompokkan per `Module`.
- Seluruh visibilitas tombol berasal dari permission, tetapi penegakan tetap di server.

---

## Revisi Fluent 2 (v3.0, 2026-11-13)

Bagian ini **menggantikan** wireframe, pola, dan istilah lama yang bertentangan. Acuan: `docs/Design.md` v2.0 (Fluent 2) dan `docs/Design/_Foundation-UI.md` v3.0. Mock-up visual: `docs/Design/mockup/index.html`.

> Glyph pada wireframe di dokumen ini (⏰ ⚠ ✓ ◔ ⛔ 🔒 ⋯ ▾) hanyalah **notasi**. UI memakai ikon Fluent 16px + teks, tanpa emoji (Design.md bagian 12).

### Tab Manager
| Layar | ID tab |
|---|---|
| Daftar Pengguna | `admin:users` |
| Detail/Ubah Pengguna | `admin:user:{username}` (Tambah: `admin:user:baru`) |
| Daftar Role | `admin:roles` |
| Editor Role | `admin:role:{nama}` (Tambah: `admin:role:baru`) |
| Audit Log | `admin:audit` |

Form Pengguna, editor Role (matriks izin), dan form Ganti Password sukarela menandai tab **belum disimpan** (titik oranye) saat ada perubahan.

### Form masuk (menggantikan 2.1)
Halaman penuh di luar layout 4 zona, **mock-up `docs/Design/mockup/login.html`**:
- Latar SVG bergerak bertema bisnis (`position: fixed`, 100vw×100vh), redup, **mati pada `prefers-reduced-motion`**.
- Kartu putih **420px** di tengah layar, bayangan panel. Isi: logo "G" + "GMS" + nama aplikasi, judul **Masuk**, **Nama pengguna**, **Kata sandi** (tombol tampil/sembunyi, `aria-pressed`), tombol **Masuk** (status memuat, nonaktif saat proses), tautan "Lupa kata sandi? Hubungi Administrator".
- Toggle **ID/EN** di pojok kanan atas, footer copyright di dasar layar.
- Galat = `MessageBar` error `role="alert"`, tidak dapat ditutup. Pesan generik: "Nama pengguna atau kata sandi salah." (juga untuk akun nonaktif). Terkunci: "Terlalu banyak percobaan. Coba lagi dalam 15 menit atau hubungi Administrator." Sesi digantikan: `MessageBar` peringatan "Akun Anda digunakan di perangkat lain. Silakan masuk kembali."
- Fokus awal pada Nama pengguna; Enter mengirim; `returnUrl` dipertahankan. Label teks mengikuti Design.md ("Nama pengguna", "Kata sandi"), menggantikan "Username/Password".

### Ganti Password dan Akses Ditolak (Static SSR)
Memakai latar dan kartu 420px yang sama dengan form masuk (tanpa 4 zona). Ganti password **wajib**: tanpa navigasi, tanpa Batal, hanya tombol Keluar. **Sukarela** (dari menu pengguna): halaman kartu yang sama dengan tombol Batal dan tautan kembali ke Beranda. *Catatan Frontend:* karena halaman Static SSR membuat sirkuit baru, state Tab Manager tidak terbawa; bila terasa mengganggu, Frontend mengajukan versi dialog 480px (interaktif) sebagai usulan. Aturan password: daftar ikon centang/silang + teks, `aria-live="polite"`.

### Daftar Pengguna (menggantikan 2.3)
- PageHeader: "Pengguna" + jumlah, tombol **primary** `⊕ Tambah Pengguna` (ikon kiri).
- `DataList`: **pencarian** (username/nama), dropdown Role, **filter chip dengan jumlah**: Semua · Aktif · Terkunci · Nonaktif. Footer "Menampilkan X dari Y".
- Kolom Status = `StatusBadge`: **Aktif** hijau, **Terkunci** merah, **Nonaktif** abu. Menu `⋯`: Lihat/Ubah, Reset Password, Buka Kunci (hanya bila terkunci), Akhiri Sesi (hanya bila sedang login), Nonaktifkan/Aktifkan.

### Detail/Ubah Pengguna
Badge status di PageHeader. Kartu "Status Akun": tombol `Reset Password`, `Akhiri Sesi` (default), **`Nonaktifkan` = bahaya** (hanya aksi merusak). Footer form: [Batal] [Simpan] (primary, tanpa ikon). Pesan penahan Administrator terakhir tampil sebagai `MessageBar` peringatan.

### Editor Role + Matriks Izin
Grid input: tab belum disimpan saat sel berubah. Baris grup **Administrasi** terkunci (ikon gembok + teks "Hanya Administrator, tidak dapat diberikan"), sel "–" tidak fokus. Role sistem: `MessageBar` info "Role sistem tidak dapat diubah" (dapat ditutup), halaman hanya baca. Ringkasan sebelum Simpan = Dialog 480px ("Tambah 3, lepas 1 permission. Berlaku untuk 2 pengguna pada aksi berikutnya"). Hapus Role: tombol bahaya nonaktif dengan tooltip bila dipakai pengguna.

### Audit Log
Rentang tanggal wajib (default 7 hari). **Tambah pencarian** (pelaku, objek) dan chip kategori peristiwa dengan jumlah (Login · Pengguna · Role · Lainnya). Baris dapat diperluas (nilai sebelum/sesudah). Footer "Menampilkan X dari Y". Tidak ada tombol ubah/hapus.

### Dialog dan sesi
Dialog Reset Password, Akhiri Sesi, Nonaktifkan: 480px. Peringatan sesi habis (2 menit sebelum timeout 30 menit): Dialog `role="alertdialog"` dengan hitung mundur dan [Tetap masuk] primary; berlaku di seluruh shell dan semua tab.


---

## Komponen dan Token (Design.md)

Acuan: `docs/Design.md` v2.0 dan `docs/Design/_Foundation-UI.md` v3.0. Nama komponen mengikuti kontrak Design.md bagian 13 (pemetaan dari nama lama: Fondasi bagian 8). Pustaka UI ditentukan oleh keputusan **K-1** (Fondasi bagian 12); spesifikasi ini tidak bergantung pada pustaka.

- **Komponen umum:** `AppHeader` (pencarian, bahasa, pengguna), `TopNav`, `TabManager`, `Workspace`, `PageHeader`, `MessageBar`, `StatusBadge`, `DataList` (cari, filter chip, footer "Menampilkan X dari Y"), `ToastHost`, `Dialog` (480px).
- **Khusus modul:** `TextField`/`LookupSelect` (FormField) untuk form pengguna dan role; `Dialog` untuk Reset Password, Nonaktifkan/Aktifkan, Buka Kunci, Hapus Role, ringkasan perubahan matriks; matriks izin berupa grid `role="grid"` dengan checkbox; daftar aturan password (ikon centang/silang); halaman Form masuk (`LoginLayout`, latar SVG).
- **Token yang dipakai:** `--brand` (primary, fokus), `--s1` (input), `--fg3` (garis bawah input), `--sh16` (kartu login), kelas `t-ok` (Aktif), `t-err` (Terkunci), `t-obs` (Nonaktif), `t-warn` (peringatan sesi). Tidak ada hex/px mentah.
- **Deviasi:** tidak ada. Catatan: Ganti Password sukarela sebagai halaman penuh (Static SSR) tidak mempertahankan state tab; diajukan sebagai catatan Frontend, bukan deviasi desain.
