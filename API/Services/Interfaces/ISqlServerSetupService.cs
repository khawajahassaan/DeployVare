using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

/// <summary>
/// Inspects SQL Server on a registered server over SSH and creates SQL logins with database access, connecting
/// with an admin SQL login entered by the user (or Windows authentication as the SSH user). Passwords are never stored.
/// </summary>
public interface ISqlServerSetupService
{
    /// <summary>
    /// Installed instances and their authentication mode. When connection details are given, also connects and
    /// lists databases. Installed is false when SQL Server is absent.
    /// </summary>
    Task<SqlServerInfoDto> GetSqlServerInfoAsync(int serverId, SqlServerConnectRequest? connection);

    /// <summary>Users of a database with their mapped login, login state and database roles.</summary>
    Task<List<SqlDatabaseUserDto>> GetDatabaseUsersAsync(int serverId, SqlDatabaseUsersRequest request);

    /// <summary>Creates (or resets) a SQL login, maps it to a database user and adds it to db_owner.</summary>
    Task<ServerSetupActionResultDto> CreateSqlLoginAsync(int serverId, CreateSqlLoginRequest request);

    /// <summary>Switches an instance to SQL Server and Windows authentication and restarts it.</summary>
    Task<ServerSetupActionResultDto> EnableSqlAuthenticationAsync(int serverId, string instanceName);

    /// <summary>
    /// Installs SQL Server Express silently from the Installers folder (mixed mode, TCP enabled). Uses the full
    /// offline installer; the SSEI web bootstrapper is rejected because client servers have no internet.
    /// </summary>
    Task<ServerSetupActionResultDto> InstallSqlServerAsync(int serverId, InstallSqlServerRequest request);
}
