using API.Services.Interfaces;
using System.Text.RegularExpressions;

namespace API.Services.Implementations;

/// <summary>
/// Linux (Ubuntu/Debian) server management via SSH.
/// Uses bash scripts and standard Linux utilities (systemctl, apt-get, etc.).
/// </summary>
public class LinuxServerService : ILinuxServerService
{
    private readonly ISshService _ssh;
    private readonly ILogger<LinuxServerService> _logger;

    public LinuxServerService(ISshService ssh, ILogger<LinuxServerService> logger)
    {
        _ssh = ssh;
        _logger = logger;
    }

    public async Task<(bool Installed, string? Version, string? Error)> CheckDotNetAsync(
        string host, int port, string username, string? password, string? privateKey, int minimumMajor)
    {
        try
        {
            var cmd = "dotnet --version 2>/dev/null || echo 'NOT_FOUND'";
            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            var version = output.Trim();
            if (version == "NOT_FOUND")
                return (false, null, "dotnet command not found");

            var major = int.Parse(version.Split('.')[0]);
            if (major < minimumMajor)
                return (false, version, $".NET {major} does not meet minimum requirement of {minimumMajor}");

            return (true, version, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking .NET on {Host}", host);
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string Output)> InstallDotNetAsync(
        string host, int port, string username, string? password, string? privateKey, int majorVersion, int timeoutMinutes = 15)
    {
        try
        {
            // Add Microsoft package repository
            var addRepo = $"""
                wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb && \
                sudo dpkg -i /tmp/packages-microsoft-prod.deb > /dev/null 2>&1 && \
                rm /tmp/packages-microsoft-prod.deb
                """;

            var (repoSuccess, _, repoError) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, addRepo);
            if (!repoSuccess)
            {
                _logger.LogWarning("Failed to add Microsoft repo on {Host}: {Error}", host, repoError);
            }

            // Install .NET SDK and Runtime
            var installCmd = $"""
                sudo apt-get update -qq && \
                sudo apt-get install -y dotnet-sdk-{majorVersion} dotnet-runtime-{majorVersion} > /tmp/dotnet-install.log 2>&1 && \
                dotnet --version
                """;

            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(
                host, port, username, password, privateKey, installCmd,
                timeoutSeconds: timeoutMinutes * 60);

            if (!success)
            {
                return (false, $"Installation failed: {error}");
            }

            return (true, $".NET {majorVersion} installed: {output.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing .NET on {Host}", host);
            return (false, $"Exception: {ex.Message}");
        }
    }

    public async Task<(bool Running, string? Version, int? Port, string? Error)> CheckRedisAsync(
        string host, int port, string username, string? password, string? privateKey, int redisPort)
    {
        try
        {
            // Check if Redis service is running
            var checkService = $"sudo systemctl is-active redis-server 2>/dev/null && echo 'RUNNING' || echo 'STOPPED'";
            var (_, output, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, checkService);

            var running = output.Trim() == "RUNNING";

            // Try to get version
            var versionCmd = "redis-cli --version 2>/dev/null || echo 'UNKNOWN'";
            var (_, versionOutput, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, versionCmd);
            var version = ExtractVersion(versionOutput);

            return (running, version, redisPort, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Redis on {Host}", host);
            return (false, null, null, ex.Message);
        }
    }

    public async Task<(bool Success, string Output)> InstallRedisAsync(
        string host, int port, string username, string? password, string? privateKey, int redisPort, int timeoutMinutes = 10)
    {
        try
        {
            var cmd = $"""
                sudo apt-get update -qq && \
                sudo apt-get install -y redis-server > /tmp/redis-install.log 2>&1 && \
                sudo sed -i 's/^port .*/port {redisPort}/' /etc/redis/redis.conf && \
                sudo systemctl enable redis-server > /dev/null 2>&1 && \
                sudo systemctl start redis-server > /dev/null 2>&1 && \
                redis-cli ping
                """;

            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(
                host, port, username, password, privateKey, cmd,
                timeoutSeconds: timeoutMinutes * 60);

            if (!success)
            {
                return (false, $"Installation failed: {error}");
            }

            return (true, $"Redis installed and running on port {redisPort}. Response: {output.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing Redis on {Host}", host);
            return (false, $"Exception: {ex.Message}");
        }
    }

    public async Task<(bool Installed, bool Running, string? Error)> CheckNginxAsync(
        string host, int port, string username, string? password, string? privateKey)
    {
        try
        {
            var checkCmd = $"""
                which nginx > /dev/null 2>&1 && echo 'INSTALLED' || echo 'NOT_INSTALLED'
                """;

            var (_, output, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, checkCmd);
            var installed = output.Trim() == "INSTALLED";

            if (!installed)
                return (false, false, null);

            // Check if running
            var statusCmd = $"sudo systemctl is-active nginx 2>/dev/null && echo 'RUNNING' || echo 'STOPPED'";
            var (_, statusOutput, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, statusCmd);
            var running = statusOutput.Trim() == "RUNNING";

            return (true, running, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Nginx on {Host}", host);
            return (false, false, ex.Message);
        }
    }

    public async Task<(bool Success, string Output)> InstallNginxAsync(
        string host, int port, string username, string? password, string? privateKey, int timeoutMinutes = 10)
    {
        try
        {
            var cmd = $"""
                sudo apt-get update -qq && \
                sudo apt-get install -y nginx > /tmp/nginx-install.log 2>&1 && \
                sudo systemctl enable nginx > /dev/null 2>&1 && \
                sudo systemctl start nginx > /dev/null 2>&1 && \
                nginx -v
                """;

            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(
                host, port, username, password, privateKey, cmd,
                timeoutSeconds: timeoutMinutes * 60);

            if (!success)
            {
                return (false, $"Installation failed: {error}");
            }

            return (true, $"Nginx installed and started. {output.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing Nginx on {Host}", host);
            return (false, $"Exception: {ex.Message}");
        }
    }

    public async Task<(bool Installed, bool Running, string? Error)> CheckApacheAsync(
        string host, int port, string username, string? password, string? privateKey)
    {
        try
        {
            var checkCmd = $"which apache2ctl > /dev/null 2>&1 && echo 'INSTALLED' || echo 'NOT_INSTALLED'";
            var (_, output, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, checkCmd);
            var installed = output.Trim() == "INSTALLED";

            if (!installed)
                return (false, false, null);

            var statusCmd = $"sudo systemctl is-active apache2 2>/dev/null && echo 'RUNNING' || echo 'STOPPED'";
            var (_, statusOutput, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, statusCmd);
            var running = statusOutput.Trim() == "RUNNING";

            return (true, running, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Apache on {Host}", host);
            return (false, false, ex.Message);
        }
    }

    public async Task<(bool Success, string Output)> InstallApacheAsync(
        string host, int port, string username, string? password, string? privateKey, int timeoutMinutes = 10)
    {
        try
        {
            var cmd = $"""
                sudo apt-get update -qq && \
                sudo apt-get install -y apache2 libapache2-mod-proxy-html libapache2-mod-rewrite > /tmp/apache-install.log 2>&1 && \
                sudo a2enmod proxy proxy_http rewrite ssl headers > /dev/null 2>&1 && \
                sudo systemctl enable apache2 > /dev/null 2>&1 && \
                sudo systemctl start apache2 > /dev/null 2>&1 && \
                apache2ctl -v | head -1
                """;

            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(
                host, port, username, password, privateKey, cmd,
                timeoutSeconds: timeoutMinutes * 60);

            if (!success)
            {
                return (false, $"Installation failed: {error}");
            }

            return (true, $"Apache2 installed and started. {output.Trim()}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing Apache on {Host}", host);
            return (false, $"Exception: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Error)> InstallCertificateAsync(
        string host, int port, string username, string? password, string? privateKey, string certPem, string keyPem)
    {
        try
        {
            // Verify cert directory exists
            var mkdirCmd = "sudo mkdir -p /etc/deployvare/ssl && sudo chmod 700 /etc/deployvare/ssl";
            await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, mkdirCmd);

            // Upload certificate files via SFTP
            var certPath = "/tmp/certificate.crt";
            var keyPath = "/tmp/certificate.key";

            // Note: ISshService should have SFTP upload capability; for now using bash echo
            var certCmd = $@"sudo bash -c 'cat > /etc/deployvare/ssl/certificate.crt << 'EOF'
{certPem}
EOF
'";

            var keyCmd = $@"sudo bash -c 'cat > /etc/deployvare/ssl/certificate.key << 'EOF'
{keyPem}
EOF
chmod 600 /etc/deployvare/ssl/certificate.key'";

            var (certSuccess, _, certError) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, certCmd);
            if (!certSuccess)
                return (false, $"Failed to install certificate: {certError}");

            var (keySuccess, _, keyError) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, keyCmd);
            if (!keySuccess)
                return (false, $"Failed to install key: {keyError}");

            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing certificate on {Host}", host);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Error)> ConfigureNginxSiteAsync(
        string host, int port, string username, string? password, string? privateKey, string siteName,
        string siteHostName, string deploymentPath, bool useSsl = true)
    {
        try
        {
            // Build Nginx config (simplified; real implementation would be more sophisticated)
            var sslBlock = useSsl ? $"""
                ssl_certificate /etc/deployvare/ssl/certificate.crt;
                ssl_certificate_key /etc/deployvare/ssl/certificate.key;
                """ : "";

            var config = $@"
upstream dotnet_backend {{
    server 127.0.0.1:5000 max_fails=3 fail_timeout=30s;
}}

server {{
    listen 80;
    server_name {siteHostName};
    return 301 https://$server_name$request_uri;
}}

server {{
    listen 443 ssl http2;
    server_name {siteHostName};
    root {deploymentPath};

    {sslBlock}

    add_header Strict-Transport-Security ""max-age=31536000; includeSubDomains"" always;

    location / {{
        proxy_pass http://dotnet_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }}
}}
";

            var cmd = $@"sudo bash -c 'cat > /etc/nginx/sites-available/{siteName} << 'EOF'
{config}
EOF
' && \
sudo ln -sf /etc/nginx/sites-available/{siteName} /etc/nginx/sites-enabled/{siteName} && \
sudo nginx -t && \
sudo systemctl reload nginx";

            var (success, _, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            if (!success)
                return (false, $"Nginx configuration failed: {error}");

            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring Nginx site on {Host}", host);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Error)> ConfigureApacheSiteAsync(
        string host, int port, string username, string? password, string? privateKey, string siteName,
        string siteHostName, string deploymentPath, bool useSsl = true)
    {
        try
        {
            var sslBlock = useSsl ? $"""
                SSLEngine On
                SSLCertificateFile /etc/deployvare/ssl/certificate.crt
                SSLCertificateKeyFile /etc/deployvare/ssl/certificate.key
                """ : "";

            var config = $@"
<VirtualHost *:80>
    ServerName {siteHostName}
    RewriteEngine On
    RewriteRule ^(.*)$ https://%{{HTTP_HOST}}%{{REQUEST_URI}} [L,R=301]
</VirtualHost>

<VirtualHost *:443>
    ServerName {siteHostName}
    DocumentRoot {deploymentPath}

    {sslBlock}

    Header always set Strict-Transport-Security ""max-age=31536000; includeSubDomains""

    ProxyPreserveHost On
    ProxyPass / http://127.0.0.1:5000/ timeout=3600
    ProxyPassReverse / http://127.0.0.1:5000/
</VirtualHost>
";

            var cmd = $@"sudo bash -c 'cat > /etc/apache2/sites-available/{siteName}.conf << 'EOF'
{config}
EOF
' && \
sudo a2ensite {siteName} > /dev/null 2>&1 && \
sudo a2dissite 000-default > /dev/null 2>&1 && \
sudo apache2ctl configtest && \
sudo systemctl reload apache2";

            var (success, _, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            if (!success)
                return (false, $"Apache configuration failed: {error}");

            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring Apache site on {Host}", host);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Error)> CreateSystemdServiceAsync(
        string host, int port, string username, string? password, string? privateKey, string serviceName,
        string appPath, string? environmentFile = null)
    {
        try
        {
            var envBlock = string.IsNullOrEmpty(environmentFile)
                ? ""
                : $"EnvironmentFile={environmentFile}";

            var serviceContent = $@"
[Unit]
Description=DeployVare {serviceName}
After=network.target redis-server.service
Wants=redis-server.service

[Service]
Type=notify
User=www-data
WorkingDirectory={appPath}
ExecStart={appPath}/API
Restart=on-failure
RestartSec=5s
StandardOutput=journal
StandardError=journal
SyslogIdentifier={serviceName}
Environment=""ASPNETCORE_ENVIRONMENT=Production""
Environment=""ASPNETCORE_URLS=http://127.0.0.1:5000""
Environment=""DOTNET_CLI_TELEMETRY_OPTOUT=1""
{envBlock}

[Install]
WantedBy=multi-user.target
";

            var cmd = $@"sudo bash -c 'cat > /etc/systemd/system/{serviceName}.service << 'EOF'
{serviceContent}
EOF
' && \
sudo systemctl daemon-reload";

            var (success, _, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            if (!success)
                return (false, $"Failed to create systemd service: {error}");

            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating systemd service on {Host}", host);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Error)> StartServiceAsync(
        string host, int port, string username, string? password, string? privateKey, string serviceName)
    {
        try
        {
            var cmd = $"sudo systemctl enable {serviceName} > /dev/null 2>&1 && sudo systemctl start {serviceName}";
            var (success, _, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            if (!success)
                return (false, $"Failed to start service: {error}");

            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting service on {Host}", host);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Running, string? Status, string? Error)> GetServiceStatusAsync(
        string host, int port, string username, string? password, string? privateKey, string serviceName)
    {
        try
        {
            var cmd = $"sudo systemctl status {serviceName}";
            var (success, output, error) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            var running = output.Contains("active (running)");
            var status = output.Split('\n').FirstOrDefault()?.Trim();

            return (running, status, success ? null : error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service status on {Host}", host);
            return (false, null, ex.Message);
        }
    }

    public async Task<LinuxSystemFacts?> GetSystemFactsAsync(
        string host, int port, string username, string? password, string? privateKey)
    {
        try
        {
            var cmd = $@"bash -c '
            echo ""HOSTNAME:$(hostname)""
            echo ""OS:$(lsb_release -ds 2>/dev/null || echo Linux)""
            echo ""KERNEL:$(uname -r)""
            echo ""CPU:$(nproc)""
            echo ""MEMORY:$(free -m | awk '\''NR==2{{print $2}}'\'')""
            echo ""DISK_FREE:$(df /root | awk '\''NR==2{{print $4}}'\'')""
            echo ""DISK_TOTAL:$(df /root | awk '\''NR==2{{print $2}}'\'')""
            echo ""DOTNET:$(dotnet --version 2>/dev/null || echo none)""
            echo ""NGINX:$(systemctl is-active nginx 2>/dev/null || echo inactive)""
            echo ""APACHE:$(systemctl is-active apache2 2>/dev/null || echo inactive)""
            echo ""REDIS:$(systemctl is-active redis-server 2>/dev/null || echo inactive)""
            '";

            var (success, output, _) = await _ssh.ExecuteBashCommandAsync(host, port, username, password, privateKey, cmd);

            if (!success)
                return null;

            var facts = new LinuxSystemFacts();
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(':');
                if (parts.Length < 2) continue;

                var key = parts[0].Trim();
                var value = string.Join(":", parts.Skip(1)).Trim();

                switch (key)
                {
                    case "HOSTNAME":
                        facts.HostName = value;
                        break;
                    case "OS":
                        facts.Os = value;
                        break;
                    case "KERNEL":
                        facts.KernelVersion = value;
                        break;
                    case "CPU":
                        if (int.TryParse(value, out var cpu)) facts.CpuCount = cpu;
                        break;
                    case "MEMORY":
                        if (long.TryParse(value, out var mem)) facts.MemoryMb = mem;
                        break;
                    case "DISK_FREE":
                        if (long.TryParse(value, out var free)) facts.DiskFreeMb = free;
                        break;
                    case "DISK_TOTAL":
                        if (long.TryParse(value, out var total)) facts.DiskTotalMb = total;
                        break;
                    case "DOTNET":
                        facts.InstalledPackages["dotnet"] = value != "none";
                        break;
                    case "NGINX":
                        facts.RunningServices["nginx"] = value == "active";
                        break;
                    case "APACHE":
                        facts.RunningServices["apache2"] = value == "active";
                        break;
                    case "REDIS":
                        facts.RunningServices["redis"] = value == "active";
                        break;
                }
            }

            return facts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system facts from {Host}", host);
            return null;
        }
    }

    private static string? ExtractVersion(string output)
    {
        var match = Regex.Match(output, @"v?(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }
}
