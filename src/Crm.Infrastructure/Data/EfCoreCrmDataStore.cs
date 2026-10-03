using Crm.Application.Abstractions;
using Crm.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Data;

public sealed class EfCoreCrmDataStore(CrmDbContext db) : ICrmDataStore
{
    public TResult Read<TResult>(Func<CrmDataSet, TResult> query) => query(Load(tracking: false));

    public TResult Write<TResult>(Func<CrmDataSet, TResult> command)
    {
        var data = Load(tracking: true);
        var known = KnownIds(data);
        var result = command(data);
        RemoveMissing(db.QuoteLines, data.QuoteLines, known);
        AddNew(data, known);
        try
        {
            db.SaveChanges();
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException("رکورد توسط درخواست دیگری تغییر کرده است؛ داده‌ها را تازه‌سازی و دوباره تلاش کنید.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627)
        {
            throw new Crm.Application.Contracts.SelfServiceConflictException("عملیات هم‌زمان یا شناسهٔ تکراری است؛ وضعیت ذخیره‌شده را تازه‌سازی کنید.");
        }
    }

    private CrmDataSet Load(bool tracking)
    {
        var data = new CrmDataSet();
        data.PortalRequests.AddRange(Query(db.PortalRequests, tracking));
        data.MobileVisits.AddRange(Query(db.MobileVisits, tracking));
        data.MobileOperationReceipts.AddRange(Query(db.MobileOperationReceipts, tracking));
        data.Customers.AddRange(Query(db.Customers, tracking));
        data.CustomerContacts.AddRange(Query(db.CustomerContacts, tracking));
        data.CustomerAddresses.AddRange(Query(db.CustomerAddresses, tracking));
        data.CustomerTimelineEvents.AddRange(Query(db.CustomerTimelineEvents, tracking));
        data.CustomerOwnershipHistory.AddRange(Query(db.CustomerOwnershipHistory, tracking));
        data.CustomerDuplicateCandidates.AddRange(Query(db.CustomerDuplicateCandidates, tracking));
        data.CustomerMergeOperations.AddRange(Query(db.CustomerMergeOperations, tracking));
        data.Leads.AddRange(Query(db.Leads, tracking));
        data.LeadStatusHistory.AddRange(Query(db.LeadStatusHistory, tracking));
        data.Opportunities.AddRange(Query(db.Opportunities, tracking));
        data.OpportunityStageHistory.AddRange(Query(db.OpportunityStageHistory, tracking));
        data.OpportunityActivities.AddRange(Query(db.OpportunityActivities, tracking));
        data.Quotes.AddRange(Query(db.Quotes, tracking));
        data.QuoteLines.AddRange(Query(db.QuoteLines, tracking));
        data.QuoteStatusHistory.AddRange(Query(db.QuoteStatusHistory, tracking));
        data.QuoteApprovalDecisions.AddRange(Query(db.QuoteApprovalDecisions, tracking));
        data.OrderRequests.AddRange(Query(db.OrderRequests, tracking));
        data.OrderCreditDecisions.AddRange(Query(db.OrderCreditDecisions, tracking));
        data.OrderStatusHistory.AddRange(Query(db.OrderStatusHistory, tracking));
        data.OrderIntegrationMessages.AddRange(Query(db.OrderIntegrationMessages, tracking));
        data.OrderIntegrationAttempts.AddRange(Query(db.OrderIntegrationAttempts, tracking));
        data.Dealers.AddRange(Query(db.Dealers, tracking));
        data.DealerContracts.AddRange(Query(db.DealerContracts, tracking));
        data.DealerTerritoryAssignments.AddRange(Query(db.DealerTerritoryAssignments, tracking));
        data.DealerCustomerAssignments.AddRange(Query(db.DealerCustomerAssignments, tracking));
        data.DealerTargets.AddRange(Query(db.DealerTargets, tracking));
        data.DealerFinancialSnapshots.AddRange(Query(db.DealerFinancialSnapshots, tracking));
        data.DealerPerformanceSnapshots.AddRange(Query(db.DealerPerformanceSnapshots, tracking));
        data.DealerStatusHistory.AddRange(Query(db.DealerStatusHistory, tracking));
        data.WorkItems.AddRange(Query(db.WorkItems, tracking));
        data.Users.AddRange(Query(db.Users, tracking));
        data.ExternalIdentities.AddRange(Query(db.ExternalIdentities, tracking));
        data.UserSessions.AddRange(Query(db.UserSessions, tracking));
        data.UserRoleAssignments.AddRange(Query(db.UserRoleAssignments, tracking));
        data.SecurityAuditEvents.AddRange(Query(db.SecurityAuditEvents, tracking));
        data.Companies.AddRange(Query(db.Companies, tracking));
        data.OrganizationUnits.AddRange(Query(db.OrganizationUnits, tracking));
        data.Territories.AddRange(Query(db.Territories, tracking));
        data.OrganizationChanges.AddRange(Query(db.OrganizationChanges, tracking));
        return data;
    }

    private static List<T> Query<T>(DbSet<T> set, bool tracking) where T : class =>
        tracking ? set.ToList() : set.AsNoTracking().ToList();

    private static Dictionary<Type, HashSet<Guid>> KnownIds(CrmDataSet data) => new()
    {
        [typeof(Crm.Domain.SelfService.PortalRequest)] = Ids(data.PortalRequests),
        [typeof(Crm.Domain.SelfService.MobileVisit)] = Ids(data.MobileVisits),
        [typeof(Crm.Domain.SelfService.MobileOperationReceipt)] = Ids(data.MobileOperationReceipts),
        [typeof(Crm.Domain.Customers.Customer)] = Ids(data.Customers),
        [typeof(Crm.Domain.Customers.CustomerContact)] = Ids(data.CustomerContacts),
        [typeof(Crm.Domain.Customers.CustomerAddress)] = Ids(data.CustomerAddresses),
        [typeof(Crm.Domain.Customers.CustomerTimelineEvent)] = Ids(data.CustomerTimelineEvents),
        [typeof(Crm.Domain.Customers.CustomerOwnershipHistory)] = Ids(data.CustomerOwnershipHistory),
        [typeof(Crm.Domain.Customers.CustomerDuplicateCandidate)] = Ids(data.CustomerDuplicateCandidates),
        [typeof(Crm.Domain.Customers.CustomerMergeOperation)] = Ids(data.CustomerMergeOperations),
        [typeof(Crm.Domain.Sales.Lead)] = Ids(data.Leads),
        [typeof(Crm.Domain.Sales.LeadStatusHistory)] = Ids(data.LeadStatusHistory),
        [typeof(Crm.Domain.Sales.Opportunity)] = Ids(data.Opportunities),
        [typeof(Crm.Domain.Sales.OpportunityStageHistory)] = Ids(data.OpportunityStageHistory),
        [typeof(Crm.Domain.Sales.OpportunityActivity)] = Ids(data.OpportunityActivities),
        [typeof(Crm.Domain.Commercial.Quote)] = Ids(data.Quotes),
        [typeof(Crm.Domain.Commercial.QuoteLine)] = Ids(data.QuoteLines),
        [typeof(Crm.Domain.Commercial.QuoteStatusHistory)] = Ids(data.QuoteStatusHistory),
        [typeof(Crm.Domain.Commercial.QuoteApprovalDecision)] = Ids(data.QuoteApprovalDecisions),
        [typeof(Crm.Domain.Commercial.OrderRequest)] = Ids(data.OrderRequests),
        [typeof(Crm.Domain.Commercial.OrderCreditDecision)] = Ids(data.OrderCreditDecisions),
        [typeof(Crm.Domain.Commercial.OrderStatusHistory)] = Ids(data.OrderStatusHistory),
        [typeof(Crm.Domain.Commercial.OrderIntegrationMessage)] = Ids(data.OrderIntegrationMessages),
        [typeof(Crm.Domain.Commercial.OrderIntegrationAttempt)] = Ids(data.OrderIntegrationAttempts),
        [typeof(Crm.Domain.Channel.Dealer)] = Ids(data.Dealers),
        [typeof(Crm.Domain.Channel.DealerContract)] = Ids(data.DealerContracts),
        [typeof(Crm.Domain.Channel.DealerTerritoryAssignment)] = Ids(data.DealerTerritoryAssignments),
        [typeof(Crm.Domain.Channel.DealerCustomerAssignment)] = Ids(data.DealerCustomerAssignments),
        [typeof(Crm.Domain.Channel.DealerTarget)] = Ids(data.DealerTargets),
        [typeof(Crm.Domain.Channel.DealerFinancialSnapshot)] = Ids(data.DealerFinancialSnapshots),
        [typeof(Crm.Domain.Channel.DealerPerformanceSnapshot)] = Ids(data.DealerPerformanceSnapshots),
        [typeof(Crm.Domain.Channel.DealerStatusHistory)] = Ids(data.DealerStatusHistory),
        [typeof(Crm.Domain.Work.CrmWorkItem)] = Ids(data.WorkItems),
        [typeof(Crm.Domain.Identity.CrmUser)] = Ids(data.Users),
        [typeof(Crm.Domain.Identity.ExternalIdentity)] = Ids(data.ExternalIdentities),
        [typeof(Crm.Domain.Identity.UserSession)] = Ids(data.UserSessions),
        [typeof(Crm.Domain.Identity.UserRoleAssignment)] = Ids(data.UserRoleAssignments),
        [typeof(Crm.Domain.Identity.SecurityAuditEvent)] = Ids(data.SecurityAuditEvents),
        [typeof(Crm.Domain.Organization.Company)] = Ids(data.Companies),
        [typeof(Crm.Domain.Organization.OrganizationUnit)] = Ids(data.OrganizationUnits),
        [typeof(Crm.Domain.Organization.Territory)] = Ids(data.Territories),
        [typeof(Crm.Domain.Organization.OrganizationChange)] = Ids(data.OrganizationChanges)
    };

    private void AddNew(CrmDataSet data, IReadOnlyDictionary<Type, HashSet<Guid>> known)
    {
        AddNew(db.PortalRequests, data.PortalRequests, known);
        AddNew(db.MobileVisits, data.MobileVisits, known);
        AddNew(db.MobileOperationReceipts, data.MobileOperationReceipts, known);
        AddNew(db.Customers, data.Customers, known);
        AddNew(db.CustomerContacts, data.CustomerContacts, known);
        AddNew(db.CustomerAddresses, data.CustomerAddresses, known);
        AddNew(db.CustomerTimelineEvents, data.CustomerTimelineEvents, known);
        AddNew(db.CustomerOwnershipHistory, data.CustomerOwnershipHistory, known);
        AddNew(db.CustomerDuplicateCandidates, data.CustomerDuplicateCandidates, known);
        AddNew(db.CustomerMergeOperations, data.CustomerMergeOperations, known);
        AddNew(db.Leads, data.Leads, known);
        AddNew(db.LeadStatusHistory, data.LeadStatusHistory, known);
        AddNew(db.Opportunities, data.Opportunities, known);
        AddNew(db.OpportunityStageHistory, data.OpportunityStageHistory, known);
        AddNew(db.OpportunityActivities, data.OpportunityActivities, known);
        AddNew(db.Quotes, data.Quotes, known);
        AddNew(db.QuoteLines, data.QuoteLines, known);
        AddNew(db.QuoteStatusHistory, data.QuoteStatusHistory, known);
        AddNew(db.QuoteApprovalDecisions, data.QuoteApprovalDecisions, known);
        AddNew(db.OrderRequests, data.OrderRequests, known);
        AddNew(db.OrderCreditDecisions, data.OrderCreditDecisions, known);
        AddNew(db.OrderStatusHistory, data.OrderStatusHistory, known);
        AddNew(db.OrderIntegrationMessages, data.OrderIntegrationMessages, known);
        AddNew(db.OrderIntegrationAttempts, data.OrderIntegrationAttempts, known);
        AddNew(db.Dealers, data.Dealers, known);
        AddNew(db.DealerContracts, data.DealerContracts, known);
        AddNew(db.DealerTerritoryAssignments, data.DealerTerritoryAssignments, known);
        AddNew(db.DealerCustomerAssignments, data.DealerCustomerAssignments, known);
        AddNew(db.DealerTargets, data.DealerTargets, known);
        AddNew(db.DealerFinancialSnapshots, data.DealerFinancialSnapshots, known);
        AddNew(db.DealerPerformanceSnapshots, data.DealerPerformanceSnapshots, known);
        AddNew(db.DealerStatusHistory, data.DealerStatusHistory, known);
        AddNew(db.WorkItems, data.WorkItems, known);
        AddNew(db.Users, data.Users, known);
        AddNew(db.ExternalIdentities, data.ExternalIdentities, known);
        AddNew(db.UserSessions, data.UserSessions, known);
        AddNew(db.UserRoleAssignments, data.UserRoleAssignments, known);
        AddNew(db.SecurityAuditEvents, data.SecurityAuditEvents, known);
        AddNew(db.Companies, data.Companies, known);
        AddNew(db.OrganizationUnits, data.OrganizationUnits, known);
        AddNew(db.Territories, data.Territories, known);
        AddNew(db.OrganizationChanges, data.OrganizationChanges, known);
    }

    private static HashSet<Guid> Ids<T>(IEnumerable<T> values) where T : Entity => values.Select(x => x.Id).ToHashSet();

    private static void AddNew<T>(DbSet<T> set, IEnumerable<T> values, IReadOnlyDictionary<Type, HashSet<Guid>> known)
        where T : Entity
    {
        foreach (var value in values.Where(x => !known[typeof(T)].Contains(x.Id))) set.Add(value);
    }

    private static void RemoveMissing<T>(DbSet<T> set, IEnumerable<T> values,
        IReadOnlyDictionary<Type, HashSet<Guid>> known) where T : Entity
    {
        var retained = values.Select(x => x.Id).ToHashSet();
        var removed = known[typeof(T)].Where(id => !retained.Contains(id)).ToHashSet();
        if (removed.Count == 0) return;
        foreach (var entity in set.Local.Where(x => removed.Contains(x.Id)).ToArray()) set.Remove(entity);
    }
}
