using System.Reflection;

namespace BookTrak.OpenLibrary;

/// <summary>Open Library and audnexus both ask for a descriptive User-Agent in exchange for
/// not requiring an API key — sending one is non-optional or we risk being blocked.</summary>
internal static class UserAgent
{
    public static string Build(string contactInfo)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1";
        return $"BookTrak/{version} ({contactInfo})";
    }
}
