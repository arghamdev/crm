-- Generated from 202610030012_RoleScopes.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030011_AuditLogIndexes') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030012_RoleScopes')
BEGIN
ALTER TABLE [iam].[RoleDefinitions] ADD [AllowedScopeTypes] nvarchar(64) NOT NULL CONSTRAINT [DF_RoleDefinitions_AllowedScopeTypes] DEFAULT N'Company';
ALTER TABLE [iam].[RoleDefinitions] ADD [IsSystem] bit NOT NULL CONSTRAINT [DF_RoleDefinitions_IsSystem] DEFAULT CAST(0 AS bit);
EXEC(N'
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Company'', [IsSystem] = 1 WHERE [RoleKey] = N''CompanyMember'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Company,Branch'', [IsSystem] = 1 WHERE [RoleKey] = N''SalesManager'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Branch,Territory'', [IsSystem] = 1 WHERE [RoleKey] = N''SalesSupervisor'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Branch,Territory'', [IsSystem] = 1 WHERE [RoleKey] = N''SalesExpert'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Company'', [IsSystem] = 1 WHERE [RoleKey] = N''FinanceManager'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Company'', [IsSystem] = 1 WHERE [RoleKey] = N''ChannelManager'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Company,Branch'', [IsSystem] = 1 WHERE [RoleKey] = N''Executive'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Dealer'', [IsSystem] = 1 WHERE [RoleKey] = N''DealerUser'';
UPDATE [iam].[RoleDefinitions] SET [AllowedScopeTypes] = N''Branch,Territory'', [IsSystem] = 1 WHERE [RoleKey] = N''ServiceAgent'';
');
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030012_RoleScopes',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
