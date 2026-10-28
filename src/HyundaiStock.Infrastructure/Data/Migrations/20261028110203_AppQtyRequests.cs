using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppQtyRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RequestedByAdminId",
                table: "QuantityCorrections",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "RequestedByUserId",
                table: "QuantityCorrections",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_RequestedByUserId",
                table: "QuantityCorrections",
                column: "RequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuantityCorrections_AppUsers_RequestedByUserId",
                table: "QuantityCorrections",
                column: "RequestedByUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuantityCorrections_AppUsers_RequestedByUserId",
                table: "QuantityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_QuantityCorrections_RequestedByUserId",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "QuantityCorrections");

            migrationBuilder.AlterColumn<int>(
                name: "RequestedByAdminId",
                table: "QuantityCorrections",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
