using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcfrApi.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TitleVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TitleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    XmlContent = table.Column<string>(type: "TEXT", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Checksum = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleWordCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TitleNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleWordCounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TitleVersions_TitleNumber_IssueDate",
                table: "TitleVersions",
                columns: new[] { "TitleNumber", "IssueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TitleWordCounts_TitleNumber_Date",
                table: "TitleWordCounts",
                columns: new[] { "TitleNumber", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitleVersions");

            migrationBuilder.DropTable(
                name: "TitleWordCounts");
        }
    }
}
