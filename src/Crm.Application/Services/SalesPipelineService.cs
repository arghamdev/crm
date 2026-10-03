using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;

namespace Crm.Application.Services;

public sealed class SalesPipelineService(ICrmDataStore store, IAccessSnapshotService access, ICrmQuerySource? querySource = null) : ISalesPipelineService
{
    private static readonly LeadStatus[] ClosedLeadStatuses =
        [LeadStatus.Disqualified, LeadStatus.Duplicate, LeadStatus.Invalid, LeadStatus.Converted];

    /// <summary>Synchronous convenience over <see cref="GetLeadsAsync"/>; returns the first page of up to 200 leads.</summary>
    public LeadListDto GetLeads(Guid currentUserId, OrganizationSelection organization, string? query = null,
        LeadStatus? status = null, bool includeClosed = false, DateTimeOffset? nowUtc = null) =>
        GetLeadsAsync(currentUserId, organization, query, status, includeClosed, 1, PageRequest.MaxPageSize, nowUtc)
            .GetAwaiter().GetResult();

    /// <summary>
    /// Filters, SLA-orders and pages leads inside the data source (SQL for the EF store) and computes the KPI tiles
    /// with aggregate queries, so neither the table nor its history is loaded into memory.
    /// </summary>
    public async Task<LeadListDto> GetLeadsAsync(Guid currentUserId, OrganizationSelection organization, string? query = null,
        LeadStatus? status = null, bool includeClosed = false, int page = 1, int pageSize = PageRequest.DefaultPageSize,
        DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var dueSoon = now.AddHours(2);
        var source = querySource ?? store as ICrmQuerySource ??
            throw new InvalidOperationException("No query source is configured for lead lists.");

        var visible = source.Query<Lead>().InScope(snapshot, organization, "Lead.Read");
        if (!ManagesAllSalesRecords(snapshot, organization.CompanyId))
        {
            var users = await source.ToListAsync(source.Query<CrmUser>().Where(x => x.Id == currentUserId), cancellationToken);
            var myName = users.SingleOrDefault()?.DisplayName ?? "\0";
            visible = visible.Where(x => x.OwnerUserId == currentUserId || x.OwnerUserId == null && x.Owner == myName);
        }

        var filtered = includeClosed ? visible : visible.Where(x => !ClosedLeadStatuses.Contains(x.Status));
        if (status.HasValue) filtered = filtered.Where(x => x.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            // Phone search is limited to users who may see phone numbers, so masking cannot be bypassed by probing.
            filtered = HasPermission(snapshot, organization.CompanyId, FieldMasking.ContactPermission)
                ? filtered.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.Contact.Contains(term) ||
                    x.Phone != null && x.Phone.Contains(term))
                : filtered.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.Contact.Contains(term));
        }

        // Same order as the SLA badge: overdue, due soon, on track, then completed/closed.
        var ordered = filtered
            .OrderBy(x => x.FirstContactAtUtc != null || ClosedLeadStatuses.Contains(x.Status) ? 3
                : x.FirstContactDueAtUtc < now ? 0
                : x.FirstContactDueAtUtc <= dueSoon ? 1 : 2)
            .ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id);
        var pageResult = await ordered.ToPageAsync(source, PageRequest.Of(page, pageSize), x => Map(x, now, snapshot), query?.Trim(), cancellationToken);

        var open = visible.Where(x => !ClosedLeadStatuses.Contains(x.Status));
        var openCount = await source.CountAsync(open, cancellationToken);
        var qualified = await source.CountAsync(open.Where(x => x.Status == LeadStatus.Qualified), cancellationToken);
        var overdue = await source.CountAsync(open.Where(x => x.FirstContactAtUtc == null && x.FirstContactDueAtUtc < now), cancellationToken);
        var average = await source.AverageAsync(open.Select(x => x.Score), cancellationToken);
        return new LeadListDto(pageResult.Items, query?.Trim(), status, includeClosed, openCount, qualified, overdue,
            average is null ? 0 : Math.Round((decimal)average.Value, 1), pageResult.Page, pageResult.PageSize, pageResult.TotalCount);
    }

    private static bool ManagesAllSalesRecords(AccessSnapshot snapshot, string companyId) =>
        snapshot.ScopeGrants.Any(x => Same(x.CompanyId, companyId) &&
            (x.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) || x.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase)));

    public LeadDetailsDto? GetLead(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset? nowUtc = null)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return store.Read(data =>
        {
            var lead = data.Leads.SingleOrDefault(x => x.Id == id && CanAccessSalesRecord(data, snapshot,
                currentUserId, organization, "Lead.Read", x, x.OwnerUserId, x.Owner));
            if (lead is null) return null;
            var canManage = CanManageRecord(data, snapshot, currentUserId, organization.CompanyId, lead.OwnerUserId, lead.Owner);
            var isOpen = !ClosedLeadStatuses.Contains(lead.Status);
            return new LeadDetailsDto(Map(lead, now, snapshot), data.Find<Crm.Domain.Sales.LeadStatusHistory>(x => x.LeadId == lead.Id)
                .OrderByDescending(x => x.ChangedAtUtc).Select(x => Map(data, x)).ToList(),
                EligibleOwners(data, organization.CompanyId, lead.BranchId, now),
                isOpen && canManage && HasPermission(snapshot, organization.CompanyId, "Lead.Assign"),
                isOpen && canManage && HasPermission(snapshot, organization.CompanyId, "Lead.Update"),
                canManage && HasPermission(snapshot, organization.CompanyId, "Lead.Convert") && lead.Status == LeadStatus.Qualified);
        });
    }

    public IReadOnlyList<SalesOwnerOptionDto> GetEligibleOwners(Guid currentUserId, OrganizationSelection organization, string branchId)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!snapshot.HasCompany(organization.CompanyId)) throw new UnauthorizedAccessException("Company access is required.");
        if (organization.BranchId is not null && !Same(organization.BranchId, branchId))
            throw new UnauthorizedAccessException("شعبه خارج از محیط کاری انتخاب شده است.");
        if (!snapshot.AllowsRecord(organization.CompanyId, "Lead.Create", branchId, organization.TerritoryId))
            throw new UnauthorizedAccessException("دسترسی ایجاد رکورد در شعبه انتخاب‌شده وجود ندارد.");
        return store.Read(data => EligibleOwners(data, organization.CompanyId, branchId, DateTimeOffset.UtcNow));
    }

    public LeadDto CreateLead(Guid currentUserId, OrganizationSelection organization, CreateLeadCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var scope = ResolveWriteScope(data, snapshot, organization, "Lead.Create", command.BranchId, command.TerritoryId);
            EnsureText(command.Name, "نام سرنخ الزامی است.");
            if (string.IsNullOrWhiteSpace(command.Contact) && string.IsNullOrWhiteSpace(command.Phone) && string.IsNullOrWhiteSpace(command.Email))
                throw new InvalidOperationException("حداقل نام تماس، تلفن یا ایمیل الزامی است.");
            EnsureNoOpenDuplicate(data, organization.CompanyId, command.Name, command.Phone, command.Email);
            var ownerId = command.OwnerUserId ?? currentUserId;
            if (ownerId != currentUserId && !HasPermission(snapshot, organization.CompanyId, "Lead.Assign"))
                throw new UnauthorizedAccessException("تخصیص سرنخ به کاربر دیگر نیازمند مجوز Lead.Assign است.");
            var owner = RequiredOwner(data, organization.CompanyId, scope.BranchId, ownerId, nowUtc);
            var lead = new Lead(Guid.NewGuid(), NextLeadCode(data, nowUtc), command.Name, command.Contact ?? string.Empty,
                string.IsNullOrWhiteSpace(command.Source) ? "نامشخص" : command.Source, owner.DisplayName,
                organization.CompanyId, scope.BranchId, scope.TerritoryId, ownerUserId: owner.UserId,
                phone: command.Phone, email: command.Email, firstContactDueAtUtc: nowUtc.AddHours(4));
            lead.ApplyScore(ScoreLead(command));
            data.Leads.Add(lead);
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, null, LeadStatus.New, "ثبت سرنخ", currentUserId, nowUtc));
            var from = lead.Status;
            lead.Assign(owner.UserId, owner.DisplayName, nowUtc, nowUtc.AddHours(4), "تخصیص اولیه");
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, from, lead.Status, "تخصیص اولیه", currentUserId, nowUtc));
            return Map(lead, nowUtc, snapshot);
        });
    }

    public LeadDto AssignLead(Guid currentUserId, OrganizationSelection organization, Guid id, AssignLeadCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Lead.Assign");
        return store.Write(data =>
        {
            var lead = RequiredLead(data, snapshot, currentUserId, organization, id, "Lead.Assign", requireOwnership: false);
            EnsureVersion(lead.Version, command.ExpectedVersion);
            var owner = RequiredOwner(data, organization.CompanyId, lead.BranchId, command.OwnerUserId, nowUtc);
            var from = lead.Status;
            lead.Assign(owner.UserId, owner.DisplayName, nowUtc, command.FirstContactDueAtUtc, command.Reason);
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, from, lead.Status, command.Reason, currentUserId, nowUtc));
            return Map(lead, nowUtc, snapshot);
        });
    }

    public LeadDto TransitionLead(Guid currentUserId, OrganizationSelection organization, Guid id, TransitionLeadCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Lead.Update");
        return store.Write(data =>
        {
            var lead = RequiredLead(data, snapshot, currentUserId, organization, id, "Lead.Update");
            EnsureVersion(lead.Version, command.ExpectedVersion);
            var from = lead.Status;
            switch (command.TargetStatus)
            {
                case LeadStatus.Contacted:
                    if (!command.NextActionAtUtc.HasValue) throw new InvalidOperationException("زمان اقدام بعدی الزامی است.");
                    lead.MarkContacted(nowUtc, command.NextAction ?? string.Empty, command.NextActionAtUtc.Value, command.Reason);
                    break;
                case LeadStatus.Qualified:
                    lead.Qualify(command.Score, command.Reason);
                    break;
                case LeadStatus.Nurture:
                    if (!command.NextActionAtUtc.HasValue) throw new InvalidOperationException("زمان اقدام بعدی الزامی است.");
                    lead.Nurture(command.Reason, command.NextAction ?? string.Empty, command.NextActionAtUtc.Value, nowUtc);
                    break;
                case LeadStatus.Disqualified:
                case LeadStatus.Duplicate:
                case LeadStatus.Invalid:
                    lead.Close(command.TargetStatus, command.Reason, nowUtc);
                    break;
                default:
                    throw new InvalidOperationException("انتقال وضعیت انتخاب‌شده از این فرم مجاز نیست.");
            }
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, from, lead.Status, command.Reason, currentUserId, nowUtc));
            return Map(lead, nowUtc, snapshot);
        });
    }

    public OpportunityDto ConvertLead(Guid currentUserId, OrganizationSelection organization, Guid id, ConvertLeadCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Lead.Convert");
        return store.Write(data =>
        {
            var lead = RequiredLead(data, snapshot, currentUserId, organization, id, "Lead.Convert");
            EnsureVersion(lead.Version, command.ExpectedVersion);
            if (lead.Status != LeadStatus.Qualified) throw new InvalidOperationException("فقط سرنخ واجد شرایط قابل تبدیل است.");
            if (command.Value <= 0) throw new InvalidOperationException("ارزش فرصت باید بیشتر از صفر باشد.");
            if (command.ExpectedCloseAtUtc <= nowUtc) throw new InvalidOperationException("تاریخ بستن فرصت باید در آینده باشد.");
            var customer = ResolveConversionCustomer(data, snapshot, organization, lead, command.ExistingCustomerId, currentUserId, nowUtc);
            var opportunity = new Opportunity(Guid.NewGuid(), NextOpportunityCode(data, nowUtc), command.OpportunityTitle,
                customer.Name, customer.Id, command.Value, lead.Owner, lead.CompanyId, lead.BranchId, lead.TerritoryId,
                lead.OwnerUserId, lead.Id, command.ExpectedCloseAtUtc, lead.Source);
            opportunity.Update(command.OpportunityTitle, command.Value, command.ExpectedCloseAtUtc, lead.Source,
                null, OpportunityRiskLevel.Medium, "جلسه کشف نیاز", nowUtc.AddDays(2));
            data.Opportunities.Add(opportunity);
            data.Append<Crm.Domain.Sales.OpportunityStageHistory>(new OpportunityStageHistory(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, null, opportunity.Stage,
                opportunity.Probability, "تبدیل سرنخ", currentUserId, nowUtc));
            data.OpportunityActivities.Add(new OpportunityActivity(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, OpportunityActivityType.Task,
                "شروع فرایند فروش", "فرصت از سرنخ ایجاد شد", nowUtc, currentUserId,
                opportunity.NextAction, opportunity.NextActionAtUtc));
            var from = lead.Status;
            lead.Convert(customer.Id, opportunity.Id, nowUtc);
            data.Append<Crm.Domain.Sales.LeadStatusHistory>(new LeadStatusHistory(Guid.NewGuid(), lead.CompanyId, lead.BranchId,
                lead.TerritoryId, lead.Id, from, lead.Status, "تبدیل به مشتری و فرصت", currentUserId, nowUtc));
            data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
                CustomerTimelineType.LeadConverted, "سرنخ به فرصت تبدیل شد", opportunity.Code,
                nowUtc, "CRM", lead.Code, currentUserId));
            return Map(opportunity);
        });
    }

    /// <summary>Synchronous convenience over <see cref="GetPipelineAsync"/>.</summary>
    public PipelineBoardDto GetPipeline(Guid currentUserId, OrganizationSelection organization, string? query = null,
        bool includeClosed = false, DateTimeOffset? nowUtc = null) =>
        GetPipelineAsync(currentUserId, organization, query, includeClosed, nowUtc).GetAwaiter().GetResult();

    /// <summary>Most recently closed (won/lost) opportunities shown when the board includes closed deals.</summary>
    public const int ClosedOpportunityLimit = 100;

    /// <summary>
    /// Scope, ownership and search run in the data source. The board needs every open deal (stage columns and totals),
    /// which is a bounded working set; closed deals grow without bound, so only the latest ones are included.
    /// </summary>
    public async Task<PipelineBoardDto> GetPipelineAsync(Guid currentUserId, OrganizationSelection organization, string? query = null,
        bool includeClosed = false, DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var source = querySource ?? store as ICrmQuerySource ??
            throw new InvalidOperationException("No query source is configured for the pipeline board.");
        var visible = source.Query<Opportunity>().InScope(snapshot, organization, "Opportunity.Read");
        if (!ManagesAllSalesRecords(snapshot, organization.CompanyId))
        {
            var users = await source.ToListAsync(source.Query<CrmUser>().Where(x => x.Id == currentUserId), cancellationToken);
            var myName = users.SingleOrDefault()?.DisplayName ?? "\0";
            visible = visible.Where(x => x.OwnerUserId == currentUserId || x.OwnerUserId == null && x.Owner == myName);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            visible = visible.Where(x => x.Title.Contains(term) || x.Code.Contains(term) || x.Customer.Contains(term));
        }
        var rows = await source.ToListAsync(visible.Where(x => x.Stage != OpportunityStage.Won && x.Stage != OpportunityStage.Lost), cancellationToken);
        if (includeClosed)
            rows.AddRange(await source.ToListAsync(visible.Where(x => x.Stage == OpportunityStage.Won || x.Stage == OpportunityStage.Lost)
                .OrderByDescending(x => x.ClosedAtUtc).ThenBy(x => x.Id).Take(ClosedOpportunityLimit), cancellationToken));
        var values = rows.OrderBy(x => x.ExpectedCloseAtUtc).Select(Map).ToList();
        var stageValues = values.GroupBy(x => x.Stage).Select(group => new OpportunityStageSummaryDto(group.Key,
            group.Count(), group.Sum(x => x.Value), group.Sum(x => x.Value * x.Probability / 100m))).ToList();
        return new PipelineBoardDto(values, stageValues, query?.Trim(), includeClosed,
            values.Sum(x => x.Value), values.Sum(x => x.Value * x.Probability / 100m), values.Count,
            values.Count(x => x.LastActivityAtUtc < now.AddDays(-7)),
            values.Count(x => x.NextActionAtUtc.HasValue && x.NextActionAtUtc < now),
            values.Count == 0 ? 0 : Math.Round((decimal)values.Average(x => Math.Max(0, (now - x.LastActivityAtUtc!.Value).TotalDays)), 1));
    }

    public OpportunityDetailsDto? GetOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data =>
        {
            var opportunity = data.Opportunities.SingleOrDefault(x => x.Id == id && CanAccessSalesRecord(data, snapshot,
                currentUserId, organization, "Opportunity.Read", x, x.OwnerUserId, x.Owner));
            if (opportunity is null) return null;
            var canManage = CanManageRecord(data, snapshot, currentUserId, organization.CompanyId, opportunity.OwnerUserId, opportunity.Owner);
            var isOpen = opportunity.Stage is not (OpportunityStage.Won or OpportunityStage.Lost);
            return new OpportunityDetailsDto(Map(opportunity), data.Find<Crm.Domain.Sales.OpportunityStageHistory>(x => x.OpportunityId == id)
                .OrderByDescending(x => x.ChangedAtUtc).Select(x => Map(data, x)).ToList(),
                data.OpportunityActivities.Where(x => x.OpportunityId == id).OrderByDescending(x => x.OccurredAtUtc)
                    .Select(x => Map(data, x)).ToList(), EligibleOwners(data, organization.CompanyId, opportunity.BranchId, DateTimeOffset.UtcNow),
                isOpen && canManage && HasPermission(snapshot, organization.CompanyId, "Opportunity.Assign"),
                isOpen && canManage && HasPermission(snapshot, organization.CompanyId, "Opportunity.Update"),
                isOpen && canManage && HasPermission(snapshot, organization.CompanyId, "Opportunity.Close"));
        });
    }

    public OpportunityDto CreateOpportunity(Guid currentUserId, OrganizationSelection organization, CreateOpportunityCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Opportunity.Create");
        return store.Write(data =>
        {
            var scope = ResolveWriteScope(data, snapshot, organization, "Opportunity.Create", command.BranchId, command.TerritoryId);
            var customer = data.Customers.SingleOrDefault(x => x.Id == command.CustomerId && x.Status != CustomerStatus.Inactive &&
                InContext(snapshot, organization, "Customer.Read", x)) ?? throw new InvalidOperationException("مشتری فعال و مجاز انتخاب نشده است.");
            if (!Same(customer.BranchId, scope.BranchId)) throw new InvalidOperationException("شعبه فرصت باید با شعبه مشتری یکسان باشد.");
            if (command.OwnerUserId != currentUserId && !HasPermission(snapshot, organization.CompanyId, "Opportunity.Assign"))
                throw new UnauthorizedAccessException("تخصیص فرصت به کاربر دیگر نیازمند مجوز Opportunity.Assign است.");
            var owner = RequiredOwner(data, organization.CompanyId, scope.BranchId, command.OwnerUserId, nowUtc);
            var opportunity = new Opportunity(Guid.NewGuid(), NextOpportunityCode(data, nowUtc), command.Title,
                customer.Name, customer.Id, command.Value, owner.DisplayName, organization.CompanyId, scope.BranchId,
                scope.TerritoryId, owner.UserId, null, command.ExpectedCloseAtUtc, command.Source);
            opportunity.Update(command.Title, command.Value, command.ExpectedCloseAtUtc, command.Source,
                command.Competitor, command.RiskLevel, command.NextAction, command.NextActionAtUtc);
            data.Opportunities.Add(opportunity);
            data.Append<Crm.Domain.Sales.OpportunityStageHistory>(new OpportunityStageHistory(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, null, opportunity.Stage,
                opportunity.Probability, "ایجاد فرصت", currentUserId, nowUtc));
            return Map(opportunity);
        });
    }

    public OpportunityDto UpdateOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, UpdateOpportunityCommand command)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Opportunity.Update");
        return store.Write(data =>
        {
            var opportunity = RequiredOpportunity(data, snapshot, currentUserId, organization, id, "Opportunity.Update");
            EnsureVersion(opportunity.Version, command.ExpectedVersion);
            opportunity.Update(command.Title, command.Value, command.ExpectedCloseAtUtc, command.Source,
                command.Competitor, command.RiskLevel, command.NextAction, command.NextActionAtUtc);
            return Map(opportunity);
        });
    }

    public OpportunityDto AssignOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, AssignOpportunityCommand command)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Opportunity.Assign");
        return store.Write(data =>
        {
            var opportunity = RequiredOpportunity(data, snapshot, currentUserId, organization, id, "Opportunity.Assign", requireOwnership: false);
            EnsureVersion(opportunity.Version, command.ExpectedVersion);
            EnsureText(command.Reason, "دلیل تخصیص الزامی است.");
            var owner = RequiredOwner(data, organization.CompanyId, opportunity.BranchId, command.OwnerUserId, DateTimeOffset.UtcNow);
            opportunity.Assign(owner.UserId, owner.DisplayName);
            data.OpportunityActivities.Add(new OpportunityActivity(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, OpportunityActivityType.Note,
                "تغییر مالک فرصت", command.Reason, DateTimeOffset.UtcNow, currentUserId));
            return Map(opportunity);
        });
    }

    public OpportunityDto MoveOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, MoveOpportunityStageCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var permission = command.TargetStage is OpportunityStage.Won or OpportunityStage.Lost ? "Opportunity.Close" : "Opportunity.Update";
        RequirePermission(snapshot, organization.CompanyId, permission);
        return store.Write(data =>
        {
            var opportunity = RequiredOpportunity(data, snapshot, currentUserId, organization, id, permission);
            EnsureVersion(opportunity.Version, command.ExpectedVersion);
            if (command.TargetStage == OpportunityStage.Won &&
                !data.Quotes.Any(x => x.OpportunityId == opportunity.Id && x.Status == Crm.Domain.Commercial.QuoteStatus.Accepted))
                throw new InvalidOperationException("ثبت Won فقط پس از پذیرش یک پیشنهاد معتبر مرتبط مجاز است.");
            var from = opportunity.Stage;
            opportunity.MoveTo(command.TargetStage, command.Reason, nowUtc);
            data.Append<Crm.Domain.Sales.OpportunityStageHistory>(new OpportunityStageHistory(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.BranchId, opportunity.TerritoryId, opportunity.Id, from, opportunity.Stage,
                opportunity.Probability, command.Reason, currentUserId, nowUtc));
            data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), opportunity.CompanyId,
                opportunity.CustomerId, CustomerTimelineType.OpportunityChanged, "مرحله فرصت تغییر کرد",
                $"{from} → {opportunity.Stage}: {command.Reason}", nowUtc, "CRM", opportunity.Code, currentUserId));
            return Map(opportunity);
        });
    }

    public OpportunityActivityDto AddActivity(Guid currentUserId, OrganizationSelection organization, Guid id, AddOpportunityActivityCommand command)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Opportunity.Update");
        return store.Write(data =>
        {
            var opportunity = RequiredOpportunity(data, snapshot, currentUserId, organization, id, "Opportunity.Update");
            EnsureVersion(opportunity.Version, command.ExpectedVersion);
            EnsureText(command.Subject, "موضوع فعالیت الزامی است.");
            EnsureText(command.Outcome, "نتیجه فعالیت الزامی است.");
            opportunity.RecordActivity(command.OccurredAtUtc, command.NextAction, command.NextActionAtUtc);
            var activity = new OpportunityActivity(Guid.NewGuid(), opportunity.CompanyId, opportunity.BranchId,
                opportunity.TerritoryId, opportunity.Id, command.Type, command.Subject, command.Outcome,
                command.OccurredAtUtc, currentUserId, command.NextAction, command.NextActionAtUtc);
            data.OpportunityActivities.Add(activity);
            return Map(data, activity);
        });
    }

    private static Customer ResolveConversionCustomer(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization,
        Lead lead, Guid? selectedCustomerId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        if (selectedCustomerId.HasValue)
        {
            var selected = data.Customers.SingleOrDefault(x => x.Id == selectedCustomerId && x.Status != CustomerStatus.Inactive &&
                InContext(snapshot, organization, "Customer.Read", x)) ?? throw new InvalidOperationException("مشتری انتخاب‌شده معتبر یا مجاز نیست.");
            if (!Same(selected.BranchId, lead.BranchId))
                throw new InvalidOperationException("شعبه مشتری مقصد باید با شعبه سرنخ یکسان باشد.");
            return selected;
        }
        var matches = data.Customers.Where(x => x.Status != CustomerStatus.Inactive && Same(x.CompanyId, lead.CompanyId) &&
            Same(x.BranchId, lead.BranchId) && x.Name.Equals(lead.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new InvalidOperationException("چند مشتری هم‌نام وجود دارد؛ مشتری مقصد را صریح انتخاب کنید.");
        if (matches.Count == 1) return matches[0];
        var branch = data.OrganizationUnits.Single(x => Same(x.CompanyId, lead.CompanyId) && Same(x.UnitId, lead.BranchId));
        var customer = new Customer(Guid.NewGuid(), RecordCodes.Next(data.Customers.Select(x => x.Code), "CUS-", 481, 5), lead.Name, string.Empty,
            lead.Owner, lead.CompanyId, lead.BranchId, branch.Name, lead.TerritoryId, "تبدیل سرنخ", 0,
            primaryPhone: lead.Phone, primaryEmail: lead.Email, dataSource: "Lead Conversion");
        data.Customers.Add(customer);
        data.Append<Crm.Domain.Customers.CustomerOwnershipHistory>(new CustomerOwnershipHistory(Guid.NewGuid(), customer.CompanyId, customer.Id,
            customer.BranchId, customer.TerritoryId, customer.Owner, nowUtc, "تبدیل سرنخ", actorUserId));
        data.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
            CustomerTimelineType.Created, "مشتری از سرنخ ساخته شد", lead.Code, nowUtc, "CRM", lead.Code, actorUserId));
        if (!string.IsNullOrWhiteSpace(lead.Contact) || !string.IsNullOrWhiteSpace(lead.Phone) || !string.IsNullOrWhiteSpace(lead.Email))
            data.CustomerContacts.Add(new CustomerContact(Guid.NewGuid(), customer.CompanyId, customer.Id,
                string.IsNullOrWhiteSpace(lead.Contact) ? "تماس سرنخ" : lead.Contact, "Lead Contact", lead.Phone,
                lead.Email, true, ContactConsentStatus.Unknown));
        return customer;
    }

    private static Lead RequiredLead(CrmDataSet data, AccessSnapshot snapshot, Guid userId, OrganizationSelection organization,
        Guid id, string permission, bool requireOwnership = true)
    {
        var lead = data.Leads.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
            throw new KeyNotFoundException("سرنخ در دامنه جاری پیدا نشد.");
        if (requireOwnership && !CanManageRecord(data, snapshot, userId, organization.CompanyId, lead.OwnerUserId, lead.Owner))
            throw new UnauthorizedAccessException("سرنخ به کاربر دیگری تخصیص یافته است.");
        return lead;
    }

    private static Opportunity RequiredOpportunity(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, Guid id, string permission, bool requireOwnership = true)
    {
        var opportunity = data.Opportunities.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
            throw new KeyNotFoundException("فرصت در دامنه جاری پیدا نشد.");
        if (requireOwnership && !CanManageRecord(data, snapshot, userId, organization.CompanyId, opportunity.OwnerUserId, opportunity.Owner))
            throw new UnauthorizedAccessException("فرصت به کاربر دیگری تخصیص یافته است.");
        return opportunity;
    }

    private static bool CanAccessSalesRecord(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, string permission, IOrganizationScoped record, Guid? ownerUserId, string ownerName) =>
        InContext(snapshot, organization, permission, record) && CanManageRecord(data, snapshot, userId, organization.CompanyId, ownerUserId, ownerName);

    private static bool CanManageRecord(CrmDataSet data, AccessSnapshot snapshot, Guid userId, string companyId,
        Guid? ownerUserId, string ownerName)
    {
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, companyId) &&
            (x.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) || x.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase))))
            return true;
        if (ownerUserId.HasValue) return ownerUserId.Value == userId;
        var user = data.Users.SingleOrDefault(x => x.Id == userId);
        return user is not null && user.DisplayName.Equals(ownerName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SalesOwnerOptionDto> EligibleOwners(CrmDataSet data, string companyId, string branchId, DateTimeOffset nowUtc)
    {
        var rolePriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { ["SalesManager"] = 1, ["SalesSupervisor"] = 2, ["SalesExpert"] = 3 };
        return data.UserRoleAssignments.Where(x => Same(x.CompanyId, companyId) && x.IsEffective(nowUtc) &&
                rolePriority.ContainsKey(x.RoleKey) && (x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
                    x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) && Same(x.ScopeId, branchId)))
            .Join(data.Users.Where(x => x.IsActiveAt(nowUtc)), assignment => assignment.CrmUserId, user => user.Id,
                (assignment, user) => new { assignment, user })
            .GroupBy(x => x.user.Id).Select(group => group.OrderBy(x => rolePriority[x.assignment.RoleKey]).First())
            .OrderBy(x => x.user.DisplayName).Select(x => new SalesOwnerOptionDto(x.user.Id, x.user.DisplayName,
                branchId, x.assignment.RoleLabel)).ToList();
    }

    private static SalesOwnerOptionDto RequiredOwner(CrmDataSet data, string companyId, string branchId, Guid ownerId, DateTimeOffset nowUtc) =>
        EligibleOwners(data, companyId, branchId, nowUtc).SingleOrDefault(x => x.UserId == ownerId) ??
        throw new InvalidOperationException("مالک انتخاب‌شده در شعبه و شرکت جاری نقش فروش فعال ندارد.");

    private static (string BranchId, string? TerritoryId) ResolveWriteScope(CrmDataSet data, AccessSnapshot snapshot,
        OrganizationSelection organization, string permission, string branchId, string? territoryId)
    {
        var branch = data.OrganizationUnits.SingleOrDefault(x => Same(x.CompanyId, organization.CompanyId) &&
            Same(x.UnitId, branchId) && x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active) ??
            throw new UnauthorizedAccessException("شعبه فعال و معتبر انتخاب نشده است.");
        var resolvedTerritory = string.IsNullOrWhiteSpace(territoryId) ? organization.TerritoryId : territoryId.Trim();
        if (resolvedTerritory is not null && !data.Territories.Any(x => Same(x.CompanyId, organization.CompanyId) &&
            Same(x.TerritoryId, resolvedTerritory) && x.Status == OrganizationStatus.Active))
            throw new UnauthorizedAccessException("قلمرو انتخاب‌شده معتبر نیست.");
        if (organization.BranchId is not null && !Same(branch.UnitId, organization.BranchId))
            throw new UnauthorizedAccessException("شعبه خارج از محیط کاری انتخاب شده است.");
        if (!snapshot.AllowsRecord(organization.CompanyId, permission, branch.UnitId, resolvedTerritory))
            throw new UnauthorizedAccessException("دامنه انتخاب‌شده خارج از مجوز کاربر است.");
        return (branch.UnitId, resolvedTerritory);
    }

    private static int ScoreLead(CreateLeadCommand command)
    {
        var score = 20;
        if (!string.IsNullOrWhiteSpace(command.Contact)) score += 15;
        if (!string.IsNullOrWhiteSpace(command.Phone)) score += 20;
        if (!string.IsNullOrWhiteSpace(command.Email)) score += 15;
        if (!string.IsNullOrWhiteSpace(command.Source)) score += 10;
        if (!string.IsNullOrWhiteSpace(command.TerritoryId)) score += 10;
        if (command.OwnerUserId.HasValue) score += 10;
        return Math.Clamp(score, 0, 100);
    }

    private static void EnsureNoOpenDuplicate(CrmDataSet data, string companyId, string name, string? phone, string? email)
    {
        var normalizedPhone = Normalize(phone);
        var normalizedEmail = Normalize(email);
        var duplicate = data.Leads.Any(x => Same(x.CompanyId, companyId) && !ClosedLeadStatuses.Contains(x.Status) &&
            ((normalizedPhone is not null && Normalize(x.Phone) == normalizedPhone) ||
             (normalizedEmail is not null && Normalize(x.Email) == normalizedEmail) ||
             (x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
              (normalizedPhone is not null || normalizedEmail is not null))));
        if (duplicate) throw new InvalidOperationException("سرنخ باز مشابه با تلفن، ایمیل یا نام یکسان وجود دارد.");
    }

    private static string NextLeadCode(CrmDataSet data, DateTimeOffset nowUtc) => RecordCodes.Next(data.Leads.Select(x => x.Code), $"LD-{nowUtc.Year}-", 118, 3);
    private static string NextOpportunityCode(CrmDataSet data, DateTimeOffset nowUtc) => RecordCodes.Next(data.Opportunities.Select(x => x.Code), $"OP-{nowUtc.Year}-", 2041, 4);
    private static LeadSlaState Sla(Lead x, DateTimeOffset nowUtc)
    {
        if (x.FirstContactAtUtc.HasValue) return LeadSlaState.Completed;
        if (ClosedLeadStatuses.Contains(x.Status)) return LeadSlaState.NotApplicable;
        if (x.FirstContactDueAtUtc < nowUtc) return LeadSlaState.Overdue;
        return x.FirstContactDueAtUtc <= nowUtc.AddHours(2) ? LeadSlaState.DueSoon : LeadSlaState.OnTrack;
    }
    private static int LeadSort(Lead x, DateTimeOffset nowUtc) => Sla(x, nowUtc) switch
        { LeadSlaState.Overdue => 0, LeadSlaState.DueSoon => 1, LeadSlaState.OnTrack => 2, _ => 3 };

    private static LeadDto Map(Lead x, DateTimeOffset nowUtc, AccessSnapshot snapshot) => new LeadDto(x.Id, x.Code, x.Name, x.Contact, x.Source,
        x.Owner, x.CompanyId, x.BranchId, x.TerritoryId, x.Score, x.Status, x.CustomerId, x.OwnerUserId,
        x.Phone, x.Email, x.AssignedAtUtc, x.FirstContactDueAtUtc, x.FirstContactAtUtc, x.LastActivityAtUtc,
        x.NextAction, x.NextActionAtUtc, x.StatusReason, x.ConvertedOpportunityId, x.Version, Sla(x, nowUtc)).Mask(snapshot);
    private static OpportunityDto Map(Opportunity x) => new(x.Id, x.Code, x.Title, x.Customer, x.Value, x.Owner,
        x.CompanyId, x.BranchId, x.TerritoryId, x.Stage, x.Probability, x.CustomerId, x.OwnerUserId,
        x.OriginLeadId, x.ExpectedCloseAtUtc, x.Source, x.NextAction, x.NextActionAtUtc, x.LastActivityAtUtc,
        x.Competitor, x.RiskLevel, x.OutcomeReason, x.ClosedAtUtc, x.Version);
    private static LeadStatusHistoryDto Map(CrmDataSet data, LeadStatusHistory x) => new(x.Id, x.FromStatus,
        x.ToStatus, x.Reason, x.ChangedByUserId, ActorName(data, x.ChangedByUserId), x.ChangedAtUtc);
    private static OpportunityStageHistoryDto Map(CrmDataSet data, OpportunityStageHistory x) => new(x.Id,
        x.FromStage, x.ToStage, x.Probability, x.Reason, x.ChangedByUserId, ActorName(data, x.ChangedByUserId), x.ChangedAtUtc);
    private static OpportunityActivityDto Map(CrmDataSet data, OpportunityActivity x) => new(x.Id, x.Type,
        x.Subject, x.Outcome, x.OccurredAtUtc, x.ActorUserId, ActorName(data, x.ActorUserId), x.NextAction, x.NextActionAtUtc);
    private static string ActorName(CrmDataSet data, Guid id) => data.Users.SingleOrDefault(x => x.Id == id)?.DisplayName ?? "سیستم";

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("No active access snapshot was found.");
    private static void RequirePermission(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!HasPermission(snapshot, companyId, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
    }
    private static bool HasPermission(AccessSnapshot snapshot, string companyId, string permission) =>
        snapshot.PermissionsFor(companyId).Contains(permission);
    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        Same(entity.CompanyId, organization.CompanyId) && (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InvalidOperationException("رکورد تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static void EnsureText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
    }
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(" ", string.Empty).ToUpperInvariant();
}
