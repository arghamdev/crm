-- Generated from 202610030016_Notifications.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030015_DealerAssurance') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030016_Notifications')
BEGIN
IF SCHEMA_ID(N'notify') IS NULL EXEC(N'CREATE SCHEMA [notify];');
CREATE TABLE [notify].[NotificationMessages] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [RecipientUserId] uniqueidentifier NOT NULL,
    [Channel] nvarchar(8) NOT NULL,
    [Address] nvarchar(256) NOT NULL,
    [Category] nvarchar(32) NOT NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Body] nvarchar(1000) NOT NULL,
    [SourceReference] nvarchar(120) NULL,
    [DedupKey] nvarchar(160) NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [AttemptCount] int NOT NULL,
    [NextAttemptAtUtc] datetimeoffset(3) NULL,
    [LastError] nvarchar(500) NULL,
    [ProviderMessageId] nvarchar(120) NULL,
    [SentAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_NotificationMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationMessages_Users_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [notify].[NotificationPreferences] (
    [Id] uniqueidentifier NOT NULL,
    [Mobile] nvarchar(20) NULL,
    [EmailEnabled] bit NOT NULL,
    [SmsEnabled] bit NOT NULL,
    [MutedCategories] nvarchar(200) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_NotificationPreferences] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationPreferences_Users_Id] FOREIGN KEY ([Id]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_NotificationMessages_DedupKey_RecipientUserId_Channel] ON [notify].[NotificationMessages] ([DedupKey], [RecipientUserId], [Channel]);
CREATE INDEX [IX_NotificationMessages_RecipientUserId_CreatedAtUtc] ON [notify].[NotificationMessages] ([RecipientUserId], [CreatedAtUtc]);
CREATE INDEX [IX_NotificationMessages_Status_NextAttemptAtUtc] ON [notify].[NotificationMessages] ([Status], [NextAttemptAtUtc]);
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030016_Notifications',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
