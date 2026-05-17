using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(BocchiDbContext))]
    [Migration("20260516130000_AddLocalizationTextOverrides")]
    public partial class AddLocalizationTextOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommonTextOverridesJson",
                table: "LocalizationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommonTextOverridesJson",
                table: "LocalizationSettings");
        }
    }
}
