SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL
        THROW 51020, 'Stop: EF migration history is missing. Establish the approved baseline before applying Priority 5.', 1;
    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609210003_CustomerRelationshipsAndMerge')
        THROW 51021, 'Stop: Priority 4 migration must be applied before Priority 5.', 1;

    IF COL_LENGTH('sales.Leads', 'OwnerUserId') IS NULL ALTER TABLE [sales].[Leads] ADD [OwnerUserId] uniqueidentifier NULL;
    IF COL_LENGTH('sales.Leads', 'Phone') IS NULL ALTER TABLE [sales].[Leads] ADD [Phone] nvarchar(40) NULL;
    IF COL_LENGTH('sales.Leads', 'Email') IS NULL ALTER TABLE [sales].[Leads] ADD [Email] nvarchar(256) NULL;
    IF COL_LENGTH('sales.Leads', 'ConvertedOpportunityId') IS NULL ALTER TABLE [sales].[Leads] ADD [ConvertedOpportunityId] uniqueidentifier NULL;
    IF COL_LENGTH('sales.Leads', 'AssignedAtUtc') IS NULL ALTER TABLE [sales].[Leads] ADD [AssignedAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Leads', 'FirstContactDueAtUtc') IS NULL ALTER TABLE [sales].[Leads] ADD [FirstContactDueAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Leads', 'FirstContactAtUtc') IS NULL ALTER TABLE [sales].[Leads] ADD [FirstContactAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Leads', 'LastActivityAtUtc') IS NULL ALTER TABLE [sales].[Leads] ADD [LastActivityAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Leads', 'NextAction') IS NULL ALTER TABLE [sales].[Leads] ADD [NextAction] nvarchar(300) NULL;
    IF COL_LENGTH('sales.Leads', 'NextActionAtUtc') IS NULL ALTER TABLE [sales].[Leads] ADD [NextActionAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Leads', 'StatusReason') IS NULL ALTER TABLE [sales].[Leads] ADD [StatusReason] nvarchar(1000) NULL;

    IF COL_LENGTH('sales.Opportunities', 'OwnerUserId') IS NULL ALTER TABLE [sales].[Opportunities] ADD [OwnerUserId] uniqueidentifier NULL;
    IF COL_LENGTH('sales.Opportunities', 'OriginLeadId') IS NULL ALTER TABLE [sales].[Opportunities] ADD [OriginLeadId] uniqueidentifier NULL;
    IF COL_LENGTH('sales.Opportunities', 'ExpectedCloseAtUtc') IS NULL ALTER TABLE [sales].[Opportunities] ADD [ExpectedCloseAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Opportunities', 'Source') IS NULL ALTER TABLE [sales].[Opportunities] ADD [Source] nvarchar(80) NULL;
    IF COL_LENGTH('sales.Opportunities', 'NextAction') IS NULL ALTER TABLE [sales].[Opportunities] ADD [NextAction] nvarchar(300) NULL;
    IF COL_LENGTH('sales.Opportunities', 'NextActionAtUtc') IS NULL ALTER TABLE [sales].[Opportunities] ADD [NextActionAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Opportunities', 'LastActivityAtUtc') IS NULL ALTER TABLE [sales].[Opportunities] ADD [LastActivityAtUtc] datetimeoffset(3) NULL;
    IF COL_LENGTH('sales.Opportunities', 'Competitor') IS NULL ALTER TABLE [sales].[Opportunities] ADD [Competitor] nvarchar(200) NULL;
    IF COL_LENGTH('sales.Opportunities', 'RiskLevel') IS NULL ALTER TABLE [sales].[Opportunities] ADD [RiskLevel] nvarchar(24) NULL;
    IF COL_LENGTH('sales.Opportunities', 'OutcomeReason') IS NULL ALTER TABLE [sales].[Opportunities] ADD [OutcomeReason] nvarchar(1000) NULL;
    IF COL_LENGTH('sales.Opportunities', 'ClosedAtUtc') IS NULL ALTER TABLE [sales].[Opportunities] ADD [ClosedAtUtc] datetimeoffset(3) NULL;

-- Defer binding until ALTER TABLE has added the referenced columns.
EXEC sys.sp_executesql N'    UPDATE l SET [OwnerUserId] = COALESCE(l.[OwnerUserId], u.[Id]),
        [FirstContactDueAtUtc] = COALESCE(l.[FirstContactDueAtUtc], DATEADD(hour, 4, l.[CreatedAtUtc])),
        [LastActivityAtUtc] = COALESCE(l.[LastActivityAtUtc], l.[UpdatedAtUtc]),
        [AssignedAtUtc] = COALESCE(l.[AssignedAtUtc], CASE WHEN u.[Id] IS NULL THEN NULL ELSE l.[CreatedAtUtc] END),
        [StatusReason] = COALESCE(l.[StatusReason], N''انتقال داده قدیمی'')
    FROM [sales].[Leads] l OUTER APPLY (SELECT TOP (1) [Id] FROM [iam].[Users] WHERE [DisplayName] = l.[Owner] ORDER BY [Id]) u;

    UPDATE o SET [OwnerUserId] = COALESCE(o.[OwnerUserId], u.[Id]),
        [Stage] = CASE WHEN o.[Stage] = N''Solution'' THEN N''SolutionOffer'' WHEN o.[Stage] = N''Quote'' THEN N''Commit'' ELSE o.[Stage] END,
        [Probability] = CASE o.[Stage] WHEN N''Discovery'' THEN 25 WHEN N''Solution'' THEN 60 WHEN N''Negotiation'' THEN 75 WHEN N''Quote'' THEN 90 WHEN N''Won'' THEN 100 WHEN N''Lost'' THEN 0 ELSE o.[Probability] END,
        [ExpectedCloseAtUtc] = COALESCE(o.[ExpectedCloseAtUtc], DATEADD(day, 30, o.[CreatedAtUtc])),
        [Source] = COALESCE(o.[Source], N''Legacy''), [LastActivityAtUtc] = COALESCE(o.[LastActivityAtUtc], o.[UpdatedAtUtc]),
        [RiskLevel] = COALESCE(o.[RiskLevel], N''Medium''),
        [ClosedAtUtc] = COALESCE(o.[ClosedAtUtc], CASE WHEN o.[Stage] IN (N''Won'', N''Lost'') THEN o.[UpdatedAtUtc] ELSE NULL END)
    FROM [sales].[Opportunities] o OUTER APPLY (SELECT TOP (1) [Id] FROM [iam].[Users] WHERE [DisplayName] = o.[Owner] ORDER BY [Id]) u;

    ALTER TABLE [sales].[Leads] ALTER COLUMN [FirstContactDueAtUtc] datetimeoffset(3) NOT NULL;
    ALTER TABLE [sales].[Leads] ALTER COLUMN [LastActivityAtUtc] datetimeoffset(3) NOT NULL;
    ALTER TABLE [sales].[Opportunities] ALTER COLUMN [ExpectedCloseAtUtc] datetimeoffset(3) NOT NULL;
    ALTER TABLE [sales].[Opportunities] ALTER COLUMN [Source] nvarchar(80) NOT NULL;
    ALTER TABLE [sales].[Opportunities] ALTER COLUMN [LastActivityAtUtc] datetimeoffset(3) NOT NULL;
    ALTER TABLE [sales].[Opportunities] ALTER COLUMN [RiskLevel] nvarchar(24) NOT NULL;

    IF OBJECT_ID(N''[sales].[LeadStatusHistory]'', N''U'') IS NULL
    CREATE TABLE [sales].[LeadStatusHistory] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_LeadStatusHistory] PRIMARY KEY,
        [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
        [LeadId] uniqueidentifier NOT NULL, [FromStatus] nvarchar(24) NULL, [ToStatus] nvarchar(24) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL, [ChangedByUserId] uniqueidentifier NOT NULL,
        [ChangedAtUtc] datetimeoffset(3) NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL,
        [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
        CONSTRAINT [FK_LeadStatusHistory_Leads_LeadId] FOREIGN KEY ([LeadId]) REFERENCES [sales].[Leads]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_LeadStatusHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users]([Id]));

    IF OBJECT_ID(N''[sales].[OpportunityStageHistory]'', N''U'') IS NULL
    CREATE TABLE [sales].[OpportunityStageHistory] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_OpportunityStageHistory] PRIMARY KEY,
        [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
        [OpportunityId] uniqueidentifier NOT NULL, [FromStage] nvarchar(24) NULL, [ToStage] nvarchar(24) NOT NULL,
        [Probability] int NOT NULL, [Reason] nvarchar(1000) NOT NULL, [ChangedByUserId] uniqueidentifier NOT NULL,
        [ChangedAtUtc] datetimeoffset(3) NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL,
        [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
        CONSTRAINT [FK_OpportunityStageHistory_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OpportunityStageHistory_Users_ChangedByUserId] FOREIGN KEY ([ChangedByUserId]) REFERENCES [iam].[Users]([Id]));

    IF OBJECT_ID(N''[sales].[OpportunityActivities]'', N''U'') IS NULL
    CREATE TABLE [sales].[OpportunityActivities] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_OpportunityActivities] PRIMARY KEY,
        [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
        [OpportunityId] uniqueidentifier NOT NULL, [Type] nvarchar(24) NOT NULL, [Subject] nvarchar(250) NOT NULL,
        [Outcome] nvarchar(2000) NOT NULL, [OccurredAtUtc] datetimeoffset(3) NOT NULL,
        [ActorUserId] uniqueidentifier NOT NULL, [NextAction] nvarchar(300) NULL, [NextActionAtUtc] datetimeoffset(3) NULL,
        [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
        CONSTRAINT [FK_OpportunityActivities_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OpportunityActivities_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [iam].[Users]([Id]));

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Leads_CompanyId_OwnerUserId_Status_FirstContactDueAtUtc'' AND object_id=OBJECT_ID(N''[sales].[Leads]'')) CREATE INDEX [IX_Leads_CompanyId_OwnerUserId_Status_FirstContactDueAtUtc] ON [sales].[Leads]([CompanyId],[OwnerUserId],[Status],[FirstContactDueAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Leads_OwnerUserId'' AND object_id=OBJECT_ID(N''[sales].[Leads]'')) CREATE INDEX [IX_Leads_OwnerUserId] ON [sales].[Leads]([OwnerUserId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Leads_ConvertedOpportunityId'' AND object_id=OBJECT_ID(N''[sales].[Leads]'')) CREATE INDEX [IX_Leads_ConvertedOpportunityId] ON [sales].[Leads]([ConvertedOpportunityId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Opportunities_CompanyId_OwnerUserId_Stage_ExpectedCloseAtUtc'' AND object_id=OBJECT_ID(N''[sales].[Opportunities]'')) CREATE INDEX [IX_Opportunities_CompanyId_OwnerUserId_Stage_ExpectedCloseAtUtc] ON [sales].[Opportunities]([CompanyId],[OwnerUserId],[Stage],[ExpectedCloseAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Opportunities_OwnerUserId'' AND object_id=OBJECT_ID(N''[sales].[Opportunities]'')) CREATE INDEX [IX_Opportunities_OwnerUserId] ON [sales].[Opportunities]([OwnerUserId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_Opportunities_OriginLeadId'' AND object_id=OBJECT_ID(N''[sales].[Opportunities]'')) CREATE INDEX [IX_Opportunities_OriginLeadId] ON [sales].[Opportunities]([OriginLeadId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_LeadStatusHistory_CompanyId_LeadId_ChangedAtUtc'' AND object_id=OBJECT_ID(N''[sales].[LeadStatusHistory]'')) CREATE INDEX [IX_LeadStatusHistory_CompanyId_LeadId_ChangedAtUtc] ON [sales].[LeadStatusHistory]([CompanyId],[LeadId],[ChangedAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_LeadStatusHistory_LeadId'' AND object_id=OBJECT_ID(N''[sales].[LeadStatusHistory]'')) CREATE INDEX [IX_LeadStatusHistory_LeadId] ON [sales].[LeadStatusHistory]([LeadId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_LeadStatusHistory_ChangedByUserId'' AND object_id=OBJECT_ID(N''[sales].[LeadStatusHistory]'')) CREATE INDEX [IX_LeadStatusHistory_ChangedByUserId] ON [sales].[LeadStatusHistory]([ChangedByUserId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityStageHistory_CompanyId_OpportunityId_ChangedAtUtc'' AND object_id=OBJECT_ID(N''[sales].[OpportunityStageHistory]'')) CREATE INDEX [IX_OpportunityStageHistory_CompanyId_OpportunityId_ChangedAtUtc] ON [sales].[OpportunityStageHistory]([CompanyId],[OpportunityId],[ChangedAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityStageHistory_OpportunityId'' AND object_id=OBJECT_ID(N''[sales].[OpportunityStageHistory]'')) CREATE INDEX [IX_OpportunityStageHistory_OpportunityId] ON [sales].[OpportunityStageHistory]([OpportunityId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityStageHistory_ChangedByUserId'' AND object_id=OBJECT_ID(N''[sales].[OpportunityStageHistory]'')) CREATE INDEX [IX_OpportunityStageHistory_ChangedByUserId] ON [sales].[OpportunityStageHistory]([ChangedByUserId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityActivities_CompanyId_OpportunityId_OccurredAtUtc'' AND object_id=OBJECT_ID(N''[sales].[OpportunityActivities]'')) CREATE INDEX [IX_OpportunityActivities_CompanyId_OpportunityId_OccurredAtUtc] ON [sales].[OpportunityActivities]([CompanyId],[OpportunityId],[OccurredAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityActivities_OpportunityId'' AND object_id=OBJECT_ID(N''[sales].[OpportunityActivities]'')) CREATE INDEX [IX_OpportunityActivities_OpportunityId] ON [sales].[OpportunityActivities]([OpportunityId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N''IX_OpportunityActivities_ActorUserId'' AND object_id=OBJECT_ID(N''[sales].[OpportunityActivities]'')) CREATE INDEX [IX_OpportunityActivities_ActorUserId] ON [sales].[OpportunityActivities]([ActorUserId]);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N''FK_Leads_Users_OwnerUserId'') ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Users_OwnerUserId] FOREIGN KEY([OwnerUserId]) REFERENCES [iam].[Users]([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N''FK_Leads_Opportunities_ConvertedOpportunityId'') ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Opportunities_ConvertedOpportunityId] FOREIGN KEY([ConvertedOpportunityId]) REFERENCES [sales].[Opportunities]([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N''FK_Opportunities_Users_OwnerUserId'') ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Users_OwnerUserId] FOREIGN KEY([OwnerUserId]) REFERENCES [iam].[Users]([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N''FK_Opportunities_Leads_OriginLeadId'') ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Leads_OriginLeadId] FOREIGN KEY([OriginLeadId]) REFERENCES [sales].[Leads]([Id]);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N''202609220004_SalesPipelineGovernance'')
        INSERT INTO [dbo].[__EFMigrationsHistory]([MigrationId],[ProductVersion]) VALUES(N''202609220004_SalesPipelineGovernance'',N''10.0.12'');';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
