#!/bin/bash
# DeployVare offline manual deployment for Linux (Ubuntu/Debian)
# Run with: sudo bash Deploy.sh
# Reads deploy-config.json. Does not call DeployVare or Azure DevOps. Does not print secrets.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_ROOT="$(dirname "$SCRIPT_DIR")"
CONFIG_PATH="$PACKAGE_ROOT/deploy-config.json"
LOG_FILE="$PACKAGE_ROOT/deployment.log"

# Arrays to track steps
declare -a STEP_LOGS=()
SITE_STOPPED=false
REDIS_SERVICE=""

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# ---- Utility Functions ----
write_step() {
    local msg="$1"
    echo -e "${GREEN}[$(date +'%H:%M:%S')]${NC} $msg"
    STEP_LOGS+=("$msg")
}

write_error() {
    local msg="$1"
    echo -e "${RED}[$(date +'%H:%M:%S')] ERROR: $msg${NC}"
    STEP_LOGS+=("FAILED: $msg")
}

write_info() {
    local msg="$1"
    echo "[$(date +'%H:%M:%S')] $msg"
    STEP_LOGS+=("$msg")
}

fail() {
    local msg="$1"
    write_error "$msg"

    # Attempt to restore service state
    if [ "$SITE_STOPPED" = true ] && [ -n "$REDIS_SERVICE" ]; then
        write_info "Attempting to restart $REDIS_SERVICE after failure..."
        sudo systemctl start "$REDIS_SERVICE" 2>/dev/null || true
    fi

    # Save log
    {
        printf '%s\n' "${STEP_LOGS[@]}"
    } >> "$LOG_FILE"

    exit 1
}

assert_root() {
    if [[ $EUID -ne 0 ]]; then
        fail "This script must be run as root (use: sudo bash Deploy.sh)"
    fi
}

read_config() {
    if [[ ! -f "$CONFIG_PATH" ]]; then
        fail "deploy-config.json not found at $CONFIG_PATH"
    fi

    # Parse JSON using jq or fall back to basic grep
    if ! command -v jq &> /dev/null; then
        write_info "jq not found; installing..."
        apt-get update -qq && apt-get install -y jq > /dev/null
    fi

    # Export config values as variables
    export SERVER_NAME=$(jq -r '.serverName // empty' "$CONFIG_PATH")
    export SERVER_TYPE=$(jq -r '.serverType // empty' "$CONFIG_PATH")
    export WEB_SERVER=$(jq -r '.webServer // "nginx"' "$CONFIG_PATH")
    export DEPLOYMENT_PATH=$(jq -r '.deploymentPath // empty' "$CONFIG_PATH")
    export SITE_NAME=$(jq -r '.siteName // empty' "$CONFIG_PATH")
    export SITE_HOST_NAME=$(jq -r '.siteHostName // empty' "$CONFIG_PATH")
    export HTTPS_PORT=$(jq -r '.httpsPort // 443' "$CONFIG_PATH")
    export CERTIFICATE_FILE=$(jq -r '.certificateFile // empty' "$CONFIG_PATH")
    export CERTIFICATE_KEY_FILE=$(jq -r '.certificateKeyFile // empty' "$CONFIG_PATH")
    export SQL_DATA_SOURCE=$(jq -r '.sqlDataSource // empty' "$CONFIG_PATH")
    export SQL_DATABASE=$(jq -r '.databaseName // empty' "$CONFIG_PATH")
    export FRESH_DATABASE=$(jq -r '.freshDatabase // false' "$CONFIG_PATH")
    export REQUIRED_DOTNET_MAJOR=$(jq -r '.requiredDotNetMajor // 8' "$CONFIG_PATH")
    export REQUIRED_REDIS_MAJOR=$(jq -r '.requiredRedisMajor // 7' "$CONFIG_PATH")
    export REDIS_PORT=$(jq -r '.redisPort // 6379' "$CONFIG_PATH")

    # Validate required fields
    [[ -n "$SERVER_NAME" ]] || fail "serverName missing from deploy-config.json"
    [[ -n "$SERVER_TYPE" ]] || fail "serverType missing from deploy-config.json"
    [[ -n "$DEPLOYMENT_PATH" ]] || fail "deploymentPath missing from deploy-config.json"
    [[ -n "$SITE_NAME" ]] || fail "siteName missing from deploy-config.json"
    [[ -n "$SITE_HOST_NAME" ]] || fail "siteHostName missing from deploy-config.json"

    # Validate path (must be absolute and safe)
    if [[ ! "$DEPLOYMENT_PATH" =~ ^/ ]]; then
        fail "deploymentPath must be an absolute path (starting with /): $DEPLOYMENT_PATH"
    fi
    if [[ "$DEPLOYMENT_PATH" =~ \.\. ]]; then
        fail "deploymentPath must not contain .."
    fi

    write_step "Configuration loaded from deploy-config.json"
}

installer_path() {
    local pattern="$1"
    local dir="$PACKAGE_ROOT/installers"
    if [[ ! -d "$dir" ]]; then
        echo ""
        return
    fi
    # Return most recent matching file
    find "$dir" -maxdepth 1 -name "$pattern" -type f -printf '%T@ %p\n' 2>/dev/null | \
        sort -rn | head -1 | cut -d' ' -f2- || echo ""
}

# ---- System Checks ----
ensure_root_writable_tmp() {
    # Ensure /tmp is writable (needed for builds, extracts, etc.)
    if ! touch /tmp/.write_test 2>/dev/null; then
        fail "/tmp is not writable; cannot proceed"
    fi
    rm -f /tmp/.write_test
    write_step "[OK] /tmp is writable"
}

ensure_dependencies() {
    write_step "Installing system dependencies..."
    apt-get update -qq
    apt-get install -y --no-install-recommends \
        curl wget gpg systemctl openssh-client ca-certificates \
        > /dev/null 2>&1
    write_step "[OK] System dependencies installed"
}

ensure_dotnet() {
    if [[ "$SERVER_TYPE" != "Api" ]]; then return; fi

    local major=$REQUIRED_DOTNET_MAJOR
    write_step "Checking .NET $major runtime..."

    if ! command -v dotnet &> /dev/null; then
        write_step "Installing .NET $major SDK and Runtime..."

        # Add Microsoft package repository
        wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb \
            -O packages-microsoft-prod.deb
        dpkg -i packages-microsoft-prod.deb > /dev/null 2>&1
        rm packages-microsoft-prod.deb

        apt-get update -qq
        apt-get install -y dotnet-sdk-$major dotnet-runtime-$major > /dev/null 2>&1
    fi

    # Verify installation
    local installed_version=$(dotnet --version 2>/dev/null | cut -d'.' -f1)
    if [[ "$installed_version" != "$major" ]]; then
        fail ".NET $major not installed; found version: $installed_version"
    fi

    write_step "[OK] .NET $major"
}

ensure_nginx() {
    if [[ "$WEB_SERVER" != "nginx" && "$WEB_SERVER" != "both" ]]; then return; fi

    if command -v nginx &> /dev/null; then
        if systemctl is-active --quiet nginx; then
            write_step "[OK] Nginx is installed and running"
        else
            write_step "Starting Nginx..."
            systemctl start nginx
            write_step "[OK] Nginx started"
        fi
        return
    fi

    write_step "Installing Nginx..."
    apt-get install -y nginx > /dev/null 2>&1

    # Enable and start
    systemctl enable nginx > /dev/null 2>&1
    systemctl start nginx > /dev/null 2>&1

    write_step "[OK] Nginx installed and started"
}

ensure_apache() {
    if [[ "$WEB_SERVER" != "apache" && "$WEB_SERVER" != "both" ]]; then return; fi

    if command -v apache2ctl &> /dev/null; then
        if systemctl is-active --quiet apache2; then
            write_step "[OK] Apache2 is installed and running"
        else
            write_step "Starting Apache2..."
            systemctl start apache2
            write_step "[OK] Apache2 started"
        fi
        return
    fi

    write_step "Installing Apache2 and required modules..."
    apt-get install -y apache2 libapache2-mod-proxy-html libapache2-mod-rewrite > /dev/null 2>&1

    # Enable required modules
    a2enmod proxy proxy_http rewrite ssl headers > /dev/null 2>&1

    # Enable and start
    systemctl enable apache2 > /dev/null 2>&1
    systemctl start apache2 > /dev/null 2>&1

    write_step "[OK] Apache2 installed and started"
}

ensure_redis() {
    if [[ "$SERVER_TYPE" != "Api" ]]; then return; fi

    local major=$REQUIRED_REDIS_MAJOR
    write_step "Checking Redis $major..."

    # Try to find existing Redis service
    if systemctl list-units --all | grep -E "redis|valkey" > /dev/null; then
        REDIS_SERVICE=$(systemctl list-units --all | grep -oE 'redis-server|valkey-server' | head -1)
        if systemctl is-active --quiet "$REDIS_SERVICE"; then
            write_step "[OK] Redis/Valkey is running ($REDIS_SERVICE on port $REDIS_PORT)"
            return
        fi
    fi

    write_step "Installing Redis $major..."

    # Install from Ubuntu repos (may not be latest major version; consider building from source if strict version needed)
    apt-get install -y redis-server > /dev/null 2>&1

    # Configure port
    if grep -q "^port " /etc/redis/redis.conf; then
        sed -i "s/^port .*/port $REDIS_PORT/" /etc/redis/redis.conf
    fi

    # Enable and start
    systemctl enable redis-server > /dev/null 2>&1
    systemctl start redis-server > /dev/null 2>&1

    REDIS_SERVICE="redis-server"
    write_step "[OK] Redis installed on port $REDIS_PORT"
}

ensure_directories() {
    write_step "Creating deployment directories..."

    mkdir -p "$DEPLOYMENT_PATH"
    if [[ "$SERVER_TYPE" == "Api" ]]; then
        mkdir -p /var/log/deployvare
        mkdir -p /etc/deployvare/ssl
    fi

    write_step "[OK] Directories created"
}

ensure_ssl_certificate() {
    if [[ -z "$CERTIFICATE_FILE" || -z "$CERTIFICATE_KEY_FILE" ]]; then
        write_info "No SSL certificate configured; skipping"
        return
    fi

    write_step "Installing SSL certificate..."

    local src_cert="$PACKAGE_ROOT/$CERTIFICATE_FILE"
    local src_key="$PACKAGE_ROOT/$CERTIFICATE_KEY_FILE"
    local dst_dir="/etc/deployvare/ssl"

    if [[ ! -f "$src_cert" ]]; then
        fail "Certificate file not found: $src_cert"
    fi
    if [[ ! -f "$src_key" ]]; then
        fail "Certificate key file not found: $src_key"
    fi

    cp "$src_cert" "$dst_dir/certificate.crt"
    cp "$src_key" "$dst_dir/certificate.key"
    chmod 600 "$dst_dir/certificate.key"

    write_step "[OK] SSL certificate installed"
}

# ---- Web Server Configuration ----
configure_nginx() {
    if [[ "$WEB_SERVER" != "nginx" && "$WEB_SERVER" != "both" ]]; then return; fi

    write_step "Configuring Nginx for $SITE_NAME..."

    local conf_file="/etc/nginx/sites-available/$SITE_NAME"
    local enabled_link="/etc/nginx/sites-enabled/$SITE_NAME"

    # Create upstream for .NET app
    cat > "$conf_file" << 'NGINX_CONF'
# Auto-generated by DeployVare Deploy.sh
upstream dotnet_backend {
    server 127.0.0.1:5000 max_fails=3 fail_timeout=30s;
}

server {
    listen 80;
    server_name SITE_HOST_NAME;
    root DEPLOYMENT_PATH;

    # Redirect HTTP to HTTPS
    location / {
        return 301 https://$server_name$request_uri;
    }
}

server {
    listen 443 ssl http2;
    server_name SITE_HOST_NAME;
    root DEPLOYMENT_PATH;

    ssl_certificate /etc/deployvare/ssl/certificate.crt;
    ssl_certificate_key /etc/deployvare/ssl/certificate.key;

    # Security headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # Proxy to .NET app
    location / {
        proxy_pass http://dotnet_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # Static files
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
        expires 30d;
        add_header Cache-Control "public, immutable";
    }
}
NGINX_CONF

    # Replace placeholders
    sed -i "s|SITE_HOST_NAME|$SITE_HOST_NAME|g" "$conf_file"
    sed -i "s|DEPLOYMENT_PATH|$DEPLOYMENT_PATH|g" "$conf_file"

    # Enable site
    if [[ ! -L "$enabled_link" ]]; then
        ln -s "$conf_file" "$enabled_link"
    fi

    # Test and reload
    if nginx -t > /dev/null 2>&1; then
        systemctl reload nginx > /dev/null 2>&1
        write_step "[OK] Nginx configured"
    else
        fail "Nginx configuration test failed"
    fi
}

configure_apache() {
    if [[ "$WEB_SERVER" != "apache" && "$WEB_SERVER" != "both" ]]; then return; fi

    write_step "Configuring Apache for $SITE_NAME..."

    local conf_file="/etc/apache2/sites-available/$SITE_NAME.conf"
    local enabled_link="/etc/apache2/sites-enabled/$SITE_NAME.conf"

    cat > "$conf_file" << 'APACHE_CONF'
# Auto-generated by DeployVare Deploy.sh
<VirtualHost *:80>
    ServerName SITE_HOST_NAME
    DocumentRoot DEPLOYMENT_PATH

    # Redirect HTTP to HTTPS
    RewriteEngine On
    RewriteCond %{HTTPS} off
    RewriteRule ^(.*)$ https://%{HTTP_HOST}%{REQUEST_URI} [L,R=301]
</VirtualHost>

<VirtualHost *:443>
    ServerName SITE_HOST_NAME
    DocumentRoot DEPLOYMENT_PATH

    SSLEngine On
    SSLCertificateFile /etc/deployvare/ssl/certificate.crt
    SSLCertificateKeyFile /etc/deployvare/ssl/certificate.key

    # Security headers
    Header always set Strict-Transport-Security "max-age=31536000; includeSubDomains"
    Header always set X-Content-Type-Options "nosniff"
    Header always set X-Frame-Options "DENY"
    Header always set X-XSS-Protection "1; mode=block"
    Header always set Referrer-Policy "strict-origin-when-cross-origin"

    # Proxy to .NET app
    ProxyPreserveHost On
    ProxyPass / http://127.0.0.1:5000/ timeout=3600
    ProxyPassReverse / http://127.0.0.1:5000/

    # WebSocket support
    RewriteEngine On
    RewriteCond %{HTTP:Upgrade} websocket [NC]
    RewriteCond %{HTTP:Connection} upgrade [NC]
    RewriteRule ^/?(.*) "http://127.0.0.1:5000/$1" [P,L]

    # Static files caching
    <FilesMatch "\.(?:js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$">
        Header set Cache-Control "public, max-age=2592000"
    </FilesMatch>
</VirtualHost>
APACHE_CONF

    # Replace placeholders
    sed -i "s|SITE_HOST_NAME|$SITE_HOST_NAME|g" "$conf_file"
    sed -i "s|DEPLOYMENT_PATH|$DEPLOYMENT_PATH|g" "$conf_file"

    # Enable site
    if [[ ! -f "$enabled_link" ]]; then
        ln -s "$conf_file" "$enabled_link"
    fi

    # Disable default site
    a2dissite 000-default > /dev/null 2>&1 || true

    # Test and reload
    if apache2ctl configtest 2>&1 | grep -q "Syntax OK"; then
        systemctl reload apache2 > /dev/null 2>&1
        write_step "[OK] Apache configured"
    else
        fail "Apache configuration test failed"
    fi
}

# ---- Database Setup (SQL Server) ----
ensure_sql_server() {
    if [[ "$SERVER_TYPE" != "Api" || -z "$SQL_DATABASE" ]]; then
        return
    fi

    write_info "Note: SQL Server setup requires pre-installed SQL Server instance"
    write_info "SQL_DATA_SOURCE: $SQL_DATA_SOURCE"
    write_info "Database: $SQL_DATABASE"

    if [[ "$FRESH_DATABASE" == "true" ]]; then
        write_info "Fresh database flag set; manual SQL restore required"
        write_info "Check deployment documentation for restore procedures"
    fi
}

# ---- Application Deployment ----
backup_site() {
    if [[ ! -d "$DEPLOYMENT_PATH" ]] || [[ -z "$(ls -A "$DEPLOYMENT_PATH")" ]]; then
        write_info "No existing site to backup"
        return
    fi

    local backup_dir="$PACKAGE_ROOT/backups"
    mkdir -p "$backup_dir"

    local timestamp=$(date +%Y%m%d_%H%M%S)
    local backup_file="$backup_dir/${SITE_NAME}_${timestamp}.tar.gz"

    write_step "Backing up existing site to $backup_file..."
    tar -czf "$backup_file" -C "$(dirname "$DEPLOYMENT_PATH")" "$(basename "$DEPLOYMENT_PATH")" \
        2>/dev/null || true

    write_step "[OK] Backup created"
}

publish_app() {
    write_step "Deploying application..."

    if [[ ! -d "$PACKAGE_ROOT/app" ]]; then
        fail "app/ folder not found in package"
    fi

    # Stop the application if running
    if systemctl is-active --quiet deployvare-app 2>/dev/null; then
        write_step "Stopping deployvare-app service..."
        systemctl stop deployvare-app || true
        SITE_STOPPED=true
    fi

    # Deploy files
    mkdir -p "$DEPLOYMENT_PATH"
    cp -r "$PACKAGE_ROOT/app"/* "$DEPLOYMENT_PATH/" 2>/dev/null || true

    # Copy SQL scripts if API server
    if [[ "$SERVER_TYPE" == "Api" && -d "$PACKAGE_ROOT/sqlscripts" ]]; then
        cp -r "$PACKAGE_ROOT/sqlscripts" "$DEPLOYMENT_PATH/" || true
    fi

    # Set permissions
    chown -R www-data:www-data "$DEPLOYMENT_PATH" 2>/dev/null || \
    chown -R nginx:nginx "$DEPLOYMENT_PATH" 2>/dev/null || true
    chmod -R 755 "$DEPLOYMENT_PATH"

    write_step "[OK] Application deployed"
}

create_systemd_service() {
    if [[ "$SERVER_TYPE" != "Api" ]]; then return; fi

    write_step "Creating systemd service for deployvare-app..."

    local service_file="/etc/systemd/system/deployvare-app.service"

    cat > "$service_file" << SYSTEMD_CONF
[Unit]
Description=DeployVare API Server
After=network.target redis-server.service
Wants=redis-server.service

[Service]
Type=notify
User=www-data
WorkingDirectory=$DEPLOYMENT_PATH
ExecStart=$DEPLOYMENT_PATH/API
Restart=on-failure
RestartSec=5s
StandardOutput=journal
StandardError=journal
SyslogIdentifier=deployvare
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://127.0.0.1:5000"
Environment="DOTNET_CLI_TELEMETRY_OPTOUT=1"

[Install]
WantedBy=multi-user.target
SYSTEMD_CONF

    systemctl daemon-reload > /dev/null 2>&1
    systemctl enable deployvare-app > /dev/null 2>&1

    write_step "[OK] Systemd service created"
}

start_app() {
    if [[ "$SERVER_TYPE" != "Api" ]]; then return; fi

    write_step "Starting deployvare-app service..."

    systemctl start deployvare-app || fail "Failed to start deployvare-app"

    # Wait for service to be ready
    sleep 2

    if systemctl is-active --quiet deployvare-app; then
        write_step "[OK] deployvare-app is running"
    else
        fail "deployvare-app failed to start; check: sudo journalctl -u deployvare-app -n 50"
    fi

    SITE_STOPPED=false
}

ensure_hosts_entry() {
    local hostname="$SITE_HOST_NAME"

    if [[ "$hostname" == "localhost" || "$hostname" == "127.0.0.1"* ]]; then
        return
    fi

    if grep -q "127.0.0.1.*$hostname" /etc/hosts; then
        write_info "Hosts entry for $hostname already exists"
        return
    fi

    write_step "Adding hosts entry for $hostname..."
    echo "127.0.0.1    $hostname" >> /etc/hosts
    write_step "[OK] Hosts entry added"
}

# ---- Main ----
main() {
    write_step "=== DeployVare Linux Deployment ==="
    write_step "Server: $SERVER_NAME"
    write_step "Type: $SERVER_TYPE"
    write_step "Path: $DEPLOYMENT_PATH"
    write_step ""

    assert_root
    ensure_root_writable_tmp
    read_config

    # System setup
    ensure_dependencies
    ensure_dotnet
    ensure_nginx
    ensure_apache
    ensure_redis
    ensure_directories
    ensure_ssl_certificate

    # Web server config
    configure_nginx
    configure_apache

    # Database
    ensure_sql_server

    # Application deployment
    backup_site
    publish_app
    create_systemd_service
    start_app
    ensure_hosts_entry

    write_step ""
    write_step "=== Deployment Complete ==="
    write_step "Application running at: https://$SITE_HOST_NAME"
    write_step "Logs: sudo journalctl -u deployvare-app -f"
    write_step ""

    # Save log
    {
        printf '%s\n' "${STEP_LOGS[@]}"
    } >> "$LOG_FILE"

    write_step "Deployment log saved to: $LOG_FILE"
}

main "$@"
