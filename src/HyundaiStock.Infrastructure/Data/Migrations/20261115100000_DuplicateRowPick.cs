using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>Per-user grant to pick a specific row when one part number has several rows. Off by default.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115100000_DuplicateRowPick")]
    public partial class DuplicateRowPick : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanPickDuplicateRow",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CanPickDuplicateRow", table: "AppUsers");
        }
    }
}
