# FSD — Master Data

| Atribut | Nilai |
|---|---|
| Modul | Master Data |
| Versi | 0.1 (Draft, menunggu persetujuan) |
| Tipe | Modul Baru |
| Penulis | System Analyst |
| Fase | 1 |
| Dependensi | Access Management (autentikasi, permission, audit log) |
| Dipakai oleh | Customer PO, Work Order, Production Output, Payroll Borongan |

---

## 1. Business Objective

Menyediakan satu sumber data referensi yang konsisten untuk seluruh proses bisnis GMS. Data ini dikendalikan oleh **Comfi** (kecuali data Pekerja yang dikendalikan **Konveksi**) dan menjadi dasar:

- pembuatan PO dan Work Order (FG, Size, Color, Customer),
- penentuan urutan proses produksi dan tarif upah borongan (Routing dan Tarif),
- pencatatan output dan penggajian pekerja (Pekerja, Process).

### Keputusan Cakupan Fase 1
- **BOM ditiadakan.** Customer tidak menginginkan kontrol material yang ketat. FG tidak memiliki daftar kebutuhan material. Kebutuhan ini dapat ditambahkan sebagai Enhancement di kemudian hari.
- **Material tetap ada sebagai master** (tanpa harga dan supplier), tetapi belum dipakai oleh modul transaksi di fase 1. Master ini menyiapkan fondasi untuk pembelian di fase berikutnya.
- Kain dengan warna berbeda adalah **material yang berbeda** (contoh: "Drill Navy" dan "Drill Orange").
- Mata uang: **Rupiah** saja.

### Di Luar Scope
- BOM dan perhitungan kebutuhan material.
- Harga material dan data supplier.
- Rekening bank pekerja.
- Beberapa kelompok size (satu daftar size berlaku untuk semua FG).
- Proses paralel dalam routing (routing selalu linear).
- Riwayat versi routing (cukup audit log).

---

## 2. Entitas dan Kepemilikan

| Entitas | Dikelola | Kode |
|---|---|---|
| Satuan (UoM) | Comfi | Manual |
| Kategori Material | Comfi | Manual |
| Material | Comfi | Manual |
| Size | Comfi | Manual |
| Color | Comfi | Manual |
| Customer | Comfi | Otomatis (CUS-0001) |
| FG (Finished Goods) | Comfi | Manual |
| Process | Comfi | Manual |
| Routing (bagian dari FG) | Comfi | – |
| Tarif (bagian dari FG) | Comfi | – |
| Vendor | Comfi | Otomatis (VND-0001) |
| Pekerja | **Konveksi** | Otomatis (PKJ-0001) |

> Kepemilikan di atas diwujudkan melalui permission (bagian 5), bukan pemisahan data. Administrator menetapkan siapa yang memegang hak tersebut melalui Role.

---

## 3. Aturan Umum (Berlaku untuk Semua Entitas)

- **R1 Kode unik**, tidak peka huruf besar-kecil. Kode manual divalidasi pada saat simpan.
- **R2 Kode tidak dapat diubah** setelah data dipakai oleh data lain atau transaksi.
- **R3 Tidak ada penghapusan.** Data hanya **dinonaktifkan** atau diaktifkan kembali. Data nonaktif tidak muncul sebagai pilihan untuk transaksi baru, tetapi tetap tampil pada data lama yang sudah memakainya.
- **R4 Audit.** Setiap pembuatan, perubahan, dan penonaktifan tercatat di audit log (pelaku, waktu, nilai sebelum dan sesudah). Perubahan Routing dan Tarif wajib tercatat rinci.
- **R5 Concurrency.** Pengubahan bersamaan atas data yang sama ditolak untuk penyimpan kedua dengan pesan "Data telah diubah oleh pengguna lain, muat ulang".
- **R6 Daftar standar.** Setiap daftar menyediakan pencarian, filter status (Aktif/Nonaktif/Semua), pengurutan, dan paging.
- **R7 Snapshot.** Perubahan Routing, Tarif, atau data master lain **tidak memengaruhi** PO/WO/output yang sudah ada. Perubahan hanya berlaku untuk dokumen yang dibuat atau dirilis sesudahnya.

---

## 4. Atribut per Entitas (Data Dictionary)

### 4.1 Satuan (UoM)
Kode, Nama, IsActive. Contoh: MTR (meter), YRD (yard), PCS, KG, ROL, LSN.

### 4.2 Kategori Material
Kode, Nama, IsActive. Contoh: Kain, Benang, Kancing, Resleting, Label.

### 4.3 Material
| Atribut | Aturan |
|---|---|
| Kode | Manual, unik |
| Nama | Wajib |
| Kategori | Wajib, pilih dari Kategori Material aktif |
| Satuan | Wajib, pilih dari UoM aktif |
| Spesifikasi/Keterangan | Opsional |
| IsActive | Ya/Tidak |

Tidak ada atribut harga, supplier, dan warna. Warna dinyatakan dalam nama material.

### 4.4 Size
Kode, Nama, Urutan Tampil (angka, menentukan urutan di seluruh sistem), IsActive. Contoh: S, M, L, XL, 2XL, 3XL. Satu daftar size untuk seluruh FG.

### 4.5 Color
Kode, Nama, Kode Hex (opsional, untuk tampilan), IsActive.

### 4.6 Customer
Kode (otomatis), Nama (wajib, unik), Kontak/PIC, Telepon, Alamat, Keterangan, IsActive.

### 4.7 Process
| Atribut | Aturan |
|---|---|
| Kode | Manual, unik (contoh: CUT, SEW, EMB) |
| Nama | Wajib (contoh: Cutting, Sewing, Bordir) |
| Tipe | **Internal** (dikerjakan pekerja Konveksi, bertarif) atau **Vendor** (dikerjakan vendor luar, tanpa tarif) |
| IsActive | Ya/Tidak |

Tipe tidak dapat diubah setelah Process dipakai pada suatu Routing.

### 4.8 FG (Finished Goods)
| Atribut | Aturan |
|---|---|
| Kode | Manual, unik |
| Nama | Wajib |
| Jenis | Teks bebas (contoh: Kemeja, Celana, Coverall) |
| Customer | Opsional |
| Keterangan | Opsional |
| **Size Berlaku** | Minimal 1 size dari master Size. FG tidak terikat warna. |
| **Routing** | Daftar urut Process (bagian 4.9) |
| **Tarif** | Matriks Process Internal × Size Berlaku (bagian 4.10) |
| IsActive | Ya/Tidak |
| **Status Kesiapan** | Dihitung otomatis (lihat 4.11) |

### 4.9 Routing (per FG)
- Daftar langkah berurutan: Urutan 1..n, masing-masing menunjuk satu Process aktif.
- Routing **linear**: output langkah ke-n tidak boleh melebihi output langkah ke-(n−1) (aturan ini ditegakkan di modul Production Output).
- **Satu Process hanya boleh muncul sekali** dalam satu Routing.
- Minimal 1 langkah.
- Urutan dapat diubah (naik/turun), langkah dapat ditambah atau dihapus.

### 4.10 Tarif (per FG × Process Internal × Size)
| Atribut | Aturan |
|---|---|
| Nilai | Rupiah per unit, **lebih besar dari 0**, presisi hingga **5 angka di belakang koma** |
| Cakupan | Hanya untuk Process bertipe Internal yang ada di Routing FG, dan Size yang berlaku untuk FG |
| Perubahan | Tidak ada tanggal berlaku. Yang tersimpan adalah tarif saat ini, riwayat perubahan ada di audit log. |

Aturan turunan:
- Menghapus langkah dari Routing menghapus tarif langkah tersebut (nilainya tetap terekam di audit log), dengan konfirmasi.
- Menghapus Size dari FG menghapus tarif size tersebut, dengan konfirmasi.
- Menambah langkah Internal atau Size baru membuat sel tarif kosong sehingga FG belum Siap Produksi sampai terisi.

### 4.11 Status Kesiapan FG ("Siap Produksi")
FG berstatus **Siap Produksi** bila semua terpenuhi:
1. FG aktif.
2. Minimal 1 Size Berlaku.
3. Routing memiliki minimal 1 langkah dan semua Process di dalamnya aktif.
4. Setiap langkah bertipe Internal memiliki tarif (> 0) untuk **setiap** Size Berlaku.

Jika tidak terpenuhi, sistem menampilkan alasan spesifik (contoh: "Tarif Sewing untuk size XL belum diisi"). Hanya FG Siap Produksi yang dapat dipilih untuk Work Order (ditegakkan di FSD Work Order).

### 4.12 Vendor
Kode (otomatis), Nama (wajib), Jenis Layanan (teks, contoh: Bordir), Kontak/PIC, Telepon, Alamat, Keterangan, IsActive.

### 4.13 Pekerja (dikelola Konveksi)
| Atribut | Aturan |
|---|---|
| Kode | Otomatis (PKJ-0001) |
| Nama | Wajib |
| Tanggal Masuk | Opsional |
| Proses Utama | Opsional (pilih dari Process Internal), hanya untuk memudahkan pencarian. **Tidak membatasi** proses yang boleh dikerjakan. |
| Keterangan | Opsional |
| IsActive | Ya/Tidak |

---

## 5. Permission (Didaftarkan ke Katalog Access Management)

| Resource | Action |
|---|---|
| MasterData.UoM, MasterData.KategoriMaterial, MasterData.Material | View, Create, Edit, Deactivate, Import (hanya Material) |
| MasterData.Size, MasterData.Color, MasterData.Customer, MasterData.Process, MasterData.Vendor | View, Create, Edit, Deactivate |
| MasterData.FG | View, Create, Edit, Deactivate, Import |
| MasterData.Routing | View, Edit |
| MasterData.Tarif | View, Edit |
| MasterData.Pekerja | View, Create, Edit, Deactivate, Import |

Catatan untuk konfigurasi Role (usulan awal):
- **Comfi:** semua resource kecuali `MasterData.Pekerja`.
- **Konveksi:** `MasterData.Pekerja` penuh, sisanya hanya `View` (Tarif tidak harus terlihat oleh Konveksi, ditentukan Administrator).
- Katalog di FSD Access Management bagian 2 diperbarui mengikuti tabel ini (penambahan Customer, UoM, Kategori Material, dan Import).

---

## 6. User Stories

| ID | Sebagai | Saya ingin | Agar |
|---|---|---|---|
| US-MD-01 | Staf Comfi | mengelola master Size, Color, UoM, Kategori Material, dan Material | data referensi seragam |
| US-MD-02 | Staf Comfi | mengelola Customer dan Vendor | PO dan proses vendor merujuk data yang benar |
| US-MD-03 | Staf Comfi | mengelola Process dan menentukan tipenya (Internal/Vendor) | routing bisa dibentuk |
| US-MD-04 | Staf Comfi | membuat FG lengkap dengan Size Berlaku | PO dan WO memakai FG dan size yang valid |
| US-MD-05 | Staf Comfi | menyusun Routing urut per FG | alur produksi tiap FG jelas |
| US-MD-06 | Staf Comfi | mengisi Tarif per Process × Size untuk tiap FG | upah borongan dapat dihitung |
| US-MD-07 | Staf Comfi | melihat status kesiapan FG beserta alasannya | tahu apa yang kurang sebelum membuat WO |
| US-MD-08 | Staf Comfi | menyalin Routing dan Tarif dari FG lain | tidak mengulang input untuk FG yang mirip |
| US-MD-09 | Staf Konveksi | mengelola master Pekerja | output dan gaji merujuk pekerja yang benar |
| US-MD-10 | Staf | mengimpor data awal Material, FG, dan Pekerja dari file CSV | tidak perlu input satu per satu |
| US-MD-11 | Staf | menonaktifkan data yang tidak dipakai lagi tanpa menghapus riwayat | histori tetap utuh |

---

## 7. Acceptance Criteria

### 7.1 Umum
- **AC-MD-01** Setiap entitas menyediakan daftar dengan pencarian, filter status, dan paging, serta formulir tambah dan ubah.
- **AC-MD-02** Given kode duplikat (tanpa peka huruf besar-kecil), When disimpan, Then ditolak dengan pesan "Kode sudah digunakan".
- **AC-MD-03** Given data sudah dipakai oleh data lain, When mengubah kode, Then ditolak (kolom kode terkunci).
- **AC-MD-04** Given data dinonaktifkan, Then data tidak muncul pada pilihan (dropdown) transaksi baru, tetapi tetap ditampilkan apa adanya pada data lama.
- **AC-MD-05** Tidak ada tombol hapus. Data hanya dapat dinonaktifkan atau diaktifkan kembali.
- **AC-MD-06** Seluruh perubahan tercatat di audit log dengan nilai sebelum dan sesudah.
- **AC-MD-07** Tombol dan menu yang tidak diizinkan oleh permission disembunyikan, dan aksi ditolak server bila dipanggil langsung.

### 7.2 Size, Color, Material, Customer, Vendor
- **AC-MD-08** Size diurutkan berdasarkan Urutan Tampil di seluruh dropdown dan grid.
- **AC-MD-09** Material wajib memiliki Kategori dan Satuan aktif.
- **AC-MD-10** Kode Customer, Vendor, dan Pekerja dibuat otomatis berurutan dan tidak dapat diedit.
- **AC-MD-11** Menonaktifkan Size, Color, Kategori, atau UoM yang masih dipakai ditolak bila dipakai pada data aktif (Size pada FG aktif, Kategori/UoM pada Material aktif), dengan menampilkan daftar pemakainya. Color yang sudah dipakai pada dokumen lama dapat dinonaktifkan.

### 7.3 Process
- **AC-MD-12** Process memiliki Tipe Internal atau Vendor.
- **AC-MD-13** Tipe Process tidak dapat diubah bila sudah dipakai pada Routing.
- **AC-MD-14** Menonaktifkan Process yang masih ada di Routing FG aktif ditolak. Sistem menampilkan daftar FG terkait agar langkahnya dihapus lebih dulu.

### 7.4 FG
- **AC-MD-15** FG wajib memiliki minimal 1 Size Berlaku. Size yang tidak aktif tidak dapat ditambahkan.
- **AC-MD-16** Menghapus Size dari FG yang sudah memiliki tarif untuk size itu meminta konfirmasi, lalu tarif size tersebut dihapus dan nilainya tercatat di audit log.
- **AC-MD-17** FG menampilkan Status Kesiapan (Siap Produksi/Belum Siap) beserta daftar alasan spesifik sesuai bagian 4.11.
- **AC-MD-18** Menonaktifkan FG yang masih memiliki PO/WO terbuka ditolak (aturan rinci mengikuti FSD Customer PO dan Work Order).

### 7.5 Routing
- **AC-MD-19** Routing dibangun dengan menambah langkah dari Process aktif. Urutan dapat diubah (naik/turun). Satu Process tidak boleh dobel.
- **AC-MD-20** Menghapus langkah yang sudah memiliki tarif meminta konfirmasi, lalu tarif langkah itu dihapus dan nilainya tercatat di audit log.
- **AC-MD-21** Perubahan Routing tampil dengan peringatan "Hanya berlaku untuk Work Order yang dirilis setelah ini".

### 7.6 Tarif
- **AC-MD-22** Tarif diinput pada **grid Process × Size**. Hanya langkah Internal pada Routing dan Size Berlaku yang tampil. Langkah bertipe Vendor tidak memiliki baris tarif.
- **AC-MD-23** Grid menyediakan aksi "Isi semua size dengan nilai ini" per baris Process.
- **AC-MD-24** Nilai tarif harus **> 0**, memakai format angka Indonesia (koma sebagai desimal), maksimal 5 digit di belakang koma. Nilai lebih dari 5 digit desimal ditolak (tidak dibulatkan diam-diam).
- **AC-MD-25** Setiap perubahan tarif menyimpan nilai lama, nilai baru, pelaku, dan waktu di audit log.
- **AC-MD-26** Perubahan tarif tidak mengubah WO yang sudah dirilis (R7).

### 7.7 Salin dari FG Lain
- **AC-MD-27** Pada FG, pengguna dengan permission Routing dan Tarif dapat memilih "Salin dari FG lain". Routing disalin penuh. Tarif disalin hanya untuk kombinasi Process × Size yang juga ada pada FG tujuan. Hasil salinan ditampilkan untuk ditinjau sebelum disimpan, dan data Routing/Tarif FG tujuan yang sudah ada akan ditimpa setelah konfirmasi.

### 7.8 Pekerja
- **AC-MD-28** Pekerja dikelola hanya oleh pengguna dengan permission `MasterData.Pekerja`. Pekerja nonaktif tidak muncul pada input output baru, tetapi output lama tetap utuh.
- **AC-MD-29** Proses Utama pekerja tidak membatasi proses yang boleh diinput untuknya.

### 7.9 Import CSV (Material, FG, Pekerja)
- **AC-MD-30** Sistem menyediakan template CSV per entitas yang dapat diunduh. Format: UTF-8, pemisah koma atau titik koma (terdeteksi otomatis).
- **AC-MD-31** Alur: unggah file → **validasi seluruh baris** → tampilkan hasil per baris (valid/error beserta alasan) → pengguna mengonfirmasi. Impor hanya dilanjutkan bila **semua baris valid** (semua-atau-tidak-sama-sekali).
- **AC-MD-32** Import hanya **menambah** data baru. Kode yang sudah ada ditolak sebagai error (tidak ada update massal di fase 1).
- **AC-MD-33** Batas ukuran file 2 MB atau 2.000 baris per impor. Melebihi batas ditolak dengan pesan jelas.
- **AC-MD-34** Import FG hanya membuat data dasar FG beserta Size Berlaku (daftar kode size dipisah tanda `|`). Routing dan Tarif tidak diimpor di fase 1 (diisi manual atau melalui "Salin dari FG lain").
- **AC-MD-35** Setiap impor tercatat di audit log (pelaku, nama file, jumlah baris).

---

## 8. Render Mode (Frontend)

| Halaman | Render Mode | Alasan |
|---|---|---|
| Seluruh daftar master (UoM, Kategori, Material, Size, Color, Customer, Process, FG, Vendor, Pekerja) | **Interactive Server** | Pencarian, filter, dan paging dinamis |
| Formulir tambah/ubah semua entitas | **Interactive Server** | Validasi langsung, dropdown dependen |
| Halaman detail FG (Size Berlaku, Routing, Tarif, Status Kesiapan) | **Interactive Server** | Reorder routing, grid tarif, status kesiapan dihitung ulang langsung |
| Import CSV (unggah, hasil validasi, konfirmasi) | **Interactive Server** | Pratinjau hasil validasi per baris tanpa memuat ulang |

Tidak ada halaman Static SSR pada modul ini. WebAssembly **tidak dipakai** (pengguna sedikit, jaringan internal).

---

## 9. Edge Cases & Error Handling

| # | Kasus | Perilaku |
|---|---|---|
| E1 | Dua pengguna mengedit data yang sama bersamaan | Penyimpan kedua ditolak, muat ulang (R5) |
| E2 | Dua pengguna membuat data dengan kode otomatis bersamaan | Nomor urut dijamin unik oleh sistem, tidak ada duplikat |
| E3 | Process dinonaktifkan saat masih ada di Routing | Ditolak (AC-MD-14) |
| E4 | Process di Routing berubah dari Internal ke Vendor | Tidak diizinkan setelah dipakai (AC-MD-13) |
| E5 | Menambah Size baru ke FG yang sudah Siap Produksi | Status berubah menjadi Belum Siap sampai tarif size baru terisi. PO/WO yang sudah ada tidak terpengaruh. |
| E6 | Pengguna menghapus semua langkah Routing | Ditolak, minimal 1 langkah |
| E7 | Tarif diisi 0, negatif, atau lebih dari 5 digit desimal | Ditolak dengan pesan spesifik per sel |
| E8 | Penggunaan titik/koma pada input tarif | UI memakai format Indonesia. Titik dibaca sebagai pemisah ribuan, koma sebagai desimal. Input ambigu ditolak dengan petunjuk. |
| E9 | File CSV dengan encoding salah, kolom kurang/lebih, atau baris kosong | Seluruh file ditolak dengan pesan kolom/baris bermasalah |
| E10 | File CSV berisi kode ganda dalam file yang sama | Kedua baris ditandai error |
| E11 | Import Material dengan Kategori/Satuan yang belum ada | Baris error: "Kategori 'X' tidak ditemukan" (tidak membuat otomatis) |
| E12 | Salin dari FG lain: Size FG sumber tidak ada di FG tujuan | Tarif size itu dilewati, dilaporkan pada pratinjau |
| E13 | Menonaktifkan Pekerja yang memiliki output pada periode payroll terbuka | Diizinkan. Output tetap utuh dan tetap ikut dihitung gajinya. |
| E14 | Koneksi Blazor terputus saat mengisi grid tarif | Perubahan yang belum disimpan hilang. Tampilkan peringatan "Ada perubahan belum disimpan" saat meninggalkan halaman. |
| E15 | Size Berlaku sangat banyak sehingga grid tarif lebar | Grid dapat di-scroll horizontal dengan kolom Process tetap |

---

## 10. Non-Functional Requirements (Ringkas)

- Bahasa UI: Bahasa Indonesia. Format angka dan tanggal sesuai lokal Indonesia.
- Presisi tarif: 5 digit desimal disimpan dan dihitung tanpa pembulatan tersembunyi. Aturan pembulatan hasil perkalian (qty × tarif) ditetapkan di FSD Payroll Borongan.
- Daftar tetap responsif untuk puluhan ribu baris (pencarian dan paging di sisi server).
- Seluruh pemeriksaan permission dilakukan di sisi server.

---

## 11. Catatan untuk Peran Berikutnya

- **Architect:** modelkan Routing dan Tarif sebagai bagian dari agregat FG. Rancang mekanisme penomoran otomatis yang aman terhadap konkurensi. Siapkan struktur snapshot Routing/Tarif yang akan dipakai Work Order.
- **FSD berikutnya:** Customer PO wajib memakai Customer, FG, Size, dan Color dari modul ini. Work Order wajib memeriksa Status Kesiapan FG saat dirilis.

---

## 12. Asumsi & Pertanyaan Terbuka

| # | Asumsi / Pertanyaan | Status |
|---|---|---|
| Q1 | Kode otomatis hanya untuk Customer, Vendor, dan Pekerja. Process, Kategori, UoM memakai kode manual. | Asumsi SA, perlu konfirmasi |
| Q2 | Satu Process hanya sekali dalam satu Routing. Bila ada kebutuhan proses yang sama dikerjakan dua kali (misal QC di tengah dan akhir), buat dua Process terpisah. | Asumsi SA, perlu konfirmasi |
| Q3 | Tarif harus > 0. Tidak ada proses internal tanpa upah. | Asumsi SA, perlu konfirmasi |
| Q4 | Fitur "Salin dari FG lain" (AC-MD-27) termasuk fase 1. | Usulan SA, perlu konfirmasi |
| Q5 | Import CSV hanya menambah data (tanpa update massal) dan semua-atau-tidak-sama-sekali. | Usulan SA, perlu konfirmasi |
| Q6 | Material, Kategori, dan UoM dibangun di fase 1 walau belum dipakai transaksi. | Perlu konfirmasi (alternatif: tunda ke fase pembelian) |
| Q7 | Jumlah data awal (Material, FG, Pekerja) belum diketahui, hanya memengaruhi batas import (2.000 baris). | Informasi |
