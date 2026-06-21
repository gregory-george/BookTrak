namespace BookTrak.OpenLibrary.Models;

/// <summary>Output of the normalization layer — clean types only, no string-or-object ambiguity left.</summary>
public sealed record NormalizedAuthor(
    string? OpenLibraryId,
    string Name,
    string? PersonalName,
    IReadOnlyList<string> AlternateNames,
    string? Bio,
    string? BirthDate,
    string? DeathDate,
    string? PhotoId,
    IReadOnlyList<string> Links,
    string? Wikipedia);
