# FSD — Access Management

| Atribut | Nilai |
|---|---|
| Modul | Access Management |
| Versi | 1.0 (Disetujui user) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Tidak ada (modul fondasi; semua modul lain bergantung pada modul ini) |

---

## 1. Business Objective

Perusahaan terdiri dari dua manajemen, **Comfi** (master data, PO, Work Order) dan **Konveksi** (produksi, pekerja, penggajian), dengan tanggung jawab dan keuangan terpisah. Satu sistem dipakai bersama oleh kurang lebih **5–6 user**. Karena itu:

- Setiap user hanya boleh melihat dan mengubah data sesuai tugasnya (contoh: tarif hanya diubah Comfi, master pekerja hanya diubah Konveksi).
- Hak akses harus dapat diatur per modul dan per aksi oleh Administrator, tanpa perlu mengubah kode.
- Tindakan sensitif (login, perubahan user, perubahan hak akses) harus tercatat dan dapat ditelusuri.

### Di Luar Scope (Fase 1)
- Single Sign-On / login via Google / Microsoft.
- Reset password mandiri via email (sistem internal; reset dilakukan Administrator).
- Multi-factor authentication.
- Pemisahan data per perusahaan (Comfi/Konveksi) pada level baris data. Pemisahan hanya melalui hak akses per modul/resource.

---

## 2. Konsep Hak Akses

Hak akses dibentuk dari **Permission = Resource + Action**.

- **Resource**: objek bisnis yang dilindungi, misalnya `MasterData.Pekerja`, `MasterData.Tarif`, `WorkOrder`, `ProductionOutput`, `Payroll`.
- **Action**: tindakan standar `View`, `Create`, `Edit`, `Void`, ditambah tindakan khusus per resource (misalnya `WorkOrder.Release`, `WorkOrder.Close`, `Payroll.ClosePeriod`).
- **Katalog permission** didefinisikan oleh sistem (seed). Administrator tidak membuat permission baru, hanya memberikannya ke Role. Setiap FSD modul berikutnya **wajib mendaftarkan daftar permission-nya** pada katalog ini.
- **Role** adalah kumpulan permission yang diberi nama dan dapat dikonfigurasi (contoh: "Admin Produksi", "PPIC Comfi", "Admin Gaji").
- Satu **User** memiliki tepat **satu Role**.
- **Administrator** adalah role bawaan sistem dengan akses penuh ke semua resource dan action, tidak dapat diubah atau dihapus. Hanya Administrator yang dapat mengelola modul ini.

### Katalog Permission Awal (indikatif, difinalkan di FSD masing-masing modul)

| Resource | Action |
|---|---|
| AccessManagement.User | View, Create, Edit, Deactivate, ResetPassword |
| AccessManagement.Role | View, Create, Edit, Delete |
| AccessManagement.AuditLog | View |
| MasterData.Material, FG, BOM, Size, Color, Process, Vendor | View, Create, Edit, Deactivate |
| MasterData.Routing, MasterData.Tarif | View, Create, Edit |
| MasterData.Pekerja | View, Create, Edit, Deactivate |
| WorkOrder | View, Create, Edit, Release, Complete, Close |
| ProductionOutput | View, Create, Void, Correct |
| VendorJob | View, Create, Void |
| Payroll | View, CreatePeriod, ClosePeriod, Export |

> Modul `AccessManagement` hanya dapat diakses Administrator. Permission-nya tidak dapat diberikan ke Role lain.

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-AM-01 | User | login dengan username dan password | dapat memakai sistem sesuai hak akses saya |
| US-AM-02 | User | logout, dan otomatis logout bila tidak aktif | akun tidak disalahgunakan di PC bersama |
| US-AM-03 | User | mengganti password saya sendiri | password tetap rahasia |
| US-AM-04 | Administrator | membuat, mengubah, dan menonaktifkan user | hanya orang berwenang yang bisa masuk |
| US-AM-05 | Administrator | mereset password user | user yang lupa password bisa masuk lagi |
| US-AM-06 | Administrator | membuat dan mengubah Role beserta matriks permission-nya | hak akses sesuai tugas masing-masing |
| US-AM-07 | Administrator | menetapkan Role untuk setiap user | user memperoleh hak akses yang tepat |
| US-AM-08 | Administrator | melihat audit log akses dan perubahan | dapat menelusuri siapa melakukan apa |
| US-AM-09 | User | hanya melihat menu dan tombol yang boleh saya pakai | tampilan tidak membingungkan |
| US-AM-10 | Pemilik sistem | akun Administrator awal tersedia saat pertama kali deploy | sistem bisa dikonfigurasi dari kosong |

---

## 4. Acceptance Criteria

### 4.1 Autentikasi
- **AC-AM-01** Given user aktif dengan kredensial benar, When login, Then user masuk dan diarahkan ke halaman utama, dan menu hanya menampilkan modul yang punya permission `View`.
- **AC-AM-02** Given username atau password salah, When login, Then tampil pesan generik "Username atau password salah" (tidak menyebutkan mana yang salah) dan percobaan gagal dicatat.
- **AC-AM-03** Given 5 kali gagal login berturut-turut pada satu username, Then akun terkunci selama 15 menit (nilai dapat dikonfigurasi) dan Administrator dapat membuka kunci lebih awal.
- **AC-AM-04** Given user berstatus nonaktif, When login, Then ditolak dengan pesan generik yang sama seperti AC-AM-02.
- **AC-AM-05** Given user tidak beraktivitas selama 30 menit (dapat dikonfigurasi), Then sesi berakhir dan user diarahkan ke halaman login.
- **AC-AM-06** Given user logout, Then sesi berakhir dan halaman terproteksi tidak dapat diakses dengan tombol Back.
- **AC-AM-06a** Satu user hanya boleh memiliki **satu sesi aktif**. Given user login dari PC lain saat sesi sebelumnya masih aktif, Then sesi lama diakhiri dan sesi baru berlaku. Sesi lama diarahkan ke login dengan pesan "Akun digunakan di perangkat lain". Kejadian ini tercatat di audit log.
- **AC-AM-06b** Administrator dapat mengakhiri paksa sesi aktif user tertentu (misalnya PC ditinggalkan dalam keadaan login).

### 4.2 Password
- **AC-AM-07** Password minimal 8 karakter dan mengandung huruf dan angka. Password disimpan hanya dalam bentuk hash.
- **AC-AM-08** Given user baru atau password baru direset Administrator, When login pertama, Then user **wajib** mengganti password sebelum mengakses halaman lain.
- **AC-AM-09** Given user mengganti password sendiri, Then password lama wajib diisi dengan benar dan password baru tidak boleh sama dengan password lama.

### 4.3 Manajemen User (Administrator)
- **AC-AM-10** Administrator dapat membuat user dengan: username (unik, tidak peka huruf besar-kecil), nama lengkap, Role, dan password awal.
- **AC-AM-11** User **tidak dihapus**, hanya dinonaktifkan atau diaktifkan kembali. Riwayat data dan audit log yang merujuk user tetap utuh.
- **AC-AM-12** Username tidak dapat diubah setelah dibuat.
- **AC-AM-13** Sistem **menolak** menonaktifkan atau menurunkan Role dari Administrator terakhir yang aktif.
- **AC-AM-14** Administrator tidak dapat menonaktifkan akunnya sendiri.

### 4.4 Manajemen Role dan Permission
- **AC-AM-15** Administrator dapat membuat Role baru dengan nama unik, lalu mencentang permission dalam matriks Resource × Action.
- **AC-AM-16** Role "Administrator" bawaan tidak dapat diubah atau dihapus.
- **AC-AM-17** Role yang masih dipakai oleh minimal satu user **tidak dapat dihapus**. Sistem menampilkan jumlah user yang memakainya.
- **AC-AM-18** Permission turunan otomatis konsisten. Mencentang `Create`, `Edit`, atau `Void` pada suatu resource otomatis menyertakan `View` pada resource yang sama.
- **AC-AM-19** Perubahan permission Role berlaku untuk user terkait **paling lambat pada aksi atau navigasi berikutnya** tanpa harus login ulang.

### 4.5 Penegakan Akses
- **AC-AM-20** Given user tanpa permission `View` pada suatu halaman, When mengakses lewat URL langsung, Then tampil halaman "Akses ditolak" (403) dan percobaan dicatat.
- **AC-AM-21** Given user tanpa permission aksi tertentu, Then tombol aksi disembunyikan di UI **dan** aksi tersebut ditolak oleh server/API bila dipanggil langsung. Penegakan di server bersifat final, penyembunyian di UI hanya kemudahan.
- **AC-AM-22** Given user belum login, When mengakses halaman terproteksi, Then diarahkan ke login dan kembali ke halaman tujuan setelah berhasil login.

### 4.6 Audit Log
- **AC-AM-23** Peristiwa berikut tercatat: login berhasil, login gagal, logout, akun terkunci, ganti dan reset password, pembuatan, perubahan, dan penonaktifan user, perubahan Role dan permission, akses ditolak (403).
- **AC-AM-24** Setiap catatan berisi: waktu (UTC dan ditampilkan lokal), pelaku, jenis peristiwa, objek terkait, nilai sebelum dan sesudah (untuk perubahan), dan alamat IP.
- **AC-AM-25** Audit log **tidak dapat diubah atau dihapus** lewat aplikasi. Administrator dapat memfilter berdasarkan rentang tanggal, user, dan jenis peristiwa.
- **AC-AM-26** Password atau hash password tidak pernah dicatat di audit log.

### 4.7 Inisialisasi
- **AC-AM-27** Saat deploy pertama pada database kosong, sistem membuat Role Administrator, katalog permission, dan satu akun Administrator awal. Password awalnya diambil dari konfigurasi/secret deployment (tidak di-hardcode di repository) dan wajib diganti saat login pertama.
- **AC-AM-28** Seeder bersifat idempoten. Menjalankannya ulang tidak menduplikasi data dan tidak menimpa perubahan Role atau user yang sudah ada. Permission baru dari modul baru ditambahkan ke katalog tanpa mengubah Role selain Administrator.

---

## 5. Data Dictionary (Konsep Data)

### User
| Atribut | Keterangan |
|---|---|
| UserId | Identitas unik |
| Username | Unik, tidak peka huruf besar-kecil, tidak bisa diubah |
| FullName | Nama lengkap |
| PasswordHash | Hash password (tidak pernah ditampilkan) |
| RoleId | Role tunggal yang dimiliki |
| IsActive | Aktif atau nonaktif |
| MustChangePassword | Wajib ganti password saat login berikutnya |
| FailedLoginCount, LockoutUntil | Kontrol penguncian akun |
| LastLoginAt | Waktu login terakhir |
| CreatedAt/By, UpdatedAt/By | Jejak pembuatan dan perubahan |

### Role
| Atribut | Keterangan |
|---|---|
| RoleId | Identitas unik |
| Name | Unik |
| Description | Keterangan |
| IsSystem | Ya untuk Administrator (tidak dapat diubah atau dihapus) |

### Permission (katalog, hasil seed)
| Atribut | Keterangan |
|---|---|
| PermissionId / Key | Contoh: `WorkOrder.Release` |
| Resource, Action | Komponen permission |
| Module | Pengelompokan untuk tampilan matriks |
| Description | Penjelasan singkat |

### RolePermission
Relasi Role ↔ Permission (many-to-many).

### AuditLog
| Atribut | Keterangan |
|---|---|
| AuditId, Timestamp | Identitas dan waktu |
| ActorUserId / ActorUsername | Pelaku (username disimpan juga agar tetap terbaca bila user berubah) |
| EventType | Login, LoginFailed, PasswordChanged, RoleChanged, AccessDenied, dan lainnya |
| TargetType, TargetId | Objek yang terkena |
| OldValue, NewValue | Snapshot perubahan (tanpa data rahasia) |
| IpAddress | Alamat IP klien |

---

## 6. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Login | **Static SSR** | Pembuatan cookie autentikasi harus terjadi pada respons HTTP, bukan di dalam sirkuit interaktif. Halaman sederhana. |
| Logout | **Static SSR** (form POST) | Alasan sama dengan Login. |
| Ganti Password (wajib dan sukarela) | **Static SSR** | Form sederhana, tidak butuh interaktivitas. |
| Akses Ditolak (403) | **Static SSR** | Halaman statis. |
| Daftar dan Form User | **Interactive Server** | Pencarian, filter, dialog, dan validasi langsung. Pengguna sedikit dan di jaringan internal. |
| Daftar dan Form Role + Matriks Permission | **Interactive Server** | Matriks centang interaktif (aturan View otomatis, pencarian resource). |
| Audit Log Viewer | **Interactive Server** | Filter, paging, dan pencarian dinamis. |

Global: **Interactive Server** menjadi default aplikasi untuk fase 1. WebAssembly **tidak dipakai** karena pengguna sedikit di jaringan internal, tidak ada kebutuhan offline, dan Server lebih sederhana untuk dikelola.

---

## 7. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Dua Administrator mengedit Role yang sama bersamaan | Penyimpanan kedua ditolak dengan pesan "Data telah diubah oleh pengguna lain, muat ulang" (optimistic concurrency). |
| E2 | Role user diubah saat user sedang aktif | Berlaku pada aksi atau navigasi berikutnya (AC-AM-19). Tidak memutus sesi. |
| E3 | User dinonaktifkan saat sedang login | Sesi tidak lagi valid pada permintaan berikutnya, user diarahkan ke login. |
| E4 | Koneksi Blazor Server terputus (sirkuit hilang) | Tampil pesan "Menyambung ulang". Bila gagal, muat ulang halaman. Sesi login tetap valid selama belum timeout. |
| E5 | Username sudah dipakai (termasuk oleh user nonaktif) | Ditolak, pesan "Username sudah digunakan". |
| E6 | Administrator lupa password dan tidak ada Administrator lain | Prosedur pemulihan dilakukan di luar aplikasi (konfigurasi/skrip deployment oleh DevOps) dan tercatat di runbook. |
| E7 | Nama Role kosong, duplikat, atau Role tanpa permission | Role tanpa permission diizinkan (user tidak dapat melihat modul apa pun). Nama kosong atau duplikat ditolak. |
| E8 | Permission di katalog dihapus pada rilis baru | Mapping di RolePermission yang tidak lagi valid dibersihkan oleh migrasi, dan dicatat di audit log. |
| E9 | Percobaan login brute-force dari banyak username | Penguncian per akun (AC-AM-03). Semua percobaan gagal tercatat dengan IP. |
| E10 | Endpoint API dipanggil langsung tanpa token/cookie valid | 401. Dengan sesi valid tetapi tanpa permission, 403. Respons tidak membocorkan detail internal. |
| E11 | Audit log tumbuh besar | Filter wajib memakai rentang tanggal. Kebijakan retensi/arsip diputuskan di fase Architect/DevOps. |

---

## 8. Non-Functional Requirements (Ringkas)

- **Keamanan:** password di-hash dengan algoritma standar yang kuat, cookie HttpOnly + Secure, proteksi antiforgery pada form, seluruh pemeriksaan permission dilakukan di sisi server.
- **Kinerja:** login dan pemeriksaan permission tidak menambah latensi yang terasa (permission user di-cache per sesi, valid tanpa menyalahi AC-AM-19).
- **Bahasa UI:** Bahasa Indonesia.
- **Audit:** semua tindakan di bagian 4.6 tercatat secara sinkron dengan transaksinya.

---

## 9. Catatan untuk Peran Berikutnya

- **Architect:** konfirmasi penggunaan ASP.NET Core Identity. Kemungkinan butuh package `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, sehingga **wajib minta izin user** sesuai aturan dependency proyek dan didaftarkan di `src/Directory.Packages.props`. Tentukan juga kebijakan retensi audit log.
- **Semua FSD modul berikutnya:** wajib memuat tabel permission (Resource + Action) yang akan didaftarkan ke katalog.
- **QA:** skenario khusus untuk AC-AM-13 (Administrator terakhir), AC-AM-19 (perubahan permission real-time), AC-AM-21 (penegakan di server).

---

## 10. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Satu user = satu Role. | **Disetujui** |
| Q2 | Kunci akun 5× gagal / 15 menit (dapat dibuka Administrator), timeout sesi 30 menit. | **Disetujui** |
| Q3 | Password min. 8 karakter, huruf + angka. | **Disetujui** |
| Q4 | Reset password oleh Administrator, tanpa email. | **Disetujui** |
| Q5 | Satu sesi aktif per user (login baru menggantikan sesi lama, lihat AC-AM-06a). | **Disetujui** (mekanisme "menggantikan" adalah usulan SA, bukan "menolak login baru") |
| Q7 | Penggunaan ASP.NET Core Identity. | **Izin diberikan user**; pemasangan dilakukan pada fase Architect saat solution dibuat |
| Q6 | Katalog permission di bagian 2 bersifat indikatif dan difinalkan per modul. | Informasi |
