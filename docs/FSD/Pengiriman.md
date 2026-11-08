# FSD — Pengiriman ke Customer

| Atribut | Nilai |
|---|---|
| Modul | Pengiriman ke Customer (Surat Jalan) |
| Versi | 1.0 (Disetujui user) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (Customer, FG, Color, Size), Customer PO, Serah Terima & Stok FG |
| Berinteraksi dengan | Customer PO (Terkirim dan penutupan otomatis), Stok FG (pengurangan stok) |

---

## 1. Business Objective

Mencatat barang jadi yang dikirim Comfi ke customer. Tanpa modul ini, stok FG tidak pernah berkurang dan PO tidak pernah berstatus terkirim. Setiap pengiriman:

- mengurangi **Stok FG**,
- mengonsumsi **alokasi** pada baris PO,
- menambah **Terkirim** pada baris PO,
- menutup PO otomatis saat seluruh kebutuhan terkirim.

### Keputusan Cakupan
- **Satu surat jalan untuk satu PO.**
- Pengiriman hanya dari qty yang sudah **dialokasikan** ke baris PO tersebut (FSD Customer PO 2.2), dan tidak boleh melebihi `Qty − Terkirim` baris PO.
- Pengiriman bisa **bertahap** (banyak surat jalan per PO).
- Surat jalan dicatat langsung (tidak ada status Draft).
- Cetak/PDF surat jalan **ditunda** (butuh keputusan library).

### Di Luar Scope
- Invoice, harga, dan pembayaran.
- Retur dari customer.
- Biaya dan ekspedisi.
- Tanda terima digital/tanda tangan.
- Cetak/PDF surat jalan.

---

## 2. Konsep Kunci

| Aspek | Aturan |
|---|---|
| Sumber qty | Hanya `Dialokasikan` aktif pada baris PO (FG × Color × Size) |
| Batas atas | `min(Dialokasikan aktif, Qty − Terkirim)` per baris PO |
| Dampak saat disimpan | Stok Fisik −qty (entri ledger Pengiriman); alokasi dikonsumsi (entri Konsumsi); Terkirim +qty |
| Status PO yang diizinkan | In Progress (atau Confirmed bila belum otomatis berubah). Draft, Completed, dan Cancelled ditolak. |
| Penutupan PO | Otomatis Completed bila semua baris `Terkirim = Qty` |
| Void | Membalik semua dampak. PO yang Completed karenanya kembali ke In Progress. |

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-PG-01 | Staf Comfi | membuat surat jalan dari PO dengan qty per FG × Color × Size | pengiriman customer tercatat |
| US-PG-02 | Staf Comfi | melihat qty yang dialokasikan dan sisa kebutuhan per baris | tidak mengirim melebihi yang dialokasikan atau dipesan |
| US-PG-03 | Staf Comfi | langsung mengalokasikan stok dari layar pengiriman bila alokasi kurang | tidak berpindah-pindah halaman |
| US-PG-04 | Staf Comfi | mengirim bertahap untuk satu PO | sesuai tanggal kirim per baris |
| US-PG-05 | Staf Comfi | mengubah informasi non-qty pada surat jalan (penerima, kendaraan, sopir, catatan) | data pengiriman tetap rapi |
| US-PG-06 | Staf Comfi | membatalkan (void) atau mengoreksi surat jalan yang salah | stok dan PO kembali akurat |
| US-PG-07 | Staf Comfi/Konveksi | melihat riwayat pengiriman per PO dan seluruhnya | menelusuri apa yang sudah dikirim |
| US-PG-08 | Sistem | menutup PO otomatis saat semua baris terkirim | tidak perlu penutupan manual |

---

## 4. Acceptance Criteria

### 4.1 Pembuatan Surat Jalan
- **AC-PG-01** Pengguna dengan `Pengiriman.Create` memilih **PO** (status In Progress atau Confirmed). Layar menampilkan baris PO (FG, Color, Size, Qty, Terkirim, Dialokasikan, Tanggal Kirim baris, **Maksimal Kirim**).
- **AC-PG-02** Qty kirim per baris adalah bilangan bulat ≥ 0 dan ≤ `Maksimal Kirim = min(Dialokasikan aktif, Qty − Terkirim)`. Minimal satu baris bernilai > 0.
- **AC-PG-03** Header: Kode SJ (otomatis, format `SJ-YYYYMM-NNNN`, aman dari duplikasi), Tanggal Kirim (wajib, tidak di masa depan), Nama Penerima (teks, opsional), Kendaraan/No. Polisi (opsional), Sopir (opsional), Catatan (opsional). Customer diambil dari PO.
- **AC-PG-04** Bila alokasi kurang dari yang ingin dikirim, layar menyediakan aksi **"Alokasikan stok"** per baris (membuka dialog alokasi PO) bagi pengguna yang memiliki permission `CustomerPO.Alokasi.Allocate`. Tanpa permission itu, hanya pesan petunjuk yang tampil.
- **AC-PG-05** Saat disimpan, dalam satu transaksi: entri ledger Pengiriman (−) dibuat per FG × Color × Size, entri Konsumsi alokasi dibuat, `Terkirim` baris PO bertambah, dan status PO diperbarui (In Progress bila belum, Completed bila semua baris terkirim penuh).
- **AC-PG-06** Dua pengguna yang mengirim dari alokasi yang sama secara bersamaan: hanya yang pertama lolos sesuai ketersediaan. Yang kedua ditolak dengan batas terbaru.

### 4.2 Perubahan Setelah Disimpan
- **AC-PG-07** Pengguna dengan `Pengiriman.Edit` dapat mengubah **hanya** Tanggal Kirim (tidak di masa depan), Penerima, Kendaraan, Sopir, dan Catatan. Perubahan tercatat di audit log.
- **AC-PG-08** Qty, baris, dan PO pada surat jalan **tidak dapat diubah**. Gunakan Void atau Koreksi.

### 4.3 Void dan Koreksi
- **AC-PG-09** Void (`Pengiriman.Void`) wajib alasan. Dalam satu transaksi: entri ledger pembalik (+) dibuat, konsumsi alokasi dibalik (qty menjadi `Dialokasikan` aktif kembali pada baris PO), `Terkirim` baris PO berkurang. Surat jalan berstatus Voided dan tetap terlihat.
- **AC-PG-10** Bila void membuat PO yang Completed tidak lagi terpenuhi, PO kembali otomatis ke In Progress.
- **AC-PG-11** Void ditolak bila PO berstatus Cancelled. (PO yang sudah memiliki pengiriman tidak dapat dibatalkan, sehingga situasi ini hanya mungkin bila data tidak konsisten dan harus dilaporkan.)
- **AC-PG-12** **Koreksi** (`Pengiriman.Correct`) = void surat jalan lama + surat jalan baru dalam satu transaksi atomik, dengan batas dihitung pada kondisi akhir. Alasan wajib. Surat jalan baru menyimpan rujukan ke yang digantikan.
- **AC-PG-13** Surat jalan Voided tidak dapat diaktifkan kembali.

### 4.4 Daftar dan Detail
- **AC-PG-14** Daftar: Kode SJ, Tanggal Kirim, PO (kode internal dan nomor PO customer), Customer, ringkasan qty, status (Aktif/Void), pembuat. Filter: PO, customer, rentang tanggal, status. Pencarian: kode SJ, kode PO, nomor PO customer.
- **AC-PG-15** Detail menampilkan header, baris (FG, Color, Size, Qty), rujukan antar-surat jalan (hasil koreksi), alasan void, dan jejak. Tab Pengiriman pada detail PO menampilkan surat jalan yang terkait.
- **AC-PG-16** Tombol dan menu mengikuti permission, penegakan di server bersifat final. Seluruh aksi tercatat di audit log.

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| Pengiriman | View, Create, Edit, Void, Correct |

Usulan awal Role: **Comfi** penuh. **Konveksi** `Pengiriman.View` bila dibutuhkan, ditentukan Administrator.

---

## 6. Data Dictionary (Konsep Data)

### Pengiriman (Header)
| Atribut | Keterangan |
|---|---|
| DeliveryId, KodeSJ | Identitas dan kode otomatis |
| POId | Wajib, 1 SJ = 1 PO |
| TanggalKirim | Wajib, tidak di masa depan |
| NamaPenerima, Kendaraan, Sopir, Catatan | Opsional |
| Status | Active, Voided |
| ReplacesDeliveryId | Rujukan SJ yang digantikan (hasil Koreksi) |
| Alasan, VoidedAt/By, CreatedAt/By | Jejak |

### Pengiriman Line
DeliveryId, POLineId (FG × Color × Size), Qty (integer > 0).

> Pengiriman menghasilkan entri ledger stok jenis **Pengiriman** (−) dan entri alokasi jenis **Konsumsi** (FSD Serah Terima & Stok FG dan FSD Customer PO). Void menghasilkan **Pembalikan Pengiriman** (+) dan pembalik Konsumsi.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Form surat jalan (pilih PO, grid qty, batas maksimal, alokasi cepat) | **Interactive Server** | Validasi langsung terhadap alokasi dan sisa kebutuhan |
| Daftar dan detail surat jalan | **Interactive Server** | Filter, paging, void/koreksi dengan pratinjau dampak |
| Tab Pengiriman pada detail PO | **Interactive Server** | Bagian dari halaman PO yang sudah interaktif |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Alokasi berkurang (dilepas pengguna lain) saat form terbuka | Penyimpanan ditolak dengan batas terbaru, input dipertahankan. |
| E2 | Stok fisik berkurang karena penyesuaian saat form terbuka | Penyesuaian tidak dapat menembus total alokasi (FSD Stok), sehingga alokasi tetap aman. |
| E3 | Mengirim qty melebihi `Qty − Terkirim` | Ditolak. Jika customer meminta lebih, revisi Qty PO terlebih dahulu. |
| E4 | Tanggal Kirim lebih awal dari Tanggal PO | Peringatan, tidak diblok. |
| E5 | Void SJ yang membuat PO Completed kembali In Progress | Diizinkan (AC-PG-10) dan tercatat di riwayat status PO. |
| E6 | Void SJ saat PO Cancelled | Ditolak (AC-PG-11). |
| E7 | SJ sebagian untuk baris yang memiliki beberapa tanggal kirim | Pengiriman memakai baris PO yang dipilih. Satu baris PO = satu kombinasi FG × Color × Size × Tanggal Kirim. |
| E8 | FG, Color, atau Customer dinonaktifkan setelah PO dibuat | Pengiriman tetap diizinkan untuk PO yang ada. |
| E9 | Koneksi Blazor terputus saat mengisi form | Input belum tersimpan hilang. Peringatan sebelum meninggalkan halaman. |
| E10 | Dua pengguna menyimpan SJ yang bersama-sama melebihi alokasi | Hanya yang pertama lolos (AC-PG-06). |
| E11 | Surat jalan butuh dicetak | Tidak tersedia di fase 1. Pengguna memakai data di layar sebagai rujukan. |

---

## 9. Non-Functional Requirements (Ringkas)

- Bahasa UI Indonesia, tanggal dan angka lokal.
- Seluruh dampak (ledger stok, alokasi, metrik PO, status PO) diperbarui **atomik** dalam satu transaksi.
- Seluruh pemeriksaan permission di server. Seluruh perubahan penting tercatat di audit log.

---

## 10. Catatan untuk Peran dan FSD Berikutnya

- **Customer PO:** `Terkirim` dan status Completed diturunkan dari pengiriman aktif. Tab Pengiriman pada detail PO bersumber dari modul ini.
- **Architect:** satukan penanganan transaksi untuk ledger stok, konsumsi alokasi, dan status PO agar konsisten pada konkurensi dan pada void.
- **Cetak/PDF:** akan dibahas bersama keputusan library pencetakan (perlu izin sesuai aturan dependency).

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Format nomor `SJ-YYYYMM-NNNN` | **Disetujui** |
| Q2 | Field header: tanggal, penerima, kendaraan/no. polisi, sopir, catatan | **Disetujui** |
| Q3 | Satu surat jalan untuk satu PO | **Disetujui** |
| Q4 | Void diizinkan walau PO sudah Completed (PO kembali In Progress) | **Disetujui** |
| Q5 | Cetak/PDF surat jalan ditunda | **Disetujui** (jawaban sebelumnya) |
