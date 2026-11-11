# Audit Keamanan: Seluruh Sistem (Backend API, Web BFF, Deploy)

- Tanggal: 2026-11-12
- Cakupan: commit `159d0d9` (branch `uat`). Static review kode, dependensi, konfigurasi deploy.
- Metode: `dotnet list package --vulnerable --include-transitive`, pembacaan kode (autentikasi/sesi, otorisasi endpoint, handler Access, Web BFF, compose, Dockerfile), grep pola berisiko (SQL mentah, MarkupString, rahasia).
- Batasan: bukan penetration test dinamis; tidak menguji TLS/Traefik di VPS nyata; tidak memindai image container.

## Ringkasan

| ID | Tingkat | Temuan | Tiket |
|---|---|---|---|
| SEC-001 | ~~Tinggi~~ **Rendah** (dikoreksi) | Pertahanan berlapis: handler pengguna tidak membatasi pengelolaan role/akun Administrator oleh non-Administrator. Saat ini tidak dapat dieksploitasi karena izin `AccessManagement.*` hanya untuk Administrator | `Bugs/SEC-001-Eskalasi-Hak-Administrator.md` |
| SEC-002 | Sedang | Web tidak mengirim header keamanan (CSP, anti-framing, nosniff, Referrer-Policy) | `Bugs/SEC-002-Header-Keamanan-Web.md` |
| SEC-003 | Sedang | Compose UAT mempublikasikan Web di `0.0.0.0:5001` (HTTP polos) | `Bugs/SEC-003-Port-UAT-Terbuka.md` |
| SEC-004 | Rendah | Ganti password tidak membatasi percobaan "password lama" salah; login tanpa pembatasan per IP | `Bugs/SEC-004-Throttling-Password.md` |
| SEC-005 | Info | Hal yang bergantung pada isolasi jaringan (IP/UA dari body login, ForwardedHeaders, `AllowedHosts`, waktu respons user tak dikenal) | dicatat di laporan |

Tidak ada temuan Kritis. **Koreksi 2026-11-12:** tingkat awal SEC-001 (Tinggi) keliru. Saat membuktikannya dengan tes, API role menolak pemberian `AccessManagement.*` ke role selain Administrator (`IsAssignable=false`), sehingga tidak ada non-Administrator yang dapat memegang izin pengelolaan pengguna lewat jalur normal. Tingkat diturunkan menjadi Rendah (pertahanan berlapis); guard tetap dipasang.

## Status perbaikan (2026-11-12)

| ID | Status | Perbaikan | Bukti |
|---|---|---|---|
| SEC-001 | Selesai | `SystemRoleGuard`: hanya Administrator yang boleh menetapkan role sistem dan mengelola pengguna ber-role sistem (ubah, reset password, buka kunci, akhiri sesi, nonaktif/aktif); non-Administrator tidak dapat mengubah role sendiri; `ICurrentUser.IsAdministrator` | `PrivilegeEscalationTests` (6 tes; izin disisipkan langsung ke DB), uji mutasi: 4 tes gagal saat guard dimatikan |
| SEC-002 | Selesai | `SecurityHeaders` middleware: CSP dengan nonce per permintaan untuk import map (tanpa `unsafe-inline` skrip), `frame-ancestors 'none'`, `nosniff`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` | `SecurityE2ETests` (header, nol pelanggaran CSP pada alur login/MegaMenu/palette/tema, iframe diblokir); 44 E2E lulus |
| SEC-003 | Selesai | UAT dipublikasikan hanya di `127.0.0.1:5001`; `AllowedHosts` dari `UAT_ALLOWED_HOSTS` (UAT) dan `DOMAIN` (prod); `Deployment.md` diperbarui | `docker compose config` kedua lingkungan. **Belum diuji di server nyata** |
| SEC-004 | Selesai | Backend: password lama salah dihitung sebagai kegagalan (lockout, audit, reset saat sukses). Web: rate limit login per IP (`RateLimit:LoginPerMinute`, bawaan 20/menit) dengan pesan di halaman login | `AuthTests` (2 baru), `SecurityE2ETests` (batas per IP); 512 tes API lulus |

## Yang diperiksa dan dinyatakan baik

| Area | Hasil |
|---|---|
| Dependensi | `dotnet list package --vulnerable --include-transitive`: 0 paket rentan di 8 proyek (termasuk pin `System.Security.Cryptography.Pkcs` 10.0.14). |
| SQL injection | Seluruh akses lewat EF Core. Dua pemanggilan SQL mentah (`DeliveryHandlers.cs:384`, `PoLifecycle.cs:61`) berparameter (`ExecuteSqlInterpolated` dan placeholder `{0}`), tanpa penggabungan string. |
| Otorisasi endpoint | 128 endpoint API: seluruhnya memakai `RequirePermission`/`RequireAnyPermission`/`RequireLogin`, kecuali `login` dan `health` (anonim sengaja). Dashboard hanya login, izin diperiksa per widget di `DashboardQueries`. Penegakan di server (final), bukan hanya UI. |
| Sesi | Token acak 256 bit, hanya hash SHA-256 di DB, divalidasi ke DB pada setiap permintaan (pencabutan, nonaktif, perubahan permission berlaku seketika), timeout 30 menit tidak aktif, satu sesi aktif per pengguna, logout mencabut sesi. |
| Login | Pesan galat generik (akun nonaktif sama dengan password salah), lockout 5x/15 menit, audit setiap kegagalan, password sementara tidak dicatat. |
| BFF/cookie | `gms.auth`: HttpOnly, Secure di luar Development, SameSite=Lax, terenkripsi Data Protection (kunci di volume). Token API tidak pernah sampai ke browser. Form Static SSR dilindungi antiforgery. |
| Open redirect | `SafeReturnUrl` hanya menerima jalur lokal (menolak `//`, `/\`, CR/LF, `/login`). |
| Mass assignment | Endpoint memakai record request (DTO), bukan entitas EF; field sensitif (RoleId, IsActive, RowVersion) diatur eksplisit di handler. |
| XSS | Tidak ada `MarkupString`; `AddMarkupContent` hanya untuk literal statis (`ProgressMatrix.razor`). Blazor meng-encode keluaran; JS palette memakai `textContent`/node DOM, bukan `innerHTML`. |
| Kebocoran galat | `ProblemWriter.Map` tidak membocorkan detail internal (pesan generik untuk galat tak terduga/DB). Tidak ada Swagger/OpenAPI terbuka. |
| Rahasia | Tidak ada rahasia di repo; `.env`, `.env.*`, `secrets.json` di `.gitignore` (hanya `.env.example` dilacak); GitHub Secrets hanya untuk SSH; password admin awal lewat environment. |
| Container | Image API/Web berjalan non-root (`USER $APP_UID`); API tidak dipublikasikan di prod (jaringan `backend`), hanya Traefik yang membuka 80/443. |
| Paginasi | `MaxPageSize = 200` mencegah dump tak terbatas. |

## Temuan

### SEC-001 (Rendah setelah koreksi, awalnya Tinggi): Eskalasi hak ke Administrator
`CreateUserHandler` dan `UpdateUserHandler` menerima `RoleId` apa pun, termasuk role sistem Administrator, tanpa memeriksa bahwa pelaku sendiri Administrator. `ResetPasswordHandler` juga tidak membatasi target. Akibat: pengguna dengan role "admin pengguna" (izin `User.Create`/`User.Edit`/`User.ResetPassword`, yang dapat diberikan ke role biasa) dapat (a) membuat pengguna baru ber-role Administrator, (b) mengubah role dirinya sendiri menjadi Administrator, atau (c) mereset password Administrator lalu login sebagai Administrator. Kondisi: harus ada role non-Administrator yang memegang izin tersebut. Awalnya dinilai mungkin lewat matriks izin; pembuktian dengan tes menunjukkan izin `AccessManagement.*` tidak dapat diberikan ke role lain lewat API (hanya lewat suntingan data langsung atau perubahan katalog di masa depan).
Rekomendasi: hanya Administrator yang boleh memberi/mencabut role sistem dan mengelola pengguna ber-role sistem; ubah role diri sendiri ditolak; uji API untuk ketiga skenario.

### SEC-002 (Sedang): Header keamanan Web
Tidak ada CSP, `X-Frame-Options`/`frame-ancestors`, `X-Content-Type-Options`, `Referrer-Policy`. Risiko: clickjacking pada halaman aksi (void, koreksi, tutup periode) dan tanpa lapisan pertahanan tambahan bila ada XSS. Hanya HSTS yang ada.
Rekomendasi: middleware header (atau label Traefik); CSP yang kompatibel Blazor Server (`script-src 'self'`, `connect-src 'self' wss:`, `style-src 'self' 'unsafe-inline'` karena AntDesign menulis gaya inline; `frame-ancestors 'none'`). Uji dengan E2E agar Blazor/AntDesign tidak rusak.

### SEC-003 (Sedang): Port UAT
`deploy/uat/docker-compose.uat.yml` memakai `ports: "5001:8080"` (semua antarmuka). Cookie `Secure` wajib di UAT, jadi akses sah harus lewat proxy TLS; port mentah hanya membuka jalur HTTP tanpa enkripsi dan memungkinkan spoof `X-Forwarded-*` karena `KnownProxies` dikosongkan.
Rekomendasi: `127.0.0.1:5001:8080` dan reverse proxy TLS di host; atau gabungkan ke Traefik seperti prod.

### SEC-004 (Rendah): Throttling
`ChangePasswordHandler` memakai `ChangePasswordAsync`, yang tidak menambah hitungan gagal; pemegang sesi curian dapat menebak password lama tanpa terkunci. Login hanya dibatasi per akun (bisa dipakai mengunci akun orang lain; password spraying lintas akun tidak terbatas).
Rekomendasi: hitung kegagalan password lama lewat `AccessFailedAsync`/lockout; tambah `RateLimiter` per IP pada `/account/login`.

### SEC-005 (Info)
- `LoginRequest.IpAddress`/`UserAgent` dari body dipercaya untuk audit; aman karena API tidak dipublikasikan. Jangan pernah mempublikasikan API langsung.
- `ForwardedHeaders` mengosongkan proxy tepercaya: aman hanya bila Web tidak terjangkau selain lewat proxy (lihat SEC-003).
- `AllowedHosts: "*"`: batasi ke domain UAT/PROD di `appsettings.{UAT,Production}.json`.
- Login untuk pengguna tak dikenal tidak menghitung hash password, sehingga waktu respons dapat membedakan username valid (kecil; sebanding dengan lockout).
- Logout (POST) tanpa token antiforgery eksplisit; dilindungi SameSite=Lax, dampak hanya logout paksa.

## Tindak lanjut
Tiket SEC-001..004 di `docs/Bugs/` sudah diperbaiki (lihat Status perbaikan). Verifikasi ulang: tes otomatis di atas lulus; yang tersisa adalah pengujian di server UAT nyata (TLS, header lewat proxy, port loopback).
