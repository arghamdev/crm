-- Generated from 202609270007_DealerChannelGovernance.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609260006_OrderIntegrationVisibility') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609270007_DealerChannelGovernance')
BEGIN
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'channel') EXEC(N'CREATE SCHEMA [channel]');

CREATE TABLE [channel].[Dealers] (
    [Id] uniqueidentifier NOT NULL, [DealerId] nvarchar(64) NOT NULL, [Code] nvarchar(32) NOT NULL,
    [LegalName] nvarchar(250) NOT NULL, [TradeName] nvarchar(200) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
    [City] nvarchar(100) NOT NULL, [NationalId] nvarchar(32) NULL, [Phone] nvarchar(40) NULL,
    [Email] nvarchar(256) NULL, [ChannelManagerUserId] uniqueidentifier NOT NULL,
    [Status] nvarchar(32) NOT NULL, [StatusReason] nvarchar(1000) NULL,
    [ActivatedAtUtc] datetimeoffset(3) NULL, [TerminatedAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Dealers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Dealers_Users_ChannelManagerUserId] FOREIGN KEY ([ChannelManagerUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [channel].[DealerContracts] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL,
    [ContractNumber] nvarchar(64) NOT NULL, [ValidFromUtc] datetimeoffset(3) NOT NULL,
    [ValidToUtc] datetimeoffset(3) NOT NULL, [AnnualTarget] decimal(18,2) NOT NULL,
    [PaymentTerms] nvarchar(1000) NOT NULL, [RequestedByUserId] uniqueidentifier NOT NULL,
    [Status] nvarchar(32) NOT NULL, [ApprovedByUserId] uniqueidentifier NULL,
    [ApprovedAtUtc] datetimeoffset(3) NULL, [DecisionReason] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerContracts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerContracts_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DealerContracts_Users_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [iam].[Users] ([Id]),
    CONSTRAINT [FK_DealerContracts_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [channel].[DealerTerritoryAssignments] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NOT NULL, [DealerId] uniqueidentifier NOT NULL, [IsExclusive] bit NOT NULL,
    [ValidFromUtc] datetimeoffset(3) NOT NULL, [ValidToUtc] datetimeoffset(3) NULL,
    [RequestedByUserId] uniqueidentifier NOT NULL, [Status] nvarchar(32) NOT NULL,
    [ApprovedByUserId] uniqueidentifier NULL, [ApprovedAtUtc] datetimeoffset(3) NULL,
    [Reason] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerTerritoryAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerTerritoryAssignments_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DealerTerritoryAssignments_Users_RequestedByUserId] FOREIGN KEY ([RequestedByUserId]) REFERENCES [iam].[Users] ([Id]),
    CONSTRAINT [FK_DealerTerritoryAssignments_Users_ApprovedByUserId] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [channel].[DealerCustomerAssignments] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [ValidFromUtc] datetimeoffset(3) NOT NULL, [ValidToUtc] datetimeoffset(3) NULL,
    [AssignedByUserId] uniqueidentifier NOT NULL, [Reason] nvarchar(1000) NOT NULL,
    [EndedByUserId] uniqueidentifier NULL, [EndReason] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerCustomerAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerCustomerAssignments_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DealerCustomerAssignments_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_DealerCustomerAssignments_Users_AssignedByUserId] FOREIGN KEY ([AssignedByUserId]) REFERENCES [iam].[Users] ([Id]),
    CONSTRAINT [FK_DealerCustomerAssignments_Users_EndedByUserId] FOREIGN KEY ([EndedByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [channel].[DealerTargets] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL,
    [PeriodFromUtc] datetimeoffset(3) NOT NULL, [PeriodToUtc] datetimeoffset(3) NOT NULL,
    [Amount] decimal(18,2) NOT NULL, [Source] nvarchar(200) NOT NULL, [SetByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerTargets] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerTargets_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DealerTargets_Users_SetByUserId] FOREIGN KEY ([SetByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [channel].[DealerFinancialSnapshots] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL,
    [CreditLimit] decimal(18,2) NOT NULL, [CreditUsed] decimal(18,2) NOT NULL,
    [Balance] decimal(18,2) NOT NULL, [OverdueAmount] decimal(18,2) NOT NULL,
    [Source] nvarchar(200) NOT NULL, [SynchronizedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerFinancialSnapshots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerFinancialSnapshots_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [channel].[DealerPerformanceSnapshots] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL,
    [PeriodFromUtc] datetimeoffset(3) NOT NULL, [PeriodToUtc] datetimeoffset(3) NOT NULL,
    [NetSales] decimal(18,2) NOT NULL, [OrderCount] int NOT NULL,
    [Source] nvarchar(200) NOT NULL, [SynchronizedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerPerformanceSnapshots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerPerformanceSnapshots_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [channel].[DealerStatusHistory] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [DealerId] uniqueidentifier NOT NULL,
    [FromStatus] nvarchar(32) NULL, [ToStatus] nvarchar(32) NOT NULL,
    [Reason] nvarchar(1000) NOT NULL, [ChangedByUserId] uniqueidentifier NOT NULL,
    [ChangedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerStatusHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerStatusHistory_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DealerStatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE UNIQUE INDEX [IX_Dealers_CompanyId_DealerId] ON [channel].[Dealers] ([CompanyId],[DealerId]);
CREATE UNIQUE INDEX [IX_Dealers_CompanyId_Code] ON [channel].[Dealers] ([CompanyId],[Code]);
CREATE UNIQUE INDEX [IX_Dealers_CompanyId_NationalId] ON [channel].[Dealers] ([CompanyId],[NationalId]) WHERE [NationalId] IS NOT NULL;
CREATE INDEX [IX_Dealers_CompanyId_BranchId_TerritoryId_Status] ON [channel].[Dealers] ([CompanyId],[BranchId],[TerritoryId],[Status]);
CREATE INDEX [IX_Dealers_ChannelManagerUserId] ON [channel].[Dealers] ([ChannelManagerUserId]);
CREATE UNIQUE INDEX [IX_DealerContracts_CompanyId_ContractNumber] ON [channel].[DealerContracts] ([CompanyId],[ContractNumber]);
CREATE INDEX [IX_DealerContracts_DealerId_Status_ValidToUtc] ON [channel].[DealerContracts] ([DealerId],[Status],[ValidToUtc]);
CREATE INDEX [IX_DealerContracts_RequestedByUserId] ON [channel].[DealerContracts] ([RequestedByUserId]);
CREATE INDEX [IX_DealerContracts_ApprovedByUserId] ON [channel].[DealerContracts] ([ApprovedByUserId]);
CREATE INDEX [IX_DealerTerritoryAssignments_CompanyId_TerritoryId_Status_ValidFromUtc] ON [channel].[DealerTerritoryAssignments] ([CompanyId],[TerritoryId],[Status],[ValidFromUtc]);
CREATE INDEX [IX_DealerTerritoryAssignments_DealerId_TerritoryId_Status] ON [channel].[DealerTerritoryAssignments] ([DealerId],[TerritoryId],[Status]);
CREATE INDEX [IX_DealerTerritoryAssignments_RequestedByUserId] ON [channel].[DealerTerritoryAssignments] ([RequestedByUserId]);
CREATE INDEX [IX_DealerTerritoryAssignments_ApprovedByUserId] ON [channel].[DealerTerritoryAssignments] ([ApprovedByUserId]);
CREATE INDEX [IX_DealerCustomerAssignments_CompanyId_CustomerId_ValidToUtc] ON [channel].[DealerCustomerAssignments] ([CompanyId],[CustomerId],[ValidToUtc]);
CREATE INDEX [IX_DealerCustomerAssignments_DealerId_ValidToUtc] ON [channel].[DealerCustomerAssignments] ([DealerId],[ValidToUtc]);
CREATE INDEX [IX_DealerCustomerAssignments_CustomerId] ON [channel].[DealerCustomerAssignments] ([CustomerId]);
CREATE INDEX [IX_DealerCustomerAssignments_AssignedByUserId] ON [channel].[DealerCustomerAssignments] ([AssignedByUserId]);
CREATE INDEX [IX_DealerCustomerAssignments_EndedByUserId] ON [channel].[DealerCustomerAssignments] ([EndedByUserId]);
CREATE UNIQUE INDEX [IX_DealerTargets_DealerId_PeriodFromUtc_PeriodToUtc] ON [channel].[DealerTargets] ([DealerId],[PeriodFromUtc],[PeriodToUtc]);
CREATE INDEX [IX_DealerTargets_SetByUserId] ON [channel].[DealerTargets] ([SetByUserId]);
CREATE UNIQUE INDEX [IX_DealerFinancialSnapshots_DealerId_SynchronizedAtUtc] ON [channel].[DealerFinancialSnapshots] ([DealerId],[SynchronizedAtUtc]);
CREATE UNIQUE INDEX [IX_DealerPerformanceSnapshots_DealerId_PeriodFromUtc_PeriodToUtc_SynchronizedAtUtc] ON [channel].[DealerPerformanceSnapshots] ([DealerId],[PeriodFromUtc],[PeriodToUtc],[SynchronizedAtUtc]);
CREATE INDEX [IX_DealerStatusHistory_DealerId_ChangedAtUtc] ON [channel].[DealerStatusHistory] ([DealerId],[ChangedAtUtc]);
CREATE INDEX [IX_DealerStatusHistory_ChangedByUserId] ON [channel].[DealerStatusHistory] ([ChangedByUserId]);
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202609270007_DealerChannelGovernance',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
