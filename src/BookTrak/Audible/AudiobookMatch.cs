using System.Text;
using BookTrak.Audible.Models;

namespace BookTrak.Audible;

/// <summary>Pure matching logic for the auto-attach flow: given Audible search candidates and the
/// local book's title + author names, decide whether there is one unambiguous, high-confidence
/// audiobook to attach without asking the user. Kept strict on purpose — a miss is cheap (the
/// book is still added and the manual "Find matching audiobook" button remains), but a wrong
/// auto-attach (wrong series volume, abridged, foreign edition) is annoying to undo.</summary>
internal static class AudiobookMatch
{
    /// <summary>Returns the single confident candidate, or null when there is no match or the
    /// match is ambiguous (more than one candidate clears the title+author bar).</summary>
    public static AudiobookCandidate? PickConfident(
        IReadOnlyList<AudiobookCandidate> candidates,
        string bookTitle,
        IReadOnlyList<string> bookAuthors)
    {
        var qualifying = candidates
            .Where(c => TitleMatches(bookTitle, c) && AuthorMatches(bookAuthors, c))
            .ToList();

        // Exactly one candidate clears both bars -> confident. Zero or many -> not confident
        // (many usually means a series where every volume shares the work title + author).
        return qualifying.Count == 1 ? qualifying[0] : null;
    }

    /// <summary>Candidate title equals, or starts with, the book title once both are normalized.
    /// Also tries "Title: Subtitle" since Audible often splits the series tag into the subtitle
    /// (e.g. title "The Final Empire", subtitle "Mistborn Book 1").</summary>
    private static bool TitleMatches(string bookTitle, AudiobookCandidate candidate)
    {
        var book = Normalize(bookTitle);
        if (book.Length == 0)
        {
            return false;
        }

        var title = Normalize(candidate.Title);
        var combined = Normalize($"{candidate.Title} {candidate.Subtitle}");

        return Equal(title, book) || StartsWith(title, book) ||
               Equal(combined, book) || StartsWith(combined, book);
    }

    /// <summary>Any book author's surname (last whitespace-delimited token) appears in any
    /// candidate author name, case-insensitively. Surname-only keeps "J.R.R. Tolkien" matching
    /// "John Ronald Reuel Tolkien".</summary>
    private static bool AuthorMatches(IReadOnlyList<string> bookAuthors, AudiobookCandidate candidate)
    {
        if (bookAuthors.Count == 0 || candidate.Authors.Count == 0)
        {
            return false;
        }

        var candidateNames = candidate.Authors.Select(Normalize).Where(n => n.Length > 0).ToList();

        foreach (var author in bookAuthors)
        {
            var normalized = Normalize(author);
            if (normalized.Length == 0)
            {
                continue;
            }

            var surname = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (surname is null || surname.Length < 2)
            {
                continue;
            }

            if (candidateNames.Any(n => ContainsWord(n, surname)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Equal(string a, string b) => a == b;

    private static bool StartsWith(string longer, string prefix) =>
        prefix.Length > 0 && (longer == prefix || longer.StartsWith(prefix + ' ', StringComparison.Ordinal));

    private static bool ContainsWord(string haystack, string word) =>
        haystack == word ||
        haystack.StartsWith(word + ' ', StringComparison.Ordinal) ||
        haystack.EndsWith(' ' + word, StringComparison.Ordinal) ||
        haystack.Contains(' ' + word + ' ', StringComparison.Ordinal);

    /// <summary>Lowercase, drop a leading article, strip punctuation to spaces, collapse runs of
    /// whitespace. Produces a stable token string for equality/prefix/word comparisons.</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or ':' or ',' or '.' or '\'' or '"' or '(' or ')' or '!' or '?' or '&')
            {
                sb.Append(' ');
            }
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && tokens[0] is "the" or "a" or "an")
        {
            tokens = tokens[1..];
        }

        return string.Join(' ', tokens);
    }
}
