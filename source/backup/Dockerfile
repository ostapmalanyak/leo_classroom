# Backup sidecar: runs on its OWN cron (independent failure domain), dumps both Postgres DBs and the
# Forgejo data, then pushes them offsite via restic (encrypted, deduplicated) with retention pruning.
FROM docker.io/library/alpine:3.23

# The client major version must be >= the server's: pg_dump refuses to dump a newer server than itself, so
# this tracks the postgres image in compose.yaml. Alpine 3.23 is the first release carrying postgresql18.
RUN apk add --no-cache restic postgresql18-client tar gzip tzdata dcron

WORKDIR /opt/backup
COPY backup.sh ./backup.sh
COPY entrypoint.sh ./entrypoint.sh
RUN chmod +x ./backup.sh ./entrypoint.sh

ENTRYPOINT ["./entrypoint.sh"]
