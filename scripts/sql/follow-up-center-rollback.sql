-- Generated from 202610040018_FollowUpCenter.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]>N'202610040018_FollowUpCenter') THROW 51203, 'Roll back newer migrations first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610040018_FollowUpCenter')
BEGIN
DROP TABLE [followup].[ActivityLinks];
DROP TABLE [followup].[Approvals];
DROP TABLE [followup].[ChecklistItems];
DROP TABLE [followup].[Documents];
DROP TABLE [followup].[Events];
DROP TABLE [followup].[Items];
DROP TABLE [followup].[QueueMembers];
DROP TABLE [followup].[Referrals];
DROP TABLE [followup].[SavedViews];
DROP TABLE [followup].[TemplateStages];
DROP TABLE [followup].[Stages];
DROP TABLE [followup].[Queues];
DROP TABLE [followup].[Cases];
DROP TABLE [followup].[Templates];
DROP TABLE [followup].[SlaPolicies];
DELETE FROM [iam].[RolePermissionGrants] WHERE [Permission] LIKE N'FollowUp.%';
IF SCHEMA_ID(N'followup') IS NOT NULL EXEC(N'DROP SCHEMA [followup];');
DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610040018_FollowUpCenter';
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
