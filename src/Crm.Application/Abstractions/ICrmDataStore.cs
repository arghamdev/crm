using Crm.Domain.Commercial;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.Work;

namespace Crm.Application.Abstractions;

/// <summary>
/// Loads one entity table on first access. Persistent stores implement this so a request only
/// materializes the tables its use case actually touches instead of the whole database.
/// </summary>
public interface ICrmDataSetSource
{
    List<T> Load<T>() where T : class;

    /// <summary>Loads only the rows matching <paramref name="predicate"/> (tracked in a write) without loading the table.</summary>
    List<T> Find<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class;

    /// <summary>Schedules an insert without loading the table (append-only logs and child records).</summary>
    void Append<T>(T entity) where T : class;
}

public sealed class CrmDataSet
{
    private readonly ICrmDataSetSource? _source;
    private readonly Dictionary<Type, object> _sets = [];

    /// <summary>Creates a fully materialized, empty data set (used by the in-memory store and tests).</summary>
    public CrmDataSet()
    {
        // Materialize every list up front so concurrent readers never mutate the lazy cache.
        foreach (var property in typeof(CrmDataSet).GetProperties()) property.GetValue(this);
    }

    /// <summary>Creates a data set whose tables are loaded on demand from <paramref name="source"/>.</summary>
    public CrmDataSet(ICrmDataSetSource source) => _source = source;

    public IReadOnlyCollection<Type> LoadedTypes => _sets.Keys;

    public bool IsLoaded<T>() => _sets.ContainsKey(typeof(T));

    /// <summary>
    /// Returns the rows matching <paramref name="predicate"/>. For a persistent store this queries only those rows
    /// instead of loading the table, so use it for point lookups and per-parent children (sessions, history, audit).
    /// The predicate must be translatable (property comparisons only); apply domain methods to the result.
    /// </summary>
    public List<T> Find<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class =>
        _source is null || _sets.ContainsKey(typeof(T))
            ? Set<T>().Where(predicate.Compile()).ToList()
            : _source.Find(predicate);

    /// <summary>
    /// Adds a new row without loading its table. If the table was already loaded in this unit of work the row is
    /// added to that list as well, so later enumeration in the same command sees it.
    /// </summary>
    public void Append<T>(T entity) where T : class
    {
        if (_source is null || _sets.ContainsKey(typeof(T))) Set<T>().Add(entity);
        else _source.Append(entity);
    }

    /// <summary>Typed access to a table's list, used by generic infrastructure such as the in-memory query source.</summary>
    public List<T> Table<T>() where T : class => Set<T>();

    private List<T> Set<T>() where T : class
    {
        if (_sets.TryGetValue(typeof(T), out var existing)) return (List<T>)existing;
        var loaded = _source?.Load<T>() ?? [];
        _sets[typeof(T)] = loaded;
        return loaded;
    }

    public List<DealerCommissionPlan> DealerCommissionPlans => Set<DealerCommissionPlan>();
    public List<DealerCommissionStatement> DealerCommissionStatements => Set<DealerCommissionStatement>();
    public List<DealerEvaluation> DealerEvaluations => Set<DealerEvaluation>();
    public List<DealerGuarantee> DealerGuarantees => Set<DealerGuarantee>();
    public List<DealerTraining> DealerTrainings => Set<DealerTraining>();
    public List<CommissionPayoutMessage> CommissionPayoutMessages => Set<CommissionPayoutMessage>();
    public List<Crm.Domain.Notifications.NotificationPreference> NotificationPreferences => Set<Crm.Domain.Notifications.NotificationPreference>();
    public List<Crm.Domain.Notifications.NotificationMessage> NotificationMessages => Set<Crm.Domain.Notifications.NotificationMessage>();
    public List<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public List<RolePermissionGrant> RolePermissionGrants => Set<RolePermissionGrant>();
    public List<Crm.Domain.Service.ServiceCase> ServiceCases => Set<Crm.Domain.Service.ServiceCase>();
    public List<Crm.Domain.Service.ServiceCaseHistory> ServiceCaseHistory => Set<Crm.Domain.Service.ServiceCaseHistory>();
    public List<Crm.Domain.SelfService.PortalRequest> PortalRequests => Set<Crm.Domain.SelfService.PortalRequest>();
    public List<Crm.Domain.SelfService.MobileVisit> MobileVisits => Set<Crm.Domain.SelfService.MobileVisit>();
    public List<Crm.Domain.SelfService.MobileOperationReceipt> MobileOperationReceipts => Set<Crm.Domain.SelfService.MobileOperationReceipt>();
    public List<Customer> Customers => Set<Customer>();
    public List<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public List<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public List<Crm.Domain.Accounts.CrmActivity> CrmActivities => Set<Crm.Domain.Accounts.CrmActivity>();
    public List<Crm.Domain.Accounts.ActivityParticipant> ActivityParticipants => Set<Crm.Domain.Accounts.ActivityParticipant>();
    public List<Crm.Domain.Accounts.ClientOperation> ClientOperations => Set<Crm.Domain.Accounts.ClientOperation>();
    public List<Crm.Domain.Accounts.AccountNote> AccountNotes => Set<Crm.Domain.Accounts.AccountNote>();
    public List<Crm.Domain.Accounts.CrmDocument> CrmDocuments => Set<Crm.Domain.Accounts.CrmDocument>();
    public List<Crm.Domain.Accounts.DocumentContent> DocumentContents => Set<Crm.Domain.Accounts.DocumentContent>();
    public List<Crm.Domain.Accounts.DocumentLink> DocumentLinks => Set<Crm.Domain.Accounts.DocumentLink>();
    public List<Crm.Domain.Accounts.AccountPayment> AccountPayments => Set<Crm.Domain.Accounts.AccountPayment>();
    public List<Crm.Domain.Accounts.AccountBankAccount> AccountBankAccounts => Set<Crm.Domain.Accounts.AccountBankAccount>();
    public List<Crm.Domain.Accounts.AccountContract> AccountContracts => Set<Crm.Domain.Accounts.AccountContract>();
    public List<Crm.Domain.Accounts.AccountProject> AccountProjects => Set<Crm.Domain.Accounts.AccountProject>();
    public List<Crm.Domain.Accounts.AccountParticipation> AccountParticipations => Set<Crm.Domain.Accounts.AccountParticipation>();
    public List<Crm.Domain.Accounts.AccountAllocation> AccountAllocations => Set<Crm.Domain.Accounts.AccountAllocation>();
    public List<Crm.Domain.Accounts.Campaign> Campaigns => Set<Crm.Domain.Accounts.Campaign>();
    public List<Crm.Domain.Accounts.CampaignMember> CampaignMembers => Set<Crm.Domain.Accounts.CampaignMember>();
    public List<Crm.Domain.Accounts.TargetList> TargetLists => Set<Crm.Domain.Accounts.TargetList>();
    public List<Crm.Domain.Accounts.TargetListMember> TargetListMembers => Set<Crm.Domain.Accounts.TargetListMember>();
    public List<Crm.Domain.Accounts.Survey> Surveys => Set<Crm.Domain.Accounts.Survey>();
    public List<Crm.Domain.Accounts.SurveyResponse> SurveyResponses => Set<Crm.Domain.Accounts.SurveyResponse>();
    public List<Crm.Domain.FollowUps.SlaPolicy> FollowUpSlaPolicies => Set<Crm.Domain.FollowUps.SlaPolicy>();
    public List<Crm.Domain.FollowUps.FollowUpTemplate> FollowUpTemplates => Set<Crm.Domain.FollowUps.FollowUpTemplate>();
    public List<Crm.Domain.FollowUps.FollowUpTemplateStage> FollowUpTemplateStages => Set<Crm.Domain.FollowUps.FollowUpTemplateStage>();
    public List<Crm.Domain.FollowUps.FollowUpQueue> FollowUpQueues => Set<Crm.Domain.FollowUps.FollowUpQueue>();
    public List<Crm.Domain.FollowUps.FollowUpQueueMember> FollowUpQueueMembers => Set<Crm.Domain.FollowUps.FollowUpQueueMember>();
    public List<Crm.Domain.FollowUps.FollowUpCase> FollowUpCases => Set<Crm.Domain.FollowUps.FollowUpCase>();
    public List<Crm.Domain.FollowUps.FollowUpStage> FollowUpStages => Set<Crm.Domain.FollowUps.FollowUpStage>();
    public List<Crm.Domain.FollowUps.FollowUpChecklistItem> FollowUpChecklistItems => Set<Crm.Domain.FollowUps.FollowUpChecklistItem>();
    public List<Crm.Domain.FollowUps.FollowUpItem> FollowUpItems => Set<Crm.Domain.FollowUps.FollowUpItem>();
    public List<Crm.Domain.FollowUps.FollowUpReferral> FollowUpReferrals => Set<Crm.Domain.FollowUps.FollowUpReferral>();
    public List<Crm.Domain.FollowUps.FollowUpDocument> FollowUpDocuments => Set<Crm.Domain.FollowUps.FollowUpDocument>();
    public List<Crm.Domain.FollowUps.FollowUpApproval> FollowUpApprovals => Set<Crm.Domain.FollowUps.FollowUpApproval>();
    public List<Crm.Domain.FollowUps.FollowUpEvent> FollowUpEvents => Set<Crm.Domain.FollowUps.FollowUpEvent>();
    public List<Crm.Domain.FollowUps.FollowUpActivityLink> FollowUpActivityLinks => Set<Crm.Domain.FollowUps.FollowUpActivityLink>();
    public List<Crm.Domain.FollowUps.FollowUpSavedView> FollowUpSavedViews => Set<Crm.Domain.FollowUps.FollowUpSavedView>();
    public List<Crm.Domain.FollowUps.FollowUpDraft> FollowUpDrafts => Set<Crm.Domain.FollowUps.FollowUpDraft>();
    public List<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public List<CustomerLogo> CustomerLogos => Set<CustomerLogo>();
    public List<CustomerTimelineEvent> CustomerTimelineEvents => Set<CustomerTimelineEvent>();
    public List<CustomerOwnershipHistory> CustomerOwnershipHistory => Set<CustomerOwnershipHistory>();
    public List<CustomerDuplicateCandidate> CustomerDuplicateCandidates => Set<CustomerDuplicateCandidate>();
    public List<CustomerMergeOperation> CustomerMergeOperations => Set<CustomerMergeOperation>();
    public List<Lead> Leads => Set<Lead>();
    public List<LeadStatusHistory> LeadStatusHistory => Set<LeadStatusHistory>();
    public List<Opportunity> Opportunities => Set<Opportunity>();
    public List<OpportunityStageHistory> OpportunityStageHistory => Set<OpportunityStageHistory>();
    public List<OpportunityActivity> OpportunityActivities => Set<OpportunityActivity>();
    public List<Quote> Quotes => Set<Quote>();
    public List<QuoteLine> QuoteLines => Set<QuoteLine>();
    public List<QuoteStatusHistory> QuoteStatusHistory => Set<QuoteStatusHistory>();
    public List<QuoteApprovalDecision> QuoteApprovalDecisions => Set<QuoteApprovalDecision>();
    public List<OrderRequest> OrderRequests => Set<OrderRequest>();
    public List<OrderCreditDecision> OrderCreditDecisions => Set<OrderCreditDecision>();
    public List<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public List<OrderIntegrationMessage> OrderIntegrationMessages => Set<OrderIntegrationMessage>();
    public List<OrderIntegrationAttempt> OrderIntegrationAttempts => Set<OrderIntegrationAttempt>();
    public List<Dealer> Dealers => Set<Dealer>();
    public List<DealerContract> DealerContracts => Set<DealerContract>();
    public List<DealerTerritoryAssignment> DealerTerritoryAssignments => Set<DealerTerritoryAssignment>();
    public List<DealerCustomerAssignment> DealerCustomerAssignments => Set<DealerCustomerAssignment>();
    public List<DealerTarget> DealerTargets => Set<DealerTarget>();
    public List<DealerFinancialSnapshot> DealerFinancialSnapshots => Set<DealerFinancialSnapshot>();
    public List<DealerPerformanceSnapshot> DealerPerformanceSnapshots => Set<DealerPerformanceSnapshot>();
    public List<DealerStatusHistory> DealerStatusHistory => Set<DealerStatusHistory>();
    public List<CrmWorkItem> WorkItems => Set<CrmWorkItem>();
    public List<CrmUser> Users => Set<CrmUser>();
    public List<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public List<UserSession> UserSessions => Set<UserSession>();
    public List<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public List<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public List<Company> Companies => Set<Company>();
    public List<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public List<Territory> Territories => Set<Territory>();
    public List<OrganizationChange> OrganizationChanges => Set<OrganizationChange>();
}

public interface ICrmDataStore
{
    TResult Read<TResult>(Func<CrmDataSet, TResult> query);
    TResult Write<TResult>(Func<CrmDataSet, TResult> command);
}
