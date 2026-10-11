using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Models.Licensing;

namespace API.Utilities;

/// <summary>
/// Signing and verification for licences and activations: ECDSA on the NIST P-256 curve with SHA-256.
/// Only standard .NET cryptography is used. The same routines run in DeployVare (signing and verifying) and
/// in the licensed application (verifying only, with the public key).
/// </summary>
public static class LicenseSigner
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions FileJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static ECDsa CreateKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Serializes a payload and returns it base64-encoded; these exact bytes are what gets signed.</summary>
    public static string Encode<T>(T payload) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, Json));

    public static T Decode<T>(string payloadBase64)
    {
        var bytes = Convert.FromBase64String(payloadBase64);
        return JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new InvalidOperationException("The signed payload is empty.");
    }

    public static string Sign(ECDsa key, string payloadBase64) =>
        Convert.ToBase64String(key.SignData(Convert.FromBase64String(payloadBase64), HashAlgorithmName.SHA256));

    public static bool Verify(string publicKeyBase64, string payloadBase64, string signatureBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(
                Convert.FromBase64String(payloadBase64),
                Convert.FromBase64String(signatureBase64),
                HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            // A malformed key, payload or signature is simply an invalid signature
            return false;
        }
    }

    /// <summary>Short, stable identifier of a machine key, used for display and for uniqueness checks.</summary>
    public static string Fingerprint(string publicKeyBase64) =>
        Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(publicKeyBase64)))[..32];

    public static string ToFile(SignedDocument document) => JsonSerializer.Serialize(document, FileJson);

    public static SignedDocument ParseFile(string content)
    {
        SignedDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SignedDocument>(content, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The file is not a valid licensing file.", ex);
        }

        if (document == null || string.IsNullOrWhiteSpace(document.Payload) || string.IsNullOrWhiteSpace(document.Signature))
            throw new InvalidOperationException("The file is not a valid licensing file.");

        return document;
    }
}
