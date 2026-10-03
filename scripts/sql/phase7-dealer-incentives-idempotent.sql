-- Generated from 202610030013_DealerIncentives.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030012_RoleScopes') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030013_DealerIncentives')
BEGIN
CREATE TABLE [channel].[DealerCommissionPlans] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [TierDefinition] nvarchar(400) NOT NULL,
    [UpdatedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerCommissionPlans] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerCommissionPlans_Users_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [channel].[DealerEvaluations] (
    [Id] uniqueidentifier NOT NULL,
    [DealerId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [PeriodFromUtc] datetimeoffset(3) NOT NULL,
    [PeriodToUtc] datetimeoffset(3) NOT NULL,
    [AchievementScore] decimal(5,1) NOT NULL,
    [CollectionScore] decimal(5,1) NOT NULL,
    [GrowthScore] decimal(5,1) NOT NULL,
    [ServiceScore] decimal(5,1) NOT NULL,
    [TotalScore] decimal(5,1) NOT NULL,
    [Tier] nvarchar(16) NOT NULL,
    [Rank] int NOT NULL,
    [RankedDealerCount] int NOT NULL,
    [EvaluatedByUserId] uniqueidentifier NOT NULL,
    [EvaluatedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerEvaluations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerEvaluations_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]),
    CONSTRAINT [FK_DealerEvaluations_Users_EvaluatedByUserId] FOREIGN KEY ([EvaluatedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [channel].[DealerCommissionStatements] (
    [Id] uniqueidentifier NOT NULL,
    [DealerId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [PeriodFromUtc] datetimeoffset(3) NOT NULL,
    [PeriodToUtc] datetimeoffset(3) NOT NULL,
    [NetSales] decimal(18,2) NOT NULL,
    [TargetAmount] decimal(18,2) NOT NULL,
    [OverdueAmount] decimal(18,2) NOT NULL,
    [PlanId] uniqueidentifier NOT NULL,
    [AchievementPercent] decimal(9,2) NOT NULL,
    [RatePercent] decimal(5,2) NOT NULL,
    [CommissionAmount] decimal(18,2) NOT NULL,
    [Status] nvarchar(24) NOT NULL,
    [CalculatedByUserId] uniqueidentifier NOT NULL,
    [CalculatedAtUtc] datetimeoffset(3) NOT NULL,
    [DecidedByUserId] uniqueidentifier NULL,
    [DecidedAtUtc] datetimeoffset(3) NULL,
    [DecisionNote] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DealerCommissionStatements] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DealerCommissionStatements_DealerCommissionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [channel].[DealerCommissionPlans] ([Id]),
    CONSTRAINT [FK_DealerCommissionStatements_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]),
    CONSTRAINT [FK_DealerCommissionStatements_Users_CalculatedByUserId] FOREIGN KEY ([CalculatedByUserId]) REFERENCES [iam].[Users] ([Id]),
    CONSTRAINT [FK_DealerCommissionStatements_Users_DecidedByUserId] FOREIGN KEY ([DecidedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_DealerCommissionPlans_CompanyId] ON [channel].[DealerCommissionPlans] ([CompanyId]);
CREATE INDEX [IX_DealerCommissionPlans_UpdatedByUserId] ON [channel].[DealerCommissionPlans] ([UpdatedByUserId]);
CREATE INDEX [IX_DealerCommissionStatements_CalculatedByUserId] ON [channel].[DealerCommissionStatements] ([CalculatedByUserId]);
CREATE INDEX [IX_DealerCommissionStatements_CompanyId_PeriodFromUtc_Status] ON [channel].[DealerCommissionStatements] ([CompanyId], [PeriodFromUtc], [Status]);
CREATE INDEX [IX_DealerCommissionStatements_DealerId_PeriodFromUtc] ON [channel].[DealerCommissionStatements] ([DealerId], [PeriodFromUtc]);
CREATE INDEX [IX_DealerCommissionStatements_DecidedByUserId] ON [channel].[DealerCommissionStatements] ([DecidedByUserId]);
CREATE INDEX [IX_DealerCommissionStatements_PlanId] ON [channel].[DealerCommissionStatements] ([PlanId]);
CREATE INDEX [IX_DealerEvaluations_CompanyId_PeriodFromUtc_Rank] ON [channel].[DealerEvaluations] ([CompanyId], [PeriodFromUtc], [Rank]);
CREATE INDEX [IX_DealerEvaluations_DealerId_PeriodFromUtc] ON [channel].[DealerEvaluations] ([DealerId], [PeriodFromUtc]);
CREATE INDEX [IX_DealerEvaluations_EvaluatedByUserId] ON [channel].[DealerEvaluations] ([EvaluatedByUserId]);
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fd1e2e8d-add7-5863-b5f0-650d7bae5e0e',N'SalesManager',N'Dealer.Commission.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Dealer.Commission.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'b0173f37-b8e5-5ca5-bc05-de5c8fca6406',N'SalesManager',N'Dealer.Evaluation.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Dealer.Evaluation.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '31e51dbb-792e-5606-aba0-655aea1e402b',N'FinanceManager',N'Dealer.Commission.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Commission.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fb1c0ae9-058e-5b64-8c14-281408b55124',N'FinanceManager',N'Dealer.Commission.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Commission.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '5de6cec8-c26c-532a-99a8-533f4447212d',N'FinanceManager',N'Dealer.Evaluation.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Dealer.Evaluation.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7db789c9-3e0e-5ae9-8fa2-4e82256bc3bb',N'ChannelManager',N'Dealer.Commission.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Commission.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7492d06f-7d68-5555-a639-e90ffa70d789',N'ChannelManager',N'Dealer.Commission.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Commission.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '939abab9-0646-53f9-934b-e4206f465739',N'ChannelManager',N'Dealer.Evaluation.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Evaluation.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '43680e83-7e35-5f7f-8e22-4cc1a75351ce',N'ChannelManager',N'Dealer.Evaluation.Run',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Dealer.Evaluation.Run');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '53627b02-a936-5e2d-8e22-7ccae685a610',N'Executive',N'Dealer.Commission.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Dealer.Commission.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'af6560af-47eb-5b66-b101-e2996f22f07f',N'Executive',N'Dealer.Evaluation.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Dealer.Evaluation.Read');
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030013_DealerIncentives',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
