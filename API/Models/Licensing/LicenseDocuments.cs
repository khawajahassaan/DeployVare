namespace API.Models.Licensing;

/// <summary>
/// Envelope of every signed licensing file. The payload is kept base64-encoded so the exact bytes that were
/// signed are the bytes that are verified, whatever a JSON library does with property order or whitespace.
/// </summary>
public sealed class SignedDocument
{
    public const string LicenseKind = "license";
    public const string ActivationRequestKind = "activation-request";
    public const string ActivationResponseKind = "activation-response";

    public string Kind { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    /// <summary>Base64 of the JSON payload.</summary>
    public string Payload { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>Signing key that produced the signature; empty for machine-signed requests.</summary>
    public string? KeyId { get; set; }
}

/// <summary>What we sign when a licence is issued; the .lic file the partner receives.</summary>
public sealed class LicensePayload
{
    public string LicenseId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<string> Features { get; set; } = new();
    public int MaxActivations { get; set; } = 1;
    public string KeyId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}

/// <summary>
/// What a client server asks for. Signed with the machine's own private key, which proves the requester holds
/// the private half of the public key it is asking us to bind.
/// </summary>
public sealed class ActivationRequestPayload
{
    public string LicenseId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;

    /// <summary>Base64 SubjectPublicKeyInfo of the machine key.</summary>
    public string MachinePublicKey { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    /// <summary>Site name, deployment path or similar, shown to the administrator who approves.</summary>
    public string? ServerIdentity { get; set; }

    /// <summary>Random value echoed in the response, so an old response cannot be replayed as a new one.</summary>
    public string Nonce { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>What we sign back: this licence, on this machine, until this date.</summary>
public sealed class ActivationResponsePayload
{
    public string LicenseId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string MachinePublicKey { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public string ActivationId { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
}
