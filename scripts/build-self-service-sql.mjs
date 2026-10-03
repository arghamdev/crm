import {readFileSync,writeFileSync} from 'node:fs';
const root=new URL('../',import.meta.url);
for (const {id,baseline,prefix} of [
 {id:'202609270007_DealerChannelGovernance',baseline:'202609260006_OrderIntegrationVisibility',prefix:'priority8-dealer-channel'},
 {id:'202609270008_PortalMobileSelfService',baseline:'202609270007_DealerChannelGovernance',prefix:'priority10-portal-mobile'},
 {id:'202610030009_ServiceDeskSla',baseline:'202609270008_PortalMobileSelfService',prefix:'phase8-service-desk'}
]) {
const source=readFileSync(new URL('src/Crm.Infrastructure/Migrations/'+id+'.cs',root),'utf8');
function body(direction){const match=source.match(new RegExp('protected override void '+direction+'\\(MigrationBuilder \\w+\\) => \\w+\\.Sql\\("""([\\s\\S]*?)"""\\);'));if(!match)throw new Error('Migration raw SQL boundary changed: '+id);return match[1].trim();}
function wrap(direction){const up=direction==='Up';return `-- Generated from ${id}.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
${up?"IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'"+baseline+"') THROW 51202, 'Required baseline migration is missing.', 1;":"IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]>N'"+id+"') THROW 51203, 'Roll back newer migrations first.', 1;"}
IF ${up?'NOT ':''}EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'${id}')
BEGIN
${body(direction)}
${up?"INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'"+id+"',N'10.0.12');":"DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'"+id+"';"}
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
`;}
writeFileSync(new URL('scripts/sql/'+prefix+'-idempotent.sql',root),wrap('Up'));
writeFileSync(new URL('scripts/sql/'+prefix+'-rollback.sql',root),wrap('Down'));
console.log(prefix+' SQL scripts generated with transaction, lock and history guards.');
}
