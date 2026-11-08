# FSD — Payroll Borongan

| Atribut | Nilai |
|---|---|
| Modul | Payroll Borongan |
| Versi | 1.0 (Disetujui user; library ekspor/PDF menunggu izin, lihat bagian 10) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (Pekerja, Process, Size), Work Order (tarif snapshot), Production Output |
| Berinteraksi dengan | Production Output (penguncian tanggal kerja pada periode tertutup, Penyesuaian) |

---

## 1. Business Objective

Menghitung **upah borongan kotor** setiap pekerja Konveksi dari output yang dicatat, per periode yang ditentukan Admin Gaji (mingguan, dua mingguan, atau bulanan). Sistem:

- menjumlahkan `qty × tarif` dari output aktif berdasarkan **tanggal kerja**,
- memakai **tarif snapshot WO** (bukan tarif master terbaru),
- membekukan hasil perhitungan saat periode **ditutup**, sehingga angka yang sudah dibayarkan tidak berubah,
- menyajikan rekap yang dapat ditelusuri sampai entri output asal, termasuk **rincian per hari**,
- mengekspor rekap ke **Excel (.xlsx) dan CSV**, serta mencetak **slip gaji PDF** per pekerja.

### Keputusan Cakupan
- Hanya **upah borongan kotor**. Tidak ada bonus, potongan, kasbon, absensi, atau pajak.
- Semua pekerja adalah pekerja borongan (tidak ada gaji tetap atau harian).
- Reject dan rework tidak dibayar lagi dan tidak ada potongan.
- Koreksi setelah periode ditutup dilakukan lewat **Penyesuaian di periode berikutnya** (FSD Production Output). Periode yang sudah ditutup **tidak dibuka kembali**.
- Mata uang Rupiah.

### Di Luar Scope
- Pembayaran dan integrasi bank.
- Komponen gaji selain borongan.
- Pembukaan kembali periode yang sudah ditutup.
- Keuangan Konveksi (pencatatan keuangan terpisah di luar sistem).

---

## 2. Konsep Kunci

### 2.1 Periode Gaji
- Berisi Tanggal Mulai dan Tanggal Selesai (inklusif), serta status **Open** atau **Closed**.
- Periode **berurutan tanpa celah dan tanpa tumpang tindih**: Tanggal Mulai periode baru = hari setelah Tanggal Selesai periode sebelumnya. Periode pertama bebas menentukan Tanggal Mulai.
- Hanya **satu periode Open** pada satu waktu. Periode baru hanya dapat dibuat setelah periode sebelumnya Closed.
- Panjang periode ditentukan Admin Gaji saat membuat (fleksibel).

### 2.2 Sumber Perhitungan
Semua baris output **aktif** (tidak Voided) dengan pekerja, yaitu jenis **Output** dan **Penyesuaian**, yang Tanggal Kerjanya berada dalam periode. Per baris:

`Upah baris = Qty × Tarif snapshot WO (langkah Internal, Size)`

- Qty penyesuaian bisa negatif sehingga upahnya negatif.
- Proses vendor tidak masuk perhitungan.

### 2.3 Pembulatan
- Upah baris tidak dibulatkan (presisi tarif 5 digit desimal).
- **Total per pekerja** = jumlah seluruh upah baris pekerja itu, lalu **dibulatkan sekali** ke rupiah penuh (setengah ke atas menjauhi nol).
- **Total periode** = jumlah total per pekerja yang sudah dibulatkan.
- Rekap menampilkan `Jumlah (sebelum pembulatan)` dan `Dibayarkan (dibulatkan)`.

### 2.4 Status Periode

| Status | Arti |
|---|---|
| **Open** | Rekap dihitung **langsung** dari data output terkini (berubah bila ada input, void, koreksi) |
| **Closed** | Rekap **dibekukan** (disimpan). Tanggal kerja pada periode ini terkunci bagi input, void, dan koreksi di Production Output. |

Penutupan bersifat permanen.

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-PY-01 | Admin Gaji | membuat periode gaji dengan rentang tanggal yang saya pilih | gaji dapat dihitung mingguan, dua mingguan, atau bulanan |
| US-PY-02 | Admin Gaji | melihat rekap upah per pekerja secara langsung selama periode Open | dapat memeriksa sebelum menutup |
| US-PY-03 | Admin Gaji | menelusuri total pekerja sampai rincian WO, proses, size, dan hari | dapat memverifikasi angka |
| US-PY-04 | Admin Gaji | menutup periode | angka final tidak berubah dan input tanggal itu terkunci |
| US-PY-05 | Admin Gaji | melihat periode yang sudah ditutup persis seperti saat ditutup | arsip gaji konsisten |
| US-PY-06 | Admin Gaji | mengekspor rekap periode ke CSV | dipakai untuk pembayaran dan arsip |
| US-PY-07 | Admin Gaji | melihat penyesuaian yang masuk ke periode ini beserta tanggal asalnya | koreksi periode lalu transparan |
| US-PY-08 | Pemilik/Manajemen | melihat total upah per periode | memantau biaya tenaga kerja |

---

## 4. Acceptance Criteria

### 4.1 Pembuatan dan Pengelolaan Periode
- **AC-PY-01** Pengguna dengan `Payroll.CreatePeriod` membuat periode: Tanggal Mulai, Tanggal Selesai, Catatan (opsional). Kode periode otomatis (format `GJ-YYYYMMDD-YYYYMMDD` dari rentang tanggalnya).
- **AC-PY-02** Untuk periode ke-2 dan seterusnya, Tanggal Mulai otomatis = hari setelah periode sebelumnya dan tidak dapat diubah. Tanggal Selesai wajib ≥ Tanggal Mulai.
- **AC-PY-03** Periode baru ditolak bila masih ada periode Open.
- **AC-PY-04** Untuk periode pertama, Tanggal Mulai bebas tetapi **tidak boleh lebih akhir dari Tanggal Kerja output aktif paling awal** yang ada. Sistem menampilkan tanggal tersebut sebagai petunjuk.
- **AC-PY-05** Selama Open, Tanggal Selesai dan Catatan dapat diubah (Tanggal Selesai ≥ Tanggal Mulai). Untuk periode pertama, Tanggal Mulai juga dapat diubah dengan syarat AC-PY-04. Tidak ada penghapusan periode.
- **AC-PY-06** Setelah periode pertama dibuat, Production Output menolak Tanggal Kerja yang lebih awal dari Tanggal Mulai periode pertama (FSD Production Output AC-PR-10a).

### 4.2 Rekap (Periode Open)
- **AC-PY-07** Rekap menampilkan per pekerja: Kode, Nama, Total Qty, Jumlah (sebelum pembulatan), Dibayarkan (dibulatkan). Di bawahnya total periode dan jumlah pekerja.
- **AC-PY-08** Pekerja dengan output pada periode tampil walaupun sudah nonaktif. Pekerja tanpa output tidak tampil.
- **AC-PY-09** Rincian per pekerja tersedia dalam dua tampilan yang dapat ditukar:
  - **Per WO → Proses → Size:** Qty, Tarif, Subtotal.
  - **Per Hari:** diurutkan menurut Tanggal Kerja. Tiap hari menampilkan WO, Proses, Size, Qty, Tarif, Subtotal, serta **subtotal harian**, dan total pekerja di akhir.
  Baris jenis **Penyesuaian** diberi penanda, menampilkan Tanggal Asal dan alasan.
- **AC-PY-10** Tab **Rincian Harian** pada tingkat periode menampilkan seluruh pekerja: Tanggal Kerja, Pekerja, WO, Proses, Size, Jenis, Qty, Tarif, Subtotal, dengan subtotal harian per pekerja. Filter: rentang tanggal dalam periode, pekerja, WO, proses. Tiap baris dapat ditelusuri ke entri output asal (bagi pengguna yang berhak di Production Output). Qty dijumlahkan lintas Color karena tarif tidak bergantung pada Color. Rincian per Color tersedia pada detail entri.
- **AC-PY-10a** Tersedia tampilan **Matriks Harian**: baris = pekerja, kolom = tanggal dalam periode, sel = total qty dan upah hari itu, ditambah kolom total per pekerja dan baris total per hari. Hari tanpa output pekerja ditampilkan kosong.
- **AC-PY-11** Rekap Open dihitung ulang dari data terkini saat dibuka dan dapat disegarkan. Tampil penanda "Belum final".
- **AC-PY-12** Total pekerja yang bernilai negatif (akibat penyesuaian) ditampilkan apa adanya dengan penanda khusus. Sistem tidak membatasinya ke nol.
- **AC-PY-13** Filter dan pencarian: nama atau kode pekerja, WO, proses.
- **AC-PY-14** Tarif dan nilai rupiah hanya tampil di modul ini bagi pengguna dengan `Payroll.View`.

### 4.3 Penutupan Periode
- **AC-PY-15** Pengguna dengan `Payroll.ClosePeriod` menutup periode Open. Dialog konfirmasi menampilkan: rentang tanggal, jumlah pekerja, total periode, serta peringatan bahwa penutupan **permanen**.
- **AC-PY-16** Penutupan tidak diblok oleh data apa pun, tetapi dialog menampilkan peringatan (tidak memblokir) untuk: tanggal selesai periode di masa depan, dan pekerja dengan total negatif.
- **AC-PY-17** Saat ditutup, sistem menyimpan snapshot beku: baris rincian (pekerja, WO, proses, size, tanggal kerja, jenis, qty, tarif, subtotal, rujukan entri) beserta total per pekerja (sebelum dan sesudah pembulatan) dan total periode. Penutupan dilakukan atomik.
- **AC-PY-18** Setelah Closed, rekap periode **selalu** diambil dari snapshot dan tidak berubah walau data master atau entri lain berubah.
- **AC-PY-19** Setelah Closed, Production Output menolak input, void, dan koreksi untuk Tanggal Kerja dalam rentang periode (AC-PR-10, AC-PR-13). Kesalahan diperbaiki dengan Penyesuaian yang masuk ke periode berikutnya.
- **AC-PY-20** Periode Closed tidak dapat dibuka kembali, diubah, atau dihapus.
- **AC-PY-21** Penutupan serta pembuatan periode tercatat di audit log (pelaku, waktu, total periode).
- **AC-PY-22** Dua pengguna yang menutup periode bersamaan: hanya satu yang berhasil. Penyimpanan Production Output yang bersamaan dengan penutupan diserialkan sehingga tidak ada entri yang lolos ke tanggal terkunci.

### 4.4 Daftar Periode
- **AC-PY-23** Daftar periode: Kode, rentang tanggal, status, jumlah pekerja, total (untuk Closed dari snapshot, untuk Open dihitung langsung), ditutup oleh dan kapan.
- **AC-PY-24** Detail periode memiliki tab: Ringkasan per pekerja, Rincian, Rincian Harian (termasuk Matriks Harian), dan Penyesuaian (baris jenis Penyesuaian yang masuk periode ini).

### 4.5 Ekspor dan Slip Gaji
- **AC-PY-25** Pengguna dengan `Payroll.Export` dapat mengekspor periode ke **Excel (.xlsx)**. Satu berkas berisi sheet: Ringkasan, Rincian (WO → Proses → Size), Harian, dan Penyesuaian. Kolom angka disimpan sebagai **angka** (bukan teks) dengan format yang sesuai (tarif hingga 5 desimal, rupiah tanpa desimal pada kolom Dibayarkan). Baris judul memuat periode dan status.
- **AC-PY-26** `Payroll.Export` juga mengekspor **CSV** (UTF-8 dengan BOM, pemisah koma, desimal titik) untuk: (a) Ringkasan per pekerja dan (b) Rincian Harian lengkap.
- **AC-PY-27** Ekspor periode Open diberi tanda "BELUM FINAL" pada nama berkas dan isi. Ekspor periode Closed identik dengan snapshot. Rupiah dibulatkan hanya pada kolom "Dibayarkan". Setiap ekspor tercatat di audit log (pelaku, periode, jenis).
- **AC-PY-28** Pengguna dengan `Payroll.PrintSlip` dapat membuat **Slip Gaji PDF**: (a) untuk satu pekerja, atau (b) satu berkas berisi slip seluruh pekerja pada periode (satu pekerja mulai di halaman baru, urut Kode Pekerja).
- **AC-PY-29** Isi slip (A4 portrait): kop (nama dan alamat Konveksi dari konfigurasi aplikasi), judul "Slip Upah Borongan", periode, Kode dan Nama Pekerja, tabel ringkas **WO → Proses → Size** (Qty, Tarif, Subtotal), baris Penyesuaian (dengan Tanggal Asal), **Jumlah (sebelum pembulatan)** dan **Dibayarkan (dibulatkan)**, kolom tanda terima ("Diterima oleh"), serta catatan "Dicetak oleh [pengguna] pada [waktu]".
- **AC-PY-30** Pilihan **"Sertakan rincian harian"** (default nonaktif) menambahkan tabel per Tanggal Kerja pada slip.
- **AC-PY-31** Slip periode Closed bersumber dari snapshot dan identik dengan rekap. Slip periode Open diberi watermark **"BELUM FINAL"**.
- **AC-PY-32** Total negatif ditandai jelas pada slip. Teks dan huruf slip menampilkan karakter Indonesia dengan benar. Setiap pencetakan slip tercatat di audit log (pelaku, periode, pekerja atau "semua").

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| Payroll | View, CreatePeriod, ClosePeriod, Export, PrintSlip |

Usulan awal Role: **Admin Gaji (Konveksi)** penuh. **Comfi** dan **Owner/Manajemen** `Payroll.View`. `Payroll.View` berarti dapat melihat tarif dan upah.

---

## 6. Data Dictionary (Konsep Data)

### Periode Gaji
| Atribut | Keterangan |
|---|---|
| PayrollPeriodId, Kode | Identitas, kode dari rentang tanggal |
| TanggalMulai, TanggalSelesai | Inklusif |
| Status | Open, Closed |
| Catatan | Opsional |
| ClosedAt/By | Jejak penutupan |
| TotalDibayarkan | Hanya terisi setelah Closed (snapshot) |

### Snapshot Rincian (dibuat saat Closed)
| Atribut | Keterangan |
|---|---|
| PeriodId | |
| WorkerId + salinan Kode, Nama | |
| WOId + salinan Kode WO | |
| Proses (salinan Kode, Nama) | |
| Size (salinan Kode) | |
| TanggalKerja | |
| Jenis | Output, Penyesuaian |
| TanggalAsal, Alasan | Untuk Penyesuaian |
| Qty (bertanda), Tarif (5 desimal), Subtotal (tanpa pembulatan) | |
| SourceEntryId | Rujukan entri output asal |

### Snapshot Total Pekerja (dibuat saat Closed)
PeriodId, WorkerId (+ salinan Kode, Nama), TotalQty, JumlahSebelumPembulatan, Dibayarkan (dibulatkan).

> Untuk periode Open, rekap adalah **hasil hitung** dari Production Output dan tarif snapshot WO, bukan data tersimpan.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Daftar periode dan formulir buat/ubah periode | **Interactive Server** | Validasi tanggal berurutan secara langsung |
| Detail periode (ringkasan, rincian, drill-down, penyesuaian) | **Interactive Server** | Pembukaan kelompok, filter, dan pencarian dinamis |
| Dialog Penutupan Periode | **Interactive Server** | Menampilkan ringkasan dan peringatan sebelum penutupan |
| Ekspor Excel/CSV dan Slip Gaji PDF | **Interactive Server** (memicu unduhan file melalui endpoint) | Unduhan berkas, bukan halaman |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Output diinput pada saat yang sama dengan penutupan periode | Operasi diserialkan. Entri yang tersimpan setelah penutupan untuk tanggal terkunci ditolak (AC-PY-22). |
| E2 | Tanggal Selesai periode lebih awal dari output aktif yang sudah ada sesudahnya | Tidak masalah. Output sesudah tanggal selesai menjadi bagian periode berikutnya. |
| E3 | Periode pertama dibuat dengan Tanggal Mulai lebih akhir dari output tertua | Ditolak (AC-PY-04). |
| E4 | Output dengan tanggal kerja di antara periode yang sudah ditutup dan belum dibuat periode baru | Diizinkan (tidak berada pada periode tertutup). Masuk saat periode yang memuat tanggal itu dibuat. |
| E5 | Pengguna ingin periode yang sudah ditutup dibuka lagi | Tidak tersedia. Gunakan Penyesuaian di periode berikutnya (AC-PY-19). |
| E6 | Total pekerja negatif | Ditampilkan apa adanya (AC-PY-12), penanganan pembayaran di luar sistem. |
| E7 | Penyesuaian dengan Tanggal Kerja di periode Open yang berbeda dari periode asal | Masuk periode yang memuat Tanggal Kerja penyesuaian. Tanggal Asal ditampilkan sebagai keterangan. |
| E8 | Pekerja dinonaktifkan | Tetap tampil di rekap bila punya output pada periode. |
| E9 | Master tarif berubah setelah WO dirilis | Tidak berpengaruh. Upah memakai tarif snapshot WO. |
| E10 | WO di-Close atau dibatalkan setelah output dicatat | Output tetap dihitung gajinya. |
| E11 | Void output pada periode Open | Rekap langsung ikut berubah. Pada periode Closed ditolak oleh Production Output. |
| E12 | Pembulatan menghasilkan selisih antara jumlah baris dan jumlah dibayarkan | Wajar. Selisih ditampilkan lewat kolom "Jumlah (sebelum pembulatan)". |
| E13 | Rekap berukuran besar (banyak pekerja dan baris) | Rincian dibuka per kelompok dan dipaging di server. Ekspor dibuat dengan streaming. |
| E14 | Tarif snapshot hilang pada baris (data tidak konsisten) | Penutupan ditolak dengan daftar baris bermasalah dan dilaporkan sebagai kesalahan data. Tidak boleh ada upah bernilai tebakan. |
| E15 | Koneksi terputus saat menutup periode | Transaksi atomik: periode tetap Open bila belum selesai. Pengguna memeriksa status setelah menyambung kembali. |

---

## 9. Non-Functional Requirements (Ringkas)

- Bahasa UI Indonesia. Format angka: pemisah ribuan dan desimal lokal. Tarif tampil hingga 5 desimal (tanpa nol ekor yang tidak perlu).
- Perhitungan memakai tipe desimal presisi tinggi, tanpa floating point.
- Hanya pengguna dengan `Payroll.View` yang dapat memperoleh data tarif dan upah (penegakan di server, termasuk pada ekspor).
- Penutupan periode atomik dan konsisten terhadap input bersamaan.
- Seluruh aksi penting (buat, ubah, tutup, ekspor) tercatat di audit log.

---

## 10. Catatan untuk Peran Berikutnya

- **Architect:** rancang mekanisme penguncian tanggal kerja yang atomik terhadap penutupan periode. Putuskan apakah snapshot disimpan sebagai tabel terpisah (disarankan) dan bagaimana ekspor CSV disajikan melalui endpoint.
- **Library ekspor/PDF (WAJIB IZIN USER SEBELUM DITAMBAHKAN):** hasil riset SA per 2026-11-08 (versi diverifikasi ulang oleh Architect saat instalasi):
  - **PDF:** rekomendasi **PDFsharp + MigraDoc** (`PDFsharp-MigraDoc`, lisensi MIT, seri 6.x menargetkan .NET 10). **Jangan** QuestPDF: lisensi Community terbaru bersifat source-available dengan syarat pendapatan tahunan, bukan open source murni, sehingga bertentangan dengan aturan proyek.
  - **Excel:** rekomendasi **ClosedXML** (MIT, 0.105.x). Alternatif: MiniExcel (Apache-2.0, ringan untuk data besar). **Jangan** EPPlus (Polyform Noncommercial, berbayar untuk penggunaan komersial).
  - **CSV** tidak membutuhkan library.
  - Semua versi dicatat di `src/Directory.Packages.props` (Central Package Management).
- **DevOps/Architect (font PDF):** PDFsharp pada Linux/Docker membutuhkan penyediaan font sendiri (font resolver) agar PDF tidak gagal. Sertakan font berlisensi terbuka (misalnya keluarga Noto atau Liberation) dalam image dan uji karakter Indonesia.
- **Konfigurasi aplikasi:** nama dan alamat Konveksi untuk kop slip disimpan di konfigurasi (bukan di kode).
- **QA:** skenario khusus untuk AC-PY-18, AC-PY-19, AC-PY-22 (konkurensi), AC-PY-12 (total negatif), dan pembulatan di sekitar setengah rupiah.

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Periode berurutan tanpa celah, hanya satu Open pada satu waktu | **Disetujui** |
| Q2 | Pembulatan sekali per pekerja (jumlah semua baris), setengah ke atas menjauhi nol | **Disetujui** |
| Q3 | Rekap butuh **rincian per hari** (tampilan Per Hari, tab Rincian Harian, Matriks Harian) | **Disetujui** (koreksi dari user) |
| Q4 | Status periode hanya Open dan Closed | **Disetujui** |
| Q5 | Ekspor Excel/CSV dan **slip gaji PDF masuk fase 1** (library open source dan stabil) | **Disetujui**; izin penambahan library masih menunggu (bagian 10) |
| Q6 | Total negatif ditampilkan apa adanya | **Disetujui** |
| Q7 | Pekerja nonaktif tetap tampil bila punya output pada periode | **Disetujui** |
| Q8 | `Payroll.View` = boleh melihat tarif dan upah. Admin Gaji penuh, Comfi dan manajemen View. | **Disetujui** |
| Q9 | Periode Closed bersifat permanen (tidak ada Reopen) | Sesuai keputusan sebelumnya (koreksi lewat Penyesuaian) |
