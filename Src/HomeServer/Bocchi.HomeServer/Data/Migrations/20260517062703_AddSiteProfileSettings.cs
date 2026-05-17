using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteProfileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteDescription",
                table: "DashboardSettings");

            migrationBuilder.DropColumn(
                name: "SiteTitle",
                table: "DashboardSettings");

            migrationBuilder.CreateTable(
                name: "SiteProfileSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    DefaultTitle = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PublicBaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CopyrightNotice = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DefaultThemeId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteProfileSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteProfileSettings");

            migrationBuilder.AddColumn<string>(
                name: "SiteDescription",
                table: "DashboardSettings",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SiteTitle",
                table: "DashboardSettings",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "");
        }
    }
}
