using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BranchWarehouseNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Warehouses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Branches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Number",
                table: "Warehouses",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Number",
                table: "Branches",
                column: "Number",
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Warehouses_Number",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Branches_Number",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Branches");
        }
    }
}
