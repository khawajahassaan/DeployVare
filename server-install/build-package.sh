#!/usr/bin/env bash
# Builds server-install/output/DeployVare-Server-<timestamp>.zip for Install-DeployVare.ps1:
#   api/         the API, published for Windows (framework-dependent; the .NET 10 Hosting Bundle runs it)
#   web/         the frontend as static files (the API address is written into config.js at install time)
#   database/    a copy-only backup of the DeployVare database (exact copy: users, clients, servers, settings)
#   installers/  the Installers folder (Hosting Bundle, Memurai, URL Rewrite, SQL Express, AuditVareSchema.bak)
#
# Usage (password in SQLCMDPASSWORD, never on the command line):
#   SQLCMDPASSWORD=... ./server-install/build-package.sh [--sql-server localhost,1433] [--sql-user sa]
#                     [--database DeployVare] [--no-database] [--no-installers]
#
# The zip holds encrypted secrets and the key that decrypts them (appsettings.json). Treat it like a password.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQLCMD="${SQLCMD:-/opt/mssql-tools18/bin/sqlcmd}"
SQL_SERVER="localhost,1433"
SQL_USER="sa"
DATABASE="DeployVare"
WITH_DATABASE=1
WITH_INSTALLERS=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --sql-server) SQL_SERVER="$2"; shift 2 ;;
    --sql-user) SQL_USER="$2"; shift 2 ;;
    --database) DATABASE="$2"; shift 2 ;;
    --no-database) WITH_DATABASE=0; shift ;;
    --no-installers) WITH_INSTALLERS=0; shift ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
OUT_DIR="$ROOT/server-install/output"
mkdir -p "$OUT_DIR"

echo "==> Publishing the API for Windows"
dotnet publish "$ROOT/API/API.csproj" -c Release -r win-x64 --self-contained false -o "$STAGE/api" --nologo -v quiet
# A server install runs as Production; development settings (local connection strings) stay behind
rm -f "$STAGE/api/appsettings.Development.json"

echo "==> Building the frontend as static files"
(cd "$ROOT/web" && rm -rf out && TAURI_BUILD=1 npm run build --silent >/dev/null)
cp -r "$ROOT/web/out" "$STAGE/web"
rm -rf "$ROOT/web/out"

if [[ $WITH_DATABASE -eq 1 ]]; then
  : "${SQLCMDPASSWORD:?Set SQLCMDPASSWORD for $SQL_USER (or pass --no-database)}"
  echo "==> Backing up database $DATABASE (copy-only, compressed)"
  # SQL Server writes the file as its own service account; a pre-created, world-writable file keeps it
  # readable for this user (works for SQL Server on Linux and for a local Windows instance alike)
  BAK_DIR="$(mktemp -d /tmp/dv-pkg-XXXXXX)"
  chmod 777 "$BAK_DIR"
  touch "$BAK_DIR/$DATABASE.bak" && chmod 666 "$BAK_DIR/$DATABASE.bak"
  "$SQLCMD" -S "$SQL_SERVER" -U "$SQL_USER" -C -b -Q \
    "BACKUP DATABASE [$DATABASE] TO DISK = N'$BAK_DIR/$DATABASE.bak' WITH COPY_ONLY, COMPRESSION, CHECKSUM, FORMAT, INIT; RESTORE VERIFYONLY FROM DISK = N'$BAK_DIR/$DATABASE.bak' WITH CHECKSUM;" >/dev/null
  mkdir -p "$STAGE/database"
  cp "$BAK_DIR/$DATABASE.bak" "$STAGE/database/$DATABASE.bak"
  rm -rf "$BAK_DIR"
fi

if [[ $WITH_INSTALLERS -eq 1 && -d "$ROOT/Installers" ]]; then
  echo "==> Adding the Installers folder"
  mkdir -p "$STAGE/installers"
  find "$ROOT/Installers" -maxdepth 1 -type f -exec cp {} "$STAGE/installers/" \;
fi

cp "$ROOT/server-install/Install-DeployVare.ps1" "$STAGE/"
cp "$ROOT/server-install/README.txt" "$STAGE/"

ZIP="$OUT_DIR/DeployVare-Server-$(date +%Y%m%d-%H%M).zip"
(cd "$STAGE" && zip -q -r "$ZIP" .)
echo "==> $ZIP ($(du -h "$ZIP" | cut -f1))"
