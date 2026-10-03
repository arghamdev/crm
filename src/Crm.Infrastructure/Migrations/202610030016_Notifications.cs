using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>Email/SMS notification outbox (deduplicated per recipient and channel) and per-user notification preferences.</summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610030016_Notifications")]
public sealed class Notifications : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
IF SCHEMA_ID(N'notify') IS NULL EXEC(N'CREATE SCHEMA [notify];');
CREATE TABLE [notify].[NotificationMessages] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [RecipientUserId] uniqueidentifier NOT NULL,
    [Channel] nvarchar(8) NOT NULL,
    [Address] nvarchar(256) NOT NULL,
    [Category] nvarchar(32) NOT NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Body] nvarchar(1000) NOT NULL,
    [SourceReference] nvarchar(120) NULL,
    [DedupKey] nvarchar(160) NOT NULL,
    [Status] nvarchar(16) NOT NULL,
    [AttemptCount] int NOT NULL,
    [NextAttemptAtUtc] datetimeoffset(3) NULL,
    [LastError] nvarchar(500) NULL,
    [ProviderMessageId] nvarchar(120) NULL,
    [SentAtUtc] datetimeoffset(3) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_NotificationMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationMessages_Users_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE TABLE [notify].[NotificationPreferences] (
    [Id] uniqueidentifier NOT NULL,
    [Mobile] nvarchar(20) NULL,
    [EmailEnabled] bit NOT NULL,
    [SmsEnabled] bit NOT NULL,
    [MutedCategories] nvarchar(200) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_NotificationPreferences] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationPreferences_Users_Id] FOREIGN KEY ([Id]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_NotificationMessages_DedupKey_RecipientUserId_Channel] ON [notify].[NotificationMessages] ([DedupKey], [RecipientUserId], [Channel]);
CREATE INDEX [IX_NotificationMessages_RecipientUserId_CreatedAtUtc] ON [notify].[NotificationMessages] ([RecipientUserId], [CreatedAtUtc]);
CREATE INDEX [IX_NotificationMessages_Status_NextAttemptAtUtc] ON [notify].[NotificationMessages] ([Status], [NextAttemptAtUtc]);
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP TABLE [notify].[NotificationMessages];
DROP TABLE [notify].[NotificationPreferences];
IF SCHEMA_ID(N'notify') IS NOT NULL EXEC(N'DROP SCHEMA [notify];');
""");
}
