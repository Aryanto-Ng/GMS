# SEC-003: Port UAT terbuka di semua antarmuka (Sedang)

- Ditemukan: 2026-11-12. Pemilik: DevOps. Status: **Diperbaiki 2026-11-12** (menunggu verifikasi ulang). Port loopback + AllowedHosts per lingkungan; belum diuji di server nyata.
- Masalah: `deploy/uat/docker-compose.uat.yml` memakai `5001:8080` (0.0.0.0, HTTP).
- Perbaikan: ikat ke `127.0.0.1:5001:8080` dan sediakan reverse proxy TLS di host, atau pakai Traefik; dokumentasikan di `docs/Deployment.md`. Batasi `AllowedHosts` di `appsettings.UAT.json`/`Production.json`.
- Penerimaan: dari luar host, port 5001 tidak terjangkau; akses UAT hanya lewat HTTPS.
