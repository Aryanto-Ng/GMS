#!/bin/bash
# Dump PostgreSQL (format custom, terkompresi) -> /backups lalu kirim ke luar server (S3/R2) via rclone.
# Gagal kirim off-site = exit non-zero (terlihat di log container). Dump lokal tetap disimpan.
set -euo pipefail
STAMP=$(date +%Y%m%d-%H%M%S)
FILE="/backups/gms-${STAMP}.dump"

echo "[backup] ${STAMP} pg_dump -> ${FILE}"
pg_dump --format=custom --compress=6 --no-owner --file="${FILE}.partial"
mv "${FILE}.partial" "${FILE}"
pg_restore --list "${FILE}" > /dev/null   # verifikasi dump terbaca

echo "[backup] kirim off-site -> ${BACKUP_REMOTE_PATH}"
rclone copyto "${FILE}" "${BACKUP_REMOTE_PATH}/$(basename "${FILE}")" --s3-no-check-bucket

find /backups -name 'gms-*.dump' -mtime +"${BACKUP_RETENTION_DAYS:-14}" -delete
echo "[backup] selesai $(du -h "${FILE}" | cut -f1)"
