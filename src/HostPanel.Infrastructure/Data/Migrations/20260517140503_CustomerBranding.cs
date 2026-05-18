using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds per-customer branding overrides + folds in the earlier hand-authored migrations
    /// (20260517000000_SetupWizardConfigured, 20260517010000_SystemSettings) that lacked
    /// Designer.cs partners and so were never picked up by EF. Idempotent: existing schema
    /// (from those orphans being applied manually) is left alone.
    /// </summary>
    public partial class CustomerBranding : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Configured column to BrandingConfigs if missing
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = N'Configured' AND Object_ID = Object_ID(N'BrandingConfigs'))
                    ALTER TABLE [BrandingConfigs] ADD [Configured] bit NOT NULL CONSTRAINT [DF_BrandingConfigs_Configured] DEFAULT (0);
            ");

            // Create SystemSettings if missing
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SystemSettings')
                BEGIN
                    CREATE TABLE [SystemSettings] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [Key] nvarchar(200) NOT NULL,
                        [Value] nvarchar(max) NULL,
                        [Category] nvarchar(100) NULL,
                        [IsSensitive] bit NOT NULL DEFAULT (0),
                        [CreatedAt] datetime2 NOT NULL,
                        [UpdatedAt] datetime2 NULL,
                        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
                END;
            ");

            // CustomerBrandings — always new
            migrationBuilder.CreateTable(
                name: "CustomerBrandings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PanelName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoginMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoginBackgroundUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBrandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBrandings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBrandings_UserId",
                table: "CustomerBrandings",
                column: "UserId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerBrandings");
            // Leave Configured + SystemSettings — original orphan migrations are their owners.
        }
    }
}
