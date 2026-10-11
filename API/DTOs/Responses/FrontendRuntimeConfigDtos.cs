namespace API.DTOs.Responses;

public class RuntimeSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class FrontendRuntimeConfigDto
{
    public int ServerId { get; set; }
    public string ServerType { get; set; } = string.Empty;

    /// <summary>Saved settings (only keys with a value).</summary>
    public List<RuntimeSettingDto> Settings { get; set; } = new();

    /// <summary>Keys offered by default when nothing is configured yet.</summary>
    public List<string> StandardKeys { get; set; } = new();

    /// <summary>All keys the frontend reads at runtime (RUNTIME_ENV_KEYS).</summary>
    public List<string> SuggestedKeys { get; set; } = new();

    /// <summary>config.js generated from the saved settings; null when nothing is configured.</summary>
    public string? ConfigJsPreview { get; set; }
}
