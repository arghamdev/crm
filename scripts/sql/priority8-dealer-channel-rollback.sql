-- Generated from 202609270007_DealerChannelGovernance.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]>N'202609270007_DealerChannelGovernance') THROW 51203, 'Roll back newer migrations first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609270007_DealerChannelGovernance')
BEGIN
DROP TABLE IF EXISTS [channel].[DealerStatusHistory];
DROP TABLE IF EXISTS [channel].[DealerPerformanceSnapshots];
DROP TABLE IF EXISTS [channel].[DealerFinancialSnapshots];
DROP TABLE IF EXISTS [channel].[DealerTargets];
DROP TABLE IF EXISTS [channel].[DealerCustomerAssignments];
DROP TABLE IF EXISTS [channel].[DealerTerritoryAssignments];
DROP TABLE IF EXISTS [channel].[DealerContracts];
DROP TABLE IF EXISTS [channel].[Dealers];
DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609270007_DealerChannelGovernance';
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
