# FSD — Serah Terima & Stok FG

| Atribut | Nilai |
|---|---|
| Modul | Serah Terima & Stok FG |
| Versi | 0.1 (Draft, menunggu persetujuan) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (FG, Color, Size, Material), Customer PO, Work Order, Production Output |
| Berinteraksi dengan | Customer PO (alokasi), Pengiriman (pengurangan stok) |

---

## 1. Business Objective

Modul ini menghubungkan produksi Konveksi dengan inventori Comfi:

1. **Serah Terima FG (Konveksi → Comfi):** barang jadi hasil WO diserahkan secara bertahap. Stok FG Comfi bertambah **saat serah terima dicatat** (satu langkah). Kelebihan produksi otomatis menjadi stok bebas yang dapat dipakai PO berikutnya.
2. **Stok FG:** saldo barang jadi per **FG × Color × Size** di satu gudang Comfi, dengan kartu stok, alokasi (reservasi) untuk PO, dan penyesuaian manual.
3. **Serah Terima Material (Comfi → Konveksi):** catatan sederhana material yang diserahkan untuk suatu WO, **tanpa stok material**.

### Keputusan Cakupan
- Satu gudang (lokasi tunggal).
- Serah terima FG **satu langkah**: dicatat oleh Admin Produksi (Konveksi), stok langsung bertambah tanpa konfirmasi Comfi.
- Serah terima material dicatat oleh **Admin Produksi (Konveksi)** yang diberi hak akses, sebagai penerima.
- Koreksi stok hanya melalui **penyesuaian manual dengan alasan wajib**. Tidak ada stock opname.
- Stok awal (saldo awal) dimasukkan melalui penyesuaian atau import CSV.

### Di Luar Scope
- Stok material, pembelian, dan BOM.
- Banyak gudang, lokasi rak, dan transfer antar gudang.
- Stock opname (hitung fisik dan bandingkan).
- Konfirmasi penerimaan oleh Comfi (dua langkah).
- Cetak/PDF dokumen serah terima.

---

## 2. Konsep Kunci

### 2.1 Stok FG
| Istilah | Arti |
|---|---|
| **Stok Fisik** | Saldo ledger per FG × Color × Size (selalu ≥ 0) |
| **Teralokasi** | Total alokasi aktif seluruh PO untuk kombinasi tersebut (lihat FSD Customer PO) |
| **Stok Tersedia** | `Stok Fisik − Teralokasi` (selalu ≥ 0) |

Ledger stok bersifat **append-only**. Setiap perubahan saldo berupa entri bertanda (+/−) dengan sumber dokumen.

| Jenis Entri | Arah | Sumber |
|---|---|---|
| Penerimaan Serah Terima | + | Serah Terima FG |
| Pembalikan Serah Terima | − | Void/Koreksi Serah Terima FG |
| Pengiriman | − | Pengiriman ke Customer |
| Pembalikan Pengiriman | + | Void Pengiriman |
| Penyesuaian | ± | Penyesuaian manual (alasan wajib) |
| Saldo Awal | + | Penyesuaian jenis Saldo Awal atau Import CSV |

### 2.2 Batas Serah Terima FG
Per Color × Size pada sebuah WO:
`Qty serah terima ≤ Output kumulatif proses terakhir − Total yang sudah diserahterimakan (aktif)`

Proses terakhir bertipe Internal maupun Vendor (bila vendor, yang dihitung adalah qty terima dari vendor).

### 2.3 Alokasi Otomatis ke PO
Saat Serah Terima FG dicatat (sesuai FSD Customer PO 2.2):
- `Teralokasi otomatis = min(qty serah terima, max(Qty WO − total diserahterimakan sebelumnya, 0))` per Color × Size, ke baris PO asal WO.
- Sisanya (kelebihan produksi) menjadi **stok bebas** (tidak teralokasi).

Pembalikan: saat Serah Terima di-void, alokasi otomatis yang berasal dari dokumen itu dilepas lebih dulu (selama masih aktif), kemudian stok dikurangi.

### 2.4 Status WO yang Diizinkan

| Status WO | Serah Terima FG | Serah Terima Material |
|---|---|---|
| Draft | Tidak | Ya |
| Released | Tidak (belum ada output) | Ya |
| In Progress | Ya | Ya |
| Completed | Ya | Ya |
| Closed | Tidak (Reopen dulu) | Tidak |
| Cancelled | Tidak | Tidak |

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-ST-01 | Admin Produksi | mencatat serah terima FG per WO secara bertahap dengan qty per Color × Size | Comfi menerima barang jadi sesuai hasil produksi |
| US-ST-02 | Admin Produksi | melihat batas qty yang dapat diserahterimakan | tidak melebihi output proses terakhir |
| US-ST-03 | Admin Produksi | membatalkan atau mengoreksi serah terima yang salah | data stok akurat |
| US-ST-04 | Staf Comfi | melihat stok fisik, teralokasi, dan tersedia per FG × Color × Size | tahu apa yang dapat dipakai untuk PO |
| US-ST-05 | Staf Comfi | melihat kartu stok (riwayat mutasi dengan saldo berjalan) | dapat menelusuri asal-usul stok |
| US-ST-06 | Staf Comfi | membuat penyesuaian stok dengan alasan | selisih fisik dapat dikoreksi dan terjejak |
| US-ST-07 | Staf Comfi | memasukkan stok awal lewat import CSV | go-live tidak perlu input satu per satu |
| US-ST-08 | Admin Produksi | mencatat material yang diterima dari Comfi untuk suatu WO | ada catatan material yang diserahkan |
| US-ST-09 | Staf Comfi | melihat kelebihan produksi yang masuk sebagai stok bebas | dapat dipakai pada PO berikutnya |

---

## 4. Acceptance Criteria

### 4.1 Serah Terima FG
- **AC-ST-01** Pengguna dengan `SerahTerimaFG.Create` memilih WO (status In Progress atau Completed). Grid Color × Size menampilkan qty yang **dapat diserahterimakan** per sel (aturan 2.2). Input melebihi batas ditolak.
- **AC-ST-02** Header berisi Tanggal (wajib, tidak di masa depan), Nomor Dokumen (opsional), Diterima Oleh (teks bebas, opsional), dan Catatan (opsional). Pembuat tercatat otomatis.
- **AC-ST-03** Serah terima boleh dilakukan **bertahap** (beberapa dokumen per WO). Satu dokumen harus memiliki minimal satu sel qty > 0 (bilangan bulat).
- **AC-ST-04** Saat disimpan, stok FG bertambah **seketika** per Color × Size dan alokasi otomatis ke baris PO asal dibuat sesuai 2.3. Entri alokasi bersumber "Serah Terima" dan terlihat pada tab Alokasi PO.
- **AC-ST-05** Kelebihan produksi (qty di atas sisa Qty WO) menjadi stok bebas dan ditampilkan pada ringkasan penyimpanan: "x pcs teralokasi ke PO, y pcs menjadi stok bebas".
- **AC-ST-06** Penyimpanan tidak mengubah status WO.

### 4.2 Void dan Koreksi Serah Terima FG
- **AC-ST-07** Void (permission `SerahTerimaFG.Void`) wajib alasan. Entri stok pembalik dibuat, dokumen berstatus Voided, dan tetap terlihat di riwayat.
- **AC-ST-08** Saat Void, sistem melepas alokasi otomatis yang berasal dari dokumen itu (bila masih aktif), lalu memeriksa `Stok Tersedia ≥ 0` pada kondisi akhir untuk setiap sel. Bila gagal (stok sudah dialokasikan ke PO lain atau sudah dikirim), void ditolak dengan pesan penahan, misalnya "Stok sudah dikirim ke customer, batalkan pengiriman terlebih dahulu".
- **AC-ST-09** **Koreksi** (permission `SerahTerimaFG.Correct`) = void dokumen lama + dokumen baru dalam satu transaksi atomik. Pemeriksaan hanya pada kondisi akhir. Alasan wajib.
- **AC-ST-10** Void/Koreksi ditolak bila WO berstatus Closed atau Cancelled.
- **AC-ST-11** Dokumen yang sudah Voided tidak dapat diaktifkan kembali.

### 4.3 Stok FG
- **AC-ST-12** Halaman Stok FG menampilkan per FG × Color × Size: Stok Fisik, Teralokasi, Stok Tersedia. Filter: FG, Color, Size, Customer (dari FG), "hanya yang ada stok". Pencarian: kode/nama FG.
- **AC-ST-13** Klik pada baris stok membuka **Kartu Stok**: seluruh entri ledger (tanggal, jenis, dokumen sumber, qty ±, saldo berjalan) dengan filter rentang tanggal.
- **AC-ST-14** Stok Fisik dan Stok Tersedia tidak pernah negatif. Operasi yang akan membuatnya negatif ditolak.
- **AC-ST-15** Ringkasan menampilkan total stok bebas kelebihan produksi per FG sebagai petunjuk bagi Comfi saat menerima PO baru.

### 4.4 Penyesuaian Stok
- **AC-ST-16** Pengguna dengan `StokFG.Adjust` membuat penyesuaian: FG, Color, Size, qty (positif atau negatif), tanggal, alasan wajib.
- **AC-ST-17** Penyesuaian ditolak bila membuat `Stok Tersedia < 0`. Pesan menyebutkan bahwa alokasi harus dilepas terlebih dahulu.
- **AC-ST-18** Penyesuaian tidak dapat diubah atau dihapus. Kesalahan diperbaiki dengan penyesuaian lawan.
- **AC-ST-19** Setiap penyesuaian tercatat di audit log.

### 4.5 Stok Awal (Import CSV)
- **AC-ST-20** Pengguna dengan `StokFG.Import` mengunggah CSV berisi: Kode FG, Kode Color, Kode Size, Qty (> 0), Tanggal (opsional, default hari ini). Template dapat diunduh.
- **AC-ST-21** Alur sama dengan import Master Data (AC-MD-30 s.d. 35): validasi seluruh baris, semua-atau-tidak-sama-sekali, batas 2.000 baris dan 2 MB.
- **AC-ST-22** FG, Color, dan Size pada file harus ada, aktif, dan Size harus termasuk Size Berlaku FG. Hasil impor berupa entri jenis **Saldo Awal** dengan alasan otomatis "Saldo awal (import)".

### 4.6 Serah Terima Material
- **AC-ST-23** Pengguna dengan `SerahTerimaMaterial.Create` membuat dokumen untuk WO (status Draft, Released, In Progress, atau Completed). Header: Tanggal (wajib, tidak di masa depan), Nomor Dokumen (opsional), Catatan (opsional).
- **AC-ST-24** Baris: Material (aktif), Qty (bilangan desimal > 0, maksimal 3 digit di belakang koma), Keterangan (opsional). Satuan ditampilkan otomatis dari master Material.
- **AC-ST-25** Dokumen tidak memengaruhi stok apa pun dan tidak dibandingkan dengan BOM. Ini hanya catatan.
- **AC-ST-26** Dokumen dapat di-void dengan alasan wajib (permission `SerahTerimaMaterial.Void`). Void tidak memiliki syarat selain WO belum Closed atau Cancelled.
- **AC-ST-27** Detail WO menampilkan tab Material berisi dokumen serah terima material dan total qty per Material.

### 4.7 Tampilan dan Keamanan
- **AC-ST-28** Daftar Serah Terima FG: tanggal, WO, PO, FG, ringkasan qty, status, pembuat. Filter: WO, PO, FG, tanggal, status.
- **AC-ST-29** Detail WO menampilkan total diserahterimakan per Color × Size dan sisa belum diserahterimakan (output proses terakhir − diserahterimakan).
- **AC-ST-30** Tombol dan menu mengikuti permission, penegakan di server bersifat final. Seluruh aksi tercatat di audit log.
- **AC-ST-31** Perhitungan batas serah terima dan saldo stok konsisten terhadap konkurensi. Dua pengguna yang bersamaan tidak dapat sama-sama lolos bila melanggar batas atau menyebabkan stok negatif.

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| SerahTerimaFG | View, Create, Void, Correct |
| SerahTerimaMaterial | View, Create, Void |
| StokFG | View, Adjust, Import |

Usulan awal Role:
- **Admin Produksi (Konveksi):** SerahTerimaFG penuh, SerahTerimaMaterial penuh, StokFG.View (opsional).
- **Comfi:** SerahTerimaFG.View, SerahTerimaMaterial.View, StokFG penuh.

---

## 6. Data Dictionary (Konsep Data)

### Serah Terima FG (Header)
| Atribut | Keterangan |
|---|---|
| HandoverFGId | Identitas |
| WOId | Wajib |
| Tanggal | Wajib, tidak di masa depan |
| NomorDokumen, DiterimaOleh, Catatan | Opsional |
| Status | Active, Voided |
| ReplacesId | Rujukan dokumen yang digantikan (hasil Koreksi) |
| Alasan, VoidedAt/By, CreatedAt/By | Jejak |

### Serah Terima FG Line
HandoverFGId, WOLineId (Color × Size), Qty (integer > 0).

### Entri Ledger Stok FG
| Atribut | Keterangan |
|---|---|
| StockEntryId | Identitas |
| FGId, ColorId, SizeId | Kunci stok |
| Jenis | Penerimaan, Pembalikan Serah Terima, Pengiriman, Pembalikan Pengiriman, Penyesuaian, Saldo Awal |
| Qty | Bertanda (+/−) |
| SourceType, SourceId | Dokumen sumber |
| Tanggal, Alasan, Pelaku, Waktu | Jejak |

### Serah Terima Material (Header dan Line)
Header: HandoverMaterialId, WOId, Tanggal, NomorDokumen, Catatan, Status (Active/Voided), Alasan Void, jejak.
Line: Material, Qty (desimal, 3 digit), Keterangan.

> Stok Fisik, Teralokasi, dan Tersedia adalah hasil hitung (ledger dan alokasi aktif), bukan input.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Input Serah Terima FG (grid dengan batas tersedia) | **Interactive Server** | Validasi langsung terhadap batas |
| Daftar dan detail Serah Terima FG | **Interactive Server** | Filter, paging, void/koreksi dengan pratinjau dampak |
| Halaman Stok FG dan Kartu Stok | **Interactive Server** | Filter dinamis, saldo berjalan |
| Dialog Penyesuaian dan Import CSV | **Interactive Server** | Pratinjau hasil validasi per baris |
| Serah Terima Material (form, daftar) | **Interactive Server** | Form dengan baris dinamis |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Void serah terima yang stoknya sudah dialokasikan ke PO lain | Ditolak bila `Stok Tersedia` menjadi negatif. Pesan: lepas alokasi lebih dulu. |
| E2 | Void serah terima yang stoknya sudah dikirim | Ditolak. Pesan: batalkan pengiriman terkait dulu. |
| E3 | Serah terima melebihi Qty WO (kelebihan produksi) | Diizinkan selama ≤ output proses terakhir. Kelebihan menjadi stok bebas. |
| E4 | Alokasi otomatis tidak dapat dilakukan karena baris PO sudah dibatalkan atau Completed | Seluruh qty menjadi stok bebas. Dicatat pada ringkasan. |
| E5 | Void output proses terakhir yang sudah diserahterimakan | Modul Production Output menolak bila output menjadi < total diserahterimakan (bagian 2.2 berlaku sebagai invariant). |
| E6 | WO Closed sementara ada output yang belum diserahterimakan | Peringatan di WO. Serah terima dibuka kembali lewat Reopen WO. |
| E7 | Dua pengguna menyerahterimakan sel yang sama bersamaan | Hanya yang pertama lolos jika melebihi batas (AC-ST-31). |
| E8 | FG, Color, atau Size dinonaktifkan setelah ada stok | Stok tetap tampil dan dapat dialokasikan/dikirim. Tidak dapat menerima Saldo Awal baru lewat import. |
| E9 | Penyesuaian dilakukan pada kombinasi tanpa riwayat | Diizinkan bila FG, Color, dan Size valid. Entri pertama berupa Penyesuaian. |
| E10 | Import saldo awal dilakukan dua kali dengan file yang sama | Stok bertambah dua kali (import bersifat menambah). Pratinjau menampilkan peringatan bila sudah ada saldo awal untuk kombinasi tersebut. |
| E11 | Koneksi Blazor terputus saat mengisi grid | Input belum tersimpan hilang. Peringatan sebelum meninggalkan halaman. |
| E12 | Serah terima material untuk WO Draft lalu WO dibatalkan | Dokumen tetap sebagai catatan historis. Void dilarang pada WO Cancelled. |
| E13 | Qty material memiliki lebih dari 3 digit desimal | Ditolak (tidak dibulatkan diam-diam). |
| E14 | Serah terima dicatat dengan tanggal lebih awal dari tanggal output pertama WO | Diizinkan dengan peringatan. |

---

## 9. Non-Functional Requirements (Ringkas)

- Bahasa UI Indonesia, tanggal dan angka lokal.
- Saldo stok tidak boleh negatif dan alokasi tidak boleh ganda. Pembaruan ledger dan alokasi dilakukan dalam satu transaksi.
- Kartu stok tetap responsif untuk puluhan ribu entri (paging di server).
- Seluruh pemeriksaan permission di server. Seluruh perubahan penting tercatat di audit log.

---

## 10. Catatan untuk Peran dan FSD Berikutnya

- **Pengiriman:** mengurangi stok dengan entri Pengiriman dan mengonsumsi alokasi PO (entri Konsumsi). Void pengiriman membuat pembalik di ledger dan mengembalikan alokasi.
- **Production Output:** invariant tambahan: `output proses terakhir ≥ total diserahterimakan`.
- **Customer PO:** metrik `Dalam Produksi` = Qty WO aktif − total diserahterimakan (sudah tercermin di FSD PO).
- **Architect:** rancang agar stok fisik/teralokasi konsisten pada konkurensi (kunci per kombinasi FG × Color × Size atau tabel saldo yang diperbarui bersama ledger).

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Serah terima FG satu langkah, dicatat Admin Produksi | **Disetujui** |
| Q2 | Serah terima material dicatat Admin Produksi (hak akses), tanpa stok | **Disetujui** |
| Q3 | Satu gudang, penyesuaian manual dengan alasan, tanpa stock opname | **Disetujui** |
| Q4 | Stok awal lewat penyesuaian atau import CSV | **Disetujui** |
| Q5 | Field "Diterima Oleh" berupa teks bebas opsional | Default SA, perlu konfirmasi |
| Q6 | Qty material berupa desimal 3 digit (karena satuan meter/kg) | Default SA, perlu konfirmasi |
| Q7 | Serah terima material boleh sejak WO Draft hingga Completed | Default SA, perlu konfirmasi |
| Q8 | Serah terima FG ditolak saat WO Closed (harus Reopen) | Default SA, perlu konfirmasi |
