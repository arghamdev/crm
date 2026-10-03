-- ابتدا پشتیبان بگیرید و سپس مقدار زیر را آگاهانه به 1 تغییر دهید.
DECLARE @ConfirmDestructiveRollback bit = 0;
IF @ConfirmDestructiveRollback <> 1
    THROW 51010, 'Rollback is locked. Set @ConfirmDestructiveRollback = 1 after backup and impact review.', 1;
IF OBJECT_ID('crm.CustomerMergeOperations', 'U') IS NOT NULL AND EXISTS (SELECT 1 FROM [crm].[CustomerMergeOperations] WHERE [Status] = N'Merged')
    THROW 51011, 'Rollback blocked: active merge operations exist. Unmerge them through the application first.', 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NOT NULL
    DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'202609210003_CustomerRelationshipsAndMerge';
DROP TABLE IF EXISTS [crm].[CustomerMergeOperations];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Opportunities_OpportunityId];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Customers_CustomerId];
ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT IF EXISTS [FK_Opportunities_Customers_CustomerId];
ALTER TABLE [sales].[Leads] DROP CONSTRAINT IF EXISTS [FK_Leads_Customers_CustomerId];
DROP INDEX IF EXISTS [IX_Quotes_OpportunityId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Quotes_CompanyId_CustomerId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Opportunities_CompanyId_CustomerId] ON [sales].[Opportunities];
DROP INDEX IF EXISTS [IX_Leads_CompanyId_CustomerId] ON [sales].[Leads];
IF COL_LENGTH('commercial.Quotes', 'OpportunityId') IS NOT NULL ALTER TABLE [commercial].[Quotes] DROP COLUMN [OpportunityId];
IF COL_LENGTH('commercial.Quotes', 'CustomerId') IS NOT NULL ALTER TABLE [commercial].[Quotes] DROP COLUMN [CustomerId];
IF COL_LENGTH('sales.Opportunities', 'CustomerId') IS NOT NULL ALTER TABLE [sales].[Opportunities] DROP COLUMN [CustomerId];
IF COL_LENGTH('sales.Leads', 'CustomerId') IS NOT NULL ALTER TABLE [sales].[Leads] DROP COLUMN [CustomerId];
COMMIT TRANSACTION;
