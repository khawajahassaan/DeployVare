using API.Models.Entities;
using API.Models.Enums;

namespace API.Utilities;

/// <summary>
/// Picks the opposite-type server (API ↔ frontend) for a combined manual-deploy package.
/// </summary>
public static class ManualPackagePairing
{
    public static ApplicationServer Resolve(
        ApplicationServer selected,
        IReadOnlyList<ApplicationServer> counterparts,
        int? requestedId)
    {
        if (counterparts.Count == 0)
        {
            var needed = selected.ServerType == ServerType.Api ? "frontend" : "API";
            throw new InvalidOperationException(
                $"A combined package needs both an API server and a frontend server on this application. Add a {needed} server, or choose This server only.");
        }

        if (requestedId is int id)
        {
            return counterparts.FirstOrDefault(s => s.Id == id)
                ?? throw new InvalidOperationException(
                    "The other server is not on this application, or it is the same type as the selected server.");
        }

        if (counterparts.Count == 1)
            return counterparts[0];

        var sameHost = counterparts
            .Where(s => string.Equals(s.HostName, selected.HostName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameHost.Count == 1)
            return sameHost[0];

        throw new InvalidOperationException(
            "This application has more than one matching server. Select which API or frontend server to include in the combined package.");
    }
}
