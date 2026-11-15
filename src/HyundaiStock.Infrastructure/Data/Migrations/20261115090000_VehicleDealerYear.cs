using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>Agent (الوكيل) and manufacture year for each vehicle in the session file. Both are optional so older sessions stay valid.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115090000_VehicleDealerYear")]
    public partial class VehicleDealerYear : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dealer",
                table: "ExpectedVins",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManufactureYear",
                table: "ExpectedVins",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ManufactureYear", table: "ExpectedVins");
            migrationBuilder.DropColumn(name: "Dealer", table: "ExpectedVins");
        }
    }
}
