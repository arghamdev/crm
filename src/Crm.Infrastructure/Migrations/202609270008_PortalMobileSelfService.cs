using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("202609270008_PortalMobileSelfService")]
public sealed class PortalMobileSelfService : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name=N'portal') EXEC(N'CREATE SCHEMA [portal]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name=N'mobile') EXEC(N'CREATE SCHEMA [mobile]');
CREATE TABLE [portal].[PortalRequests] (
 [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
 [DealerId] uniqueidentifier NOT NULL, [CreatedByUserId] uniqueidentifier NOT NULL, [OperationId] uniqueidentifier NOT NULL, [Fingerprint] nvarchar(64) NOT NULL,
 [Kind] nvarchar(24) NOT NULL, [Status] nvarchar(24) NOT NULL, [Subject] nvarchar(160) NOT NULL, [Description] nvarchar(2000) NOT NULL,
 [CustomerId] uniqueidentifier NULL, [ProductCode] nvarchar(64) NULL, [Quantity] decimal(18,3) NOT NULL, [UnitPrice] decimal(18,2) NOT NULL,
 [Source] nvarchar(200) NULL, [PriceAtUtc] datetimeoffset(3) NULL, [Email] nvarchar(256) NULL, [TargetUserId] uniqueidentifier NULL,
 [PublicReply] nvarchar(2000) NOT NULL, [ReviewedByUserId] uniqueidentifier NULL, [LinkedRecordId] uniqueidentifier NULL, [ProtectedUntilUtc] datetimeoffset(3) NULL, [ProtectionKey] nvarchar(64) NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
 CONSTRAINT [FK_PortalRequests_Dealers_DealerId] FOREIGN KEY ([DealerId]) REFERENCES [channel].[Dealers]([Id]),
 CONSTRAINT [FK_PortalRequests_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [iam].[Users]([Id]),
 CONSTRAINT [FK_PortalRequests_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers]([Id])
);
CREATE UNIQUE INDEX [IX_PortalRequests_CompanyId_CreatedByUserId_OperationId] ON [portal].[PortalRequests]([CompanyId],[CreatedByUserId],[OperationId]);
CREATE UNIQUE INDEX [IX_PortalRequests_CompanyId_ProtectionKey] ON [portal].[PortalRequests]([CompanyId],[ProtectionKey]) WHERE [ProtectionKey] IS NOT NULL;
CREATE UNIQUE INDEX [IX_PortalRequests_CompanyId_LinkedRecordId] ON [portal].[PortalRequests]([CompanyId],[LinkedRecordId]) WHERE [Kind] = N'Order' AND [LinkedRecordId] IS NOT NULL;
CREATE INDEX [IX_PortalRequests_CompanyId_DealerId_Status] ON [portal].[PortalRequests]([CompanyId],[DealerId],[Status]);
CREATE INDEX [IX_PortalRequests_DealerId] ON [portal].[PortalRequests]([DealerId]);
CREATE INDEX [IX_PortalRequests_CreatedByUserId] ON [portal].[PortalRequests]([CreatedByUserId]);
CREATE INDEX [IX_PortalRequests_CustomerId] ON [portal].[PortalRequests]([CustomerId]);
CREATE TABLE [mobile].[MobileVisits] (
 [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CompanyId] nvarchar(32) NOT NULL, [BranchId] nvarchar(32) NOT NULL, [TerritoryId] nvarchar(32) NULL,
 [CustomerId] uniqueidentifier NOT NULL, [OwnerUserId] uniqueidentifier NOT NULL, [PlannedAtUtc] datetimeoffset(3) NOT NULL, [Purpose] nvarchar(1000) NOT NULL,
 [Status] nvarchar(24) NOT NULL, [CheckedInAtUtc] datetimeoffset(3) NULL, [CompletedAtUtc] datetimeoffset(3) NULL, [LastReceivedAtUtc] datetimeoffset(3) NULL,
 [Outcome] nvarchar(2000) NOT NULL, [Latitude] decimal(9,6) NULL, [Longitude] decimal(9,6) NULL, [LocationConsent] bit NOT NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
 CONSTRAINT [FK_MobileVisits_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [crm].[Customers]([Id]),
 CONSTRAINT [FK_MobileVisits_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [iam].[Users]([Id])
);
CREATE INDEX [IX_MobileVisits_CompanyId_OwnerUserId_PlannedAtUtc] ON [mobile].[MobileVisits]([CompanyId],[OwnerUserId],[PlannedAtUtc]);
CREATE INDEX [IX_MobileVisits_CustomerId] ON [mobile].[MobileVisits]([CustomerId]);
CREATE INDEX [IX_MobileVisits_OwnerUserId] ON [mobile].[MobileVisits]([OwnerUserId]);
CREATE TABLE [mobile].[OperationReceipts] (
 [Id] uniqueidentifier NOT NULL PRIMARY KEY, [CompanyId] nvarchar(32) NOT NULL, [ActorUserId] uniqueidentifier NOT NULL,
 [OperationId] uniqueidentifier NOT NULL, [VisitId] uniqueidentifier NOT NULL, [Fingerprint] nvarchar(64) NOT NULL, [AppliedVersion] bigint NOT NULL, [ReceivedAtUtc] datetimeoffset(3) NOT NULL,
 [CreatedAtUtc] datetimeoffset(3) NOT NULL, [UpdatedAtUtc] datetimeoffset(3) NOT NULL, [Version] bigint NOT NULL,
 CONSTRAINT [FK_OperationReceipts_MobileVisits_VisitId] FOREIGN KEY ([VisitId]) REFERENCES [mobile].[MobileVisits]([Id]),
 CONSTRAINT [FK_OperationReceipts_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [iam].[Users]([Id])
);
CREATE UNIQUE INDEX [IX_OperationReceipts_CompanyId_ActorUserId_OperationId] ON [mobile].[OperationReceipts]([CompanyId],[ActorUserId],[OperationId]);
CREATE INDEX [IX_OperationReceipts_VisitId] ON [mobile].[OperationReceipts]([VisitId]);
CREATE INDEX [IX_OperationReceipts_ActorUserId] ON [mobile].[OperationReceipts]([ActorUserId]);
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP TABLE [mobile].[OperationReceipts];
DROP TABLE [mobile].[MobileVisits];
DROP TABLE [portal].[PortalRequests];
""");
}
