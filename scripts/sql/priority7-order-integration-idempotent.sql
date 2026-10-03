SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
    THROW 51100, 'Stop: EF migration history was not found.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609260005_QuotePricingGovernance')
    THROW 51101, 'Stop: priority-6 baseline is required.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202609260006_OrderIntegrationVisibility')
BEGIN COMMIT; RETURN; END;
IF OBJECT_ID(N'[commercial].[OrderRequests]', N'U') IS NOT NULL OR
   OBJECT_ID(N'[integration].[OrderIntegrationMessages]', N'U') IS NOT NULL
    THROW 51102, 'Stop: partial priority-7 schema detected; inspect before retry.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name=N'integration') EXEC(N'CREATE SCHEMA [integration]');

CREATE TABLE [commercial].[OrderRequests](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[Code] nvarchar(32) NOT NULL,[QuoteId] uniqueidentifier NOT NULL,
 [QuoteCode] nvarchar(64) NOT NULL,[CustomerId] uniqueidentifier NOT NULL,[Customer] nvarchar(200) NOT NULL,
 [OpportunityId] uniqueidentifier NULL,[OwnerUserId] uniqueidentifier NULL,[CompanyId] nvarchar(32) NOT NULL,
 [BranchId] nvarchar(32) NOT NULL,[TerritoryId] nvarchar(32) NULL,[CurrencyCode] nvarchar(8) NOT NULL,
 [NetAmount] decimal(18,2) NOT NULL,[Status] nvarchar(32) NOT NULL,
 [SubmissionIdempotencyKey] nvarchar(100) NOT NULL,[CorrelationId] nvarchar(100) NOT NULL,
 [CreditLimit] decimal(18,2) NOT NULL,[CreditUsed] decimal(18,2) NOT NULL,
 [OverdueAmount] decimal(18,2) NOT NULL,[AvailableCredit] decimal(18,2) NOT NULL,[IsCreditHold] bit NOT NULL,
 [CreditReason] nvarchar(1000) NULL,[CreditSource] nvarchar(200) NULL,[CreditSnapshotAtUtc] datetimeoffset(3) NULL,
 [CreditOverrideExpiresAtUtc] datetimeoffset(3) NULL,[ErpOrderNumber] nvarchar(100) NULL,
 [DeliveryReference] nvarchar(100) NULL,[InvoiceNumber] nvarchar(100) NULL,[PaymentReference] nvarchar(100) NULL,
 [LastIntegrationError] nvarchar(2000) NULL,[SubmittedAtUtc] datetimeoffset(3) NULL,
 [LastSynchronizedAtUtc] datetimeoffset(3) NULL,[CreatedAtUtc] datetimeoffset(3) NOT NULL,
 [UpdatedAtUtc] datetimeoffset(3) NOT NULL,[Version] bigint NOT NULL,
 CONSTRAINT [FK_OrderRequests_Quotes_QuoteId] FOREIGN KEY([QuoteId]) REFERENCES [commercial].[Quotes]([Id]),
 CONSTRAINT [FK_OrderRequests_Customers_CustomerId] FOREIGN KEY([CustomerId]) REFERENCES [crm].[Customers]([Id]),
 CONSTRAINT [FK_OrderRequests_Opportunities_OpportunityId] FOREIGN KEY([OpportunityId]) REFERENCES [sales].[Opportunities]([Id]),
 CONSTRAINT [FK_OrderRequests_Users_OwnerUserId] FOREIGN KEY([OwnerUserId]) REFERENCES [iam].[Users]([Id]));

CREATE TABLE [commercial].[OrderCreditDecisions](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[CompanyId] nvarchar(32) NOT NULL,[BranchId] nvarchar(32) NOT NULL,
 [TerritoryId] nvarchar(32) NULL,[OrderRequestId] uniqueidentifier NOT NULL,[Decision] nvarchar(24) NOT NULL,
 [CreditLimit] decimal(18,2) NOT NULL,[CreditUsed] decimal(18,2) NOT NULL,[OverdueAmount] decimal(18,2) NOT NULL,
 [AvailableCredit] decimal(18,2) NOT NULL,[Reason] nvarchar(1000) NOT NULL,[Source] nvarchar(200) NOT NULL,
 [DecidedByUserId] uniqueidentifier NOT NULL,[DecidedAtUtc] datetimeoffset(3) NOT NULL,[ExpiresAtUtc] datetimeoffset(3) NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL,[UpdatedAtUtc] datetimeoffset(3) NOT NULL,[Version] bigint NOT NULL,
 CONSTRAINT [FK_OrderCreditDecisions_OrderRequests_OrderRequestId] FOREIGN KEY([OrderRequestId]) REFERENCES [commercial].[OrderRequests]([Id]) ON DELETE CASCADE,
 CONSTRAINT [FK_OrderCreditDecisions_Users_DecidedByUserId] FOREIGN KEY([DecidedByUserId]) REFERENCES [iam].[Users]([Id]));

CREATE TABLE [commercial].[OrderStatusHistory](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[CompanyId] nvarchar(32) NOT NULL,[BranchId] nvarchar(32) NOT NULL,
 [TerritoryId] nvarchar(32) NULL,[OrderRequestId] uniqueidentifier NOT NULL,[FromStatus] nvarchar(32) NULL,
 [ToStatus] nvarchar(32) NOT NULL,[Reason] nvarchar(2000) NOT NULL,[Source] nvarchar(200) NOT NULL,
 [ChangedByUserId] uniqueidentifier NOT NULL,[ChangedAtUtc] datetimeoffset(3) NOT NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL,[UpdatedAtUtc] datetimeoffset(3) NOT NULL,[Version] bigint NOT NULL,
 CONSTRAINT [FK_OrderStatusHistory_OrderRequests_OrderRequestId] FOREIGN KEY([OrderRequestId]) REFERENCES [commercial].[OrderRequests]([Id]) ON DELETE CASCADE,
 CONSTRAINT [FK_OrderStatusHistory_Users_ChangedByUserId] FOREIGN KEY([ChangedByUserId]) REFERENCES [iam].[Users]([Id]));

CREATE TABLE [integration].[OrderIntegrationMessages](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[CompanyId] nvarchar(32) NOT NULL,[BranchId] nvarchar(32) NOT NULL,
 [TerritoryId] nvarchar(32) NULL,[OrderRequestId] uniqueidentifier NOT NULL,[MessageType] nvarchar(80) NOT NULL,
 [IdempotencyKey] nvarchar(100) NOT NULL,[CorrelationId] nvarchar(100) NOT NULL,[PayloadFingerprint] nvarchar(64) NOT NULL,
 [Status] nvarchar(32) NOT NULL,[AttemptCount] int NOT NULL,[NextAttemptAtUtc] datetimeoffset(3) NULL,
 [LastError] nvarchar(2000) NULL,[ExternalReference] nvarchar(100) NULL,[CompletedAtUtc] datetimeoffset(3) NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL,[UpdatedAtUtc] datetimeoffset(3) NOT NULL,[Version] bigint NOT NULL,
 CONSTRAINT [FK_OrderIntegrationMessages_OrderRequests_OrderRequestId] FOREIGN KEY([OrderRequestId]) REFERENCES [commercial].[OrderRequests]([Id]) ON DELETE CASCADE);

CREATE TABLE [integration].[OrderIntegrationAttempts](
 [Id] uniqueidentifier NOT NULL PRIMARY KEY,[CompanyId] nvarchar(32) NOT NULL,[BranchId] nvarchar(32) NOT NULL,
 [TerritoryId] nvarchar(32) NULL,[MessageId] uniqueidentifier NOT NULL,[OrderRequestId] uniqueidentifier NOT NULL,
 [AttemptNumber] int NOT NULL,[Outcome] nvarchar(32) NOT NULL,[Detail] nvarchar(2000) NOT NULL,
 [ExternalReference] nvarchar(100) NULL,[AttemptedAtUtc] datetimeoffset(3) NOT NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL,[UpdatedAtUtc] datetimeoffset(3) NOT NULL,[Version] bigint NOT NULL,
 CONSTRAINT [FK_OrderIntegrationAttempts_OrderIntegrationMessages_MessageId] FOREIGN KEY([MessageId]) REFERENCES [integration].[OrderIntegrationMessages]([Id]) ON DELETE CASCADE,
 CONSTRAINT [FK_OrderIntegrationAttempts_OrderRequests_OrderRequestId] FOREIGN KEY([OrderRequestId]) REFERENCES [commercial].[OrderRequests]([Id]));

CREATE UNIQUE INDEX [IX_OrderRequests_CompanyId_Code] ON [commercial].[OrderRequests]([CompanyId],[Code]);
CREATE UNIQUE INDEX [IX_OrderRequests_QuoteId] ON [commercial].[OrderRequests]([QuoteId]);
CREATE UNIQUE INDEX [IX_OrderRequests_SubmissionIdempotencyKey] ON [commercial].[OrderRequests]([SubmissionIdempotencyKey]);
CREATE INDEX [IX_OrderRequests_CompanyId_BranchId_TerritoryId_Status] ON [commercial].[OrderRequests]([CompanyId],[BranchId],[TerritoryId],[Status]);
CREATE INDEX [IX_OrderRequests_CompanyId_CustomerId] ON [commercial].[OrderRequests]([CompanyId],[CustomerId]);
CREATE INDEX [IX_OrderRequests_OpportunityId] ON [commercial].[OrderRequests]([OpportunityId]);
CREATE INDEX [IX_OrderRequests_OwnerUserId] ON [commercial].[OrderRequests]([OwnerUserId]);
CREATE INDEX [IX_OrderCreditDecisions_CompanyId_OrderRequestId_DecidedAtUtc] ON [commercial].[OrderCreditDecisions]([CompanyId],[OrderRequestId],[DecidedAtUtc]);
CREATE INDEX [IX_OrderCreditDecisions_DecidedByUserId] ON [commercial].[OrderCreditDecisions]([DecidedByUserId]);
CREATE INDEX [IX_OrderStatusHistory_CompanyId_OrderRequestId_ChangedAtUtc] ON [commercial].[OrderStatusHistory]([CompanyId],[OrderRequestId],[ChangedAtUtc]);
CREATE INDEX [IX_OrderStatusHistory_ChangedByUserId] ON [commercial].[OrderStatusHistory]([ChangedByUserId]);
CREATE UNIQUE INDEX [IX_OrderIntegrationMessages_OrderRequestId] ON [integration].[OrderIntegrationMessages]([OrderRequestId]);
CREATE UNIQUE INDEX [IX_OrderIntegrationMessages_IdempotencyKey] ON [integration].[OrderIntegrationMessages]([IdempotencyKey]);
CREATE INDEX [IX_OrderIntegrationMessages_Status_NextAttemptAtUtc] ON [integration].[OrderIntegrationMessages]([Status],[NextAttemptAtUtc]);
CREATE UNIQUE INDEX [IX_OrderIntegrationAttempts_MessageId_AttemptNumber] ON [integration].[OrderIntegrationAttempts]([MessageId],[AttemptNumber]);
CREATE INDEX [IX_OrderIntegrationAttempts_OrderRequestId_AttemptedAtUtc] ON [integration].[OrderIntegrationAttempts]([OrderRequestId],[AttemptedAtUtc]);

INSERT INTO [dbo].[__EFMigrationsHistory]([MigrationId],[ProductVersion])
VALUES(N'202609260006_OrderIntegrationVisibility',N'10.0.12');
COMMIT;
