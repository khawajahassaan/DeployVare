namespace API.Models;

/// <summary>
/// How DeployVare connects to SQL Server to inspect it and create logins: an admin SQL login entered by the
/// user, or Windows authentication as the SSH user. ToString() never includes the password.
/// </summary>
public sealed class SqlAdminCredentials
{
    public bool UseWindowsAuthentication { get; }
    public string? Login { get; }
    public string? Password { get; }

    private SqlAdminCredentials(bool useWindowsAuthentication, string? login, string? password)
    {
        UseWindowsAuthentication = useWindowsAuthentication;
        Login = login;
        Password = password;
    }

    public static SqlAdminCredentials Windows() => new(true, null, null);

    public static SqlAdminCredentials Sql(string login, string password) => new(false, login, password);

    public override string ToString() => UseWindowsAuthentication ? "Windows authentication" : $"SQL login {Login}";
}
