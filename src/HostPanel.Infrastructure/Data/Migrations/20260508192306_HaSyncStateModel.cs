using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HaSyncStateModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HaSyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SyncDirection = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AutoFailback = table.Column<bool>(type: "bit", nullable: false),
                    HealthCheckIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    FailoverAfterMissedChecks = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    FailoverTriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailbackCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncLog = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyncFiles = table.Column<bool>(type: "bit", nullable: false),
                    SyncDatabases = table.Column<bool>(type: "bit", nullable: false),
                    SyncEmail = table.Column<bool>(type: "bit", nullable: false),
                    SyncExcludes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaSyncStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaSyncStates_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaSyncStates_ServerId",
                table: "HaSyncStates",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaSyncStates");
        }
    }
}
