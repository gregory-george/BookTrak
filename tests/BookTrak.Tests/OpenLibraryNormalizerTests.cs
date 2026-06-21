using System.Text.Json;
using BookTrak.OpenLibrary;
using BookTrak.OpenLibrary.Raw;

namespace BookTrak.Tests;

public class OpenLibraryNormalizerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // -- description / bio: string-or-{type,value} is the classic OL bug magnet --

    [Fact]
    public void NormalizeWork_DescriptionAsPlainString_ExtractsValue()
    {
        var raw = Deserialize<RawWork>("""{ "key": "/works/OL1W", "title": "T", "description": "A plain description." }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Equal("A plain description.", work.Description);
    }

    [Fact]
    public void NormalizeWork_DescriptionAsTypedObject_ExtractsValue()
    {
        var raw = Deserialize<RawWork>("""
            { "key": "/works/OL1W", "title": "T", "description": { "type": "/type/text", "value": "An object description." } }
            """);

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Equal("An object description.", work.Description);
    }

    [Fact]
    public void NormalizeWork_DescriptionMissing_IsNull()
    {
        var raw = Deserialize<RawWork>("""{ "key": "/works/OL1W", "title": "T" }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Null(work.Description);
    }

    [Fact]
    public void NormalizeWork_DescriptionNull_IsNull()
    {
        var raw = Deserialize<RawWork>("""{ "key": "/works/OL1W", "title": "T", "description": null }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Null(work.Description);
    }

    [Fact]
    public void NormalizeAuthor_BioAsPlainString_ExtractsValue()
    {
        var raw = Deserialize<RawAuthor>("""{ "key": "/authors/OL1A", "name": "N", "bio": "Plain bio." }""");

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Equal("Plain bio.", author.Bio);
    }

    [Fact]
    public void NormalizeAuthor_BioAsTypedObject_ExtractsValue()
    {
        var raw = Deserialize<RawAuthor>("""
            { "key": "/authors/OL1A", "name": "N", "bio": { "type": "/type/text", "value": "Object bio." } }
            """);

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Equal("Object bio.", author.Bio);
    }

    [Fact]
    public void NormalizeAuthor_BioMissing_IsNull()
    {
        var raw = Deserialize<RawAuthor>("""{ "key": "/authors/OL1A", "name": "N" }""");

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Null(author.Bio);
    }

    // -- free-text dates: never parse as DateTime, just keep the string verbatim --

    [Fact]
    public void NormalizeAuthor_FreeTextBirthDate_PassesThrough()
    {
        var raw = Deserialize<RawAuthor>("""{ "key": "/authors/OL1A", "name": "N", "birth_date": "circa 1800" }""");

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Equal("circa 1800", author.BirthDate);
    }

    [Fact]
    public void NormalizeAuthor_NumericBirthDate_StringifiesWithoutThrowing()
    {
        // OL is supposed to send strings, but tolerate a bare year number defensively.
        var raw = Deserialize<RawAuthor>("""{ "key": "/authors/OL1A", "name": "N", "birth_date": 1892 }""");

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Equal("1892", author.BirthDate);
    }

    [Fact]
    public void NormalizeAuthor_MissingDates_AreNull()
    {
        var raw = Deserialize<RawAuthor>("""{ "key": "/authors/OL1A", "name": "N" }""");

        var author = OpenLibraryNormalizer.NormalizeAuthor(raw);

        Assert.Null(author.BirthDate);
        Assert.Null(author.DeathDate);
    }

    // -- key normalization and nested-reference extraction --

    [Fact]
    public void NormalizeWork_StripsKeyPrefix()
    {
        var raw = Deserialize<RawWork>("""{ "key": "/works/OL27448W", "title": "The Hobbit" }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Equal("OL27448W", work.OpenLibraryWorkId);
    }

    [Fact]
    public void NormalizeWork_ExtractsMultipleAuthorKeys()
    {
        var raw = Deserialize<RawWork>("""
            {
              "key": "/works/OL1W",
              "title": "T",
              "authors": [
                { "author": { "key": "/authors/OL1A" } },
                { "author": { "key": "/authors/OL2A" } }
              ]
            }
            """);

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Equal(["OL1A", "OL2A"], work.AuthorOpenLibraryIds);
    }

    [Fact]
    public void NormalizeWork_NoAuthors_ReturnsEmptyList()
    {
        var raw = Deserialize<RawWork>("""{ "key": "/works/OL1W", "title": "T" }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Empty(work.AuthorOpenLibraryIds);
    }

    // -- cover id picking: ignore the -1/0 "no cover" sentinels --

    [Theory]
    [InlineData("[258027]", "258027")]
    [InlineData("[-1, 258027]", "258027")]
    [InlineData("[-1]", null)]
    [InlineData("[]", null)]
    [InlineData(null, null)]
    public void NormalizeWork_PicksFirstPositiveCoverId(string? coversJson, string? expected)
    {
        var coversField = coversJson is null ? "" : $""", "covers": {coversJson}""";
        var raw = Deserialize<RawWork>($$"""{ "key": "/works/OL1W", "title": "T"{{coversField}} }""");

        var work = OpenLibraryNormalizer.NormalizeWork(raw);

        Assert.Equal(expected, work.PrimaryCoverId);
    }

    // -- ISBN edition: back-reference to its work(s) --

    [Fact]
    public void NormalizeEdition_IsbnLookup_ExtractsWorkIds()
    {
        var raw = Deserialize<RawIsbnEdition>("""
            {
              "key": "/books/OL1M",
              "isbn_13": ["9780000000001"],
              "works": [ { "key": "/works/OL1W" } ]
            }
            """);

        var edition = OpenLibraryNormalizer.NormalizeEdition(raw);

        Assert.Equal("OL1M", edition.OpenLibraryEditionId);
        Assert.Equal("9780000000001", edition.Isbn13);
        Assert.Equal(["OL1W"], edition.WorkOpenLibraryIds);
    }

    [Fact]
    public void NormalizeEdition_PlainEditionsEntry_HasNoWorkIds()
    {
        // /works/{id}/editions.json entries don't carry a "works" back-reference.
        var raw = Deserialize<RawEdition>("""{ "key": "/books/OL1M", "isbn_13": ["9780000000001"] }""");

        var edition = OpenLibraryNormalizer.NormalizeEdition(raw);

        Assert.Empty(edition.WorkOpenLibraryIds);
    }

    [Fact]
    public void NormalizeEdition_ExtractsLanguageBareCode()
    {
        var raw = Deserialize<RawEdition>("""
            { "key": "/books/OL1M", "languages": [ { "key": "/languages/eng" } ] }
            """);

        var edition = OpenLibraryNormalizer.NormalizeEdition(raw);

        Assert.Equal("eng", edition.Language);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("Deserialization returned null.");
}
