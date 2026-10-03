using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Commercial;
using Crm.Domain.Customers;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.Work;

namespace Crm.Application.Services;

public sealed class CrmApplicationService(ICrmDataStore store, IAccessSnapshotService access, ICustomerQueryStore? customerQueries = null) : ICrmApplicationService
{
    public DashboardDto GetDashboard(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data =>
        {
            var opportunities = data.Opportunities
                .Where(x => InContext(snapshot, organization, "Dashboard.Read", x) &&
                    CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner) &&
                    x.Stage is not OpportunityStage.Won and not OpportunityStage.Lost).ToList();
            var leads = data.Leads.Where(x => InContext(snapshot, organization, "Dashboard.Read", x) &&
                CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner)).ToList();
            var quotes = data.Quotes.Where(x => InContext(snapshot, organization, "Dashboard.Read", x)).ToList();
            var workItems = data.WorkItems.Where(x => x.AssignedToUserId == currentUserId &&
                InContext(snapshot, organization, "Dashboard.Read", x)).ToList();
            return new DashboardDto(
                opportunities.Sum(x => x.Value),
                leads.Count(x => x.Status != LeadStatus.Converted),
                quotes.Count(x => x.Status == QuoteStatus.PendingApproval),
                workItems.Count(x => !x.IsDone),
                opportunities.OrderByDescending(x => x.Probability).Take(3).Select(Map).ToList(),
                workItems.Where(x => !x.IsDone).OrderBy(x => x.DueAtUtc).Take(5).Select(Map).ToList());
        });
    }

    public IReadOnlyList<CustomerDto> GetCustomers(Guid currentUserId, OrganizationSelection organization, string? query = null)
        => SearchCustomers(currentUserId, organization, query, 1, 100).Items;

    public PagedResult<CustomerDto> SearchCustomers(Guid currentUserId, OrganizationSelection organization, string? query = null, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var snapshot = RequiredSnapshot(currentUserId);
        if (customerQueries is not null)
        {
            var grants = snapshot.PermissionScopeGrants.Where(x =>
                x.CompanyId.Equals(organization.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.Permission.Equals("Customer.Read", StringComparison.OrdinalIgnoreCase)).ToList();
            var companyWide = grants.Any(x => x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase));
            var branches = grants.Where(x => x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase)).Select(x => x.ScopeId).Distinct().ToList();
            var territories = grants.Where(x => x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase)).Select(x => x.ScopeId).Distinct().ToList();
            var result = customerQueries.Search(new CustomerQuerySpec(organization.CompanyId, organization.BranchId,
                organization.TerritoryId, companyWide, branches, territories, query?.Trim(), page, pageSize));
            var actualPage = result.TotalCount == 0 ? 1 : Math.Min(page, (int)Math.Ceiling(result.TotalCount / (double)pageSize));
            if (actualPage != page)
                result = customerQueries.Search(new CustomerQuerySpec(organization.CompanyId, organization.BranchId,
                    organization.TerritoryId, companyWide, branches, territories, query?.Trim(), actualPage, pageSize));
            var items = result.Items.Select(x => Map(x,
                result.CustomerIdsWithActiveContacts.Contains(x.Id), result.CustomerIdsWithActiveAddresses.Contains(x.Id), snapshot)).ToList();
            return new PagedResult<CustomerDto>(items, actualPage, pageSize, result.TotalCount, query?.Trim());
        }
        return store.Read(data =>
        {
            IEnumerable<Customer> result = data.Customers.Where(x => InContext(snapshot, organization, "Customer.Read", x));
            if (!string.IsNullOrWhiteSpace(query))
            {
                var term = query.Trim();
                result = result.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    x.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            var ordered = result.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id);
            var total = ordered.Count();
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => Map(data, x, snapshot)).ToList();
            var actualPage = total == 0 ? 1 : Math.Min(page, (int)Math.Ceiling(total / (double)pageSize));
            if (actualPage != page)
                items = ordered.Skip((actualPage - 1) * pageSize).Take(pageSize).Select(x => Map(data, x, snapshot)).ToList();
            return new PagedResult<CustomerDto>(items, actualPage, pageSize, total, query?.Trim());
        });
    }

    public CustomerDto? GetCustomer(Guid currentUserId, OrganizationSelection organization, Guid id)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.Customers
            .Where(x => x.Id == id && InContext(snapshot, organization, "Customer.Read", x))
            .Select(x => Map(data, x, snapshot)).SingleOrDefault());
    }

    public CustomerDto CreateCustomer(Guid currentUserId, OrganizationSelection organization, CreateCustomerCommand command, CustomerLogoUpload? logo = null)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        // The full customer form (profile) derives the identifier, phone and email from its kind-specific fields.
        var form = command.Profile is null ? null : CustomerFormRules.Normalize(command.Kind, command.Profile, command.Contacts, command.PrimaryEmail);
        var nationalId = form is null ? command.NationalId : form.NationalId;
        var primaryPhone = form is null ? command.PrimaryPhone : form.PrimaryPhone;
        var primaryEmail = form is null ? command.PrimaryEmail : form.PrimaryEmail;
        return store.Write(data =>
        {
            Ensure(command.Name, nameof(command.Name));
            if (string.IsNullOrWhiteSpace(command.Owner)) throw new InvalidOperationException("مالک حساب الزامی است.");
            var scope = ResolveWriteScope(data, snapshot, organization, "Customer.Create", command.BranchId);
            var matches = CustomerDataQualityRules.FindDuplicates(data.Customers, organization.CompanyId, command.Name,
                command.City, nationalId, primaryPhone, primaryEmail);
            if (matches.Any(x => x.IsExact))
                throw new InvalidOperationException("شناسه ملی مشتری دیگری در همین شرکت ثبت شده است.");
            if (matches.Count > 0 && !command.AllowPotentialDuplicate)
                throw new InvalidOperationException("رکورد مشابه پیدا شد؛ بررسی کنید یا ایجاد رکورد مستقل را همراه دلیل تأیید کنید.");
            if (matches.Count > 0 && string.IsNullOrWhiteSpace(command.DuplicateReason))
                throw new InvalidOperationException("برای ایجاد رکورد مشابه، ثبت دلیل الزامی است.");
            var customer = new Customer(Guid.NewGuid(), RecordCodes.Next(data.Customers.Select(x => x.Code), "CUS-", 481, 5), command.Name,
                command.City, command.Owner, organization.CompanyId, scope.Id, scope.Name, organization.TerritoryId,
                command.Segment, 1_000_000_000m, command.Kind, nationalId, primaryPhone, primaryEmail);
            data.Customers.Add(customer);
            data.Append<Crm.Domain.Customers.CustomerOwnershipHistory>(new CustomerOwnershipHistory(Guid.NewGuid(), customer.CompanyId,
                customer.Id, customer.BranchId, customer.TerritoryId, customer.Owner, DateTimeOffset.UtcNow,
                "ایجاد مشتری", currentUserId));
            data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
                CustomerTimelineType.Created, "مشتری ایجاد شد", command.DuplicateReason ?? "ثبت اولیه",
                DateTimeOffset.UtcNow, "CRM", null, currentUserId));
            if (form is not null)
            {
                CustomerProfileWriter.Apply(data, customer, form, logo, removeLogo: false);
                CustomerProfileWriter.AddContacts(data, customer, form);
            }
            else if (!string.IsNullOrWhiteSpace(command.PrimaryPhone) || !string.IsNullOrWhiteSpace(command.PrimaryEmail))
                data.CustomerContacts.Add(new CustomerContact(Guid.NewGuid(), customer.CompanyId, customer.Id,
                    "تماس اصلی", "Primary", command.PrimaryPhone, command.PrimaryEmail, true, ContactConsentStatus.Unknown));
            foreach (var match in matches)
                data.CustomerDuplicateCandidates.Add(new CustomerDuplicateCandidate(Guid.NewGuid(), customer.CompanyId,
                    customer.Id, match.Customer.Id, match.Score, match.Reasons, DateTimeOffset.UtcNow));
            return Map(data, customer, snapshot);
        });
    }

    public IReadOnlyList<LeadDto> GetLeads(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.Leads
            .Where(x => x.Status != LeadStatus.Converted && InContext(snapshot, organization, "Lead.Read", x) &&
                CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner))
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => Map(x, snapshot)).ToList());
    }

    public LeadDto CreateLead(Guid currentUserId, OrganizationSelection organization, CreateLeadCommand command)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            Ensure(command.Name, nameof(command.Name));
            Ensure(command.Contact ?? string.Empty, nameof(command.Contact));
            var scope = ResolveWriteScope(data, snapshot, organization, "Lead.Create", command.BranchId);
            var ownerUserId = command.OwnerUserId ?? data.Users
                .SingleOrDefault(x => x.DisplayName.Equals(command.Owner, StringComparison.OrdinalIgnoreCase))?.Id;
            var lead = new Lead(Guid.NewGuid(), RecordCodes.Next(data.Leads.Select(x => x.Code), "LD-1405-", 118, 3), command.Name, command.Contact ?? string.Empty,
                command.Source, command.Owner ?? string.Empty, organization.CompanyId, scope.Id, command.TerritoryId ?? organization.TerritoryId,
                ownerUserId: ownerUserId, phone: command.Phone, email: command.Email);
            data.Leads.Add(lead);
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, null, lead.Status, "ثبت سرنخ", currentUserId, DateTimeOffset.UtcNow));
            return Map(lead, snapshot);
        });
    }

    public OpportunityDto ConvertLead(Guid currentUserId, OrganizationSelection organization, Guid id)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var lead = data.Leads.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, "Lead.Convert", x) &&
                CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner)) ??
                throw new UnauthorizedAccessException("Lead is outside the current organization context.");
            var now = DateTimeOffset.UtcNow;
            if (lead.Status is LeadStatus.New or LeadStatus.Assigned)
            {
                var from = lead.Status;
                lead.MarkContacted(now, "تکمیل تبدیل سرنخ", now.AddDays(1), "تماس پیش از تبدیل");
                data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                    lead.TerritoryId, lead.Id, from, lead.Status, "تماس پیش از تبدیل", currentUserId, now));
            }
            if (lead.Status == LeadStatus.Contacted)
            {
                var from = lead.Status;
                lead.Qualify(Math.Max(75, lead.Score), "احراز صلاحیت پیش از تبدیل");
                data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                    lead.TerritoryId, lead.Id, from, lead.Status, "احراز صلاحیت پیش از تبدیل", currentUserId, now));
            }
            if (lead.Status != LeadStatus.Qualified)
                throw new InvalidOperationException("فقط سرنخ واجد شرایط قابل تبدیل است.");
            var customer = data.Customers.FirstOrDefault(x => x.Status != CustomerStatus.Inactive && x.CompanyId.Equals(lead.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.Name.Equals(lead.Name, StringComparison.OrdinalIgnoreCase));
            if (customer is null)
            {
                var branch = data.OrganizationUnits.Single(x => x.CompanyId.Equals(lead.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                    x.UnitId.Equals(lead.BranchId, StringComparison.OrdinalIgnoreCase));
                customer = new Customer(Guid.NewGuid(), RecordCodes.Next(data.Customers.Select(x => x.Code), "CUS-", 481, 5), lead.Name, string.Empty,
                    lead.Owner, lead.CompanyId, lead.BranchId, branch.Name, lead.TerritoryId, "تبدیل سرنخ", 0,
                    primaryPhone: lead.Phone ?? lead.Contact, primaryEmail: lead.Email, dataSource: "Lead Conversion");
                data.Customers.Add(customer);
                data.Append<Crm.Domain.Customers.CustomerOwnershipHistory>(new CustomerOwnershipHistory(Guid.NewGuid(), customer.CompanyId,
                    customer.Id, customer.BranchId, customer.TerritoryId, customer.Owner, DateTimeOffset.UtcNow,
                    "تبدیل سرنخ", currentUserId));
                data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
                    CustomerTimelineType.Created, "مشتری از سرنخ ساخته شد", lead.Code, DateTimeOffset.UtcNow,
                    "CRM", lead.Code, currentUserId));
            }
            var opportunity = new Opportunity(Guid.NewGuid(), RecordCodes.Next(data.Opportunities.Select(x => x.Code), "OP-", 2041, 4),
                $"فرصت همکاری با {lead.Name}", customer.Name, customer.Id, 2_500_000_000m, lead.Owner, lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.OwnerUserId, lead.Id, now.AddDays(30), lead.Source);
            opportunity.Update(opportunity.Title, opportunity.Value, opportunity.ExpectedCloseAtUtc, opportunity.Source,
                null, OpportunityRiskLevel.Medium, "جلسه کشف نیاز", now.AddDays(2));
            data.Opportunities.Add(opportunity);
            data.Append<Crm.Domain.Sales.OpportunityStageHistory>(new OpportunityStageHistory(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, null, opportunity.Stage,
                opportunity.Probability, "تبدیل سرنخ", currentUserId, now));
            data.OpportunityActivities.Add(new OpportunityActivity(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, OpportunityActivityType.Task,
                "شروع فرایند فروش", "فرصت از سرنخ ایجاد شد", now, currentUserId,
                opportunity.NextAction, opportunity.NextActionAtUtc));
            var qualified = lead.Status;
            lead.Convert(customer.Id, opportunity.Id, now);
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, qualified, lead.Status, "تبدیل به مشتری و فرصت", currentUserId, now));
            data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
                CustomerTimelineType.LeadConverted, "سرنخ به فرصت تبدیل شد", opportunity.Code, now,
                "CRM", lead.Code, currentUserId));
            return Map(opportunity);
        });
    }

    public IReadOnlyList<OpportunityDto> GetOpportunities(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.Opportunities
            .Where(x => InContext(snapshot, organization, "Opportunity.Read", x) &&
                CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner)).Select(Map).ToList());
    }

    public OpportunityDto AdvanceOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var opportunity = data.Opportunities.SingleOrDefault(x => x.Id == id &&
                InContext(snapshot, organization, "Opportunity.Advance", x) &&
                CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner)) ??
                throw new UnauthorizedAccessException("Opportunity is outside the current organization context.");
            opportunity.Advance();
            return Map(opportunity);
        });
    }

    public IReadOnlyList<QuoteDto> GetQuotes(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.Quotes
            .Where(x => InContext(snapshot, organization, "Quote.Read", x))
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => Map(x, snapshot)).ToList());
    }

    public QuoteDto CreateQuote(Guid currentUserId, OrganizationSelection organization, CreateQuoteCommand command)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var scope = ResolveWriteScope(data, snapshot, organization, "Quote.Create", command.BranchId);
            var customer = command.CustomerId != Guid.Empty
                ? data.Customers.SingleOrDefault(x => x.Id == command.CustomerId && InContext(snapshot, organization, "Customer.Read", x))
                : data.Customers.SingleOrDefault(x => x.Name.Equals(command.Customer?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    InContext(snapshot, organization, "Customer.Read", x));
            if (customer is null) throw new InvalidOperationException("مشتری معتبر و مجاز انتخاب نشده است.");
            if (customer.Status == CustomerStatus.Inactive)
                throw new InvalidOperationException("برای مشتری غیرفعال نمی‌توان پیشنهاد قیمت جدید ثبت کرد.");
            var opportunity = command.OpportunityId.HasValue
                ? data.Opportunities.SingleOrDefault(x => x.Id == command.OpportunityId && x.CustomerId == customer.Id &&
                    InContext(snapshot, organization, "Opportunity.Read", x) &&
                    CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner))
                : data.Opportunities.FirstOrDefault(x => x.CustomerId == customer.Id &&
                    x.Title.Equals(command.Opportunity?.Trim(), StringComparison.OrdinalIgnoreCase) && InContext(snapshot, organization, "Opportunity.Read", x) &&
                    CanManageSalesRecord(data, snapshot, currentUserId, organization.CompanyId, x.OwnerUserId, x.Owner));
            if (opportunity is null) throw new InvalidOperationException("فرصت معتبر و مرتبط با مشتری انتخاب نشده است.");
            var quote = new Quote(Guid.NewGuid(), RecordCodes.Next(data.Quotes.Select(x => x.Code), "Q-1405-", 31, 3), customer.Name, customer.Id,
                opportunity.Title, opportunity.Id, command.Amount, command.DiscountPercent, command.MarginPercent,
                organization.CompanyId, scope.Id, organization.TerritoryId);
            data.Quotes.Add(quote);
            return Map(quote, snapshot);
        });
    }

    public QuoteDto DecideQuote(Guid currentUserId, OrganizationSelection organization, Guid id, bool approved)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var quote = data.Quotes.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, "Quote.Approve", x)) ??
                throw new UnauthorizedAccessException("Quote is outside the current organization context.");
            if (approved) quote.Approve(); else quote.Reject();
            return Map(quote, snapshot);
        });
    }

    public IReadOnlyList<WorkItemDto> GetWorkItems(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.WorkItems.Where(x => x.AssignedToUserId == currentUserId &&
                InContext(snapshot, organization, "WorkQueue.Read", x))
            .OrderBy(x => x.IsDone).ThenBy(x => x.DueAtUtc).Select(Map).ToList());
    }

    public WorkItemDto CompleteWorkItem(Guid currentUserId, OrganizationSelection organization, Guid id)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var item = data.WorkItems.SingleOrDefault(x => x.Id == id && x.AssignedToUserId == currentUserId &&
                InContext(snapshot, organization, "WorkQueue.Complete", x)) ??
                throw new UnauthorizedAccessException("Work item is outside the current organization context.");
            item.Complete();
            return Map(item);
        });
    }

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("No active access snapshot was found.");

    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        entity.CompanyId.Equals(organization.CompanyId, StringComparison.OrdinalIgnoreCase) &&
        (organization.BranchId is null || entity.BranchId.Equals(organization.BranchId, StringComparison.OrdinalIgnoreCase)) &&
        (organization.TerritoryId is null || string.Equals(entity.TerritoryId, organization.TerritoryId, StringComparison.OrdinalIgnoreCase)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);

    private static (string Id, string Name) ResolveWriteScope(CrmDataSet data, AccessSnapshot snapshot,
        OrganizationSelection organization, string permission, string branchId)
    {
        var branch = data.OrganizationUnits.SingleOrDefault(x =>
            x.UnitId.Equals(branchId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.CompanyId.Equals(organization.CompanyId, StringComparison.OrdinalIgnoreCase) &&
            x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active) ??
            throw new UnauthorizedAccessException("The selected branch does not belong to the current company.");
        if (organization.BranchId is not null &&
            !branch.UnitId.Equals(organization.BranchId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The selected branch is outside the narrowed branch context.");
        if (!snapshot.AllowsRecord(organization.CompanyId, permission, branch.UnitId, organization.TerritoryId))
            throw new UnauthorizedAccessException("The selected branch is outside the permission scope.");
        return (branch.UnitId, branch.Name);
    }

    private static CustomerDto Map(CrmDataSet data, Customer x, AccessSnapshot snapshot)
    {
        var issues = CustomerDataQualityRules.Issues(data, x);
        return new CustomerDto(x.Id, x.Code, x.Name, x.City, x.Owner, x.CompanyId, x.Branch,
            x.BranchId, x.TerritoryId, x.Segment, x.Status, x.Balance, x.CreditLimit, x.Kind,
            x.NationalId, x.PrimaryPhone, x.PrimaryEmail, x.DataSource, x.LastSynchronizedAtUtc,
            x.Version, CustomerDataQualityRules.Score(issues)).Mask(snapshot);
    }
    private static CustomerDto Map(Customer x, bool hasActiveContact, bool hasActiveAddress, AccessSnapshot snapshot)
    {
        var issues = CustomerDataQualityRules.Issues(x, hasActiveContact, hasActiveAddress);
        return new CustomerDto(x.Id, x.Code, x.Name, x.City, x.Owner, x.CompanyId, x.Branch,
            x.BranchId, x.TerritoryId, x.Segment, x.Status, x.Balance, x.CreditLimit, x.Kind,
            x.NationalId, x.PrimaryPhone, x.PrimaryEmail, x.DataSource, x.LastSynchronizedAtUtc,
            x.Version, CustomerDataQualityRules.Score(issues)).Mask(snapshot);
    }
    private static LeadDto Map(Lead x, AccessSnapshot snapshot) => new LeadDto(x.Id, x.Code, x.Name, x.Contact, x.Source, x.Owner, x.CompanyId,
        x.BranchId, x.TerritoryId, x.Score, x.Status, x.CustomerId, x.OwnerUserId, x.Phone, x.Email,
        x.AssignedAtUtc, x.FirstContactDueAtUtc, x.FirstContactAtUtc, x.LastActivityAtUtc, x.NextAction,
        x.NextActionAtUtc, x.StatusReason, x.ConvertedOpportunityId, x.Version).Mask(snapshot);
    private static OpportunityDto Map(Opportunity x) => new(x.Id, x.Code, x.Title, x.Customer, x.Value, x.Owner,
        x.CompanyId, x.BranchId, x.TerritoryId, x.Stage, x.Probability, x.CustomerId, x.OwnerUserId,
        x.OriginLeadId, x.ExpectedCloseAtUtc, x.Source, x.NextAction, x.NextActionAtUtc, x.LastActivityAtUtc,
        x.Competitor, x.RiskLevel, x.OutcomeReason, x.ClosedAtUtc, x.Version);
    private static QuoteDto Map(Quote x, AccessSnapshot snapshot) => new QuoteDto(x.Id, x.Code, x.Customer, x.Opportunity, x.Amount, x.DiscountPercent,
        x.MarginPercent, x.CompanyId, x.BranchId, x.TerritoryId, x.Status, x.NetAmount, x.CustomerId, x.OpportunityId).Mask(snapshot);
    private static WorkItemDto Map(CrmWorkItem x) => new(x.Id, x.Title, x.Priority, x.DueAtUtc, x.IsDone);
    private static void Ensure(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
    }
    private static bool CanManageSalesRecord(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        string companyId, Guid? ownerUserId, string ownerName)
    {
        if (snapshot.ScopeGrants.Any(x => string.Equals(x.CompanyId, companyId, StringComparison.OrdinalIgnoreCase) &&
            (x.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) ||
             x.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase)))) return true;
        if (ownerUserId.HasValue) return ownerUserId.Value == userId;
        return data.Users.SingleOrDefault(x => x.Id == userId)?.DisplayName.Equals(ownerName, StringComparison.OrdinalIgnoreCase) == true;
    }
}
