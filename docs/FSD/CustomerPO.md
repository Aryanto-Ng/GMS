# FSD — Customer PO

| Atribut | Nilai |
|---|---|
| Modul | Customer PO |
| Versi | 1.0 (Disetujui user; Q6 dan Q7 berlaku sebagai default) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (Customer, FG, Size, Color) |
| Berinteraksi dengan | Work Order, Serah Terima & Stok FG, Pengiriman |

---

## 1. Business Objective

Comfi menerima permintaan produksi dari customer dalam bentuk PO. Terkadang komitmen dari customer sudah ada **sebelum** nomor PO customer terbit, sehingga Comfi membutuhkan dokumen internal sebagai dasar kerja. Modul ini:

- mencatat kebutuhan customer per **FG × Color × Size** beserta tanggal kirim,
- menjadi dasar pembuatan Work Order (1 WO = 1 PO),
- memungkinkan Comfi memenuhi kebutuhan dari **stok FG** (alokasi/reservasi) sebelum memproduksi,
- menunjukkan status pemenuhan: berapa yang dialokasikan, sedang diproduksi, dan terkirim,
- menutup PO otomatis setelah seluruh kebutuhan terkirim.

### Keputusan Cakupan
- PO **tanpa harga/nilai** (keuangan di luar scope).
- **Setiap WO wajib terkait PO.** Produksi untuk stok dilakukan dengan membuka PO internal dengan nomor PO customer dikosongkan.
- Alokasi stok hanya untuk FG, Color, dan Size yang **sama persis**.

### Di Luar Scope
- Harga, diskon, invoice, dan pembayaran.
- Cetak/PDF dokumen PO.
- Satu WO yang menggabungkan beberapa PO.
- Pembukaan kembali PO yang sudah Completed.

---

## 2. Konsep Kunci

### 2.1 Identitas PO
- **Kode PO Internal** (otomatis, unik): format `PO-YYYYMM-NNNN` (nomor urut direset tiap bulan).
- **Nomor PO Customer** (opsional): dapat dikosongkan saat PO dibuka dan diisi kemudian. Bila terisi, harus unik per Customer di antara PO yang tidak dibatalkan.

### 2.2 Metrik Pemenuhan per Baris PO
Untuk setiap baris PO (FG × Color × Size):

| Metrik | Arti |
|---|---|
| **Qty** | Kebutuhan customer |
| **Terkirim** | Total yang sudah dikirim ke customer (dari modul Pengiriman) |
| **Dialokasikan** | Stok FG yang sedang dicadangkan untuk baris ini dan belum terkirim |
| **Dalam Produksi** | Total qty WO aktif untuk baris ini dikurangi qty yang sudah diserahterimakan |
| **Sisa Rencana** | `Qty − Terkirim − Dialokasikan − Dalam Produksi` (selalu ≥ 0). Ini kebutuhan yang belum punya rencana pemenuhan. |

Perilaku terhadap modul lain:
- **Membuat WO** untuk baris ini dibatasi sampai `Sisa Rencana`.
- **Serah Terima FG** dari suatu WO otomatis mengalokasikan qty yang diterima ke baris PO asal WO, sebesar `min(qty serah terima, sisa qty WO yang belum diserahterimakan)`. Kelebihan produksi (di atas qty WO) menjadi **stok bebas** (tidak teralokasi).
- **Pengiriman** untuk baris ini hanya dapat diambil dari `Dialokasikan`, dan tidak boleh melebihi `Qty − Terkirim`. Untuk mengirim dari stok bebas, lakukan alokasi lebih dulu.

### 2.3 Status PO

```
Draft ──► Confirmed ──► In Progress ──► Completed
  │           │              │
  └───────────┴──────────────┴──► Cancelled
```

| Status | Arti | Perubahan |
|---|---|---|
| Draft | Sedang disusun | Manual (dibuat) |
| Confirmed | Komitmen customer ada (PO customer boleh belum terbit) | Manual oleh Comfi |
| In Progress | Sudah ada alokasi, WO aktif, atau pengiriman | **Otomatis** pada kejadian pertama |
| Completed | Seluruh baris terkirim penuh (`Terkirim = Qty` untuk semua baris) | **Otomatis** |
| Cancelled | Dibatalkan | Manual oleh Comfi, alasan wajib |

Alokasi dan pembuatan WO hanya diperbolehkan pada status **Confirmed** dan **In Progress**.

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-PO-01 | Staf Comfi | membuka PO internal walau nomor PO customer belum ada | pekerjaan bisa dimulai berdasarkan komitmen |
| US-PO-02 | Staf Comfi | mengisi nomor PO customer kemudian | dokumen internal cocok dengan dokumen customer |
| US-PO-03 | Staf Comfi | menginput baris kebutuhan FG × Color × Size dengan tanggal kirim per baris | pengiriman bertahap terencana |
| US-PO-04 | Staf Comfi | mengonfirmasi PO | WO dan alokasi bisa dimulai |
| US-PO-05 | Staf Comfi | melihat stok FG yang tersedia untuk baris PO dan mengalokasikannya | kebutuhan dipenuhi dari stok lebih dulu |
| US-PO-06 | Staf Comfi | melihat Sisa Rencana per baris | tahu berapa yang harus diproduksi lewat WO |
| US-PO-07 | Staf Comfi | merevisi PO setelah dikonfirmasi dengan batasan yang aman | perubahan dari customer dapat diakomodasi |
| US-PO-08 | Staf Comfi | membatalkan PO dengan alasan | PO yang tidak jadi tidak membebani stok |
| US-PO-09 | Staf Comfi/Konveksi | melihat progres PO (alokasi, produksi, terkirim) dan baris yang terlambat | prioritas kerja jelas |
| US-PO-10 | Sistem | menutup PO otomatis saat semua baris terkirim | tidak perlu penutupan manual |

---

## 4. Acceptance Criteria

### 4.1 Pembuatan dan Header
- **AC-PO-01** Saat PO dibuat, sistem membuat Kode PO Internal otomatis (format `PO-YYYYMM-NNNN`, aman dari duplikasi saat dibuat bersamaan).
- **AC-PO-02** Header: Customer (wajib, aktif), Tanggal PO (wajib), Nomor PO Customer (opsional), Keterangan (opsional).
- **AC-PO-03** Nomor PO Customer boleh dikosongkan pada semua status hingga Completed, dan dapat diisi atau diubah dengan audit log. Bila terisi dan sudah ada PO lain (tidak dibatalkan) dengan Customer dan nomor yang sama, simpan ditolak.
- **AC-PO-04** PO baru berstatus Draft. PO Draft dapat diubah bebas.

### 4.2 Baris PO
- **AC-PO-05** Baris berisi FG (aktif), Color (aktif), Size (harus termasuk Size Berlaku FG), Qty (bilangan bulat > 0), Tanggal Kirim (wajib), Keterangan (opsional).
- **AC-PO-06** Input baris tersedia dalam **grid**: pilih FG + Color + Tanggal Kirim, lalu isi qty per Size dalam satu baris grid. Sistem menyimpannya sebagai satu baris data per Size. Kombinasi FG + Color + Size + Tanggal Kirim tidak boleh duplikat dalam satu PO.
- **AC-PO-07** FG pada PO tidak wajib berstatus Siap Produksi (pemeriksaan kesiapan dilakukan saat WO dirilis).
- **AC-PO-08** PO tidak dapat dikonfirmasi bila tidak memiliki baris.

### 4.3 Konfirmasi, Status, dan Penutupan
- **AC-PO-09** Comfi mengubah Draft menjadi Confirmed. Konfirmasi tidak mensyaratkan Nomor PO Customer.
- **AC-PO-10** Status berubah ke In Progress otomatis saat pertama kali terjadi alokasi, pembuatan WO, atau pengiriman pada PO tersebut.
- **AC-PO-11** Status berubah ke Completed otomatis saat semua baris `Terkirim = Qty`. PO Completed terkunci (tidak dapat direvisi).
- **AC-PO-12** Status Completed, Confirmed, dan In Progress tampil dengan penanda warna dan teks yang berbeda di daftar.

### 4.4 Alokasi Stok
- **AC-PO-13** Pada baris PO berstatus Confirmed atau In Progress, pengguna dengan permission dapat mengalokasikan stok. Qty alokasi ≤ `min(Stok Tersedia FG-Color-Size, Sisa Rencana)`. **Stok Tersedia** = stok fisik − total alokasi aktif semua PO.
- **AC-PO-14** Layar alokasi menampilkan Stok Fisik, Teralokasi (semua PO), dan Stok Tersedia untuk kombinasi FG × Color × Size baris tersebut.
- **AC-PO-15** Alokasi dapat dikurangi (dilepas) selama qty dilepas ≤ `Dialokasikan` aktif. Qty yang dilepas kembali menjadi stok tersedia, dan `Sisa Rencana` bertambah.
- **AC-PO-16** Setiap alokasi dan pelepasan tercatat (pelaku, waktu, qty, alasan opsional) dan tidak dapat diubah. Koreksi dilakukan dengan alokasi atau pelepasan baru.
- **AC-PO-17** Dua pengguna yang mengalokasikan stok yang sama secara bersamaan: hanya yang pertama berhasil sesuai ketersediaan. Yang kedua ditolak dengan pesan ketersediaan terbaru.
- **AC-PO-18** Alokasi otomatis akibat Serah Terima FG mengikuti aturan bagian 2.2 dan tampil pada riwayat alokasi dengan sumber "Serah Terima".

### 4.5 Revisi PO
- **AC-PO-19** Pada status Confirmed dan In Progress, pengguna dapat: mengubah Nomor PO Customer, Keterangan, dan Tanggal Kirim; menambah baris; menaikkan Qty.
- **AC-PO-20** Qty baris tidak boleh diturunkan di bawah `Terkirim + Dialokasikan + Dalam Produksi`. Pesan menyebutkan komponen mana yang menahan, misalnya "Lepas alokasi 20 pcs atau ubah WO terlebih dahulu".
- **AC-PO-21** FG, Color, dan Size pada baris yang sudah memiliki alokasi, WO, atau pengiriman **tidak dapat diubah**. Gunakan baris baru dan turunkan Qty baris lama.
- **AC-PO-22** Baris dapat dihapus hanya bila `Dialokasikan`, `Dalam Produksi`, dan `Terkirim` semuanya nol.
- **AC-PO-23** Setiap revisi tercatat pada audit log (nilai sebelum dan sesudah).
- **AC-PO-24** Bila revisi menurunkan Qty sehingga semua baris menjadi `Terkirim = Qty`, PO menjadi Completed otomatis.

### 4.6 Pembatalan
- **AC-PO-25** Pembatalan wajib alasan dan konfirmasi.
- **AC-PO-26** PO tidak dapat dibatalkan bila memiliki pengiriman (gunakan revisi Qty turun sampai sama dengan Terkirim) atau WO aktif (WO harus dibatalkan atau ditutup terlebih dahulu sesuai FSD Work Order).
- **AC-PO-27** Saat PO dibatalkan, seluruh alokasi aktif otomatis dilepas dan stok kembali tersedia.
- **AC-PO-28** PO Cancelled tidak dapat diubah atau diaktifkan kembali. Tidak ada penghapusan PO.

### 4.7 Daftar dan Detail
- **AC-PO-29** Daftar PO: Kode Internal, Nomor PO Customer (kosong ditandai "Belum ada"), Customer, Tanggal PO, Status, ringkasan progres (total Qty, Dialokasikan, Dalam Produksi, Terkirim), dan indikator Terlambat.
- **AC-PO-30** Filter: status, customer, rentang tanggal, "PO Customer belum ada", "Terlambat". Pencarian: kode internal, nomor PO customer, nama customer.
- **AC-PO-31** Baris dianggap **Terlambat** bila Tanggal Kirim sudah lewat dan `Terkirim < Qty` pada PO yang tidak Cancelled.
- **AC-PO-32** Halaman detail menampilkan header, tabel baris dengan seluruh metrik pemenuhan, serta tab Alokasi, Work Order terkait, Pengiriman, dan Riwayat/Audit.
- **AC-PO-33** Tombol dan menu mengikuti permission, dan penegakan di server bersifat final.

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| CustomerPO | View, Create, Edit, Confirm, Cancel |
| CustomerPO.Alokasi | View, Allocate, Release |

Usulan awal Role: **Comfi** penuh. **Konveksi** hanya `CustomerPO.View` (untuk melihat konteks WO), ditentukan Administrator.

---

## 6. Data Dictionary (Konsep Data)

### PO (Header)
| Atribut | Keterangan |
|---|---|
| POId, KodePO | Identitas dan kode internal otomatis |
| CustomerId | Wajib |
| NomorPOCustomer | Opsional, unik per Customer (di antara PO tidak dibatalkan) |
| TanggalPO | Wajib |
| Status | Draft, Confirmed, In Progress, Completed, Cancelled |
| Keterangan | Opsional |
| AlasanPembatalan | Wajib bila Cancelled |
| CreatedAt/By, UpdatedAt/By, ConfirmedAt/By | Jejak |

### PO Line
| Atribut | Keterangan |
|---|---|
| POLineId, POId | Identitas |
| FGId, ColorId, SizeId | Kombinasi kebutuhan |
| Qty | Integer > 0 |
| TanggalKirim | Wajib |
| Keterangan | Opsional |
| Metrik turunan | Terkirim, Dialokasikan, DalamProduksi, SisaRencana (dihitung, bukan input) |

### Alokasi Stok (log, hanya tambah)
| Atribut | Keterangan |
|---|---|
| AllocationEntryId, POLineId | Identitas |
| Tipe | Alokasi, Pelepasan, Konsumsi (akibat pengiriman) |
| Qty | Positif |
| Sumber | Manual, Serah Terima, Pembatalan PO, Pengiriman |
| Alasan | Opsional |
| Pelaku, Waktu | Jejak |

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Daftar PO | **Interactive Server** | Filter, pencarian, dan paging dinamis |
| Formulir PO (header + grid baris per Size) | **Interactive Server** | Grid dinamis, validasi langsung |
| Detail PO (metrik, tab Alokasi/WO/Pengiriman/Riwayat) | **Interactive Server** | Metrik diperbarui setelah alokasi tanpa memuat ulang |
| Dialog Alokasi/Pelepasan stok | **Interactive Server** | Menampilkan ketersediaan stok langsung |
| Dialog Konfirmasi dan Pembatalan | **Interactive Server** | Interaksi modal |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | PO dibuka tanpa Nomor PO Customer dan tidak pernah diisi hingga selesai | Diizinkan. Ditandai "Belum ada" di daftar untuk ditindaklanjuti. |
| E2 | PO internal untuk produksi stok | Customer tetap wajib. Gunakan Customer khusus (misal "STOK INTERNAL") pada Master Data. Nomor PO Customer dikosongkan. |
| E3 | Dua pengguna merevisi PO yang sama bersamaan | Penyimpan kedua ditolak, muat ulang. |
| E4 | Stok fisik berkurang (penyesuaian) di bawah total alokasi | Penyesuaian ditolak oleh modul Stok FG sampai alokasi dilepas. |
| E5 | FG, Color, atau Customer dinonaktifkan setelah PO dibuat | PO yang ada tetap utuh. Tidak dapat dipilih untuk baris baru. |
| E6 | Size dihapus dari FG setelah PO dibuat | Baris PO tetap valid (snapshot). WO tunduk pada pemeriksaan kesiapan FG. |
| E7 | Tanggal Kirim lebih awal dari Tanggal PO | Peringatan, tidak diblok. |
| E8 | Qty sangat besar (salah ketik) | Validasi batas atas (default 1.000.000) dan konfirmasi bila qty melebihi 100.000. |
| E9 | Serah terima melebihi qty WO (kelebihan produksi) | Kelebihan menjadi stok bebas dan tidak teralokasi ke PO. |
| E10 | Serah Terima di-void setelah alokasi otomatis | Alokasi otomatis yang bersangkutan dilepas. Void ditolak bila stok sudah terkirim (aturan rinci di FSD Serah Terima & Stok FG). |
| E11 | WO dibatalkan atau ditutup sebelum selesai | `Dalam Produksi` WO tersebut menjadi nol dan `Sisa Rencana` bertambah (aturan rinci di FSD Work Order). |
| E12 | Nomor PO Customer duplikat pada customer yang sama | Ditolak (AC-PO-03). Pada customer berbeda diizinkan. |
| E13 | PO sudah Completed tetapi customer menambah pesanan | Dibuat PO baru. PO Completed tidak dibuka kembali. |

---

## 9. Non-Functional Requirements (Ringkas)

- Bahasa UI Indonesia. Format tanggal dan angka lokal Indonesia.
- Perhitungan metrik pemenuhan dilakukan konsisten secara transaksional agar tidak terjadi alokasi ganda pada stok yang sama.
- Seluruh pemeriksaan permission dilakukan di sisi server.
- Seluruh perubahan penting tercatat di audit log.

---

## 10. Catatan untuk Peran dan FSD Berikutnya

- **Work Order:** 1 WO = 1 PO. Qty WO per baris dibatasi `Sisa Rencana`. WO wajib menyimpan rujukan ke baris PO. Perlu diputuskan apakah 1 WO = 1 FG.
- **Serah Terima & Stok FG:** menjalankan aturan alokasi otomatis (bagian 2.2) dan penolakan penyesuaian di bawah total alokasi.
- **Pengiriman:** hanya dari `Dialokasikan` pada baris PO, dan menghasilkan entri Konsumsi.
- **Architect:** rancang agar alokasi dan stok konsisten pada konkurensi. Metrik pemenuhan dapat berupa hasil hitung atau nilai tersimpan, keputusan ada pada Architect.

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Format Kode PO `PO-YYYYMM-NNNN` | **Disetujui** |
| Q2 | Customer tetap wajib pada PO internal (pakai Customer "STOK INTERNAL") | **Disetujui** |
| Q3 | PO Completed terkunci, penambahan pesanan lewat PO baru | **Disetujui** |
| Q4 | Serah terima otomatis mengalokasikan ke baris PO asal WO (bagian 2.2) | **Disetujui** |
| Q5 | Pengiriman hanya dari qty yang sudah dialokasikan ke baris PO | **Disetujui** |
| Q6 | Qty pengiriman tidak boleh melebihi Qty baris PO | Default (belum dijawab user), berlaku sampai ada keberatan |
| Q7 | Ambang qty besar: batas 1.000.000, konfirmasi di atas 100.000 | Default (belum dijawab user), berlaku sampai ada keberatan |
