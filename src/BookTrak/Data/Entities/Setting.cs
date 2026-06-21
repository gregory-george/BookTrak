namespace BookTrak.Data.Entities;

/// <summary>Small app-wide key/value store for things that don't warrant their own column
/// (most port/prefs/schema-version state lives in config.json instead — see AppConfig).</summary>
public class Setting
{
    public required string Key { get; set; }

    public string? Value { get; set; }
}
