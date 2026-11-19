IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Branches] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Branches] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [Admins] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [PhoneNumber] nvarchar(11) NOT NULL,
    [PasswordHash] nvarchar(256) NOT NULL,
    [Role] int NOT NULL,
    [BranchId] int NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Admins] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Admins_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Warehouses] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [Type] int NOT NULL,
    [BranchId] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Warehouses_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AppUsers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [PhoneNumber] nvarchar(11) NOT NULL,
    [PasswordHash] nvarchar(256) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedByAdminId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_AppUsers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AppUsers_Admins_CreatedByAdminId] FOREIGN KEY ([CreatedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [Sessions] (
    [Id] int NOT NULL IDENTITY,
    [Year] int NOT NULL,
    [Status] int NOT NULL,
    [StartedByAdminId] int NOT NULL,
    [StartedAt] datetime2 NOT NULL,
    [EndedAt] datetime2 NULL,
    [EndedByAdminId] int NULL,
    [FileName] nvarchar(260) NOT NULL,
    [StoredFileName] nvarchar(260) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Sessions_Admins_StartedByAdminId] FOREIGN KEY ([StartedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AppUserBranches] (
    [AppUserId] int NOT NULL,
    [BranchId] int NOT NULL,
    CONSTRAINT [PK_AppUserBranches] PRIMARY KEY ([AppUserId], [BranchId]),
    CONSTRAINT [FK_AppUserBranches_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AppUserBranches_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AppUserWarehouses] (
    [AppUserId] int NOT NULL,
    [WarehouseId] int NOT NULL,
    CONSTRAINT [PK_AppUserWarehouses] PRIMARY KEY ([AppUserId], [WarehouseId]),
    CONSTRAINT [FK_AppUserWarehouses_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AppUserWarehouses_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ExpectedVins] (
    [Id] int NOT NULL IDENTITY,
    [SessionId] int NOT NULL,
    [Vin] nvarchar(17) NOT NULL,
    [Model] nvarchar(100) NULL,
    [WarehouseId] int NULL,
    [BranchId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ExpectedVins] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExpectedVins_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExpectedVins_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ExpectedVins_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [InventoryParts] (
    [Id] int NOT NULL IDENTITY,
    [SessionId] int NOT NULL,
    [PartNumber] nvarchar(64) NOT NULL,
    [Description] nvarchar(400) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [SystemQuantity] int NOT NULL,
    [SystemLocation] nvarchar(64) NULL,
    [WarehouseId] int NULL,
    [BranchId] int NULL,
    [CountedQuantity] int NULL,
    [CountedLocation] nvarchar(64) NULL,
    [CountedByUserId] int NULL,
    [CountedAt] datetime2 NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_InventoryParts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InventoryParts_AppUsers_CountedByUserId] FOREIGN KEY ([CountedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryParts_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InventoryParts_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_InventoryParts_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ScannedVins] (
    [Id] int NOT NULL IDENTITY,
    [SessionId] int NOT NULL,
    [Vin] nvarchar(17) NOT NULL,
    [UserId] int NOT NULL,
    [WarehouseId] int NOT NULL,
    [BranchId] int NOT NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ScannedVins] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ScannedVins_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScannedVins_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScannedVins_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ScannedVins_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PartChangeLogs] (
    [Id] int NOT NULL IDENTITY,
    [InventoryPartId] int NOT NULL,
    [Field] nvarchar(64) NOT NULL,
    [OldValue] nvarchar(200) NULL,
    [NewValue] nvarchar(200) NULL,
    [ChangedBy] nvarchar(150) NOT NULL,
    [Source] nvarchar(64) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_PartChangeLogs] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PartChangeLogs_InventoryParts_InventoryPartId] FOREIGN KEY ([InventoryPartId]) REFERENCES [InventoryParts] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [QuantityCorrections] (
    [Id] int NOT NULL IDENTITY,
    [InventoryPartId] int NULL,
    [PartNumber] nvarchar(64) NOT NULL,
    [PartDescription] nvarchar(400) NOT NULL,
    [OldQuantity] int NULL,
    [NewQuantity] int NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [BranchId] int NULL,
    [RequestedByAdminId] int NOT NULL,
    [ApprovalLevel] int NOT NULL,
    [Status] int NOT NULL,
    [FirstApprovedByAdminId] int NULL,
    [FinalApprovedByAdminId] int NULL,
    [RejectedByAdminId] int NULL,
    [UpdatedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_QuantityCorrections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuantityCorrections_Admins_RequestedByAdminId] FOREIGN KEY ([RequestedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_QuantityCorrections_InventoryParts_InventoryPartId] FOREIGN KEY ([InventoryPartId]) REFERENCES [InventoryParts] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [RecountRequests] (
    [Id] int NOT NULL IDENTITY,
    [InventoryPartId] int NOT NULL,
    [AssignedUserId] int NOT NULL,
    [RequestedByAdminId] int NOT NULL,
    [Status] int NOT NULL,
    [ResultQuantity] int NULL,
    [CompletedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_RecountRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecountRequests_Admins_RequestedByAdminId] FOREIGN KEY ([RequestedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecountRequests_AppUsers_AssignedUserId] FOREIGN KEY ([AssignedUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecountRequests_InventoryParts_InventoryPartId] FOREIGN KEY ([InventoryPartId]) REFERENCES [InventoryParts] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [VinCorrections] (
    [Id] int NOT NULL IDENTITY,
    [ScannedVinId] int NOT NULL,
    [CurrentVin] nvarchar(17) NOT NULL,
    [NewVin] nvarchar(17) NOT NULL,
    [Reason] nvarchar(1000) NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [ApprovedByAdminId] int NULL,
    [Status] int NOT NULL,
    [ApprovedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_VinCorrections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_VinCorrections_Admins_ApprovedByAdminId] FOREIGN KEY ([ApprovedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VinCorrections_AppUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VinCorrections_ScannedVins_ScannedVinId] FOREIGN KEY ([ScannedVinId]) REFERENCES [ScannedVins] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_Admins_BranchId] ON [Admins] ([BranchId]);
GO

CREATE UNIQUE INDEX [IX_Admins_PhoneNumber] ON [Admins] ([PhoneNumber]);
GO

CREATE INDEX [IX_AppUserBranches_BranchId] ON [AppUserBranches] ([BranchId]);
GO

CREATE INDEX [IX_AppUsers_CreatedByAdminId] ON [AppUsers] ([CreatedByAdminId]);
GO

CREATE UNIQUE INDEX [IX_AppUsers_PhoneNumber] ON [AppUsers] ([PhoneNumber]);
GO

CREATE INDEX [IX_AppUserWarehouses_WarehouseId] ON [AppUserWarehouses] ([WarehouseId]);
GO

CREATE UNIQUE INDEX [IX_Branches_Name] ON [Branches] ([Name]);
GO

CREATE INDEX [IX_ExpectedVins_BranchId] ON [ExpectedVins] ([BranchId]);
GO

CREATE INDEX [IX_ExpectedVins_SessionId_Vin] ON [ExpectedVins] ([SessionId], [Vin]);
GO

CREATE INDEX [IX_ExpectedVins_WarehouseId] ON [ExpectedVins] ([WarehouseId]);
GO

CREATE INDEX [IX_InventoryParts_BranchId] ON [InventoryParts] ([BranchId]);
GO

CREATE INDEX [IX_InventoryParts_CountedByUserId] ON [InventoryParts] ([CountedByUserId]);
GO

CREATE INDEX [IX_InventoryParts_SessionId_PartNumber] ON [InventoryParts] ([SessionId], [PartNumber]);
GO

CREATE INDEX [IX_InventoryParts_WarehouseId] ON [InventoryParts] ([WarehouseId]);
GO

CREATE INDEX [IX_PartChangeLogs_InventoryPartId] ON [PartChangeLogs] ([InventoryPartId]);
GO

CREATE INDEX [IX_QuantityCorrections_InventoryPartId] ON [QuantityCorrections] ([InventoryPartId]);
GO

CREATE INDEX [IX_QuantityCorrections_RequestedByAdminId] ON [QuantityCorrections] ([RequestedByAdminId]);
GO

CREATE INDEX [IX_RecountRequests_AssignedUserId] ON [RecountRequests] ([AssignedUserId]);
GO

CREATE INDEX [IX_RecountRequests_InventoryPartId] ON [RecountRequests] ([InventoryPartId]);
GO

CREATE INDEX [IX_RecountRequests_RequestedByAdminId] ON [RecountRequests] ([RequestedByAdminId]);
GO

CREATE INDEX [IX_ScannedVins_BranchId] ON [ScannedVins] ([BranchId]);
GO

CREATE INDEX [IX_ScannedVins_SessionId_Vin] ON [ScannedVins] ([SessionId], [Vin]);
GO

CREATE INDEX [IX_ScannedVins_UserId] ON [ScannedVins] ([UserId]);
GO

CREATE INDEX [IX_ScannedVins_WarehouseId] ON [ScannedVins] ([WarehouseId]);
GO

CREATE INDEX [IX_Sessions_StartedByAdminId] ON [Sessions] ([StartedByAdminId]);
GO

CREATE INDEX [IX_VinCorrections_ApprovedByAdminId] ON [VinCorrections] ([ApprovedByAdminId]);
GO

CREATE INDEX [IX_VinCorrections_RequestedByUserId] ON [VinCorrections] ([RequestedByUserId]);
GO

CREATE INDEX [IX_VinCorrections_ScannedVinId] ON [VinCorrections] ([ScannedVinId]);
GO

CREATE UNIQUE INDEX [IX_Warehouses_BranchId_Name] ON [Warehouses] ([BranchId], [Name]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261028102332_Initial', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[QuantityCorrections]') AND [c].[name] = N'RequestedByAdminId');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [QuantityCorrections] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [QuantityCorrections] ALTER COLUMN [RequestedByAdminId] int NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [RequestedByUserId] int NULL;
GO

CREATE INDEX [IX_QuantityCorrections_RequestedByUserId] ON [QuantityCorrections] ([RequestedByUserId]);
GO

ALTER TABLE [QuantityCorrections] ADD CONSTRAINT [FK_QuantityCorrections_AppUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261028110203_AppQtyRequests', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Warehouses] ADD [Number] int NULL;
GO

ALTER TABLE [Branches] ADD [Number] int NULL;
GO

CREATE UNIQUE INDEX [IX_Warehouses_Number] ON [Warehouses] ([Number]) WHERE [Number] IS NOT NULL;
GO

CREATE UNIQUE INDEX [IX_Branches_Number] ON [Branches] ([Number]) WHERE [Number] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261028115304_BranchWarehouseNumbers', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [QuantityCorrections] ADD [DecisionReason] nvarchar(1000) NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [FinalApprovedAt] datetime2 NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [FirstApprovedAt] datetime2 NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [RejectedAt] datetime2 NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [WarehouseId] int NULL;
GO

CREATE TABLE [AdminBranches] (
    [AdminId] int NOT NULL,
    [BranchId] int NOT NULL,
    CONSTRAINT [PK_AdminBranches] PRIMARY KEY ([AdminId], [BranchId]),
    CONSTRAINT [FK_AdminBranches_Admins_AdminId] FOREIGN KEY ([AdminId]) REFERENCES [Admins] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AdminBranches_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [AdminWarehouses] (
    [AdminId] int NOT NULL,
    [WarehouseId] int NOT NULL,
    CONSTRAINT [PK_AdminWarehouses] PRIMARY KEY ([AdminId], [WarehouseId]),
    CONSTRAINT [FK_AdminWarehouses_Admins_AdminId] FOREIGN KEY ([AdminId]) REFERENCES [Admins] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AdminWarehouses_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [NewPartRequests] (
    [Id] int NOT NULL IDENTITY,
    [PartNumber] nvarchar(64) NOT NULL,
    [NormalizedPartNumber] nvarchar(64) NOT NULL,
    [Description] nvarchar(400) NOT NULL,
    [Quantity] int NOT NULL,
    [Location] nvarchar(64) NOT NULL,
    [WarehouseId] int NOT NULL,
    [BranchId] int NOT NULL,
    [SessionId] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [Latitude] float NOT NULL,
    [Longitude] float NOT NULL,
    [Status] int NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [SparePartsApprovedByAdminId] int NULL,
    [SparePartsApprovedAt] datetime2 NULL,
    [AccountantApprovedByAdminId] int NULL,
    [AccountantApprovedAt] datetime2 NULL,
    [ReturnReason] nvarchar(1000) NULL,
    [CancelledAt] datetime2 NULL,
    [InventoryPartId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_NewPartRequests] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NewPartRequests_AppUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NewPartRequests_Branches_BranchId] FOREIGN KEY ([BranchId]) REFERENCES [Branches] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NewPartRequests_InventoryParts_InventoryPartId] FOREIGN KEY ([InventoryPartId]) REFERENCES [InventoryParts] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NewPartRequests_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NewPartRequests_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [WorkflowAudits] (
    [Id] int NOT NULL IDENTITY,
    [Subject] nvarchar(32) NOT NULL,
    [SubjectId] int NOT NULL,
    [Action] nvarchar(32) NOT NULL,
    [FromStatus] nvarchar(64) NULL,
    [ToStatus] nvarchar(64) NOT NULL,
    [ActorAdminId] int NULL,
    [ActorUserId] int NULL,
    [ActorRole] nvarchar(64) NOT NULL,
    [Reason] nvarchar(1000) NULL,
    [SessionId] int NULL,
    [WarehouseId] int NULL,
    [BranchId] int NULL,
    [InventoryPartId] int NULL,
    [NewPartRequestId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_WorkflowAudits] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WorkflowAudits_Admins_ActorAdminId] FOREIGN KEY ([ActorAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowAudits_AppUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowAudits_NewPartRequests_NewPartRequestId] FOREIGN KEY ([NewPartRequestId]) REFERENCES [NewPartRequests] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_QuantityCorrections_Status_BranchId] ON [QuantityCorrections] ([Status], [BranchId]);
GO

CREATE INDEX [IX_QuantityCorrections_Status_WarehouseId] ON [QuantityCorrections] ([Status], [WarehouseId]);
GO

CREATE INDEX [IX_QuantityCorrections_WarehouseId] ON [QuantityCorrections] ([WarehouseId]);
GO

CREATE INDEX [IX_AdminBranches_BranchId] ON [AdminBranches] ([BranchId]);
GO

CREATE INDEX [IX_AdminWarehouses_WarehouseId] ON [AdminWarehouses] ([WarehouseId]);
GO

CREATE INDEX [IX_NewPartRequests_BranchId] ON [NewPartRequests] ([BranchId]);
GO

CREATE INDEX [IX_NewPartRequests_InventoryPartId] ON [NewPartRequests] ([InventoryPartId]);
GO

CREATE INDEX [IX_NewPartRequests_RequestedByUserId_CreatedAt] ON [NewPartRequests] ([RequestedByUserId], [CreatedAt]);
GO

CREATE INDEX [IX_NewPartRequests_SessionId_Status] ON [NewPartRequests] ([SessionId], [Status]);
GO

CREATE UNIQUE INDEX [IX_NewPartRequests_SessionId_WarehouseId_NormalizedPartNumber] ON [NewPartRequests] ([SessionId], [WarehouseId], [NormalizedPartNumber]) WHERE [Status] IN (0, 1, 2);
GO

CREATE INDEX [IX_NewPartRequests_Status_BranchId_CreatedAt] ON [NewPartRequests] ([Status], [BranchId], [CreatedAt]);
GO

CREATE INDEX [IX_NewPartRequests_WarehouseId] ON [NewPartRequests] ([WarehouseId]);
GO

CREATE INDEX [IX_WorkflowAudits_ActorAdminId] ON [WorkflowAudits] ([ActorAdminId]);
GO

CREATE INDEX [IX_WorkflowAudits_ActorUserId] ON [WorkflowAudits] ([ActorUserId]);
GO

CREATE INDEX [IX_WorkflowAudits_NewPartRequestId] ON [WorkflowAudits] ([NewPartRequestId]);
GO

CREATE INDEX [IX_WorkflowAudits_Subject_SubjectId_CreatedAt] ON [WorkflowAudits] ([Subject], [SubjectId], [CreatedAt]);
GO

ALTER TABLE [QuantityCorrections] ADD CONSTRAINT [FK_QuantityCorrections_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
GO

UPDATE QuantityCorrections
SET WarehouseId = (SELECT p.WarehouseId FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId)
WHERE WarehouseId IS NULL AND InventoryPartId IS NOT NULL
AND EXISTS (SELECT 1 FROM InventoryParts AS p WHERE p.Id = QuantityCorrections.InventoryPartId AND p.WarehouseId IS NOT NULL);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261106125420_ApprovalWorkflows', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Admins] ADD [AllBranches] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

CREATE TABLE [DeviceRegistrations] (
    [Id] int NOT NULL IDENTITY,
    [AppUserId] int NOT NULL,
    [Token] nvarchar(512) NOT NULL,
    [Platform] nvarchar(32) NOT NULL,
    [IsActive] bit NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_DeviceRegistrations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DeviceRegistrations_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_DeviceRegistrations_AppUserId] ON [DeviceRegistrations] ([AppUserId]);
GO

CREATE UNIQUE INDEX [IX_DeviceRegistrations_Token] ON [DeviceRegistrations] ([Token]) WHERE [Token] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108190000_RolesScopeDevices', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [QuantityCorrections] ADD [AudienceKind] int NULL;
GO

ALTER TABLE [QuantityCorrections] ADD [AudienceUserId] int NULL;
GO

CREATE INDEX [IX_QuantityCorrections_AudienceUserId] ON [QuantityCorrections] ([AudienceUserId]);
GO

ALTER TABLE [QuantityCorrections] ADD CONSTRAINT [FK_QuantityCorrections_AppUsers_AudienceUserId] FOREIGN KEY ([AudienceUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261109080000_QuantityAudience', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [ExpectedVins] ADD [Dealer] nvarchar(100) NULL;
GO

ALTER TABLE [ExpectedVins] ADD [ManufactureYear] int NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261115090000_VehicleDealerYear', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [AppUsers] ADD [CanPickDuplicateRow] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261115100000_DuplicateRowPick', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [RecountAsks] (
    [Id] int NOT NULL IDENTITY,
    [InventoryPartId] int NOT NULL,
    [RequestedByUserId] int NOT NULL,
    [Status] int NOT NULL,
    [DecidedByAdminId] int NULL,
    [DecidedAt] datetime2 NULL,
    [DecisionReason] nvarchar(1000) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_RecountAsks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RecountAsks_Admins_DecidedByAdminId] FOREIGN KEY ([DecidedByAdminId]) REFERENCES [Admins] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecountAsks_AppUsers_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RecountAsks_InventoryParts_InventoryPartId] FOREIGN KEY ([InventoryPartId]) REFERENCES [InventoryParts] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_RecountAsks_DecidedByAdminId] ON [RecountAsks] ([DecidedByAdminId]);
GO

CREATE INDEX [IX_RecountAsks_InventoryPartId_RequestedByUserId_Status] ON [RecountAsks] ([InventoryPartId], [RequestedByUserId], [Status]);
GO

CREATE INDEX [IX_RecountAsks_RequestedByUserId] ON [RecountAsks] ([RequestedByUserId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117171939_RecountAsks', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [AppUsers] ADD [CountType] int NOT NULL DEFAULT 3;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118090000_UserCountType', N'8.0.11');
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

ALTER TABLE [Sessions] ADD [Kind] int NOT NULL DEFAULT 0;
GO

ALTER TABLE [Sessions] ADD [Month] int NULL;
GO

ALTER TABLE [Sessions] ADD [Name] nvarchar(60) NOT NULL DEFAULT N'';
GO

UPDATE Sessions SET Name = N'جرد سنوي ' + CAST([Year] AS nvarchar(10)) WHERE Name = N''
GO

CREATE INDEX [IX_Sessions_Year_Kind_Month] ON [Sessions] ([Year], [Kind], [Month]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261119090000_MonthlySessions', N'8.0.11');
GO

COMMIT;
GO

