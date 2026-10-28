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

