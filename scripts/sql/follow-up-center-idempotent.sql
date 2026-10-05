-- Generated from 202610040018_FollowUpCenter.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030017_AccountFile') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610040018_FollowUpCenter')
BEGIN
IF SCHEMA_ID(N'followup') IS NULL EXEC(N'CREATE SCHEMA [followup];');
CREATE TABLE [followup].[Queues] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Country] nvarchar(60) NOT NULL,
    [BranchId] nvarchar(32) NULL,
    [CaseType] nvarchar(40) NULL,
    [PartFamily] nvarchar(80) NULL,
    [Language] nvarchar(16) NULL,
    [Method] nvarchar(20) NOT NULL,
    [Ordering] nvarchar(20) NOT NULL,
    [OverflowQueueId] uniqueidentifier NULL,
    [NotifySupervisorOnOverflow] bit NOT NULL,
    [RuleOrder] int NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Queues] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Queues_Queues_OverflowQueueId] FOREIGN KEY ([OverflowQueueId]) REFERENCES [followup].[Queues] ([Id])
);
CREATE TABLE [followup].[SavedViews] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(80) NOT NULL,
    [Query] nvarchar(500) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_SavedViews] PRIMARY KEY ([Id])
);
CREATE TABLE [followup].[SlaPolicies] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Priority] nvarchar(12) NOT NULL,
    [CaseType] nvarchar(40) NULL,
    [TimeZoneId] nvarchar(64) NOT NULL,
    [WorkDays] nvarchar(100) NOT NULL,
    [WorkStart] nvarchar(5) NOT NULL,
    [WorkEnd] nvarchar(5) NOT NULL,
    [Holidays] nvarchar(2000) NULL,
    [FirstResponseHours] int NOT NULL,
    [StageHours] int NOT NULL,
    [ResolutionHours] int NOT NULL,
    [PauseOnWaitingCustomer] bit NOT NULL,
    [PauseOnWaitingInternal] bit NOT NULL,
    [Escalations] nvarchar(400) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_SlaPolicies] PRIMARY KEY ([Id])
);
CREATE TABLE [followup].[QueueMembers] (
    [Id] uniqueidentifier NOT NULL,
    [QueueId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Capacity] int NOT NULL,
    [IsAvailable] bit NOT NULL,
    [AvailabilityNote] nvarchar(200) NULL,
    [Skills] nvarchar(200) NULL,
    [LastAssignedAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_QueueMembers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_QueueMembers_Queues_QueueId] FOREIGN KEY ([QueueId]) REFERENCES [followup].[Queues] ([Id]),
    CONSTRAINT [FK_QueueMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [followup].[Templates] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Code] nvarchar(40) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [CaseType] nvarchar(40) NOT NULL,
    [TemplateVersion] int NOT NULL,
    [Status] nvarchar(12) NOT NULL,
    [Scope] nvarchar(32) NOT NULL,
    [OwnerUnit] nvarchar(120) NOT NULL,
    [Description] nvarchar(1000) NULL,
    [ClosingCriteria] nvarchar(500) NULL,
    [RequireAllRequiredStages] bit NOT NULL,
    [RequireCustomerApproval] bit NOT NULL,
    [Rules] nvarchar(2000) NULL,
    [SlaPolicyId] uniqueidentifier NULL,
    [DefaultPriority] nvarchar(12) NOT NULL,
    [PublishedAtUtc] datetimeoffset(3) NULL,
    [PublishedByUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Templates] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Templates_SlaPolicies_SlaPolicyId] FOREIGN KEY ([SlaPolicyId]) REFERENCES [followup].[SlaPolicies] ([Id])
);
CREATE TABLE [followup].[Cases] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(24) NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [BranchId] nvarchar(32) NOT NULL,
    [TerritoryId] nvarchar(32) NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [ContactId] uniqueidentifier NULL,
    [DealerId] uniqueidentifier NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Description] nvarchar(4000) NULL,
    [ExpectedOutcome] nvarchar(500) NULL,
    [CaseType] nvarchar(40) NOT NULL,
    [ExtraFields] nvarchar(2000) NULL,
    [TemplateId] uniqueidentifier NOT NULL,
    [TemplateVersion] int NOT NULL,
    [Priority] nvarchar(12) NOT NULL,
    [PriorityReason] nvarchar(300) NULL,
    [Channel] nvarchar(12) NOT NULL,
    [Language] nvarchar(16) NOT NULL,
    [PartFamily] nvarchar(80) NULL,
    [RelatedKind] nvarchar(16) NOT NULL,
    [RelatedId] uniqueidentifier NULL,
    [RelatedCode] nvarchar(40) NULL,
    [QueueId] uniqueidentifier NULL,
    [OwnerUserId] uniqueidentifier NULL,
    [CreatedByUserId] uniqueidentifier NOT NULL,
    [OpenedAtUtc] datetimeoffset(3) NOT NULL,
    [Status] nvarchar(24) NOT NULL,
    [ProgressPercent] int NOT NULL,
    [SlaPolicyId] uniqueidentifier NULL,
    [FirstResponseDueAtUtc] datetimeoffset(3) NULL,
    [FirstRespondedAtUtc] datetimeoffset(3) NULL,
    [ResolutionDueAtUtc] datetimeoffset(3) NULL,
    [PausedSinceUtc] datetimeoffset(3) NULL,
    [PausedMinutes] int NOT NULL,
    [EscalationLevel] int NOT NULL,
    [NextAction] nvarchar(200) NULL,
    [NextActionAtUtc] datetimeoffset(3) NULL,
    [NextActionOwnerUserId] uniqueidentifier NULL,
    [ReviewAtUtc] datetimeoffset(3) NULL,
    [WaitReason] nvarchar(500) NULL,
    [WaitingOn] nvarchar(120) NULL,
    [WaitStageId] uniqueidentifier NULL,
    [Outcome] nvarchar(200) NULL,
    [OutcomeNote] nvarchar(2000) NULL,
    [ClosedAtUtc] datetimeoffset(3) NULL,
    [ClosedByUserId] uniqueidentifier NULL,
    [ReopenCount] int NOT NULL,
    [CancelReason] nvarchar(500) NULL,
    [MergedIntoCaseId] uniqueidentifier NULL,
    [ParentCaseId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Cases] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Cases_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers] ([Id]),
    CONSTRAINT [FK_Cases_Templates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [followup].[Templates] ([Id]),
    CONSTRAINT [FK_Cases_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [followup].[TemplateStages] (
    [Id] uniqueidentifier NOT NULL,
    [TemplateId] uniqueidentifier NOT NULL,
    [Order] int NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Weight] int NOT NULL,
    [ResponsibleRole] nvarchar(60) NOT NULL,
    [Required] bit NOT NULL,
    [Checklist] nvarchar(2000) NULL,
    [Condition] nvarchar(200) NULL,
    [ParallelWithPrevious] bit NOT NULL,
    [DurationHours] int NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_TemplateStages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TemplateStages_Templates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [followup].[Templates] ([Id])
);
CREATE TABLE [followup].[ActivityLinks] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [StageId] uniqueidentifier NULL,
    [Channel] nvarchar(12) NOT NULL,
    [PreChecklist] nvarchar(1000) NULL,
    [ResultCode] nvarchar(32) NULL,
    [StageCompletionRequested] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_ActivityLinks] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ActivityLinks_Activities_Id] FOREIGN KEY ([Id]) REFERENCES [account].[Activities] ([Id]),
    CONSTRAINT [FK_ActivityLinks_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id])
);
CREATE TABLE [followup].[Approvals] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [StageId] uniqueidentifier NULL,
    [RequestedByUserId] uniqueidentifier NOT NULL,
    [ApproverUserId] uniqueidentifier NOT NULL,
    [ApproverRole] nvarchar(60) NOT NULL,
    [ReviewItems] nvarchar(1000) NULL,
    [PassedItems] nvarchar(1000) NULL,
    [RequestedAtUtc] datetimeoffset(3) NOT NULL,
    [Decision] nvarchar(16) NULL,
    [DecisionNote] nvarchar(1000) NULL,
    [CorrectionOwnerUserId] uniqueidentifier NULL,
    [CorrectionDueAtUtc] datetimeoffset(3) NULL,
    [DecidedByUserId] uniqueidentifier NULL,
    [DecidedAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Approvals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Approvals_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id])
);
CREATE TABLE [followup].[Documents] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [StageId] uniqueidentifier NULL,
    [Kind] nvarchar(20) NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [DocumentVersion] int NOT NULL,
    [DocumentId] uniqueidentifier NOT NULL,
    [UploadedByUserId] uniqueidentifier NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [ReviewedByUserId] uniqueidentifier NULL,
    [ReviewedAtUtc] datetimeoffset(3) NULL,
    [ReviewNote] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Documents_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id]),
    CONSTRAINT [FK_Documents_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [account].[Documents] ([Id])
);
CREATE TABLE [followup].[Events] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Kind] nvarchar(32) NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Detail] nvarchar(2000) NULL,
    [ActorUserId] uniqueidentifier NULL,
    [AtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Events_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id])
);
CREATE TABLE [followup].[Items] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [PartCode] nvarchar(60) NOT NULL,
    [AlternateCode] nvarchar(60) NULL,
    [Description] nvarchar(200) NULL,
    [Compatibility] nvarchar(200) NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [Unit] nvarchar(20) NOT NULL,
    [Warehouse] nvarchar(60) NULL,
    [SerialOrBatch] nvarchar(60) NULL,
    [Status] nvarchar(12) NOT NULL,
    [DeliveredQuantity] decimal(18,3) NOT NULL,
    [Note] nvarchar(300) NULL,
    [IsRemoved] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Items_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id])
);
CREATE TABLE [followup].[Referrals] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Scope] nvarchar(8) NOT NULL,
    [StageId] uniqueidentifier NULL,
    [FromUserId] uniqueidentifier NOT NULL,
    [ToUserId] uniqueidentifier NOT NULL,
    [ToBranchId] nvarchar(32) NULL,
    [ToTeam] nvarchar(120) NULL,
    [Reason] nvarchar(500) NOT NULL,
    [SentAtUtc] datetimeoffset(3) NOT NULL,
    [AcceptDueAtUtc] datetimeoffset(3) NOT NULL,
    [IncludeHistory] bit NOT NULL,
    [IncludeQuote] bit NOT NULL,
    [IncludeTechnical] bit NOT NULL,
    [Status] nvarchar(12) NOT NULL,
    [RespondedAtUtc] datetimeoffset(3) NULL,
    [ResponseNote] nvarchar(500) NULL,
    [LateReminderSent] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Referrals] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Referrals_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id]),
    CONSTRAINT [FK_Referrals_Users_ToUserId] FOREIGN KEY ([ToUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [followup].[Stages] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [Order] int NOT NULL,
    [Name] nvarchar(120) NOT NULL,
    [Weight] int NOT NULL,
    [Required] bit NOT NULL,
    [ResponsibleRole] nvarchar(60) NOT NULL,
    [ResponsibleUserId] uniqueidentifier NULL,
    [Status] nvarchar(12) NOT NULL,
    [ParallelWithPrevious] bit NOT NULL,
    [Condition] nvarchar(200) NULL,
    [DurationHours] int NULL,
    [Progress] int NOT NULL,
    [DueAtUtc] datetimeoffset(3) NULL,
    [StartedAtUtc] datetimeoffset(3) NULL,
    [CompletedAtUtc] datetimeoffset(3) NULL,
    [CompletedByUserId] uniqueidentifier NULL,
    [ReturnReason] nvarchar(500) NULL,
    [SkipReason] nvarchar(500) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Stages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Stages_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [followup].[Cases] ([Id])
);
CREATE TABLE [followup].[ChecklistItems] (
    [Id] uniqueidentifier NOT NULL,
    [CaseId] uniqueidentifier NOT NULL,
    [StageId] uniqueidentifier NOT NULL,
    [Order] int NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [IsDone] bit NOT NULL,
    [DoneByUserId] uniqueidentifier NULL,
    [DoneAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_ChecklistItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ChecklistItems_Stages_StageId] FOREIGN KEY ([StageId]) REFERENCES [followup].[Stages] ([Id])
);
CREATE INDEX [IX_ActivityLinks_CaseId_StageId] ON [followup].[ActivityLinks] ([CaseId], [StageId]);
CREATE INDEX [IX_Approvals_ApproverUserId_Decision] ON [followup].[Approvals] ([ApproverUserId], [Decision]);
CREATE INDEX [IX_Approvals_CaseId] ON [followup].[Approvals] ([CaseId]);
CREATE UNIQUE INDEX [IX_Cases_CompanyId_Code] ON [followup].[Cases] ([CompanyId], [Code]);
CREATE INDEX [IX_Cases_CompanyId_Status_NextActionAtUtc] ON [followup].[Cases] ([CompanyId], [Status], [NextActionAtUtc]);
CREATE INDEX [IX_Cases_CustomerId_Status] ON [followup].[Cases] ([CustomerId], [Status]);
CREATE INDEX [IX_Cases_OwnerUserId_Status] ON [followup].[Cases] ([OwnerUserId], [Status]);
CREATE INDEX [IX_Cases_QueueId] ON [followup].[Cases] ([QueueId]);
CREATE INDEX [IX_Cases_RelatedKind_RelatedId] ON [followup].[Cases] ([RelatedKind], [RelatedId]);
CREATE INDEX [IX_Cases_TemplateId] ON [followup].[Cases] ([TemplateId]);
CREATE INDEX [IX_ChecklistItems_CaseId_StageId_Order] ON [followup].[ChecklistItems] ([CaseId], [StageId], [Order]);
CREATE INDEX [IX_ChecklistItems_StageId] ON [followup].[ChecklistItems] ([StageId]);
CREATE INDEX [IX_Documents_CaseId_Kind] ON [followup].[Documents] ([CaseId], [Kind]);
CREATE INDEX [IX_Documents_DocumentId] ON [followup].[Documents] ([DocumentId]);
CREATE INDEX [IX_Events_CaseId_AtUtc] ON [followup].[Events] ([CaseId], [AtUtc]);
CREATE INDEX [IX_Items_CaseId] ON [followup].[Items] ([CaseId]);
CREATE UNIQUE INDEX [IX_QueueMembers_QueueId_UserId] ON [followup].[QueueMembers] ([QueueId], [UserId]);
CREATE INDEX [IX_QueueMembers_UserId] ON [followup].[QueueMembers] ([UserId]);
CREATE INDEX [IX_Queues_CompanyId_IsActive_RuleOrder] ON [followup].[Queues] ([CompanyId], [IsActive], [RuleOrder]);
CREATE INDEX [IX_Queues_OverflowQueueId] ON [followup].[Queues] ([OverflowQueueId]);
CREATE INDEX [IX_Referrals_CaseId_Status] ON [followup].[Referrals] ([CaseId], [Status]);
CREATE INDEX [IX_Referrals_ToUserId_Status] ON [followup].[Referrals] ([ToUserId], [Status]);
CREATE UNIQUE INDEX [IX_SavedViews_UserId_CompanyId_Name] ON [followup].[SavedViews] ([UserId], [CompanyId], [Name]);
CREATE INDEX [IX_SlaPolicies_CompanyId_IsActive_Priority] ON [followup].[SlaPolicies] ([CompanyId], [IsActive], [Priority]);
CREATE INDEX [IX_Stages_CaseId_Order] ON [followup].[Stages] ([CaseId], [Order]);
CREATE INDEX [IX_Stages_ResponsibleUserId_Status] ON [followup].[Stages] ([ResponsibleUserId], [Status]);
CREATE INDEX [IX_Templates_CompanyId_CaseType_Status] ON [followup].[Templates] ([CompanyId], [CaseType], [Status]);
CREATE UNIQUE INDEX [IX_Templates_CompanyId_Code_TemplateVersion] ON [followup].[Templates] ([CompanyId], [Code], [TemplateVersion]);
CREATE INDEX [IX_Templates_SlaPolicyId] ON [followup].[Templates] ([SlaPolicyId]);
CREATE INDEX [IX_TemplateStages_TemplateId_Order] ON [followup].[TemplateStages] ([TemplateId], [Order]);
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '79567e26-dac7-5c10-95b0-032beb962a73',N'SalesManager',N'FollowUp.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'fe51962a-8ac0-536b-80e9-05f080b5b4c6',N'SalesManager',N'FollowUp.Assign',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Assign');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'edce9285-ac09-522a-84c1-2aebcc2a7304',N'SalesManager',N'FollowUp.Close',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Close');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'db886103-cfb9-53ce-8300-d96763e207ae',N'SalesManager',N'FollowUp.Configure',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Configure');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '4abecf39-db76-5cfb-b44c-9eac16fa4bf7',N'SalesManager',N'FollowUp.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'eb83fe8a-1cc1-5c22-8467-27e6013dd5c0',N'SalesManager',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '3532b05b-4cb0-5185-b571-58d808e47bca',N'SalesManager',N'FollowUp.Reopen',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Reopen');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'e6f719ac-e500-59ea-91d4-a9ab2c2bc713',N'SalesManager',N'FollowUp.Supervise',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Supervise');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '562d9740-7cb4-5535-92a1-ab8184592cde',N'SalesManager',N'FollowUp.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesManager' AND [Permission] = N'FollowUp.Update');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '904fb615-4d08-5183-bdef-619fe2f6d1bd',N'SalesSupervisor',N'FollowUp.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '4ceeb964-738d-5786-b0fd-51513ffb37d0',N'SalesSupervisor',N'FollowUp.Assign',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Assign');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'bc3890ac-e2b9-5349-97f9-2c58f144ac36',N'SalesSupervisor',N'FollowUp.Close',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Close');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '3e0dd49d-1120-5529-b5b7-e915d74a2353',N'SalesSupervisor',N'FollowUp.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '1217828b-5bef-5bf5-85f9-412ed4d61414',N'SalesSupervisor',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '4dbc697d-ad19-50e1-8852-c4e19cc4c58f',N'SalesSupervisor',N'FollowUp.Reopen',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Reopen');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f0d71908-c815-5209-872d-9fbbba82f4d2',N'SalesSupervisor',N'FollowUp.Supervise',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Supervise');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'acf46b7d-bf0b-5e6c-bf21-83e5d4dd33c4',N'SalesSupervisor',N'FollowUp.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesSupervisor') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesSupervisor' AND [Permission] = N'FollowUp.Update');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '85005c09-b62d-58d4-9046-0352e44493f9',N'SalesExpert',N'FollowUp.Close',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'FollowUp.Close');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'e2ea4005-f64f-5052-8a7e-01729a107dfb',N'SalesExpert',N'FollowUp.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'FollowUp.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cc4c79ec-9335-59b0-a3c8-bf8568f8ac30',N'SalesExpert',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'cbb4d1ee-f2c0-50ec-a891-64014dfd0ac6',N'SalesExpert',N'FollowUp.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'SalesExpert') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'SalesExpert' AND [Permission] = N'FollowUp.Update');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f5ba7c67-7893-5e34-8bf1-f5c4ea89b198',N'FinanceManager',N'FollowUp.Approve',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'FollowUp.Approve');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '40565928-9d85-508e-b918-dea5dfeb447b',N'FinanceManager',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'FinanceManager') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'FinanceManager' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '0929ec34-7b20-522b-8f15-67c070b2bf06',N'Executive',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'de6827dc-747c-5952-bce9-259347711797',N'Executive',N'FollowUp.Supervise',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'Executive') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'Executive' AND [Permission] = N'FollowUp.Supervise');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT 'f91f8a51-e390-5328-8557-6875a307afc3',N'ServiceAgent',N'FollowUp.Close',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'FollowUp.Close');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '62be1766-a88e-5b46-9913-237a43fbca11',N'ServiceAgent',N'FollowUp.Create',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'FollowUp.Create');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '519a050b-7992-5074-b824-0fa1fcc5214f',N'ServiceAgent',N'FollowUp.Read',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'FollowUp.Read');
INSERT INTO [iam].[RolePermissionGrants] ([Id],[RoleKey],[Permission],[CreatedAtUtc],[UpdatedAtUtc],[Version]) SELECT '1f25b612-3d96-5264-900e-fccf2b5556c4',N'ServiceAgent',N'FollowUp.Update',SYSUTCDATETIME(),SYSUTCDATETIME(),1 WHERE EXISTS (SELECT 1 FROM [iam].[RoleDefinitions] WHERE [RoleKey] = N'ServiceAgent') AND NOT EXISTS (SELECT 1 FROM [iam].[RolePermissionGrants] WHERE [RoleKey] = N'ServiceAgent' AND [Permission] = N'FollowUp.Update');
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610040018_FollowUpCenter',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
