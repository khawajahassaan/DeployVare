using API.Models;
using API.Services;

namespace API.Tests.AzureDevOps;

public class AzureDevOpsCredentialsTests
{
    [Theory]
    [InlineData("Transviti", "Transviti")]
    [InlineData("  Transviti ", "Transviti")]
    [InlineData("https://dev.azure.com/Transviti", "Transviti")]
    [InlineData("https://dev.azure.com/Transviti/AuditVare_Release_2", "Transviti")]
    [InlineData("https://transviti.visualstudio.com", "transviti")]
    public void NormalizeOrganization_AcceptsNamesAndServiceUrls(string input, string expected)
    {
        Assert.Equal(expected, AzureDevOpsCredentials.NormalizeOrganization(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://dev.azure.com/Transviti")]
    [InlineData("https://attacker.example.com/Transviti")]
    [InlineData("Trans viti")]
    [InlineData("-Transviti")]
    public void NormalizeOrganization_RejectsInvalidValues(string input)
    {
        var ex = Assert.Throws<AzureDevOpsException>(() => AzureDevOpsCredentials.NormalizeOrganization(input));
        Assert.Equal(AzureDevOpsErrorCode.InvalidConfiguration, ex.ErrorCode);
    }

    [Theory]
    [InlineData("main", "main")]
    [InlineData("refs/heads/main", "main")]
    [InlineData("release/2026.09", "release/2026.09")]
    public void NormalizeBranch_ReturnsShortName(string input, string expected)
    {
        Assert.Equal(expected, AzureDevOpsCredentials.NormalizeBranch(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("feature/../main")]
    [InlineData("my branch")]
    [InlineData("main~1")]
    public void NormalizeBranch_RejectsInvalidNames(string input)
    {
        Assert.Throws<AzureDevOpsException>(() => AzureDevOpsCredentials.NormalizeBranch(input));
    }

    [Theory]
    [InlineData("auditvare/uat")]
    [InlineData("repo?x")]
    [InlineData("")]
    public void ValidateName_RejectsInvalidNames(string input)
    {
        Assert.Throws<AzureDevOpsException>(() => AzureDevOpsCredentials.ValidateName(input, "Repository"));
    }

    [Fact]
    public void Create_RequiresToken()
    {
        var ex = Assert.Throws<AzureDevOpsException>(() => AzureDevOpsCredentials.Create("Transviti", "AuditVare_Release_2", " "));
        Assert.Equal(AzureDevOpsErrorCode.InvalidConfiguration, ex.ErrorCode);
    }

    [Fact]
    public void ToString_NeverContainsToken()
    {
        var credentials = AzureDevOpsCredentials.Create("Transviti", "AuditVare_Release_2", "super-secret-test-token");

        Assert.DoesNotContain("super-secret-test-token", credentials.ToString());
        Assert.Equal("Transviti/AuditVare_Release_2", credentials.ToString());
    }
}
