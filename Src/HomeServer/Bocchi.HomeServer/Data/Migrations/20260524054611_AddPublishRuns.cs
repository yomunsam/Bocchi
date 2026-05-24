using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublishPlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BuildRunId = table.Column<long>(type: "INTEGER", nullable: true),
                    BuildSessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    BuildFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ArtifactCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteCommitSha = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishRuns_PublishPlans_PublishPlanId",
                        column: x => x.PublishPlanId,
                        principalTable: "PublishPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishRuns_PublishPlanId",
                table: "PublishRuns",
                column: "PublishPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishRuns_StartedAt",
                table: "PublishRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishRuns");
        }
    }
}
