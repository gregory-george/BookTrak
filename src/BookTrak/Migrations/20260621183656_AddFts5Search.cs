using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTrak.Migrations
{
    /// <inheritdoc />
    public partial class AddFts5Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Not external-content — rowid is set explicitly to Books.Id so search hits join
            // straight back to Books. Kept in sync via triggers below rather than rebuilt per query.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE BookSearch USING fts5(
                    Title,
                    Subtitle,
                    AuthorNames,
                    tokenize = 'unicode61'
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO BookSearch(rowid, Title, Subtitle, AuthorNames)
                SELECT
                    b.Id,
                    b.Title,
                    b.Subtitle,
                    (SELECT IFNULL(group_concat(a.Name, ' '), '')
                     FROM BookAuthors ba JOIN Authors a ON a.Id = ba.AuthorId
                     WHERE ba.BookId = b.Id)
                FROM Books b;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_Books_AfterInsert AFTER INSERT ON Books BEGIN
                    INSERT INTO BookSearch(rowid, Title, Subtitle, AuthorNames)
                    VALUES (NEW.Id, NEW.Title, NEW.Subtitle, '');
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_Books_AfterUpdate AFTER UPDATE OF Title, Subtitle ON Books BEGIN
                    UPDATE BookSearch SET Title = NEW.Title, Subtitle = NEW.Subtitle WHERE rowid = NEW.Id;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_Books_AfterDelete AFTER DELETE ON Books BEGIN
                    DELETE FROM BookSearch WHERE rowid = OLD.Id;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_BookAuthors_AfterInsert AFTER INSERT ON BookAuthors BEGIN
                    UPDATE BookSearch SET AuthorNames = (
                        SELECT IFNULL(group_concat(a.Name, ' '), '')
                        FROM BookAuthors ba JOIN Authors a ON a.Id = ba.AuthorId
                        WHERE ba.BookId = NEW.BookId
                    ) WHERE rowid = NEW.BookId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_BookAuthors_AfterDelete AFTER DELETE ON BookAuthors BEGIN
                    UPDATE BookSearch SET AuthorNames = (
                        SELECT IFNULL(group_concat(a.Name, ' '), '')
                        FROM BookAuthors ba JOIN Authors a ON a.Id = ba.AuthorId
                        WHERE ba.BookId = OLD.BookId
                    ) WHERE rowid = OLD.BookId;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER BookSearch_Authors_AfterUpdate AFTER UPDATE OF Name ON Authors BEGIN
                    UPDATE BookSearch SET AuthorNames = (
                        SELECT IFNULL(group_concat(a.Name, ' '), '')
                        FROM BookAuthors ba JOIN Authors a ON a.Id = ba.AuthorId
                        WHERE ba.BookId = BookSearch.rowid
                    ) WHERE rowid IN (SELECT BookId FROM BookAuthors WHERE AuthorId = NEW.Id);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_Authors_AfterUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_BookAuthors_AfterDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_BookAuthors_AfterInsert;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_Books_AfterDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_Books_AfterUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BookSearch_Books_AfterInsert;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS BookSearch;");
        }
    }
}
