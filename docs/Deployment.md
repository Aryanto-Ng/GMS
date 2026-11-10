# Deployment (VPS "Budget Production")

Arsitektur: **Traefik** (TLS Let's Encrypt) → **Web** (Blazor, publik) → **API** (internal) → **PostgreSQL 17** (Docker, data di bind-mount host). **Backup** harian off-site (S3/R2) berjalan sebagai container sendiri.

```
Internet ──443──> traefik ──> web:8080 ──> api:8080 ──> db:5432
                                              (jaringan "backend", API & DB tidak dipublikasikan)
```

Berkas: `deploy/Dockerfile.api`, `deploy/Dockerfile.web`, `deploy/docker-compose.yml`, `deploy/.env.example`, `deploy/backup/*`, `.github/workflows/{ci,deploy}.yml`.

## 1. Prasyarat VPS (sekali)
```bash
# Ubuntu 22.04/24.04
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker deploy            # user khusus deploy (bukan root)
sudo mkdir -p /opt/gms && sudo chown deploy: /opt/gms
```
DNS: arahkan A record `DOMAIN` ke IP VPS. Buka port 80 dan 443 (firewall: `ufw allow 80,443/tcp`, dan SSH).

## 2. Konfigurasi
Di `/opt/gms`: salin `deploy/.env.example` menjadi `.env` lalu isi semua nilai (**jangan di-commit**).

| Variabel | Keterangan |
|---|---|
| `DOMAIN`, `ACME_EMAIL` | Host publik dan email Let's Encrypt |
| `DB_PASSWORD` | Password PostgreSQL (acak, panjang) |
| `ADMIN_BOOTSTRAP_PASSWORD` | Password awal akun `admin`; **wajib diganti saat login pertama** |
| `COMFI_*`, `KONVEKSI_*` | Nama/alamat pada kop surat jalan & slip gaji |
| `BACKUP_S3_*`, `BACKUP_SCHEDULE`, `BACKUP_RETENTION_DAYS` | Tujuan off-site dan jadwal (default 02:00 WIB, simpan lokal 14 hari) |

Folder data (dibuat otomatis oleh Docker; beri izin untuk kunci Web yang berjalan sebagai user non-root):
```bash
mkdir -p data/web-keys data/backups data/postgres data/letsencrypt
sudo chown 1654:1654 data/web-keys        # UID default image .NET (APP_UID)
```

## 3. Deploy pertama (manual)
```bash
cd /opt/gms
docker login ghcr.io                       # jika paket GHCR privat (PAT dengan read:packages)
docker compose up -d
docker compose ps                          # semua "healthy"
```
Migrasi dan seed dijalankan API saat start (`Database__MigrateOnStartup/SeedOnStartup=true`). Buka `https://DOMAIN`, login `admin` + `ADMIN_BOOTSTRAP_PASSWORD`, ganti password.

## 4. CI/CD (GitHub Actions)
- `ci.yml`: build + seluruh tes (unit, bUnit, integrasi API, E2E Playwright) pada setiap PR dan push ke `main`. Runner `ubuntu-latest` sudah menyediakan Docker (Testcontainers).
- `deploy.yml`: push ke `main` → tes → build & push image `ghcr.io/<owner>/gms-api|gms-web` (tag SHA + `latest`) → SSH ke VPS lalu `docker compose pull && up -d`.

Secrets repository (Settings → Secrets and variables → Actions; **tidak ada kredensial di repo**):
`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` (private key khusus deploy), `VPS_PORT` (opsional), `VPS_PATH` (mis. `/opt/gms`).
Environment GitHub `production` dapat diberi *required reviewers* sebagai gerbang persetujuan.

Rollback: `IMAGE_TAG=<sha lama> docker compose up -d`.

## 5. Backup & pemulihan
Container `backup` menjalankan `pg_dump` (format custom) pada `BACKUP_SCHEDULE`, memverifikasi dump (`pg_restore --list`), menyalinnya ke `BACKUP_S3_BUCKET` lewat rclone, dan menghapus dump lokal lebih tua dari `BACKUP_RETENTION_DAYS`.
Atur **lifecycle rule** pada bucket (mis. hapus setelah 90 hari) untuk retensi off-site.

```bash
docker compose exec backup backup.sh                       # backup manual sekarang
docker compose logs backup                                 # lihat hasil jadwal
# Pemulihan (hentikan aplikasi dulu agar tidak ada koneksi aktif):
docker compose stop web api
docker compose exec backup restore.sh /backups/gms-YYYYMMDD-HHMMSS.dump
docker compose start api web
# Dump yang hanya ada off-site: unduh dulu ke data/backups/ (rclone/konsol R2) lalu jalankan restore.sh.
```
**Uji pemulihan minimal sekali per kuartal** pada VPS/DB cadangan; backup yang tak pernah diuji dianggap belum ada.

## 6. Operasional
- **Sesi login** bertahan setelah restart/deploy: kunci Data Protection ada di `data/web-keys` (jangan dihapus).
- **Zona waktu** `Asia/Jakarta` (`tzdata` ada di image) — memengaruhi "hari ini" dan tutup periode payroll.
- **PDF di Linux** memakai Liberation Sans (lisensi OFL) yang disalin ke `/app/Fonts` saat build image.
- **Stateless**: aplikasi tidak menyimpan berkas unggahan di disk (impor CSV diproses di memori). Bila fitur unggah berkas ditambahkan kelak, gunakan Object Storage (R2/S3), bukan disk VPS.
- **Log**: `docker compose logs -f api web`. Healthcheck: API `/api/v1/health`, Web `/healthz`.
- **Update PostgreSQL mayor** (17 → 18) memerlukan dump/restore; jangan ubah tag image tanpa rencana.

## 7. Alternatif: PostgreSQL di host (Bare Machine)
Bila DB dipasang langsung di OS VPS, hapus service `db` dari compose dan:
```bash
sudo apt install -y postgresql-17 postgresql-contrib-17           # ekstensi citext & btree_gist (trusted) dibuat migrasi
sudo -u postgres psql -c "CREATE USER gms WITH PASSWORD '<DB_PASSWORD>';"
sudo -u postgres psql -c "CREATE DATABASE gms OWNER gms;"
# /etc/postgresql/17/main/postgresql.conf : listen_addresses = 'localhost,172.17.0.1'
# /etc/postgresql/17/main/pg_hba.conf     : host gms gms 172.16.0.0/12 scram-sha-256
sudo systemctl restart postgresql
```
Set connection string API ke IP gateway host, bukan nama container: `Host=172.17.0.1;Port=5432;Database=gms;Username=gms;Password=...`, dan arahkan `PGHOST=172.17.0.1` pada service `backup`.

## 8. Uji lokal tanpa TLS
Untuk mencoba stack di laptop: build image (`docker build -f deploy/Dockerfile.api -t gms-api:local .`, idem web), lalu jalankan compose dengan override yang mengganti `image:` ke tag `:local` dan memetakan `web` ke port lokal, tanpa service `traefik`.
