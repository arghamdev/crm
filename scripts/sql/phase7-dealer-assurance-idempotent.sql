-- Generated from 202610030015_DealerAssurance.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030014_CustomerProfiles') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030015_DealerAssurance')
BEGIN
ALTER TABLE [channel].[DealerCommissionStatements] ADD [SplitDefinition] nvarchar(60) NULL;
CREATE TABLE [channel].[CommissionPayoutMessages] (
    [Id] uniqueidentifier NOT NULL,
    [StatementId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [IdempotencyKey] nvarchar(80) NOT NULL,
    [Payload] nvarchar(4000) NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [AttemptCount] int NOT NULL,
    [NextAttemptAtUtc] datetimeoffset(3) NULL,
    [LastError] nvarchar(500) NULL,
    [ExternalReference] nvarchar(80) NULL,
    [CompletedAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_CommissionPayoutMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CommissionPayoutMessages_DealerCommissionStatements_StatementId] FOREIGN KEY ([StatementId]) REFERENCES [channel].[DealerCommissionStatements] ([Id])
);
CREATE TABLE [channel].[DealerGuarantees] (
    [Id] uniqueidentifier NOT NULL,
    [DealerId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [Type] nvarchar(24) NOT NULL,
    [Number] nvarchar(60) NOT NULL,
    [Issuer] nvarchar(120) NULL,
    [Amount] decimal(18,2) NOT NULL,
    [IssuedOn] date NOT NULL,
    [ExpiresOn] date NULL,
    [Notes] nvarchar(500) NULL,
    [Status] nvarchar(16) NOT NULL,
    [RegisteredByUserId] uniqueidentifier NOT NULL,
    [DecidedByUserId] uniqueidentifier NULL,
    [DecidedAtUtc] datetimeoffset(3) NULL,
    [DecisionReason] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerGuarantees] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]),
    CONSTRAINT [FK_DealerGuarantees_Users_DecidedByUserId] FOREIGN KEY ([DecidedByUserId]) REFERENCES [iam].[Users] ([Id]),
    CONSTRAINT [FK_DealerGuarantees_Users_RegisteredByUserId] FOREIGN KEY ([RegisteredByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [channel].[DealerTrainings] (
    [Id] uniqueidentifier NOT NULL,
    [DealerId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [Title] nvarchar(160) NOT NULL,
    [Topic] nvarchar(24) NOT NULL,
    [HeldOn] date NOT NULL,
    [Hours] decimal(5,1) NOT NULL,
    [Participants] int NOT NULL,
    [Score] decimal(5,1) NULL,
    [CertificateValidTo] date NULL,
    [RecordedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerTrainings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerTrainings_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]),
    CONSTRAINT [FK_DealerTrainings_Users_RecordedByUserId] FOREIGN KEY ([RecordedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_CommissionPayoutMessages_IdempotencyKey] ON [channel].[CommissionPayoutMessages] ([IdempotencyKey]);
CREATE UNIQUE INDEX [IX_CommissionPayoutMessages_StatementId] ON [channel].[CommissionPayoutMessages] ([StatementId]);
CREATE INDEX [IX_CommissionPayoutMessages_Status_NextAttemptAtUtc] ON [channel].[CommissionPayoutMessages] ([Status], [NextAttemptAtUtc]);
CREATE INDEX [IX_DealerGuarantees_CompanyId_Status_ExpiresOn] ON [channel].[DealerGuarantees] ([CompanyId], [Status], [ExpiresOn]);
CREATE INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees] ([DealerId], [Status]);
CREATE INDEX [IX_DealerGuarantees_DecidedByUserId] ON [channel].[DealerGuarantees] ([DecidedByUserId]);
CREATE INDEX [IX_DealerGuarantees_RegisteredByUserId] ON [channel].[DealerGuarantees] ([RegisteredByUserId]);
CREATE INDEX [IX_DealerTrainings_DealerId_HeldOn] ON [channel].[DealerTrainings] ([DealerId], [HeldOn]);
CREATE INDEX [IX_DealerTrainings_RecordedByUserId] ON [channel].[DealerTrainings] ([RecordedByUserId]);
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '61e13a59-1d7e-56a4-97ed-516d687e5f96',N'SalesManager',N'Dealer.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Dealer.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'abe450fe-3c40-5ba7-9fe5-351778cf0d89',N'SalesManager',N'Dealer.Training.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Dealer.Training.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'c1dcff86-b0c7-5db8-937e-fd8d540f4b1e',N'FinanceManager',N'Dealer.Guarantee.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Guarantee.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'dcb86830-2b6b-54c2-b18e-d2254793d6ce',N'FinanceManager',N'Dealer.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '8ad732bf-f8ba-585a-b2cf-d9da69e1a4a9',N'FinanceManager',N'Dealer.Training.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Training.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7b5e5810-181e-5dcf-839f-a5e5b350b051',N'ChannelManager',N'Dealer.Guarantee.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Guarantee.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7e340ee4-be0f-5c89-b017-c757079e92c0',N'ChannelManager',N'Dealer.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '24777b5d-e930-526c-85c7-5194dc4826f4',N'ChannelManager',N'Dealer.Training.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Training.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fc5ce11e-dfee-5dcd-bfae-c99adfec2fb4',N'ChannelManager',N'Dealer.Training.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Training.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f31456bc-c68d-554f-ba54-82f7f4f972b8',N'Executive',N'Dealer.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Dealer.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a1dc2980-7df6-5d81-940b-ab5fa1134b7a',N'Executive',N'Dealer.Training.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Dealer.Training.Read');
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030015_DealerAssurance',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
