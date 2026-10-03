using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609210003_CustomerRelationshipsAndMerge")]
public sealed class CustomerRelationshipsAndMerge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
ALTER TABLE [sales].[Leads] ADD [CustomerId] uniqueidentifier NULL;
ALTER TABLE [sales].[Opportunities] ADD [CustomerId] uniqueidentifier NULL;
ALTER TABLE [commercial].[Quotes] ADD [CustomerId] uniqueidentifier NULL, [OpportunityId] uniqueidentifier NULL;

-- Compile only after the preceding ALTER TABLE has added these columns.
EXEC sys.sp_executesql N'UPDATE o SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
    WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer] AND c.[BranchId] = o.[BranchId])
FROM [sales].[Opportunities] o
WHERE o.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
    WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer] AND c.[BranchId] = o.[BranchId]);
UPDATE o SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
    WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer])
FROM [sales].[Opportunities] o
WHERE o.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
    WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer]);

UPDATE q SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
    WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer] AND c.[BranchId] = q.[BranchId])
FROM [commercial].[Quotes] q
WHERE q.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
    WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer] AND c.[BranchId] = q.[BranchId]);
UPDATE q SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
    WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer])
FROM [commercial].[Quotes] q
WHERE q.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
    WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer]);

UPDATE q SET [OpportunityId] = (SELECT o.[Id] FROM [sales].[Opportunities] o
    WHERE o.[CompanyId] = q.[CompanyId] AND o.[Title] = q.[Opportunity] AND o.[CustomerId] = q.[CustomerId])
FROM [commercial].[Quotes] q
WHERE q.[OpportunityId] IS NULL AND 1 = (SELECT COUNT(*) FROM [sales].[Opportunities] o
    WHERE o.[CompanyId] = q.[CompanyId] AND o.[Title] = q.[Opportunity] AND o.[CustomerId] = q.[CustomerId]);

IF EXISTS (SELECT 1 FROM [sales].[Opportunities] WHERE [CustomerId] IS NULL)
    THROW 51001, ''CustomerId backfill failed for one or more opportunities. Resolve legacy customer names before migration.'', 1;
IF EXISTS (SELECT 1 FROM [commercial].[Quotes] WHERE [CustomerId] IS NULL)
    THROW 51002, ''CustomerId backfill failed for one or more quotes. Resolve legacy customer names before migration.'', 1;

ALTER TABLE [sales].[Opportunities] ALTER COLUMN [CustomerId] uniqueidentifier NOT NULL;
ALTER TABLE [commercial].[Quotes] ALTER COLUMN [CustomerId] uniqueidentifier NOT NULL;
CREATE INDEX [IX_Leads_CompanyId_CustomerId] ON [sales].[Leads] ([CompanyId], [CustomerId]);
CREATE INDEX [IX_Opportunities_CompanyId_CustomerId] ON [sales].[Opportunities] ([CompanyId], [CustomerId]);
CREATE INDEX [IX_Quotes_CompanyId_CustomerId] ON [commercial].[Quotes] ([CompanyId], [CustomerId]);
CREATE INDEX [IX_Quotes_OpportunityId] ON [commercial].[Quotes] ([OpportunityId]);
ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities] ([Id]);

CREATE TABLE [crm].[CustomerMergeOperations] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [DuplicateCandidateId] uniqueidentifier NOT NULL,
    [SurvivorCustomerId] uniqueidentifier NOT NULL, [MergedCustomerId] uniqueidentifier NOT NULL,
    [MergedCustomerPreviousStatus] nvarchar(24) NOT NULL, [TransferManifestJson] nvarchar(max) NOT NULL,
    [Reason] nvarchar(1000) NOT NULL, [MergedByUserId] uniqueidentifier NOT NULL, [MergedAtUtc] datetimeoffset(3) NOT NULL,
    [Status] nvarchar(24) NOT NULL, [RevertedByUserId] uniqueidentifier NULL, [RevertedAtUtc] datetimeoffset(3) NULL,
    [RevertReason] nvarchar(1000) NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerMergeOperations] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerMergeOperations_CompanyId_MergedAtUtc] ON [crm].[CustomerMergeOperations] ([CompanyId], [MergedAtUtc]);
CREATE INDEX [IX_CustomerMergeOperations_MergedCustomerId_Status] ON [crm].[CustomerMergeOperations] ([MergedCustomerId], [Status]);
CREATE UNIQUE INDEX [UX_CustomerMergeOperations_DuplicateCandidateId_Active] ON [crm].[CustomerMergeOperations] ([DuplicateCandidateId]) WHERE [Status] = N''Merged'';
CREATE UNIQUE INDEX [UX_CustomerMergeOperations_MergedCustomerId_Active] ON [crm].[CustomerMergeOperations] ([MergedCustomerId]) WHERE [Status] = N''Merged'';';
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DROP TABLE IF EXISTS [crm].[CustomerMergeOperations];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Opportunities_OpportunityId];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Customers_CustomerId];
ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT IF EXISTS [FK_Opportunities_Customers_CustomerId];
ALTER TABLE [sales].[Leads] DROP CONSTRAINT IF EXISTS [FK_Leads_Customers_CustomerId];
DROP INDEX IF EXISTS [IX_Quotes_OpportunityId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Quotes_CompanyId_CustomerId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Opportunities_CompanyId_CustomerId] ON [sales].[Opportunities];
DROP INDEX IF EXISTS [IX_Leads_CompanyId_CustomerId] ON [sales].[Leads];
ALTER TABLE [commercial].[Quotes] DROP COLUMN [OpportunityId], [CustomerId];
ALTER TABLE [sales].[Opportunities] DROP COLUMN [CustomerId];
ALTER TABLE [sales].[Leads] DROP COLUMN [CustomerId];
""");
}
