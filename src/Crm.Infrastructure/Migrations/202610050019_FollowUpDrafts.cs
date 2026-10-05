using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>Follow-up center: server-side form drafts (پیش‌نویس) per user, company and form.</summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610050019_FollowUpDrafts")]
public sealed class FollowUpDrafts : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
CREATE TABLE [followup].[Drafts] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [Kind] nvarchar(32) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [SavedAtUtc] datetimeoffset(3) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_Drafts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Drafts_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [iam].[Users] ([Id])
);
CREATE UNIQUE INDEX [IX_Drafts_UserId_CompanyId_Kind] ON [followup].[Drafts] ([UserId], [CompanyId], [Kind]);
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP TABLE [followup].[Drafts];
""");
}
