using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609220004_SalesPipelineGovernance")]
public sealed class SalesPipelineGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
ALTER TABLE [sales].[Leads] ADD
    [OwnerUserId] uniqueidentifier NULL, [Phone] nvarchar(40) NULL, [Email] nvarchar(256) NULL,
    [ConvertedOpportunityId] uniqueidentifier NULL, [AssignedAtUtc] datetimeoffset(3) NULL,
    [FirstContactDueAtUtc] datetimeoffset(3) NULL, [FirstContactAtUtc] datetimeoffset(3) NULL,
    [LastActivityAtUtc] datetimeoffset(3) NULL, [NextAction] nvarchar(300) NULL,
    [NextActionAtUtc] datetimeoffset(3) NULL, [StatusReason] nvarchar(1000) NULL;

ALTER TABLE [sales].[Opportunities] ADD
    [OwnerUserId] uniqueidentifier NULL, [OriginLeadId] uniqueidentifier NULL,
    [ExpectedCloseAtUtc] datetimeoffset(3) NULL, [Source] nvarchar(80) NULL,
    [NextAction] nvarchar(300) NULL, [NextActionAtUtc] datetimeoffset(3) NULL,
    [LastActivityAtUtc] datetimeoffset(3) NULL, [Competitor] nvarchar(200) NULL,
    [RiskLevel] nvarchar(24) NULL, [OutcomeReason] nvarchar(1000) NULL,
    [ClosedAtUtc] datetimeoffset(3) NULL;

-- Compile only after the preceding ALTER TABLE has added these columns.
EXEC sys.sp_executesql N'UPDATE l SET [OwnerUserId] = u.[Id]
FROM [sales].[Leads] l
OUTER APPLY (SELECT TOP (1) [Id] FROM [iam].[Users] WHERE [DisplayName] = l.[Owner] ORDER BY [Id]) u;
UPDATE [sales].[Leads] SET
    [FirstContactDueAtUtc] = DATEADD(hour, 4, [CreatedAtUtc]),
    [LastActivityAtUtc] = [UpdatedAtUtc],
    [AssignedAtUtc] = CASE WHEN [OwnerUserId] IS NULL THEN NULL ELSE [CreatedAtUtc] END,
    [StatusReason] = N''انتقال داده قدیمی'';

UPDATE o SET [OwnerUserId] = u.[Id]
FROM [sales].[Opportunities] o
OUTER APPLY (SELECT TOP (1) [Id] FROM [iam].[Users] WHERE [DisplayName] = o.[Owner] ORDER BY [Id]) u;
UPDATE [sales].[Opportunities] SET
    [Stage] = CASE WHEN [Stage] = N''Solution'' THEN N''SolutionOffer'' WHEN [Stage] = N''Quote'' THEN N''Commit'' ELSE [Stage] END,
    [Probability] = CASE [Stage] WHEN N''Discovery'' THEN 25 WHEN N''Solution'' THEN 60 WHEN N''Negotiation'' THEN 75 WHEN N''Quote'' THEN 90 WHEN N''Won'' THEN 100 WHEN N''Lost'' THEN 0 ELSE [Probability] END,
    [ExpectedCloseAtUtc] = DATEADD(day, 30, [CreatedAtUtc]), [Source] = N''Legacy'',
    [LastActivityAtUtc] = [UpdatedAtUtc], [RiskLevel] = N''Medium'',
    [ClosedAtUtc] = CASE WHEN [Stage] IN (N''Won'', N''Lost'') THEN [UpdatedAtUtc] ELSE NULL END;

ALTER TABLE [sales].[Leads] ALTER COLUMN [FirstContactDueAtUtc] datetimeoffset(3) NOT NULL;
ALTER TABLE [sales].[Leads] ALTER COLUMN [LastActivityAtUtc] datetimeoffset(3) NOT NULL;
ALTER TABLE [sales].[Opportunities] ALTER COLUMN [ExpectedCloseAtUtc] datetimeoffset(3) NOT NULL;
ALTER TABLE [sales].[Opportunities] ALTER COLUMN [Source] nvarchar(80) NOT NULL;
ALTER TABLE [sales].[Opportunities] ALTER COLUMN [LastActivityAtUtc] datetimeoffset(3) NOT NULL;
ALTER TABLE [sales].[Opportunities] ALTER COLUMN [RiskLevel] nvarchar(24) NOT NULL;

CREATE TABLE [sales].[LeadStatusHistory] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [LeadId] uniqueidentifier NOT NULL,
    [FromStatus] nvarchar(24) NULL, [ToStatus] nvarchar(24) NOT NULL, [Reason] nvarchar(1000) NOT NULL,
    [ChangedByUserId] uniqueidentifier NOT NULL, [ChangedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_LeadStatusHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeadStatusHistory_Leads_LeadId] FOREIGN KEY ([LeadId]) REFERENCES [sales].[Leads] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_LeadStatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [sales].[OpportunityStageHistory] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [OpportunityId] uniqueidentifier NOT NULL,
    [FromStage] nvarchar(24) NULL, [ToStage] nvarchar(24) NOT NULL, [Probability] int NOT NULL,
    [Reason] nvarchar(1000) NOT NULL, [ChangedByUserId] uniqueidentifier NOT NULL,
    [ChangedAtUtc] datetimeoffset(3) NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_OpportunityStageHistory] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OpportunityStageHistory_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OpportunityStageHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [sales].[OpportunityActivities] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL, [OpportunityId] uniqueidentifier NOT NULL,
    [Type] nvarchar(24) NOT NULL, [Subject] nvarchar(250) NOT NULL, [Outcome] nvarchar(2000) NOT NULL,
    [OccurredAtUtc] datetimeoffset(3) NOT NULL, [ActorUserId] uniqueidentifier NOT NULL,
    [NextAction] nvarchar(300) NULL, [NextActionAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_OpportunityActivities] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OpportunityActivities_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OpportunityActivities_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [iam].[Users] ([Id])
);

CREATE INDEX [IX_Leads_CompanyId_OwnerUserId_Status_FirstContactDueAtUtc] ON [sales].[Leads] ([CompanyId], [OwnerUserId], [Status], [FirstContactDueAtUtc]);
CREATE INDEX [IX_Leads_OwnerUserId] ON [sales].[Leads] ([OwnerUserId]);
CREATE INDEX [IX_Leads_ConvertedOpportunityId] ON [sales].[Leads] ([ConvertedOpportunityId]);
CREATE INDEX [IX_Opportunities_CompanyId_OwnerUserId_Stage_ExpectedCloseAtUtc] ON [sales].[Opportunities] ([CompanyId], [OwnerUserId], [Stage], [ExpectedCloseAtUtc]);
CREATE INDEX [IX_Opportunities_OwnerUserId] ON [sales].[Opportunities] ([OwnerUserId]);
CREATE INDEX [IX_Opportunities_OriginLeadId] ON [sales].[Opportunities] ([OriginLeadId]);
CREATE INDEX [IX_LeadStatusHistory_CompanyId_LeadId_ChangedAtUtc] ON [sales].[LeadStatusHistory] ([CompanyId], [LeadId], [ChangedAtUtc]);
CREATE INDEX [IX_LeadStatusHistory_LeadId] ON [sales].[LeadStatusHistory] ([LeadId]);
CREATE INDEX [IX_LeadStatusHistory_ChangedByUserId] ON [sales].[LeadStatusHistory] ([ChangedByUserId]);
CREATE INDEX [IX_OpportunityStageHistory_CompanyId_OpportunityId_ChangedAtUtc] ON [sales].[OpportunityStageHistory] ([CompanyId], [OpportunityId], [ChangedAtUtc]);
CREATE INDEX [IX_OpportunityStageHistory_OpportunityId] ON [sales].[OpportunityStageHistory] ([OpportunityId]);
CREATE INDEX [IX_OpportunityStageHistory_ChangedByUserId] ON [sales].[OpportunityStageHistory] ([ChangedByUserId]);
CREATE INDEX [IX_OpportunityActivities_CompanyId_OpportunityId_OccurredAtUtc] ON [sales].[OpportunityActivities] ([CompanyId], [OpportunityId], [OccurredAtUtc]);
CREATE INDEX [IX_OpportunityActivities_OpportunityId] ON [sales].[OpportunityActivities] ([OpportunityId]);
CREATE INDEX [IX_OpportunityActivities_ActorUserId] ON [sales].[OpportunityActivities] ([ActorUserId]);

ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users] ([Id]);
ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Opportunities_ConvertedOpportunityId] FOREIGN KEY ([ConvertedOpportunityId]) REFERENCES [sales].[Opportunities] ([Id]);
ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users] ([Id]);
ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Leads_OriginLeadId] FOREIGN KEY ([OriginLeadId]) REFERENCES [sales].[Leads] ([Id]);';
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
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
UPDATE [sales].[Opportunities] SET [Stage] = CASE WHEN [Stage] = N'SolutionOffer' THEN N'Solution' WHEN [Stage] = N'Commit' THEN N'Quote' ELSE [Stage] END;
ALTER TABLE [sales].[Opportunities] DROP COLUMN [OwnerUserId], [OriginLeadId], [ExpectedCloseAtUtc], [Source], [NextAction], [NextActionAtUtc], [LastActivityAtUtc], [Competitor], [RiskLevel], [OutcomeReason], [ClosedAtUtc];
ALTER TABLE [sales].[Leads] DROP COLUMN [OwnerUserId], [Phone], [Email], [ConvertedOpportunityId], [AssignedAtUtc], [FirstContactDueAtUtc], [FirstContactAtUtc], [LastActivityAtUtc], [NextAction], [NextActionAtUtc], [StatusReason];
""");
}
