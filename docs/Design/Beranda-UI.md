# Desain UI — Beranda

| Atribut | Nilai |
|---|---|
| FSD | `docs/FSD/Beranda.md` v1.0 |
| Fondasi | `docs/Design/_Foundation-UI.md` |
| Render Mode | **Interactive Server** |
| Mode kerapatan | Comfortable |

---

## 1. Halaman dan Rute

| Halaman | Rute | Permission |
|---|---|---|
| Beranda | `/` | Semua pengguna login |

---

## 2. Tata Letak

Grid 12 kolom, jarak antarkartu 16 px. Kartu dikelompokkan per area dengan judul seksi. Area yang paling relevan dengan permission pengguna tampil lebih dahulu (AC-BR-08). Kartu yang tidak berhak tidak dirender.

```
Selamat datang, Budi                                 Diperbarui 08 Nov 14:30  [⟳ Segarkan]
┌ Aksi Cepat ───────────────────────────────────────────────────────────────────────┐
│ [Input Output] [Kirim/Terima Vendor] [Serah Terima FG] [Serah Terima Material] [Lainnya ▾] │
└───────────────────────────────────────────────────────────────────────────────────┘

PRODUKSI
┌ Output Hari Ini ──────┐ ┌ WO Berjalan ───────────────────────────┐ ┌ Sisa di Vendor ──┐
│ 1.240 pcs             │ │ Released 2 · In Progress 5 · Selesai 1  │ │ 150 pcs          │
│ Cutting 400           │ │ WO-…0004 KMJ-001  ▓▓▓░░ 62%  Target 20 Nov │ │ CV Bordir 150    │
│ Sewing  520           │ │ WO-…0002 CLN-004  ▓▓▓▓░ 88%  Target 18 Nov │ │ [Lihat]          │
│ QC      320   [Lihat] │ │ … (maks 5)                  [Lihat]       │ └──────────────────┘
└───────────────────────┘ └────────────────────────────────────────┘
┌ Belum Diserahterimakan ──────────────────────────┐ ┌ WO Terlambat ─────┐
│ 450 pcs                                           │ │ ⏰ 2 WO           │
│ WO-…0004 · 300   WO-…0002 · 100  …    [Lihat]    │ │ [Lihat]           │
└──────────────────────────────────────────────────┘ └───────────────────┘

PENJUALAN
┌ PO Terlambat ──────────┐ ┌ Jatuh Tempo 7 Hari ─┐ ┌ PO Belum Ada No. Customer ┐ ┌ Kebutuhan Belum Terencana ┐
│ ⏰ 3 PO · 420 pcs       │ │ 5 PO · 1.150 pcs     │ │ ⚠ 2 PO                    │ │ 640 pcs · 4 PO             │
│ PO-…0001 PT Jaya  −3h  │ │ [Lihat]              │ │ [Lihat]                   │ │ [Lihat]                    │
│ … (maks 5)  [Lihat]    │ └──────────────────────┘ └───────────────────────────┘ └────────────────────────────┘

GUDANG
┌ Stok FG ───────────────────────────────────────┐
│ Fisik 2.140 · Teralokasi 640 · Tersedia 1.500   │
│ KMJ-001 · 520   CLN-004 · 410   …   [Lihat]    │
└─────────────────────────────────────────────────┘

PENGGAJIAN
┌ Periode Gaji ───────────────────────────────────┐
│ GJ-20261102-20261108 · Open  (BELUM FINAL)       │
│ 14 pekerja · Rp 18.450.000* · sisa 2 hari        │
│ [Buka]                                          │
└─────────────────────────────────────────────────┘

ADMINISTRASI
┌ Keamanan Akun ──────────────────────────────────┐
│ ⛔ 1 akun terkunci · 4 login gagal (24 jam) [Lihat] │
└─────────────────────────────────────────────────┘
```

### Responsif
| Lebar | Perilaku |
|---|---|
| ≥ 1366 | 3–4 kartu per baris sesuai ukuran kartu (kecil 3 kolom, sedang 6, lebar 9–12) |
| 1024–1365 | 2 kartu per baris |
| < 1024 | 1 kolom |

---

## 3. Spesifikasi Kartu

| Kartu | Ukuran | Isi | Aksi klik |
|---|---|---|---|
| W-01 PO Terlambat | Sedang | Jumlah PO + pcs, 5 PO teratas (kode, customer, selisih hari) | Judul/angka → `/po?terlambat=1`; baris → detail PO |
| W-02 Jatuh Tempo 7 Hari | Kecil | pcs, jumlah PO | `/po?jatuhTempo7=1` |
| W-03 PO Belum Ada No. Customer | Kecil | Jumlah PO | `/po?poCustomerKosong=1` |
| W-04 Kebutuhan Belum Terencana | Kecil | pcs, jumlah PO | `/po?sisaRencana=1` |
| W-05 WO Berjalan | Lebar | Hitung per status, 5 WO (target terdekat) dengan bar progres | `/wo`; baris → detail WO |
| W-06 WO Terlambat | Kecil | Jumlah | `/wo?terlambat=1` |
| W-07 Output Hari Ini | Sedang | Total pcs, rincian per proses | `/produksi/riwayat?hariIni=1` |
| W-08 Sisa di Vendor | Kecil | pcs, per vendor teratas | `/produksi/riwayat?jenis=vendor` |
| W-09 Belum Diserahterimakan | Lebar | pcs + 5 WO terbesar | `/gudang/serah-terima` atau WO |
| W-10 Stok FG | Lebar | Fisik/Teralokasi/Tersedia, 5 FG teratas | `/gudang/stok` |
| W-11 Periode Gaji | Sedang | Periode Open (atau ajakan buat), jumlah pekerja, total sementara, sisa hari | `/gaji/{id}` atau `/gaji` |
| W-12 Keamanan Akun | Kecil | Akun terkunci, login gagal 24 jam | `/admin/pengguna?status=terkunci`, `/admin/audit` |
| W-13 Aksi Cepat | Penuh (strip) | ≤ 6 tombol + "Lainnya" | Form terkait |

Aturan visual:
- Angka utama besar (28–32 px), label kecil di atasnya. Status "terlambat" memakai ikon ⏰ + teks, bukan warna saja.
- Kartu dengan angka nol menampilkan nilai netral ("0 PO terlambat" dengan ikon centang), bukan state kosong, agar "baik-baik saja" terbaca jelas.
- Total periode gaji berasterisk dan chip "Belum final".

---

## 4. State UI

| Kondisi | Tampilan |
|---|---|
| **Loading** | Setiap kartu menampilkan skeleton sesuai bentuknya. Kartu dimuat bertahap (tidak menunggu semuanya) |
| **Kartu error** | Di dalam kartu: ikon, "Tidak dapat memuat data", [Coba lagi] dan traceId kecil. Kartu lain tidak terpengaruh |
| **Kartu kosong** | Daftar kosong: teks netral dengan ikon centang ("Tidak ada PO terlambat") |
| **Tanpa widget** | Sapaan + tautan ke modul yang boleh diakses (AC-BR-03) |
| **Instalasi baru** | Panduan 4 langkah: ① Master Data (Size, Color, Process, FG, Pekerja) ② Customer PO ③ Work Order ④ Input Output. Hanya langkah yang berhak yang berupa tautan |
| **Segarkan** | Tombol berputar, `aria-busy` pada kartu yang sedang dimuat ulang. Konten lama tetap terlihat |
| **Koneksi terputus** | Banner menyambung ulang. Setelah tersambung, kartu dimuat ulang |

---

## 5. Aksesibilitas

- Urutan fokus mengikuti urutan visual per area. Judul seksi adalah heading (`h2`), kartu `h3`.
- Setiap kartu berupa `region` berlabel ("PO Terlambat"). Angka dibaca lengkap dengan satuan ("3 PO terlambat, total 420 pcs").
- Bar progres memiliki `role="progressbar"` + nilai teks. Seluruh kartu dapat dioperasikan keyboard.
- Perubahan setelah Segarkan diumumkan satu kali ("Beranda diperbarui") lewat `aria-live="polite"`.
- Kontras kartu pada tema terang/gelap memenuhi 4,5:1.

---

## 6. Catatan untuk Frontend/Architect

- Satu endpoint ringkasan per widget (dapat dimuat independen, permission dicek per widget).
- Parameter query filter harus sama dengan yang dipakai chip cepat di modul tujuan (`terlambat`, `jatuhTempo7`, `poCustomerKosong`, `sisaRencana`, `hariIni`).
- Zona waktu aplikasi (WIB) untuk perhitungan "hari ini".
