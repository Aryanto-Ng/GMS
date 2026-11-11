# Deployment (UAT + Production)

Alur branch: pengembangan di branch **`uat`** → UAT lulus dan disetujui → merge ke **`main`** (Production).

| Branch | Workflow | Tujuan | Tag image |
|---|---|---|---|
| PR, `uat`, `main` | `.github/workflows/ci.yml` | Build + semua tes (unit, bUnit, integrasi API, E2E Playwright) | tidak ada |
| `uat` | `.github/workflows/deploy-uat.yml` | Tes → image GHCR → deploy ke server UAT | `uat`, `<sha12>` |
| `main` | `.github/workflows/deploy-prod.yml` | Tes → image GHCR → deploy ke VPS Production (Environment `production` dengan required reviewers) | `latest`, `<sha12>` |

## Arsitektur

```
Internet ──443──> traefik ──> web:8080 ──> api:8080 ──> PostgreSQL
                                (BFF)       (internal, tanpa port publik)
```
Hanya Web yang publik. API tidak dipublikasikan dan tidak memakai JWT: Web menyimpan token sesi opaque di cookie terenkripsi `gms.auth`.

Berkas:
- `deploy/Dockerfile.api`, `deploy/Dockerfile.web`: image (konteks build: root repo).
- `deploy/prod/docker-compose.prod.yml` + `.env.example`: Traefik + Let's Encrypt, Web, API, backup, dan PostgreSQL opsional (profil `with-db`).
- `deploy/uat/docker-compose.uat.yml` + `.env.example`: Web + API, database eksternal (`UAT_DB_CONNECTION`), data demo otomatis.
- `deploy/backup/`: `backup.sh`, `restore.sh`, cron harian (container `backup`).
- Konfigurasi non-sensitif: `src/Backend/Gms.Api/appsettings.{UAT,Production}.json` dan `src/Frontend/Gms.Web/appsettings.{UAT,Production}.json` (termasuk `Api:BaseUrl`). Rahasia hanya lewat `.env` di server atau GitHub Secrets (`docs/Secrets-Checklist.md`).

Layout di server (di bawah `*_VPS_PATH`, mis. `/opt/gms`): `prod/` (compose + `.env` + `data/`), `backup/` (konteks build backup), `uat/` (compose + `.env` + `data/`). Pipeline menyalin compose dan skrip, tidak pernah menyentuh `.env` atau `data/`.

## 1. Prasyarat server (sekali)
```bash
# Ubuntu 22.04/24.04
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker deploy            # user khusus deploy, bukan root
sudo mkdir -p /opt/gms && sudo chown deploy: /opt/gms
```
Production: arahkan A record `DOMAIN` ke IP VPS; buka port 80 dan 443 (`ufw allow 80,443/tcp`) serta SSH. UAT: pasang reverse proxy dengan TLS di depan port 5001 (cookie login berstatus Secure di lingkungan non-Development).

## 2. Konfigurasi
- GitHub: isi Secrets sesuai `docs/Secrets-Checklist.md` (Environment `uat` dan `production`), aktifkan required reviewers pada `production`.
- Server: salin `.env.example` menjadi `.env` di `deploy/prod/` atau `deploy/uat/` (setelah pipeline pertama menyalin folder, atau buat manual) dan isi semua nilai.
- Izin folder kunci cookie (Web berjalan sebagai user non-root, UID 1654):
```bash
mkdir -p /opt/gms/prod/data/web-keys && sudo chown 1654:1654 /opt/gms/prod/data/web-keys   # idem untuk uat/data/web-keys
```
- Image GHCR privat: `docker login ghcr.io` dengan PAT `read:packages`, atau jadikan paket publik.

## 3. Deploy pertama
Dorong ke `uat` (otomatis) atau jalankan manual di server:
```bash
cd /opt/gms/prod
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps        # semua "healthy"
```
Migrasi dan seed dijalankan API saat start. Buka `https://DOMAIN`, login `admin` dengan password bootstrap dan ganti password.

## 4. Database

**Docker (data di host):** `PROD_DB_HOST=db` dan `COMPOSE_PROFILES=with-db` di `.env`; data di `deploy/prod/data/postgres` (wajib di-mount ke host).

**Bare machine / eksternal:** hapus profil `with-db`, lalu pasang PostgreSQL di host:
```bash
sudo apt install -y postgresql-17 postgresql-contrib       # citext & btree_gist (trusted) dibuat oleh migrasi
sudo -u postgres psql -c "CREATE USER gms WITH PASSWORD '<PROD_DB_PASSWORD>';"
sudo -u postgres psql -c "CREATE DATABASE gms OWNER gms;"
# postgresql.conf : listen_addresses = 'localhost,172.17.0.1'
# pg_hba.conf     : host gms gms 172.16.0.0/12 scram-sha-256
sudo systemctl restart postgresql
```
Set `PROD_DB_HOST=172.17.0.1` (IP gateway host, bukan nama container). Container `backup` memakai host yang sama.

## 5. Backup & pemulihan
Container `backup` menjalankan `pg_dump` (format custom) pada `BACKUP_SCHEDULE` (default 02:00 WIB), memverifikasi dump (`pg_restore --list`), mengunggahnya ke `PROD_S3_BUCKET` lewat rclone, dan menghapus dump lokal lebih tua dari `BACKUP_RETENTION_DAYS`. Atur lifecycle rule pada bucket untuk retensi off-site.

```bash
cd /opt/gms/prod
docker compose -f docker-compose.prod.yml exec backup backup.sh                 # backup manual
docker compose -f docker-compose.prod.yml logs backup                           # hasil jadwal
# Pemulihan (hentikan aplikasi dulu):
docker compose -f docker-compose.prod.yml stop web api
docker compose -f docker-compose.prod.yml exec backup restore.sh /backups/gms-YYYYMMDD-HHMMSS.dump
docker compose -f docker-compose.prod.yml start api web
```
Dump yang hanya ada off-site: unduh ke `data/backups/` lalu jalankan `restore.sh`. **Uji pemulihan minimal sekali per kuartal.**

## 6. Operasional
- **Rollback:** `IMAGE_TAG=<sha12 lama> docker compose -f docker-compose.prod.yml up -d`.
- **Sesi login** bertahan setelah restart/deploy karena kunci Data Protection ada di `data/web-keys`; jangan dihapus.
- **Zona waktu** `Asia/Jakarta` (`tzdata` ada di image): memengaruhi "hari ini" dan tutup periode payroll.
- **PDF di Linux:** Liberation Sans (OFL) disalin ke `/app/Fonts` saat build image.
- **Stateless:** tidak ada berkas unggahan di disk (impor CSV diproses di memori). Bila fitur unggah ditambahkan, pakai Object Storage (R2/S3).
- **Log:** `docker compose -f <file> logs -f api web`. Healthcheck: API `/api/v1/health`, Web `/healthz`.
- **PostgreSQL mayor** (17 → 18) memerlukan dump/restore.

## 7. Uji lokal tanpa TLS
Build image (`docker build -f deploy/Dockerfile.api -t gms-api:local .`, idem web), lalu jalankan compose dengan override yang mengganti `image:` ke tag `:local`, memetakan `web` ke port lokal, dan tanpa service `traefik`.
