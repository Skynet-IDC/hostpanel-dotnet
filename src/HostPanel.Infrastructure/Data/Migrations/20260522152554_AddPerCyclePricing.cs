using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerCyclePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceAnnual",
                table: "ServicePlans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceMonthly",
                table: "ServicePlans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceQuarterly",
                table: "ServicePlans",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceSemiAnnual",
                table: "ServicePlans",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceAnnual",
                table: "ServicePlans");

            migrationBuilder.DropColumn(
                name: "PriceMonthly",
                table: "ServicePlans");

            migrationBuilder.DropColumn(
                name: "PriceQuarterly",
                table: "ServicePlans");

            migrationBuilder.DropColumn(
                name: "PriceSemiAnnual",
                table: "ServicePlans");
        }
    }
}
