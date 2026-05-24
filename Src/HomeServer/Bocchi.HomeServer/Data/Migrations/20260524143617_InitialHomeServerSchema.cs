using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bocchi.HomeServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialHomeServerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryTrees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TreeJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryTrees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardGuideCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardGuideCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppearanceMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalLoginProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProtectedClientSecret = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CallbackPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Authority = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ResponseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UsePkce = table.Column<bool>(type: "INTEGER", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NameClaimType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailClaimType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLoginProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GitHubIntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    LoginEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OAuthClientId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ProtectedOAuthClientSecret = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    CallbackPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubIntegrationSettings", x => x.Id);
                });

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
                name: "HomeServerSetupStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FirstAdminUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    DataRoot = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeServerSetupStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalizationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrimaryLanguage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EnabledLanguagesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CustomLanguagesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CommonTextOverridesJson = table.Column<string>(type: "TEXT", nullable: false),
                    UrlPolicy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizationSettings", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "ThemeConfigurationRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThemeId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    I18nTextOverridesJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemeConfigurationRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "PublishPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedCredentialJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    GitProviderConnectionId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishPlans_GitProviderConnections_GitProviderConnectionId",
                        column: x => x.GitProviderConnectionId,
                        principalTable: "GitProviderConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

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
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryTrees_Scope",
                table: "CategoryTrees",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentWorkspaceRemotes_GitProviderConnectionId",
                table: "ContentWorkspaceRemotes",
                column: "GitProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardGuideCards_Key",
                table: "DashboardGuideCards",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLoginProviderSettings_ProviderKey",
                table: "ExternalLoginProviderSettings",
                column: "ProviderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitProviderConnections_ProviderKey_BaseUrl_AccountLogin",
                table: "GitProviderConnections",
                columns: new[] { "ProviderKey", "BaseUrl", "AccountLogin" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishPlans_GitProviderConnectionId",
                table: "PublishPlans",
                column: "GitProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishRuns_PublishPlanId",
                table: "PublishRuns",
                column: "PublishPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishRuns_StartedAt",
                table: "PublishRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ThemeConfigurationRecords_ThemeId",
                table: "ThemeConfigurationRecords",
                column: "ThemeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CategoryTrees");

            migrationBuilder.DropTable(
                name: "ContentWorkspaceRemotes");

            migrationBuilder.DropTable(
                name: "DashboardGuideCards");

            migrationBuilder.DropTable(
                name: "DashboardSettings");

            migrationBuilder.DropTable(
                name: "ExternalLoginProviderSettings");

            migrationBuilder.DropTable(
                name: "GitHubIntegrationSettings");

            migrationBuilder.DropTable(
                name: "HomeServerSetupStates");

            migrationBuilder.DropTable(
                name: "LocalizationSettings");

            migrationBuilder.DropTable(
                name: "PublishRuns");

            migrationBuilder.DropTable(
                name: "SiteProfileSettings");

            migrationBuilder.DropTable(
                name: "ThemeConfigurationRecords");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "PublishPlans");

            migrationBuilder.DropTable(
                name: "GitProviderConnections");
        }
    }
}
