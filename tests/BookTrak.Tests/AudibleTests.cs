using System.Text.Json;
using BookTrak.Audible;
using BookTrak.Audible.Models;
using BookTrak.Audible.Raw;

namespace BookTrak.Tests;

public class AudibleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // -- raw search response -> candidate mapping --

    [Fact]
    public void Normalize_MapsAllFields()
    {
        var raw = Deserialize<RawAudibleProduct>("""
            {
              "asin": "B002V0QCYU",
              "title": "The Final Empire",
              "subtitle": "Mistborn Book 1",
              "authors": [{ "asin": "B001IGFHW6", "name": "Brandon Sanderson" }],
              "narrators": [{ "name": "Michael Kramer" }],
              "publisher_name": "Macmillan Audio"
            }
            """);

        var candidate = AudibleNormalizer.Normalize(raw);

        Assert.Equal("B002V0QCYU", candidate.Asin);
        Assert.Equal("The Final Empire", candidate.Title);
        Assert.Equal("Mistborn Book 1", candidate.Subtitle);
        Assert.Equal(["Brandon Sanderson"], candidate.Authors);
        Assert.Equal(["Michael Kramer"], candidate.Narrators);
        Assert.Equal("Macmillan Audio", candidate.PublisherName);
    }

    [Fact]
    public void Normalize_MissingContributors_YieldsEmptyLists()
    {
        var raw = Deserialize<RawAudibleProduct>("""{ "asin": "B1", "title": "Solo" }""");

        var candidate = AudibleNormalizer.Normalize(raw);

        Assert.Empty(candidate.Authors);
        Assert.Empty(candidate.Narrators);
        Assert.Null(candidate.Subtitle);
        Assert.Null(candidate.PublisherName);
    }

    [Fact]
    public void DeserializeSearchResponse_ParsesProductsArray()
    {
        var raw = Deserialize<RawAudibleSearchResponse>("""
            { "products": [ { "asin": "B1", "title": "A" }, { "asin": "B2", "title": "B" } ], "total_results": 2 }
            """);

        Assert.NotNull(raw.Products);
        Assert.Equal(2, raw.Products!.Count);
    }

    // -- confidence gate (the wrong-auto-attach guard) --

    [Fact]
    public void PickConfident_ExactTitleAndAuthor_ReturnsCandidate()
    {
        var candidates = new[] { Candidate("B1", "Project Hail Mary", null, "Andy Weir") };

        var match = AudiobookMatch.PickConfident(candidates, "Project Hail Mary", ["Andy Weir"]);

        Assert.NotNull(match);
        Assert.Equal("B1", match!.Asin);
    }

    [Fact]
    public void PickConfident_MatchesTitleViaSubtitle()
    {
        // Audible often pushes the series tag into the subtitle.
        var candidates = new[] { Candidate("B1", "The Final Empire", "Mistborn Book 1", "Brandon Sanderson") };

        var match = AudiobookMatch.PickConfident(candidates, "The Final Empire", ["Brandon Sanderson"]);

        Assert.NotNull(match);
    }

    [Fact]
    public void PickConfident_IgnoresLeadingArticleDifference()
    {
        var candidates = new[] { Candidate("B1", "Final Empire", null, "Brandon Sanderson") };

        var match = AudiobookMatch.PickConfident(candidates, "The Final Empire", ["Brandon Sanderson"]);

        Assert.NotNull(match);
    }

    [Fact]
    public void PickConfident_MatchesAuthorBySurname()
    {
        var candidates = new[] { Candidate("B1", "The Hobbit", null, "John Ronald Reuel Tolkien") };

        var match = AudiobookMatch.PickConfident(candidates, "The Hobbit", ["J.R.R. Tolkien"]);

        Assert.NotNull(match);
    }

    [Fact]
    public void PickConfident_AuthorMismatch_ReturnsNull()
    {
        var candidates = new[] { Candidate("B1", "Project Hail Mary", null, "Someone Else") };

        var match = AudiobookMatch.PickConfident(candidates, "Project Hail Mary", ["Andy Weir"]);

        Assert.Null(match);
    }

    [Fact]
    public void PickConfident_DifferentTitle_ReturnsNull()
    {
        var candidates = new[] { Candidate("B1", "Artemis", null, "Andy Weir") };

        var match = AudiobookMatch.PickConfident(candidates, "Project Hail Mary", ["Andy Weir"]);

        Assert.Null(match);
    }

    [Fact]
    public void PickConfident_AmbiguousSeriesMatches_ReturnsNull()
    {
        // A keyword search for a series title returns multiple same-title-prefixed volumes that
        // all share the author — refuse to guess which one the user wants.
        var candidates = new[]
        {
            Candidate("B1", "Mistborn", "The Final Empire", "Brandon Sanderson"),
            Candidate("B2", "Mistborn", "The Well of Ascension", "Brandon Sanderson"),
        };

        var match = AudiobookMatch.PickConfident(candidates, "Mistborn", ["Brandon Sanderson"]);

        Assert.Null(match);
    }

    [Fact]
    public void PickConfident_OneStrongMatchAmongNoise_ReturnsIt()
    {
        var candidates = new[]
        {
            Candidate("B1", "Project Hail Mary", null, "Andy Weir"),
            Candidate("B2", "Artemis", null, "Andy Weir"),
            Candidate("B3", "The Martian", null, "Andy Weir"),
        };

        var match = AudiobookMatch.PickConfident(candidates, "Project Hail Mary", ["Andy Weir"]);

        Assert.NotNull(match);
        Assert.Equal("B1", match!.Asin);
    }

    [Fact]
    public void PickConfident_NoCandidates_ReturnsNull()
    {
        var match = AudiobookMatch.PickConfident([], "Project Hail Mary", ["Andy Weir"]);

        Assert.Null(match);
    }

    [Fact]
    public void PickConfident_NoBookAuthors_ReturnsNull()
    {
        var candidates = new[] { Candidate("B1", "Project Hail Mary", null, "Andy Weir") };

        var match = AudiobookMatch.PickConfident(candidates, "Project Hail Mary", []);

        Assert.Null(match);
    }

    private static AudiobookCandidate Candidate(string asin, string title, string? subtitle, params string[] authors) =>
        new(asin, title, subtitle, authors, [], null);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("null");
}
