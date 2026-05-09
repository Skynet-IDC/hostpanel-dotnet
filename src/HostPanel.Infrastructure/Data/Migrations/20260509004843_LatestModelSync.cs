using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LatestModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudflareApiToken",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudflareLoadBalancerId",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CloudflareUseLoadBalancer",
                table: "HaSyncStates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CloudflareZoneId",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DnsProvider",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DnsTtlSeconds",
                table: "HaSyncStates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailoverDomains",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailoverStrategy",
                table: "HaSyncStates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FloatingIp",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FloatingIpInterface",
                table: "HaSyncStates",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudflareApiToken",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "CloudflareLoadBalancerId",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "CloudflareUseLoadBalancer",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "CloudflareZoneId",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "DnsProvider",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "DnsTtlSeconds",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "FailoverDomains",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "FailoverStrategy",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "FloatingIp",
                table: "HaSyncStates");

            migrationBuilder.DropColumn(
                name: "FloatingIpInterface",
                table: "HaSyncStates");
        }
    }
}
