#!/usr/bin/env bash
# Runs on the server (deploy.sh --restore calls it): restores a .bak as the DeployVare database
# in the AuditVare stack's SQL Server. Usage: restore-db.sh /path/to/backup.bak DeployVare
set -euo pipefail
BAK=${1:?backup file}; DB=${2:-DeployVare}
AUDITVARE_DIR=/opt/auditvare-docker
cd "$AUDITVARE_DIR"

sudo mv "$BAK" backups/deployvare-restore.bak
sudo chmod 644 backups/deployvare-restore.bak

# sqlcmd inside the mssql container, with its own SA password (never printed).
SQL() { docker compose exec -T mssql bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b "$@"' _ "$@"; }

LIST=$(SQL -h-1 -W -s'|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK='/backups/deployvare-restore.bak'")
DATA=$(echo "$LIST" | awk -F'|' '$3=="D"{print $1; exit}')
LOG=$(echo "$LIST" | awk -F'|' '$3=="L"{print $1; exit}')
[ -n "$DATA" ] && [ -n "$LOG" ] || { echo "Could not read the file names in the backup"; exit 1; }

SQL -Q "IF DB_ID('$DB') IS NOT NULL ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$DB] FROM DISK='/backups/deployvare-restore.bak' WITH REPLACE, STATS=25,
  MOVE '$DATA' TO '/var/opt/mssql/data/$DB.mdf', MOVE '$LOG' TO '/var/opt/mssql/data/${DB}_log.ldf';
ALTER DATABASE [$DB] SET MULTI_USER;"

sudo rm -f backups/deployvare-restore.bak
echo "Restored $DB."
