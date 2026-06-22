using BookTrak.Data.Entities;
using BookTrak.Import;

namespace BookTrak.Tests;

public class ReadingExportParserTests
{
    [Fact]
    public void Parse_GoodreadsExport_MapsColumnsAndStripsIsbnWrapper()
    {
        var csv =
            "Book Id,Title,Author,ISBN,ISBN13,My Rating,Date Read,Exclusive Shelf\n" +
            "1,\"Dune, Book One\",Frank Herbert,=\"0441013597\",=\"9780441013593\",5,2024/03/15,read\n" +
            "2,The Hobbit,J.R.R. Tolkien,=\"\",=\"\",0,,to-read\n";

        var rows = ReadingExportParser.Parse(csv);

        Assert.Equal(2, rows.Count);

        var dune = rows[0];
        Assert.Equal("Dune, Book One", dune.Title);
        Assert.Equal("Frank Herbert", dune.Author);
        Assert.Equal("9780441013593", dune.Isbn);
        Assert.Equal(5, dune.Rating);
        Assert.Equal(new DateTime(2024, 3, 15), dune.DateRead);
        Assert.Equal(BookStatus.Read, dune.Status);

        var hobbit = rows[1];
        Assert.Null(hobbit.Isbn);
        Assert.Null(hobbit.Rating);
        Assert.Null(hobbit.DateRead);
        Assert.Equal(BookStatus.WantToRead, hobbit.Status);
    }

    [Fact]
    public void Parse_StoryGraphExport_MapsColumns()
    {
        var csv =
            "Title,Authors,ISBN/UID,Star Rating,Last Date Read,Read Status\n" +
            "Project Hail Mary,Andy Weir,9780593135204,4.5,2024/01/02,read\n" +
            "Mistborn,Brandon Sanderson,9780765311788,,,currently-reading\n";

        var rows = ReadingExportParser.Parse(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("9780593135204", rows[0].Isbn);
        Assert.Equal(4.5, rows[0].Rating);
        Assert.Equal(new DateTime(2024, 1, 2), rows[0].DateRead);
        Assert.Equal(BookStatus.Read, rows[0].Status);

        Assert.Equal(BookStatus.Reading, rows[1].Status);
        Assert.Null(rows[1].Rating);
    }

    [Fact]
    public void Parse_UnrecognizedHeader_Throws()
    {
        var csv = "Foo,Bar\n1,2\n";

        Assert.Throws<UnrecognizedExportFormatException>(() => ReadingExportParser.Parse(csv));
    }

    [Fact]
    public void Parse_BlankLines_AreSkipped()
    {
        var csv = "Title,Author,ISBN,ISBN13,My Rating,Date Read,Exclusive Shelf\nA,B,,,,,\n\nC,D,,,,,\n";

        var rows = ReadingExportParser.Parse(csv);

        Assert.Equal(2, rows.Count);
    }
}
