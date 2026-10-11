namespace API.DTOs.Requests;

/// <summary>
/// Validation for a server's SSH host: an IP address or DNS name only. The port has its own field, so
/// "host:port", URLs and whitespace are rejected (IPv6 addresses, which contain several colons, are allowed).
/// </summary>
public static class ServerHostName
{
    public const string Pattern = @"^(?![^:]+:\d+$)(?!.*://)\S+$";

    public const string ErrorMessage =
        "Host Name must be only the IP address or DNS name (e.g. 20.203.78.63). Put the port in the SSH Port field.";
}
