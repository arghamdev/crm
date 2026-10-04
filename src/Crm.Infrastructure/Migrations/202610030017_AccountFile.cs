using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>Account file (پرونده حساب): activities, notes, documents, payments, contracts, projects, marketing and relation tables; account classification and hierarchy; account guarantees; grants.</summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610030017_AccountFile")]
public sealed class AccountFile : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
IF SCHEMA_ID(N'account') IS NULL EXEC(N'CREATE SCHEMA [account];');
IF SCHEMA_ID(N'marketing') IS NULL EXEC(N'CREATE SCHEMA [marketing];');
ALTER TABLE [sales].[Opportunities] ADD [ContactId] uniqueidentifier NULL;
ALTER TABLE [sales].[Opportunities] ADD [CurrencyCode] nvarchar(3) NOT NULL DEFAULT N'IRR';
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[channel].[DealerGuarantees]') AND [c].[name] = N'DealerId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT ' + @var + ';');
DROP INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees];
ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId];
ALTER TABLE [channel].[DealerGuarantees] ALTER COLUMN [DealerId] uniqueidentifier NULL;
ALTER TABLE [channel].[DealerGuarantees] ADD CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]);
CREATE INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees] ([DealerId], [Status]);
ALTER TABLE [channel].[DealerGuarantees] ADD [CustomerId] uniqueidentifier NULL;
ALTER TABLE [crm].[Customers] ADD [ParentCustomerId] uniqueidentifier NULL;
ALTER TABLE [crm].[Customers] ADD [RelationshipType] nvarchar(16) NOT NULL DEFAULT N'Customer';
ALTER TABLE [crm].[Customers] ADD [Tags] nvarchar(400) NULL;
CREATE TABLE [account].[Activities] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [Type] nvarchar(12) NOT NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Description] nvarchar(4000) NULL,
    [ContactId] uniqueidentifier NULL,
    [OwnerUserId] uniqueidentifier NOT NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [StartAtUtc] datetimeoffset(3) NOT NULL,
    [EndAtUtc] datetimeoffset(3) NULL,
    [DurationMinutes] int NULL,
    [Direction] nvarchar(12) NULL,
    [Location] nvarchar(300) NULL,
    [Priority] nvarchar(12) NOT NULL,
    [ReminderMinutesBefore] int NULL,
    [RelatedKind] nvarchar(16) NOT NULL,
    [RelatedId] uniqueidentifier NULL,
    [Status] nvarchar(12) NOT NULL,
    [CallResult] nvarchar(16) NULL,
    [Outcome] nvarchar(4000) NULL,
    [CompletedAtUtc] datetimeoffset(3) NULL,
    [CompletedByUserId] uniqueidentifier NULL,
    [CancelReason] nvarchar(500) NULL,
    [FollowUpOfId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Activities] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Activities_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_Activities_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [account].[Allocations] (
    [Id] uniqueidentifier NOT NULL,
    [Kind] nvarchar(12) NOT NULL,
    [ItemCode] nvarchar(40) NULL,
    [ItemName] nvarchar(200) NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [Date] date NOT NULL,
    [Status] nvarchar(12) NOT NULL,
    [Notes] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Allocations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Allocations_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [account].[BankAccounts] (
    [Id] uniqueidentifier NOT NULL,
    [BankName] nvarchar(80) NOT NULL,
    [Iban] nvarchar(26) NOT NULL,
    [AccountNumber] nvarchar(30) NULL,
    [HolderName] nvarchar(150) NOT NULL,
    [IsPrimary] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_BankAccounts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BankAccounts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [marketing].[Campaigns] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Type] nvarchar(16) NOT NULL,
    [Status] nvarchar(12) NOT NULL,
    [StartOn] date NOT NULL,
    [EndOn] date NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Campaigns] PRIMARY KEY ([Id])
);
CREATE TABLE [account].[ClientOperations] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [Kind] nvarchar(40) NOT NULL,
    [EntityId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_ClientOperations] PRIMARY KEY ([Id])
);
CREATE TABLE [account].[Contracts] (
    [Id] uniqueidentifier NOT NULL,
    [Kind] nvarchar(8) NOT NULL,
    [Number] nvarchar(32) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [StartOn] date NOT NULL,
    [EndOn] date NOT NULL,
    [Amount] decimal(18,2) NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [OpportunityId] uniqueidentifier NULL,
    [ServiceLevel] nvarchar(120) NULL,
    [Status] nvarchar(12) NOT NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [TerminationReason] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Contracts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Contracts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [account].[Notes] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [Visibility] nvarchar(12) NOT NULL,
    [AuthorUserId] uniqueidentifier NOT NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Notes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Notes_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_Notes_Users_AuthorUserId] FOREIGN KEY ([AuthorUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [account].[Participations] (
    [Id] uniqueidentifier NOT NULL,
    [Kind] nvarchar(16) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Role] nvarchar(80) NULL,
    [StartOn] date NOT NULL,
    [EndOn] date NULL,
    [Status] nvarchar(12) NOT NULL,
    [Notes] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Participations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Participations_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [account].[Payments] (
    [Id] uniqueidentifier NOT NULL,
    [Direction] nvarchar(8) NOT NULL,
    [Amount] decimal(18,2) NOT NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [PaidOn] date NOT NULL,
    [Method] nvarchar(16) NOT NULL,
    [Reference] nvarchar(80) NULL,
    [InvoiceReference] nvarchar(40) NULL,
    [Description] nvarchar(500) NULL,
    [Status] nvarchar(12) NOT NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [DecidedByUserId] uniqueidentifier NULL,
    [DecidedAtUtc] datetimeoffset(3) NULL,
    [DecisionNote] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Payments_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [account].[Projects] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(32) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [StartOn] date NOT NULL,
    [EndOn] date NULL,
    [ManagerUserId] uniqueidentifier NULL,
    [OpportunityId] uniqueidentifier NULL,
    [ContractId] uniqueidentifier NULL,
    [Budget] decimal(18,2) NULL,
    [CurrencyCode] nvarchar(3) NOT NULL,
    [Status] nvarchar(12) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Projects] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Projects_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [marketing].[Surveys] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Title] nvarchar(160) NOT NULL,
    [Kind] nvarchar(8) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Surveys] PRIMARY KEY ([Id])
);
CREATE TABLE [marketing].[TargetLists] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Description] nvarchar(500) NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_TargetLists] PRIMARY KEY ([Id])
);
CREATE TABLE [account].[ActivityParticipants] (
    [Id] uniqueidentifier NOT NULL,
    [ActivityId] uniqueidentifier NOT NULL,
    [Kind] nvarchar(8) NOT NULL,
    [ParticipantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_ActivityParticipants] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ActivityParticipants_Activities_ActivityId] FOREIGN KEY ([ActivityId]) REFERENCES [account].[Activities] ([Id])
);
CREATE TABLE [marketing].[CampaignMembers] (
    [Id] uniqueidentifier NOT NULL,
    [CampaignId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [ContactId] uniqueidentifier NULL,
    [Status] nvarchar(12) NOT NULL,
    [AddedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_CampaignMembers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CampaignMembers_Campaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [marketing].[Campaigns] ([Id]),
    CONSTRAINT [FK_CampaignMembers_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [account].[Documents] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [FileName] nvarchar(200) NOT NULL,
    [ContentType] nvarchar(120) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Sha256] nvarchar(64) NOT NULL,
    [IsSensitive] bit NOT NULL,
    [UploadedByUserId] uniqueidentifier NOT NULL,
    [NoteId] uniqueidentifier NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Documents_Notes_NoteId] FOREIGN KEY ([NoteId]) REFERENCES [account].[Notes] ([Id])
);
CREATE TABLE [account].[SurveyResponses] (
    [Id] uniqueidentifier NOT NULL,
    [SurveyId] uniqueidentifier NOT NULL,
    [ContactId] uniqueidentifier NULL,
    [Score] int NOT NULL,
    [Comment] nvarchar(1000) NULL,
    [RespondedOn] date NOT NULL,
    [RecordedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SurveyResponses] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SurveyResponses_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_SurveyResponses_Surveys_SurveyId] FOREIGN KEY ([SurveyId]) REFERENCES [marketing].[Surveys] ([Id])
);
CREATE TABLE [marketing].[TargetListMembers] (
    [Id] uniqueidentifier NOT NULL,
    [TargetListId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [AddedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_TargetListMembers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TargetListMembers_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_TargetListMembers_TargetLists_TargetListId] FOREIGN KEY ([TargetListId]) REFERENCES [marketing].[TargetLists] ([Id])
);
CREATE TABLE [account].[DocumentContents] (
    [Id] uniqueidentifier NOT NULL,
    [Bytes] varbinary(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DocumentContents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DocumentContents_Documents_Id] FOREIGN KEY ([Id]) REFERENCES [account].[Documents] ([Id])
);
CREATE TABLE [account].[DocumentLinks] (
    [Id] uniqueidentifier NOT NULL,
    [DocumentId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [LinkedByUserId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_DocumentLinks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DocumentLinks_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_DocumentLinks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [account].[Documents] ([Id])
);
EXEC(N'CREATE INDEX [IX_Opportunities_ContactId] ON [sales].[Opportunities] ([ContactId]);');
EXEC(N'CREATE INDEX [IX_DealerGuarantees_CustomerId_Status] ON [channel].[DealerGuarantees] ([CustomerId], [Status]);');
EXEC(N'CREATE INDEX [IX_Customers_ParentCustomerId] ON [crm].[Customers] ([ParentCustomerId]);');
CREATE INDEX [IX_Activities_ContactId] ON [account].[Activities] ([ContactId]);
CREATE INDEX [IX_Activities_CustomerId_Status_StartAtUtc] ON [account].[Activities] ([CustomerId], [Status], [StartAtUtc]);
CREATE INDEX [IX_Activities_OwnerUserId_Status_StartAtUtc] ON [account].[Activities] ([OwnerUserId], [Status], [StartAtUtc]);
CREATE INDEX [IX_Activities_RelatedKind_RelatedId] ON [account].[Activities] ([RelatedKind], [RelatedId]);
CREATE UNIQUE INDEX [IX_ActivityParticipants_ActivityId_Kind_ParticipantId] ON [account].[ActivityParticipants] ([ActivityId], [Kind], [ParticipantId]);
CREATE INDEX [IX_Allocations_CustomerId] ON [account].[Allocations] ([CustomerId]);
CREATE INDEX [IX_BankAccounts_CustomerId] ON [account].[BankAccounts] ([CustomerId]);
CREATE UNIQUE INDEX [IX_CampaignMembers_CampaignId_CustomerId] ON [marketing].[CampaignMembers] ([CampaignId], [CustomerId]);
CREATE INDEX [IX_CampaignMembers_CustomerId] ON [marketing].[CampaignMembers] ([CustomerId]);
CREATE INDEX [IX_Campaigns_CompanyId_Status] ON [marketing].[Campaigns] ([CompanyId], [Status]);
CREATE UNIQUE INDEX [IX_ClientOperations_UserId_OperationId] ON [account].[ClientOperations] ([UserId], [OperationId]);
CREATE UNIQUE INDEX [IX_Contracts_CompanyId_Number] ON [account].[Contracts] ([CompanyId], [Number]);
CREATE INDEX [IX_Contracts_CustomerId] ON [account].[Contracts] ([CustomerId]);
CREATE INDEX [IX_Contracts_OpportunityId] ON [account].[Contracts] ([OpportunityId]);
CREATE INDEX [IX_DocumentLinks_CustomerId] ON [account].[DocumentLinks] ([CustomerId]);
CREATE UNIQUE INDEX [IX_DocumentLinks_DocumentId_CustomerId] ON [account].[DocumentLinks] ([DocumentId], [CustomerId]);
CREATE INDEX [IX_Documents_CompanyId_IsDeleted_Title] ON [account].[Documents] ([CompanyId], [IsDeleted], [Title]);
CREATE INDEX [IX_Documents_NoteId] ON [account].[Documents] ([NoteId]);
CREATE INDEX [IX_Notes_AuthorUserId] ON [account].[Notes] ([AuthorUserId]);
CREATE INDEX [IX_Notes_CustomerId_IsDeleted_CreatedAtUtc] ON [account].[Notes] ([CustomerId], [IsDeleted], [CreatedAtUtc]);
CREATE INDEX [IX_Participations_CustomerId] ON [account].[Participations] ([CustomerId]);
CREATE INDEX [IX_Payments_CustomerId] ON [account].[Payments] ([CustomerId]);
CREATE INDEX [IX_Payments_CustomerId_Status_CurrencyCode] ON [account].[Payments] ([CustomerId], [Status], [CurrencyCode]);
CREATE UNIQUE INDEX [IX_Projects_CompanyId_Code] ON [account].[Projects] ([CompanyId], [Code]);
CREATE INDEX [IX_Projects_CustomerId] ON [account].[Projects] ([CustomerId]);
CREATE INDEX [IX_SurveyResponses_CustomerId] ON [account].[SurveyResponses] ([CustomerId]);
CREATE INDEX [IX_SurveyResponses_SurveyId] ON [account].[SurveyResponses] ([SurveyId]);
CREATE INDEX [IX_TargetListMembers_CustomerId] ON [marketing].[TargetListMembers] ([CustomerId]);
CREATE UNIQUE INDEX [IX_TargetListMembers_TargetListId_CustomerId] ON [marketing].[TargetListMembers] ([TargetListId], [CustomerId]);
EXEC(N'ALTER TABLE [crm].[Customers] ADD CONSTRAINT [FK_Customers_Customers_ParentCustomerId] FOREIGN KEY ([ParentCustomerId]) REFERENCES [crm].[Customers] ([Id]);');
EXEC(N'ALTER TABLE [channel].[DealerGuarantees] ADD CONSTRAINT [FK_DealerGuarantees_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]);');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '509ee20f-4a53-5ad3-97d9-516f005009a2',N'SalesManager',N'Account.Guarantee.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Account.Guarantee.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fab13ec0-182d-559b-8b5c-e62e87f5deea',N'SalesManager',N'Account.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Account.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '767c5437-05e6-5b93-b939-9a4c119cf2f8',N'SalesManager',N'Account.Relation.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Account.Relation.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'b63cc367-906b-5a61-8164-af0c881066ba',N'SalesManager',N'Activity.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Activity.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'c0fbbc19-b907-547a-8011-36a8323a9d62',N'SalesManager',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '333c79cb-7350-5184-93cb-3f902c199ea7',N'SalesManager',N'Activity.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Activity.Update');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '48421b4d-efd1-5f31-85ac-8c1f14b14451',N'SalesManager',N'BankAccount.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'BankAccount.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '2f77680f-0c7b-5a62-888a-f147f99ff80e',N'SalesManager',N'BankAccount.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'BankAccount.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'dddcfef6-bb9b-5b53-865a-4c359b58f719',N'SalesManager',N'Campaign.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Campaign.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a2fb7156-17b7-5520-b141-8a78bcbe64ac',N'SalesManager',N'Campaign.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Campaign.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '388e4def-0a5a-581c-9f49-e3b7b2c4c2e8',N'SalesManager',N'Contract.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Contract.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '409df6ea-a991-57c9-b3fd-d758198e1c85',N'SalesManager',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd6441dc1-667b-506a-a9db-c746887c8713',N'SalesManager',N'Customer.Status.Change',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Customer.Status.Change');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fd704d9b-c9e4-5e5e-b910-93a4ec683802',N'SalesManager',N'Document.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Document.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ea22cc47-4b40-59d0-b1b2-e701d2451448',N'SalesManager',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '358fdf3e-c185-566d-90cb-383193a0d22e',N'SalesManager',N'Document.Sensitive.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Document.Sensitive.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7276e17e-76b1-54f2-ab0f-0950fd1af84f',N'SalesManager',N'Note.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Note.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '3f031986-5dc6-5816-8bec-734dc265a796',N'SalesManager',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ed95c958-7e33-5a37-bdd3-1a78d9503825',N'SalesManager',N'Note.Restricted.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Note.Restricted.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '571c651a-e1a4-54db-891e-b47530aa2ac4',N'SalesManager',N'Payment.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Payment.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '1dfbf3c0-0f24-5a62-b1f4-ccec542d4201',N'SalesManager',N'Payment.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Payment.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f209e172-173a-5236-a3de-1d4b95b50e2f',N'SalesManager',N'Payment.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Payment.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '1b6f782b-e4a6-5ae3-b106-e72ea7f15d61',N'SalesManager',N'Project.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Project.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'eaa009f9-875e-5d8f-b566-1ed883e82edc',N'SalesManager',N'Project.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Project.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '86117121-4249-5be6-8bc1-61994ebf9b90',N'SalesManager',N'Survey.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Survey.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '4d41465d-721b-5f09-946f-20e100702848',N'SalesManager',N'Survey.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'Survey.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd0c97602-e002-59cd-b15f-033eb04c432d',N'SalesSupervisor',N'Account.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Account.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f8ba2c1f-8525-547a-8480-d5d7311366db',N'SalesSupervisor',N'Account.Relation.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Account.Relation.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fd6a4f28-b808-52dc-8410-b42b91d1791e',N'SalesSupervisor',N'Activity.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Activity.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'b6eaf192-d03b-570a-86c1-3bcca3e76e01',N'SalesSupervisor',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '808f1708-da4e-5fff-99f3-44e7fe0fadb9',N'SalesSupervisor',N'Activity.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Activity.Update');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '98cb19dd-04d4-5e4a-952c-959aee75aa7e',N'SalesSupervisor',N'BankAccount.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'BankAccount.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '1de760e0-5d26-584e-8aea-dfa47706882c',N'SalesSupervisor',N'Campaign.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Campaign.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '162e1934-fab8-5e4a-8cf4-9eb125ad9a2a',N'SalesSupervisor',N'Campaign.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Campaign.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cfa4376d-b186-57ea-a8e9-64af9967561c',N'SalesSupervisor',N'Contract.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Contract.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'af0c0db9-994d-5834-8e8f-b37b68570eda',N'SalesSupervisor',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '8aecd47b-3677-5942-b4ba-0c56d56a0d05',N'SalesSupervisor',N'Customer.Status.Change',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Customer.Status.Change');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a1cdde52-3580-58fd-bbe0-928476ca440e',N'SalesSupervisor',N'Document.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Document.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'db35aea6-4bec-5fe4-b298-9e79d5456d1d',N'SalesSupervisor',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd02feef2-5d8e-523c-bfae-d3a048ba1542',N'SalesSupervisor',N'Document.Sensitive.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Document.Sensitive.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f8358a40-8dca-5621-89f5-3f3f479bed64',N'SalesSupervisor',N'Note.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Note.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '96d4ca14-c641-5e72-aed1-ef1a1779603d',N'SalesSupervisor',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'efbe4830-8512-53a6-91d7-47e7e0151359',N'SalesSupervisor',N'Note.Restricted.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Note.Restricted.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '073cdbee-7bdb-5fae-a634-cd491ec0105e',N'SalesSupervisor',N'Payment.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Payment.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f67754aa-75b5-53cf-88ff-a939029b9aff',N'SalesSupervisor',N'Payment.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Payment.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '0e1a3a97-1eab-591f-a995-2e11a2c1b7ad',N'SalesSupervisor',N'Project.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Project.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '61c4091d-4405-59e0-a4b9-34787d50ed94',N'SalesSupervisor',N'Project.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Project.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'bc2dee08-10fa-5108-88bb-f881a7ed0393',N'SalesSupervisor',N'Survey.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Survey.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '30ad2eb8-d52e-5932-aa78-7abfe9c04d10',N'SalesSupervisor',N'Survey.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'Survey.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'c263ae8c-c277-5eca-930a-cb360f77352e',N'SalesExpert',N'Account.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Account.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cb8e7e6a-9e06-5273-8e91-5294f611d7e8',N'SalesExpert',N'Account.Relation.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Account.Relation.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'aec6c88e-ad1d-5292-9f20-8b762fd3b902',N'SalesExpert',N'Activity.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Activity.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cba5a41e-ee12-5b0b-874e-77c6148b1900',N'SalesExpert',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7ad24b73-28ab-5e33-abc7-ca6af374c0e7',N'SalesExpert',N'BankAccount.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'BankAccount.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'e6b81d8a-062f-50c5-ab1a-42b09f2fb5c6',N'SalesExpert',N'Campaign.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Campaign.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7caa9e6d-a217-5bb2-9ace-3938ca3bc119',N'SalesExpert',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd2d89844-8e45-5bc3-96e9-a77c067f1032',N'SalesExpert',N'Document.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Document.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f150c4f3-8466-5c5e-abd6-c132c69af43c',N'SalesExpert',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '0a2564a9-7c8c-59d8-b9d6-97c016e0f024',N'SalesExpert',N'Note.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Note.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ce652c89-873a-55c6-85cb-99f7891f060a',N'SalesExpert',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '5164d45d-6f51-50a2-ac63-deeb83070402',N'SalesExpert',N'Payment.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Payment.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '440ce5d9-92b3-55c7-9ae8-52cba673270b',N'SalesExpert',N'Payment.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Payment.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ac9a8b06-6f4f-595c-a979-f9e10157745e',N'SalesExpert',N'Project.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Project.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '594b289f-f494-5fdc-9652-efd7dc76ea90',N'SalesExpert',N'Survey.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Survey.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7ec94125-2944-58a7-a89d-dcd60e20e3a6',N'SalesExpert',N'Survey.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'Survey.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd1faa67b-d5ae-5b41-af74-bd56d97bb8b0',N'FinanceManager',N'Account.Guarantee.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Account.Guarantee.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '8a257c1d-2ce3-50d8-bf38-ea86694fc9ce',N'FinanceManager',N'Account.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Account.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'c6cdf007-4956-5787-8146-6fd2d4b3fd38',N'FinanceManager',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'eb648418-6846-5e26-b8d0-d53dc01d449e',N'FinanceManager',N'BankAccount.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'BankAccount.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '397e9384-eda2-5bca-9734-4efdd0c32c16',N'FinanceManager',N'BankAccount.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'BankAccount.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'e5cc9b5f-2279-5131-b9d9-0be3cca60227',N'FinanceManager',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '421040f5-81ef-5a8b-9198-8363874113c0',N'FinanceManager',N'Document.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Document.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'bc52b554-a1f5-5b94-90ca-c1c38143463a',N'FinanceManager',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a52ddcaf-6de0-5c26-b2ad-31befcc846d4',N'FinanceManager',N'Document.Sensitive.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Document.Sensitive.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '479d9a22-4fce-5f64-8b57-5fd17d3d4726',N'FinanceManager',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '482eb6ef-0efa-5534-9e59-357a79768c87',N'FinanceManager',N'Payment.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Payment.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f9f50f46-a3f1-5abc-aed4-c2286fd1163c',N'FinanceManager',N'Payment.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Payment.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'bbc97e2d-f23c-50a7-aa6b-22aa4df5c943',N'FinanceManager',N'Payment.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Payment.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a22ac5e9-19cc-503f-84db-561ff54b65f3',N'FinanceManager',N'Project.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'Project.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '2ce5ee81-2ecc-5c74-acaa-74c9dd8f8e47',N'ChannelManager',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'b9093bf9-71e8-5bd9-a0f3-1b290f25f81e',N'ChannelManager',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ChannelManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ChannelManager' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cd76def3-0a2b-5881-b526-e8ce5842645d',N'Executive',N'Account.Guarantee.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Account.Guarantee.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '3d9194b9-a103-5f10-a074-7bc69ad3e65f',N'Executive',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7eff9241-d20d-53f9-ad75-6580e9a14ea3',N'Executive',N'BankAccount.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'BankAccount.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f32ae5bf-1469-52e5-8bb7-b5c9f6592566',N'Executive',N'Campaign.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Campaign.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f13a06de-a719-54ad-8063-b78efc596e8b',N'Executive',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'e00583d1-5015-503e-bda8-97ddc2733f0f',N'Executive',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '2c34e619-76cf-589e-a406-dcf61cd375f1',N'Executive',N'Document.Sensitive.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Document.Sensitive.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '84949724-4173-5baa-b846-2c06779e70fc',N'Executive',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'bd5f163e-9a8e-5f05-a276-d16b1b62508c',N'Executive',N'Note.Restricted.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Note.Restricted.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '5bc6f291-4b52-57f3-b210-59c4f649100a',N'Executive',N'Payment.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Payment.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a967f789-c8c0-5a7f-9b45-af24b07860b5',N'Executive',N'Project.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Project.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'd3f29c92-789c-5cac-ac9b-622f9b5e1bcf',N'Executive',N'Survey.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'Survey.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'a815a766-db18-5464-a566-1528c7a14e2c',N'ServiceAgent',N'Activity.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Activity.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7bad77ea-ee3c-567a-9280-2728928b6315',N'ServiceAgent',N'Activity.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Activity.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'dc088851-fccb-5590-8b52-029601307bfe',N'ServiceAgent',N'Contract.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Contract.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ccf1f78a-10db-50a6-9a43-08333c634120',N'ServiceAgent',N'Document.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Document.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '64e6d25c-7bed-5026-b79a-e69a1cd08bc2',N'ServiceAgent',N'Document.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Document.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'ae61b1a4-94f5-522d-9dc2-42436ea47dc7',N'ServiceAgent',N'Note.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Note.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fdda768d-3cc9-5644-8a13-f99827a72267',N'ServiceAgent',N'Note.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Note.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '23997046-3270-58d8-8b82-5c61bc6af84b',N'ServiceAgent',N'Survey.Manage',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Survey.Manage');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '7f569a2c-13f4-5fb6-b5dc-1d5c3b51e4a0',N'ServiceAgent',N'Survey.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'Survey.Read');
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DELETE FROM [iam].[RolePermissionGrants] WHERE [Permission] LIKE N'Activity.%' OR [Permission] LIKE N'Note.%' OR [Permission] LIKE N'Document.%' OR [Permission] LIKE N'Payment.%' OR [Permission] LIKE N'BankAccount.%' OR [Permission] LIKE N'Account.%' OR [Permission] LIKE N'Contract.%' OR [Permission] LIKE N'Project.%' OR [Permission] LIKE N'Campaign.%' OR [Permission] LIKE N'Survey.%' OR [Permission] = N'Customer.Status.Change';
DROP TABLE [account].[DocumentLinks];
DROP TABLE [account].[DocumentContents];
DROP TABLE [account].[Documents];
DROP TABLE [account].[ActivityParticipants];
DROP TABLE [account].[Activities];
DROP TABLE [account].[Notes];
DROP TABLE [account].[SurveyResponses];
DROP TABLE [marketing].[CampaignMembers];
DROP TABLE [marketing].[TargetListMembers];
DROP TABLE [marketing].[Campaigns];
DROP TABLE [marketing].[TargetLists];
DROP TABLE [marketing].[Surveys];
DROP TABLE [account].[ClientOperations];
DROP TABLE [account].[Payments];
DROP TABLE [account].[BankAccounts];
DROP TABLE [account].[Contracts];
DROP TABLE [account].[Projects];
DROP TABLE [account].[Participations];
DROP TABLE [account].[Allocations];
ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT [FK_DealerGuarantees_Customers_CustomerId];
DROP INDEX [IX_DealerGuarantees_CustomerId_Status] ON [channel].[DealerGuarantees];
DELETE FROM [channel].[DealerGuarantees] WHERE [DealerId] IS NULL;
ALTER TABLE [channel].[DealerGuarantees] DROP COLUMN [CustomerId];
DROP INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees];
ALTER TABLE [channel].[DealerGuarantees] DROP CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId];
ALTER TABLE [channel].[DealerGuarantees] ALTER COLUMN [DealerId] uniqueidentifier NOT NULL;
ALTER TABLE [channel].[DealerGuarantees] ADD CONSTRAINT [FK_DealerGuarantees_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers] ([Id]);
CREATE INDEX [IX_DealerGuarantees_DealerId_Status] ON [channel].[DealerGuarantees] ([DealerId], [Status]);
ALTER TABLE [crm].[Customers] DROP CONSTRAINT [FK_Customers_Customers_ParentCustomerId];
DROP INDEX [IX_Customers_ParentCustomerId] ON [crm].[Customers];
ALTER TABLE [crm].[Customers] DROP COLUMN [ParentCustomerId];
DECLARE @dc nvarchar(max);
SELECT @dc = QUOTENAME([d].[name]) FROM [sys].[default_constraints] [d] INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id] WHERE [d].[parent_object_id] = OBJECT_ID(N'[crm].[Customers]') AND [c].[name] = N'RelationshipType';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [crm].[Customers] DROP CONSTRAINT ' + @dc + ';');
ALTER TABLE [crm].[Customers] DROP COLUMN [RelationshipType], [Tags];
DROP INDEX [IX_Opportunities_ContactId] ON [sales].[Opportunities];
SET @dc = NULL;
SELECT @dc = QUOTENAME([d].[name]) FROM [sys].[default_constraints] [d] INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id] WHERE [d].[parent_object_id] = OBJECT_ID(N'[sales].[Opportunities]') AND [c].[name] = N'CurrencyCode';
IF @dc IS NOT NULL EXEC(N'ALTER TABLE [sales].[Opportunities] DROP CONSTRAINT ' + @dc + ';');
ALTER TABLE [sales].[Opportunities] DROP COLUMN [ContactId], [CurrencyCode];
IF SCHEMA_ID(N'account') IS NOT NULL EXEC(N'DROP SCHEMA [account];');
IF SCHEMA_ID(N'marketing') IS NOT NULL EXEC(N'DROP SCHEMA [marketing];');
""");
}
