# SEC-004: Throttling password (Rendah)

- Ditemukan: 2026-11-12. Pemilik: Backend + Frontend. Status: Terbuka.
- Masalah: (1) `ChangePasswordHandler` tidak menghitung kegagalan "password lama" (tebakan tanpa batas dari sesi curian); (2) login hanya dibatasi per akun, tanpa batas per IP.
- Perbaikan: panggil `AccessFailedAsync` + cek lockout pada ganti password; `RateLimiter` per IP untuk `/account/login` di Web.
- Penerimaan: tes API (5 password lama salah -> akun terkunci), tes Web (batas per IP -> 429/pesan).
