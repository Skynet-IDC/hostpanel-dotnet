using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PleskMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PleskApiPort = table.Column<int>(type: "int", nullable: false),
                    SshUser = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SshPort = table.Column<int>(type: "int", nullable: false),
                    SshKeyPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PleskApiKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DnsProvider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MigrationSourceId = table.Column<int>(type: "int", nullable: false),
                    SourceDomain = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetSubscriptionId = table.Column<int>(type: "int", nullable: true),
                    TargetUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServicePlanId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DiscoveryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreflightReport = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MigrationLog = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StagedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CutoverAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RolledBackAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MigrationJobs_MigrationSources_MigrationSourceId",
                        column: x => x.MigrationSourceId,
                        principalTable: "MigrationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobs_MigrationSourceId_SourceDomain",
                table: "MigrationJobs",
                columns: new[] { "MigrationSourceId", "SourceDomain" });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationJobs_State",
                table: "MigrationJobs",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationJobs");

            migrationBuilder.DropTable(
                name: "MigrationSources");
        }
    }
}
