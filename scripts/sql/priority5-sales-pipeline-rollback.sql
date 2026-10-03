-- ابتدا پشتیبان بگیرید و سپس مقدار زیر را آگاهانه به 1 تغییر دهید.
DECLARE @ConfirmDestructiveRollback bit = 0;
IF @ConfirmDestructiveRollback <> 1
    THROW 51022, 'Rollback is locked. Set @ConfirmDestructiveRollback = 1 after backup and impact review.', 1;

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID('dbo.__EFMigrationsHistory','U') IS NOT NULL
        DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609220004_SalesPipelineGovernance';
    ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT IF EXISTS [FK_Opportunities_Leads_OriginLeadId];
    ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT IF EXISTS [FK_Opportunities_Users_OwnerUserId];
    ALTER TABLE [sales].[Leads] DROP CONSTRAINT IF EXISTS [FK_Leads_Opportunities_ConvertedOpportunityId];
    ALTER TABLE [sales].[Leads] DROP CONSTRAINT IF EXISTS [FK_Leads_Users_OwnerUserId];
    DROP TABLE IF EXISTS [sales].[OpportunityActivities];
    DROP TABLE IF EXISTS [sales].[OpportunityStageHistory];
    DROP TABLE IF EXISTS [sales].[LeadStatusHistory];
    DROP INDEX IF EXISTS [IX_Opportunities_OriginLeadId] ON [sales].[Opportunities];
    DROP INDEX IF EXISTS [IX_Opportunities_OwnerUserId] ON [sales].[Opportunities];
    DROP INDEX IF EXISTS [IX_Opportunities_CompanyId_OwnerUserId_Stage_ExpectedCloseAtUtc] ON [sales].[Opportunities];
    DROP INDEX IF EXISTS [IX_Leads_ConvertedOpportunityId] ON [sales].[Leads];
    DROP INDEX IF EXISTS [IX_Leads_OwnerUserId] ON [sales].[Leads];
    DROP INDEX IF EXISTS [IX_Leads_CompanyId_OwnerUserId_Status_FirstContactDueAtUtc] ON [sales].[Leads];
    UPDATE [sales].[Opportunities] SET [Stage] = CASE WHEN [Stage]=N'SolutionOffer' THEN N'Solution' WHEN [Stage]=N'Commit' THEN N'Quote' ELSE [Stage] END;
    IF COL_LENGTH('sales.Opportunities','OwnerUserId') IS NOT NULL ALTER TABLE [sales].[Opportunities] DROP COLUMN [OwnerUserId],[OriginLeadId],[ExpectedCloseAtUtc],[Source],[NextAction],[NextActionAtUtc],[LastActivityAtUtc],[Competitor],[RiskLevel],[OutcomeReason],[ClosedAtUtc];
    IF COL_LENGTH('sales.Leads','OwnerUserId') IS NOT NULL ALTER TABLE [sales].[Leads] DROP COLUMN [OwnerUserId],[Phone],[Email],[ConvertedOpportunityId],[AssignedAtUtc],[FirstContactDueAtUtc],[FirstContactAtUtc],[LastActivityAtUtc],[NextAction],[NextActionAtUtc],[StatusReason];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
