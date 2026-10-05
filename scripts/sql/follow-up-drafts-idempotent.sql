-- Generated from 202610050019_FollowUpDrafts.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610040018_FollowUpCenter') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610050019_FollowUpDrafts')
BEGIN
CREATE TABLE [followup].[Drafts] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Kind] nvarchar(32) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [SavedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Drafts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Drafts_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_Drafts_UserId_CompanyId_Kind] ON [followup].[Drafts] ([UserId], [CompanyId], [Kind]);
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610050019_FollowUpDrafts',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
