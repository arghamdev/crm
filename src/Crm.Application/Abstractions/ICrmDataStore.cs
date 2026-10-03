using Crm.Domain.Commercial;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.Work;

namespace Crm.Application.Abstractions;

public sealed class CrmDataSet
{
    public List<Crm.Domain.SelfService.PortalRequest> PortalRequests { get; } = [];
    public List<Crm.Domain.SelfService.MobileVisit> MobileVisits { get; } = [];
    public List<Crm.Domain.SelfService.MobileOperationReceipt> MobileOperationReceipts { get; } = [];
    public List<Customer> Customers { get; } = [];
    public List<CustomerContact> CustomerContacts { get; } = [];
    public List<CustomerAddress> CustomerAddresses { get; } = [];
    public List<CustomerTimelineEvent> CustomerTimelineEvents { get; } = [];
    public List<CustomerOwnershipHistory> CustomerOwnershipHistory { get; } = [];
    public List<CustomerDuplicateCandidate> CustomerDuplicateCandidates { get; } = [];
    public List<CustomerMergeOperation> CustomerMergeOperations { get; } = [];
    public List<Lead> Leads { get; } = [];
    public List<LeadStatusHistory> LeadStatusHistory { get; } = [];
    public List<Opportunity> Opportunities { get; } = [];
    public List<OpportunityStageHistory> OpportunityStageHistory { get; } = [];
    public List<OpportunityActivity> OpportunityActivities { get; } = [];
    public List<Quote> Quotes { get; } = [];
    public List<QuoteLine> QuoteLines { get; } = [];
    public List<QuoteStatusHistory> QuoteStatusHistory { get; } = [];
    public List<QuoteApprovalDecision> QuoteApprovalDecisions { get; } = [];
    public List<OrderRequest> OrderRequests { get; } = [];
    public List<OrderCreditDecision> OrderCreditDecisions { get; } = [];
    public List<OrderStatusHistory> OrderStatusHistory { get; } = [];
    public List<OrderIntegrationMessage> OrderIntegrationMessages { get; } = [];
    public List<OrderIntegrationAttempt> OrderIntegrationAttempts { get; } = [];
    public List<Dealer> Dealers { get; } = [];
    public List<DealerContract> DealerContracts { get; } = [];
    public List<DealerTerritoryAssignment> DealerTerritoryAssignments { get; } = [];
    public List<DealerCustomerAssignment> DealerCustomerAssignments { get; } = [];
    public List<DealerTarget> DealerTargets { get; } = [];
    public List<DealerFinancialSnapshot> DealerFinancialSnapshots { get; } = [];
    public List<DealerPerformanceSnapshot> DealerPerformanceSnapshots { get; } = [];
    public List<DealerStatusHistory> DealerStatusHistory { get; } = [];
    public List<CrmWorkItem> WorkItems { get; } = [];
    public List<CrmUser> Users { get; } = [];
    public List<ExternalIdentity> ExternalIdentities { get; } = [];
    public List<UserSession> UserSessions { get; } = [];
    public List<UserRoleAssignment> UserRoleAssignments { get; } = [];
    public List<SecurityAuditEvent> SecurityAuditEvents { get; } = [];
    public List<Company> Companies { get; } = [];
    public List<OrganizationUnit> OrganizationUnits { get; } = [];
    public List<Territory> Territories { get; } = [];
    public List<OrganizationChange> OrganizationChanges { get; } = [];
}

public interface ICrmDataStore
{
    TResult Read<TResult>(Func<CrmDataSet, TResult> query);
    TResult Write<TResult>(Func<CrmDataSet, TResult> command);
}
