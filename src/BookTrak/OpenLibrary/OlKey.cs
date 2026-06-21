namespace BookTrak.OpenLibrary;

/// <summary>
/// Open Library keys arrive as paths like "/works/OL27448W" or "/authors/OL26320A", but
/// BookTrak stores the bare id ("OL27448W") to match the spec's OpenLibraryWorkId/AuthorId/EditionId
/// columns. /search/authors.json is an exception — it already returns bare ids — so this is
/// idempotent for already-bare input.
/// </summary>
internal static class OlKey
{
    public static string? ToBareId(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var lastSlash = key.LastIndexOf('/');
        return lastSlash >= 0 ? key[(lastSlash + 1)..] : key;
    }
}
