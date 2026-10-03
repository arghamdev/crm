using Crm.Application.Abstractions;
using Crm.Domain.Customers;
using Crm.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

using (var modelDb = new CrmDbContext(new DbContextOptionsBuilder<CrmDbContext>()
    .UseSqlServer("Server=localhost;Database=CrmModelValidation;User Id=sa;Password=ModelOnly!1405;TrustServerCertificate=True")
    .Options))
{
    var model = modelDb.Model;
    var unmapped = model.GetEntityTypes().SelectMany(entity => entity.ClrType.GetProperties()
        .Where(property => property.DeclaringType?.GetField($"<{property.Name}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is not null
            && !Attribute.IsDefined(property, typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute))
            && entity.FindProperty(property.Name) is null && entity.FindNavigation(property.Name) is null)
        .Select(property => $"{entity.ClrType.Name}.{property.Name}")).ToArray();
    if (unmapped.Length > 0)
        throw new InvalidOperationException("Unmapped stored entity properties: " + string.Join(", ", unmapped));
    if (modelDb.Database.HasPendingModelChanges())
        throw new InvalidOperationException("The EF model differs from its migration snapshot; reconcile it before deployment.");
    var ids=modelDb.GetService<IMigrationsIdGenerator>();
    if(!ids.IsValidId("202609270007_DealerChannelGovernance") || ids.GetName("202609270007_DealerChannelGovernance")!="DealerChannelGovernance" || !ids.IsValidId(ids.GenerateId("NextMigration")))
        throw new InvalidOperationException("Legacy migration IDs must resolve without rewriting history.");
    foreach (var type in new[] { typeof(Crm.Domain.SelfService.PortalRequest), typeof(Crm.Domain.SelfService.MobileVisit), typeof(Crm.Domain.SelfService.MobileOperationReceipt) })
    {
        var entity = model.FindEntityType(type) ?? throw new InvalidOperationException("P10 entity missing.");
        if (entity.GetSchema() is not ("portal" or "mobile") || entity.FindProperty("Version")?.IsConcurrencyToken != true || !entity.GetForeignKeys().Any())
            throw new InvalidOperationException("P10 scope, concurrency or relationships are missing.");
        if (type != typeof(Crm.Domain.SelfService.MobileVisit) && !entity.GetIndexes().Any(x=>x.IsUnique && x.Properties.Select(p=>p.Name).Contains("OperationId")))
            throw new InvalidOperationException("P10 operation uniqueness is missing.");
    }
    var script = modelDb.GetService<IMigrator>().GenerateScript("202609270007_DealerChannelGovernance", "202609270008_PortalMobileSelfService");
    var portalModel=model.FindEntityType(typeof(Crm.Domain.SelfService.PortalRequest))!;
    if(!portalModel.GetIndexes().Any(x=>x.IsUnique&&x.Properties.Any(p=>p.Name=="ProtectionKey")) || !portalModel.GetIndexes().Any(x=>x.IsUnique&&x.Properties.Any(p=>p.Name=="LinkedRecordId")))
        throw new InvalidOperationException("P10 lead protection and order links require database race protection.");
    if (!script.Contains("CREATE TABLE [portal].[PortalRequests]") || !script.Contains("CREATE TABLE [mobile].[OperationReceipts]") || !script.Contains("CREATE UNIQUE INDEX"))
        throw new InvalidOperationException("P10 upgrade SQL is incomplete.");
    var down = modelDb.GetService<IMigrator>().GenerateScript("202609270008_PortalMobileSelfService", "202609270007_DealerChannelGovernance");
    if (!down.Contains("DROP TABLE [mobile].[OperationReceipts]") || down.Contains("DROP TABLE [channel].[Dealers]"))
        throw new InvalidOperationException("P10 rollback must preserve P8/P9 dealer data.");
    Console.WriteLine("P10 EF model, operation uniqueness, concurrency, relationships and upgrade/rollback SQL checks passed.");
    var order = model.FindEntityType(typeof(Crm.Domain.Commercial.OrderRequest));
    var message = model.FindEntityType(typeof(Crm.Domain.Commercial.OrderIntegrationMessage));
    var dealer = model.FindEntityType(typeof(Crm.Domain.Channel.Dealer));
    var dealerCustomer = model.FindEntityType(typeof(Crm.Domain.Channel.DealerCustomerAssignment));
    if (order?.GetTableName() != "OrderRequests" || order.GetSchema() != "commercial" ||
        message?.GetTableName() != "OrderIntegrationMessages" || message.GetSchema() != "integration" ||
        dealer?.GetTableName() != "Dealers" || dealer.GetSchema() != "channel" ||
        dealerCustomer?.GetTableName() != "DealerCustomerAssignments" || dealerCustomer.GetSchema() != "channel")
    {
        Console.Error.WriteLine("Priority-8 EF model does not map order, integration and dealer aggregates to the expected schemas.");
        return 1;
    }
}

var connectionString = Environment.GetEnvironmentVariable("CRM_TEST_SQLSERVER");
var requireSqlServer = Environment.GetEnvironmentVariable("CRM_REQUIRE_SQLSERVER")
    ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (requireSqlServer)
    {
        Console.Error.WriteLine("CRM_REQUIRE_SQLSERVER=true but CRM_TEST_SQLSERVER is not set.");
        return 1;
    }
    Console.WriteLine("CRM_TEST_SQLSERVER is not set; SQL Server persistence check skipped.");
    return 0;
}

var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (string.IsNullOrWhiteSpace(databaseName) ||
    !(databaseName.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
      databaseName.Contains("Ci", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Persistence tests refuse EnsureDeleted unless the database name contains Test or Ci.");
    return 1;
}

var services = new ServiceCollection()
    .AddDbContext<CrmDbContext>(options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()))
    .AddScoped<ICrmDataStore, EfCoreCrmDataStore>()
    .AddScoped<ICustomerQueryStore, EfCoreCustomerQueryStore>()
    .BuildServiceProvider();

await using (services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    if (!await WaitForSqlServer(connectionString, TimeSpan.FromSeconds(90)))
    {
        Console.Error.WriteLine("SQL Server did not become ready within 90 seconds.");
        return 1;
    }
    await db.Database.EnsureDeletedAsync();
    await CrmDatabaseInitializer.MigrateAndSeedAsync(services);
    await SqlVerification.CheckImmutableSeedValues(db);

    var store = scope.ServiceProvider.GetRequiredService<ICrmDataStore>();
    var counts = store.Read(data => new
    {
        Companies = data.Companies.Count,
        Units = data.OrganizationUnits.Count,
        Territories = data.Territories.Count,
        Users = data.Users.Count,
        Customers = data.Customers.Count,
        CustomerContacts = data.CustomerContacts.Count,
        CustomerAddresses = data.CustomerAddresses.Count,
        CustomerTimelineEvents = data.CustomerTimelineEvents.Count,
        CustomerOwnershipHistory = data.CustomerOwnershipHistory.Count,
        CustomerDuplicateCandidates = data.CustomerDuplicateCandidates.Count,
        CustomerMergeOperations = data.CustomerMergeOperations.Count,
        Leads = data.Leads.Count,
        LeadStatusHistory = data.LeadStatusHistory.Count,
        Opportunities = data.Opportunities.Count,
        OpportunityStageHistory = data.OpportunityStageHistory.Count,
        OpportunityActivities = data.OpportunityActivities.Count,
        Quotes = data.Quotes.Count,
        QuoteLines = data.QuoteLines.Count,
        QuoteStatusHistory = data.QuoteStatusHistory.Count,
        QuoteApprovalDecisions = data.QuoteApprovalDecisions.Count,
        OrderRequests = data.OrderRequests.Count,
        OrderCreditDecisions = data.OrderCreditDecisions.Count,
        OrderStatusHistory = data.OrderStatusHistory.Count,
        Dealers = data.Dealers.Count,
        DealerContracts = data.DealerContracts.Count,
        DealerTerritories = data.DealerTerritoryAssignments.Count,
        DealerCustomers = data.DealerCustomerAssignments.Count,
        DealerTargets = data.DealerTargets.Count,
        DealerFinancial = data.DealerFinancialSnapshots.Count,
        DealerPerformance = data.DealerPerformanceSnapshots.Count,
        DealerHistory = data.DealerStatusHistory.Count
    });
    if (counts.Companies != 2 || counts.Units < 7 || counts.Territories < 3 || counts.Users < 5 || counts.Customers < 6 ||
        counts.CustomerContacts < 3 || counts.CustomerAddresses < 3 || counts.CustomerTimelineEvents < 3 ||
        counts.CustomerOwnershipHistory < 3 || counts.CustomerDuplicateCandidates < 1 || counts.Leads < 4 ||
        counts.LeadStatusHistory < 8 || counts.Opportunities < 4 || counts.OpportunityStageHistory < 4 ||
        counts.OpportunityActivities < 4 || counts.Quotes < 4 || counts.QuoteLines < 4 ||
        counts.QuoteStatusHistory < 4 || counts.QuoteApprovalDecisions < 1 || counts.OrderRequests < 1 ||
        counts.OrderCreditDecisions < 1 || counts.OrderStatusHistory < 2 || counts.Dealers < 1 ||
        counts.DealerContracts < 1 || counts.DealerTerritories < 1 || counts.DealerCustomers < 2 ||
        counts.DealerTargets < 1 || counts.DealerFinancial < 1 || counts.DealerPerformance < 1 || counts.DealerHistory < 3)
    {
        Console.Error.WriteLine($"Unexpected seed counts: {counts}");
        return 1;
    }

    var newChangeId = Guid.NewGuid();
    store.Write(data =>
    {
        data.OrganizationChanges.Add(new Crm.Domain.Organization.OrganizationChange(
            newChangeId, "C01", "PersistenceCheck", "CI", "Created", "SQL persistence check",
            data.Users.First().Id, DateTimeOffset.UtcNow, "persistence-test", string.Empty, "{}"));
        return true;
    });
    if (!store.Read(data => data.OrganizationChanges.Any(x => x.Id == newChangeId)))
    {
        Console.Error.WriteLine("EF Core store did not persist the organization change.");
        return 1;
    }

    var newContactId = Guid.NewGuid();
    store.Write(data =>
    {
        data.CustomerContacts.Add(new Crm.Domain.Customers.CustomerContact(newContactId, "C01",
            Guid.Parse("20000000-0000-4000-8000-000000000001"), "Persistence Check", "CI",
            "09120000000", null, false, Crm.Domain.Customers.ContactConsentStatus.Unknown));
        return true;
    });
    if (!store.Read(data => data.CustomerContacts.Any(x => x.Id == newContactId)))
    {
        Console.Error.WriteLine("EF Core store did not persist the Customer 360 contact record.");
        return 1;
    }

    var customerQueries = scope.ServiceProvider.GetRequiredService<ICustomerQueryStore>();
    var customerPage = customerQueries.Search(new CustomerQuerySpec("C01", null, null, true, [], [], null, 1, 2));
    if (customerPage.TotalCount != 4 || customerPage.Items.Count != 2 || customerPage.Items.Any(x => x.Id == Guid.Empty))
    {
        Console.Error.WriteLine("EF Core customer query store did not preserve server-side paging and identifiers.");
        return 1;
    }

    var mergeOperationId = Guid.NewGuid();
    store.Write(data =>
    {
        data.CustomerMergeOperations.Add(new CustomerMergeOperation(mergeOperationId, "C01",
            Guid.Parse("25000000-0000-4000-8000-000000000001"),
            Guid.Parse("20000000-0000-4000-8000-000000000004"),
            Guid.Parse("20000000-0000-4000-8000-000000000003"), CustomerStatus.Active,
            "{\"contacts\":[],\"addresses\":[],\"leads\":[],\"opportunities\":[],\"quotes\":[]}",
            "SQL persistence check", data.Users.First().Id, DateTimeOffset.UtcNow));
        return true;
    });
    if (!store.Read(data => data.CustomerMergeOperations.Any(x => x.Id == mergeOperationId && x.Status == CustomerMergeStatus.Merged)))
    {
        Console.Error.WriteLine("EF Core store did not persist the merge audit manifest.");
        return 1;
    }

    var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
    if (!appliedMigrations.Contains("202609270008_PortalMobileSelfService")) throw new InvalidOperationException("P10 migration missing.");
    await CheckSelfServicePersistence(db, store);
    Console.WriteLine("P10 SQL round-trip, stale-version rejection and duplicate-operation rejection passed.");
    if (!appliedMigrations.Contains("202609210002_Customer360", StringComparer.Ordinal) ||
        !appliedMigrations.Contains("202609210003_CustomerRelationshipsAndMerge", StringComparer.Ordinal) ||
        !appliedMigrations.Contains("202609220004_SalesPipelineGovernance", StringComparer.Ordinal) ||
        !appliedMigrations.Contains("202609260005_QuotePricingGovernance", StringComparer.Ordinal) ||
        !appliedMigrations.Contains("202609260006_OrderIntegrationVisibility", StringComparer.Ordinal) ||
        !appliedMigrations.Contains("202609270007_DealerChannelGovernance", StringComparer.Ordinal))
    {
        Console.Error.WriteLine("The cumulative Customer 360, pipeline, quote, order and dealer migrations were not applied.");
        return 1;
    }
    if (store.Read(data => data.Opportunities.Any(x => x.CustomerId == Guid.Empty) ||
        data.Quotes.Any(x => x.CustomerId == Guid.Empty) || data.OrderRequests.Any(x => x.CustomerId == Guid.Empty)))
    {
        Console.Error.WriteLine("Seeded commercial relations must use CustomerId.");
        return 1;
    }

    if (Environment.GetEnvironmentVariable("CRM_TEST_ROLLBACK")
        ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
    {
        var migrator = db.GetService<IMigrator>();
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync("202609270007_DealerChannelGovernance");
        if ((await db.Database.GetAppliedMigrationsAsync()).Contains("202609270008_PortalMobileSelfService") || !await db.Dealers.AnyAsync())
            throw new InvalidOperationException("P10 rollback must retain existing dealer rows.");
        await migrator.MigrateAsync();
        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains("202609270008_PortalMobileSelfService"))
            throw new InvalidOperationException("P10 roll-forward failed.");
        await migrator.MigrateAsync("202609260006_OrderIntegrationVisibility");
        var afterRollback = await db.Database.GetAppliedMigrationsAsync();
        if (afterRollback.Contains("202609270007_DealerChannelGovernance", StringComparer.Ordinal) ||
            !afterRollback.Contains("202609260006_OrderIntegrationVisibility", StringComparer.Ordinal) ||
            !await PriorityEightSchemaWasRemoved(db))
        {
            Console.Error.WriteLine("Priority-8 rollback did not remove only the dealer/channel schema.");
            return 1;
        }

        await migrator.MigrateAsync();
        var afterRollForward = await db.Database.GetAppliedMigrationsAsync();
        if (!afterRollForward.Contains("202609270007_DealerChannelGovernance", StringComparer.Ordinal))
        {
            Console.Error.WriteLine("Priority-8 migration could not be applied again after rollback.");
            return 1;
        }
        Console.WriteLine("EF rollback and roll-forward P10/P8 passed; preceding dealer/order data was preserved.");
        await SqlVerification.CheckStandaloneSql(db, services);
    }
}

Console.WriteLine("SQL Server migrations, dealer/channel seed and EF Core persistence checks passed.");
return 0;

static async Task CheckSelfServicePersistence(CrmDbContext db, ICrmDataStore store)
{
    var id=Guid.NewGuid();var receiptId=Guid.NewGuid();var operation=Guid.NewGuid();var requestId=Guid.NewGuid();
    var actor=Guid.Parse("10000000-0000-4000-8000-000000000001");var now=DateTimeOffset.UtcNow;
    store.Write(d=>{
        var c=d.Customers.First(x=>x.CompanyId=="C01");var dealer=d.Dealers.First(x=>x.CompanyId=="C01");
        d.MobileVisits.Add(new(id,c.CompanyId,c.BranchId,c.TerritoryId,c.Id,actor,now,"SQL round trip"));
        d.MobileOperationReceipts.Add(new(receiptId,"C01",actor,operation,id,new string('1',64),1,now));
        d.PortalRequests.Add(new(requestId,"C01",dealer.BranchId,dealer.TerritoryId,dealer.Id,actor,Guid.NewGuid(),new string('2',64),Crm.Domain.SelfService.PortalRequestKind.Complaint,"SQL request","round trip",null,null,0,0,null,null,null,null));return true;
    });
    db.ChangeTracker.Clear();
    if (!store.Read(d=>d.MobileVisits.Any(x=>x.Id==id)&&d.MobileOperationReceipts.Any(x=>x.Id==receiptId)&&d.PortalRequests.Any(x=>x.Id==requestId))) throw new InvalidOperationException("P10 EF store did not persist all aggregates.");
    var options=new DbContextOptionsBuilder<CrmDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
    await using var first=new CrmDbContext(options);await using var second=new CrmDbContext(options);
    var a=await first.MobileVisits.SingleAsync(x=>x.Id==id);var b=await second.MobileVisits.SingleAsync(x=>x.Id==id);
    a.Transition(Crm.Domain.SelfService.VisitStatus.CheckedIn,now,now,"",false,null,null);await first.SaveChangesAsync();
    b.Transition(Crm.Domain.SelfService.VisitStatus.CheckedIn,now,now,"",false,null,null);
    try {await second.SaveChangesAsync();throw new InvalidOperationException("P10 stale update was accepted.");} catch(DbUpdateConcurrencyException){}
    await using var duplicate=new CrmDbContext(options);
    duplicate.MobileOperationReceipts.Add(new(Guid.NewGuid(),"C01",actor,operation,id,new string('1',64),1,now));
    try {await duplicate.SaveChangesAsync();throw new InvalidOperationException("P10 duplicate operation was accepted.");} catch(DbUpdateException e) when(e.InnerException is SqlException sql && sql.Number is 2601 or 2627){}
}

static async Task<bool> WaitForSqlServer(string testConnectionString, TimeSpan timeout)
{
    var builder = new SqlConnectionStringBuilder(testConnectionString) { InitialCatalog = "master" };
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (SqlException)
        {
            // The CI service container may still be starting.
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
    return false;
}

static async Task<bool> PriorityEightSchemaWasRemoved(CrmDbContext db)
{
    await db.Database.OpenConnectionAsync();
    try
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN
                OBJECT_ID('channel.Dealers', 'U') IS NULL AND
                OBJECT_ID('channel.DealerContracts', 'U') IS NULL AND
                OBJECT_ID('channel.DealerTerritoryAssignments', 'U') IS NULL AND
                OBJECT_ID('channel.DealerCustomerAssignments', 'U') IS NULL AND
                OBJECT_ID('channel.DealerTargets', 'U') IS NULL AND
                OBJECT_ID('channel.DealerFinancialSnapshots', 'U') IS NULL AND
                OBJECT_ID('channel.DealerPerformanceSnapshots', 'U') IS NULL AND
                OBJECT_ID('channel.DealerStatusHistory', 'U') IS NULL AND
                OBJECT_ID('commercial.OrderRequests', 'U') IS NOT NULL
            THEN 1 ELSE 0 END
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}
