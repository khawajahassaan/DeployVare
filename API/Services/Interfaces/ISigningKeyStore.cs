using System.Security.Cryptography;

namespace API.Services.Interfaces;

public sealed record SigningKeyInfo(string KeyId, string Algorithm, string PublicKey, DateTime CreatedAt, string CreatedBy, string Location, string Protection);

/// <summary>
/// The private signing key of this installation. It lives outside the application folder, so a DeployVare
/// installation handed to a partner has no key and can never issue licences or approve activations.
/// </summary>
public interface ISigningKeyStore
{
    bool HasKey { get; }

    string Location { get; }

    SigningKeyInfo? GetInfo();

    /// <summary>Creates the key pair; fails when one already exists, so an existing key can never be replaced by accident.</summary>
    SigningKeyInfo Create(string createdBy);

    /// <summary>The private key for signing. Caller disposes.</summary>
    ECDsa LoadPrivateKey();
}
