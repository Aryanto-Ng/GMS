# FSD — Beranda (Dashboard)

| Atribut | Nilai |
|---|---|
| Modul | Beranda |
| Versi | 1.0 (Disetujui user) |
| Tipe | Modul Baru (ditambahkan atas permintaan user saat tahap UI/UX) |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management; membaca data dari Customer PO, Work Order, Production Output, Serah Terima & Stok FG, Pengiriman, Payroll Borongan |

---

## 1. Business Objective

Memberi setiap pengguna **ringkasan situasi kerja** begitu masuk, sesuai hak aksesnya, sehingga mereka tahu apa yang perlu ditindaklanjuti tanpa membuka tiap modul: PO yang terlambat, WO yang berjalan, output hari ini, stok bebas, dan periode gaji yang terbuka.

### Prinsip
- **Hanya membaca.** Beranda tidak mengubah data dan tidak memiliki aturan bisnis sendiri.
- **Mengikuti hak akses.** Widget tampil hanya bila pengguna memiliki permission `View` pada sumber datanya. Tidak ada permission baru.
- **Dapat ditindaklanjuti.** Setiap angka dapat diklik menuju daftar modul terkait dengan filter yang sudah terpasang.
- **Sederhana.** Angka ringkas dan daftar pendek (maksimal 5 baris). Tidak ada grafik tren di fase 1.

### Di Luar Scope
- Grafik tren dan analitik historis.
- Widget yang dapat dikustomisasi pengguna.
- Notifikasi email/push.
- Ekspor dari beranda.

---

## 2. Widget (Kartu) dan Sumber Datanya

| ID | Widget | Isi | Syarat tampil | Klik menuju |
|---|---|---|---|---|
| W-01 | **PO Terlambat** | Jumlah PO yang memiliki baris terlambat dan total pcs kekurangannya. Daftar 5 teratas (paling lama terlambat) | `CustomerPO.View` | Daftar PO, filter "Terlambat" |
| W-02 | **Jatuh Tempo 7 Hari** | Total pcs dengan Tanggal Kirim dalam 7 hari ke depan yang belum terkirim, dan jumlah PO | `CustomerPO.View` | Daftar PO, filter jatuh tempo |
| W-03 | **PO Belum Ada Nomor Customer** | Jumlah PO aktif yang Nomor PO Customer-nya masih kosong | `CustomerPO.View` | Daftar PO, filter "PO Customer belum ada" |
| W-04 | **Kebutuhan Belum Terencana** | Total `Sisa Rencana` pcs pada PO Confirmed/In Progress dan jumlah PO | `CustomerPO.View` | Daftar PO, filter terkait |
| W-05 | **WO Berjalan** | Jumlah WO per status (Released, In Progress, Completed menunggu Close). Daftar 5 WO dengan target selesai terdekat beserta progres | `WorkOrder.View` | Daftar WO |
| W-06 | **WO Terlambat** | Jumlah WO yang melewati Target Selesai | `WorkOrder.View` | Daftar WO, filter "Terlambat" |
| W-07 | **Output Hari Ini** | Total qty output aktif dengan Tanggal Kerja hari ini, dipecah per proses | `ProductionOutput.View` | Riwayat Output, filter hari ini |
| W-08 | **Sisa di Vendor** | Total pcs yang masih di vendor (terkirim − diterima) dan jumlah vendor/WO | `VendorJob.View` | Riwayat Vendor |
| W-09 | **Belum Diserahterimakan** | Total pcs output proses terakhir yang belum diserahterimakan, pada WO yang belum Closed/Cancelled. Daftar 5 WO terbesar | `SerahTerimaFG.View` | Daftar WO atau Serah Terima |
| W-10 | **Stok FG** | Stok Fisik, Teralokasi, dan Tersedia (stok bebas) dalam pcs. Daftar 5 FG dengan stok tersedia terbanyak | `StokFG.View` | Halaman Stok FG |
| W-11 | **Periode Gaji** | Periode Open: rentang tanggal, jumlah pekerja, total sementara (ditandai "Belum final"), sisa hari sampai tanggal selesai. Bila tidak ada periode Open: ajakan membuat periode (bila berhak) | `Payroll.View` | Detail periode |
| W-12 | **Keamanan Akun** | Jumlah akun terkunci dan jumlah login gagal 24 jam terakhir | `AccessManagement.AuditLog.View` | Audit Log / Daftar User |
| W-13 | **Aksi Cepat** | Tombol pintasan sesuai permission: Buat PO, Buat WO, Input Output, Kirim/Terima Vendor, Serah Terima FG, Buat Surat Jalan, Buka Periode Gaji | Masing-masing sesuai permission `Create` | Form terkait |

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-BR-01 | Staf Comfi | melihat PO terlambat dan jatuh tempo saat masuk | dapat memprioritaskan tindak lanjut |
| US-BR-02 | Staf Comfi | melihat kebutuhan yang belum terencana | tahu PO mana yang belum dibuatkan WO atau alokasi |
| US-BR-03 | Admin Produksi | melihat output hari ini dan WO berjalan | tahu progres dan apa yang belum diinput |
| US-BR-04 | Admin Produksi | melihat sisa di vendor dan output yang belum diserahterimakan | tidak ada barang yang terlewat |
| US-BR-05 | Admin Gaji | melihat periode gaji yang terbuka dan total sementara | mengetahui kapan harus menutup periode |
| US-BR-06 | Administrator | melihat akun terkunci dan percobaan login gagal | dapat bereaksi cepat terhadap masalah akses |
| US-BR-07 | Semua pengguna | beranda hanya menampilkan hal yang boleh saya lihat | tampilan tidak membingungkan |
| US-BR-08 | Semua pengguna | mengklik angka dan langsung menuju daftar yang relevan | tidak perlu mencari dan memfilter manual |

---

## 4. Acceptance Criteria

### 4.1 Umum
- **AC-BR-01** Beranda adalah halaman tujuan setelah login berhasil dan item pertama pada navigasi.
- **AC-BR-02** Widget hanya tampil bila pengguna memiliki permission sumbernya (bagian 2). Pengecekan dilakukan di **server**: data widget yang tidak berhak tidak pernah dikirim ke klien.
- **AC-BR-03** Bila pengguna tidak berhak atas satu pun widget data, Beranda menampilkan sapaan dan tautan ke modul yang boleh diaksesnya.
- **AC-BR-04** Setiap widget dimuat **independen**. Kegagalan satu widget hanya menampilkan state error di widget itu (dengan tombol Coba Lagi), tidak mengganggu widget lain.
- **AC-BR-05** Selama memuat, widget menampilkan skeleton. Bila tidak ada data, widget menampilkan state kosong yang informatif (misalnya "Tidak ada PO terlambat").
- **AC-BR-06** Beranda menampilkan waktu "Diperbarui pukul HH:mm" dan tombol **Segarkan**. Data dihitung saat halaman dibuka dan saat Segarkan ditekan. Penyegaran otomatis tidak ada di fase 1.
- **AC-BR-07** Setiap angka dan baris daftar dapat diklik menuju halaman modul dengan filter yang sesuai (kolom "Klik menuju"). Bila filter tujuan tidak tersedia pada modul, FSD modul tujuan wajib menambahkannya.
- **AC-BR-08** Tata letak widget dikelompokkan per area (Penjualan, Produksi, Gudang, Penggajian, Administrasi) dan urutannya mengikuti peran: area yang paling relevan dengan permission pengguna tampil lebih dahulu.

### 4.2 Definisi Perhitungan
- **AC-BR-09** *PO Terlambat*: PO tidak Cancelled yang memiliki baris dengan Tanggal Kirim < hari ini dan `Terkirim < Qty`. Kekurangan = `Qty − Terkirim`.
- **AC-BR-10** *Jatuh Tempo 7 Hari*: baris PO dengan Tanggal Kirim dari hari ini sampai 7 hari ke depan dan `Terkirim < Qty`, pada PO yang tidak Cancelled.
- **AC-BR-11** *Kebutuhan Belum Terencana*: total `Sisa Rencana` baris PO pada PO Confirmed dan In Progress.
- **AC-BR-12** *WO Terlambat*: sesuai FSD Work Order AC-WO-26.
- **AC-BR-13** *Output Hari Ini*: total qty entri Output dan Penyesuaian aktif dengan Tanggal Kerja = hari ini. Entri Voided tidak dihitung.
- **AC-BR-14** *Sisa di Vendor*: `Terkirim − Diterima` (entri aktif) pada WO yang belum Closed/Cancelled.
- **AC-BR-15** *Belum Diserahterimakan*: `Output kumulatif proses terakhir − total diserahterimakan` pada WO yang belum Closed/Cancelled.
- **AC-BR-16** *Stok FG*: Stok Fisik, Teralokasi, dan Tersedia sesuai FSD Serah Terima & Stok FG bagian 2.1.
- **AC-BR-17** *Periode Gaji*: periode berstatus Open. Total sementara dihitung langsung (sesuai FSD Payroll) dan ditandai "Belum final".
- **AC-BR-18** "Hari ini" dan "7 hari" memakai zona waktu aplikasi (default WIB, UTC+7) yang dapat dikonfigurasi.

### 4.3 Aksi Cepat
- **AC-BR-19** Aksi Cepat hanya menampilkan tombol bila pengguna memiliki permission `Create` yang sesuai. Maksimal 6 tombol terlihat, sisanya dalam menu "Lainnya".

---

## 5. Permission

Tidak ada permission baru. Visibilitas widget mengikuti permission yang sudah ada pada bagian 2. Beranda dapat diakses oleh **semua pengguna yang sudah login**.

---

## 6. Data Dictionary (Konsep Data)

Beranda tidak menyimpan data sendiri. Seluruh angka adalah **hasil hitung (read model)** dari modul sumber. Satu-satunya konfigurasi adalah zona waktu aplikasi.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Beranda (widget independen, Segarkan) | **Interactive Server** | Pemuatan widget bertahap, state loading/error per widget, tombol Segarkan tanpa memuat ulang halaman |

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Instalasi baru tanpa data | Widget menampilkan state kosong dengan panduan awal ("Mulai dari Master Data"), tanpa error. |
| E2 | Satu modul sumber lambat atau gagal | Hanya widget terkait yang menampilkan error. Lainnya normal. |
| E3 | Permission pengguna berubah saat Beranda terbuka | Berlaku saat Segarkan atau navigasi berikutnya (selaras AC-AM-19). |
| E4 | Data sangat besar | Angka dihitung agregat di server. Daftar dibatasi 5 baris. |
| E5 | Filter tujuan belum ada di modul | Dicatat sebagai kebutuhan lintas FSD (AC-BR-07) dan ditutup sebelum rilis. |
| E6 | Pengguna hanya punya satu permission View | Hanya satu widget tampil, tata letak menyesuaikan tanpa ruang kosong. |
| E7 | Tanggal sistem server berbeda zona waktu | "Hari ini" mengikuti zona waktu aplikasi, bukan zona browser. |
| E8 | Koneksi Blazor terputus | Banner menyambung ulang. Setelah tersambung, widget dimuat ulang. |

---

## 9. Non-Functional Requirements (Ringkas)

- Beranda terbuka cepat. Target: widget pertama tampil < 1 detik pada data normal, seluruh widget < 3 detik.
- Perhitungan berupa query agregat ringan yang membaca data transaksi tanpa mengunci.
- Bahasa Indonesia, format angka dan tanggal lokal.
- Aksesibilitas: angka dan status tidak hanya dibedakan oleh warna.

---

## 10. Catatan untuk Peran Berikutnya

- **UI/UX:** rancang kartu dan tata letak responsif (grid). Status "Terlambat" memakai warna **dan** ikon/teks.
- **Architect:** sediakan endpoint ringkasan per widget (agar dapat dimuat independen dan dapat dicek permission-nya per widget).
- **Modul lain:** tambahkan filter yang diminta (daftar PO: Terlambat, Jatuh Tempo, PO Customer belum ada, Sisa Rencana > 0; Riwayat Output: hari ini; Daftar WO: Terlambat).

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Daftar widget W-01 s.d. W-13 sudah sesuai | **Disetujui** |
| Q2 | Jatuh tempo memakai jendela 7 hari | **Disetujui** |
| Q3 | Tanpa grafik tren di fase 1 | **Disetujui** |
| Q4 | Data dihitung saat dibuka dan saat Segarkan (tanpa auto-refresh) | **Disetujui** |
| Q5 | Zona waktu aplikasi WIB | **Disetujui** |
