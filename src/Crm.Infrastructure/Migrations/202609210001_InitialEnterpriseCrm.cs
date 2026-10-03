using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609210001_InitialEnterpriseCrm")]
public sealed class InitialEnterpriseCrm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
IF SCHEMA_ID(N'org') IS NULL EXEC(N'CREATE SCHEMA [org]');
IF SCHEMA_ID(N'crm') IS NULL EXEC(N'CREATE SCHEMA [crm]');
IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA [sales]');
IF SCHEMA_ID(N'commercial') IS NULL EXEC(N'CREATE SCHEMA [commercial]');
IF SCHEMA_ID(N'iam') IS NULL EXEC(N'CREATE SCHEMA [iam]');

CREATE TABLE [org].[Companies] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [TimeZoneId] nvarchar(80) NOT NULL,
    [Status] nvarchar(24) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Companies_CompanyId] ON [org].[Companies] ([CompanyId]);
CREATE UNIQUE INDEX [IX_Companies_Code] ON [org].[Companies] ([Code]);

CREATE TABLE [org].[OrganizationUnits] (
    [Id] uniqueidentifier NOT NULL,
    [UnitId] nvarchar(32) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Type] nvarchar(24) NOT NULL,
    [ParentUnitId] nvarchar(32) NULL,
    [Status] nvarchar(24) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_OrganizationUnits] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_OrganizationUnits_CompanyId_UnitId] ON [org].[OrganizationUnits] ([CompanyId], [UnitId]);
CREATE UNIQUE INDEX [IX_OrganizationUnits_CompanyId_Code] ON [org].[OrganizationUnits] ([CompanyId], [Code]);
CREATE INDEX [IX_OrganizationUnits_CompanyId_ParentUnitId] ON [org].[OrganizationUnits] ([CompanyId], [ParentUnitId]);

CREATE TABLE [org].[Territories] (
    [Id] uniqueidentifier NOT NULL,
    [TerritoryId] nvarchar(32) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Dimension] nvarchar(24) NOT NULL,
    [ValidFromUtc] datetimeoffset(3) NOT NULL,
    [ValidToUtc] datetimeoffset(3) NULL,
    [Status] nvarchar(24) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Territories] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Territories_CompanyId_TerritoryId] ON [org].[Territories] ([CompanyId], [TerritoryId]);
CREATE UNIQUE INDEX [IX_Territories_CompanyId_Code] ON [org].[Territories] ([CompanyId], [Code]);
CREATE INDEX [IX_Territories_CompanyId_Status_ValidFromUtc] ON [org].[Territories] ([CompanyId], [Status], [ValidFromUtc]);

CREATE TABLE [org].[OrganizationChanges] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [EntityType] nvarchar(64) NOT NULL,
    [EntityBusinessId] nvarchar(64) NOT NULL,
    [Action] nvarchar(64) NOT NULL,
    [Summary] nvarchar(500) NOT NULL,
    [ActorUserId] uniqueidentifier NOT NULL,
    [OccurredAtUtc] datetimeoffset(3) NOT NULL,
    [CorrelationId] nvarchar(100) NOT NULL,
    [BeforeValue] nvarchar(max) NOT NULL,
    [AfterValue] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_OrganizationChanges] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_OrganizationChanges_CompanyId_OccurredAtUtc] ON [org].[OrganizationChanges] ([CompanyId], [OccurredAtUtc]);

CREATE TABLE [crm].[Customers] (
    [Id] uniqueidentifier NOT NULL, [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL,
    [City] nvarchar(100) NOT NULL, [Owner] nvarchar(150) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [Branch] nvarchar(150) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [Segment] nvarchar(80) NOT NULL, [Status] nvarchar(24) NOT NULL,
    [CreditLimit] decimal(18,2) NOT NULL, [Balance] decimal(18,2) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Customers_CompanyId_Code] ON [crm].[Customers] ([CompanyId], [Code]);
CREATE INDEX [IX_Customers_CompanyId_BranchId_TerritoryId] ON [crm].[Customers] ([CompanyId], [BranchId], [TerritoryId]);

CREATE TABLE [crm].[WorkItems] (
    [Id] uniqueidentifier NOT NULL, [Title] nvarchar(250) NOT NULL, [Priority] nvarchar(24) NOT NULL,
    [DueAtUtc] datetimeoffset(3) NOT NULL, [AssignedToUserId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
    [IsDone] bit NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_WorkItems] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_WorkItems_CompanyId_AssignedToUserId_IsDone] ON [crm].[WorkItems] ([CompanyId], [AssignedToUserId], [IsDone]);

CREATE TABLE [sales].[Leads] (
    [Id] uniqueidentifier NOT NULL, [Code] nvarchar(32) NOT NULL, [Name] nvarchar(200) NOT NULL,
    [Contact] nvarchar(200) NOT NULL, [Source] nvarchar(80) NOT NULL, [Owner] nvarchar(150) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
    [Score] int NOT NULL, [Status] nvarchar(24) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Leads] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Leads_CompanyId_Code] ON [sales].[Leads] ([CompanyId], [Code]);
CREATE INDEX [IX_Leads_CompanyId_BranchId_TerritoryId] ON [sales].[Leads] ([CompanyId], [BranchId], [TerritoryId]);

CREATE TABLE [sales].[Opportunities] (
    [Id] uniqueidentifier NOT NULL, [Code] nvarchar(32) NOT NULL, [Title] nvarchar(250) NOT NULL,
    [Customer] nvarchar(200) NOT NULL, [Value] decimal(18,2) NOT NULL, [Owner] nvarchar(150) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
    [Stage] nvarchar(24) NOT NULL, [Probability] int NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Opportunities] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Opportunities_CompanyId_Code] ON [sales].[Opportunities] ([CompanyId], [Code]);
CREATE INDEX [IX_Opportunities_CompanyId_BranchId_TerritoryId] ON [sales].[Opportunities] ([CompanyId], [BranchId], [TerritoryId]);

CREATE TABLE [commercial].[Quotes] (
    [Id] uniqueidentifier NOT NULL, [Code] nvarchar(32) NOT NULL, [Customer] nvarchar(200) NOT NULL,
    [Opportunity] nvarchar(250) NOT NULL, [Amount] decimal(18,2) NOT NULL,
    [DiscountPercent] decimal(5,2) NOT NULL, [MarginPercent] decimal(5,2) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
    [Status] nvarchar(24) NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Quotes] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Quotes_CompanyId_Code] ON [commercial].[Quotes] ([CompanyId], [Code]);
CREATE INDEX [IX_Quotes_CompanyId_BranchId_TerritoryId] ON [commercial].[Quotes] ([CompanyId], [BranchId], [TerritoryId]);

CREATE TABLE [iam].[Users] (
    [Id] uniqueidentifier NOT NULL, [DisplayName] nvarchar(200) NOT NULL, [UserName] nvarchar(100) NOT NULL,
    [NormalizedEmail] nvarchar(256) NOT NULL, [EmployeeNumber] nvarchar(64) NULL,
    [Culture] nvarchar(20) NOT NULL, [TimeZoneId] nvarchar(80) NOT NULL, [Status] nvarchar(24) NOT NULL,
    [AccessValidFromUtc] datetimeoffset(3) NOT NULL, [AccessValidToUtc] datetimeoffset(3) NULL,
    [SecurityVersion] bigint NOT NULL, [LastLoginAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_Users_UserName] ON [iam].[Users] ([UserName]);
CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [iam].[Users] ([NormalizedEmail]);

CREATE TABLE [iam].[ExternalIdentities] (
    [Id] uniqueidentifier NOT NULL, [CrmUserId] uniqueidentifier NOT NULL, [ProviderKey] nvarchar(80) NOT NULL,
    [Issuer] nvarchar(300) NOT NULL, [Subject] nvarchar(300) NOT NULL, [EmailAtLink] nvarchar(256) NULL,
    [LinkedAtUtc] datetimeoffset(3) NOT NULL, [LastSeenAtUtc] datetimeoffset(3) NOT NULL, [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_ExternalIdentities] PRIMARY KEY ([Id])
);
CREATE UNIQUE INDEX [IX_ExternalIdentities_Issuer_Subject] ON [iam].[ExternalIdentities] ([Issuer], [Subject]);
CREATE INDEX [IX_ExternalIdentities_CrmUserId] ON [iam].[ExternalIdentities] ([CrmUserId]);

CREATE TABLE [iam].[UserSessions] (
    [Id] uniqueidentifier NOT NULL, [CrmUserId] uniqueidentifier NOT NULL,
    [IssuedAtUtc] datetimeoffset(3) NOT NULL, [LastSeenAtUtc] datetimeoffset(3) NOT NULL,
    [IdleExpiresAtUtc] datetimeoffset(3) NOT NULL, [AbsoluteExpiresAtUtc] datetimeoffset(3) NOT NULL,
    [RevokedAtUtc] datetimeoffset(3) NULL, [RevokeReason] nvarchar(300) NULL, [SecurityVersionAtIssue] bigint NOT NULL,
    [IpHash] nvarchar(128) NOT NULL, [UserAgentSummary] nvarchar(300) NOT NULL,
    [SelectedCompanyId] nvarchar(32) NULL, [SelectedBranchId] nvarchar(32) NULL, [SelectedTerritoryId] nvarchar(32) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_UserSessions_CrmUserId_RevokedAtUtc_AbsoluteExpiresAtUtc] ON [iam].[UserSessions] ([CrmUserId], [RevokedAtUtc], [AbsoluteExpiresAtUtc]);

CREATE TABLE [iam].[UserRoleAssignments] (
    [Id] uniqueidentifier NOT NULL, [CrmUserId] uniqueidentifier NOT NULL,
    [RoleKey] nvarchar(80) NOT NULL, [RoleLabel] nvarchar(150) NOT NULL, [CompanyId] nvarchar(32) NOT NULL,
    [ScopeType] nvarchar(32) NOT NULL, [ScopeId] nvarchar(64) NOT NULL, [ScopeLabel] nvarchar(200) NOT NULL,
    [ValidFromUtc] datetimeoffset(3) NOT NULL, [ValidToUtc] datetimeoffset(3) NULL,
    [AssignedByUserId] uniqueidentifier NOT NULL, [Reason] nvarchar(500) NOT NULL,
    [Status] nvarchar(24) NOT NULL, [RevokedAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_UserRoleAssignments] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_UserRoleAssignments_CrmUserId_CompanyId_Status] ON [iam].[UserRoleAssignments] ([CrmUserId], [CompanyId], [Status]);
CREATE INDEX [IX_UserRoleAssignments_CompanyId_ScopeType_ScopeId] ON [iam].[UserRoleAssignments] ([CompanyId], [ScopeType], [ScopeId]);

CREATE TABLE [iam].[SecurityAuditEvents] (
    [Id] uniqueidentifier NOT NULL, [OccurredAtUtc] datetimeoffset(3) NOT NULL,
    [EventType] nvarchar(100) NOT NULL, [Outcome] nvarchar(32) NOT NULL,
    [ActorUserId] uniqueidentifier NULL, [TargetUserId] uniqueidentifier NULL, [SessionId] uniqueidentifier NULL,
    [CorrelationId] nvarchar(100) NOT NULL, [Reason] nvarchar(1000) NOT NULL,
    [IpHash] nvarchar(128) NOT NULL, [UserAgentSummary] nvarchar(300) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_SecurityAuditEvents] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_SecurityAuditEvents_TargetUserId_OccurredAtUtc] ON [iam].[SecurityAuditEvents] ([TargetUserId], [OccurredAtUtc]);
CREATE INDEX [IX_SecurityAuditEvents_ActorUserId_OccurredAtUtc] ON [iam].[SecurityAuditEvents] ([ActorUserId], [OccurredAtUtc]);
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DROP TABLE IF EXISTS [iam].[SecurityAuditEvents];
DROP TABLE IF EXISTS [iam].[UserRoleAssignments];
DROP TABLE IF EXISTS [iam].[UserSessions];
DROP TABLE IF EXISTS [iam].[ExternalIdentities];
DROP TABLE IF EXISTS [iam].[Users];
DROP TABLE IF EXISTS [commercial].[Quotes];
DROP TABLE IF EXISTS [sales].[Opportunities];
DROP TABLE IF EXISTS [sales].[Leads];
DROP TABLE IF EXISTS [crm].[WorkItems];
DROP TABLE IF EXISTS [crm].[Customers];
DROP TABLE IF EXISTS [org].[OrganizationChanges];
DROP TABLE IF EXISTS [org].[Territories];
DROP TABLE IF EXISTS [org].[OrganizationUnits];
DROP TABLE IF EXISTS [org].[Companies];
""");
}
