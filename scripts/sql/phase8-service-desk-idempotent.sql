-- Generated from 202610030009_ServiceDeskSla.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609270008_PortalMobileSelfService') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030009_ServiceDeskSla')
BEGIN
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name=N'service') EXEC(N'CREATE SCHEMA [service]');
CREATE TABLE [service].[ServiceCases] (
 [Id] uniqueidentifier NOT NULL PRIMARY KEY, [Code] nvarchar(32) NOT NULL, [Subject] nvarchar(200) NOT NULL, [Description] nvarchar(4000) NOT NULL,
 [CustomerId] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
 [Category] nvarchar(24) NOT NULL, [Channel] nvarchar(24) NOT NULL, [Priority] nvarchar(24) NOT NULL, [Status] nvarchar(24) NOT NULL,
 [CreatedByUserId] uniqueidentifier NOT NULL, [OwnerUserId] uniqueidentifier NULL, [Owner] nvarchar(200) NOT NULL,
 [OpenedAtUtc] datetimeoffset(3) NOT NULL, [FirstResponseDueAtUtc] datetimeoffset(3) NOT NULL, [ResolutionDueAtUtc] datetimeoffset(3) NOT NULL,
 [FirstRespondedAtUtc] datetimeoffset(3) NULL, [PausedAtUtc] datetimeoffset(3) NULL, [PausedMinutes] bigint NOT NULL,
 [ResolvedAtUtc] datetimeoffset(3) NULL, [ClosedAtUtc] datetimeoffset(3) NULL, [EscalationLevel] int NOT NULL, [LastEscalatedAtUtc] datetimeoffset(3) NULL,
 [RootCause] nvarchar(1000) NULL, [CorrectiveAction] nvarchar(1000) NULL, [Resolution] nvarchar(2000) NULL, [ReopenCount] int NOT NULL,
 [SatisfactionScore] int NULL, [SatisfactionComment] nvarchar(1000) NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
 CONSTRAINT [FK_ServiceCases_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers]([Id]),
 CONSTRAINT [FK_ServiceCases_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [iam].[Users]([Id]),
 CONSTRAINT [FK_ServiceCases_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users]([Id])
);
CREATE UNIQUE INDEX [IX_ServiceCases_CompanyId_Code] ON [service].[ServiceCases]([CompanyId],[Code]);
CREATE INDEX [IX_ServiceCases_CompanyId_Status_ResolutionDueAtUtc] ON [service].[ServiceCases]([CompanyId],[Status],[ResolutionDueAtUtc]);
CREATE INDEX [IX_ServiceCases_CompanyId_OwnerUserId_Status] ON [service].[ServiceCases]([CompanyId],[OwnerUserId],[Status]);
CREATE INDEX [IX_ServiceCases_CompanyId_CustomerId] ON [service].[ServiceCases]([CompanyId],[CustomerId]);
CREATE INDEX [IX_ServiceCases_CustomerId] ON [service].[ServiceCases]([CustomerId]);
CREATE INDEX [IX_ServiceCases_CreatedByUserId] ON [service].[ServiceCases]([CreatedByUserId]);
CREATE INDEX [IX_ServiceCases_OwnerUserId] ON [service].[ServiceCases]([OwnerUserId]);
CREATE TABLE [service].[ServiceCaseHistory] (
 [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CaseId] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
 [TerritoryId] nvarchar(32) NULL, [FromStatus] nvarchar(24) NULL, [ToStatus] nvarchar(24) NOT NULL, [Action] nvarchar(80) NOT NULL,
 [Note] nvarchar(1000) NOT NULL, [ActorUserId] uniqueidentifier NULL, [OccurredAtUtc] datetimeoffset(3) NOT NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
 CONSTRAINT [FK_ServiceCaseHistory_ServiceCases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [service].[ServiceCases]([Id]),
 CONSTRAINT [FK_ServiceCaseHistory_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [iam].[Users]([Id])
);
CREATE INDEX [IX_ServiceCaseHistory_CaseId_OccurredAtUtc] ON [service].[ServiceCaseHistory]([CaseId],[OccurredAtUtc]);
CREATE INDEX [IX_ServiceCaseHistory_ActorUserId] ON [service].[ServiceCaseHistory]([ActorUserId]);
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030009_ServiceDeskSla',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
