using System.Text.Json;
using System.Text.Json.Serialization;
using BookTrak.Services;

namespace BookTrak.Hosting;

internal sealed class AppConfig
{
    public int Port { get; set; } = 6123;

    public int SchemaVersion { get; set; } = 0;

    /// <summary>Appended to the Open Library / audnexus User-Agent header so we're a good
    /// citizen of their free APIs. Edit config.json directly to set a real contact.</summary>
    public string ContactInfo { get; set; } = "local personal-use install, no public contact";

    /// <summary>Remembered sort order for the Library page. Stored as the enum name so the
    /// config stays readable and survives enum reordering.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LibrarySortOrder>))]
    public LibrarySortOrder LibrarySort { get; set; } = LibrarySortOrder.DateAddedDesc;

    /// <summary>Remembered sort order for the Authors page.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AuthorSortOrder>))]
    public AuthorSortOrder AuthorSort { get; set; } = AuthorSortOrder.FirstNameAsc;

    public static AppConfig LoadOrCreate()
    {
        if (File.Exists(AppPaths.ConfigFile))
        {
            try
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
                if (config is not null)
                {
                    return config;
                }
            }
            catch (JsonException)
            {
                // Corrupt config.json — fall through and recreate with defaults.
            }
        }

        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, AppConfigJsonContext.Default.AppConfig);
        var tempFile = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tempFile, json);
        File.Move(tempFile, AppPaths.ConfigFile, overwrite: true);
    }
}

[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}
