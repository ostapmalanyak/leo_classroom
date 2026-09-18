#!/bin/sh
# Installs the backup cron from $BACKUP_CRON and runs dcron in the foreground.
set -eu

CRON="${BACKUP_CRON:-0 2 * * *}"
echo "$CRON /opt/backup/backup.sh >> /var/log/backup.log 2>&1" > /etc/crontabs/root

echo "backup: scheduled '$CRON' (independent of the backend)"
touch /var/log/backup.log
# Stream the log so `podman logs backup` shows backup runs.
tail -F /var/log/backup.log &
exec crond -f -l 8
