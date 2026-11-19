using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>Monthly counts beside the annual one: session kind (0 annual, 1 monthly), month and a display name.
    /// Existing sessions become annual and are named «جرد سنوي <year>».</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119090000_MonthlySessions")]
    public partial class MonthlySessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "Kind", table: "Sessions", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Month", table: "Sessions", type: "int", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Name", table: "Sessions", type: "nvarchar(60)", maxLength: 60, nullable: false, defaultValue: "");
            migrationBuilder.Sql("UPDATE Sessions SET Name = N'جرد سنوي ' + CAST([Year] AS nvarchar(10)) WHERE Name = N''");
            migrationBuilder.CreateIndex(name: "IX_Sessions_Year_Kind_Month", table: "Sessions", columns: new[] { "Year", "Kind", "Month" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Sessions_Year_Kind_Month", table: "Sessions");
            migrationBuilder.DropColumn(name: "Name", table: "Sessions");
            migrationBuilder.DropColumn(name: "Month", table: "Sessions");
            migrationBuilder.DropColumn(name: "Kind", table: "Sessions");
        }
    }
}
