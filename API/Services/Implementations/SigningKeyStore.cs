using System.Security.Cryptography;
using System.Text.Json;
using API.Models;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public sealed class SigningKeyStore : ISigningKeyStore
{
    private sealed class StoredKey
    {
        public string KeyId { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string Protection { get; set; } = "file-permissions";
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private readonly LicensingOptions _options;
    private readonly ILogger<SigningKeyStore> _logger;
    private readonly object _gate = new();

    public SigningKeyStore(IOptions<LicensingOptions> options, ILogger<SigningKeyStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Location => Path.Combine(ResolveDirectory(), _options.KeyFileName);

    public bool HasKey => File.Exists(Location);

    public SigningKeyInfo? GetInfo()
    {
        var stored = Read();
        return stored == null
            ? null
            : new SigningKeyInfo(stored.KeyId, stored.Algorithm, stored.PublicKey, stored.CreatedAt, stored.CreatedBy, Location, stored.Protection);
    }

    public SigningKeyInfo Create(string createdBy)
    {
        lock (_gate)
        {
            if (HasKey)
                throw new InvalidOperationException($"A signing key already exists at {Location}. Remove it deliberately before creating a new one; licences signed with the old key stop verifying.");

            using var key = LicenseSigner.CreateKey();
            var stored = new StoredKey
            {
                KeyId = $"dv-{Guid.NewGuid():N}"[..15],
                Algorithm = LicenseSigner.Algorithm,
                PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                PrivateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };

            var directory = ResolveDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Location, JsonSerializer.Serialize(stored, LicenseSigner.FileJson));

            _logger.LogWarning("Licensing signing key {KeyId} created at {Location}. Restrict this folder to administrators and keep an offline backup: licences cannot be re-signed without it.",
                stored.KeyId, Location);

            return new SigningKeyInfo(stored.KeyId, stored.Algorithm, stored.PublicKey, stored.CreatedAt, stored.CreatedBy, Location, stored.Protection);
        }
    }

    public ECDsa LoadPrivateKey()
    {
        var stored = Read()
            ?? throw new InvalidOperationException($"No signing key was found at {Location}. Only the installation that holds the key can issue licences and approve activations.");

        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(stored.PrivateKey), out _);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private StoredKey? Read()
    {
        var path = Location;
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<StoredKey>(File.ReadAllText(path), LicenseSigner.Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "The signing key file at {Location} could not be read", path);
            throw new InvalidOperationException($"The signing key file at {path} could not be read.", ex);
        }
    }

    /// <summary>Configured folder; on a non-Windows development machine it falls back to the local app data folder.</summary>
    private string ResolveDirectory()
    {
        var configured = _options.KeyDirectory;
        if (OperatingSystem.IsWindows() || Path.IsPathRooted(configured) && !configured.Contains(':'))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DeployVare", "Licensing");
    }
}
