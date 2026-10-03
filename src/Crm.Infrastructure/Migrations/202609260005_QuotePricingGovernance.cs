using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609260005_QuotePricingGovernance")]
public sealed class QuotePricingGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DROP INDEX IF EXISTS [IX_Quotes_CompanyId_Code] ON [commercial].[Quotes];

ALTER TABLE [commercial].[Quotes] ADD
    [OwnerUserId] uniqueidentifier NULL,
    [CurrencyCode] nvarchar(8) NOT NULL CONSTRAINT [DF_Quotes_CurrencyCode] DEFAULT N'IRR',
    [ValidUntilUtc] datetimeoffset(3) NOT NULL CONSTRAINT [DF_Quotes_ValidUntilUtc] DEFAULT DATEADD(day, 30, SYSUTCDATETIME()),
    [PaymentTerms] nvarchar(500) NOT NULL CONSTRAINT [DF_Quotes_PaymentTerms] DEFAULT N'تسویه ۳۰ روزه',
    [Revision] int NOT NULL CONSTRAINT [DF_Quotes_Revision] DEFAULT 1,
    [ParentQuoteId] uniqueidentifier NULL,
    [GrossAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_Quotes_GrossAmount] DEFAULT 0,
    [DiscountAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_Quotes_DiscountAmount] DEFAULT 0,
    [CostAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_Quotes_CostAmount] DEFAULT 0,
    [NetAmount] decimal(18,2) NOT NULL CONSTRAINT [DF_Quotes_NetAmount] DEFAULT 0,
    [ApprovalLevel] nvarchar(32) NOT NULL CONSTRAINT [DF_Quotes_ApprovalLevel] DEFAULT N'None',
    [SubmittedAtUtc] datetimeoffset(3) NULL,
    [ApprovedAtUtc] datetimeoffset(3) NULL,
    [SentAtUtc] datetimeoffset(3) NULL,
    [AcceptedAtUtc] datetimeoffset(3) NULL;

-- Compile only after the preceding ALTER TABLE has added these columns.
EXEC sys.sp_executesql N'UPDATE q SET
    [OwnerUserId] = o.[OwnerUserId],
    [GrossAmount] = q.[Amount],
    [DiscountAmount] = q.[Amount] * q.[DiscountPercent] / 100.0,
    [NetAmount] = q.[Amount] * (1 - q.[DiscountPercent] / 100.0),
    [CostAmount] = q.[Amount] * (1 - q.[DiscountPercent] / 100.0) * (1 - q.[MarginPercent] / 100.0),
    [ApprovalLevel] = CASE
        WHEN q.[DiscountPercent] <= 5 AND q.[MarginPercent] >= 25 THEN N''None''
        WHEN q.[DiscountPercent] <= 8 AND q.[MarginPercent] >= 22 THEN N''SalesSupervisor''
        WHEN q.[DiscountPercent] <= 12 AND q.[MarginPercent] >= 18 THEN N''CommercialManager''
        ELSE N''JointSalesAndFinance'' END,
    [SubmittedAtUtc] = CASE WHEN q.[Status] <> N''Draft'' THEN q.[CreatedAtUtc] ELSE NULL END,
    [ApprovedAtUtc] = CASE WHEN q.[Status] = N''Approved'' THEN q.[UpdatedAtUtc] ELSE NULL END
FROM [commercial].[Quotes] q
LEFT JOIN [sales].[Opportunities] o ON o.[Id] = q.[OpportunityId];

CREATE TABLE [commercial].[QuoteLines] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [QuoteId] uniqueidentifier NOT NULL,
    [ProductCode] nvarchar(64) NOT NULL, [ProductName] nvarchar(250) NOT NULL, [Unit] nvarchar(32) NOT NULL,
    [Quantity] decimal(18,3) NOT NULL, [ListUnitPrice] decimal(18,2) NOT NULL,
    [StandardUnitCost] decimal(18,2) NOT NULL, [DiscountPercent] decimal(5,2) NOT NULL,
    [PriceSource] nvarchar(200) NOT NULL, [PriceEffectiveAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_QuoteLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuoteLines_Quotes_QuoteId] FOREIGN KEY ([QuoteId]) REFERENCES [commercial].[Quotes] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [commercial].[QuoteStatusHistory] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [QuoteId] uniqueidentifier NOT NULL,
    [FromStatus] nvarchar(24) NULL, [ToStatus] nvarchar(24) NOT NULL, [Reason] nvarchar(1000) NOT NULL,
    [ChangedByUserId] uniqueidentifier NOT NULL, [ChangedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_QuoteStatusHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuoteStatusHistory_Quotes_QuoteId] FOREIGN KEY ([QuoteId]) REFERENCES [commercial].[Quotes] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_QuoteStatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE TABLE [commercial].[QuoteApprovalDecisions] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [QuoteId] uniqueidentifier NOT NULL,
    [Role] nvarchar(32) NOT NULL, [Decision] nvarchar(16) NOT NULL, [Comment] nvarchar(1000) NOT NULL,
    [DecidedByUserId] uniqueidentifier NOT NULL, [DecidedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_QuoteApprovalDecisions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QuoteApprovalDecisions_Quotes_QuoteId] FOREIGN KEY ([QuoteId]) REFERENCES [commercial].[Quotes] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_QuoteApprovalDecisions_Users_DecidedByUserId] FOREIGN KEY ([DecidedByUserId]) REFERENCES [iam].[Users] ([Id])
);

INSERT INTO [commercial].[QuoteLines] ([Id],[CompanyId],[BranchId],[TerritoryId],[QuoteId],[ProductCode],[ProductName],[Unit],
    [Quantity],[ListUnitPrice],[StandardUnitCost],[DiscountPercent],[PriceSource],[PriceEffectiveAtUtc],[CreatedAtUtc],[UpdatedAtUtc],[Version])
SELECT NEWID(), q.[CompanyId], q.[BranchId], q.[TerritoryId], q.[Id], N''LEGACY'', N''ردیف انتقال داده قدیمی'', N''مورد'',
    1, q.[GrossAmount], q.[CostAmount], q.[DiscountPercent], N''Legacy Migration Snapshot'', q.[CreatedAtUtc],
    SYSUTCDATETIME(), SYSUTCDATETIME(), 1
FROM [commercial].[Quotes] q WHERE q.[GrossAmount] > 0;

INSERT INTO [commercial].[QuoteStatusHistory] ([Id],[CompanyId],[BranchId],[TerritoryId],[QuoteId],[FromStatus],[ToStatus],
    [Reason],[ChangedByUserId],[ChangedAtUtc],[CreatedAtUtc],[UpdatedAtUtc],[Version])
SELECT NEWID(), q.[CompanyId], q.[BranchId], q.[TerritoryId], q.[Id], NULL, q.[Status], N''انتقال داده اولویت ۶'',
    COALESCE(q.[OwnerUserId], u.[Id]), q.[UpdatedAtUtc], SYSUTCDATETIME(), SYSUTCDATETIME(), 1
FROM [commercial].[Quotes] q
CROSS APPLY (SELECT TOP (1) [Id] FROM [iam].[Users] ORDER BY [Id]) u;

CREATE UNIQUE INDEX [IX_Quotes_CompanyId_Code_Revision] ON [commercial].[Quotes] ([CompanyId],[Code],[Revision]);
CREATE INDEX [IX_Quotes_OwnerUserId] ON [commercial].[Quotes] ([OwnerUserId]);
CREATE INDEX [IX_Quotes_ParentQuoteId] ON [commercial].[Quotes] ([ParentQuoteId]);
CREATE UNIQUE INDEX [IX_QuoteLines_QuoteId_ProductCode] ON [commercial].[QuoteLines] ([QuoteId],[ProductCode]);
CREATE INDEX [IX_QuoteStatusHistory_CompanyId_QuoteId_ChangedAtUtc] ON [commercial].[QuoteStatusHistory] ([CompanyId],[QuoteId],[ChangedAtUtc]);
CREATE INDEX [IX_QuoteStatusHistory_ChangedByUserId] ON [commercial].[QuoteStatusHistory] ([ChangedByUserId]);
CREATE UNIQUE INDEX [IX_QuoteApprovalDecisions_QuoteId_Role] ON [commercial].[QuoteApprovalDecisions] ([QuoteId],[Role]);
CREATE INDEX [IX_QuoteApprovalDecisions_DecidedByUserId] ON [commercial].[QuoteApprovalDecisions] ([DecidedByUserId]);

ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users] ([Id]);
ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Quotes_ParentQuoteId] FOREIGN KEY ([ParentQuoteId]) REFERENCES [commercial].[Quotes] ([Id]);';
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Quotes_ParentQuoteId];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [FK_Quotes_Users_OwnerUserId];
DROP TABLE IF EXISTS [commercial].[QuoteApprovalDecisions];
DROP TABLE IF EXISTS [commercial].[QuoteStatusHistory];
DROP TABLE IF EXISTS [commercial].[QuoteLines];
DROP INDEX IF EXISTS [IX_Quotes_ParentQuoteId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Quotes_OwnerUserId] ON [commercial].[Quotes];
DROP INDEX IF EXISTS [IX_Quotes_CompanyId_Code_Revision] ON [commercial].[Quotes];
CREATE UNIQUE INDEX [IX_Quotes_CompanyId_Code] ON [commercial].[Quotes] ([CompanyId],[Code]);
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_CurrencyCode];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_ValidUntilUtc];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_PaymentTerms];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_Revision];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_GrossAmount];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_DiscountAmount];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_CostAmount];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_NetAmount];
ALTER TABLE [commercial].[Quotes] DROP CONSTRAINT IF EXISTS [DF_Quotes_ApprovalLevel];
ALTER TABLE [commercial].[Quotes] DROP COLUMN [OwnerUserId],[CurrencyCode],[ValidUntilUtc],[PaymentTerms],[Revision],
    [ParentQuoteId],[GrossAmount],[DiscountAmount],[CostAmount],[NetAmount],[ApprovalLevel],[SubmittedAtUtc],
    [ApprovedAtUtc],[SentAtUtc],[AcceptedAtUtc];
""");
}
