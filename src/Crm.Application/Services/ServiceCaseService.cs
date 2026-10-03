using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Customers;
using Crm.Domain.Organization;
using Crm.Domain.Service;
using Crm.Domain.Work;

namespace Crm.Application.Services;

/// <summary>
/// Service desk use cases: case intake, triage, SLA with pause rule, root cause/corrective action,
/// CSAT, reopen and SLA-driven escalation into the work queue.
/// Visibility: permission + company/branch/territory scope; users without Service.Triage or Service.ReadAll
/// only see cases they own or opened (record ownership).
/// </summary>
public sealed class ServiceCaseService(ICrmDataStore store, IAccessSnapshotService access, ICrmQuerySource? querySource = null) : IServiceCaseService
{
    private static readonly HashSet<string> ServiceOwnerRoles = new(StringComparer.OrdinalIgnoreCase)
        { "SalesManager", "SalesSupervisor", "SalesExpert", "ServiceAgent" };

    /// <summary>Synchronous convenience over <see cref="GetCasesAsync"/>; returns the first page of up to 200 cases.</summary>
    public ServiceCaseListDto GetCases(Guid currentUserId, OrganizationSelection organization, string? query = null,
        ServiceCaseStatus? status = null, ServiceCasePriority? priority = null, bool includeClosed = false,
        bool onlyMine = false, DateTimeOffset? nowUtc = null) =>
        GetCasesAsync(currentUserId, organization, query, status, priority, includeClosed, onlyMine, 1, PageRequest.MaxPageSize, nowUtc)
            .GetAwaiter().GetResult();

    /// <summary>
    /// Filters, orders and pages cases in the data source. KPI tiles use aggregates, and SLA states are evaluated only
    /// over the open cases (a bounded set), never over the ever-growing closed history.
    /// </summary>
    public async Task<ServiceCaseListDto> GetCasesAsync(Guid currentUserId, OrganizationSelection organization, string? query = null,
        ServiceCaseStatus? status = null, ServiceCasePriority? priority = null, bool includeClosed = false, bool onlyMine = false,
        int page = 1, int pageSize = PageRequest.DefaultPageSize, DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Read");
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var source = querySource ?? store as ICrmQuerySource ??
            throw new InvalidOperationException("No query source is configured for service case lists.");

        var visible = source.Query<ServiceCase>().InScope(snapshot, organization, "Service.Read");
        if (!HasPermission(snapshot, organization.CompanyId, "Service.Triage") && !HasPermission(snapshot, organization.CompanyId, "Service.ReadAll"))
            visible = visible.Where(x => x.OwnerUserId == currentUserId || x.CreatedByUserId == currentUserId);

        var filtered = includeClosed ? visible : visible.Where(x => x.Status != ServiceCaseStatus.Closed);
        if (status.HasValue) filtered = filtered.Where(x => x.Status == status.Value);
        if (priority.HasValue) filtered = filtered.Where(x => x.Priority == priority.Value);
        if (onlyMine) filtered = filtered.Where(x => x.OwnerUserId == currentUserId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            var companyId = organization.CompanyId;
            var matchingCustomers = source.Query<Customer>().Where(c => c.CompanyId == companyId && c.Name.Contains(term)).Select(c => c.Id);
            filtered = filtered.Where(x => x.Code.Contains(term) || x.Subject.Contains(term) || matchingCustomers.Contains(x.CustomerId));
        }

        // Open before resolved/closed, SLA-breached first, then priority (explicit rank: the column stores names) and nearest due time.
        var ordered = filtered
            .OrderBy(x => x.Status == ServiceCaseStatus.Resolved || x.Status == ServiceCaseStatus.Closed ? 1 : 0)
            .ThenBy(x => x.FirstRespondedAtUtc == null && x.FirstResponseDueAtUtc < now ||
                x.ResolvedAtUtc == null && x.Status != ServiceCaseStatus.WaitingOnCustomer && x.ResolutionDueAtUtc < now ? 0 : 1)
            .ThenBy(x => x.Priority == ServiceCasePriority.Critical ? 0 : x.Priority == ServiceCasePriority.High ? 1 :
                x.Priority == ServiceCasePriority.Medium ? 2 : 3)
            .ThenBy(x => x.ResolutionDueAtUtc).ThenBy(x => x.Id);
        var total = await source.CountAsync(ordered, cancellationToken);
        var request = PageRequest.Of(page, pageSize);
        request = request with { Page = Math.Min(request.Page, Math.Max(1, (int)Math.Ceiling(total / (double)request.PageSize))) };
        var rows = await source.ToListAsync(ordered.Skip(request.Skip).Take(request.PageSize), cancellationToken);

        var open = await source.ToListAsync(visible.Where(x => x.Status != ServiceCaseStatus.Resolved && x.Status != ServiceCaseStatus.Closed), cancellationToken);
        var resolved = visible.Where(x => x.ResolvedAtUtc != null);
        var resolvedCount = await source.CountAsync(resolved, cancellationToken);
        var metCount = await source.CountAsync(resolved.Where(x => x.ResolvedAtUtc <= x.ResolutionDueAtUtc), cancellationToken);
        var satisfaction = await source.AverageAsync(visible.Where(x => x.SatisfactionScore != null).Select(x => x.SatisfactionScore!.Value), cancellationToken);

        var customerIds = rows.Select(x => x.CustomerId).Distinct().ToList();
        var customers = (await source.ToListAsync(source.Query<Customer>().Where(c => customerIds.Contains(c.Id)), cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);
        return new ServiceCaseListDto(rows.Select(x => Map(x, customers, now)).ToList(), query?.Trim(), status, priority, includeClosed, onlyMine,
            open.Count,
            open.Count(x => x.FirstResponseSla(now) == ServiceSlaState.Breached || x.ResolutionSla(now) == ServiceSlaState.Breached),
            open.Count(x => x.FirstResponseSla(now) == ServiceSlaState.AtRisk || x.ResolutionSla(now) == ServiceSlaState.AtRisk),
            open.Count(x => x.EscalationLevel > 0),
            satisfaction is null ? null : Math.Round((decimal)satisfaction.Value, 1),
            resolvedCount == 0 ? null : Math.Round(100m * metCount / resolvedCount, 1),
            HasPermission(snapshot, organization.CompanyId, "Service.Create"),
            HasPermission(snapshot, organization.CompanyId, "Service.Triage"),
            request.Page, request.PageSize, total);
    }

    public ServiceCaseDetailsDto? GetCase(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset? nowUtc = null)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Read");
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return store.Read(data =>
        {
            var item = data.ServiceCases.SingleOrDefault(x => x.Id == id && CanSee(snapshot, currentUserId, organization, x));
            if (item is null) return null;
            var customers = CustomerNames(data, organization.CompanyId);
            var history = data.ServiceCaseHistory.Where(x => x.CaseId == item.Id).OrderByDescending(x => x.OccurredAtUtc)
                .Select(x => new ServiceCaseHistoryDto(x.Id, x.FromStatus, x.ToStatus, x.Action, x.Note,
                    ActorName(data, x.ActorUserId), x.OccurredAtUtc)).ToList();
            var canTriage = HasPermission(snapshot, organization.CompanyId, "Service.Triage");
            var canWork = HasPermission(snapshot, organization.CompanyId, "Service.Update") &&
                (canTriage || item.OwnerUserId == currentUserId);
            return new ServiceCaseDetailsDto(Map(item, customers, now), history,
                canTriage ? EligibleOwners(data, item.CompanyId, item.BranchId, now) : [],
                canTriage && item.IsOpen,
                canWork && item.IsOpen,
                canWork && item.Status == ServiceCaseStatus.Resolved,
                canTriage && !item.IsOpen);
        });
    }

    public IReadOnlyList<ServiceCaseCustomerOptionDto> GetCustomerOptions(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Create");
        return store.Read(data => data.Customers
            .Where(x => x.Status != CustomerStatus.Inactive && InContext(snapshot, organization, "Customer.Read", x) &&
                snapshot.AllowsRecord(x.CompanyId, "Service.Create", x.BranchId, x.TerritoryId))
            .OrderBy(x => x.Name)
            .Select(x => new ServiceCaseCustomerOptionDto(x.Id, x.Code, x.Name, x.BranchId)).ToList());
    }

    public ServiceCaseDto CreateCase(Guid currentUserId, OrganizationSelection organization, CreateServiceCaseCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Create");
        if (string.IsNullOrWhiteSpace(command.Subject)) throw new InvalidOperationException("موضوع پرونده الزامی است.");
        return store.Write(data =>
        {
            var customer = data.Customers.SingleOrDefault(x => x.Id == command.CustomerId &&
                    InContext(snapshot, organization, "Customer.Read", x)) ??
                throw new UnauthorizedAccessException("مشتری در دامنه جاری پیدا نشد.");
            if (customer.Status == CustomerStatus.Inactive) throw new InvalidOperationException("برای مشتری غیرفعال نمی‌توان پرونده ثبت کرد.");
            if (!snapshot.AllowsRecord(customer.CompanyId, "Service.Create", customer.BranchId, customer.TerritoryId))
                throw new UnauthorizedAccessException("ثبت پرونده برای شعبه این مشتری مجاز نیست.");
            var item = new ServiceCase(Guid.NewGuid(), NextCode(data, nowUtc), command.Subject, command.Description ?? string.Empty,
                customer.Id, customer.CompanyId, customer.BranchId, customer.TerritoryId, command.Category,
                command.Channel, command.Priority, currentUserId, nowUtc);
            data.ServiceCases.Add(item);
            History(data, item, null, "ثبت پرونده", $"کانال {command.Channel}، اولویت {command.Priority}", currentUserId, nowUtc);
            data.CustomerTimelineEvents.Add(new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id,
                CustomerTimelineType.ServiceCase, $"ثبت پرونده خدمات {item.Code}", item.Subject, nowUtc, "Service",
                item.Code, currentUserId));
            return Map(item, CustomerNames(data, organization.CompanyId), nowUtc);
        });
    }

    public ServiceCaseDto TriageCase(Guid currentUserId, OrganizationSelection organization, Guid id, TriageServiceCaseCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Triage");
        return store.Write(data =>
        {
            var item = Required(data, snapshot, currentUserId, organization, id);
            EnsureVersion(item.Version, command.ExpectedVersion);
            var owner = EligibleOwners(data, item.CompanyId, item.BranchId, nowUtc).SingleOrDefault(x => x.UserId == command.OwnerUserId) ??
                throw new InvalidOperationException("مسئول انتخاب‌شده در شعبه این پرونده نقش فعال ندارد.");
            var from = item.Status;
            var previousPriority = item.Priority;
            item.Triage(command.Priority, owner.UserId, owner.DisplayName, nowUtc);
            var note = $"مسئول: {owner.DisplayName}؛ اولویت: {previousPriority} → {item.Priority}";
            if (!string.IsNullOrWhiteSpace(command.Note)) note += "؛ " + command.Note.Trim();
            History(data, item, from, "تریاژ", note, currentUserId, nowUtc);
            return Map(item, CustomerNames(data, organization.CompanyId), nowUtc);
        });
    }

    public ServiceCaseDto ApplyAction(Guid currentUserId, OrganizationSelection organization, Guid id, ServiceCaseActionCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var canTriage = HasPermission(snapshot, organization.CompanyId, "Service.Triage");
        RequirePermission(snapshot, organization.CompanyId, command.Action == ServiceCaseAction.Reopen ? "Service.Triage" : "Service.Update");
        return store.Write(data =>
        {
            var item = Required(data, snapshot, currentUserId, organization, id);
            EnsureVersion(item.Version, command.ExpectedVersion);
            if (!canTriage && item.OwnerUserId != currentUserId)
                throw new UnauthorizedAccessException("فقط مسئول پرونده یا سرپرست خدمات می‌تواند این اقدام را انجام دهد.");
            var from = item.Status;
            string label;
            switch (command.Action)
            {
                case ServiceCaseAction.StartWork:
                    label = from == ServiceCaseStatus.WaitingOnCustomer ? "ادامه رسیدگی" : "شروع رسیدگی";
                    item.StartWork(nowUtc);
                    break;
                case ServiceCaseAction.WaitOnCustomer:
                    label = "در انتظار مشتری (توقف SLA)";
                    item.WaitOnCustomer(nowUtc);
                    break;
                case ServiceCaseAction.Resolve:
                    label = "حل پرونده";
                    item.Resolve(command.RootCause ?? string.Empty, command.CorrectiveAction ?? string.Empty,
                        command.Resolution ?? string.Empty, nowUtc);
                    data.CustomerTimelineEvents.Add(new CustomerTimelineEvent(Guid.NewGuid(), item.CompanyId, item.CustomerId,
                        CustomerTimelineType.ServiceCase, $"حل پرونده خدمات {item.Code}", item.Resolution ?? string.Empty,
                        nowUtc, "Service", item.Code, currentUserId));
                    break;
                case ServiceCaseAction.Close:
                    label = "بستن و ثبت رضایت";
                    item.Close(command.SatisfactionScore, command.Note, nowUtc);
                    break;
                case ServiceCaseAction.Reopen:
                    if (string.IsNullOrWhiteSpace(command.Note)) throw new InvalidOperationException("دلیل بازگشایی الزامی است.");
                    label = "بازگشایی";
                    item.Reopen(nowUtc);
                    break;
                default:
                    throw new InvalidOperationException("اقدام انتخاب‌شده معتبر نیست.");
            }
            History(data, item, from, label, command.Note?.Trim() ?? string.Empty, currentUserId, nowUtc);
            return Map(item, CustomerNames(data, organization.CompanyId), nowUtc);
        });
    }

    public ServiceEscalationResult RunEscalation(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Service.Triage");
        return store.Write(data => Escalate(data,
            data.ServiceCases.Where(x => x.IsOpen && CanSee(snapshot, currentUserId, organization, x)).ToList(), nowUtc));
    }

    public ServiceEscalationResult EscalateBreaches(DateTimeOffset nowUtc) =>
        store.Write(data => Escalate(data, data.ServiceCases.Where(x => x.IsOpen).ToList(), nowUtc));

    private static ServiceEscalationResult Escalate(CrmDataSet data, IReadOnlyList<ServiceCase> candidates, DateTimeOffset nowUtc)
    {
        int escalated = 0, workItems = 0;
        foreach (var item in candidates)
        {
            var from = item.Status;
            if (!item.Escalate(nowUtc)) continue;
            escalated++;
            var target = EscalationTarget(data, item, nowUtc);
            History(data, item, from, $"ارجاع سطح {item.EscalationLevel}",
                target is null ? "گیرنده ارجاع پیدا نشد." : $"ارجاع به کارتابل {target.Value.Name}", null, nowUtc);
            if (target is null) continue;
            data.WorkItems.Add(new CrmWorkItem(Guid.NewGuid(),
                $"ارجاع سطح {item.EscalationLevel} پرونده {item.Code}: نقض SLA", "فوری", nowUtc.AddHours(2),
                target.Value.UserId, item.CompanyId, item.BranchId, item.TerritoryId));
            workItems++;
        }
        return new ServiceEscalationResult(escalated, workItems);
    }

    /// <summary>Level 1 goes to the branch supervisor (fallback: company sales manager); level 2 goes to the sales manager.</summary>
    private static (Guid UserId, string Name)? EscalationTarget(CrmDataSet data, ServiceCase item, DateTimeOffset nowUtc)
    {
        var assignments = data.UserRoleAssignments.Where(x => Same(x.CompanyId, item.CompanyId) && x.IsEffective(nowUtc))
            .Join(data.Users.Where(x => x.IsActiveAt(nowUtc)), a => a.CrmUserId, u => u.Id, (a, u) => (a, u)).ToList();
        var supervisor = assignments.Where(x => x.a.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase) &&
                x.a.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) && Same(x.a.ScopeId, item.BranchId))
            .OrderBy(x => x.u.DisplayName).Select(x => ((Guid, string)?)(x.u.Id, x.u.DisplayName)).FirstOrDefault();
        var manager = assignments.Where(x => x.a.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) &&
                x.a.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.u.DisplayName).Select(x => ((Guid, string)?)(x.u.Id, x.u.DisplayName)).FirstOrDefault();
        return item.EscalationLevel >= 2 ? manager ?? supervisor : supervisor ?? manager;
    }

    private static IReadOnlyList<SalesOwnerOptionDto> EligibleOwners(CrmDataSet data, string companyId, string branchId, DateTimeOffset nowUtc) =>
        data.UserRoleAssignments.Where(x => Same(x.CompanyId, companyId) && x.IsEffective(nowUtc) && ServiceOwnerRoles.Contains(x.RoleKey) &&
                (x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
                 x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) && Same(x.ScopeId, branchId)))
            .Join(data.Users.Where(x => x.IsActiveAt(nowUtc)), a => a.CrmUserId, u => u.Id, (a, u) => (a, u))
            .GroupBy(x => x.u.Id).Select(g => g.First())
            .OrderBy(x => x.u.DisplayName)
            .Select(x => new SalesOwnerOptionDto(x.u.Id, x.u.DisplayName, branchId, x.a.RoleLabel)).ToList();

    private static ServiceCase Required(CrmDataSet data, AccessSnapshot snapshot, Guid userId, OrganizationSelection organization, Guid id) =>
        data.ServiceCases.SingleOrDefault(x => x.Id == id && CanSee(snapshot, userId, organization, x)) ??
        throw new KeyNotFoundException("پرونده در دامنه جاری پیدا نشد.");

    private static bool CanSee(AccessSnapshot snapshot, Guid userId, OrganizationSelection organization, ServiceCase item) =>
        InContext(snapshot, organization, "Service.Read", item) &&
        (HasPermission(snapshot, organization.CompanyId, "Service.Triage") || HasPermission(snapshot, organization.CompanyId, "Service.ReadAll") ||
         item.OwnerUserId == userId || item.CreatedByUserId == userId);

    private static void History(CrmDataSet data, ServiceCase item, ServiceCaseStatus? from, string action, string note, Guid? actor, DateTimeOffset nowUtc) =>
        data.ServiceCaseHistory.Add(new ServiceCaseHistory(Guid.NewGuid(), item.Id, item.CompanyId, item.BranchId, item.TerritoryId,
            from, item.Status, action, note, actor, nowUtc));

    private static string NextCode(CrmDataSet data, DateTimeOffset nowUtc) =>
        RecordCodes.Next(data.ServiceCases.Select(x => x.Code), $"CS-{nowUtc.Year}-", 1001, 4);

    private static Dictionary<Guid, string> CustomerNames(CrmDataSet data, string companyId) =>
        data.Customers.Where(x => Same(x.CompanyId, companyId)).ToDictionary(x => x.Id, x => x.Name);

    private static ServiceCaseDto Map(ServiceCase x, IReadOnlyDictionary<Guid, string> customers, DateTimeOffset now) => new(
        x.Id, x.Code, x.Subject, x.Description, x.CustomerId, customers.GetValueOrDefault(x.CustomerId, "—"),
        x.CompanyId, x.BranchId, x.TerritoryId, x.Category, x.Channel, x.Priority, x.Status, x.OwnerUserId,
        x.Owner, x.OpenedAtUtc, x.FirstResponseDueAtUtc, x.ResolutionDueAtUtc, x.FirstRespondedAtUtc,
        x.ResolvedAtUtc, x.ClosedAtUtc, x.PausedMinutes, x.EscalationLevel, x.RootCause, x.CorrectiveAction,
        x.Resolution, x.ReopenCount, x.SatisfactionScore, x.SatisfactionComment, x.FirstResponseSla(now),
        x.ResolutionSla(now), x.Version);

    private static string ActorName(CrmDataSet data, Guid? id) =>
        id is { } value ? data.Users.SingleOrDefault(x => x.Id == value)?.DisplayName ?? "—" : "سیستم";

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

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
