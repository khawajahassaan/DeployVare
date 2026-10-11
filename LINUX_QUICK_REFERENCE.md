# Linux Deployment Quick Reference

## 🚀 Manual Deployment (30 seconds)

```bash
# 1. Extract package and navigate
cd /path/to/package

# 2. Edit configuration
nano deploy-config.json

# 3. Run deployment (as root)
sudo bash Deploy.sh

# 4. Check status
curl -k https://localhost:443/health
```

---

## 🐳 Docker Deployment (60 seconds)

```bash
# 1. Create .env file
cat > .env << EOF
DB_CONNECTION_STRING=Server=sql-server;Database=DeployVare;Integrated Security=false;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true
JWT_SECRET_KEY=ChangeMe_UseAReallyLongSecureKeyAtLeast32Characters
EOF

# 2. Start services
docker compose -f docker-compose.linux.yml up -d

# 3. Check health
curl -k https://localhost/health
```

---

## 📋 Configuration Quick Reference

### deploy-config.json (Manual)

```json
{
  "serverName": "api-prod",
  "serverType": "Api",
  "webServer": "nginx",
  "deploymentPath": "/var/www/api",
  "siteName": "api",
  "siteHostName": "api.example.com",
  "certificateFile": "certificate/cert.crt",
  "certificateKeyFile": "certificate/cert.key",
  "sqlDataSource": "sql-server.example.com",
  "databaseName": "DeployVare",
  "redisPort": 6379
}
```

### .env (Docker)

```bash
DB_CONNECTION_STRING=Server=...;Database=...;User Id=...;Password=...;
JWT_SECRET_KEY=YourSecureKeyHere
JWT_ISSUER=DeployVare
JWT_AUDIENCE=DeployVare
```

---

## 📊 Service Management

```bash
# View service status
sudo systemctl status deployvare-app

# View real-time logs
sudo journalctl -u deployvare-app -f

# View last 100 lines
sudo journalctl -u deployvare-app -n 100

# Restart service
sudo systemctl restart deployvare-app

# Stop service
sudo systemctl stop deployvare-app

# Start service
sudo systemctl start deployvare-app
```

---

## 🐳 Docker Commands

```bash
# Start all services
docker compose -f docker-compose.linux.yml up -d

# Stop all services
docker compose -f docker-compose.linux.yml down

# View logs (all services)
docker compose -f docker-compose.linux.yml logs -f

# View logs (specific service)
docker compose -f docker-compose.linux.yml logs -f api

# Tail last 50 lines
docker compose -f docker-compose.linux.yml logs -f --tail 50 api

# Check running containers
docker ps

# Execute command in container
docker exec deployvare-api dotnet --version

# Rebuild images
docker compose -f docker-compose.linux.yml build --no-cache
```

---

## 🔍 Troubleshooting Commands

### Check if service is running

```bash
# Manual deployment
sudo systemctl is-active deployvare-app && echo "RUNNING" || echo "STOPPED"

# Docker
docker ps | grep deployvare-api
```

### Check logs for errors

```bash
# Manual
sudo journalctl -u deployvare-app -n 50 --no-pager | grep -i error

# Docker
docker logs deployvare-api 2>&1 | grep -i error
```

### Test API endpoint

```bash
# Test HTTP redirect to HTTPS
curl -i http://localhost
# Should return 301 redirect to https://

# Test HTTPS (ignoring self-signed cert)
curl -k https://localhost/health
# Should return 200 with health status

# Test with verbose output
curl -k -v https://localhost/health
```

### Check port availability

```bash
# Check if ports are in use
sudo netstat -tlnp | grep -E ':(80|443|5000|6379)'

# Or with ss (modern alternative)
sudo ss -tlnp | grep -E ':(80|443|5000|6379)'
```

### Check disk space

```bash
# View disk usage
df -h

# View directory size
du -sh /var/www/deployvare-api
du -sh /data  # Docker volumes
```

### Check Redis

```bash
# Test Redis connection
redis-cli ping
# Should respond: PONG

# View Redis info
redis-cli info

# Clear Redis cache (WARNING: clears all data)
redis-cli FLUSHALL
```

### Check Nginx/Apache

```bash
# Nginx: test configuration
sudo nginx -t

# Apache: test configuration
sudo apache2ctl configtest

# Reload web server
sudo systemctl reload nginx
sudo systemctl reload apache2

# View error log
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/apache2/error.log
```

---

## 🔐 SSL/TLS Commands

### Check certificate expiry

```bash
openssl x509 -in /etc/deployvare/ssl/certificate.crt -text -noout | grep -A1 "Not After"
```

### Generate self-signed certificate

```bash
openssl req -x509 -newkey rsa:4096 -keyout certificate.key -out certificate.crt \
  -days 3650 -nodes -subj "/C=US/ST=State/L=City/O=Org/CN=localhost"
```

### Verify certificate

```bash
openssl x509 -in /etc/deployvare/ssl/certificate.crt -text -noout
```

### Generate Let's Encrypt certificate

```bash
sudo certbot certonly --nginx -d api.example.com
sudo cp /etc/letsencrypt/live/api.example.com/fullchain.pem /etc/deployvare/ssl/certificate.crt
sudo cp /etc/letsencrypt/live/api.example.com/privkey.pem /etc/deployvare/ssl/certificate.key
```

---

## 🌍 Firewall Commands

### UFW (Ubuntu)

```bash
# Enable firewall
sudo ufw enable

# Allow SSH
sudo ufw allow 22/tcp

# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# View rules
sudo ufw status

# Deny a port
sudo ufw deny 3306/tcp
```

### iptables (manual)

```bash
# List rules
sudo iptables -L -n

# Allow port
sudo iptables -A INPUT -p tcp --dport 80 -j ACCEPT

# Save rules (Ubuntu)
sudo iptables-save > /etc/iptables/rules.v4
```

---

## 🔄 Update & Maintenance

### Update system packages

```bash
sudo apt-get update
sudo apt-get upgrade -y
```

### Update Docker images

```bash
docker pull nginx:alpine
docker pull mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy
docker pull redis:7-alpine
docker compose -f docker-compose.linux.yml pull
docker compose -f docker-compose.linux.yml up -d
```

### Backup application data

```bash
# Backup deployment directory
sudo tar -czf backup-$(date +%Y%m%d-%H%M%S).tar.gz /var/www/deployvare-api

# Backup volumes (Docker)
docker run --rm -v deployvare_data:/data -v $(pwd):/backup \
  ubuntu tar czf /backup/deployvare-data-$(date +%Y%m%d).tar.gz -C /data .
```

---

## 🧪 Testing

### Health check endpoint

```bash
curl -k https://localhost/health
# Response: {"status":"healthy"}
```

### API documentation

```bash
# Browse Swagger documentation
https://your-server/swagger
```

### Database connection

```bash
# Test SQL Server connection (from command line)
# Note: requires mssql-tools installed
sqlcmd -S sql-server.example.com -U sa -P Password -d DeployVare -Q "SELECT @@VERSION"
```

### Redis connectivity

```bash
# Simple ping
redis-cli ping

# Info command
redis-cli INFO

# Monitor commands
redis-cli MONITOR
```

---

## 📊 Performance Monitoring

### System resource usage

```bash
# CPU & Memory
free -h
top -bn1 | head -20

# Disk I/O
iostat -x 1 5

# Network
ss -s
netstat -an | grep ESTABLISHED | wc -l
```

### Application metrics

```bash
# Request count via logs
docker logs deployvare-api | grep -c "HTTP"

# Error rate
docker logs deployvare-api | grep -c "ERROR"

# Average response time
docker logs deployvare-api | grep "duration" | awk '{sum+=$NF; count++} END {print sum/count}'
```

---

## 🚨 Emergency Commands

### Force restart service

```bash
sudo systemctl kill -s SIGKILL deployvare-app
sudo systemctl restart deployvare-app
```

### Clear logs (use with caution!)

```bash
# Clear journalctl logs
sudo journalctl --vacuum=time=3d

# Clear Docker logs
docker exec deployvare-api sh -c "echo '' > /var/log/app.log"
```

### Hard reset (DANGEROUS)

```bash
# Stop all services
sudo systemctl stop deployvare-app
docker compose -f docker-compose.linux.yml down -v

# Clear data (WARNING: loses all data)
sudo rm -rf /var/www/deployvare-api /data

# Restart from scratch
docker compose -f docker-compose.linux.yml up -d
```

---

## 📚 Helpful Resources

- **Full guide:** [LINUX_DEPLOYMENT.md](LINUX_DEPLOYMENT.md)
- **Implementation summary:** [LINUX_SUPPORT_SUMMARY.md](LINUX_SUPPORT_SUMMARY.md)
- **Ubuntu firewall:** `man ufw`
- **Systemd:** `man systemctl`
- **Docker:** `docker help`
- **Nginx:** `man nginx`

---

## 💡 Pro Tips

1. **Always test locally first** — Deploy to dev server before production
2. **Backup before changes** — `tar -czf backup.tar.gz /var/www/`
3. **Use SSH keys** — Avoid password auth for remote servers
4. **Monitor logs** — Set up log aggregation (ELK, Datadog, etc.)
5. **Schedule updates** — Automate system package updates
6. **Test failover** — Practice recovery procedures
7. **Document config** — Keep `deploy-config.json` and `.env` versioned (without secrets)
8. **Use Let's Encrypt** — Automate certificate renewal with systemd timers

---

**Bookmark this page for quick reference! 📌**
