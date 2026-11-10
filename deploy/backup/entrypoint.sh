#!/bin/bash
# Memasang cron harian untuk backup.sh, lalu menjalankan crond di foreground.
set -euo pipefail
echo "${BACKUP_SCHEDULE:-0 2 * * *} /usr/local/bin/backup.sh >> /proc/1/fd/1 2>&1" > /etc/crontabs/root
echo "[backup] jadwal: ${BACKUP_SCHEDULE:-0 2 * * *} (TZ=$TZ), retensi lokal ${BACKUP_RETENTION_DAYS:-14} hari"
exec crond -f -l 8
