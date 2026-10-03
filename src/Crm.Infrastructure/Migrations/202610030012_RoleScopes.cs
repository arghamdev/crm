using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>Allowed scope types per role and a system flag, so roles (including new custom ones) are assignable from data.</summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610030012_RoleScopes")]
public sealed class RoleScopes : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
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
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
ALTER TABLE [iam].[RoleDefinitions] DROP CONSTRAINT [DF_RoleDefinitions_IsSystem];
ALTER TABLE [iam].[RoleDefinitions] DROP COLUMN [IsSystem];
ALTER TABLE [iam].[RoleDefinitions] DROP CONSTRAINT [DF_RoleDefinitions_AllowedScopeTypes];
ALTER TABLE [iam].[RoleDefinitions] DROP COLUMN [AllowedScopeTypes];
""");
}
