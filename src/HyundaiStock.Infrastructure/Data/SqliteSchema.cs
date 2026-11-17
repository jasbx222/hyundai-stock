using System.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Data;

/// <summary>
/// Sqlite is created with EnsureCreated, which does not alter a database that already exists.
/// This adds the approval-workflow tables and columns without dropping local data.
/// </summary>
public static class SqliteSchema
{
    public static async Task EnsureCurrentAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) return;
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        if (!await Table(conn, "RecountAsks"))
        {
            await Sql(db, """
                CREATE TABLE RecountAsks (
                    Id INTEGER NOT NULL CONSTRAINT PK_RecountAsks PRIMARY KEY AUTOINCREMENT,
                    InventoryPartId INTEGER NOT NULL,
                    RequestedByUserId INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    DecidedByAdminId INTEGER NULL,
                    DecidedAt TEXT NULL,
                    DecisionReason TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_RecountAsks_InventoryParts_InventoryPartId FOREIGN KEY (InventoryPartId) REFERENCES InventoryParts (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_RecountAsks_AppUsers_RequestedByUserId FOREIGN KEY (RequestedByUserId) REFERENCES AppUsers (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_RecountAsks_Admins_DecidedByAdminId FOREIGN KEY (DecidedByAdminId) REFERENCES Admins (Id) ON DELETE RESTRICT
                )
                """);
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_RecountAsks_InventoryPartId_RequestedByUserId_Status ON RecountAsks (InventoryPartId, RequestedByUserId, Status)");
        }

        if (!await Table(conn, "AdminBranches"))
        {
            await Sql(db, """
                CREATE TABLE AdminBranches (
                    AdminId INTEGER NOT NULL,
                    BranchId INTEGER NOT NULL,
                    CONSTRAINT PK_AdminBranches PRIMARY KEY (AdminId, BranchId),
                    CONSTRAINT FK_AdminBranches_Admins_AdminId FOREIGN KEY (AdminId) REFERENCES Admins (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_AdminBranches_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES Branches (Id) ON DELETE RESTRICT
                )
                """);
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_AdminBranches_BranchId ON AdminBranches (BranchId)");
        }

        if (!await Table(conn, "AdminWarehouses"))
        {
            await Sql(db, """
                CREATE TABLE AdminWarehouses (
                    AdminId INTEGER NOT NULL,
                    WarehouseId INTEGER NOT NULL,
                    CONSTRAINT PK_AdminWarehouses PRIMARY KEY (AdminId, WarehouseId),
                    CONSTRAINT FK_AdminWarehouses_Admins_AdminId FOREIGN KEY (AdminId) REFERENCES Admins (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_AdminWarehouses_Warehouses_WarehouseId FOREIGN KEY (WarehouseId) REFERENCES Warehouses (Id) ON DELETE RESTRICT
                )
                """);
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_AdminWarehouses_WarehouseId ON AdminWarehouses (WarehouseId)");
        }

        await Column(conn, db, "QuantityCorrections", "WarehouseId", "WarehouseId INTEGER NULL");
        await Column(conn, db, "QuantityCorrections", "FirstApprovedAt", "FirstApprovedAt TEXT NULL");
        await Column(conn, db, "QuantityCorrections", "FinalApprovedAt", "FinalApprovedAt TEXT NULL");
        await Column(conn, db, "QuantityCorrections", "RejectedAt", "RejectedAt TEXT NULL");
        await Column(conn, db, "QuantityCorrections", "DecisionReason", "DecisionReason TEXT NULL");
        await Column(conn, db, "QuantityCorrections", "AudienceKind", "AudienceKind INTEGER NULL");
        await Column(conn, db, "QuantityCorrections", "AudienceUserId", "AudienceUserId INTEGER NULL");
        await Sql(db, """
            UPDATE QuantityCorrections
            SET WarehouseId = (SELECT p.WarehouseId FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId)
            WHERE WarehouseId IS NULL AND InventoryPartId IS NOT NULL
            AND EXISTS (SELECT 1 FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId AND p.WarehouseId IS NOT NULL)
            """);
        await Sql(db, "CREATE INDEX IF NOT EXISTS IX_QuantityCorrections_Status_BranchId ON QuantityCorrections (Status, BranchId)");
        await Sql(db, "CREATE INDEX IF NOT EXISTS IX_QuantityCorrections_Status_WarehouseId ON QuantityCorrections (Status, WarehouseId)");
        await Sql(db, "CREATE INDEX IF NOT EXISTS IX_QuantityCorrections_WarehouseId ON QuantityCorrections (WarehouseId)");

        if (!await Table(conn, "NewPartRequests"))
        {
            await Sql(db, """
                CREATE TABLE NewPartRequests (
                    Id INTEGER NOT NULL CONSTRAINT PK_NewPartRequests PRIMARY KEY AUTOINCREMENT,
                    PartNumber TEXT NOT NULL,
                    NormalizedPartNumber TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Location TEXT NOT NULL,
                    WarehouseId INTEGER NOT NULL,
                    BranchId INTEGER NOT NULL,
                    SessionId INTEGER NOT NULL,
                    RequestedByUserId INTEGER NOT NULL,
                    Latitude REAL NOT NULL,
                    Longitude REAL NOT NULL,
                    Status INTEGER NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    SparePartsApprovedByAdminId INTEGER NULL,
                    SparePartsApprovedAt TEXT NULL,
                    AccountantApprovedByAdminId INTEGER NULL,
                    AccountantApprovedAt TEXT NULL,
                    ReturnReason TEXT NULL,
                    CancelledAt TEXT NULL,
                    InventoryPartId INTEGER NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_NewPartRequests_Warehouses_WarehouseId FOREIGN KEY (WarehouseId) REFERENCES Warehouses (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_NewPartRequests_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES Branches (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_NewPartRequests_Sessions_SessionId FOREIGN KEY (SessionId) REFERENCES Sessions (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_NewPartRequests_AppUsers_RequestedByUserId FOREIGN KEY (RequestedByUserId) REFERENCES AppUsers (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_NewPartRequests_InventoryParts_InventoryPartId FOREIGN KEY (InventoryPartId) REFERENCES InventoryParts (Id) ON DELETE RESTRICT
                )
                """);
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_RequestedByUserId_CreatedAt ON NewPartRequests (RequestedByUserId, CreatedAt)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_Status_BranchId_CreatedAt ON NewPartRequests (Status, BranchId, CreatedAt)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_SessionId_Status ON NewPartRequests (SessionId, Status)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_WarehouseId ON NewPartRequests (WarehouseId)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_BranchId ON NewPartRequests (BranchId)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_SessionId ON NewPartRequests (SessionId)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_NewPartRequests_InventoryPartId ON NewPartRequests (InventoryPartId)");
            await Sql(db, """
                CREATE UNIQUE INDEX IF NOT EXISTS IX_NewPartRequests_SessionId_WarehouseId_NormalizedPartNumber
                ON NewPartRequests (SessionId, WarehouseId, NormalizedPartNumber)
                WHERE "Status" IN (0, 1, 2)
                """);
        }

        if (!await Table(conn, "WorkflowAudits"))
        {
            await Sql(db, """
                CREATE TABLE WorkflowAudits (
                    Id INTEGER NOT NULL CONSTRAINT PK_WorkflowAudits PRIMARY KEY AUTOINCREMENT,
                    Subject TEXT NOT NULL,
                    SubjectId INTEGER NOT NULL,
                    Action TEXT NOT NULL,
                    FromStatus TEXT NULL,
                    ToStatus TEXT NOT NULL,
                    ActorAdminId INTEGER NULL,
                    ActorUserId INTEGER NULL,
                    ActorRole TEXT NOT NULL,
                    Reason TEXT NULL,
                    SessionId INTEGER NULL,
                    WarehouseId INTEGER NULL,
                    BranchId INTEGER NULL,
                    InventoryPartId INTEGER NULL,
                    NewPartRequestId INTEGER NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_WorkflowAudits_Admins_ActorAdminId FOREIGN KEY (ActorAdminId) REFERENCES Admins (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_WorkflowAudits_AppUsers_ActorUserId FOREIGN KEY (ActorUserId) REFERENCES AppUsers (Id) ON DELETE RESTRICT,
                    CONSTRAINT FK_WorkflowAudits_NewPartRequests_NewPartRequestId FOREIGN KEY (NewPartRequestId) REFERENCES NewPartRequests (Id) ON DELETE RESTRICT
                )
                """);
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_WorkflowAudits_Subject_SubjectId_CreatedAt ON WorkflowAudits (Subject, SubjectId, CreatedAt)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_WorkflowAudits_ActorAdminId ON WorkflowAudits (ActorAdminId)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_WorkflowAudits_ActorUserId ON WorkflowAudits (ActorUserId)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_WorkflowAudits_NewPartRequestId ON WorkflowAudits (NewPartRequestId)");
        }

        await Column(conn, db, "Admins", "AllBranches", "AllBranches INTEGER NOT NULL DEFAULT 0");
        await Column(conn, db, "ExpectedVins", "Dealer", "Dealer TEXT NULL");
        await Column(conn, db, "AppUsers", "CanPickDuplicateRow", "CanPickDuplicateRow INTEGER NOT NULL DEFAULT 0");
        await Column(conn, db, "ExpectedVins", "ManufactureYear", "ManufactureYear INTEGER NULL");
        if (!await Table(conn, "DeviceRegistrations"))
        {
            await Sql(db, """
                CREATE TABLE DeviceRegistrations (
                    Id INTEGER NOT NULL CONSTRAINT PK_DeviceRegistrations PRIMARY KEY AUTOINCREMENT,
                    AppUserId INTEGER NOT NULL,
                    Token TEXT NOT NULL,
                    Platform TEXT NOT NULL,
                    IsActive INTEGER NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_DeviceRegistrations_AppUsers_AppUserId FOREIGN KEY (AppUserId) REFERENCES AppUsers (Id) ON DELETE CASCADE
                )
                """);
            await Sql(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceRegistrations_Token ON DeviceRegistrations (Token)");
            await Sql(db, "CREATE INDEX IF NOT EXISTS IX_DeviceRegistrations_AppUserId ON DeviceRegistrations (AppUserId)");
        }
    }

    private static async Task<bool> Table(System.Data.Common.DbConnection conn, string name)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $n";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = name;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task Column(System.Data.Common.DbConnection conn, AppDbContext db, string table, string column, string definition)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $n";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = column;
        cmd.Parameters.Add(p);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
            await Sql(db, $"ALTER TABLE {table} ADD COLUMN {definition}");
    }

    private static Task Sql(AppDbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);
}
