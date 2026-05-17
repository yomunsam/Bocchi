using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(BocchiDbContext))]
    [Migration("20260516143000_AddThemeI18nTextOverrides")]
    public partial class AddThemeI18nTextOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "I18nTextOverridesJson",
                table: "ThemeConfigurationRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "I18nTextOverridesJson",
                table: "ThemeConfigurationRecords");
        }
    }
}
