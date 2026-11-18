using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>Allowed count type per mobile user: 1 = spare parts, 2 = vehicles, 3 = both. Existing users keep both.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261118090000_UserCountType")]
    public partial class UserCountType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountType",
                table: "AppUsers",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CountType", table: "AppUsers");
        }
    }
}
