using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;
using API.Models.Licensing;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public class LicensingService : ILicensingService
{
    private readonly DeployVareDbContext _context;
    private readonly ISigningKeyStore _keyStore;
    private readonly LicensingOptions _options;
    private readonly ILogger<LicensingService> _logger;

    public LicensingService(
        DeployVareDbContext context,
        ISigningKeyStore keyStore,
        IOptions<LicensingOptions> options,
        ILogger<LicensingService> logger)
    {
        _context = context;
        _keyStore = keyStore;
        _options = options.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------------------------------
    // Signing key
    // -------------------------------------------------------------------------------------------------
    public SigningKeyDto GetSigningKey()
    {
        var info = _keyStore.GetInfo();
        return info == null
            ? new SigningKeyDto { Exists = false, Location = _keyStore.Location }
            : new SigningKeyDto
            {
                Exists = true,
                KeyId = info.KeyId,
                Algorithm = info.Algorithm,
                PublicKey = info.PublicKey,
                CreatedAt = info.CreatedAt,
                CreatedBy = info.CreatedBy,
                Location = info.Location,
                Protection = info.Protection
            };
    }

    public SigningKeyDto CreateSigningKey(string createdBy)
    {
        try
        {
            var info = _keyStore.Create(createdBy);
            _logger.LogInformation("Signing key {KeyId} created by {User}", info.KeyId, createdBy);
        }
        catch (InvalidOperationException ex)
        {
            throw new LicensingException(ex.Message, ex);
        }

        return GetSigningKey();
    }

    // -------------------------------------------------------------------------------------------------
    // Licences
    // -------------------------------------------------------------------------------------------------
    public async Task<List<LicenseDto>> GetLicensesAsync(int? clientId)
    {
        var query = _context.Licenses
            .Include(l => l.Client)
            .Include(l => l.Activations)
            .AsNoTracking()
            .AsQueryable();

        if (clientId.HasValue)
            query = query.Where(l => l.ClientId == clientId.Value);

        var licenses = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        return licenses.Select(MapToDto).ToList();
    }

    public async Task<LicenseDto?> GetLicenseAsync(int id)
    {
        var license = await LoadAsync(id, track: false);
        return license == null ? null : MapToDto(license);
    }

    public async Task<LicenseDto> IssueAsync(IssueLicenseRequest request, string issuedBy)
    {
        RequireSigningKey();

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId)
            ?? throw new LicensingException($"Client {request.ClientId} was not found.");

        if (!client.IsActive)
            throw new LicensingException($"Client '{client.Name}' is inactive, so no licence can be issued for it.");

        var product = string.IsNullOrWhiteSpace(request.Product) ? _options.DefaultProduct : request.Product.Trim();
        var licenseId = string.IsNullOrWhiteSpace(request.LicenseId)
            ? await BuildLicenseIdAsync(client.ClientCode, product)
            : request.LicenseId.Trim().ToUpperInvariant();

        if (await _context.Licenses.AnyAsync(l => l.LicenseId == licenseId))
            throw new LicensingException($"Licence '{licenseId}' already exists.");

        var issuedAt = DateTime.UtcNow;
        var expiresAt = request.ExpiresAt?.ToUniversalTime() ?? issuedAt.AddDays(_options.DefaultValidityDays);
        if (expiresAt <= issuedAt)
            throw new LicensingException("The expiry date must be in the future.");

        var features = (request.Features ?? new List<string>())
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var license = new License
        {
            ClientId = client.Id,
            LicenseId = licenseId,
            Product = product,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            MaxActivations = request.MaxActivations,
            Status = LicenseStatus.Issued,
            Features = features.Count > 0 ? string.Join('\n', features) : null,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = issuedBy,
            CreatedAt = issuedAt
        };

        Sign(license, client);

        _context.Licenses.Add(license);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Licence {LicenseId} issued for client {ClientCode} ({Product}) by {User}, expires {ExpiresAt:u}",
            license.LicenseId, client.ClientCode, product, issuedBy, expiresAt);

        return MapToDto(await LoadAsync(license.Id, track: false) ?? license);
    }

    public async Task<LicensingFileDto> GetLicenseFileAsync(int id)
    {
        var license = await LoadAsync(id, track: false)
            ?? throw new LicensingException($"Licence {id} was not found.");

        var document = new SignedDocument
        {
            Kind = SignedDocument.LicenseKind,
            Payload = license.Payload,
            Signature = license.Signature,
            Algorithm = LicenseSigner.Algorithm,
            KeyId = license.KeyId
        };

        return new LicensingFileDto
        {
            FileName = $"{license.LicenseId}.lic",
            Content = LicenseSigner.ToFile(document)
        };
    }

    public async Task<LicenseDto> RenewAsync(int id, DateTime expiresAt, string performedBy)
    {
        RequireSigningKey();

        var license = await LoadAsync(id, track: true)
            ?? throw new LicensingException($"Licence {id} was not found.");

        if (license.Status == LicenseStatus.Revoked)
            throw new LicensingException("A revoked licence cannot be renewed.");

        var newExpiry = expiresAt.ToUniversalTime();
        if (newExpiry <= DateTime.UtcNow)
            throw new LicensingException("The expiry date must be in the future.");

        license.ExpiresAt = newExpiry;
        license.UpdatedAt = DateTime.UtcNow;
        Sign(license, license.Client);

        await _context.SaveChangesAsync();

        _logger.LogInformation("Licence {LicenseId} renewed until {ExpiresAt:u} by {User}", license.LicenseId, newExpiry, performedBy);

        // The bound server keeps working, but needs the new .lic file before the old expiry date
        return MapToDto(license);
    }

    // -------------------------------------------------------------------------------------------------
    // Activation
    // -------------------------------------------------------------------------------------------------
    public async Task<ActivationReviewDto> ReviewActivationAsync(string requestFile)
    {
        var (payload, _) = ParseActivationRequest(requestFile);
        var license = await FindLicenseAsync(payload.LicenseId);
        return BuildReview(payload, license);
    }

    public async Task<ActivationApprovalDto> ApproveActivationAsync(string requestFile, string approvedBy)
    {
        RequireSigningKey();

        var (payload, fingerprint) = ParseActivationRequest(requestFile);
        var license = await FindLicenseAsync(payload.LicenseId);

        var review = BuildReview(payload, license);
        if (!review.CanApprove || license == null)
            throw new LicensingException(review.Message);

        var existing = license.Activations.FirstOrDefault(a => a.MachineFingerprint == fingerprint);
        var activation = existing;

        if (activation == null)
        {
            activation = new LicenseActivation
            {
                LicenseId = license.Id,
                MachinePublicKey = payload.MachinePublicKey,
                MachineFingerprint = fingerprint,
                MachineName = Truncate(payload.MachineName, 200),
                ServerIdentity = Truncate(payload.ServerIdentity, 500),
                Status = ActivationStatus.Active,
                ActivatedAt = DateTime.UtcNow,
                ActivatedBy = approvedBy,
                CreatedAt = DateTime.UtcNow
            };
            license.Activations.Add(activation);
        }
        else
        {
            // Same machine asking again (re-deployment, lost response file): idempotent, no new binding
            activation.Status = ActivationStatus.Active;
            activation.DeactivatedAt = null;
            activation.DeactivationReason = null;
            activation.MachineName = Truncate(payload.MachineName, 200) ?? activation.MachineName;
            activation.ServerIdentity = Truncate(payload.ServerIdentity, 500) ?? activation.ServerIdentity;
            activation.UpdatedAt = DateTime.UtcNow;
        }

        var response = new ActivationResponsePayload
        {
            LicenseId = license.LicenseId,
            CustomerId = license.Client.ClientCode,
            Product = license.Product,
            MachinePublicKey = payload.MachinePublicKey,
            MachineFingerprint = fingerprint,
            ActivationId = Guid.NewGuid().ToString("N"),
            ActivatedAt = activation.ActivatedAt,
            ExpiresAt = license.ExpiresAt,
            Nonce = payload.Nonce,
            KeyId = license.KeyId
        };

        var responsePayload = LicenseSigner.Encode(response);
        using (var key = _keyStore.LoadPrivateKey())
        {
            activation.ResponsePayload = responsePayload;
            activation.ResponseSignature = LicenseSigner.Sign(key, responsePayload);
        }

        activation.LastIssuedAt = DateTime.UtcNow;
        license.Status = LicenseStatus.Active;
        license.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // The unique index is the real guard: two approvals racing for the same licence cannot both win
            _logger.LogWarning(ex, "Concurrent activation rejected for licence {LicenseId}", license.LicenseId);
            throw new LicensingException(
                $"This licence was activated on another server a moment ago. Licence: {license.LicenseId}. Transfer it before activating a different server.", ex);
        }

        _logger.LogInformation("Licence {LicenseId} activated on machine {Fingerprint} ({MachineName}) by {User}",
            license.LicenseId, fingerprint, payload.MachineName, approvedBy);

        var document = new SignedDocument
        {
            Kind = SignedDocument.ActivationResponseKind,
            Payload = activation.ResponsePayload,
            Signature = activation.ResponseSignature,
            Algorithm = LicenseSigner.Algorithm,
            KeyId = license.KeyId
        };

        var saved = await LoadAsync(license.Id, track: false) ?? license;
        return new ActivationApprovalDto
        {
            Review = BuildReview(payload, saved),
            ResponseFile = new LicensingFileDto
            {
                FileName = $"{license.LicenseId}.activation-response.json",
                Content = LicenseSigner.ToFile(document)
            },
            License = MapToDto(saved)
        };
    }

    public async Task<LicenseDto> TransferAsync(int id, string? reason, string performedBy)
    {
        var license = await LoadAsync(id, track: true)
            ?? throw new LicensingException($"Licence {id} was not found.");

        if (license.Status == LicenseStatus.Revoked)
            throw new LicensingException("A revoked licence cannot be transferred. Issue a new licence instead.");

        var current = license.Activations.FirstOrDefault(a => a.Status == ActivationStatus.Active)
            ?? throw new LicensingException("This licence is not activated on any server, so there is nothing to transfer.");

        Deactivate(current, reason ?? "Transferred to another server", performedBy);
        license.Status = LicenseStatus.Issued;
        license.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Licence {LicenseId} released from machine {Fingerprint} by {User}: {Reason}",
            license.LicenseId, current.MachineFingerprint, performedBy, reason);

        return MapToDto(license);
    }

    public async Task<LicenseDto> RevokeAsync(int id, string? reason, string performedBy)
    {
        var license = await LoadAsync(id, track: true)
            ?? throw new LicensingException($"Licence {id} was not found.");

        foreach (var activation in license.Activations.Where(a => a.Status == ActivationStatus.Active))
            Deactivate(activation, reason ?? "Licence revoked", performedBy);

        license.Status = LicenseStatus.Revoked;
        license.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Licence {LicenseId} revoked by {User}: {Reason}", license.LicenseId, performedBy, reason);

        return MapToDto(license);
    }

    // -------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------
    private void RequireSigningKey()
    {
        if (!_keyStore.HasKey)
            throw new LicensingException(
                $"This installation has no signing key, so it cannot issue licences or approve activations. The key is expected at {_keyStore.Location}.");
    }

    private void Sign(License license, Client client)
    {
        var payload = new LicensePayload
        {
            LicenseId = license.LicenseId,
            CustomerId = client.ClientCode,
            CustomerName = client.Name,
            Product = license.Product,
            IssuedAt = license.IssuedAt,
            ExpiresAt = license.ExpiresAt,
            Features = ParseFeatures(license.Features),
            MaxActivations = license.MaxActivations,
            KeyId = _keyStore.GetInfo()!.KeyId
        };

        var encoded = LicenseSigner.Encode(payload);
        using var key = _keyStore.LoadPrivateKey();

        license.Payload = encoded;
        license.Signature = LicenseSigner.Sign(key, encoded);
        license.KeyId = payload.KeyId;
    }

    private static (ActivationRequestPayload Payload, string Fingerprint) ParseActivationRequest(string requestFile)
    {
        if (string.IsNullOrWhiteSpace(requestFile))
            throw new LicensingException("No activation request file was provided.");

        SignedDocument document;
        try
        {
            document = LicenseSigner.ParseFile(requestFile);
        }
        catch (InvalidOperationException ex)
        {
            throw new LicensingException("This file is not an activation request produced by a client server.", ex);
        }

        if (!string.Equals(document.Kind, SignedDocument.ActivationRequestKind, StringComparison.OrdinalIgnoreCase))
            throw new LicensingException($"This file is a '{document.Kind}' file, not an activation request.");

        ActivationRequestPayload payload;
        try
        {
            payload = LicenseSigner.Decode<ActivationRequestPayload>(document.Payload);
        }
        catch (Exception ex)
        {
            throw new LicensingException("The activation request could not be read.", ex);
        }

        if (string.IsNullOrWhiteSpace(payload.MachinePublicKey) || string.IsNullOrWhiteSpace(payload.LicenseId))
            throw new LicensingException("The activation request is incomplete.");

        // The request is signed by the machine key itself: this proves the sender holds the private half
        if (!LicenseSigner.Verify(payload.MachinePublicKey, document.Payload, document.Signature))
            throw new LicensingException("The activation request is not signed by the machine key it names, so it cannot be trusted.");

        return (payload, LicenseSigner.Fingerprint(payload.MachinePublicKey));
    }

    private async Task<License?> FindLicenseAsync(string licenseId) =>
        await _context.Licenses
            .Include(l => l.Client)
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.LicenseId == licenseId);

    private static ActivationReviewDto BuildReview(ActivationRequestPayload payload, License? license)
    {
        var fingerprint = LicenseSigner.Fingerprint(payload.MachinePublicKey);
        var review = new ActivationReviewDto
        {
            LicenseId = payload.LicenseId,
            Product = payload.Product,
            CustomerId = payload.CustomerId,
            MachineFingerprint = fingerprint,
            MachineName = payload.MachineName,
            ServerIdentity = payload.ServerIdentity,
            RequestedAt = payload.CreatedAt,
            ClientName = license?.Client.Name,
            LicenseExpiresAt = license?.ExpiresAt
        };

        if (license == null)
        {
            review.Outcome = "UnknownLicense";
            review.Message = $"Licence {payload.LicenseId} was not issued by this installation.";
            return review;
        }

        var current = license.Activations.FirstOrDefault(a => a.Status == ActivationStatus.Active);
        review.CurrentActivation = current == null ? null : MapActivation(current);

        if (license.Status == LicenseStatus.Revoked)
        {
            review.Outcome = "Revoked";
            review.Message = $"Licence {license.LicenseId} has been revoked.";
            return review;
        }

        if (license.ExpiresAt <= DateTime.UtcNow)
        {
            review.Outcome = "Expired";
            review.Message = $"Licence {license.LicenseId} expired on {license.ExpiresAt:yyyy-MM-dd}. Renew it before activating.";
            return review;
        }

        if (!string.Equals(license.Product, payload.Product, StringComparison.OrdinalIgnoreCase))
        {
            review.Outcome = "WrongProduct";
            review.Message = $"Licence {license.LicenseId} covers {license.Product}, but the request is for {payload.Product}.";
            return review;
        }

        if (!string.Equals(license.Client.ClientCode, payload.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            review.Outcome = "WrongCustomer";
            review.Message = $"Licence {license.LicenseId} belongs to a different client than the request states.";
            return review;
        }

        if (current != null && current.MachineFingerprint == fingerprint)
        {
            review.CanApprove = true;
            review.Outcome = "AlreadyActivatedOnThisMachine";
            review.Message = "This server is already activated. Approving re-issues the same activation file; no new activation is used.";
            return review;
        }

        if (current != null)
        {
            review.Outcome = "ActivatedElsewhere";
            review.Message = $"Licence {license.LicenseId} is already activated on another server. Transfer the licence before activating this one.";
            return review;
        }

        review.CanApprove = true;
        review.Outcome = "ReadyToActivate";
        review.Message = $"Licence {license.LicenseId} is free and will be bound to this server.";
        return review;
    }

    private static void Deactivate(LicenseActivation activation, string reason, string performedBy)
    {
        activation.Status = ActivationStatus.Deactivated;
        activation.DeactivatedAt = DateTime.UtcNow;
        activation.DeactivationReason = $"{reason} (by {performedBy})";
        activation.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<License?> LoadAsync(int id, bool track)
    {
        var query = _context.Licenses
            .Include(l => l.Client)
            .Include(l => l.Activations)
            .AsQueryable();

        if (!track) query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(l => l.Id == id);
    }

    private async Task<string> BuildLicenseIdAsync(string clientCode, string product)
    {
        // Capital initials first, so AuditVare becomes AV; products without two capitals use their first letters
        var initials = new string(product.Where(char.IsUpper).Take(2).ToArray());
        var prefix = (initials.Length >= 2
            ? initials
            : new string(product.Where(char.IsLetter).Take(2).ToArray())).ToUpperInvariant();
        if (prefix.Length < 2) prefix = "LC";

        var code = new string(clientCode.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var candidate = $"{prefix}-{code}";

        var suffix = 1;
        while (await _context.Licenses.AnyAsync(l => l.LicenseId == candidate))
            candidate = $"{prefix}-{code}-{++suffix}";

        return candidate;
    }

    private static List<string> ParseFeatures(string? features) =>
        string.IsNullOrWhiteSpace(features)
            ? new List<string>()
            : features.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];

    private static LicenseDto MapToDto(License license)
    {
        var activations = license.Activations
            .OrderByDescending(a => a.ActivatedAt)
            .ToList();

        return new LicenseDto
        {
            Id = license.Id,
            ClientId = license.ClientId,
            ClientName = license.Client?.Name ?? string.Empty,
            CustomerId = license.Client?.ClientCode ?? string.Empty,
            LicenseId = license.LicenseId,
            Product = license.Product,
            Status = license.Status.ToString(),
            IssuedAt = license.IssuedAt,
            ExpiresAt = license.ExpiresAt,
            IsExpired = license.ExpiresAt <= DateTime.UtcNow,
            MaxActivations = license.MaxActivations,
            Features = ParseFeatures(license.Features),
            Notes = license.Notes,
            KeyId = license.KeyId,
            CreatedBy = license.CreatedBy,
            CreatedAt = license.CreatedAt,
            UpdatedAt = license.UpdatedAt,
            FileName = $"{license.LicenseId}.lic",
            CurrentActivation = activations.Where(a => a.Status == ActivationStatus.Active).Select(MapActivation).FirstOrDefault(),
            PreviousActivations = activations.Where(a => a.Status != ActivationStatus.Active).Select(MapActivation).ToList()
        };
    }

    private static LicenseActivationDto MapActivation(LicenseActivation activation) => new()
    {
        Id = activation.Id,
        MachineFingerprint = activation.MachineFingerprint,
        MachineName = activation.MachineName,
        ServerIdentity = activation.ServerIdentity,
        Status = activation.Status.ToString(),
        ActivatedAt = activation.ActivatedAt,
        DeactivatedAt = activation.DeactivatedAt,
        LastIssuedAt = activation.LastIssuedAt,
        ActivatedBy = activation.ActivatedBy,
        DeactivationReason = activation.DeactivationReason
    };
}
