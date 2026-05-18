using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HostPanel.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomServicePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerUserId",
                table: "ServicePlans",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustom",
                table: "ServicePlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlans_CustomerUserId",
                table: "ServicePlans",
                column: "CustomerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlans_AspNetUsers_CustomerUserId",
                table: "ServicePlans",
                column: "CustomerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicePlans_AspNetUsers_CustomerUserId",
                table: "ServicePlans");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlans_CustomerUserId",
                table: "ServicePlans");

            migrationBuilder.DropColumn(
                name: "CustomerUserId",
                table: "ServicePlans");

            migrationBuilder.DropColumn(
                name: "IsCustom",
                table: "ServicePlans");
        }
    }
}
