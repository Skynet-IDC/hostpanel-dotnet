using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HaTestAndLagFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReplicationCheckAt",
                table: "HaSyncStates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastReplicationLagSeconds",
                table: "HaSyncStates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestResult",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTestedAt",
                table: "HaSyncStates",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReplicationCheckAt",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "LastReplicationLagSeconds",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "LastTestResult",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "LastTestedAt",
                table: "HaSyncStates");
        }
    }
}
