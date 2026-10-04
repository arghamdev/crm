-- Generated from 202610030017_AccountFile.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]>N'202610030017_AccountFile') THROW 51203, 'Roll back newer migrations first.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030017_AccountFile')
BEGIN
DELETE FROM [iam].[RolePermissionGrants] WHERE [Permission] LIKE N'Activity.%' OR [Permission] LIKE N'Note.%' OR [Permission] LIKE N'Document.%' OR [Permission] LIKE N'Payment.%' OR [Permission] LIKE N'BankAccount.%' OR [Permission] LIKE N'Account.%' OR [Permission] LIKE N'Contract.%' OR [Permission] LIKE N'Project.%' OR [Permission] LIKE N'Campaign.%' OR [Permission] LIKE N'Survey.%' OR [Permission] = N'Customer.Status.Change';
DROP TABLE [account].[DocumentLinks];
DROP TABLE [account].[DocumentContents];
DROP TABLE [account].[Documents];
DROP TABLE [account].[ActivityParticipants];
DROP TABLE [account].[Activities];
DROP TABLE [account].[Notes];
DROP TABLE [account].[SurveyResponses];
DROP TABLE [marketing].[CampaignMembers];
DROP TABLE [marketing].[TargetListMembers];
DROP TABLE [marketing].[Campaigns];
DROP TABLE [marketing].[TargetLists];
DROP TABLE [marketing].[Surveys];
DROP TABLE [account].[ClientOperations];
DROP TABLE [account].[Payments];
DROP TABLE [account].[BankAccounts];
DROP TABLE [account].[Contracts];
DROP TABLE [account].[Projects];
DROP TABLE [account].[Participations];
DROP TABLE [account].[Allocations];
ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT [FK_DealerGuarantees_Customers_CustomerId];
DROP INDEX [IX_DealerGuarantees_CustomerId_Status] ON [channel].[DealerGuarantees];
DELETE FROM [channel].[DealerGuarantees] WHERE [DealerId] IS NULL;
ALTER TABLE [channel].[DealerGuarantees] DROP COLUMN [CustomerId];
DROP INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees];
ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId];
ALTER TABLE [channel].[DealerGuarantees] ALTER COLUMN [DealerId] uniqueidentifier NOT NULL;
ALTER TABLE [channel].[DealerGuarantees] ADD CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]);
CREATE INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees] ([DealerId], [Status]);
ALTER TABLE [crm].[Customers] DROP CONSTRAINT [FK_Customers_Customers_ParentCustomerId];
DROP INDEX [IX_Customers_ParentCustomerId] ON [crm].[Customers];
ALTER TABLE [crm].[Customers] DROP COLUMN [ParentCustomerId];
DECLARE @dc nvarchar(max);
SELECT @dc = QUOTENAME([d].[name]) FROM [sys].[default_constraints] [d] INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id] WHERE [d].[parent_object_id] = OBJECT_ID(N'[crm].[Customers]') AND [c].[name] = N'RelationshipType';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [crm].[Customers] DROP CONSTRAINT ' + @dc + ';');
ALTER TABLE [crm].[Customers] DROP COLUMN [RelationshipType], [Tags];
DROP INDEX [IX_Opportunities_ContactId] ON [sales].[Opportunities];
SET @dc = NULL;
SELECT @dc = QUOTENAME([d].[name]) FROM [sys].[default_constraints] [d] INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id] WHERE [d].[parent_object_id] = OBJECT_ID(N'[sales].[Opportunities]') AND [c].[name] = N'CurrencyCode';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT ' + @dc + ';');
ALTER TABLE [sales].[Opportunities] DROP COLUMN [ContactId], [CurrencyCode];
IF SCHEMA_ID(N'account') IS NOT NULL EXEC(N'DROP SCHEMA [account];');
IF SCHEMA_ID(N'marketing') IS NOT NULL EXEC(N'DROP SCHEMA [marketing];');
DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030017_AccountFile';
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
