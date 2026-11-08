# FSD — Production Output

| Atribut | Nilai |
|---|---|
| Modul | Production Output (termasuk Pengiriman/Penerimaan Vendor) |
| Versi | 1.0 (Disetujui user) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management, Master Data (Pekerja, Process, Vendor), Work Order (snapshot Routing/Tarif) |
| Berinteraksi dengan | Work Order (progres), Serah Terima & Stok FG, Payroll Borongan |

---

## 1. Business Objective

Konveksi harus memantau berapa output yang sudah dihasilkan **per WO dan per proses**, dan angka output pekerja menjadi **dasar upah borongan**. Modul ini mencatat output harian oleh **Admin Produksi** (di PC, angka manual) dengan prinsip:

- **Satu ledger append-only.** Data tidak pernah ditimpa. Kesalahan diperbaiki dengan *void* atau *koreksi* yang bersyarat dan tercatat.
- **Urutan proses ketat.** Output proses ke-N tidak boleh melebihi output proses ke-(N−1).
- **Granularitas Level C:** WO + Proses + Pekerja + Tanggal Kerja + Color + Size.
- **Proses vendor (bordir)** dicatat sebagai kirim ke vendor dan terima dari vendor, tanpa pekerja dan tanpa upah.

### Keputusan Cakupan
- Hanya **output lolos** yang dicatat dan dibayar. Reject dan rework tidak dicatat dan tidak memengaruhi upah.
- Kelebihan produksi (di atas Qty WO) **diizinkan**.
- Layar input tidak menampilkan nilai rupiah/tarif.

### Di Luar Scope
- Barcode/QR, bundle ticket, input via mobile.
- Pencatatan reject, rework, atau defect.
- Penjadwalan pekerja ke line.
- Harga jasa vendor (keuangan Comfi).

---

## 2. Konsep Kunci

### 2.1 Jenis Catatan

| Jenis | Untuk | Pekerja | Upah |
|---|---|---|---|
| **Output** | Langkah routing bertipe Internal | Wajib | Ya (qty × tarif snapshot WO per Size) |
| **Kirim ke Vendor** | Langkah bertipe Vendor | – | Tidak |
| **Terima dari Vendor** | Langkah bertipe Vendor | – | Tidak. Qty terima dihitung sebagai **output langkah vendor**. |

### 2.2 Aturan Kumulatif (Invariant)

Untuk setiap kombinasi **Color × Size** dalam satu WO, dengan "Kum(n)" adalah total qty aktif (tidak di-void, termasuk penyesuaian) pada langkah ke-n:

1. **Langkah pertama (Internal):** tidak ada batas atas (kelebihan produksi diizinkan).
2. **Langkah Internal ke-n (n > 1):** `Kum(n) ≤ Output(n−1)`. Bila langkah n−1 bertipe Vendor, `Output(n−1)` adalah total yang **diterima** dari vendor.
3. **Langkah Vendor:**
   - `Terkirim ≤ Output(n−1)`. Bila langkah vendor adalah langkah pertama, `Terkirim ≤ Qty WO`.
   - `Diterima ≤ Terkirim`, juga **per vendor**.
4. Rantai penuh yang harus selalu terpenuhi:
   `Output(n−1) ≥ Terkirim(n) ≥ Diterima(n) = Output(n) ≥ Kum(n+1)`

**Aturan ini dicek pada kondisi akhir setiap transaksi** (input baru, void, koreksi, penyesuaian). Void atau koreksi yang membuat salah satu pertidaksamaan terlanggar ditolak. Pesan menyebutkan langkah dan sel Color × Size yang menahan.

### 2.3 Kapan Boleh Mengubah Data

| Kondisi | Input baru | Void / Koreksi / Penyesuaian |
|---|---|---|
| WO Released atau In Progress | Ya | Ya |
| WO Completed | Tidak | Tidak (buka kembali WO ke In Progress dulu, WO AC-19) |
| WO Closed | Tidak | Tidak (Reopen oleh Comfi dulu, WO AC-22) |
| WO Draft atau Cancelled | Tidak | Tidak |
| Tanggal kerja di **periode gaji yang sudah ditutup** | Tidak (gunakan Penyesuaian) | Void/Koreksi tidak (gunakan Penyesuaian) |

### 2.4 Tanggal Kerja
- Tidak boleh di masa depan.
- Tidak boleh berada pada periode gaji yang sudah **ditutup**. Mundur diperbolehkan selama periode terkait masih terbuka.
- Output masuk ke periode gaji berdasarkan **tanggal kerja** (bukan tanggal input).

### 2.5 Penyesuaian (untuk periode yang sudah ditutup)
Entri khusus yang dipakai bila kesalahan ditemukan pada tanggal kerja yang sudah masuk periode gaji tertutup:
- Mengacu **Tanggal Asal** (wajib, harus berada pada periode tertutup) dan, bila ada, entri asal.
- **Tanggal Kerja penyesuaian** berada pada periode yang masih terbuka, sehingga upahnya masuk periode terbuka.
- Qty per sel boleh **negatif atau positif**. Alasan wajib.
- Upah penyesuaian = qty × **tarif snapshot WO** (identik dengan entri asal).
- Boleh diberikan untuk pekerja nonaktif.
- Tunduk pada aturan kumulatif.

---

## 3. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-PR-01 | Admin Produksi | menginput output beberapa pekerja sekaligus untuk satu WO, proses, dan tanggal | input harian cepat |
| US-PR-02 | Admin Produksi | melihat batas maksimal yang boleh diinput per sel | tidak salah input melebihi proses sebelumnya |
| US-PR-03 | Admin Produksi | membatalkan (void) entri yang salah dengan alasan | data keliru tidak ikut dihitung |
| US-PR-04 | Admin Produksi | memperbaiki entri dengan satu aksi Koreksi | tidak terhalang aturan kumulatif saat void dan input ulang terpisah |
| US-PR-05 | Admin Produksi | menginput penyesuaian untuk periode gaji yang sudah ditutup | kesalahan lama dapat diperbaiki tanpa mengubah periode tertutup |
| US-PR-06 | Admin Produksi | mencatat pengiriman barang ke vendor dan penerimaannya, termasuk penerimaan sebagian | sisa di vendor selalu terlihat |
| US-PR-07 | Admin Produksi/Comfi | melihat riwayat seluruh entri (termasuk void) dengan filter | audit dan penelusuran mudah |
| US-PR-08 | Admin Produksi/Comfi | melihat progres kumulatif WO per proses | memantau pekerjaan |
| US-PR-09 | Admin Gaji | memperoleh data output per pekerja, proses, dan tanggal kerja yang andal | gaji dapat dihitung |

---

## 4. Acceptance Criteria

### 4.1 Input Output (Internal)
- **AC-PR-01** Layar input: pengguna memilih **WO** (Released atau In Progress), **Proses** (langkah Internal pada routing snapshot WO), dan **Tanggal Kerja**. Lalu menambahkan baris **Pekerja** (pekerja aktif, dengan pencarian kode/nama). Kolom grid berisi kombinasi Color × Size milik WO.
- **AC-PR-01a** Satu penyimpanan dapat berisi banyak baris pekerja. Sistem membuat **satu entri per pekerja** dalam satu transaksi. Bila ada baris yang melanggar aturan, **seluruh penyimpanan ditolak** dan baris bermasalah ditandai.
- **AC-PR-02** Satu pekerja boleh memiliki **beberapa entri** pada proses dan hari yang sama (dari penyimpanan berbeda). Dalam satu penyimpanan, satu pekerja tidak boleh muncul dua kali.
- **AC-PR-03** Qty per sel adalah bilangan bulat ≥ 0. Baris dengan seluruh sel nol atau kosong tidak disimpan. Penyimpanan tanpa baris bernilai ditolak.
- **AC-PR-04** Tiap sel menampilkan **batas yang tersedia**: `Output proses sebelumnya − Kum proses ini` (untuk langkah pertama ditampilkan "tanpa batas" dan sisa terhadap Qty WO). Input melebihi batas ditolak.
- **AC-PR-05** Untuk langkah pertama, bila Kum melebihi Qty WO sel tersebut, sistem menampilkan **peringatan** (tidak memblokir) dan meminta konfirmasi.
- **AC-PR-06** Setelah menyimpan, WO, Proses, dan Tanggal tetap terisi untuk input berikutnya, sedangkan baris pekerja dikosongkan. Navigasi grid mendukung keyboard (Tab/Enter berpindah sel).
- **AC-PR-07** Output pertama pada WO berstatus Released mengubah WO menjadi In Progress secara otomatis.
- **AC-PR-08** Layar input dan riwayat tidak menampilkan tarif atau nilai rupiah.

### 4.2 Aturan Tanggal dan Periode
- **AC-PR-09** Tanggal Kerja di masa depan ditolak.
- **AC-PR-10** Tanggal Kerja yang berada pada periode gaji tertutup ditolak dengan pesan "Periode gaji sudah ditutup, gunakan Penyesuaian".
- **AC-PR-10a** Setelah periode gaji pertama dibuat, Tanggal Kerja yang lebih awal dari tanggal mulai periode pertama ditolak (agar tidak ada output yang lolos dari penggajian). Aturan penutupan periode ada di FSD Payroll Borongan.

### 4.3 Void
- **AC-PR-11** Pengguna dengan permission `Void` dapat membatalkan entri dengan **alasan wajib**. Entri berstatus Voided dan tetap tampil di riwayat, tidak dihitung pada kumulatif dan upah.
- **AC-PR-12** Void ditolak bila setelah void aturan kumulatif terlanggar (contoh: Output langkah 2 menjadi lebih kecil dari Kum langkah 3).
- **AC-PR-13** Void ditolak bila tanggal kerja entri berada pada periode tertutup atau WO tidak dalam status yang diizinkan (bagian 2.3).
- **AC-PR-14** Entri yang sudah di-void tidak dapat diaktifkan kembali. Input ulang dilakukan sebagai entri baru.

### 4.4 Koreksi
- **AC-PR-15** Aksi **Koreksi** (permission `Correct`) pada satu entri melakukan *void entri asal* dan *membuat entri baru* dalam **satu transaksi atomik**. Pengguna dapat mengubah pekerja, tanggal kerja, dan qty. Alasan wajib.
- **AC-PR-16** Aturan kumulatif dicek hanya pada **kondisi akhir** (setelah void dan entri baru), sehingga koreksi pekerja salah tidak terhalang aturan sementara.
- **AC-PR-17** Entri baru menyimpan rujukan ke entri asal. Riwayat menampilkan keterkaitan keduanya.

### 4.5 Penyesuaian
- **AC-PR-18** Penyesuaian (permission `Adjust`) berisi: WO, Proses, Pekerja, Tanggal Kerja (periode terbuka, tidak masa depan), **Tanggal Asal** (pada periode tertutup, wajib), rujukan entri asal (opsional), qty per sel (boleh negatif/positif, minimal satu sel ≠ 0), alasan (wajib).
- **AC-PR-19** Penyesuaian tunduk pada aturan kumulatif dan status WO (bagian 2.3).
- **AC-PR-20** Upah penyesuaian dihitung dengan tarif snapshot WO dan masuk ke periode yang memuat Tanggal Kerja penyesuaian (diproses di FSD Payroll).
- **AC-PR-21** Penyesuaian dapat di-void (sesuai aturan void) selama periodenya masih terbuka.

### 4.6 Pengiriman dan Penerimaan Vendor
- **AC-PR-22** Pengguna dengan permission `VendorJob.Create` dapat mencatat **Kirim** pada langkah bertipe Vendor: Vendor (aktif), Tanggal, Nomor Dokumen (opsional), qty per Color × Size, Keterangan (opsional).
- **AC-PR-23** Kirim dibatasi oleh `Output langkah sebelumnya − Terkirim sebelumnya` per sel (bila langkah vendor pertama: `Qty WO − Terkirim sebelumnya`).
- **AC-PR-24** **Terima** dicatat dengan Vendor, Tanggal, Nomor Dokumen (opsional), dan qty per sel. Dapat dilakukan bertahap (sebagian). Terima dibatasi oleh `Terkirim − Diterima` **per vendor** per sel.
- **AC-PR-25** Qty Terima otomatis menjadi **output langkah vendor** dan menjadi batas atas langkah berikutnya. Terima pertama pada WO Released memicu In Progress.
- **AC-PR-26** Sisa di vendor = `Terkirim − Diterima` ditampilkan per vendor dan per sel pada detail WO.
- **AC-PR-27** Kirim dan Terima dapat di-void (permission `VendorJob.Void`) dengan alasan wajib, dengan syarat aturan kumulatif tetap terpenuhi pada kondisi akhir. Koreksi memakai void + entri baru atomik seperti AC-PR-15.
- **AC-PR-28** Proses vendor tidak memerlukan pekerja dan tidak menghasilkan upah.
- **AC-PR-29** Tanggal Kirim/Terima di masa depan ditolak. Periode gaji tidak memengaruhi catatan vendor.

### 4.7 Riwayat dan Tampilan
- **AC-PR-30** Daftar riwayat menampilkan seluruh entri (Output, Penyesuaian, Kirim, Terima), termasuk Voided (diberi penanda). Kolom: waktu input, WO, proses, pekerja/vendor, tanggal kerja, ringkasan qty, jenis, status, pembuat.
- **AC-PR-31** Filter: WO, proses, pekerja, vendor, rentang tanggal kerja, jenis, status (Aktif/Void). Pencarian: kode WO dan nama pekerja.
- **AC-PR-32** Detail entri menampilkan qty per Color × Size, alasan void/koreksi/penyesuaian, rujukan antar-entri, dan jejak (siapa, kapan).
- **AC-PR-33** Matriks progres kumulatif per proses pada detail WO (FSD Work Order AC-WO-28) bersumber dari entri aktif modul ini.

### 4.8 Integritas dan Keamanan
- **AC-PR-34** Entri tidak pernah dihapus atau diubah nilainya. Perubahan hanya melalui void, koreksi, atau penyesuaian.
- **AC-PR-35** Penghitungan aturan kumulatif bersifat konsisten terhadap konkurensi. Dua penyimpanan bersamaan yang bersama-sama melanggar aturan tidak boleh lolos keduanya.
- **AC-PR-36** Tombol dan menu mengikuti permission, penegakan di server bersifat final. Seluruh aksi tercatat di audit log.

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| ProductionOutput | View, Create, Void, Correct, Adjust |
| VendorJob | View, Create, Void |

Usulan awal Role: **Admin Produksi (Konveksi)** mendapat semuanya kecuali `Adjust`/`Void`/`Correct` bila perlu dipisah dengan supervisor (ditentukan Administrator). **Comfi** hanya `View`. **Admin Gaji** `ProductionOutput.View` dan `Adjust`.

---

## 6. Data Dictionary (Konsep Data)

### Output Entry (Header)
| Atribut | Keterangan |
|---|---|
| OutputEntryId | Identitas |
| WOId, Urutan Langkah (Process) | Konteks routing snapshot |
| WorkerId | Wajib, pekerja aktif (kecuali Penyesuaian) |
| WorkDate | Tanggal kerja |
| Jenis | Output, Penyesuaian |
| Status | Active, Voided |
| TanggalAsal | Wajib untuk Penyesuaian |
| ReplacesEntryId | Rujukan entri yang digantikan (hasil Koreksi) |
| RefEntryId | Rujukan entri yang disesuaikan (opsional, Penyesuaian) |
| Alasan | Wajib untuk Penyesuaian, Void, Koreksi |
| VoidedAt/By | Jejak void |
| CreatedAt/By | Jejak |

### Output Entry Line
EntryId, WOLineId (Color × Size), Qty (integer; negatif hanya untuk Penyesuaian).

### Vendor Movement (Header)
| Atribut | Keterangan |
|---|---|
| VendorMovementId | Identitas |
| WOId, Urutan Langkah (Vendor) | Konteks |
| VendorId | Wajib |
| Jenis | Kirim, Terima |
| Tanggal, NomorDokumen (opsional), Keterangan | |
| Status | Active, Voided |
| ReplacesMovementId, Alasan, VoidedAt/By, CreatedAt/By | Sama seperti Output Entry |

### Vendor Movement Line
MovementId, WOLineId, Qty (integer > 0).

> Kumulatif, batas tersedia, dan sisa di vendor adalah **hasil hitung** dari entri aktif, bukan data input.

---

## 7. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Input Output (multi-pekerja, grid Color × Size, batas tersedia) | **Interactive Server** | Validasi langsung dan navigasi keyboard untuk input volume tinggi |
| Input Penyesuaian | **Interactive Server** | Form dan validasi periode |
| Kirim/Terima Vendor | **Interactive Server** | Menampilkan sisa di vendor langsung |
| Riwayat Output dan Vendor + filter | **Interactive Server** | Filter, paging, detail |
| Dialog Void/Koreksi (alasan, pratinjau dampak) | **Interactive Server** | Pratinjau dampak aturan kumulatif sebelum disimpan |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai**.

---

## 8. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Batas tersedia berubah karena pengguna lain menyimpan lebih dulu | Penyimpanan ditolak dengan batas terbaru. Data di layar diperbarui, input pengguna dipertahankan. |
| E2 | Dua pengguna menyimpan output yang bersama-sama melebihi batas | Hanya yang pertama lolos (AC-PR-35). |
| E3 | Koneksi Blazor terputus saat mengisi grid | Input yang belum tersimpan hilang. Peringatan sebelum meninggalkan halaman. |
| E4 | Pekerja dinonaktifkan setelah entri dibuat | Entri tetap utuh. Koreksi tidak dapat memilih pekerja nonaktif sebagai pekerja baru. |
| E5 | Void entri langkah awal yang sudah dipakai langkah berikutnya | Ditolak bila membuat Output(n) < Kum(n+1). Pesan menyebutkan sel dan qty penahan. |
| E6 | Void Kirim vendor yang sebagiannya sudah diterima | Ditolak bila `Terkirim` setelah void < `Diterima`. |
| E7 | Void Terima yang sebagiannya sudah dipakai langkah berikutnya | Ditolak bila membuat Output vendor < Kum langkah berikutnya. |
| E8 | Langkah pertama bertipe Vendor | Kirim dibatasi `Qty WO` (AC-PR-23). |
| E9 | Salah ketik qty besar pada langkah pertama | Peringatan melebihi Qty WO (AC-PR-05). Langkah n > 1 dibatasi aturan kumulatif. |
| E10 | WO Completed saat pengguna sedang mengisi form | Penyimpanan ditolak: "WO telah ditandai selesai". |
| E11 | Output diinput untuk tanggal yang sudah masuk periode tertutup | Ditolak (AC-PR-10), disarankan Penyesuaian. |
| E12 | Penyesuaian yang membuat kumulatif melanggar aturan | Ditolak dengan rincian sel. |
| E13 | Penyesuaian dengan Tanggal Asal di periode yang masih terbuka | Ditolak: gunakan Void atau Koreksi. |
| E14 | Pekerja yang sama muncul dua kali dalam satu penyimpanan | Ditolak, baris ditandai. |
| E15 | Input di WO dengan Color × Size yang qty-nya nol (tidak ada baris WO) | Sel tidak tersedia pada grid. |
| E16 | Pengguna ingin mencatat reject/rework | Tidak ada di fase 1. Hanya output lolos yang dicatat. |
| E17 | Pembuatan Koreksi pada entri yang sudah Voided | Ditolak. |
| E18 | Koreksi mengubah tanggal ke periode tertutup | Ditolak (AC-PR-10). |

---

## 9. Non-Functional Requirements (Ringkas)

- Layar input harus nyaman untuk volume tinggi: puluhan baris pekerja per hari dan navigasi keyboard penuh.
- Perhitungan aturan kumulatif dilakukan di server dalam transaksi yang terisolasi per WO.
- Bahasa UI Indonesia, tanggal dan angka lokal Indonesia.
- Tarif/nilai rupiah tidak dikirim ke klien pada layar modul ini.
- Seluruh perubahan penting tercatat di audit log.

---

## 10. Catatan untuk Peran dan FSD Berikutnya

- **Payroll Borongan:** sumber data = entri Output dan Penyesuaian aktif (jenis dengan pekerja), dikelompokkan per pekerja, periode (tanggal kerja), WO, proses, Size. Upah = qty × tarif snapshot WO. Aturan pembulatan hasil perkalian ditetapkan di FSD Payroll. Penutupan periode memblokir input (AC-PR-10) dan mengaktifkan Penyesuaian.
- **Serah Terima & Stok FG:** qty serah terima dibatasi `Output langkah terakhir − sudah diserahterimakan`, per Color × Size. Void output langkah terakhir harus memperhatikan qty yang sudah diserahterimakan.
- **Work Order:** matriks progres dan "Sisa di vendor" bersumber dari modul ini.
- **Architect:** pilih pendekatan yang menjaga invariant kumulatif secara atomik dan efisien (kunci per WO, atau tabel agregat kumulatif per langkah dan sel yang diperbarui dalam transaksi yang sama). Pastikan data indeks untuk laporan payroll (per tanggal kerja dan pekerja).

---

## 11. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Satu layar untuk beberapa pekerja (satu entri per pekerja) | **Disetujui** |
| Q2 | Tanggal kerja boleh mundur selama periode terbuka, masa depan dilarang | **Disetujui** |
| Q3 | Langkah pertama tanpa batas atas, langkah berikutnya dibatasi proses sebelumnya | **Disetujui** |
| Q4 | Beberapa entri per pekerja per proses per hari diperbolehkan | **Disetujui** |
| Q5 | Riwayat menampilkan semua entri termasuk void | **Disetujui** |
| Q6 | Dokumen vendor memiliki nomor dokumen opsional | **Disetujui** |
| Q7 | Proses vendor tanpa pekerja dan tanpa upah | **Disetujui** |
| Q8 | Peringatan (tidak memblokir) bila Kum langkah pertama melebihi Qty WO | Default SA, berlaku sampai ada keberatan |
| Q9 | Penyesuaian wajib mencantumkan Tanggal Asal pada periode tertutup | Default SA, berlaku sampai ada keberatan |
