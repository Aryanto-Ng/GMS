# Desain UI — Production Output

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/ProductionOutput.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | Semua **Interactive Server** |
| Pengguna utama | Admin Produksi, input volume tinggi di PC → mode **Compact**, keyboard-first |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| **Input Output** | `/produksi/input` | `ProductionOutput.Create` |
| Penyesuaian Output | `/produksi/penyesuaian` | `ProductionOutput.Adjust` |
| Kirim/Terima Vendor | `/produksi/vendor` (tab Kirim · Terima) | `VendorJob.Create` |
| Riwayat Produksi | `/produksi/riwayat` | `ProductionOutput.View` / `VendorJob.View` |
| Detail Entri | `/produksi/riwayat/{id}` (panel samping) | View |

Dialog: Void, Koreksi (grid terisi), Konfirmasi Peringatan.

---

## 2. Input Output (Layar Inti)

```
Input Output
WO*   [WO-202611-0004 · KMJ-001 · PT Maju ▾]   Proses* [3 Sewing ▾]   Tanggal Kerja* [08/11/2026]
Info: Output proses sebelumnya (Cutting) 665 · Sudah Sewing 290 · Tersedia 375         [Filter Color: ☑Navy ☑Orange]

                    │ Navy                            │ Orange              │
Pekerja             │ S      M      L      XL        │ S      M      L      │ Total
Batas tersedia      │ 40     110    100    55         │ 0      45     25     │   
───────────────────┼─────────────────────────────────┼──────────────────────┼───────
[PKJ-0012 Budi ▾] ✕ │ [20 ]  [30 ]  [   ]  [   ]      │ [  ]   [  ]   [  ]   │   50
[PKJ-0007 Siti ▾] ✕ │ [   ]  [40 ]  [25 ]  [   ]      │ [  ]   [10 ]  [  ]   │   75
[ + Tambah pekerja ]                                                            
Total               │ 20     70     25     0          │ 0      10     0       │  125
[Simpan]                                    ● Belum disimpan   2 pekerja · 125 pcs
```

### Perilaku
- **Selector atas:** WO (hanya Released/In Progress), Proses (langkah Internal pada routing snapshot, berurut), Tanggal Kerja (default hari ini; tanggal masa depan dan tanggal pada periode tertutup ditolak dengan pesan).
- **Baris info:** menampilkan output proses sebelumnya, kumulatif proses ini, dan tersedia.
- **Baris "Batas tersedia"** per kolom Color × Size: `Output proses sebelumnya − Kum proses ini`. Untuk proses pertama: "Bebas" dengan angka sisa terhadap Qty WO sebagai rujukan.
- **Kolom** = Color (kelompok) → Size. Bila kolom lebih lebar dari layar: kolom Pekerja dan header tetap, gulir horizontal. Filter Color mempersempit kolom yang ditampilkan (data di luar filter tetap tersimpan).
- **Tambah pekerja:** combobox bersifat pencarian kode/nama, hanya pekerja aktif. Pekerja yang sudah ada di tabel tidak muncul lagi. Fokus langsung ke sel pertama baris baru.
- **Keyboard:** Tab/Shift+Tab antar sel, Enter pindah ke bawah, panah, Ctrl+V tempel blok, Ctrl+Enter = Simpan, Alt+N = tambah pekerja.
- **Validasi langsung:** sel yang melebihi batas → bergaris merah + ikon + pesan "Maks 40". Total di bawah memperbarui secara langsung.
- **Proses pertama melebihi Qty WO:** sel kuning + ikon dan pesan "Melebihi Qty WO (kumulatif 1.050 dari 1.000)". Saat Simpan: dialog konfirmasi (AC-PR-05).
- **Simpan:** semua-atau-tidak-sama-sekali. Bila ada baris bermasalah: ringkasan galat di atas, baris/sel ditandai, fokus ke ringkasan.
- **Setelah berhasil:** toast "2 entri tersimpan (125 pcs)". WO, Proses, dan Tanggal **tetap**, baris pekerja dikosongkan, fokus ke tombol Tambah pekerja.
- **Penjaga keluar:** peringatan bila ada input belum tersimpan.
- Tidak ada nilai rupiah/tarif di layar ini.

---

## 3. Kirim / Terima Vendor

```
Vendor    Tab: Kirim │ Terima
WO* [WO-202611-0004 ▾]   Proses vendor* [2 Bordir ▾]   Vendor* [CV Bordir Indah ▾]
Tanggal* [08/11/2026]   No. Dokumen [_________]   Keterangan [_________]
                 Navy                         Orange
                 S     M     L     XL         S     M     L
Tersedia dikirim 110   210   160   55         —     85    45     ← (Tab Kirim)
Qty kirim       [100] [200] [   ] [   ]       [  ]  [ 80] [  ]
────────────────────────────────────────────────────────────
Sisa di vendor (CV Bordir Indah)  S 0 · M 0 · ...                    ← (Tab Terima: batas = terkirim − diterima)
[Simpan]
```
- Tab **Terima** menampilkan "Sisa di vendor" per Color × Size untuk vendor terpilih sebagai batas. Terima sebagian diperbolehkan.
- Panel samping "Ringkasan Vendor": per vendor Terkirim / Diterima / Sisa untuk langkah ini.
- Semua aturan validasi, konfirmasi, dan pesan mengikuti pola Input Output.

---

## 4. Penyesuaian Output

```
Penyesuaian Output (untuk periode gaji yang sudah ditutup)
WO* [...]  Proses* [...]  Pekerja* [PKJ-0012 Budi ▾ (termasuk nonaktif)]
Tanggal Asal* [01/11/2026]  (harus dalam periode tertutup GJ-20261026-20261101)
Tanggal Kerja penyesuaian* [08/11/2026]  (periode terbuka)
Alasan* [______________________________]
Qty (boleh negatif):   Navy S [ -5 ]  M [  ]  ...
[Simpan penyesuaian]
```
- Peringatan info: "Upah penyesuaian dihitung dengan tarif snapshot WO dan masuk ke periode yang memuat Tanggal Kerja penyesuaian."
- Tanggal Asal di periode terbuka ditolak dengan arahan "Gunakan Void atau Koreksi dari Riwayat".
- Sel menerima tanda minus. Pelanggaran aturan kumulatif ditampilkan sebagai galat dengan rincian sel.

---

## 5. Riwayat Produksi

```
Riwayat Produksi
[WO ▾][Proses ▾][Pekerja ▾][Vendor ▾][Tanggal kerja ▾][Jenis ▾][Status: Aktif ▾]   ☐ tampilkan Void
Waktu input       WO         Proses   Pekerja/Vendor  Tgl kerja   Jenis        Qty    Status   Aksi
08 Nov 10:02      WO-…0004   Sewing   Budi            08 Nov      Output       50     Aktif    ⋯
08 Nov 09:40      WO-…0004   Sewing   Siti            08 Nov      Output       ~~75~~ Void     ⋯ (↳ digantikan oleh #…)
07 Nov 16:10      WO-…0004   Bordir   CV Bordir       07 Nov      Terima       250    Aktif    ⋯
```
- Baris Voided: teks dicoret + chip "Void" + alasan di tooltip. Tautan antar entri (digantikan oleh / menggantikan).
- Klik baris membuka **panel detail**: qty per Color × Size, alasan, rujukan, jejak pembuat. Aksi: Void, Koreksi (sesuai permission dan syarat).
- Aksi Void/Koreksi dinonaktifkan dengan tooltip alasan bila WO Completed/Closed atau tanggal kerja berada di periode tertutup (arahkan ke Penyesuaian).
- Ekspor tidak ada di fase 1.

### Dialog Void
```
Batalkan entri ini?  Sewing · Siti · 08 Nov · 75 pcs
Dampak pada kumulatif:
 Langkah   Sel         Sebelum  Sesudah  Batas    Status
 3 Sewing  Navy · M     100      60       ≥ 4 QC 40  ✓
 3 Sewing  Navy · L      60      35       ≥ 4 QC 40  ✗ Tertahan oleh QC (40)
Alasan* [______________________]
[Batal] [Void entri]      ← nonaktif bila ada ✗
```
### Dialog Koreksi
Grid yang sama dengan Input Output (satu baris), terisi nilai entri asal; boleh mengubah pekerja, tanggal, dan qty. Panel pratinjau dampak (kondisi akhir) di bawah, alasan wajib. [Simpan koreksi] nonaktif bila ada ✗.

---

## 6. State UI

| Layar | Loading | Kosong | Error |
|---|---|---|---|
| Input Output | Skeleton selector + grid setelah pilih WO | Belum pilih WO: "Pilih WO dan proses untuk mulai". WO tanpa proses Internal: pesan. | Gagal muat batas: banner + Coba lagi, Simpan nonaktif |
| Simpan | Spinner pada tombol, grid `aria-busy` | – | Ringkasan galat + sel ditandai. Konflik batas berubah: banner "Batas diperbarui" dan nilai input dipertahankan |
| Kirim/Terima | Skeleton | Tab Terima tanpa kiriman: "Belum ada barang di vendor" | idem |
| Riwayat | Skeleton 10 baris | "Belum ada catatan produksi" / hasil filter kosong + Reset | Banner + Coba lagi |
| WO tidak boleh diinput | – | Pesan di atas halaman: "WO ini berstatus Completed. Buka kembali untuk menginput." (+ tautan ke WO) | – |
| Koneksi terputus | Banner "Menyambung ulang…" | – | Bila putus, peringatan data belum tersimpan hilang |

---

## 7. Aksesibilitas

- Grid input: `role="grid"`, nama sel "Budi, Navy M, qty", batas dibacakan ("maksimum 110"). Galat melalui `aria-describedby` dan pengumuman `aria-live="assertive"` untuk ringkasan.
- Tidak ada informasi yang hanya di warna: kuning/merah selalu disertai ikon dan teks.
- Combobox pekerja: pola ARIA combobox, daftar dapat dinavigasi panah, Enter memilih.
- Tombol aksi Void/Koreksi dengan label jelas ("Void entri Siti 08 Nov").
- Mode Compact tetap menjaga target klik ≥ 24 px dan jarak fokus yang jelas.

---

## 8. Catatan untuk Frontend

- Komponen **Grid Matriks Ukuran** harus mendukung: kolom bertingkat (Color → Size), baris "batas", baris pekerja dinamis, tempel blok, virtualisasi bila kolom banyak.
- Batas tersedia dan pratinjau dampak berasal dari server (aturan kumulatif tidak diimplementasikan ulang di klien).
- Kunci keputusan: penyimpanan tunggal semua-atau-tidak-sama-sekali, dan state input dipertahankan saat penolakan.
