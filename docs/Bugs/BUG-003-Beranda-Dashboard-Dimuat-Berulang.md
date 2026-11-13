# BUG-003 — Beranda memuat 12 widget dashboard berulang kali (log Web penuh permintaan ke :5222)

- **Ditemukan:** 2026-11-14, Ary melihat log frontend selalu penuh `GET http://localhost:5222/api/v1/dashboard/*` setiap login.
- **Penguji:** QA. **Keparahan:** Sedang (pemuatan ganda, pasti terjadi) + Tinggi (siklus tanpa henti, sekali teramati, belum direproduksi).
- **Status:** Poin 1 dan 3 DIPERBAIKI (2026-11-14); poin 2 (siklus tanpa henti) belum direproduksi, dipantau. Menunggu uji ulang Ary.

## Temuan
1. **Pemuatan ganda (pasti, direproduksi).** Halaman berjalan Interactive Server dengan prerender bawaan (`App.razor`: `InteractiveServer`) dan tanpa `PersistentComponentState`. Setiap kunjungan penuh ke Beranda (termasuk setelah login) memuat 12 widget dua kali: sekali saat prerender, sekali saat sirkuit interaktif; chip ringkasan menambah `po-late`, `po-no-customer-number`, `wo-late` satu kali lagi. Pada E2E bersih: 3x per widget (4x untuk tiga widget chip) setelah login + satu kali buka `/`. Ini yang Ary lihat "selalu" setelah login. Perilaku ini wajar secara teknis tetapi menggandakan beban API/DB.
2. **Siklus tanpa henti (teramati, BELUM direproduksi).** Log Web lokal Ary (`gms-web2.log`, 769 MB, 6,7 juta baris) berisi ±34.000 siklus berurutan; satu siklus = 12 permintaan dashboard (48 baris, tiap 48 baris berulang). Kemungkinan itu terjadi saat BUG-002 (50 permintaan/dtk × 12 widget menghabiskan slot Postgres). Hitungan terhenti (tidak ada pertumbuhan 10 dtk terakhir), jadi pemicunya sudah berhenti. Pada stack E2E bersih TIDAK ada permintaan dashboard baru selama 10 dtk idle (tes `DashboardLoadE2ETests`). Pemicu yang belum bisa dikesampingkan: sirkuit gagal lalu halaman dimuat ulang otomatis berulang (ReconnectModal) saat API membalas galat (53300), atau banyak tab browser.
3. **Log berisik.** `System.Net.Http.HttpClient` berlevel Information di Dev/UAT mencatat 4 baris per permintaan; ini membuat log Web membesar sangat cepat.

## Saran untuk Frontend (QA tidak memperbaiki kode)
- Hindari muat ganda: `@rendermode` tanpa prerender (`new InteractiveServerRenderMode(prerender: false)`) untuk halaman data, atau simpan hasil lewat `PersistentComponentState`.
- Tambahkan pengaman: widget tidak dimuat ulang oleh sirkuit baru dalam jendela singkat / hentikan pemuatan ulang otomatis saat galat server berulang (pemulihan dengan tombol, bukan otomatis).
- Set `Logging:LogLevel:System.Net.Http.HttpClient` = `Warning` pada `appsettings.Development.json`/UAT.

## Kriteria lulus
`DashboardLoadE2ETests` tetap lulus; tambah pengecekan jumlah permintaan per widget setelah login ≤ 1 (+ chip) setelah perbaikan ganda; tidak ada permintaan baru saat idle.

## Catatan
Folder log `%TEMP%\gms-web2.log` (769 MB) adalah keluaran proses Web yang dijalankan QA/Claude dengan `Api__BaseUrl=http://localhost:5222`; aman dihapus setelah proses Web itu dihentikan.

## Resolusi Frontend (2026-11-14)
- **Poin 1:** `App.razor` memakai `InteractiveServerRenderMode(prerender: false)`; halaman terautentikasi tidak butuh prerender/SEO. Beranda kini memuat tiap widget 1x (chip: maks. 2x karena memakai widget yang sama).
- **Poin 3:** `appsettings.json` Web: `Logging:LogLevel:System.Net.Http.HttpClient` = `Warning` (E2E tetap menaikkannya ke Information lewat environment untuk menghitung permintaan).
- **Poin 2:** tidak ada perubahan kode karena pemicunya belum diketahui; pada stack bersih tidak ada permintaan saat idle. Jika terulang, simpan `gms-web.log` beserta waktunya dan kirim ke QA. Dengan prerender mati, pemuatan ulang halaman tidak lagi menjalankan pemuatan data di sisi server dua kali.
- **Tes:** `DashboardLoadE2ETests` diperketat (maks. 1x per widget per kunjungan, 0 permintaan saat idle). E2E 49/49, bUnit 452/452.
