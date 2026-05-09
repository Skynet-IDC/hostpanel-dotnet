using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppConfigModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DomainId = table.Column<int>(type: "int", nullable: false),
                    Runtime = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryPoint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Port = table.Column<int>(type: "int", nullable: false),
                    NodeVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RestartMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BuildCommand = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartCommand = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnvironmentVariables = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkingDirectory = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoDeploy = table.Column<bool>(type: "bit", nullable: false),
                    StagingDomainId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppConfigs_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfigs_DomainId",
                table: "AppConfigs",
                column: "DomainId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfigs");
        }
    }
}
