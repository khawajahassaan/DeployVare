using API.DTOs.Requests;
using API.Services.Implementations;

namespace API.Tests.ServerSetup;

public class SqlServerSetupServiceTests
{
    [Fact]
    public void ToCredentials_WithNothingEntered_InspectsWithoutConnecting()
    {
        Assert.Null(SqlServerSetupService.ToCredentials(null));
        Assert.Null(SqlServerSetupService.ToCredentials(new SqlServerConnectRequest()));
    }

    [Fact]
    public void ToCredentials_UsesSqlAdminLogin_AndNeverPrintsPassword()
    {
        var credentials = SqlServerSetupService.ToCredentials(new SqlServerConnectRequest { AdminLogin = " sa ", AdminPassword = "Secret#123" });

        Assert.NotNull(credentials);
        Assert.False(credentials!.UseWindowsAuthentication);
        Assert.Equal("sa", credentials.Login);
        Assert.Equal("Secret#123", credentials.Password);
        Assert.DoesNotContain("Secret#123", credentials.ToString());
    }

    [Fact]
    public void ToCredentials_WindowsAuthentication_IgnoresSqlLogin()
    {
        var credentials = SqlServerSetupService.ToCredentials(new SqlServerConnectRequest { UseWindowsAuthentication = true, AdminLogin = "sa" });

        Assert.True(credentials!.UseWindowsAuthentication);
        Assert.Null(credentials.Login);
    }

    [Theory]
    [InlineData("sa", null)]
    [InlineData(null, "Secret#123")]
    [InlineData(@"CORP\admin", "Secret#123")]
    public void ToCredentials_RejectsIncompleteOrInvalidLogin(string? login, string? password)
    {
        Assert.Throws<InvalidOperationException>(() =>
            SqlServerSetupService.ToCredentials(new SqlServerConnectRequest { AdminLogin = login, AdminPassword = password }));
    }

    [Theory]
    [InlineData("MSSQLSERVER", ".")]
    [InlineData("mssqlserver", ".")]
    [InlineData("SQLEXPRESS", @".\SQLEXPRESS")]
    public void DataSourceFor_UsesLocalInstanceNames(string instanceName, string expected)
    {
        Assert.Equal(expected, SqlServerSetupService.DataSourceFor(instanceName));
    }

    [Theory]
    [InlineData("auditvare_local")]
    [InlineData("app.user@dev")]
    [InlineData("_svc-auditvare")]
    public void ValidateLoginName_AcceptsSqlLoginNames(string loginName)
    {
        Assert.Equal(loginName, SqlServerSetupService.ValidateLoginName($"  {loginName} "));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"CORP\auditvare")]
    [InlineData("audit vare")]
    [InlineData("1user")]
    [InlineData("user]; DROP LOGIN sa;--")]
    public void ValidateLoginName_RejectsInvalidNames(string loginName)
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerSetupService.ValidateLoginName(loginName));
    }

    [Theory]
    [InlineData("MSSQLSERVER")]
    [InlineData("SQLEXPRESS")]
    public void ValidateInstanceName_AcceptsInstanceNames(string instanceName)
    {
        Assert.Equal(instanceName, SqlServerSetupService.ValidateInstanceName(instanceName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOO_LONG_INSTANCE_NAME")]
    [InlineData("SQL'EXPRESS")]
    public void ValidateInstanceName_RejectsInvalidNames(string instanceName)
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerSetupService.ValidateInstanceName(instanceName));
    }
}
