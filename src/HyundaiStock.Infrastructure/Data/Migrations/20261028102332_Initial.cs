using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Admins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Admins_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warehouses_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByAdminId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUsers_Admins_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedByAdminId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedByAdminId = table.Column<int>(type: "int", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Admins_StartedByAdminId",
                        column: x => x.StartedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppUserBranches",
                columns: table => new
                {
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserBranches", x => new { x.AppUserId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_AppUserBranches_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppUserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppUserWarehouses",
                columns: table => new
                {
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserWarehouses", x => new { x.AppUserId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_AppUserWarehouses_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppUserWarehouses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpectedVins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectedVins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpectedVins_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpectedVins_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpectedVins_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    PartNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SystemQuantity = table.Column<int>(type: "int", nullable: false),
                    SystemLocation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    CountedQuantity = table.Column<int>(type: "int", nullable: true),
                    CountedLocation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CountedByUserId = table.Column<int>(type: "int", nullable: true),
                    CountedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryParts_AppUsers_CountedByUserId",
                        column: x => x.CountedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryParts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryParts_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InventoryParts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScannedVins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Vin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedVins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScannedVins_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScannedVins_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScannedVins_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScannedVins_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryPartId = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartChangeLogs_InventoryParts_InventoryPartId",
                        column: x => x.InventoryPartId,
                        principalTable: "InventoryParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuantityCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryPartId = table.Column<int>(type: "int", nullable: true),
                    PartNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PartDescription = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    OldQuantity = table.Column<int>(type: "int", nullable: true),
                    NewQuantity = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    RequestedByAdminId = table.Column<int>(type: "int", nullable: false),
                    ApprovalLevel = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FirstApprovedByAdminId = table.Column<int>(type: "int", nullable: true),
                    FinalApprovedByAdminId = table.Column<int>(type: "int", nullable: true),
                    RejectedByAdminId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuantityCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuantityCorrections_Admins_RequestedByAdminId",
                        column: x => x.RequestedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuantityCorrections_InventoryParts_InventoryPartId",
                        column: x => x.InventoryPartId,
                        principalTable: "InventoryParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecountRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryPartId = table.Column<int>(type: "int", nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedByAdminId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResultQuantity = table.Column<int>(type: "int", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecountRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecountRequests_Admins_RequestedByAdminId",
                        column: x => x.RequestedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecountRequests_AppUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecountRequests_InventoryParts_InventoryPartId",
                        column: x => x.InventoryPartId,
                        principalTable: "InventoryParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VinCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScannedVinId = table.Column<int>(type: "int", nullable: false),
                    CurrentVin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    NewVin = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    ApprovedByAdminId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VinCorrections_Admins_ApprovedByAdminId",
                        column: x => x.ApprovedByAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VinCorrections_AppUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VinCorrections_ScannedVins_ScannedVinId",
                        column: x => x.ScannedVinId,
                        principalTable: "ScannedVins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Admins_BranchId",
                table: "Admins",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Admins_PhoneNumber",
                table: "Admins",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserBranches_BranchId",
                table: "AppUserBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_CreatedByAdminId",
                table: "AppUsers",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_PhoneNumber",
                table: "AppUsers",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUserWarehouses_WarehouseId",
                table: "AppUserWarehouses",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Name",
                table: "Branches",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedVins_BranchId",
                table: "ExpectedVins",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedVins_SessionId_Vin",
                table: "ExpectedVins",
                columns: new[] { "SessionId", "Vin" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedVins_WarehouseId",
                table: "ExpectedVins",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryParts_BranchId",
                table: "InventoryParts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryParts_CountedByUserId",
                table: "InventoryParts",
                column: "CountedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryParts_SessionId_PartNumber",
                table: "InventoryParts",
                columns: new[] { "SessionId", "PartNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryParts_WarehouseId",
                table: "InventoryParts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PartChangeLogs_InventoryPartId",
                table: "PartChangeLogs",
                column: "InventoryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_InventoryPartId",
                table: "QuantityCorrections",
                column: "InventoryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_RequestedByAdminId",
                table: "QuantityCorrections",
                column: "RequestedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_RecountRequests_AssignedUserId",
                table: "RecountRequests",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecountRequests_InventoryPartId",
                table: "RecountRequests",
                column: "InventoryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_RecountRequests_RequestedByAdminId",
                table: "RecountRequests",
                column: "RequestedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVins_BranchId",
                table: "ScannedVins",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVins_SessionId_Vin",
                table: "ScannedVins",
                columns: new[] { "SessionId", "Vin" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVins_UserId",
                table: "ScannedVins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVins_WarehouseId",
                table: "ScannedVins",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_StartedByAdminId",
                table: "Sessions",
                column: "StartedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_VinCorrections_ApprovedByAdminId",
                table: "VinCorrections",
                column: "ApprovedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_VinCorrections_RequestedByUserId",
                table: "VinCorrections",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VinCorrections_ScannedVinId",
                table: "VinCorrections",
                column: "ScannedVinId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_BranchId_Name",
                table: "Warehouses",
                columns: new[] { "BranchId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUserBranches");

            migrationBuilder.DropTable(
                name: "AppUserWarehouses");

            migrationBuilder.DropTable(
                name: "ExpectedVins");

            migrationBuilder.DropTable(
                name: "PartChangeLogs");

            migrationBuilder.DropTable(
                name: "QuantityCorrections");

            migrationBuilder.DropTable(
                name: "RecountRequests");

            migrationBuilder.DropTable(
                name: "VinCorrections");

            migrationBuilder.DropTable(
                name: "InventoryParts");

            migrationBuilder.DropTable(
                name: "ScannedVins");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Admins");

            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}
