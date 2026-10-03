using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>
/// Indexes for the security audit log page (newest first, optionally filtered by event type). On a very large
/// audit table, build them in a maintenance window or with ONLINE = ON where the SQL Server edition supports it.
/// </summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610030011_AuditLogIndexes")]
public sealed class AuditLogIndexes : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
CREATE INDEX [IX_SecurityAuditEvents_OccurredAtUtc] ON [iam].[SecurityAuditEvents]([OccurredAtUtc]);
CREATE INDEX [IX_SecurityAuditEvents_EventType_OccurredAtUtc] ON [iam].[SecurityAuditEvents]([EventType],[OccurredAtUtc]);
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP INDEX [IX_SecurityAuditEvents_EventType_OccurredAtUtc] ON [iam].[SecurityAuditEvents];
DROP INDEX [IX_SecurityAuditEvents_OccurredAtUtc] ON [iam].[SecurityAuditEvents];
""");
}
