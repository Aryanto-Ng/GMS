# 🔑 Secrets & Environment Checklist

Aplikasi memakai **sesi opaque** (bukan JWT), sehingga tidak ada secret JWT. Rahasia dibagi dua tempat:

1. **GitHub Secrets** (Settings > Secrets and variables > Actions, per Environment `uat` dan `production`): hanya akses deploy ke server.
2. **File `.env` di server** (`deploy/uat/.env`, `deploy/prod/.env`, tidak pernah di-commit): rahasia aplikasi. Template: `deploy/uat/.env.example` dan `deploy/prod/.env.example`.

Tidak ada kredensial, IP server, atau kunci SSH di repository.

## GitHub Secrets

### UAT di Render + Supabase
Tidak ada GitHub Secret untuk UAT: Render mengambil kode dari GitHub langsung. Rahasia diisi di dashboard Render (variabel `sync: false` pada `render.yaml`), lihat `docs/Deployment-Render-Supabase.md`:
- [ ] API `ConnectionStrings__Default`: string Session pooler Supabase (format Npgsql)
- [ ] API `Seed__AdminPassword`: password awal admin
- [ ] API `Gateway__Key` dan Web `Api__GatewayKey`: kunci gerbang acak, nilai sama
- [ ] Web `Api__BaseUrl`, `AllowedHosts`: URL/host layanan (non-rahasia)

### Environment: `uat` (hanya bila UAT di VPS sendiri; tidak ada workflow otomatis lagi)
- [ ] `UAT_VPS_*`: tidak diperlukan kecuali Anda menambahkan workflow SSH sendiri

### Environment: `production`
- [ ] `PROD_VPS_HOST`
- [ ] `PROD_VPS_USER`
- [ ] `PROD_VPS_SSH_KEY`
- [ ] `PROD_VPS_PATH`
- [ ] `PROD_VPS_PORT`: (opsional)
- [ ] Aktifkan **required reviewers** pada Environment `production`

## File `.env` di server

### UAT (`deploy/uat/.env`)
- [ ] `UAT_DB_CONNECTION`: connection string PostgreSQL UAT (mis. Neon/Supabase/VPS UAT)
- [ ] `UAT_ADMIN_BOOTSTRAP_PASSWORD`: password awal akun `admin` (wajib diganti saat login pertama)
- [ ] `IMAGE_TAG`: (opsional) diisi otomatis oleh pipeline

### Production (`deploy/prod/.env`)
- [ ] `DOMAIN`, `ACME_EMAIL`: host publik dan email Let's Encrypt
- [ ] `PROD_DB_HOST`, `PROD_DB_PORT`, `PROD_DB_NAME`, `PROD_DB_USER`
- [ ] `PROD_DB_PASSWORD`: password PostgreSQL production
- [ ] `PROD_ADMIN_BOOTSTRAP_PASSWORD`: password awal akun `admin`
- [ ] `PROD_S3_ENDPOINT`, `PROD_S3_BUCKET`, `PROD_S3_PROVIDER`: tujuan backup off-site
- [ ] `PROD_S3_ACCESS_KEY`: access key Cloudflare R2 / AWS S3 (khusus bucket backup)
- [ ] `PROD_S3_SECRET_KEY`: secret key Cloudflare R2 / AWS S3
- [ ] `COMPOSE_PROFILES=with-db`: hanya bila database dijalankan sebagai container (bukan bare machine)
- [ ] `COMFI_*`, `KONVEKSI_*`: nama dan alamat untuk kop surat jalan dan slip gaji

## Akses image (GHCR)
- [ ] Paket `gms-api` dan `gms-web` dibuat publik, **atau** jalankan `docker login ghcr.io` di server dengan PAT `read:packages`.
