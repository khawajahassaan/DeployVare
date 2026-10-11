using API.DTOs.Responses;
using API.Utilities;

namespace API.Tests.ServerSetup;

public class FrontendRuntimeConfigTests
{
    private static RuntimeSettingDto Setting(string key, string value) => new() { Key = key, Value = value };

    [Fact]
    public void Normalize_TrimsUppercasesAndDropsEmptyValues()
    {
        var settings = FrontendRuntimeConfig.Normalize(new[]
        {
            Setting(" vite_api_url ", " https://pprod-api.auditvare.cloud/v1 "),
            Setting("VITE_DB_NAME", ""),
            Setting("", "")
        });

        var setting = Assert.Single(settings);
        Assert.Equal("VITE_API_URL", setting.Key);
        Assert.Equal("https://pprod-api.auditvare.cloud/v1", setting.Value);
    }

    [Theory]
    [InlineData("API_URL")]
    [InlineData("VITE_API-URL")]
    [InlineData("VITE_")]
    [InlineData("")]
    public void Normalize_RejectsInvalidKeys(string key)
    {
        Assert.Throws<InvalidOperationException>(() => FrontendRuntimeConfig.Normalize(new[] { Setting(key, "value") }));
    }

    [Fact]
    public void Normalize_RejectsDuplicateKeys()
    {
        Assert.Throws<InvalidOperationException>(() => FrontendRuntimeConfig.Normalize(new[]
        {
            Setting("VITE_URL", "https://a.example"),
            Setting("vite_url", "https://b.example")
        }));
    }

    [Fact]
    public void Normalize_RejectsControlCharactersInValues()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FrontendRuntimeConfig.Normalize(new[] { Setting("VITE_URL", "https://a.example\n// injected") }));
    }

    [Fact]
    public void SerializeAndParse_RoundTrip()
    {
        var settings = FrontendRuntimeConfig.Normalize(new[] { Setting("VITE_URL", "https://pprod.auditvare.cloud") });

        var parsed = FrontendRuntimeConfig.Parse(FrontendRuntimeConfig.Serialize(settings));

        Assert.Equal("https://pprod.auditvare.cloud", Assert.Single(parsed).Value);
        Assert.Null(FrontendRuntimeConfig.Serialize(new List<RuntimeSettingDto>()));
        Assert.Empty(FrontendRuntimeConfig.Parse("not json"));
    }

    [Fact]
    public void BuildConfigJs_WritesSettingsInOrder()
    {
        var js = FrontendRuntimeConfig.BuildConfigJs("PProd Frontend", new[]
        {
            Setting("VITE_API_URL", "https://pprod-api.auditvare.cloud/v1"),
            Setting("VITE_DB_NAME", "AuditVare_CFG_1")
        }, new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc));

        Assert.Contains("window.__APP_CONFIG__ = {", js);
        Assert.Contains("\"VITE_API_URL\": \"https://pprod-api.auditvare.cloud/v1\"", js);
        Assert.True(js.IndexOf("VITE_API_URL", StringComparison.Ordinal) < js.IndexOf("VITE_DB_NAME", StringComparison.Ordinal));
        Assert.Contains("PProd Frontend", js);
    }

    [Fact]
    public void BuildConfigJs_ValuesCannotBreakOutOfTheScript()
    {
        var js = FrontendRuntimeConfig.BuildConfigJs("Server", new[]
        {
            Setting("VITE_URL", "\"};alert(1);</script><script>//")
        }, DateTime.UtcNow);

        Assert.DoesNotContain("</script>", js);
        Assert.DoesNotContain("\"};alert(1)", js);
    }

    [Fact]
    public void BuildConfigJs_ServerNameCannotAddCodeLines()
    {
        var js = FrontendRuntimeConfig.BuildConfigJs("Evil\nwindow.hacked = true;", Array.Empty<RuntimeSettingDto>(), DateTime.UtcNow);

        Assert.DoesNotContain("\nwindow.hacked", js);
    }
}
