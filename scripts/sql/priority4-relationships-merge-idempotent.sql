SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL
        THROW 51003, 'Stop: EF migration history is missing. Establish the approved baseline before applying Priority 4.', 1;

    IF COL_LENGTH('sales.Leads', 'CustomerId') IS NULL ALTER TABLE [sales].[Leads] ADD [CustomerId] uniqueidentifier NULL;
    IF COL_LENGTH('sales.Opportunities', 'CustomerId') IS NULL ALTER TABLE [sales].[Opportunities] ADD [CustomerId] uniqueidentifier NULL;
    IF COL_LENGTH('commercial.Quotes', 'CustomerId') IS NULL ALTER TABLE [commercial].[Quotes] ADD [CustomerId] uniqueidentifier NULL;
    IF COL_LENGTH('commercial.Quotes', 'OpportunityId') IS NULL ALTER TABLE [commercial].[Quotes] ADD [OpportunityId] uniqueidentifier NULL;

-- Defer binding until ALTER TABLE has added the referenced columns.
EXEC sys.sp_executesql N'    UPDATE o SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
        WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer] AND c.[BranchId] = o.[BranchId])
    FROM [sales].[Opportunities] o WHERE o.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
        WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer] AND c.[BranchId] = o.[BranchId]);
    UPDATE o SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
        WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer])
    FROM [sales].[Opportunities] o WHERE o.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
        WHERE c.[CompanyId] = o.[CompanyId] AND c.[Name] = o.[Customer]);

    UPDATE q SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
        WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer] AND c.[BranchId] = q.[BranchId])
    FROM [commercial].[Quotes] q WHERE q.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
        WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer] AND c.[BranchId] = q.[BranchId]);
    UPDATE q SET [CustomerId] = (SELECT c.[Id] FROM [crm].[Customers] c
        WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer])
    FROM [commercial].[Quotes] q WHERE q.[CustomerId] IS NULL AND 1 = (SELECT COUNT(*) FROM [crm].[Customers] c
        WHERE c.[CompanyId] = q.[CompanyId] AND c.[Name] = q.[Customer]);

    UPDATE q SET [OpportunityId] = (SELECT o.[Id] FROM [sales].[Opportunities] o
        WHERE o.[CompanyId] = q.[CompanyId] AND o.[Title] = q.[Opportunity] AND o.[CustomerId] = q.[CustomerId])
    FROM [commercial].[Quotes] q WHERE q.[OpportunityId] IS NULL AND 1 = (SELECT COUNT(*) FROM [sales].[Opportunities] o
        WHERE o.[CompanyId] = q.[CompanyId] AND o.[Title] = q.[Opportunity] AND o.[CustomerId] = q.[CustomerId]);

    IF EXISTS (SELECT 1 FROM [sales].[Opportunities] WHERE [CustomerId] IS NULL)
        THROW 51001, ''Stop: unresolved Opportunity customer names. Correct source data and rerun.'', 1;
    IF EXISTS (SELECT 1 FROM [commercial].[Quotes] WHERE [CustomerId] IS NULL)
        THROW 51002, ''Stop: unresolved Quote customer names. Correct source data and rerun.'', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''sales.Opportunities'') AND name = ''CustomerId'' AND is_nullable = 1)
        ALTER TABLE [sales].[Opportunities] ALTER COLUMN [CustomerId] uniqueidentifier NOT NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(''commercial.Quotes'') AND name = ''CustomerId'' AND is_nullable = 1)
        ALTER TABLE [commercial].[Quotes] ALTER COLUMN [CustomerId] uniqueidentifier NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''sales.Leads'') AND name = ''IX_Leads_CompanyId_CustomerId'')
        CREATE INDEX [IX_Leads_CompanyId_CustomerId] ON [sales].[Leads] ([CompanyId], [CustomerId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''sales.Opportunities'') AND name = ''IX_Opportunities_CompanyId_CustomerId'')
        CREATE INDEX [IX_Opportunities_CompanyId_CustomerId] ON [sales].[Opportunities] ([CompanyId], [CustomerId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''commercial.Quotes'') AND name = ''IX_Quotes_CompanyId_CustomerId'')
        CREATE INDEX [IX_Quotes_CompanyId_CustomerId] ON [commercial].[Quotes] ([CompanyId], [CustomerId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''commercial.Quotes'') AND name = ''IX_Quotes_OpportunityId'')
        CREATE INDEX [IX_Quotes_OpportunityId] ON [commercial].[Quotes] ([OpportunityId]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = ''FK_Leads_Customers_CustomerId'')
        ALTER TABLE [sales].[Leads] ADD CONSTRAINT [FK_Leads_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = ''FK_Opportunities_Customers_CustomerId'')
        ALTER TABLE [sales].[Opportunities] ADD CONSTRAINT [FK_Opportunities_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = ''FK_Quotes_Customers_CustomerId'')
        ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = ''FK_Quotes_Opportunities_OpportunityId'')
        ALTER TABLE [commercial].[Quotes] ADD CONSTRAINT [FK_Quotes_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [sales].[Opportunities] ([Id]);

    IF OBJECT_ID(''crm.CustomerMergeOperations'', ''U'') IS NULL
    BEGIN
        CREATE TABLE [crm].[CustomerMergeOperations] (
            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_CustomerMergeOperations] PRIMARY KEY,
            [CompanyId] nvarchar(32) NOT NULL, [DuplicateCandidateId] uniqueidentifier NOT NULL,
            [SurvivorCustomerId] uniqueidentifier NOT NULL, [MergedCustomerId] uniqueidentifier NOT NULL,
            [MergedCustomerPreviousStatus] nvarchar(24) NOT NULL, [TransferManifestJson] nvarchar(max) NOT NULL,
            [Reason] nvarchar(1000) NOT NULL, [MergedByUserId] uniqueidentifier NOT NULL, [MergedAtUtc] datetimeoffset(3) NOT NULL,
            [Status] nvarchar(24) NOT NULL, [RevertedByUserId] uniqueidentifier NULL, [RevertedAtUtc] datetimeoffset(3) NULL,
            [RevertReason] nvarchar(1000) NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL,
            [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL);
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''crm.CustomerMergeOperations'') AND name = ''IX_CustomerMergeOperations_CompanyId_MergedAtUtc'')
        CREATE INDEX [IX_CustomerMergeOperations_CompanyId_MergedAtUtc] ON [crm].[CustomerMergeOperations] ([CompanyId], [MergedAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''crm.CustomerMergeOperations'') AND name = ''IX_CustomerMergeOperations_MergedCustomerId_Status'')
        CREATE INDEX [IX_CustomerMergeOperations_MergedCustomerId_Status] ON [crm].[CustomerMergeOperations] ([MergedCustomerId], [Status]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''crm.CustomerMergeOperations'') AND name = ''UX_CustomerMergeOperations_DuplicateCandidateId_Active'')
        CREATE UNIQUE INDEX [UX_CustomerMergeOperations_DuplicateCandidateId_Active] ON [crm].[CustomerMergeOperations] ([DuplicateCandidateId]) WHERE [Status] = N''Merged'';
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(''crm.CustomerMergeOperations'') AND name = ''UX_CustomerMergeOperations_MergedCustomerId_Active'')
        CREATE UNIQUE INDEX [UX_CustomerMergeOperations_MergedCustomerId_Active] ON [crm].[CustomerMergeOperations] ([MergedCustomerId]) WHERE [Status] = N''Merged'';

    IF NOT EXISTS
        (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N''202609210003_CustomerRelationshipsAndMerge'')
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N''202609210003_CustomerRelationshipsAndMerge'', N''10.0.12'');';
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
