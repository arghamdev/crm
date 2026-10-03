using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Infrastructure.Data;

public static class CrmDatabaseInitializer
{
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        if (await db.Companies.AnyAsync(cancellationToken)) return;

        var seed = SampleData.Create();
        db.AddRange(seed.PortalRequests);
        db.AddRange(seed.MobileVisits);
        db.AddRange(seed.MobileOperationReceipts);
        db.AddRange(seed.Companies);
        db.AddRange(seed.OrganizationUnits);
        db.AddRange(seed.Territories);
        db.AddRange(seed.Users);
        db.AddRange(seed.ExternalIdentities);
        db.AddRange(seed.UserSessions);
        db.AddRange(seed.UserRoleAssignments);
        db.AddRange(seed.SecurityAuditEvents);
        db.AddRange(seed.Customers);
        db.AddRange(seed.CustomerContacts);
        db.AddRange(seed.CustomerAddresses);
        db.AddRange(seed.CustomerProfiles);
        db.AddRange(seed.CustomerTimelineEvents);
        db.AddRange(seed.CustomerOwnershipHistory);
        db.AddRange(seed.CustomerDuplicateCandidates);
        db.AddRange(seed.CustomerMergeOperations);
        db.AddRange(seed.Leads);
        db.AddRange(seed.LeadStatusHistory);
        db.AddRange(seed.Opportunities);
        db.AddRange(seed.OpportunityStageHistory);
        db.AddRange(seed.OpportunityActivities);
        db.AddRange(seed.Quotes);
        db.AddRange(seed.QuoteLines);
        db.AddRange(seed.QuoteStatusHistory);
        db.AddRange(seed.QuoteApprovalDecisions);
        db.AddRange(seed.OrderRequests);
        db.AddRange(seed.OrderCreditDecisions);
        db.AddRange(seed.OrderStatusHistory);
        db.AddRange(seed.OrderIntegrationMessages);
        db.AddRange(seed.OrderIntegrationAttempts);
        db.AddRange(seed.Dealers);
        db.AddRange(seed.DealerContracts);
        db.AddRange(seed.DealerTerritoryAssignments);
        db.AddRange(seed.DealerCustomerAssignments);
        db.AddRange(seed.DealerTargets);
        db.AddRange(seed.DealerFinancialSnapshots);
        db.AddRange(seed.DealerPerformanceSnapshots);
        db.AddRange(seed.DealerStatusHistory);
        db.AddRange(seed.WorkItems);
        db.AddRange(seed.ServiceCases);
        db.AddRange(seed.DealerCommissionPlans);
        db.AddRange(seed.RoleDefinitions.Where(x => !db.RoleDefinitions.Any(r => r.RoleKey == x.RoleKey)));
        db.AddRange(seed.RolePermissionGrants.Where(x => !db.RolePermissionGrants.Any(g => g.Id == x.Id)));
        db.AddRange(seed.ServiceCaseHistory);
        db.AddRange(seed.OrganizationChanges);
        await db.SaveChangesAsync(cancellationToken);
    }
}
