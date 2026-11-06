using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HyundaiStock.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "QuantityCorrections",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalApprovedAt",
                table: "QuantityCorrections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstApprovedAt",
                table: "QuantityCorrections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "QuantityCorrections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "QuantityCorrections",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminBranches",
                columns: table => new
                {
                    AdminId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminBranches", x => new { x.AdminId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_AdminBranches_Admins_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminWarehouses",
                columns: table => new
                {
                    AdminId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminWarehouses", x => new { x.AdminId, x.WarehouseId });
                    table.ForeignKey(
                        name: "FK_AdminWarehouses_Admins_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminWarehouses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NewPartRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NormalizedPartNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SparePartsApprovedByAdminId = table.Column<int>(type: "int", nullable: true),
                    SparePartsApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccountantApprovedByAdminId = table.Column<int>(type: "int", nullable: true),
                    AccountantApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InventoryPartId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewPartRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewPartRequests_AppUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewPartRequests_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewPartRequests_InventoryParts_InventoryPartId",
                        column: x => x.InventoryPartId,
                        principalTable: "InventoryParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewPartRequests_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewPartRequests_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Subject = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorAdminId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    BranchId = table.Column<int>(type: "int", nullable: true),
                    InventoryPartId = table.Column<int>(type: "int", nullable: true),
                    NewPartRequestId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAudits_Admins_ActorAdminId",
                        column: x => x.ActorAdminId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowAudits_AppUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowAudits_NewPartRequests_NewPartRequestId",
                        column: x => x.NewPartRequestId,
                        principalTable: "NewPartRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_Status_BranchId",
                table: "QuantityCorrections",
                columns: new[] { "Status", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_Status_WarehouseId",
                table: "QuantityCorrections",
                columns: new[] { "Status", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuantityCorrections_WarehouseId",
                table: "QuantityCorrections",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminBranches_BranchId",
                table: "AdminBranches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminWarehouses_WarehouseId",
                table: "AdminWarehouses",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_BranchId",
                table: "NewPartRequests",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_InventoryPartId",
                table: "NewPartRequests",
                column: "InventoryPartId");

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_RequestedByUserId_CreatedAt",
                table: "NewPartRequests",
                columns: new[] { "RequestedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_SessionId_Status",
                table: "NewPartRequests",
                columns: new[] { "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_SessionId_WarehouseId_NormalizedPartNumber",
                table: "NewPartRequests",
                columns: new[] { "SessionId", "WarehouseId", "NormalizedPartNumber" },
                unique: true,
                filter: "[Status] IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_Status_BranchId_CreatedAt",
                table: "NewPartRequests",
                columns: new[] { "Status", "BranchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewPartRequests_WarehouseId",
                table: "NewPartRequests",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAudits_ActorAdminId",
                table: "WorkflowAudits",
                column: "ActorAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAudits_ActorUserId",
                table: "WorkflowAudits",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAudits_NewPartRequestId",
                table: "WorkflowAudits",
                column: "NewPartRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAudits_Subject_SubjectId_CreatedAt",
                table: "WorkflowAudits",
                columns: new[] { "Subject", "SubjectId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuantityCorrections_Warehouses_WarehouseId",
                table: "QuantityCorrections",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE QuantityCorrections
                SET WarehouseId = (SELECT p.WarehouseId FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId)
                WHERE WarehouseId IS NULL AND InventoryPartId IS NOT NULL
                AND EXISTS (SELECT 1 FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId AND p.WarehouseId IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QuantityCorrections_Warehouses_WarehouseId",
                table: "QuantityCorrections");

            migrationBuilder.DropTable(
                name: "AdminBranches");

            migrationBuilder.DropTable(
                name: "AdminWarehouses");

            migrationBuilder.DropTable(
                name: "WorkflowAudits");

            migrationBuilder.DropTable(
                name: "NewPartRequests");

            migrationBuilder.DropIndex(
                name: "IX_QuantityCorrections_Status_BranchId",
                table: "QuantityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_QuantityCorrections_Status_WarehouseId",
                table: "QuantityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_QuantityCorrections_WarehouseId",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "FinalApprovedAt",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "FirstApprovedAt",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "QuantityCorrections");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "QuantityCorrections");
        }
    }
}
