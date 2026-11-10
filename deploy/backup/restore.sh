#!/bin/bash
# Pemulihan: restore.sh /backups/gms-YYYYMMDD-HHMMSS.dump   (DB tujuan dikosongkan dulu — hentikan api/web sebelum menjalankan!)
set -euo pipefail
FILE="${1:?Sebutkan berkas dump}"
pg_restore --clean --if-exists --no-owner --dbname="${PGDATABASE}" "${FILE}"
echo "[restore] selesai dari ${FILE}"
