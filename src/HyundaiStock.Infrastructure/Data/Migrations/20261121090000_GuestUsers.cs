using System;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>Guest mobile users: view only, every count, scan and request is refused. Existing users stay counters.
    /// DeletedAt: the user deleted the account from the app.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261121090000_GuestUsers")]
    public partial class GuestUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGuest",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeletedAt", table: "AppUsers");
            migrationBuilder.DropColumn(name: "IsGuest", table: "AppUsers");
        }
    }
}
