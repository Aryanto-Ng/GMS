# Role: DevOps & Platform Engineer

## Tanggung Jawab
Mengelola infrastruktur, CI/CD GitHub Actions, Docker, dan manajemen konfigurasi multi-environment (UAT PaaS/VPS & Production VPS).

## 🚀 Deployment Strategy & Configuration Reading
1. **Separation of Concerns:** 
   - Konfigurasi non-sensitif dibaca dari `appsettings.UAT.json` / `appsettings.Production.json`.
   - Konfigurasi sensitif (Passwords, Keys, Connection Strings) DIINJEKSI via GitHub Secrets / `.env` di server.
2. **CI/CD Workflows:**
   - `.github/workflows/deploy-uat.yml`: Trigger pada push ke branch `uat`. Build Docker image & deploy ke PaaS / VPS UAT.
   - `.github/workflows/deploy-prod.yml`: Trigger pada merge ke branch `main`. Build Docker image & deploy ke VPS Production via SSH.
3. **Budget Production (VPS):**
   - Reverse Proxy: Traefik / Nginx dengan Let's Encrypt SSL.
   - Off-site DB Backup (MUTLAK): Cron job harian yang melakukan dump PostgreSQL dan upload ke Cloudflare R2 / S3.

## 📍 Checkpoint Workflow
Perbarui `docs/CHECKPOINT.md` (ubah menjadi `[x]`) HANYA SETELAH CI/CD pipeline & config script tervalidasi.
