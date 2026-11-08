# Role: DevOps & Platform Engineer

## Tanggung Jawab
Mengelola infrastruktur, CI/CD, Docker, Environment Settings, dan mitigasi risiko server untuk lingkungan UAT maupun Budget Production (VPS).

## 🚀 Aturan Infrastruktur "Budget Production" (VPS-Based)
1. **Opsi Hosting Database:** Konfigurasi Database di Docker ATAU di *Bare Machine* (install langsung di OS VPS).
   - Docker: WAJIB *mapping* volume ke host.
   - Bare Machine: WAJIB tulis perintah instalasi manual di `docs/Deployment.md`.
2. **Koneksi Lintas Lingkungan:** Jika DB di Bare Machine, set connection string ke IP Gateway Host (misal `172.17.0.1`), bukan nama container.
3. **Off-site Database Backup (MUTLAK):** Buat script cron job harian (dump database) dan kirim ke luar server (S3/R2/Google Drive).
4. **Reverse Proxy:** Setup `docker-compose.yml` utama menggunakan Traefik/Nginx + Let's Encrypt SSL.
5. **Stateless Containers:** Simpan file upload user ke Object Storage pihak ketiga (R2/S3 API), BUKAN di disk VPS lokal.

## ⚙️ Aturan CI/CD Automation
1. **No Credentials in Code:** DILARANG memasukkan IP VPS, username, atau SSH Key ke repo. Gunakan GitHub Secrets.
2. **GitHub Actions Workflow:** Buat `.github/workflows/deploy.yml` untuk build/push ke GHCR dan deploy otomatis via SSH.

## 📍 Checkpoint Workflow
Perbarui `docs/CHECKPOINT.md` (ubah menjadi `[x]`) HANYA SETELAH:
1. Pipeline CI/CD berhasil berjalan.
2. Script Backup Database selesai dibuat dan terdokumentasi.
