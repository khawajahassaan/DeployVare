#!/usr/bin/env bash
# Builds DeployVare on this laptop and runs it on the AuditVare Docker server, by IP and port (no domain).
# Usage: ./deploy.sh [--restore path/to/DeployVare.bak] [--skip-build]
#   --restore: restore that backup as the DeployVare database (first install, or to replace it).
#   --skip-build: ship and restart what was built last time.
# The database goes into the AuditVare stack's SQL Server (/opt/auditvare-docker), so that stack must be running.
set -euo pipefail

HOST=${HOST:-103.31.81.131}
SSH_PORT=${SSH_PORT:-1051}
WEB_PORT=${WEB_PORT:-8088}
API_PORT=${API_PORT:-8089}
DB_NAME=${DB_NAME:-DeployVare}
SERVER=ubuntu@$HOST
REMOTE_DIR=/opt/deployvare
AUDITVARE_DIR=/opt/auditvare-docker
WEB_URL=http://$HOST:$WEB_PORT
API_URL=http://$HOST:$API_PORT/api

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
KIT="$REPO/deploy/server"
CTX="$HOME/deployvare-docker-build"
SSH_OPTS="-p $SSH_PORT -o ControlMaster=auto -o ControlPath=$HOME/.ssh/cm-%r@%h:%p -o ControlPersist=20m"

BACKUP=""; BUILD=1
while [ $# -gt 0 ]; do
  case "$1" in
    --restore) BACKUP="${2:?--restore needs a .bak file}"; shift 2 ;;
    --skip-build) BUILD=0; shift ;;
    *) echo "Usage: $0 [--restore file.bak] [--skip-build]"; exit 1 ;;
  esac
done
[ -z "$BACKUP" ] || [ -f "$BACKUP" ] || { echo "No such file: $BACKUP"; exit 1; }

step() { echo; echo "==> $*"; }
mkdir -p "$HOME/.ssh"

if [ "$BUILD" = 1 ]; then
  rm -rf "$CTX"; mkdir -p "$CTX/api" "$CTX/web"

  step "Publishing the API"
  dotnet publish "$REPO/API/API.csproj" -c Release -r linux-x64 --self-contained false -o "$CTX/api/app" --nologo -v quiet

  step "Building the web app"
  (cd "$REPO/web" && npm ci --no-audit --no-fund --loglevel=error && NEXT_OUTPUT=standalone npm run build)
  # The standalone build holds server.js and its modules; Next nests it under web/ when it traces
  # from the repo root (there is a lockfile there too). That folder is the site; static files and
  # public/ go beside server.js.
  SRC="$REPO/web/.next/standalone"
  [ -f "$SRC/server.js" ] || SRC="$SRC/web"
  [ -f "$SRC/server.js" ] || { echo "server.js not found in the standalone build"; exit 1; }
  cp -r "$SRC" "$CTX/web/site"
  mkdir -p "$CTX/web/site/.next"
  cp -r "$REPO/web/.next/static" "$CTX/web/site/.next/static"
  cp -r "$REPO/web/public" "$CTX/web/site/public"
fi
[ -d "$CTX/api/app" ] && [ -d "$CTX/web/site" ] || { echo "Nothing built yet; run without --skip-build"; exit 1; }

cp "$KIT/docker-compose.yml" "$CTX/"
cp "$KIT/api/Dockerfile" "$CTX/api/"
cp "$KIT/web/Dockerfile" "$CTX/web/"
echo "window.__DEPLOYVARE_CONFIG__ = { apiUrl: \"$API_URL\" };" > "$CTX/config.js"

step "Preparing $SERVER (sudo password may be asked once)"
ssh -t $SSH_OPTS "$SERVER" "
  sudo mkdir -p $REMOTE_DIR && sudo chown \$USER: $REMOTE_DIR
  if command -v ufw >/dev/null && sudo ufw status | grep -q active; then
    sudo ufw allow $WEB_PORT/tcp >/dev/null && sudo ufw allow $API_PORT/tcp >/dev/null
  fi"

step "Uploading"
rsync -az -e "ssh $SSH_OPTS" "$CTX/docker-compose.yml" "$CTX/config.js" "$SERVER:$REMOTE_DIR/"
ssh $SSH_OPTS "$SERVER" "mkdir -p $REMOTE_DIR/api $REMOTE_DIR/web"
rsync -az -e "ssh $SSH_OPTS" "$CTX/api/Dockerfile" "$SERVER:$REMOTE_DIR/api/"
rsync -az -e "ssh $SSH_OPTS" "$CTX/web/Dockerfile" "$SERVER:$REMOTE_DIR/web/"
rsync -az --delete -e "ssh $SSH_OPTS" "$CTX/api/app/" "$SERVER:$REMOTE_DIR/api/app/"
rsync -az --delete -e "ssh $SSH_OPTS" "$CTX/web/site/" "$SERVER:$REMOTE_DIR/web/site/"

# .env: the SA password is the AuditVare stack's (never printed); ports and URLs are rewritten every deploy.
ssh $SSH_OPTS "$SERVER" "
  set -e
  SA=\$(grep -E '^MSSQL_SA_PASSWORD=' $AUDITVARE_DIR/.env | head -1 | cut -d= -f2-)
  [ -n \"\$SA\" ] || { echo 'MSSQL_SA_PASSWORD not found in $AUDITVARE_DIR/.env'; exit 1; }
  umask 077
  printf '%s\n' \"MSSQL_SA_PASSWORD=\$SA\" 'DB_NAME=$DB_NAME' 'WEB_PORT=$WEB_PORT' 'API_PORT=$API_PORT' 'WEB_URL=$WEB_URL' > $REMOTE_DIR/.env"

if [ -n "$BACKUP" ]; then
  step "Restoring $(basename "$BACKUP") as database $DB_NAME"
  rsync -az -e "ssh $SSH_OPTS" "$KIT/restore-db.sh" "$SERVER:$REMOTE_DIR/"
  rsync -az --progress -e "ssh $SSH_OPTS" "$BACKUP" "$SERVER:/tmp/deployvare-restore.bak"
  # The API is stopped first so nothing holds the database during the restore.
  ssh -t $SSH_OPTS "$SERVER" "cd $REMOTE_DIR && (docker compose stop api 2>/dev/null || true) && bash restore-db.sh /tmp/deployvare-restore.bak $DB_NAME"
fi

step "Building the images and starting DeployVare"
ssh $SSH_OPTS "$SERVER" "cd $REMOTE_DIR && docker compose build && docker compose up -d"

step "Done. Open $WEB_URL  (API $API_URL)"
echo "    Logs: ssh -p $SSH_PORT $SERVER 'cd $REMOTE_DIR && docker compose logs -f api'"
echo "    Ports $WEB_PORT and $API_PORT also need to be open in the server's OpenStack security group."
