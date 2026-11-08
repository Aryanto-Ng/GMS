# FSD — Work Order

| Atribut | Nilai |
|---|---|
| Modul | Work Order (WO) |
| Versi | 1.0 (Disetujui user) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (FG, Routing, Tarif, Process, Size, Color), Customer PO |
| Berinteraksi dengan | Production Output, Serah Terima & Stok FG, Payroll Borongan |

---

## 1. Business Objective

Work Order adalah **kontrak kerja** antara Comfi dan Konveksi. Comfi membuka WO berdasarkan PO customer, lalu Konveksi mengerjakan dan melaporkan output per proses. WO:

- menetapkan **apa** yang diproduksi (FG, Color, Size, Qty) dan **bagaimana** (Routing),
- membekukan (snapshot) Routing dan Tarif saat dirilis sehingga perubahan master data tidak mengubah WO yang berjalan,
- menjadi dasar input output harian, pemantauan progres per proses, serah terima FG, dan upah borongan.

### Keputusan Cakupan
- **1 WO = 1 PO** dan **1 WO = 1 FG** (beberapa Color dan Size dari baris PO untuk FG tersebut). PO dengan 2 FG membutuhkan 2 WO.
- Tidak ada WO tanpa PO. Produksi stok memakai PO internal (lihat FSD Customer PO).
- Vendor untuk proses vendor **tidak dipilih di WO**, tetapi saat pengiriman ke vendor dicatat (lihat FSD Production Output).

### Di Luar Scope
- Cetak/PDF dokumen WO.
- Penambahan baris Color/Size setelah WO dirilis (buat WO baru).
- Penjadwalan kapasitas line.
- Lampiran file pada WO.

---

## 2. Siklus Status

```
Draft ──► Released ──► In Progress ──► Completed ──► Closed
  │          │             ▲               │           │
  │          │             └───────────────┘           │
  │          │           (Konveksi: buka kembali)      │
  │          │                      ▲──────────────────┘
  │          │                  (Comfi: Reopen)
  └──────────┴──► Cancelled   (hanya bila belum ada aktivitas)
```

| Dari → Ke | Siapa | Syarat | Catatan |
|---|---|---|---|
| (baru) → Draft | Comfi | PO Confirmed atau In Progress | |
| Draft → Released | Comfi | Lihat 4.3 | Snapshot Routing dan Tarif dibuat |
| Released → In Progress | **Otomatis** | Output pertama dari WO dicatat (termasuk penerimaan dari vendor) | |
| In Progress → Completed | Konveksi | Minimal ada 1 output | Boleh walau Qty belum tercapai. Input output baru diblok. |
| Completed → In Progress | Konveksi | Alasan wajib | Sampai WO ditutup |
| Completed → Closed | Comfi | Lihat 4.6 | |
| Closed → Completed | Comfi (Reopen) | Alasan wajib | Satu-satunya jalan untuk koreksi setelah Closed |
| Draft atau Released → Cancelled | Comfi | Belum ada output, pengiriman vendor, maupun serah terima. Alasan wajib. | |

Hanya satu status aktif pada satu waktu dan semua perpindahan tercatat di riwayat status (siapa, kapan, alasan).

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-WO-01 | Staf Comfi | membuat WO dari PO dengan memilih FG dan mengisi Qty per Color × Size | produksi dapat direncanakan sesuai kebutuhan PO |
| US-WO-02 | Staf Comfi | merilis WO setelah memastikan FG siap produksi | Konveksi dapat mulai bekerja dengan routing dan tarif yang pasti |
| US-WO-03 | Staf Comfi | mengubah Qty WO yang sudah berjalan secara terbatas | perubahan pesanan dapat diakomodasi |
| US-WO-04 | Staf Comfi | membatalkan WO yang belum dikerjakan | WO yang salah tidak membebani rencana PO |
| US-WO-05 | Staf Konveksi | melihat WO yang dirilis beserta routing dan qty-nya | tahu apa yang harus dikerjakan |
| US-WO-06 | Staf Konveksi/Comfi | melihat progres WO per proses dan per Color × Size | memantau sampai proses mana WO berjalan |
| US-WO-07 | Staf Konveksi | menandai WO selesai produksi | menandakan tidak ada output lagi |
| US-WO-08 | Staf Comfi | menutup WO setelah serah terima | sisa kebutuhan kembali tersedia untuk rencana lain |
| US-WO-09 | Staf Comfi | membuka kembali WO yang sudah ditutup dengan alasan | koreksi data tetap dimungkinkan dan terjejak |
| US-WO-10 | Admin Gaji | melihat tarif snapshot pada WO | perhitungan upah merujuk tarif yang berlaku saat WO dirilis |

---

## 4. Acceptance Criteria

### 4.1 Pembuatan WO
- **AC-WO-01** Pembuatan WO diawali dengan memilih **PO** (status Confirmed atau In Progress), lalu memilih **FG** di antara FG yang ada pada baris PO tersebut.
- **AC-WO-02** Sistem membuat Kode WO otomatis (format `WO-YYYYMM-NNNN`, aman dari duplikasi saat dibuat bersamaan).
- **AC-WO-03** Layar menampilkan grid Color × Size berdasarkan baris PO untuk FG tersebut, beserta `Sisa Rencana` tiap sel. Pengguna mengisi Qty WO (bilangan bulat ≥ 0). Minimal satu sel Qty > 0.
- **AC-WO-04** Qty WO per sel tidak boleh melebihi `Sisa Rencana` baris PO terkait. WO berstatus Draft, Released, In Progress, dan Completed ikut dihitung sebagai "Dalam Produksi"; WO Closed dan Cancelled tidak.
- **AC-WO-05** Header WO berisi: Tanggal Mulai (rencana), Target Selesai, dan Keterangan (semua opsional). Target Selesai tidak boleh sebelum Tanggal Mulai.
- **AC-WO-06** WO Draft dapat diubah bebas (Qty, tanggal, keterangan) dan dapat dibatalkan.
- **AC-WO-07** FG dalam pembuatan WO tidak wajib berstatus Siap Produksi saat Draft. Statusnya diperiksa saat dirilis.

### 4.2 Perilaku Pencegahan
- **AC-WO-08** Dua pengguna yang membuat WO untuk baris PO yang sama secara bersamaan: bila total melebihi `Sisa Rencana`, yang kedua ditolak dengan pesan sisa terbaru.

### 4.3 Rilis WO
- **AC-WO-09** Rilis hanya dapat dilakukan Comfi dengan permission `WorkOrder.Release` dan bila terpenuhi:
  1. PO masih Confirmed atau In Progress.
  2. FG berstatus **Siap Produksi** (aturan Master Data 4.11). Bila tidak, sistem menampilkan alasan spesifik.
  3. Minimal satu baris Qty > 0.
  4. Qty tiap sel masih ≤ `Sisa Rencana` pada saat rilis (dihitung tanpa WO ini sendiri).
- **AC-WO-10** Saat rilis, sistem membuat **snapshot** yang tidak dapat diubah:
  - **Routing:** urutan langkah, kode, nama, dan tipe Process (Internal/Vendor).
  - **Tarif:** nilai per langkah Internal × Size (presisi 5 digit desimal).
  - Label FG, Color, dan Size pada saat rilis.
- **AC-WO-11** Setelah Released, perubahan Routing atau Tarif pada Master Data **tidak** memengaruhi WO ini (R7 Master Data).
- **AC-WO-12** Snapshot hanya dapat dilihat sesuai permission. Tarif hanya terlihat oleh pengguna dengan `WorkOrder.Tarif.View`.

### 4.4 Perubahan Setelah Dirilis
- **AC-WO-13** Pada status Released dan In Progress, Comfi dapat mengubah **Qty** per sel, Tanggal Mulai, Target Selesai, dan Keterangan.
- **AC-WO-14** Baris Color × Size **tidak dapat ditambah atau dihapus** setelah Released. Gunakan WO baru.
- **AC-WO-15** Qty baru tidak boleh lebih kecil dari **output kumulatif proses pertama** untuk sel tersebut. Kenaikan Qty tunduk pada `Sisa Rencana` PO.
- **AC-WO-16** Perubahan Qty tidak mengubah Routing maupun Tarif snapshot. Semua perubahan tercatat di audit log (nilai sebelum dan sesudah).

### 4.5 Konveksi: Completed
- **AC-WO-17** Konveksi (permission `WorkOrder.Complete`) dapat menandai WO In Progress sebagai Completed meski Qty belum tercapai. Sistem menampilkan ringkasan progres (per proses) dan meminta konfirmasi.
- **AC-WO-18** Pada status Completed, input output, kirim, dan terima vendor ditolak. Serah terima FG tetap diperbolehkan.
- **AC-WO-19** Konveksi dapat mengembalikan WO dari Completed ke In Progress dengan alasan wajib, selama WO belum Closed.

### 4.6 Comfi: Closed dan Reopen
- **AC-WO-20** Comfi (permission `WorkOrder.Close`) dapat menutup WO yang Completed. Bila ada output proses terakhir yang belum diserahterimakan, sistem menampilkan **peringatan** (jumlah per Color × Size), tidak memblokir.
- **AC-WO-21** Saat Closed: "Dalam Produksi" WO menjadi nol dan `Sisa Rencana` baris PO bertambah sebesar sisa. Input output dan serah terima FG ditolak.
- **AC-WO-22** Comfi (permission `WorkOrder.Reopen`) dapat mengembalikan WO dari Closed ke Completed dengan alasan wajib. Reopen hanya berlaku bila Qty WO yang dihidupkan kembali masih sesuai `Sisa Rencana` PO. Bila tidak, sistem menolak dan menjelaskan.
- **AC-WO-23** Koreksi output setelah Closed hanya dapat dilakukan setelah Reopen. Koreksi di periode gaji yang telah ditutup tunduk pada aturan penyesuaian di FSD Payroll.

### 4.7 Pembatalan
- **AC-WO-24** Cancel hanya untuk Draft atau Released **tanpa aktivitas** (tanpa output, pengiriman vendor, maupun serah terima). Alasan wajib.
- **AC-WO-25** WO Cancelled tidak dihitung dalam "Dalam Produksi" dan tidak dapat diaktifkan kembali.

### 4.8 Tampilan Daftar dan Progres
- **AC-WO-26** Daftar WO: Kode WO, PO, Customer, FG, Status, Tanggal Mulai, Target Selesai, ringkasan Qty dan progres, indikator **Terlambat** (Target Selesai lewat dan status belum Completed/Closed/Cancelled).
- **AC-WO-27** Filter: status, PO, customer, FG, rentang tanggal, Terlambat. Pencarian: kode WO, kode PO, nomor PO customer.
- **AC-WO-28** Detail WO menampilkan header, **matriks progres**: baris = langkah routing (urut), kolom = Color × Size. Sel berisi output kumulatif terhadap Qty WO. Ada juga total per langkah dengan persentase dan penanda: Belum Mulai, Berjalan, Selesai (output ≥ Qty).
- **AC-WO-29** Untuk langkah bertipe Vendor, matriks menampilkan: Terkirim ke vendor, Diterima dari vendor, dan Sisa di vendor.
- **AC-WO-30** **Progres WO** = output kumulatif proses terakhir ÷ Qty WO. Nilai di atas 100% ditampilkan apa adanya (kelebihan produksi).
- **AC-WO-31** Detail WO juga menampilkan: total diserahterimakan, riwayat status, dan tab Serah Terima, Output, dan Riwayat/Audit.
- **AC-WO-32** Tombol dan menu mengikuti permission, dan penegakan di server bersifat final.

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| WorkOrder | View, Create, Edit, Release, Cancel, Complete, Close, Reopen |
| WorkOrder.Tarif | View |

Usulan awal Role:
- **Comfi:** View, Create, Edit, Release, Cancel, Close, Reopen, dan Tarif.View.
- **Admin Produksi (Konveksi):** View, Complete. **Tanpa** Tarif.View.
- **Admin Gaji (Konveksi):** View, Tarif.View.

---

## 6. Data Dictionary (Konsep Data)

### Work Order (Header)
| Atribut | Keterangan |
|---|---|
| WOId, KodeWO | Identitas dan kode otomatis |
| POId | Wajib, 1 WO = 1 PO |
| FGId | Wajib, 1 WO = 1 FG |
| TanggalMulai, TargetSelesai | Opsional |
| Status | Draft, Released, In Progress, Completed, Closed, Cancelled |
| Keterangan | Opsional |
| ReleasedAt/By, CompletedAt/By, ClosedAt/By | Jejak |
| AlasanPembatalan | Wajib bila Cancelled |

### WO Line
| Atribut | Keterangan |
|---|---|
| WOLineId, WOId | Identitas |
| POLineId | Rujukan ke baris PO (FG × Color × Size). Menentukan Color dan Size. |
| Qty | Integer ≥ 1 |

### WO Routing Snapshot (dibuat saat Released)
| Atribut | Keterangan |
|---|---|
| WOId, Urutan | Urutan langkah |
| ProcessId + salinan Kode, Nama, Tipe | Snapshot |

### WO Tarif Snapshot (dibuat saat Released)
| Atribut | Keterangan |
|---|---|
| WOId, Urutan langkah (Internal), SizeId | Kunci |
| Tarif | Rupiah per unit, presisi 5 digit desimal |

### WO Riwayat Status
Dari, Ke, Alasan, Pelaku, Waktu.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Daftar WO | **Interactive Server** | Filter, pencarian, paging |
| Formulir WO (pilih PO/FG, grid Color × Size, tampilan Sisa Rencana) | **Interactive Server** | Grid dinamis dan validasi langsung terhadap sisa rencana |
| Detail WO + matriks progres | **Interactive Server** | Progres diperbarui saat pengguna bekerja, tab dan dialog aksi |
| Dialog Rilis/Complete/Close/Reopen/Cancel | **Interactive Server** | Konfirmasi dengan alasan dan ringkasan |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Dua pengguna menekan Rilis, Complete, atau Close pada WO yang sama | Yang kedua ditolak: "Status WO telah berubah, muat ulang". |
| E2 | Konveksi menekan Complete tepat saat admin lain menyimpan output | Satu transaksi menang. Output yang tersimpan setelah Completed ditolak dengan pesan jelas. |
| E3 | Tarif atau Routing master diubah antara Draft dan Release | Snapshot diambil saat Release (nilai terbaru). Draft tidak membekukan apa pun. |
| E4 | FG menjadi Belum Siap setelah Draft dibuat | Rilis diblok dengan alasan spesifik. Draft tetap dapat diubah atau dibatalkan. |
| E5 | FG dinonaktifkan atau Size dihapus dari FG setelah Released | Tidak berpengaruh pada WO (snapshot). |
| E6 | PO dikonfirmasi lalu direvisi menurunkan Qty saat WO Draft ada | Revisi PO ditolak karena `Dalam Produksi` menahan (aturan PO). |
| E7 | Langkah pertama routing bertipe Vendor | Pengiriman ke vendor dibatasi oleh Qty WO (bukan output proses sebelumnya). Aturan rinci di FSD Production Output. |
| E8 | Output berjalan melebihi Qty WO (kelebihan produksi) | Diizinkan. Kelebihan menjadi stok bebas saat serah terima. |
| E9 | Reopen WO ditolak karena `Sisa Rencana` PO sudah dipakai WO lain | Pesan menjelaskan selisih. Comfi menyesuaikan WO lain atau membuat PO baru. |
| E10 | WO ditandai Completed padahal ada proses vendor dengan sisa di vendor | Peringatan di ringkasan konfirmasi. Tidak diblok. |
| E11 | Qty WO diubah turun hingga sama dengan output proses pertama | Diizinkan (AC-WO-15). |
| E12 | PO sudah Completed (semua terkirim) tetapi WO Draft atau Released masih ada | Rilis diblok, WO Draft dapat dibatalkan. |
| E13 | Koneksi Blazor terputus saat mengisi grid | Perubahan belum tersimpan hilang. Tampilkan peringatan sebelum meninggalkan halaman. |

---

## 9. Non-Functional Requirements (Ringkas)

- Bahasa UI Indonesia, format tanggal dan angka lokal.
- Pembuatan snapshot dan perubahan status dilakukan atomik dalam satu transaksi.
- Matriks progres tetap responsif untuk WO dengan puluhan kombinasi Color × Size dan sekitar 10 langkah routing.
- Semua pemeriksaan permission dilakukan di sisi server. Tarif tidak boleh bocor melalui API ke pengguna tanpa `WorkOrder.Tarif.View`.
- Seluruh perubahan penting tercatat di audit log.

---

## 10. Catatan untuk Peran dan FSD Berikutnya

- **Production Output:** output hanya dapat diinput untuk WO berstatus Released atau In Progress. Aturan kumulatif ketat antar langkah dan aturan vendor langkah pertama didefinisikan di sana. Output pertama memicu Released → In Progress.
- **Serah Terima & Stok FG:** serah terima FG diperbolehkan pada In Progress dan Completed, ditolak pada Closed.
- **Payroll:** gaji dihitung dari tarif snapshot WO, bukan tarif master.
- **Architect:** snapshot Routing/Tarif dan label disimpan bersama WO agar WO tetap terbaca walau master berubah. Kode WO aman terhadap konkurensi.

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | 1 WO = 1 PO = 1 FG | **Disetujui** |
| Q2 | Format `WO-YYYYMM-NNNN` | **Disetujui** |
| Q3 | Tanggal Mulai, Target Selesai, dan Keterangan | **Disetujui** |
| Q4 | Setelah Released hanya Qty yang dapat berubah | **Disetujui** |
| Q5 | Cancel hanya bila belum ada aktivitas | **Disetujui** |
| Q6 | Completed oleh Konveksi, boleh walau Qty belum tercapai, dapat dibuka kembali | **Disetujui** |
| Q7 | Closed oleh Comfi dengan peringatan (tidak memblokir), koreksi lewat Reopen | **Disetujui** |
| Q8 | Vendor dipilih saat pengiriman, bukan di WO | **Disetujui** |
| Q9 | Tarif tidak terlihat oleh admin produksi | **Disetujui** |
| Q10 | Cetak/PDF WO ditunda | **Disetujui** |
