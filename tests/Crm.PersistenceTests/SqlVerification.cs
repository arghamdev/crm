using Crm.Domain.Common;
using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class SqlVerification
{
    public static async Task CheckImmutableSeedValues(CrmDbContext db)
    {
        db.ChangeTracker.Clear();
        var expected = new InMemoryCrmDataStore().Read(data => data);
        async Task Match<TEntity, TValue>(IEnumerable<TEntity> entities, Func<TEntity, TValue> value) where TEntity : Entity
        {
            foreach (var original in entities)
            {
                var saved = await db.Set<TEntity>().AsNoTracking().SingleAsync(x => x.Id == original.Id);
                if (!EqualityComparer<TValue>.Default.Equals(value(original), value(saved)))
                    throw new InvalidOperationException($"Immutable values did not round-trip for {typeof(TEntity).Name}.");
            }
        }
        await Match(expected.DealerPerformanceSnapshots, x => x.OrderCount);
        await Match(expected.DealerTerritoryAssignments, x => x.IsExclusive);
        await Match(expected.CustomerDuplicateCandidates, x => x.Score);
        await Match(expected.CustomerOwnershipHistory, x => x.ChangedByUserId);
        await Match(expected.CustomerTimelineEvents, x => x.ActorUserId);
        await Match(expected.UserRoleAssignments, x => x.AssignedByUserId);
        await Match(expected.OpportunityStageHistory, x => x.Probability);
        Console.WriteLine("Immutable seeded values round-tripped through SQL Server.");
    }

    public static async Task CheckStandaloneSql(CrmDbContext db, IServiceProvider services)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "scripts", "sql"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("SQL acceptance scripts were not found.");
        async Task Run(string name) => await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(root, "scripts", "sql", name)));
        db.ChangeTracker.Clear();
        await db.Database.EnsureDeletedAsync();
        await db.GetService<IMigrator>().MigrateAsync("202609210002_Customer360");
        foreach (var name in new[] { "priority4-relationships-merge-idempotent.sql", "priority5-sales-pipeline-idempotent.sql",
            "priority6-quote-pricing-idempotent.sql", "priority7-order-integration-idempotent.sql",
            "priority8-dealer-channel-idempotent.sql", "priority10-portal-mobile-idempotent.sql" })
        {
            await Run(name);
            await Run(name);
            Console.WriteLine("Standalone SQL repeated successfully: " + name);
        }
        await CrmDatabaseInitializer.MigrateAndSeedAsync(services);
        await CheckImmutableSeedValues(db);
        var dealers = await db.Dealers.CountAsync();
        await Run("priority10-portal-mobile-rollback.sql");
        await Run("priority10-portal-mobile-rollback.sql");
        if (dealers == 0 || await db.Dealers.CountAsync() != dealers ||
            (await db.Database.GetAppliedMigrationsAsync()).Contains("202609270008_PortalMobileSelfService"))
            throw new InvalidOperationException("Standalone P10 rollback changed preceding dealer data or migration history incorrectly.");
        await Run("priority10-portal-mobile-idempotent.sql");
        await Run("priority10-portal-mobile-idempotent.sql");
        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains("202609270008_PortalMobileSelfService"))
            throw new InvalidOperationException("Standalone P10 roll-forward did not restore migration history.");
        Console.WriteLine("Standalone SQL upgrades P4/P5/P6/P7/P8/P10 and repeated P10 rollback/upgrade passed.");
    }
}
