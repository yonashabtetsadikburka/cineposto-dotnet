using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinePosto.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cinemas",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cinemas", x => x.Slug);
                });

            migrationBuilder.CreateTable(
                name: "films",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TitleNormalized = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TitleKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Poster = table.Column<string>(type: "TEXT", nullable: true),
                    Backdrop = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Director = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    GenresJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_films", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "showings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilmId = table.Column<int>(type: "INTEGER", nullable: false),
                    CinemaSlug = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TimesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Screen = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SessionAttributesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_showings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_showings_cinemas_CinemaSlug",
                        column: x => x.CinemaSlug,
                        principalTable: "cinemas",
                        principalColumn: "Slug",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_showings_films_FilmId",
                        column: x => x.FilmId,
                        principalTable: "films",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_films_TitleKey",
                table: "films",
                column: "TitleKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_showings_CinemaSlug_Date",
                table: "showings",
                columns: new[] { "CinemaSlug", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_showings_Date",
                table: "showings",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_showings_FilmId",
                table: "showings",
                column: "FilmId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "showings");

            migrationBuilder.DropTable(
                name: "cinemas");

            migrationBuilder.DropTable(
                name: "films");
        }
    }
}
