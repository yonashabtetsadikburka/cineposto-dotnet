using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinePosto.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackdropFetchedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BackdropFetchedAtUtc",
                table: "films",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackdropFetchedAtUtc",
                table: "films");
        }
    }
}
