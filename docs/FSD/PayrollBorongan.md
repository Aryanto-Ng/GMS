# FSD — Payroll Borongan

| Atribut | Nilai |
|---|---|
| Modul | Payroll Borongan |
| Versi | 0.1 (Draft, menunggu persetujuan) |
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
- menyajikan rekap yang dapat ditelusuri sampai entri output asal dan dapat diekspor.

### Keputusan Cakupan
- Hanya **upah borongan kotor**. Tidak ada bonus, potongan, kasbon, absensi, atau pajak.
- Semua pekerja adalah pekerja borongan (tidak ada gaji tetap atau harian).
- Reject dan rework tidak dibayar lagi dan tidak ada potongan.
- Koreksi setelah periode ditutup dilakukan lewat **Penyesuaian di periode berikutnya** (FSD Production Output). Periode yang sudah ditutup **tidak dibuka kembali**.
- Mata uang Rupiah.

### Di Luar Scope
- Slip gaji PDF, pembayaran, dan integrasi bank.
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
- **AC-PY-09** Rincian per pekerja dikelompokkan per **WO → Proses → Size**, berisi: Qty, Tarif, Subtotal. Baris jenis **Penyesuaian** diberi penanda, menampilkan Tanggal Asal dan alasan.
- **AC-PY-10** Setiap kelompok dapat dibuka menjadi rincian **per tanggal kerja** dan entri output asal (dapat membuka entri di Production Output bagi pengguna yang berhak).
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
- **AC-PY-24** Detail periode memiliki tab: Ringkasan per pekerja, Rincian, dan Penyesuaian (baris jenis Penyesuaian yang masuk periode ini).

### 4.5 Ekspor
- **AC-PY-25** Pengguna dengan `Payroll.Export` dapat mengekspor **CSV** (UTF-8) untuk: (a) Ringkasan per pekerja dan (b) Rincian lengkap. Ekspor periode Open diberi tanda "BELUM FINAL" pada nama file dan isi.
- **AC-PY-26** Ekspor periode Closed identik dengan snapshot. Setiap ekspor tercatat di audit log (pelaku, periode, jenis).
- **AC-PY-27** Format angka pada CSV mengikuti lokal Indonesia atau netral (desimal titik). Template ditetapkan di fase desain bersama Admin Gaji. Rupiah dibulatkan hanya pada kolom "Dibayarkan".

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| Payroll | View, CreatePeriod, ClosePeriod, Export |

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
| Ekspor CSV | **Interactive Server** (memicu unduhan file melalui endpoint) | Unduhan berkas, bukan halaman |

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
- **Ekspor Excel (.xlsx):** tidak termasuk fase 1. Bila dibutuhkan kemudian, memerlukan library open-source (misalnya ClosedXML, MIT) dan **izin eksplisit user** sesuai aturan dependency.
- **QA:** skenario khusus untuk AC-PY-18, AC-PY-19, AC-PY-22 (konkurensi), AC-PY-12 (total negatif), dan pembulatan di sekitar setengah rupiah.

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Periode berurutan tanpa celah, hanya satu Open pada satu waktu | Default SA, perlu konfirmasi |
| Q2 | Pembulatan sekali per pekerja (jumlah semua baris), setengah ke atas menjauhi nol | Default SA, perlu konfirmasi |
| Q3 | Rekap per pekerja dengan drill-down WO → Proses → Size → Tanggal | Default SA, perlu konfirmasi |
| Q4 | Status periode hanya Open dan Closed (tanpa Draft/Review) | Default SA, perlu konfirmasi |
| Q5 | Ekspor CSV saja di fase 1 (Excel dan PDF slip ditunda) | Default SA, perlu konfirmasi |
| Q6 | Total negatif ditampilkan apa adanya | Default SA, perlu konfirmasi |
| Q7 | Pekerja nonaktif tetap tampil bila punya output pada periode | Default SA, perlu konfirmasi |
| Q8 | `Payroll.View` = boleh melihat tarif dan upah. Admin Gaji penuh, Comfi dan manajemen View. | Default SA, perlu konfirmasi |
| Q9 | Periode Closed bersifat permanen (tidak ada Reopen) | Sesuai keputusan sebelumnya (koreksi lewat Penyesuaian) |
