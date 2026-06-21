using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookTrak.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenLibraryId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PersonalName = table.Column<string>(type: "TEXT", nullable: true),
                    AlternateNames = table.Column<string>(type: "TEXT", nullable: true),
                    Bio = table.Column<string>(type: "TEXT", nullable: true),
                    BirthDate = table.Column<string>(type: "TEXT", nullable: true),
                    DeathDate = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoId = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    Links = table.Column<string>(type: "TEXT", nullable: true),
                    Wikipedia = table.Column<string>(type: "TEXT", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    OpenLibrarySeriesKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "BookAuthors",
                columns: table => new
                {
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAuthors", x => new { x.BookId, x.AuthorId });
                    table.ForeignKey(
                        name: "FK_BookAuthors_Authors_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookGenres",
                columns: table => new
                {
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    GenreId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookGenres", x => new { x.BookId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_BookGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenLibraryWorkId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    FirstPublishDate = table.Column<string>(type: "TEXT", nullable: true),
                    Subjects = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    SeriesPosition = table.Column<string>(type: "TEXT", nullable: true),
                    AverageRating = table.Column<double>(type: "REAL", nullable: true),
                    RatingsCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MyRating = table.Column<double>(type: "REAL", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateStarted = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DateRead = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReadEditionId = table.Column<int>(type: "INTEGER", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreferredEditionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Books_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Editions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenLibraryEditionId = table.Column<string>(type: "TEXT", nullable: true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    Format = table.Column<int>(type: "INTEGER", nullable: false),
                    Isbn10 = table.Column<string>(type: "TEXT", nullable: true),
                    Isbn13 = table.Column<string>(type: "TEXT", nullable: true),
                    Asin = table.Column<string>(type: "TEXT", nullable: true),
                    NumberOfPages = table.Column<int>(type: "INTEGER", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    PublishDate = table.Column<string>(type: "TEXT", nullable: true),
                    Narrator = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AudioPublisher = table.Column<string>(type: "TEXT", nullable: true),
                    CoverId = table.Column<string>(type: "TEXT", nullable: true),
                    CoverPath = table.Column<string>(type: "TEXT", nullable: true),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Editions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Editions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Authors_OpenLibraryId",
                table: "Authors",
                column: "OpenLibraryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookAuthors_AuthorId",
                table: "BookAuthors",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_BookGenres_GenreId",
                table: "BookGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_OpenLibraryWorkId",
                table: "Books",
                column: "OpenLibraryWorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_PreferredEditionId",
                table: "Books",
                column: "PreferredEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_ReadEditionId",
                table: "Books",
                column: "ReadEditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_SeriesId",
                table: "Books",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Editions_BookId",
                table: "Editions",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_Editions_OpenLibraryEditionId",
                table: "Editions",
                column: "OpenLibraryEditionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BookAuthors_Books_BookId",
                table: "BookAuthors",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BookGenres_Books_BookId",
                table: "BookGenres",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Editions_PreferredEditionId",
                table: "Books",
                column: "PreferredEditionId",
                principalTable: "Editions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Editions_ReadEditionId",
                table: "Books",
                column: "ReadEditionId",
                principalTable: "Editions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Editions_Books_BookId",
                table: "Editions");

            migrationBuilder.DropTable(
                name: "BookAuthors");

            migrationBuilder.DropTable(
                name: "BookGenres");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Authors");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "Editions");

            migrationBuilder.DropTable(
                name: "Series");
        }
    }
}
