using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitProviderConnectionsAndWorkspaceRemotes : Migration
    {
        /// <summary>Git provider 连接的自然键索引列，复用数组以满足 analyzer 约束。</summary>
        private static readonly string[] GitProviderConnectionNaturalKeyColumns = ["ProviderKey", "BaseUrl", "AccountLogin"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GitProviderConnectionId",
                table: "PublishPlans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GitProviderConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AccountLogin = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ProtectedCredentialJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitProviderConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentWorkspaceRemotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RemoteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    GitProviderConnectionId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastSyncMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentWorkspaceRemotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentWorkspaceRemotes_GitProviderConnections_GitProviderConnectionId",
                        column: x => x.GitProviderConnectionId,
                        principalTable: "GitProviderConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishPlans_GitProviderConnectionId",
                table: "PublishPlans",
                column: "GitProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentWorkspaceRemotes_GitProviderConnectionId",
                table: "ContentWorkspaceRemotes",
                column: "GitProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_GitProviderConnections_ProviderKey_BaseUrl_AccountLogin",
                table: "GitProviderConnections",
                columns: GitProviderConnectionNaturalKeyColumns);

            migrationBuilder.AddForeignKey(
                name: "FK_PublishPlans_GitProviderConnections_GitProviderConnectionId",
                table: "PublishPlans",
                column: "GitProviderConnectionId",
                principalTable: "GitProviderConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PublishPlans_GitProviderConnections_GitProviderConnectionId",
                table: "PublishPlans");

            migrationBuilder.DropTable(
                name: "ContentWorkspaceRemotes");

            migrationBuilder.DropTable(
                name: "GitProviderConnections");

            migrationBuilder.DropIndex(
                name: "IX_PublishPlans_GitProviderConnectionId",
                table: "PublishPlans");

            migrationBuilder.DropColumn(
                name: "GitProviderConnectionId",
                table: "PublishPlans");
        }
    }
}
