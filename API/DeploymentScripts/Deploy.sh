#!/usr/bin/env bash
# DeployVare Linux deployment kit: Ubuntu / Debian, and the Red Hat family (RHEL, Rocky Linux, AlmaLinux, CentOS Stream).
#
# Run it from the extracted kit folder as root:
#     sudo bash Deploy.sh
#
# The kit type comes from deploy-config.json next to this script:
#   Api       .NET backend: Kestrel as a systemd service behind Nginx/Apache (installs .NET and Redis when missing)
#   Frontend  static frontend files served by Nginx/Apache (SPA fallback to index.html, optional /api route)
#   Database  runs DbMigrator with the kit's SQL scripts against an existing SQL Server database
#
# Options:
#   --yes, -y           do not ask anything (used by DeployVare's one-click deployment)
#   --run-migrations    run DbMigrator even when the kit does not do so by default
#   --skip-migrations   never run DbMigrator
#
# Reads only deploy-config.json and the kit's files. Never prints passwords. A full log is written to deployment.log.

set -Eeuo pipefail

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$KIT_DIR/deploy-config.json"
LOG_FILE="$KIT_DIR/deployment.log"
ASSUME_YES=false
MIGRATIONS_OVERRIDE=""
BACKUP_FILE=""
PKG_UPDATED=false
OS_FAMILY=""
REDIS_SERVICE=""

usage() { sed -n '2,18p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

for arg in "$@"; do
    case "$arg" in
        --yes|-y) ASSUME_YES=true ;;
        --run-migrations) MIGRATIONS_OVERRIDE=true ;;
        --skip-migrations) MIGRATIONS_OVERRIDE=false ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $arg (see --help)"; exit 2 ;;
    esac
done

# ---- Output -----------------------------------------------------------------------------------------------------

step() { echo "==> $*"; }
ok()   { echo "[OK] $*"; }
info() { echo "    $*"; }
warn() { echo "[WARN] $*"; }

fail() {
    echo "[FAILED] $*"
    if [[ -n "$BACKUP_FILE" ]]; then
        echo "         The previous version is saved in $BACKUP_FILE"
        echo "         Restore it with: sudo tar -xzf '$BACKUP_FILE' -C '$(dirname "$DEPLOY_PATH")'"
    fi
    exit 1
}

trap 'echo "[FAILED] Unexpected error on line $LINENO: $BASH_COMMAND"' ERR

confirm() {
    [[ "$ASSUME_YES" == true ]] && return 0
    local answer
    read -r -p "$1 [y/N] " answer
    [[ "$answer" =~ ^[Yy]$ ]]
}

# ---- Prerequisites ----------------------------------------------------------------------------------------------

assert_environment() {
    [[ $EUID -eq 0 ]] || { echo "Run this script as root: sudo bash Deploy.sh"; exit 1; }
    command -v systemctl >/dev/null 2>&1 || { echo "systemd is required (systemctl was not found)."; exit 1; }
    [[ -f "$CONFIG" ]] || { echo "deploy-config.json was not found next to Deploy.sh. Run the script from the extracted kit folder."; exit 1; }
    detect_os
}

# debian: Ubuntu/Debian (apt). rhel: RHEL, Rocky, AlmaLinux, CentOS Stream, Oracle Linux (dnf).
# Package names, the web server's user, its configuration folders and the firewall differ between the two.
detect_os() {
    OS_NAME=$( (. /etc/os-release 2>/dev/null && echo "${PRETTY_NAME:-${ID:-Linux}}") || echo Linux)
    if command -v apt-get >/dev/null 2>&1; then
        OS_FAMILY=debian
        PKG_TOOL=apt-get
        DEFAULTS_DIR=/etc/default
    elif command -v dnf >/dev/null 2>&1 || command -v yum >/dev/null 2>&1; then
        OS_FAMILY=rhel
        PKG_TOOL=$(command -v dnf >/dev/null 2>&1 && echo dnf || echo yum)
        DEFAULTS_DIR=/etc/sysconfig
    else
        echo "This kit supports Ubuntu/Debian (apt-get) and Red Hat family servers (dnf); neither was found on $OS_NAME."
        exit 1
    fi
}

# User the web server runs as: it reads the frontend files
web_user() {
    if [[ "$OS_FAMILY" == debian ]]; then echo www-data
    elif [[ "$WEB_SERVER" == apache ]]; then echo apache
    else echo nginx
    fi
}

pkg_installed() {
    if [[ "$OS_FAMILY" == debian ]]; then dpkg -s "$1" >/dev/null 2>&1; else rpm -q "$1" >/dev/null 2>&1; fi
}

pkg_install() {
    local missing=() package
    for package in "$@"; do
        pkg_installed "$package" || missing+=("$package")
    done
    [[ ${#missing[@]} -eq 0 ]] && return 0
    if [[ "$OS_FAMILY" == debian ]]; then
        if [[ "$PKG_UPDATED" == false ]]; then
            step "Updating the package lists (apt-get update)"
            DEBIAN_FRONTEND=noninteractive apt-get update -qq
            PKG_UPDATED=true
        fi
        step "Installing ${missing[*]}"
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends "${missing[@]}" >/dev/null
    else
        step "Installing ${missing[*]}"
        "$PKG_TOOL" install -y -q "${missing[@]}" >/dev/null
    fi
}

# Commands rather than packages: minimal RHEL images ship curl-minimal, which provides curl but is not "curl"
require_commands() {
    local command
    for command in "$@"; do
        command -v "$command" >/dev/null 2>&1 || pkg_install "$command"
    done
}

selinux_enabled() {
    command -v getenforce >/dev/null 2>&1 && [[ "$(getenforce 2>/dev/null)" != "Disabled" ]]
}

ensure_semanage() {
    command -v semanage >/dev/null 2>&1 || pkg_install policycoreutils-python-utils || true
    command -v semanage >/dev/null 2>&1
}

# A file moved back from /tmp keeps the temporary label; give it its folder's default again
relabel() {
    if selinux_enabled; then restorecon "$1" || true; fi
    return 0
}

# Persistent SELinux label for a path (pattern); restorecon applies it
selinux_label() {
    ensure_semanage || { warn "semanage is not available: SELinux label $1 for $2 was not set."; return 0; }
    semanage fcontext -a -t "$1" "$2" 2>/dev/null || semanage fcontext -m -t "$1" "$2"
}

# ---- Configuration ----------------------------------------------------------------------------------------------

cfg() { jq -r "$1 // empty" "$CONFIG"; }

read_config() {
    require_commands jq curl tar
    pkg_install ca-certificates

    KIT_TYPE=$(cfg .kitType)
    APP_NAME=$(cfg .applicationName)
    BUILD_NUMBER=$(cfg .buildNumber)
    SITE_NAME=$(cfg .siteName)
    SITE_HOST=$(cfg .siteHostName)
    DEPLOY_PATH=$(cfg .deploymentPath)
    WEB_SERVER=$(cfg .webServer); WEB_SERVER=${WEB_SERVER:-nginx}
    CERT_PATH=$(cfg .certificatePath)
    CERT_KEY_PATH=$(cfg .certificateKeyPath)
    SERVICE_NAME=$(cfg .serviceName)
    SERVICE_USER=$(cfg .serviceUser); SERVICE_USER=${SERVICE_USER:-www-data}
    APP_ENTRY=$(cfg .appEntry)
    SELF_CONTAINED=$(jq -r '.selfContained // false' "$CONFIG")
    APP_PORT=$(jq -r '.appPort // 5000' "$CONFIG")
    DOTNET_MAJOR=$(jq -r '.requiredDotNetMajor // 10' "$CONFIG")
    INSTALL_REDIS=$(jq -r '.installRedis // false' "$CONFIG")
    REDIS_PORT=$(jq -r '.redisPort // 6379' "$CONFIG")
    APPSETTINGS_GENERATED=$(jq -r '.appSettingsGenerated // false' "$CONFIG")
    API_PROXY_PATH=$(cfg .apiProxyPath)
    API_PROXY_UPSTREAM=$(cfg .apiProxyUpstream)
    RUN_MIGRATIONS=$(jq -r '.runMigrations // false' "$CONFIG")
    HAS_SCRIPTS=$(jq -r '.hasScripts // false' "$CONFIG")
    DBMIGRATOR_FOLDER=$(cfg .dbMigratorFolder); DBMIGRATOR_FOLDER=${DBMIGRATOR_FOLDER:-dbmigrator}
    BACKUP_PATH=$(cfg .backupPath); BACKUP_PATH=${BACKUP_PATH:-/var/backups/deployvare}
    mapfile -t KEEP_PATHS < <(jq -r '.keepOnServerPaths[]? // empty' "$CONFIG")

    [[ -n "$MIGRATIONS_OVERRIDE" ]] && RUN_MIGRATIONS=$MIGRATIONS_OVERRIDE
    [[ "$KIT_TYPE" == "Database" && -z "$MIGRATIONS_OVERRIDE" ]] && RUN_MIGRATIONS=true

    validate_config
}

validate_name() { [[ "$2" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$ ]] || fail "$1 '$2' may only contain letters, digits, '.', '_' and '-'."; }

validate_absolute_path() {
    local label="$1" path="$2"
    [[ "$path" == /* ]] || fail "$label must be an absolute path (starting with /): $path"
    [[ "$path" != *..* && "$path" != *"//"* && "$path" =~ ^[A-Za-z0-9._/-]+$ ]] || fail "$label contains unsupported characters: $path"
}

validate_config() {
    case "$KIT_TYPE" in Api|Frontend|Database) ;; *) fail "kitType must be Api, Frontend or Database (found '$KIT_TYPE')." ;; esac
    case "$WEB_SERVER" in nginx|apache) ;; *) fail "webServer must be nginx or apache (found '$WEB_SERVER')." ;; esac
    validate_absolute_path "backupPath" "$BACKUP_PATH"
    [[ "$KIT_TYPE" == "Database" ]] && return 0

    validate_name "siteName" "$SITE_NAME"
    validate_absolute_path "deploymentPath" "$DEPLOY_PATH"
    case "${DEPLOY_PATH%/}" in
        ""|/bin|/boot|/dev|/etc|/home|/lib|/lib64|/opt|/proc|/root|/run|/sbin|/srv|/sys|/tmp|/usr|/var|/var/www|/var/lib|/var/log)
            fail "deploymentPath '$DEPLOY_PATH' is a system folder. Use a folder of its own, e.g. /var/www/$SITE_NAME." ;;
    esac
    if [[ -n "$SITE_HOST" && ! "$SITE_HOST" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$ ]]; then
        fail "siteHostName '$SITE_HOST' must be a host name or IP address (no http:// or path)."
    fi
    [[ "$KIT_TYPE" == "Frontend" && -z "$SITE_HOST" ]] && fail "siteHostName is required for a frontend kit (a domain name or the server's IP)."
    if [[ -n "$CERT_PATH" || -n "$CERT_KEY_PATH" ]]; then
        [[ -n "$CERT_PATH" && -n "$CERT_KEY_PATH" ]] || fail "Set both certificatePath and certificateKeyPath, or neither (HTTP only)."
        [[ -f "$CERT_PATH" ]] || fail "Certificate not found on this server: $CERT_PATH"
        [[ -f "$CERT_KEY_PATH" ]] || fail "Certificate key not found on this server: $CERT_KEY_PATH"
    fi

    if [[ "$KIT_TYPE" == "Api" ]]; then
        validate_name "serviceName" "$SERVICE_NAME"
        validate_name "serviceUser" "$SERVICE_USER"
        [[ -n "$APP_ENTRY" && "$APP_ENTRY" != */* ]] || fail "appEntry must be the file name of the application (e.g. MyApp.dll)."
        [[ "$APP_PORT" =~ ^[0-9]+$ && "$APP_PORT" -ge 1024 && "$APP_PORT" -le 65535 ]] || fail "appPort must be between 1024 and 65535."
        [[ -f "$KIT_DIR/app/$APP_ENTRY" ]] || fail "app/$APP_ENTRY is missing from the kit."
    fi
    if [[ "$KIT_TYPE" == "Frontend" && -n "$API_PROXY_UPSTREAM" ]]; then
        # POSIX bracket expression: a literal ] must come first ([] also allows IPv6 such as http://[::1]:5000)
        local upstream_pattern='^https?://[][A-Za-z0-9.:-]+/?$'
        [[ "$API_PROXY_UPSTREAM" =~ $upstream_pattern ]] || fail "apiProxyUpstream must look like http://127.0.0.1:5000"
        [[ "${API_PROXY_PATH:-/api}" =~ ^/[A-Za-z0-9._/-]*$ ]] || fail "apiProxyPath must start with / (e.g. /api)."
    fi
    [[ "$KIT_TYPE" == "Database" || -d "$KIT_DIR/app" ]] || fail "The kit has no app/ folder."
}

show_plan() {
    echo
    echo "DeployVare Linux deployment: $APP_NAME build #$BUILD_NUMBER ($KIT_TYPE kit) on $OS_NAME"
    case "$KIT_TYPE" in
        Api)
            echo "  Files:        $DEPLOY_PATH"
            echo "  Service:      $SERVICE_NAME (systemd, user $SERVICE_USER, http://127.0.0.1:$APP_PORT)"
            echo "  Start:        $([[ "$SELF_CONTAINED" == true ]] && echo "$APP_ENTRY (self-contained)" || echo "dotnet $APP_ENTRY (.NET $DOTNET_MAJOR)")"
            echo "  Web server:   $([[ -n "$SITE_HOST" ]] && echo "$WEB_SERVER site '$SITE_NAME' for $SITE_HOST" || echo "none (reached through the frontend's API route)")"
            echo "  Redis:        $([[ "$INSTALL_REDIS" == true ]] && echo "installed when missing (port $REDIS_PORT)" || echo "not managed")"
            echo "  Migrations:   $([[ "$RUN_MIGRATIONS" == true ]] && echo "DbMigrator runs after the files are copied" || echo "not run")"
            ;;
        Frontend)
            echo "  Files:        $DEPLOY_PATH"
            echo "  Web server:   $WEB_SERVER site '$SITE_NAME' for $SITE_HOST"
            [[ -n "$API_PROXY_UPSTREAM" ]] && echo "  API route:    ${API_PROXY_PATH:-/api} -> $API_PROXY_UPSTREAM"
            ;;
        Database)
            echo "  DbMigrator:   $DBMIGRATOR_FOLDER/DbMigrator.dll with sqlscripts/"
            echo "  Connection:   ConnectionStrings in appsettings.json next to this script"
            ;;
    esac
    [[ "$KIT_TYPE" != "Database" ]] && echo "  HTTPS:        $([[ -n "$CERT_PATH" ]] && echo "$CERT_PATH" || echo "not configured (HTTP only)")"
    selinux_enabled && echo "  SELinux:      $(getenforce): file labels and the proxy permission are set"
    echo
    confirm "Continue?" || { echo "Cancelled. Nothing was changed."; exit 0; }
}

# ---- Server components ------------------------------------------------------------------------------------------

ensure_dotnet() {
    [[ "$KIT_TYPE" == "Frontend" ]] && return 0
    if [[ "$KIT_TYPE" == "Api" && "$SELF_CONTAINED" == true ]]; then
        ok "Self-contained build: no .NET runtime needed"
        return 0
    fi

    local framework="Microsoft.AspNetCore.App"
    [[ "$KIT_TYPE" == "Database" ]] && framework="Microsoft.NETCore.App"
    if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -q "^$framework $DOTNET_MAJOR\."; then
        ok ".NET $DOTNET_MAJOR runtime ($framework) is installed"
        return 0
    fi

    step "Installing the ASP.NET Core $DOTNET_MAJOR runtime"
    if [[ "$OS_FAMILY" == rhel ]]; then
        # Red Hat ships .NET in its own repositories (installed to /usr/lib64/dotnet, labelled for SELinux)
        if pkg_install "aspnetcore-runtime-$DOTNET_MAJOR.0" 2>/dev/null \
            && dotnet --list-runtimes 2>/dev/null | grep -q "^$framework $DOTNET_MAJOR\."; then
            ok ".NET $DOTNET_MAJOR runtime installed from the $OS_NAME repositories"
            return 0
        fi
        info "aspnetcore-runtime-$DOTNET_MAJOR.0 is not in this server's repositories; using Microsoft's installer"
        pkg_install libicu
    else
        # .NET needs ICU; the package name carries the version (libicu70, libicu74, ...)
        local icu
        icu=$(apt-cache pkgnames libicu 2>/dev/null | grep -E '^libicu[0-9]+$' | sort -V | tail -1 || true)
        pkg_install ${icu:+"$icu"} libssl3 2>/dev/null || pkg_install ${icu:+"$icu"}
    fi

    # Into the existing .NET folder when there is one, so other applications keep working
    local root=/usr/share/dotnet
    if command -v dotnet >/dev/null 2>&1; then root=$(dirname "$(readlink -f "$(command -v dotnet)")"); fi
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel "$DOTNET_MAJOR.0" --runtime aspnetcore --install-dir "$root" >/dev/null
    rm -f /tmp/dotnet-install.sh
    [[ -e /usr/bin/dotnet ]] || ln -s "$root/dotnet" /usr/bin/dotnet
    if selinux_enabled; then
        # systemd may only start programs labelled as executables
        selinux_label bin_t "$root/dotnet"
        restorecon "$root/dotnet"
    fi

    dotnet --list-runtimes 2>/dev/null | grep -q "^$framework $DOTNET_MAJOR\." || fail ".NET $DOTNET_MAJOR could not be installed."
    ok ".NET $DOTNET_MAJOR runtime installed in $root"
}

ensure_web_server() {
    [[ "$KIT_TYPE" == "Database" ]] && return 0
    [[ "$KIT_TYPE" == "Api" && -z "$SITE_HOST" ]] && return 0

    if [[ "$WEB_SERVER" == "nginx" ]]; then
        pkg_install nginx
        systemctl enable --now nginx >/dev/null 2>&1
        ok "Nginx is installed and running"
    elif [[ "$OS_FAMILY" == debian ]]; then
        pkg_install apache2
        a2enmod proxy proxy_http proxy_wstunnel headers rewrite ssl >/dev/null
        systemctl enable --now apache2 >/dev/null 2>&1
        ok "Apache is installed and running"
    else
        # httpd loads the proxy, headers and rewrite modules by default; mod_ssl adds HTTPS
        pkg_install httpd
        [[ -n "$CERT_PATH" ]] && pkg_install mod_ssl
        systemctl enable --now httpd >/dev/null 2>&1
        ok "Apache (httpd) is installed and running"
    fi

    # Open HTTP/HTTPS when a firewall is on: ufw (Ubuntu) or firewalld (Red Hat)
    if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
        ufw allow "$([[ "$WEB_SERVER" == "nginx" ]] && echo "Nginx Full" || echo "Apache Full")" >/dev/null
        ok "Firewall (ufw) allows HTTP and HTTPS"
    elif command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld; then
        firewall-cmd --permanent --add-service=http --add-service=https >/dev/null
        firewall-cmd --reload >/dev/null
        ok "Firewall (firewalld) allows HTTP and HTTPS"
    fi

    # SELinux stops the web server from opening connections, so it could not forward to the app without this
    if selinux_enabled; then
        setsebool -P httpd_can_network_connect 1
        ok "SELinux allows $WEB_SERVER to forward requests (httpd_can_network_connect)"
    fi
}

ensure_redis() {
    [[ "$KIT_TYPE" == "Api" && "$INSTALL_REDIS" == true ]] || return 0
    local conf cli name=Redis
    if [[ "$OS_FAMILY" == debian ]]; then
        pkg_install redis-server
        REDIS_SERVICE=redis-server; conf=/etc/redis/redis.conf; cli=redis-cli
    elif pkg_installed redis || { ! pkg_installed valkey && pkg_install redis 2>/dev/null; }; then
        REDIS_SERVICE=redis; cli=redis-cli
        conf=/etc/redis/redis.conf; [[ -f "$conf" ]] || conf=/etc/redis.conf
    else
        # RHEL 10 ships Valkey (the open-source fork of Redis, same protocol) instead of Redis
        pkg_install valkey
        REDIS_SERVICE=valkey; conf=/etc/valkey/valkey.conf; cli=valkey-cli; name=Valkey
    fi
    if [[ -f "$conf" ]] && ! grep -qE "^port $REDIS_PORT\$" "$conf"; then
        sed -i -E "s/^port .*/port $REDIS_PORT/" "$conf"
        systemctl restart "$REDIS_SERVICE"
    fi
    systemctl enable --now "$REDIS_SERVICE" >/dev/null 2>&1
    "$cli" -p "$REDIS_PORT" ping 2>/dev/null | grep -q PONG || fail "$name is not answering on port $REDIS_PORT."
    ok "$name is running on port $REDIS_PORT"
}

# ---- Application files ------------------------------------------------------------------------------------------

backup_current() {
    if [[ ! -d "$DEPLOY_PATH" || -z "$(ls -A "$DEPLOY_PATH" 2>/dev/null)" ]]; then
        info "First deployment to $DEPLOY_PATH (nothing to back up)"
        return 0
    fi
    mkdir -p "$BACKUP_PATH"
    chmod 700 "$BACKUP_PATH"
    BACKUP_FILE="$BACKUP_PATH/${SITE_NAME}_$(date +%Y%m%d_%H%M%S).tar.gz"
    step "Backing up the current version"
    tar -czf "$BACKUP_FILE" -C "$(dirname "$DEPLOY_PATH")" "$(basename "$DEPLOY_PATH")"
    # Keep the five most recent backups of this site
    ls -1t "$BACKUP_PATH/${SITE_NAME}_"*.tar.gz 2>/dev/null | tail -n +6 | xargs -r rm -f
    ok "Backup saved to $BACKUP_FILE"
}

stop_service() {
    [[ "$KIT_TYPE" == "Api" ]] || return 0
    if systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
        step "Stopping $SERVICE_NAME"
        systemctl stop "$SERVICE_NAME"
    fi
}

publish_files() {
    step "Copying the application files to $DEPLOY_PATH"
    local staging
    staging=$(mktemp -d)
    cp -a "$KIT_DIR/app/." "$staging/"
    if [[ "$KIT_TYPE" == "Api" && -d "$KIT_DIR/sqlscripts" ]]; then
        cp -a "$KIT_DIR/sqlscripts" "$staging/sqlscripts"
    fi

    # Files the server keeps (its own appsettings.json, upload folders, ...)
    local relative
    for relative in "${KEEP_PATHS[@]}"; do
        [[ -z "$relative" || "$relative" == /* || "$relative" == *..* ]] && continue
        if [[ -e "$DEPLOY_PATH/$relative" && -e "$staging/$relative" ]]; then
            rm -rf "${staging:?}/$relative"
            info "Keeping the server's copy of $relative"
        fi
    done

    mkdir -p "$DEPLOY_PATH"
    cp -a "$staging/." "$DEPLOY_PATH/"
    rm -rf "$staging"
    ok "Application files copied"
}

has_placeholders() { [[ -f "$1" ]] && grep -qE '<(SQL_SERVER|SQL_LOGIN|SQL_PASSWORD|DATABASE_NAME)>' "$1"; }

review_appsettings() {
    [[ "$KIT_TYPE" == "Api" ]] || return 0
    local settings="$DEPLOY_PATH/appsettings.json"
    [[ -f "$settings" ]] || { warn "$settings does not exist. The application may need one."; return 0; }
    has_placeholders "$settings" || return 0

    warn "$settings still contains placeholder values such as <SQL_PASSWORD>."
    if [[ "$ASSUME_YES" == false ]] && [[ -t 0 ]] && confirm "Edit it now with ${EDITOR:-nano}?"; then
        command -v "${EDITOR:-nano}" >/dev/null 2>&1 || pkg_install nano
        "${EDITOR:-nano}" "$settings"
    fi
    has_placeholders "$settings" && warn "Edit it later with: sudo nano $settings  then: sudo systemctl restart $SERVICE_NAME"
    return 0
}

set_permissions() {
    step "Setting file ownership and permissions"
    if [[ "$KIT_TYPE" == "Api" ]]; then
        if ! id "$SERVICE_USER" >/dev/null 2>&1; then
            useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
            info "Created system user $SERVICE_USER"
        fi
        # The service user owns its files (it may write logs or uploads); nobody else can read them
        chown -R "$SERVICE_USER:$SERVICE_USER" "$DEPLOY_PATH"
        find "$DEPLOY_PATH" -type d -exec chmod 750 {} +
        find "$DEPLOY_PATH" -type f -exec chmod 640 {} +
        [[ "$SELF_CONTAINED" == true ]] && chmod 750 "$DEPLOY_PATH/$APP_ENTRY"
        ok "Owner $SERVICE_USER, folders 750, files 640"
    else
        # Static files: the web server only reads them
        local user
        user=$(web_user)
        chown -R "$user:$user" "$DEPLOY_PATH"
        find "$DEPLOY_PATH" -type d -exec chmod 755 {} +
        find "$DEPLOY_PATH" -type f -exec chmod 644 {} +
        ok "Owner $user, folders 755, files 644"
    fi
    set_selinux_labels
}

# Copied files keep the label of the temporary folder they came from, which the web server may not read and
# systemd may not start. Label the site as web content and a self-contained app as a program.
set_selinux_labels() {
    selinux_enabled || return 0
    if [[ "$DEPLOY_PATH" != /var/www/* ]]; then
        selinux_label httpd_sys_content_t "$DEPLOY_PATH(/.*)?"
    fi
    if [[ "$KIT_TYPE" == "Api" && "$SELF_CONTAINED" == true ]]; then
        selinux_label bin_t "$DEPLOY_PATH/$APP_ENTRY"
    fi
    restorecon -R "$DEPLOY_PATH"
    ok "SELinux labels set on $DEPLOY_PATH"
}

# ---- Database ---------------------------------------------------------------------------------------------------

connection_string() {
    # appsettings.json may contain // comments, which jq does not accept
    sed -E 's#^[[:space:]]*//.*$##' "$1" | jq -r '(.ConnectionStrings // {}) | to_entries | (.[0].value // empty)'
}

run_migrations() {
    [[ "$RUN_MIGRATIONS" == true ]] || { [[ "$KIT_TYPE" == "Api" ]] && info "Database migrations: not run"; return 0; }
    [[ "$HAS_SCRIPTS" == true ]] || { info "This build has no SQL scripts; nothing to migrate"; return 0; }

    local migrator scripts settings
    if [[ "$KIT_TYPE" == "Database" ]]; then
        migrator="$KIT_DIR/$DBMIGRATOR_FOLDER/DbMigrator.dll"
        settings="$KIT_DIR/appsettings.json"
    else
        migrator="$DEPLOY_PATH/DbMigrator.dll"
        settings="$DEPLOY_PATH/appsettings.json"
    fi
    scripts="$KIT_DIR/sqlscripts"
    [[ -f "$migrator" ]] || fail "DbMigrator.dll was not found ($migrator)."
    [[ -d "$scripts" ]] || fail "The sqlscripts folder is missing from the kit."
    [[ -f "$settings" ]] || fail "$settings was not found; it holds the database connection string."
    has_placeholders "$settings" && fail "Edit the connection string in $settings first (it still has <...> values)."

    local connection
    connection=$(connection_string "$settings")
    [[ -n "$connection" ]] || fail "No ConnectionStrings entry was found in $settings."

    step "Running DbMigrator ($(find "$scripts" -type f -name '*.sql' | wc -l) SQL script(s) in the kit)"
    if ! dotnet "$migrator" "$connection" "$scripts"; then
        fail "DbMigrator failed. Nothing after the failing script was applied."
    fi
    ok "Database is up to date"
}

# ---- systemd service --------------------------------------------------------------------------------------------

install_service() {
    [[ "$KIT_TYPE" == "Api" ]] || return 0
    local unit="/etc/systemd/system/$SERVICE_NAME.service" exec_start
    if [[ "$SELF_CONTAINED" == true ]]; then
        exec_start="$DEPLOY_PATH/$APP_ENTRY"
    else
        exec_start="$(readlink -f "$(command -v dotnet)") $DEPLOY_PATH/$APP_ENTRY"
    fi

    step "Writing the systemd service $unit"
    cat > "$unit" <<UNIT
# Managed by DeployVare: this file is rewritten on every deployment.
# Put your own settings in an override instead:  sudo systemctl edit $SERVICE_NAME
# Secrets can go in $DEFAULTS_DIR/$SERVICE_NAME as KEY=value lines (read when present).
[Unit]
Description=$APP_NAME API ($SERVICE_NAME)
After=network-online.target$([[ -n "$REDIS_SERVICE" ]] && echo " $REDIS_SERVICE.service")
Wants=network-online.target

[Service]
Type=simple
User=$SERVICE_USER
Group=$SERVICE_USER
WorkingDirectory=$DEPLOY_PATH
ExecStart=$exec_start
Restart=always
RestartSec=10
KillSignal=SIGINT
TimeoutStopSec=30
SyslogIdentifier=$SERVICE_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:$APP_PORT
Environment=DOTNET_NOLOGO=1
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
EnvironmentFile=-$DEFAULTS_DIR/$SERVICE_NAME

[Install]
WantedBy=multi-user.target
UNIT
    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME" >/dev/null 2>&1
    ok "Service $SERVICE_NAME starts automatically at boot"
}

start_service() {
    [[ "$KIT_TYPE" == "Api" ]] || return 0
    step "Starting $SERVICE_NAME"
    systemctl restart "$SERVICE_NAME"

    # Running, and answering HTTP on its port (any status code counts: the app is listening)
    local attempt code=""
    for attempt in $(seq 1 30); do
        if ! systemctl is-active --quiet "$SERVICE_NAME"; then
            sleep 1
            continue
        fi
        code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://127.0.0.1:$APP_PORT/" || true)
        [[ -n "$code" && "$code" != "000" ]] && break
        sleep 1
    done

    if ! systemctl is-active --quiet "$SERVICE_NAME" || [[ -z "$code" || "$code" == "000" ]]; then
        echo "---- last 40 lines of: journalctl -u $SERVICE_NAME ----"
        journalctl -u "$SERVICE_NAME" -n 40 --no-pager || true
        fail "$SERVICE_NAME did not start listening on port $APP_PORT within 30 seconds."
    fi
    ok "$SERVICE_NAME is running (HTTP $code on 127.0.0.1:$APP_PORT)"
}

# ---- Web server site --------------------------------------------------------------------------------------------

proxy_headers_nginx() {
    cat <<'NGINX'
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $dv_connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
NGINX
}

nginx_locations() {
    if [[ "$KIT_TYPE" == "Api" ]]; then
        echo "    client_max_body_size 500m;"
        echo "    location / {"
        echo "        proxy_pass http://127.0.0.1:$APP_PORT;"
        proxy_headers_nginx
        echo "    }"
        return
    fi

    cat <<NGINX
    root $DEPLOY_PATH;
    index index.html;

    # Single-page app: unknown paths load index.html, which routes in the browser
    location / {
        try_files \$uri \$uri/ /index.html;
    }
    # Always fetch the current index.html and runtime config.js
    location = /index.html { add_header Cache-Control "no-store"; }
    location = /config.js  { add_header Cache-Control "no-store"; }
    location /assets/ {
        expires 30d;
        add_header Cache-Control "public";
        try_files \$uri =404;
    }
NGINX
    if [[ -n "$API_PROXY_UPSTREAM" ]]; then
        echo "    client_max_body_size 500m;"
        echo "    location ${API_PROXY_PATH:-/api}/ {"
        echo "        proxy_pass ${API_PROXY_UPSTREAM%/};"
        proxy_headers_nginx
        echo "    }"
    fi
}

write_nginx_site() {
    # Ubuntu/Debian: sites-available + a link in sites-enabled. Red Hat: one file in conf.d.
    local available="/etc/nginx/conf.d/$SITE_NAME.conf" enabled="" previous=""
    if [[ -d /etc/nginx/sites-available && -d /etc/nginx/sites-enabled ]]; then
        available="/etc/nginx/sites-available/$SITE_NAME"
        enabled="/etc/nginx/sites-enabled/$SITE_NAME"
    fi
    [[ -f "$available" ]] && { previous=$(mktemp); cp -a "$available" "$previous"; }

    # Shared once for every DeployVare site: WebSocket upgrade header for proxied apps
    cat > /etc/nginx/conf.d/deployvare-websocket.conf <<'NGINX'
# Managed by DeployVare
map $http_upgrade $dv_connection_upgrade {
    default upgrade;
    ''      close;
}
NGINX

    {
        echo "# Managed by DeployVare: rewritten on every deployment of $SITE_NAME."
        if [[ -n "$CERT_PATH" ]]; then
            cat <<NGINX
server {
    listen 80;
    listen [::]:80;
    server_name $SITE_HOST;
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name $SITE_HOST;
    ssl_certificate     $CERT_PATH;
    ssl_certificate_key $CERT_KEY_PATH;
    ssl_protocols TLSv1.2 TLSv1.3;
NGINX
        else
            cat <<NGINX
server {
    listen 80;
    listen [::]:80;
    server_name $SITE_HOST;
NGINX
        fi
        nginx_locations
        echo "}"
    } > "$available"

    [[ -n "$enabled" ]] && ln -sfn "$available" "$enabled"
    if ! nginx -t >/dev/null 2>&1; then
        nginx -t || true
        if [[ -n "$previous" ]]; then mv "$previous" "$available"; relabel "$available"; else rm -f "$available" ${enabled:+"$enabled"}; fi
        fail "The Nginx configuration test failed; the previous site configuration was restored."
    fi
    [[ -n "$previous" ]] && rm -f "$previous"
    systemctl reload nginx
    ok "Nginx site $SITE_NAME is live for $SITE_HOST"
}

# " upgrade=websocket" when Apache supports it (2.4.47+). RHEL 8 has 2.4.37: plain proxying still works there,
# WebSockets through Apache do not.
apache_ws_option() {
    local version
    version=$(apachectl -v 2>/dev/null | sed -nE 's#.*Apache/([0-9]+\.[0-9]+\.[0-9]+).*#\1#p' | head -1 || true)
    if [[ -n "$version" ]] && printf '%s\n%s\n' 2.4.47 "$version" | sort -V -C; then echo " upgrade=websocket"; fi
    return 0
}

apache_body() {
    local ws
    ws=$(apache_ws_option)
    if [[ "$KIT_TYPE" == "Api" ]]; then
        cat <<APACHE
    ProxyPreserveHost On
    ProxyPass        / http://127.0.0.1:$APP_PORT/$ws timeout=300
    ProxyPassReverse / http://127.0.0.1:$APP_PORT/
    LimitRequestBody 524288000
APACHE
        return
    fi
    cat <<APACHE
    DocumentRoot $DEPLOY_PATH
    <Directory $DEPLOY_PATH>
        Options -Indexes
        AllowOverride None
        Require all granted
        FallbackResource /index.html
    </Directory>
    <FilesMatch "^(index\.html|config\.js)$">
        Header set Cache-Control "no-store"
    </FilesMatch>
APACHE
    if [[ -n "$API_PROXY_UPSTREAM" ]]; then
        local path="${API_PROXY_PATH:-/api}" upstream="${API_PROXY_UPSTREAM%/}"
        cat <<APACHE
    ProxyPreserveHost On
    ProxyPass        $path/ $upstream$path/$ws timeout=300
    ProxyPassReverse $path/ $upstream$path/
APACHE
    fi
}

write_apache_site() {
    # Ubuntu/Debian: sites-available, enabled with a2ensite. Red Hat: one file in /etc/httpd/conf.d.
    local available="/etc/httpd/conf.d/$SITE_NAME.conf" service=httpd previous=""
    if [[ "$OS_FAMILY" == debian ]]; then
        available="/etc/apache2/sites-available/$SITE_NAME.conf"
        service=apache2
    fi
    [[ -f "$available" ]] && { previous=$(mktemp); cp -a "$available" "$previous"; }
    {
        echo "# Managed by DeployVare: rewritten on every deployment of $SITE_NAME."
        echo "<VirtualHost *:80>"
        echo "    ServerName $SITE_HOST"
        if [[ -n "$CERT_PATH" ]]; then
            echo "    Redirect permanent / https://$SITE_HOST/"
            echo "</VirtualHost>"
            echo
            echo "<VirtualHost *:443>"
            echo "    ServerName $SITE_HOST"
            echo "    SSLEngine on"
            echo "    SSLCertificateFile    $CERT_PATH"
            echo "    SSLCertificateKeyFile $CERT_KEY_PATH"
            echo "    RequestHeader set X-Forwarded-Proto \"https\""
        else
            echo "    RequestHeader set X-Forwarded-Proto \"http\""
        fi
        apache_body
        echo "</VirtualHost>"
    } > "$available"

    [[ "$OS_FAMILY" == debian ]] && a2ensite "$SITE_NAME" >/dev/null
    if ! apachectl configtest >/dev/null 2>&1; then
        apachectl configtest || true
        if [[ -n "$previous" ]]; then
            mv "$previous" "$available"
            relabel "$available"
        else
            [[ "$OS_FAMILY" == debian ]] && { a2dissite "$SITE_NAME" >/dev/null 2>&1 || true; }
            rm -f "$available"
        fi
        fail "The Apache configuration test failed; the previous site configuration was restored."
    fi
    [[ -n "$previous" ]] && rm -f "$previous"
    systemctl reload "$service"
    ok "Apache site $SITE_NAME is live for $SITE_HOST"
}

configure_site() {
    [[ "$KIT_TYPE" == "Database" ]] && return 0
    if [[ "$KIT_TYPE" == "Api" && -z "$SITE_HOST" ]]; then
        info "No host name for the API: no web server site was created (the frontend's API route reaches it)."
        return 0
    fi
    step "Configuring the $WEB_SERVER site $SITE_NAME"
    if [[ "$WEB_SERVER" == "nginx" ]]; then write_nginx_site; else write_apache_site; fi
}

# ---- Main -------------------------------------------------------------------------------------------------------

summary() {
    local scheme=http
    [[ -n "$CERT_PATH" ]] && scheme=https
    echo
    echo "=== Deployment complete: $APP_NAME #$BUILD_NUMBER ($KIT_TYPE) ==="
    case "$KIT_TYPE" in
        Api)
            [[ -n "$SITE_HOST" ]] && echo "  URL:      $scheme://$SITE_HOST"
            echo "  Status:   sudo systemctl status $SERVICE_NAME"
            echo "  Logs:     sudo journalctl -u $SERVICE_NAME -f"
            echo "  Settings: sudo nano $DEPLOY_PATH/appsettings.json   (then: sudo systemctl restart $SERVICE_NAME)"
            ;;
        Frontend)
            echo "  URL:      $scheme://$SITE_HOST"
            echo "  Files:    $DEPLOY_PATH   (runtime settings: $DEPLOY_PATH/config.js)"
            ;;
        Database)
            echo "  The database is migrated. Deploy the API kit next, then the frontend kit."
            ;;
    esac
    [[ -n "$BACKUP_FILE" ]] && echo "  Backup:   $BACKUP_FILE"
    echo "  Log:      $LOG_FILE"
}

main() {
    assert_environment
    # Everything that follows is also written to deployment.log
    exec > >(tee -a "$LOG_FILE") 2>&1
    echo "---- $(date -Is) Deploy.sh $* ----"

    read_config
    show_plan

    ensure_dotnet
    if [[ "$KIT_TYPE" == "Database" ]]; then
        run_migrations
        summary
        return 0
    fi

    ensure_redis
    ensure_web_server
    backup_current
    stop_service
    publish_files
    review_appsettings
    set_permissions
    run_migrations
    install_service
    start_service
    configure_site
    summary
}

main "$@"
