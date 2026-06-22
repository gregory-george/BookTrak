using BookTrak.Import;

namespace BookTrak.Tests;

public class CsvParserTests
{
    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_KeepsCommaInField()
    {
        var rows = CsvParser.Parse("a,\"b, c\",d\n");

        Assert.Single(rows);
        Assert.Equal(["a", "b, c", "d"], rows[0]);
    }

    [Fact]
    public void Parse_EscapedDoubleQuote_Unescapes()
    {
        var rows = CsvParser.Parse("a,\"say \"\"hi\"\"\",c\n");

        Assert.Equal("say \"hi\"", rows[0][1]);
    }

    [Fact]
    public void Parse_GoodreadsBareEqualsQuoteIsbnCell_StaysLiteral()
    {
        // Goodreads emits ISBN cells as a bare ="12345" (Excel-formula trick) with no
        // surrounding CSV quoting — the embedded quote isn't at the start of the field, so it
        // must NOT be treated as an RFC4180 quote-opener.
        var rows = CsvParser.Parse("title,=\"0441013597\",end\n");

        Assert.Equal("=\"0441013597\"", rows[0][1]);
        Assert.Equal("end", rows[0][2]);
    }

    [Fact]
    public void Parse_MultilineQuotedField_KeepsNewlineInField()
    {
        var rows = CsvParser.Parse("a,\"line1\nline2\",c\n");

        Assert.Single(rows);
        Assert.Equal("line1\nline2", rows[0][1]);
    }

    [Fact]
    public void Parse_NoTrailingNewline_StillReturnsLastRow()
    {
        var rows = CsvParser.Parse("a,b,c");

        Assert.Single(rows);
        Assert.Equal(["a", "b", "c"], rows[0]);
    }
}
