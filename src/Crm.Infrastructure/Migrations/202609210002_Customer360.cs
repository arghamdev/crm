using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609210002_Customer360")]
public sealed class Customer360 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
ALTER TABLE [crm].[Customers] ADD
    [Kind] nvarchar(24) NOT NULL CONSTRAINT [DF_Customers_Kind] DEFAULT N'Legal',
    [NationalId] nvarchar(32) NULL,
    [PrimaryPhone] nvarchar(40) NULL,
    [PrimaryEmail] nvarchar(256) NULL,
    [DataSource] nvarchar(80) NOT NULL CONSTRAINT [DF_Customers_DataSource] DEFAULT N'CRM',
    [LastSynchronizedAtUtc] datetimeoffset(3) NULL;
-- Compile only after the preceding ALTER TABLE has added these columns.
EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [IX_Customers_CompanyId_NationalId] ON [crm].[Customers] ([CompanyId], [NationalId]) WHERE [NationalId] IS NOT NULL;';

CREATE TABLE [crm].[CustomerContacts] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [FullName] nvarchar(200) NOT NULL, [Role] nvarchar(100) NOT NULL, [Phone] nvarchar(40) NULL, [Email] nvarchar(256) NULL,
    [IsPrimary] bit NOT NULL, [ConsentStatus] nvarchar(24) NOT NULL, [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerContacts] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerContacts_CompanyId_CustomerId_IsActive] ON [crm].[CustomerContacts] ([CompanyId], [CustomerId], [IsActive]);
CREATE INDEX [IX_CustomerContacts_CompanyId_Phone] ON [crm].[CustomerContacts] ([CompanyId], [Phone]);
CREATE INDEX [IX_CustomerContacts_CompanyId_Email] ON [crm].[CustomerContacts] ([CompanyId], [Email]);

CREATE TABLE [crm].[CustomerAddresses] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [Type] nvarchar(24) NOT NULL, [Title] nvarchar(120) NOT NULL, [Province] nvarchar(100) NOT NULL,
    [City] nvarchar(100) NOT NULL, [AddressLine] nvarchar(1000) NOT NULL, [PostalCode] nvarchar(20) NULL,
    [IsPrimary] bit NOT NULL, [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerAddresses] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerAddresses_CompanyId_CustomerId_IsActive] ON [crm].[CustomerAddresses] ([CompanyId], [CustomerId], [IsActive]);

CREATE TABLE [crm].[CustomerTimelineEvents] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [Type] nvarchar(32) NOT NULL, [Title] nvarchar(200) NOT NULL, [Description] nvarchar(2000) NOT NULL,
    [OccurredAtUtc] datetimeoffset(3) NOT NULL, [Source] nvarchar(80) NOT NULL, [SourceReference] nvarchar(120) NULL,
    [ActorUserId] uniqueidentifier NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerTimelineEvents] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerTimelineEvents_CompanyId_CustomerId_OccurredAtUtc] ON [crm].[CustomerTimelineEvents] ([CompanyId], [CustomerId], [OccurredAtUtc]);

CREATE TABLE [crm].[CustomerOwnershipHistory] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL, [Owner] nvarchar(150) NOT NULL,
    [ValidFromUtc] datetimeoffset(3) NOT NULL, [ValidToUtc] datetimeoffset(3) NULL, [Reason] nvarchar(500) NOT NULL,
    [ChangedByUserId] uniqueidentifier NOT NULL, [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerOwnershipHistory] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerOwnershipHistory_CompanyId_CustomerId_ValidToUtc] ON [crm].[CustomerOwnershipHistory] ([CompanyId], [CustomerId], [ValidToUtc]);

CREATE TABLE [crm].[CustomerDuplicateCandidates] (
    [Id] uniqueidentifier NOT NULL, [CompanyId] nvarchar(32) NOT NULL, [CustomerId] uniqueidentifier NOT NULL,
    [PossibleDuplicateCustomerId] uniqueidentifier NOT NULL, [Score] int NOT NULL, [Reasons] nvarchar(1000) NOT NULL,
    [DetectedAtUtc] datetimeoffset(3) NOT NULL, [Status] nvarchar(24) NOT NULL, [ReviewedByUserId] uniqueidentifier NULL,
    [ReviewedAtUtc] datetimeoffset(3) NULL, [ReviewNote] nvarchar(1000) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerDuplicateCandidates] PRIMARY KEY ([Id])
);
CREATE INDEX [IX_CustomerDuplicateCandidates_CompanyId_Status_DetectedAtUtc] ON [crm].[CustomerDuplicateCandidates] ([CompanyId], [Status], [DetectedAtUtc]);
CREATE INDEX [IX_CustomerDuplicateCandidates_CustomerId_PossibleDuplicateCustomerId] ON [crm].[CustomerDuplicateCandidates] ([CustomerId], [PossibleDuplicateCustomerId]);
""");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DROP TABLE IF EXISTS [crm].[CustomerDuplicateCandidates];
DROP TABLE IF EXISTS [crm].[CustomerOwnershipHistory];
DROP TABLE IF EXISTS [crm].[CustomerTimelineEvents];
DROP TABLE IF EXISTS [crm].[CustomerAddresses];
DROP TABLE IF EXISTS [crm].[CustomerContacts];
DROP INDEX IF EXISTS [IX_Customers_CompanyId_NationalId] ON [crm].[Customers];
ALTER TABLE [crm].[Customers] DROP CONSTRAINT IF EXISTS [DF_Customers_Kind];
ALTER TABLE [crm].[Customers] DROP CONSTRAINT IF EXISTS [DF_Customers_DataSource];
ALTER TABLE [crm].[Customers] DROP COLUMN [Kind], [NationalId], [PrimaryPhone], [PrimaryEmail], [DataSource], [LastSynchronizedAtUtc];
""");
}
