-- Generated from 202610030015_DealerAssurance.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]>N'202610030015_DealerAssurance') THROW 51203, 'Roll back newer migrations first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030015_DealerAssurance')
BEGIN
DELETE FROM [iam].[RolePermissionGrants] WHERE [Permission] IN (N'Dealer.Guarantee.Read', N'Dealer.Guarantee.Manage', N'Dealer.Training.Read', N'Dealer.Training.Manage');
DROP TABLE [channel].[CommissionPayoutMessages];
DROP TABLE [channel].[DealerGuarantees];
DROP TABLE [channel].[DealerTrainings];
ALTER TABLE [channel].[DealerCommissionStatements] DROP COLUMN [SplitDefinition];
DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030015_DealerAssurance';
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
