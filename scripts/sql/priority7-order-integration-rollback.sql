SET XACT_ABORT ON;
DECLARE @ConfirmDestructiveRollback bit = 0;
IF @ConfirmDestructiveRollback <> 1
    THROW 51110, 'Rollback deletes all priority-7 order and integration data. Set @ConfirmDestructiveRollback=1 only after backup and approval.', 1;

BEGIN TRANSACTION;
DROP TABLE IF EXISTS [integration].[OrderIntegrationAttempts];
DROP TABLE IF EXISTS [integration].[OrderIntegrationMessages];
DROP TABLE IF EXISTS [commercial].[OrderCreditDecisions];
DROP TABLE IF EXISTS [commercial].[OrderStatusHistory];
DROP TABLE IF EXISTS [commercial].[OrderRequests];
DELETE FROM [dbo].[__EFMigrationsHistory]
WHERE [MigrationId]=N'202609260006_OrderIntegrationVisibility';
COMMIT;
