namespace API.Models.Enums;

public enum LicenseStatus
{
    /// <summary>Issued and handed to the partner, but not yet activated on a server.</summary>
    Issued,

    /// <summary>Activated and bound to a machine.</summary>
    Active,

    /// <summary>Withdrawn by an administrator; no server may activate or keep using it.</summary>
    Revoked
}

public enum ActivationStatus
{
    /// <summary>The machine this licence is currently bound to.</summary>
    Active,

    /// <summary>Released by an administrator (transfer or revoke), so another machine may activate.</summary>
    Deactivated
}
