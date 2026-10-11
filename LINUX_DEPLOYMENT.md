# Linux Deployment Guide

This guide covers deploying DeployVare on Ubuntu 20.04 LTS / 22.04 LTS or Debian 11/12. Both **manual deployment** and **Docker deployment** are supported.

---

## Quick Start (Manual Deployment)

For the fastest offline deployment with zero dependencies beyond bash and apt:

```bash
# 1. Extract deployment package on Linux server (as root or sudo user)
sudo bash Deploy.sh

# Configuration is read from deploy-config.json in the same directory
```

**That's it!** The script will:
- ✅ Install .NET 8 SDK/Runtime
- ✅ Install Nginx or Apache (or both)
- ✅ Install Redis
- ✅ Configure SSL certificates
- ✅ Create systemd service for the app
- ✅ Start the application

See [Manual Deployment](#manual-deployment-deploysh) below for details.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Manual Deployment (Deploy.sh)](#manual-deployment-deploysh)
3. [Docker Deployment (docker-compose)](#docker-deployment)
4. [Remote Deployment via DeployVare](#remote-deployment-via-deployvare)
5. [Configuration](#configuration)
6. [Troubleshooting](#troubleshooting)
7. [Security Considerations](#security-considerations)

---

## Prerequisites

### System Requirements

- **Ubuntu 20.04 LTS** or **22.04 LTS** (or Debian 11/12 equivalent)
- **2+ CPU cores** (4+ recommended)
- **2+ GB RAM** (4+ recommended)
- **20+ GB disk space**
- **SSH access** (for DeployVare remote orchestration) or local root access (for manual deployment)
- **Internet access** (for downloading packages; offline mode possible with pre-staged binaries)

### Manual Deployment

Requirements:
- Root or sudo access
- bash 4.0+
- curl or wget
- jq (installed automatically if missing)

```bash
# Check prerequisites
bash --version   # Should be 4.0 or higher
which sudo       # Should output /usr/bin/sudo
```

### Docker Deployment

Requirements:
- Docker 20.10+
- Docker Compose 2.0+

```bash
# Install Docker (Ubuntu 22.04)
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2

# Verify
docker --version
docker compose version
```

---

## Manual Deployment (Deploy.sh)

### 1. Prepare the Package

Generate a deployment package from DeployVare:

1. Open DeployVare Management Dashboard
2. Navigate: **Deployment** → **Create Package** → Select **Linux**
3. Download the ZIP file
4. Extract on your Linux server:

```bash
unzip DeployVare-Linux-<version>.zip
cd DeployVare-Linux-<version>
```

Or copy the contents manually:

```
deployvare-package/
├── Deploy.sh                          # Main deployment script
├── deploy-config.json                 # Configuration (EDIT THIS)
├── app/                               # Compiled .NET application
├── certificate/                       # SSL certificate files (optional)
├── sqlscripts/                        # SQL migration scripts (optional)
└── installers/                        # Optional: pre-downloaded installers
```

### 2. Configure Deployment

Edit `deploy-config.json` with your server details:

```json
{
  "serverName": "production-api",
  "serverType": "Api",
  "webServer": "nginx",
  "deploymentPath": "/var/www/deployvare-api",
  "siteName": "deployvare-api",
  "siteHostName": "api.example.com",
  "httpsPort": 443,
  "certificateFile": "certificate/api.example.com.crt",
  "certificateKeyFile": "certificate/api.example.com.key",
  "sqlDataSource": "sql-server.example.com",
  "databaseName": "DeployVare",
  "freshDatabase": false,
  "requiredDotNetMajor": 8,
  "requiredRedisMajor": 7,
  "redisPort": 6379
}
```

**Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `serverName` | string | Display name for your server |
| `serverType` | string | `Api` (backend + database) or `Frontend` (Nginx/Apache only) |
| `webServer` | string | `nginx`, `apache`, or `both` |
| `deploymentPath` | string | Absolute path where app files are deployed (e.g., `/var/www/deployvare-api`) |
| `siteName` | string | Name for web server config (lowercase, alphanumeric) |
| `siteHostName` | string | Domain name (e.g., `api.example.com`) |
| `httpsPort` | integer | HTTPS port (default: 443) |
| `certificateFile` | string | Path to .crt file (relative to package root) |
| `certificateKeyFile` | string | Path to .key file (relative to package root) |
| `sqlDataSource` | string | SQL Server connection (hostname or IP:port) |
| `databaseName` | string | Database name |
| `freshDatabase` | boolean | Whether to restore from backup |
| `requiredDotNetMajor` | integer | .NET version (8 or 9) |
| `requiredRedisMajor` | integer | Redis version (7 or 8) |
| `redisPort` | integer | Redis port (default: 6379) |

### 3. Run Deployment

```bash
# Give the script execute permissions
chmod +x Deploy.sh

# Run as root (required for systemd, web server, SSL installation)
sudo bash Deploy.sh
```

**Script Output:**
- ✅ Logs to console in real-time
- ✅ Saves log to `deployment.log` in the package directory

### 4. Verify Deployment

```bash
# Check if the API is running
curl -k https://localhost/health

# Check systemd service status
sudo systemctl status deployvare-app

# View real-time logs
sudo journalctl -u deployvare-app -f

# Check Nginx/Apache
sudo systemctl status nginx
sudo systemctl status apache2

# Check Redis
redis-cli ping
# Should respond: PONG
```

### 5. Next Steps

After successful deployment:

1. **Configure Firewall** (if using cloud provider)
   - Allow inbound: 80, 443 (HTTP/HTTPS)
   - Allow inbound: 3306 (if Redis needs external access)

2. **Set Up DNS** (if using domain name)
   - Point `api.example.com` to server IP
   - Test: `nslookup api.example.com`

3. **Test Application**
   - Browse to `https://api.example.com`
   - Check API health: `https://api.example.com/health`
   - View Swagger docs: `https://api.example.com/swagger`

4. **Monitor Logs**
   ```bash
   sudo journalctl -u deployvare-app -n 100    # Last 100 lines
   sudo journalctl -u deployvare-app --since today  # Today's logs
   ```

---

## Docker Deployment

### 1. Prerequisites

Install Docker and Docker Compose:

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker $USER  # Add current user to docker group
```

### 2. Prepare Environment File

Create `.env` in the repository root:

```bash
# Database connection
DB_CONNECTION_STRING=Server=sql-server.example.com;Database=DeployVare;Integrated Security=false;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true

# JWT configuration
JWT_SECRET_KEY=ChangeMe_UseAReallyLongSecureKeyAtLeast32Characters
JWT_ISSUER=DeployVare
JWT_AUDIENCE=DeployVare

# SQL Server SA password (only if using Docker SQL Server)
SA_PASSWORD=YourPassword123!
```

**⚠️ Security:** Never commit `.env` to git; add to `.gitignore`:

```bash
echo ".env" >> .gitignore
git add .gitignore && git commit -m "chore: ignore .env secrets"
```

### 3. Build and Start Services

```bash
# Build images
docker compose -f docker-compose.linux.yml build

# Start all services (detached)
docker compose -f docker-compose.linux.yml up -d

# View logs
docker compose -f docker-compose.linux.yml logs -f

# Stop services
docker compose -f docker-compose.linux.yml down
```

### 4. Verify Docker Deployment

```bash
# Check running containers
docker ps

# Test API health
curl -k https://localhost/health

# Check API logs
docker compose -f docker-compose.linux.yml logs api

# Check Redis
docker exec deployvare-redis redis-cli ping
# Should respond: PONG
```

### 5. Database Setup (Docker)

If running SQL Server in Docker:

```bash
# Start SQL Server
docker compose -f docker-compose.linux.yml up -d sql-server

# Wait for SQL Server to be ready (~30s)
docker exec deployvare-api dotnet ef database update
```

For external SQL Server, just update `DB_CONNECTION_STRING` in `.env`.

### 6. SSL/TLS Setup

#### Option 1: Self-Signed Certificate (Development)

```bash
mkdir -p Docker/nginx/ssl

# Generate self-signed certificate (10-year validity)
openssl req -x509 -newkey rsa:4096 -keyout Docker/nginx/ssl/certificate.key \
  -out Docker/nginx/ssl/certificate.crt -days 3650 -nodes \
  -subj "/C=US/ST=State/L=City/O=Org/CN=localhost"

# Rebuild and restart Nginx
docker compose -f docker-compose.linux.yml up -d --build nginx
```

#### Option 2: Let's Encrypt (Production)

```bash
# Install Certbot
sudo apt-get install -y certbot python3-certbot-nginx

# Get certificate
sudo certbot certonly --nginx -d api.example.com

# Copy to nginx SSL directory
sudo cp /etc/letsencrypt/live/api.example.com/fullchain.pem Docker/nginx/ssl/certificate.crt
sudo cp /etc/letsencrypt/live/api.example.com/privkey.pem Docker/nginx/ssl/certificate.key

# Restart Nginx
docker compose -f docker-compose.linux.yml restart nginx
```

---

## Remote Deployment via DeployVare

(Requires DeployVare server to be already deployed and accessible)

### 1. Add Linux Server in DeployVare UI

1. Open DeployVare Dashboard → **Servers** → **Add Server**
2. Fill in:
   - **Host Name:** IP or domain
   - **Port:** 22 (SSH)
   - **Username:** ubuntu or your SSH user
   - **Authentication:** Password or SSH Key
   - **Server Type:** API (for full stack) or Frontend (Nginx/Apache only)
3. Click **Test Connection** to verify SSH access

### 2. Run Server Readiness Check

1. Click **Check Readiness** to verify prerequisites
2. DeployVare will:
   - ✅ Detect OS (Ubuntu/Debian)
   - ✅ Check for .NET, Redis, Nginx/Apache
   - ✅ Report missing components

### 3. Install Components via DeployVare

1. Click **Install Component**
2. Select component (e.g., **.NET 8 Runtime**)
3. DeployVare will:
   - ✅ SSH to server
   - ✅ Run installation script
   - ✅ Verify installation
   - ✅ Report status

### 4. Configure Web Server

1. Go to **Configure Bindings**
2. Set hostname, port, SSL certificate
3. DeployVare will:
   - ✅ Upload certificate
   - ✅ Configure Nginx/Apache
   - ✅ Test configuration
   - ✅ Reload web server

### 5. Deploy Application

1. Upload deployment package via DeployVare UI
2. Application deployed with systemd service auto-start

---

## Configuration

### Environment Variables (systemd service)

File: `/etc/systemd/system/deployvare-app.service`

Common variables:

```bash
# ASP.NET Core
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000

# Logging
Serilog__MinimumLevel=Information
Serilog__WriteTo__0__Name=File

# Custom
# (Add via environment file or service override)
```

Update and reload:

```bash
sudo systemctl daemon-reload
sudo systemctl restart deployvare-app
```

### Firewall Rules

#### UFW (Ubuntu)

```bash
# Allow SSH
sudo ufw allow 22/tcp

# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Allow Redis (internal only)
sudo ufw allow from 127.0.0.1 to 127.0.0.1 port 6379

# Enable firewall
sudo ufw enable
```

#### Cloud (AWS, Azure, GCP)

Add Security Group / Network Security Group rules:
- **Inbound:** 22/tcp (SSH), 80/tcp (HTTP), 443/tcp (HTTPS)
- **Outbound:** All (or restrict as needed)

---

## Troubleshooting

### Deployment Script Fails

**Issue:** `bash: Deploy.sh: command not found`

```bash
# Ensure bash, not sh
bash Deploy.sh

# Or make executable
chmod +x Deploy.sh && ./Deploy.sh
```

**Issue:** Permission denied

```bash
# Run as root or with sudo
sudo bash Deploy.sh
```

**Issue:** `jq: command not found`

Script auto-installs jq; if it fails:

```bash
sudo apt-get install -y jq
```

### Application Won't Start

**Check service status:**

```bash
sudo systemctl status deployvare-app
sudo journalctl -u deployvare-app -n 50
```

**Common causes:**
- Database connection string invalid
- Port 5000 already in use
- Insufficient permissions on deployment path

**Fix:**

```bash
# Ensure ownership
sudo chown -R www-data:www-data /var/www/deployvare-api

# Restart
sudo systemctl restart deployvare-app

# Check for port conflicts
sudo netstat -tlnp | grep 5000
```

### Nginx/Apache Won't Reload

**Test configuration:**

```bash
# Nginx
sudo nginx -t

# Apache
sudo apache2ctl configtest
```

**Fix syntax errors** shown in output, then reload.

### Redis Connection Fails

**Test Redis:**

```bash
redis-cli ping
# Should respond: PONG

# If not running
sudo systemctl start redis-server
sudo systemctl status redis-server
```

**Check logs:**

```bash
sudo journalctl -u redis-server -n 20
```

### SSL Certificate Issues

**Verify certificate:**

```bash
# Check expiry
openssl x509 -in /etc/deployvare/ssl/certificate.crt -text -noout | grep "Not After"

# Test HTTPS
curl -k --verbose https://localhost

# Verify hostname matches
openssl x509 -in /etc/deployvare/ssl/certificate.crt -text | grep "Subject Alternative Name"
```

### Docker Container Logs

```bash
# View logs
docker logs deployvare-api

# Follow logs
docker logs -f deployvare-api

# Tail last 50 lines
docker logs --tail 50 deployvare-api
```

---

## Security Considerations

### 1. SSL/TLS

- ✅ Always use HTTPS in production
- ✅ Use certificates from trusted CA (Let's Encrypt is free)
- ✅ Enable HSTS (Strict-Transport-Security header)

### 2. Database Security

```bash
# Connect to SQL Server with encryption
Data Source=tcp:server.example.com,1433;
Initial Catalog=DeployVare;
Persist Security Info=true;
User ID=dbuser;
Password=StrongPassword123!;
Encrypt=yes;
TrustServerCertificate=false;
```

### 3. SSH Hardening

```bash
# Disable password auth (use keys only)
sudo sed -i 's/#PasswordAuthentication yes/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl restart ssh

# Use non-standard port (optional)
sudo sed -i 's/#Port 22/Port 2222/' /etc/ssh/sshd_config

# Restrict SSH to specific IPs (optional)
echo "AllowUsers ubuntu@203.0.113.10" | sudo tee -a /etc/ssh/sshd_config
```

### 4. Application Secrets

```bash
# Never commit secrets
.env
appsettings.Production.json
*.key
*.crt
```

### 5. Regular Updates

```bash
# Keep system patched
sudo apt-get update && sudo apt-get upgrade -y

# Keep Docker images updated
docker pull mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy
```

---

## Performance Tuning

### Nginx Worker Connections

```bash
# Edit nginx.conf
sudo nano Docker/nginx/nginx.conf

# Adjust for your system
worker_processes auto;
worker_connections 2048;  # Increase if handling many concurrent connections
```

### Redis Optimization

```bash
# Monitor memory usage
redis-cli info memory

# Tune maxmemory and policy
redis-cli CONFIG SET maxmemory 256mb
redis-cli CONFIG SET maxmemory-policy allkeys-lru
```

### .NET Core Optimization

Environment variables:

```bash
ASPNETCORE_ENVIRONMENT=Production
DOTNET_TieredCompilation=1
DOTNET_TieredCompilationQuickJit=1
DOTNET_TieredCompilationQuickJitForLoops=1
```

---

## Next Steps

1. **Configure Licensing** — Set up license issuance for client servers
2. **Set Up Monitoring** — Use `journalctl`, Prometheus, or ELK stack
3. **Configure Backups** — Schedule database and volume backups
4. **Enable CI/CD** — Automate deployments via GitLab CI, GitHub Actions, etc.
5. **Create Admin User** — Use DeployVare API to create first admin account

---

## Support & Resources

- **Logs:** `/var/log/deployvare/` or `docker logs`
- **Config:** `deploy-config.json` or `.env`
- **Swagger Docs:** `https://your-server/swagger`
- **Health Check:** `https://your-server/health`

---

**Happy Deploying! 🚀**
