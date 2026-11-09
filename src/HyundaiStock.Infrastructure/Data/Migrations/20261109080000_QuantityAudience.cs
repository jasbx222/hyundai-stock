using System;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261109080000_QuantityAudience")]
    public partial class QuantityAudience : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudienceKind",
                table: "QuantityCorrections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudienceUserId",
                table: "QuantityCorrections",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_AudienceUserId",
                table: "QuantityCorrections",
                column: "AudienceUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QuantityCorrections_AppUsers_AudienceUserId",
                table: "QuantityCorrections",
                column: "AudienceUserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuantityCorrections_AppUsers_AudienceUserId",
                table: "QuantityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_QuantityCorrections_AudienceUserId",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "AudienceUserId",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "AudienceKind",
                table: "QuantityCorrections");
        }
    }
}
