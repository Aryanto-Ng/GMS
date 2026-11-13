# FSD — Notifikasi

| Atribut | Nilai |
|---|---|
| Modul | Notifikasi |
| Versi | **0.9 (DRAFT — menunggu persetujuan user)** |
| Tipe | Modul Baru (diminta user saat tahap UI/UX; berlaku di fase 1) |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management (permission, pengguna); membaca peristiwa dari Customer PO, Work Order, Production Output, Serah Terima & Stok FG, Pengiriman, Payroll Borongan |
| UI | Lonceng + panel di Global Header (`docs/Design/_Foundation-UI.md` bagian 3.1), halaman "Semua notifikasi", layar Pengaturan Notifikasi |

Keputusan user (sesi brainstorming 2026-11-13): penerima **per izin**; pengiriman **polling 60 detik**; dua jenis pemicu (**kejadian** dan **kondisi terjadwal**); status baca **per pengguna**, simpan **90 hari**; kondisi terjadwal **sekali saat kondisi mulai**; pekerja latar di API **06:00 WIB + saat startup**; **layar pengaturan untuk Administrator**; "stok habis" = **transisi Tersedia dari > 0 ke 0**.

---

## 1. Business Objective

Memberi tahu pengguna tentang hal yang **perlu ditindaklanjuti atau berubah di luar pandangan mereka**, tanpa harus membuka tiap modul: WO baru siap dikerjakan, WO selesai menunggu Close, data yang sudah dibalik (Void/Koreksi), PO terlambat, atau periode gaji yang harus ditutup.

### Prinsip
- **Melengkapi Beranda, bukan menggantikannya.** Beranda = keadaan sekarang (angka). Notifikasi = peristiwa yang terjadi (jejak, bisa ditandai terbaca).
- **Hanya yang berhak.** Notifikasi hanya dibuat untuk dan hanya ditampilkan kepada pengguna yang memiliki izin `View` pada modul sumbernya. Tidak ada data yang bocor lewat notifikasi.
- **Tidak berisik.** Kondisi tidak diulang; pelaku tidak diberi tahu tentang aksinya sendiri; satu peristiwa = satu notifikasi per penerima.
- **Tidak mengubah aturan bisnis.** Notifikasi hanya membaca; kegagalannya tidak boleh membuat transaksi bisnis salah.
- **Dapat ditindaklanjuti.** Setiap notifikasi membuka dokumen terkait (di tab) saat diklik.

### Di Luar Scope (fase 1)
Email, push, WhatsApp; langganan per pengguna; tunda (snooze); komentar/percakapan; notifikasi antar-pengguna manual; pengaturan per role; notifikasi real-time (SignalR).

---

## 2. Katalog Peristiwa

Penerima = pengguna **aktif** yang memiliki **semua** izin yang tercantum, **kecuali pelaku** (untuk jenis Kejadian). Judul dan isi disimpan sebagai **kunci resource + parameter** (bukan teks jadi) agar tampil dalam bahasa pengguna (ID/EN).

### 2.1 Kejadian (dibuat seketika oleh aksi pengguna)

| ID | Peristiwa | Pemicu | Penerima (izin) | Menuju |
|---|---|---|---|---|
| NE-01 | **WO dirilis** | WO Draft → Released | `ProductionOutput.Create` | Detail WO |
| NE-02 | **WO selesai, menunggu Close** | WO → Completed | `WorkOrder.Close` | Detail WO |
| NE-03 | **Serah Terima FG dibuat** | Serah terima FG tersimpan (menyebut qty, PO teralokasi, stok bebas) | `StokFG.View` | Detail Serah Terima |
| NE-04 | **Periode gaji ditutup** | Periode Open → Closed | `Payroll.View` | Detail Periode |
| NE-05 | **Void/Koreksi Output** | Entri output/vendor di-void atau dikoreksi | `ProductionOutput.View` | Riwayat Produksi (entri terkait) |
| NE-06 | **Void/Koreksi Serah Terima FG** | Serah terima di-void/dikoreksi | `SerahTerimaFG.View` | Detail Serah Terima |
| NE-07 | **Void/Koreksi Pengiriman** | Surat jalan di-void/dikoreksi | `Pengiriman.View` | Detail Surat Jalan |
| NE-08 | **PO dibatalkan** | PO → Cancelled (menyebut jumlah alokasi dilepas) | `CustomerPO.View` | Detail PO |
| NE-09 | **Akun terkunci** | Akun terkunci karena percobaan gagal | `AccessManagement.AuditLog.View` | Daftar Pengguna (filter terkunci) |
| NE-10 | **Sesi diakhiri paksa** | Administrator mengakhiri sesi pengguna | `AccessManagement.AuditLog.View` | Daftar Pengguna |
| NE-11 | **Stok tersedia habis** | Stok Tersedia satu FG × Color × Size turun dari > 0 ke 0 (pengiriman, alokasi, penyesuaian, void). Satu notifikasi per kombinasi per transisi | `StokFG.View` | Kartu Stok kombinasi itu |
| NE-12 | **Alokasi dilepas otomatis** | Void serah terima / pembatalan PO melepas alokasi (menyebut PO dan qty) | `CustomerPO.View` | Detail PO |

### 2.2 Kondisi terjadwal (dibuat pekerja latar)

Pekerja memeriksa **sekali sehari pukul 06:00 WIB** (nilai bawaan, dapat diubah di Pengaturan) dan **saat aplikasi start** (menyusul pemeriksaan yang terlewat bila instance sempat tertidur). Pemeriksaan idempoten: tidak membuat notifikasi ganda.

| ID | Kondisi | Definisi | Penerima (izin) | Menuju |
|---|---|---|---|---|
| NC-01 | **PO terlambat** | PO Confirmed/In Progress memiliki baris yang Tanggal Kirimnya lewat dan belum terkirim penuh (selaras W-01 Beranda) | `CustomerPO.View` | Detail PO |
| NC-02 | **PO jatuh tempo** | Tanggal Kirim baris dalam **H-n hari** (n bawaan 3) dan belum terkirim penuh | `CustomerPO.View` | Detail PO |
| NC-03 | **WO terlambat** | WO belum Completed/Closed/Cancelled dan melewati Target Selesai (selaras W-06) | `WorkOrder.View` | Detail WO |
| NC-04 | **Periode gaji perlu ditutup** | Periode Open dengan Tanggal Selesai sudah lewat | `Payroll.View` | Detail Periode |

**Aturan "sekali saat kondisi mulai":** satu notifikasi per (jenis, objek, *episode*). *Episode* berakhir ketika kondisi hilang (mis. PO terkirim penuh, tanggal kirim direvisi keluar dari jendela, WO selesai); bila kondisi muncul lagi, episode baru dan notifikasi baru dibuat. PO dengan beberapa baris terlambat = **satu** notifikasi per PO (menyebut jumlah baris).

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-NT-01 | Admin Produksi (Konveksi) | diberi tahu saat WO dirilis | langsung tahu ada pekerjaan baru tanpa menunggu kabar lisan |
| US-NT-02 | Staf Comfi | diberi tahu saat WO selesai dan saat Serah Terima FG dibuat | dapat melakukan Close dan mengatur alokasi/pengiriman tepat waktu |
| US-NT-03 | Pengguna modul mana pun | diberi tahu saat data yang saya pakai di-void/dikoreksi | tidak bekerja dengan angka yang sudah berubah |
| US-NT-04 | Staf Comfi | diberi tahu saat PO terlambat atau mendekati jatuh tempo | dapat memprioritaskan tindak lanjut |
| US-NT-05 | Admin Gaji | diberi tahu saat periode perlu ditutup | tidak terlambat menutup periode |
| US-NT-06 | Administrator | diberi tahu saat akun terkunci atau sesi diakhiri paksa | dapat membantu pengguna dan memantau keamanan |
| US-NT-07 | Semua pengguna | menandai notifikasi terbaca, menandai semua terbaca, dan melihat riwayatnya | memantau yang sudah dan belum ditangani |
| US-NT-08 | Administrator | mengatur jenis notifikasi aktif, ambang jatuh tempo, dan jam pemeriksaan | menyesuaikan dengan kebiasaan kerja tanpa ubah kode |

---

## 4. Acceptance Criteria

### 4.1 Pembuatan
- **AC-NT-01** Setiap peristiwa pada bagian 2 membuat tepat **satu** notifikasi per penerima yang memenuhi izin; pelaku (Kejadian) **tidak** menerimanya.
- **AC-NT-02** Penerima ditentukan **saat notifikasi dibuat** berdasarkan izin saat itu. Pengguna yang menerima izin kemudian tidak menerima notifikasi lama.
- **AC-NT-03** Notifikasi Kejadian dibuat **dalam transaksi yang sama** dengan aksi bisnis (atomik): bila aksi dibatalkan, notifikasi tidak ada. Pembuatan notifikasi tidak boleh membuat aksi bisnis gagal karena alasan non-bisnis (mis. penerima kosong = tidak ada notifikasi, bukan galat).
- **AC-NT-04** Kondisi terjadwal mengikuti aturan episode (bagian 2.2): tidak ada notifikasi ganda untuk objek dan episode yang sama, **meski pekerja berjalan berulang, bersamaan di dua instance, atau terlewat lalu menyusul saat startup**.
- **AC-NT-05** Pembatalan/penyelesaian objek mengakhiri episode kondisi-nya (tidak ada notifikasi kondisi baru untuk objek yang sudah selesai).
- **AC-NT-06** Jenis yang dinonaktifkan di Pengaturan tidak dibuat lagi; notifikasi yang sudah ada tetap terlihat.

### 4.2 Penyajian
- **AC-NT-07** Lonceng menampilkan jumlah belum dibaca milik pengguna (maks `9+`, hilang bila 0). Jumlah diperbarui lewat **polling tiap 60 detik** saat tab browser terlihat; saat tab tersembunyi polling dijeda dan segera dilakukan saat kembali terlihat.
- **AC-NT-08** Panel menampilkan **20 notifikasi terbaru** (belum dibaca di atas penanda, lalu sisanya berurutan waktu), tiap item: judul, satu baris keterangan, waktu relatif, penanda belum dibaca.
- **AC-NT-09** Klik item menandainya terbaca lalu membuka dokumen tujuan **di tab** (fokus ke tab bila sudah terbuka). Bila dokumen tidak lagi ada atau pengguna tidak lagi berhak: tampil pesan "Dokumen tidak tersedia" tanpa membocorkan detail, notifikasi tetap terbaca.
- **AC-NT-10** "Tandai semua dibaca" menandai seluruh notifikasi pengguna itu (bukan hanya 20 yang tampak).
- **AC-NT-11** Halaman **Semua notifikasi** (`notif:all`): pencarian, filter chip (Semua · Belum dibaca · per kategori: Produksi, Penjualan, Gudang, Penggajian, Keamanan) dengan jumlah, paging 25, urut terbaru. Tab tidak membawa titik "belum disimpan".
- **AC-NT-12** Teks tampil dalam **bahasa aktif pengguna** (dari kunci + parameter), format tanggal/angka mengikuti Design.md.
- **AC-NT-13** Server **memeriksa ulang izin** saat menyajikan: notifikasi yang penerimanya sudah kehilangan izin sumbernya tidak ditampilkan dan tidak dihitung pada lencana.
- **AC-NT-14** Pengguna hanya dapat membaca dan mengubah status baca notifikasi **miliknya sendiri**.

### 4.3 Retensi
- **AC-NT-15** Notifikasi lebih tua dari **90 hari** dihapus otomatis oleh pekerja harian (nilai bawaan; tidak ada pengecualian untuk yang belum dibaca).
- **AC-NT-16** Penghapusan retensi tidak memengaruhi aturan episode kondisi (penanda episode disimpan terpisah dari baris notifikasi sampai kondisi berakhir).

### 4.4 Pengaturan (Administrator)
- **AC-NT-17** Layar **Pengaturan Notifikasi** (Administrasi → Hak Akses… atau grup baru "Notifikasi") berisi: aktif/nonaktif **per jenis** (NE-01…NE-12, NC-01…NC-04), **ambang jatuh tempo** (hari, 1–14, bawaan 3), **jam pemeriksaan harian** (bawaan 06:00, zona waktu aplikasi), **masa simpan** (hari, 30–365, bawaan 90).
- **AC-NT-18** Setiap perubahan pengaturan dicatat di audit log (nilai sebelum/sesudah) dan memakai kontrol konkurensi (`rowVersion`).
- **AC-NT-19** Mengubah ambang jatuh tempo berlaku untuk pemeriksaan berikutnya; tidak menghapus notifikasi lama.
- **AC-NT-20** Izin baru: `Notifikasi.Pengaturan.View` dan `Notifikasi.Pengaturan.Edit` (bawaan: Administrator). Lonceng dan halaman "Semua notifikasi" **tidak** memerlukan izin (setiap pengguna login memilikinya).

### 4.5 Pekerja latar
- **AC-NT-21** Hanya satu instance memproses pemeriksaan pada satu waktu (penguncian di database); instance lain melewati tanpa galat.
- **AC-NT-22** Hasil pemeriksaan dicatat (jumlah dibuat per jenis, durasi) ke log aplikasi. Kegagalan satu jenis tidak menghentikan jenis lain dan dicoba ulang pada pemeriksaan berikutnya.
- **AC-NT-23** Pemeriksaan saat startup hanya berjalan bila pemeriksaan terakhir lebih lama dari satu siklus (24 jam), agar restart berulang tidak memicu beban.

---

## 5. Data Dictionary (konsep)

| Entitas | Atribut utama | Catatan |
|---|---|---|
| **Notification** | Id, RecipientUserId, TypeCode (NE-xx/NC-xx), TitleKey, BodyKey, Params (JSON: kode dokumen, qty, tanggal…), TargetRoute (jenis + id dokumen), CreatedAt, ReadAt (null = belum dibaca), SourceEventKey | Satu baris per penerima. **Tanpa data rahasia** (tarif/rupiah tidak boleh dimasukkan ke Params). Indeks (RecipientUserId, ReadAt, CreatedAt) |
| **NotificationEpisode** | TypeCode, ObjectKey (mis. `PO:{id}`), StartedAt, EndedAt (null = aktif) | Untuk kondisi terjadwal; unik pada (TypeCode, ObjectKey) yang EndedAt null |
| **NotificationSetting** | TypeCode, Enabled, atau parameter global (DueSoonDays, ScheduleTime, RetentionDays), RowVersion | Satu baris per jenis + satu baris global |
| **NotificationJobRun** | RunAt, Type (scheduled/startup), CreatedCount per jenis, DurationMs, Error | Untuk pemantauan dan AC-NT-23 |

Pembaca tidak pernah meminta daftar penerima; server menentukannya (izin + non-pelaku).

---

## 6. Render Mode (Frontend)

| Layar | Mode |
|---|---|
| Lonceng + panel di header | **Interactive Server** (komponen shell; polling lewat timer sisi server/klien yang dijeda saat tab tersembunyi) |
| Semua notifikasi (`notif:all`) | **Interactive Server** |
| Pengaturan Notifikasi (`admin:notif-settings`) | **Interactive Server** |

Tidak ada halaman Static SSR baru. WebAssembly tidak dipakai.

---

## 7. Edge Cases & Error Handling

| Situasi | Perilaku |
|---|---|
| Tidak ada penerima yang berhak | Tidak ada notifikasi dibuat; bukan galat |
| Hanya pelaku yang berhak | Tidak ada notifikasi (pelaku tidak diberi tahu) |
| Pengguna nonaktif/terkunci | Tidak menjadi penerima baru; notifikasi lama tetap sampai retensi |
| Banyak peristiwa sekaligus (mis. impor atau Void berantai) | Satu notifikasi per peristiwa per penerima; panel menampilkan 20 terbaru dan "Lihat semua". Pengelompokan otomatis tidak ada di fase 1 |
| NE-11 pada impor saldo awal/penyesuaian massal | Hanya transisi > 0 → 0 yang memicu; impor yang menambah stok tidak memicu |
| Dokumen tujuan sudah dihapus/tidak berhak | Pesan "Dokumen tidak tersedia"; notifikasi ditandai terbaca |
| Polling gagal (jaringan/401/5xx) | Lencana mempertahankan nilai terakhir; 401 → alur sesi berakhir yang sudah ada; coba lagi pada siklus berikutnya, tanpa MessageBar berisik. Panel yang dibuka saat galat: MessageBar error + Coba lagi |
| Instance tertidur melewati jam 06:00 | Pemeriksaan saat startup menyusul (AC-NT-23) |
| Dua instance memeriksa bersamaan | Satu menang (kunci), unik pada episode mencegah ganda |
| Tanggal kirim PO direvisi keluar dari kondisi | Episode berakhir; revisi kembali masuk kondisi = episode baru |
| Zona waktu | Semua perhitungan hari memakai zona waktu aplikasi (WIB) |
| Perubahan izin | Pada penyajian, notifikasi tanpa izin sumber disaring (AC-NT-13) |
| Pengaturan nonaktif untuk jenis yang sudah punya episode aktif | Episode tidak dibuat ulang; saat diaktifkan lagi, hanya kondisi yang masih berlaku yang membuat notifikasi baru |

---

## 8. Dampak ke Modul Lain (catatan untuk Architect/Backend)

- Setiap modul sumber memanggil satu abstraksi bersama (mis. `INotificationPublisher.Publish(typeCode, params, targetRoute, actorUserId)`) **di dalam transaksi aksinya**. Penentuan penerima ada di dalam modul Notifikasi (peta jenis → izin).
- Pekerja latar menggunakan query yang **sama** dengan definisi widget Beranda untuk W-01 dan W-06 agar angka konsisten.
- Penguncian pekerja dan unik episode di database (bukan di memori).
- Hindari N+1 saat fan-out: satu `INSERT ... SELECT` pengguna aktif berizin.
- Endpoint ringan khusus lencana (jumlah belum dibaca) agar polling murah.
- Konfigurasi bawaan (H-3, 06:00, 90 hari) tersedia di appsettings sebagai nilai awal seeder, lalu dikelola dari tabel pengaturan.

---

## 9. Hal yang Perlu Dikonfirmasi

| # | Pertanyaan | Usulan bawaan |
|---|---|---|
| Q1 | Penerima NE-03 (Serah Terima FG dibuat) = pemegang `StokFG.View`. Apakah cukup, atau hanya pemegang izin alokasi/`CustomerPO.Allocate`? | `StokFG.View` |
| Q2 | NE-05/06/07 (Void/Koreksi) memberi tahu **semua** pemegang izin View modul tersebut. Apakah terlalu luas, atau cukup yang pernah terlibat (mis. pembuat entri asal)? | Semua pemegang View |
| Q3 | Retensi 90 hari berlaku juga untuk notifikasi **belum dibaca**? | Ya |
| Q4 | Posisi menu Pengaturan Notifikasi | Administrasi → grup **Notifikasi** (terpisah dari Hak Akses, karena izinnya bukan `AccessManagement.*`) |
