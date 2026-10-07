# Nightly backups for the Coolify deployment: the app database (pg_dump) and ERPNext (database dump + site files).
# Files land in /backups, bind-mounted from the host (/var/backups/autoparts-erp by default).
FROM postgres:16-alpine
RUN apk add --no-cache mariadb-client jq tzdata
COPY deploy/coolify/backup.sh /usr/local/bin/autoparts-backup
RUN chmod 755 /usr/local/bin/autoparts-backup
ENTRYPOINT ["/usr/local/bin/autoparts-backup"]
CMD ["schedule"]
