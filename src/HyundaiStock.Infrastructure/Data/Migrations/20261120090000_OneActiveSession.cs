using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <summary>The database itself allows one session in progress only (filtered unique index on Status = 0).
    /// If a live database already holds more than one open session the index is skipped, so startup never fails; the service check still applies.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120090000_OneActiveSession")]
    public partial class OneActiveSession : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF (SELECT COUNT(*) FROM Sessions WHERE [Status] = 0) <= 1
    EXEC(N'CREATE UNIQUE INDEX [IX_Sessions_Status] ON [Sessions] ([Status]) WHERE [Status] = 0')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_Status' AND object_id = OBJECT_ID(N'[Sessions]')) DROP INDEX [IX_Sessions_Status] ON [Sessions]");
        }
    }
}
