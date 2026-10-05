using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Channel;
using Crm.Domain.Commercial;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.FollowUps;
using Crm.Domain.Identity;
using Crm.Domain.Sales;
using static Crm.Application.Services.FollowUpSupport;
using P = Crm.Application.Services.FollowUpPermissions;

namespace Crm.Application.Services;

public interface IFollowUpService
{
    Task<FollowUpListDto> GetListAsync(Guid userId, OrganizationSelection organization, FollowUpListQuery query, DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default);
    FollowUpCreateOptions GetCreateOptions(Guid userId, OrganizationSelection organization, Guid? customerId, string? caseType, string? branchId, DateTimeOffset nowUtc);
    IReadOnlyList<FollowUpDuplicateDto> FindDuplicates(Guid userId, OrganizationSelection organization, Guid customerId, string? caseType, string? subject,
        Guid? relatedId, string? relatedCode);
    FollowUpActionResult Create(Guid userId, OrganizationSelection organization, CreateFollowUpCommand command, DateTimeOffset nowUtc);
    FollowUpCaseDto? GetCase(Guid userId, OrganizationSelection organization, Guid caseId, DateTimeOffset nowUtc);
    IReadOnlyList<FollowUpReferralDto> GetInbox(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc);
    IReadOnlyList<AssignmentCandidateDto> GetReceivers(Guid userId, OrganizationSelection organization, Guid caseId, string? branchId, DateTimeOffset nowUtc);

    FollowUpActionResult PlanAction(Guid userId, OrganizationSelection organization, Guid caseId, PlanFollowUpActionCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult RecordResult(Guid userId, OrganizationSelection organization, Guid caseId, Guid activityId, RecordFollowUpResultCommand command,
        DateTimeOffset nowUtc);
    FollowUpActionResult Refer(Guid userId, OrganizationSelection organization, Guid caseId, ReferFollowUpCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult RespondReferral(Guid userId, OrganizationSelection organization, Guid referralId, bool accept, string? note, DateTimeOffset nowUtc);
    FollowUpActionResult SaveChecklist(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, IReadOnlyCollection<Guid> doneItemIds,
        DateTimeOffset nowUtc);
    FollowUpActionResult CompleteStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, DateTimeOffset nowUtc);
    FollowUpActionResult SkipStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, string? reason, DateTimeOffset nowUtc);
    FollowUpActionResult AssignStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, Guid responsibleUserId, DateTimeOffset nowUtc);
    FollowUpActionResult Wait(Guid userId, OrganizationSelection organization, Guid caseId, WaitFollowUpCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult Resume(Guid userId, OrganizationSelection organization, Guid caseId, ResumeFollowUpCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult UploadDocument(Guid userId, OrganizationSelection organization, Guid caseId, Guid? stageId, FollowUpDocumentKind kind, string? title,
        bool needsApproval, FollowUpDocumentUpload file, DateTimeOffset nowUtc);
    (byte[] Content, string ContentType, string FileName)? DownloadDocument(Guid userId, OrganizationSelection organization, Guid caseId, Guid followUpDocumentId);
    FollowUpActionResult RequestApproval(Guid userId, OrganizationSelection organization, Guid caseId, RequestFollowUpApprovalCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult DecideApproval(Guid userId, OrganizationSelection organization, Guid caseId, Guid approvalId, DecideFollowUpApprovalCommand command,
        DateTimeOffset nowUtc);
    FollowUpActionResult Close(Guid userId, OrganizationSelection organization, Guid caseId, CloseFollowUpCommand command, FollowUpDocumentUpload? customerApproval,
        DateTimeOffset nowUtc);
    FollowUpActionResult Reopen(Guid userId, OrganizationSelection organization, Guid caseId, ReopenFollowUpCommand command, DateTimeOffset nowUtc);
    FollowUpActionResult Cancel(Guid userId, OrganizationSelection organization, Guid caseId, string? reason, DateTimeOffset nowUtc);
    FollowUpActionResult AddItem(Guid userId, OrganizationSelection organization, Guid caseId, FollowUpPartInput item, DateTimeOffset nowUtc);
    FollowUpActionResult UpdateItem(Guid userId, OrganizationSelection organization, Guid caseId, Guid itemId, decimal deliveredQuantity, FollowUpItemStatus status,
        string? note, bool remove, DateTimeOffset nowUtc);
    FollowUpActionResult TriggerRule(Guid userId, OrganizationSelection organization, Guid caseId, int ruleIndex, DateTimeOffset nowUtc);
    FollowUpActionResult Reassign(Guid userId, OrganizationSelection organization, Guid caseId, Guid ownerUserId, string? branchId, string? reason, DateTimeOffset nowUtc);
    FollowUpActionResult Merge(Guid userId, OrganizationSelection organization, Guid sourceCaseId, string? targetCode, string? reason, DateTimeOffset nowUtc);
    FollowUpMonitorResult RunMonitor(DateTimeOffset nowUtc);
}

/// <summary>
/// مرکز پیگیری: follow-up cases from registration to closing. Every write checks the permission inside the case's branch and that
/// the user is involved in the case (or supervises); the list and the counters are computed in the data source.
/// </summary>
public sealed partial class FollowUpService(ICrmDataStore store, IAccessSnapshotService access, IAccountActivityService activities,
    ICrmQuerySource? querySource = null) : IFollowUpService
{
    private AccessSnapshot Snapshot(Guid userId) => access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");

    // ───────────── List ─────────────

    public async Task<FollowUpListDto> GetListAsync(Guid userId, OrganizationSelection organization, FollowUpListQuery request, DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = Snapshot(userId);
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var source = querySource ?? store as ICrmQuerySource ?? throw new InvalidOperationException("No query source is configured for follow-ups.");
        var supervise = Supervises(snapshot, organization.CompanyId);
        var stagesQ = source.Query<FollowUpStage>();
        var referralsQ = source.Query<FollowUpReferral>();
        var approvalsQ = source.Query<FollowUpApproval>();
        var visible = source.Query<FollowUpCase>().InScope(snapshot, organization, P.Read);
        if (!supervise)
            visible = visible.Where(c => c.OwnerUserId == userId || c.CreatedByUserId == userId || c.NextActionOwnerUserId == userId ||
                stagesQ.Any(s => s.CaseId == c.Id && s.ResponsibleUserId == userId) ||
                referralsQ.Any(r => r.CaseId == c.Id && r.ToUserId == userId && r.Status != ReferralStatus.Cancelled) ||
                approvalsQ.Any(a => a.CaseId == c.Id && a.ApproverUserId == userId));
        var open = visible.Where(c => c.Status != FollowUpStatus.Closed && c.Status != FollowUpStatus.Cancelled);
        var overdue = open.Where(c => c.NextActionAtUtc < now || c.PausedSinceUtc == null &&
            (c.ResolutionDueAtUtc < now || c.FirstRespondedAtUtc == null && c.FirstResponseDueAtUtc < now));
        var waiting = open.Where(c => c.Status == FollowUpStatus.WaitingCustomer || c.Status == FollowUpStatus.WaitingInternal || c.Status == FollowUpStatus.OnHold);
        var referrals = open.Where(c => referralsQ.Any(r => r.CaseId == c.Id && r.ToUserId == userId && r.Status == ReferralStatus.Pending));

        var view = request.View?.Trim().ToLowerInvariant() is "mine" or "nonext" or "overdue" or "waiting" or "referrals" or "closed" ? request.View.Trim().ToLowerInvariant() : "all";
        var filtered = view switch
        {
            "mine" => open.Where(c => c.OwnerUserId == userId),
            "nonext" => open.Where(c => c.NextActionAtUtc == null),
            "overdue" => overdue,
            "waiting" => waiting,
            "referrals" => referrals,
            "closed" => visible.Where(c => c.Status == FollowUpStatus.Closed || c.Status == FollowUpStatus.Cancelled),
            _ => open
        };
        if (!string.IsNullOrWhiteSpace(request.CaseType)) filtered = filtered.Where(c => c.CaseType == request.CaseType);
        if (request.Status is { } status) filtered = filtered.Where(c => c.Status == status);
        if (request.Priority is { } priority) filtered = filtered.Where(c => c.Priority == priority);
        if (!string.IsNullOrWhiteSpace(request.BranchId)) filtered = filtered.Where(c => c.BranchId == request.BranchId);
        if (request.OwnerUserId is { } owner) filtered = filtered.Where(c => c.OwnerUserId == owner);
        if (request.CustomerId is { } customer) filtered = filtered.Where(c => c.CustomerId == customer);
        var term = request.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            var customers = source.Query<Customer>();
            filtered = filtered.Where(c => c.Code.Contains(term) || c.Subject.Contains(term) || c.RelatedCode != null && c.RelatedCode.Contains(term) ||
                customers.Any(x => x.Id == c.CustomerId && x.Name.Contains(term)));
        }
        var sort = request.Sort is "priority" or "recent" or "progress" ? request.Sort : "due";
        var ordered = sort switch
        {
            "priority" => filtered.OrderByDescending(c => c.Priority).ThenBy(c => c.NextActionAtUtc).ThenBy(c => c.Id),
            "recent" => filtered.OrderByDescending(c => c.OpenedAtUtc).ThenBy(c => c.Id),
            "progress" => filtered.OrderBy(c => c.ProgressPercent).ThenBy(c => c.Id),
            // Cases without a next action first (they need attention), then by the next action time.
            _ => filtered.OrderBy(c => c.NextActionAtUtc == null ? 0 : 1).ThenBy(c => c.NextActionAtUtc).ThenByDescending(c => c.Priority).ThenBy(c => c.Id)
        };
        var pageSize = request.PageSize is 10 or 20 or 50 or 100 ? request.PageSize : Math.Clamp(request.PageSize, 1, PageRequest.MaxPageSize);
        var page = await ordered.ToPageAsync(source, PageRequest.Of(request.Page, pageSize), x => x, term, cancellationToken);
        var rows = await Rows(source, page.Items, userId, now, cancellationToken);

        var ownerIds = (await source.ToListAsync(open.Where(c => c.OwnerUserId != null).Select(c => c.OwnerUserId!.Value).Distinct(), cancellationToken)).ToArray();
        var owners = (await source.ToListAsync(source.Query<CrmUser>().Where(u => ownerIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }), cancellationToken))
            .OrderBy(x => x.DisplayName).Select(x => (x.Id, x.DisplayName)).ToList();
        var permissions = snapshot.PermissionsFor(organization.CompanyId);
        return new FollowUpListDto(rows, page.Page, page.PageSize, page.TotalCount)
        {
            View = view, Query = term, CaseType = request.CaseType, Status = request.Status, Priority = request.Priority, BranchId = request.BranchId,
            OwnerUserId = request.OwnerUserId, Sort = sort,
            OpenCount = await source.CountAsync(open, cancellationToken),
            MineCount = await source.CountAsync(open.Where(c => c.OwnerUserId == userId), cancellationToken),
            NoNextActionCount = await source.CountAsync(open.Where(c => c.NextActionAtUtc == null), cancellationToken),
            OverdueCount = await source.CountAsync(overdue, cancellationToken),
            WaitingCount = await source.CountAsync(waiting, cancellationToken),
            ReferralCount = await source.CountAsync(referrals, cancellationToken),
            Owners = owners,
            CanCreate = permissions.Contains(P.Create),
            CanSupervise = supervise,
            CanConfigure = permissions.Contains(P.Configure)
        };
    }

    private static async Task<List<FollowUpRowDto>> Rows(ICrmQuerySource source, IReadOnlyList<FollowUpCase> cases, Guid userId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (cases.Count == 0) return [];
        var ids = cases.Select(x => x.Id).ToArray();
        var customerIds = cases.Select(x => x.CustomerId).Distinct().ToArray();
        var customers = (await source.ToListAsync(source.Query<Customer>().Where(x => customerIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }), cancellationToken))
            .ToDictionary(x => x.Id, x => x.Name);
        var stages = (await source.ToListAsync(source.Query<FollowUpStage>().Where(s => ids.Contains(s.CaseId) &&
                (s.Status == FollowUpStageStatus.Active || s.Status == FollowUpStageStatus.Returned || s.Status == FollowUpStageStatus.Waiting)), cancellationToken))
            .GroupBy(x => x.CaseId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order).First());
        var pending = (await source.ToListAsync(source.Query<FollowUpReferral>().Where(r => ids.Contains(r.CaseId) && r.Status == ReferralStatus.Pending)
            .Select(r => r.CaseId), cancellationToken)).ToHashSet();
        var userIds = cases.Select(x => x.OwnerUserId).Concat(stages.Values.Select(x => x.ResponsibleUserId)).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        var users = (await source.ToListAsync(source.Query<CrmUser>().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }), cancellationToken))
            .ToDictionary(x => x.Id, x => x.DisplayName);
        return cases.Select(c =>
        {
            stages.TryGetValue(c.Id, out var stage);
            return new FollowUpRowDto(c.Id, c.Code, c.Subject, c.CustomerId, customers.GetValueOrDefault(c.CustomerId, "—"), c.CaseType, c.Priority, c.Status,
                c.ProgressPercent, stage?.Name, c.OwnerUserId is { } o ? users.GetValueOrDefault(o) : null,
                stage?.ResponsibleUserId is { } r ? users.GetValueOrDefault(r) : null, c.NextAction, c.NextActionAtUtc, c.NearestDueUtc, c.IsOverdue(now),
                c.IsPaused, c.ReopenCount, pending.Contains(c.Id), c.BranchId);
        }).ToList();
    }

    // ───────────── Create (۱) ─────────────

    public FollowUpCreateOptions GetCreateOptions(Guid userId, OrganizationSelection organization, Guid? customerId, string? caseType, string? branchId, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        if (!Has(snapshot, organization.CompanyId, P.Create)) throw new UnauthorizedAccessException("FollowUp.Create permission is required.");
        var type = FollowUpCaseTypes.Exists(caseType) ? caseType! : "Proforma";
        return store.Read(data =>
        {
            var customers = data.Customers.Where(x => x.Status != CustomerStatus.Inactive && AccountGuard.InContext(snapshot, organization, "Customer.Read", x))
                .OrderBy(x => x.Name).Take(500).ToList();
            var customer = customerId is { } cid ? customers.FirstOrDefault(x => x.Id == cid) : null;
            var branch = branchId ?? customer?.BranchId ?? organization.BranchId ?? customers.FirstOrDefault()?.BranchId ?? string.Empty;
            var contacts = customer is null ? [] : data.Find<CustomerContact>(x => x.CustomerId == customer.Id && x.IsActive)
                .Select(x => new FollowUpOptionDto(x.Id.ToString(), $"{x.FullName}{(string.IsNullOrWhiteSpace(x.Role) ? "" : " · " + x.Role)}")).ToList();
            var templates = data.Find<FollowUpTemplate>(x => x.CompanyId == organization.CompanyId && x.Status == FollowUpTemplateStatus.Published)
                .OrderBy(x => x.CaseType == type ? 0 : 1).ThenBy(x => x.Name)
                .Select(x => new FollowUpOptionDto(x.Id.ToString(), $"{x.Name} · نسخه {x.TemplateVersion}")).ToList();
            var template = data.Find<FollowUpTemplate>(x => x.CompanyId == organization.CompanyId && x.Status == FollowUpTemplateStatus.Published && x.CaseType == type)
                .OrderByDescending(x => x.TemplateVersion).FirstOrDefault();
            var branches = data.Find<Crm.Domain.Organization.OrganizationUnit>(x => x.CompanyId == organization.CompanyId && x.Type == Crm.Domain.Organization.OrganizationUnitType.Branch)
                .Where(x => snapshot.AllowsRecord(organization.CompanyId, P.Create, x.UnitId, null) && (organization.BranchId is null || AccountGuard.Same(x.UnitId, organization.BranchId)))
                .OrderBy(x => x.Name).Select(x => new FollowUpOptionDto(x.UnitId, x.Name)).ToList();
            var owners = BranchUsers(data, organization.CompanyId, branch, null, nowUtc).Select(x => new FollowUpOptionDto(x.Id.ToString(), x.Name)).ToList();
            var queues = data.Find<FollowUpQueue>(x => x.CompanyId == organization.CompanyId && x.IsActive).OrderBy(x => x.RuleOrder)
                .Select(x => new FollowUpOptionDto(x.Id.ToString(), x.Name)).ToList();
            var dealers = data.Find<Dealer>(x => x.CompanyId == organization.CompanyId).OrderBy(x => x.TradeName)
                .Select(x => new FollowUpOptionDto(x.Id.ToString(), x.TradeName)).ToList();
            var related = new List<(FollowUpRelatedKind, Guid, string)>();
            if (customer is not null)
            {
                related.AddRange(data.Find<Opportunity>(x => x.CustomerId == customer.Id && x.Stage != OpportunityStage.Won && x.Stage != OpportunityStage.Lost)
                    .Select(x => (FollowUpRelatedKind.Opportunity, x.Id, $"فرصت {x.Code} · {x.Title}")));
                related.AddRange(data.Find<Quote>(x => x.CustomerId == customer.Id).Select(x => (FollowUpRelatedKind.Quote, x.Id, $"پیش‌فاکتور {x.Code}")));
                var quoteIds = data.Find<Quote>(x => x.CustomerId == customer.Id).Select(x => x.Id).ToArray();
                if (quoteIds.Length > 0)
                    related.AddRange(data.Find<OrderRequest>(x => quoteIds.Contains(x.QuoteId)).Select(x => (FollowUpRelatedKind.Order, x.Id, $"سفارش {x.Code}")));
            }
            var suggestion = customer is null || string.IsNullOrWhiteSpace(branch) ? null : Suggest(data, organization.CompanyId, branch, type, null, "fa", nowUtc);
            var policy = Policy(data, organization.CompanyId, template?.SlaPolicyId, template?.DefaultPriority ?? FollowUpPriority.Normal, type);
            var (first, resolution) = Dues(policy, nowUtc);
            var preview = policy is null
                ? "سیاست مهلت تعریف نشده؛ پاسخ اولیه ۴ ساعت و حل ۳ روز تقویمی"
                : $"«{policy.Name}»: پاسخ اولیه تا {TehranTime.Format(first)}، حل کامل تا {TehranTime.Format(resolution)} (ساعات کاری)";
            return new FollowUpCreateOptions(customer?.Id, customer?.Name, customers.Select(x => new FollowUpOptionDto(x.Id.ToString(), $"{x.Name} · {x.Code}")).ToList(),
                contacts, templates, branches, owners, queues, dealers, related, FollowUpCaseTypes.ExtraFields(type), type,
                suggestion?.UserName is { } name ? $"{name} — {suggestion.Explanation}" : suggestion?.Explanation, preview, template?.ClosingCriteria)
            {
                SuggestedQueueId = suggestion?.QueueId,
                SuggestedOwnerId = suggestion?.UserId
            };
        });
    }

    public IReadOnlyList<FollowUpDuplicateDto> FindDuplicates(Guid userId, OrganizationSelection organization, Guid customerId, string? caseType, string? subject,
        Guid? relatedId, string? relatedCode)
    {
        var snapshot = Snapshot(userId);
        return store.Read(data => Duplicates(data, organization.CompanyId, customerId, caseType, subject, relatedId, relatedCode)
            .Where(x => CanSee(data, snapshot, organization, userId, x.Case) || Has(snapshot, organization.CompanyId, P.Create))
            .Select(x => new FollowUpDuplicateDto(x.Case.Id, x.Case.Code, x.Case.Subject, x.Case.Status, x.Reason)).ToList());
    }

    /// <summary>Open cases of the same customer that look like the same subject: same related record, or same type with a similar subject.</summary>
    private static List<(FollowUpCase Case, string Reason)> Duplicates(CrmDataSet data, string companyId, Guid customerId, string? caseType, string? subject,
        Guid? relatedId, string? relatedCode)
    {
        static string Norm(string? text) => new((PersianText.Normalize(text) ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var wanted = Norm(subject);
        var result = new List<(FollowUpCase, string)>();
        foreach (var c in data.Find<FollowUpCase>(x => x.CompanyId == companyId && x.CustomerId == customerId &&
                     x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled))
        {
            if (relatedId is not null && c.RelatedId == relatedId) result.Add((c, "همان رکورد مرتبط"));
            else if (!string.IsNullOrWhiteSpace(relatedCode) && AccountGuard.Same(c.RelatedCode, relatedCode?.Trim())) result.Add((c, "همان شمارهٔ سند مرتبط"));
            else if (AccountGuard.Same(c.CaseType, caseType) && wanted.Length >= 4)
            {
                var existing = Norm(c.Subject);
                if (existing == wanted || existing.Length >= 6 && wanted.Contains(existing) || wanted.Length >= 6 && existing.Contains(wanted))
                    result.Add((c, "همان نوع پیگیری با موضوع مشابه"));
            }
        }
        return result;
    }

    public FollowUpActionResult Create(Guid userId, OrganizationSelection organization, CreateFollowUpCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        if (!Has(snapshot, organization.CompanyId, P.Create)) throw new UnauthorizedAccessException("FollowUp.Create permission is required.");
        if (string.IsNullOrWhiteSpace(command.Subject)) throw new InvalidOperationException("موضوع پیگیری الزامی است.");
        if (!FollowUpCaseTypes.Exists(command.CaseType)) throw new InvalidOperationException("نوع پیگیری را انتخاب کنید.");
        var assignedTo = Guid.Empty;
        var result = store.Write(data =>
        {
            if (ClientOperations.Existing(data, userId, command.OperationId) is { } replayed)
                return new FollowUpActionResult(replayed, "پرونده قبلاً ثبت شده بود.", Replayed: true);
            var customer = data.Find<Customer>(x => x.Id == command.CustomerId).SingleOrDefault(x => AccountGuard.InContext(snapshot, organization, "Customer.Read", x)) ??
                throw new InvalidOperationException("مشتری / حساب را از دامنهٔ مجاز انتخاب کنید.");
            if (customer.Status == CustomerStatus.Inactive) throw new InvalidOperationException("حساب غیرفعال است؛ برای آن پرونده جدید باز نمی‌شود.");
            var branchId = string.IsNullOrWhiteSpace(command.BranchId) ? customer.BranchId : command.BranchId.Trim();
            if (organization.BranchId is not null && !AccountGuard.Same(organization.BranchId, branchId))
                throw new UnauthorizedAccessException("شعبه خارج از محیط کاری انتخاب‌شده است.");
            if (!snapshot.AllowsRecord(organization.CompanyId, P.Create, branchId, customer.TerritoryId))
                throw new UnauthorizedAccessException("ثبت پرونده در این شعبه مجاز نیست.");
            var template = (command.TemplateId is { } tid
                ? data.Find<FollowUpTemplate>(x => x.Id == tid && x.CompanyId == organization.CompanyId && x.Status == FollowUpTemplateStatus.Published).SingleOrDefault()
                : data.Find<FollowUpTemplate>(x => x.CompanyId == organization.CompanyId && x.Status == FollowUpTemplateStatus.Published && x.CaseType == command.CaseType)
                    .OrderByDescending(x => x.TemplateVersion).FirstOrDefault())
                ?? throw new InvalidOperationException("برای این نوع پیگیری الگوی گردش کار منتشرشده‌ای وجود ندارد.");
            var duplicates = Duplicates(data, organization.CompanyId, customer.Id, command.CaseType, command.Subject, command.RelatedId, command.RelatedCode);
            if (duplicates.Count > 0 && !command.AllowDuplicate)
                throw new InvalidOperationException("پرونده باز مشابه وجود دارد: " + string.Join("، ", duplicates.Select(x => $"{x.Case.Code} ({x.Reason})")) +
                    ". پیگیری را به همان پرونده اضافه کنید یا با ذکر دلیل، پرونده مستقل بسازید.");
            if (duplicates.Count > 0 && string.IsNullOrWhiteSpace(command.DuplicateReason))
                throw new InvalidOperationException("برای ثبت پرونده مستقل کنار پرونده مشابه، دلیل الزامی است.");
            if (command.ContactId is { } contact && !data.Find<CustomerContact>(x => x.Id == contact && x.CustomerId == customer.Id && x.IsActive).Any())
                throw new InvalidOperationException("شخص تماس به این مشتری تعلق ندارد.");
            if (command.DealerId is { } dealer && !data.Find<Dealer>(x => x.Id == dealer && x.CompanyId == organization.CompanyId).Any())
                throw new InvalidOperationException("نمایندگی انتخاب‌شده معتبر نیست.");
            var templateStages = data.Find<FollowUpTemplateStage>(x => x.TemplateId == template.Id);
            if (templateStages.Count == 0) throw new InvalidOperationException("الگوی انتخاب‌شده مرحله‌ای ندارد.");

            var code = RecordCodes.Next(data.Find<FollowUpCase>(x => x.CompanyId == organization.CompanyId).Select(x => x.Code), "RQ-", 24001, 5);
            var c = new FollowUpCase(Guid.NewGuid(), code, organization.CompanyId, branchId, customer.TerritoryId, customer.Id, command.Subject!, command.CaseType!,
                template.Id, template.TemplateVersion, command.Priority, command.Channel, userId, nowUtc);
            var extra = string.Join('\n', (command.ExtraFields ?? new Dictionary<string, string?>())
                .Where(x => FollowUpCaseTypes.ExtraFields(command.CaseType).Contains(x.Key) && !string.IsNullOrWhiteSpace(x.Value)).Select(x => $"{x.Key}: {x.Value!.Trim()}"));
            c.Describe(command.Description, command.ExpectedOutcome ?? template.ClosingCriteria, command.ContactId, command.DealerId, command.RelatedKind, command.RelatedId,
                command.RelatedCode, command.PartFamily, command.Language, extra, command.PriorityReason, command.ParentCaseId);
            var policy = Policy(data, organization.CompanyId, template.SlaPolicyId, command.Priority, command.CaseType!);
            var (firstDue, resolutionDue) = Dues(policy, nowUtc);
            c.ApplySla(policy?.Id, firstDue, resolutionDue);

            // Owner: chosen explicitly (assigning someone else needs FollowUp.Assign), or by the matching queue.
            Guid? owner = null;
            Guid? queueId = command.QueueId;
            if (command.OwnerUserId is { } chosen && chosen != Guid.Empty)
            {
                // Picking someone else needs FollowUp.Assign, unless it is the person the assignment rules suggest.
                if (chosen != userId && !Has(snapshot, organization.CompanyId, P.Assign) &&
                    Suggest(data, organization.CompanyId, branchId, command.CaseType!, command.PartFamily, command.Language ?? "fa", nowUtc, command.QueueId).UserId != chosen)
                    throw new UnauthorizedAccessException("تعیین مسئول دیگر برای پرونده نیازمند مجوز FollowUp.Assign است.");
                if (!BranchUsers(data, organization.CompanyId, branchId, customer.TerritoryId, nowUtc).Any(x => x.Id == chosen))
                    throw new InvalidOperationException("مسئول پرونده باید کاربر فعال در دامنهٔ شعبه باشد.");
                owner = chosen;
            }
            else
            {
                var suggestion = Suggest(data, organization.CompanyId, branchId, command.CaseType!, command.PartFamily, command.Language ?? "fa", nowUtc, command.QueueId);
                owner = suggestion.UserId;
                queueId = suggestion.QueueId;
                if (owner is { } picked && suggestion.QueueId is { } q) MarkAssigned(data, q, picked, nowUtc);
            }
            if (owner is { } ownerId) c.AssignOwner(ownerId, queueId);
            else c.LeaveUnassigned(queueId);
            data.Append(c);
            var (stages, items) = Instantiate(data, c, templateStages);
            foreach (var part in command.Parts ?? [])
                if (!string.IsNullOrWhiteSpace(part.PartCode))
                    data.Append(new FollowUpItem(Guid.NewGuid(), c.Id, part.PartCode!, part.Description, part.Quantity ?? 1, part.Unit));
            Log(data, c, "Created", $"پرونده {c.Code} ثبت شد", $"نوع: {FollowUpCaseTypes.Label(c.CaseType)} · الگو: {template.Name} (نسخه {template.TemplateVersion}) · اولویت: {PriorityLabel(c.Priority)}" +
                (duplicates.Count > 0 ? $" · مستقل از {string.Join("، ", duplicates.Select(x => x.Case.Code))}: {command.DuplicateReason}" : ""), userId, nowUtc);
            Recompute(data, c, userId, nowUtc, null, stages, items);
            if (owner is { } assigned)
            {
                c.PlanNextAction("پاسخ اولیه به مشتری", firstDue, assigned, nowUtc);
                Log(data, c, "Owner", $"مسئول پرونده: {AccountGuard.UserNames(data, [assigned]).GetValueOrDefault(assigned, "—")}",
                    command.OwnerUserId is null ? "تخصیص خودکار براساس صف و ظرفیت" : "انتخاب در ثبت پرونده", userId, nowUtc);
                assignedTo = assigned;
            }
            else
            {
                Log(data, c, "Owner", "پرونده در انتظار تخصیص است", "در صف فرد واجد شرایط آزاد نبود؛ سرپرست مطلع شد.", userId, nowUtc);
                Notify(data, c, Supervisors(data, c, nowUtc), $"پرونده {c.Code} بدون مسئول", $"{c.Subject} — در صف تخصیص فرد آزاد نبود.", $"fu-unassigned:{c.Id}", nowUtc);
            }
            ClientOperations.Record(data, userId, command.OperationId, "FollowUpCase", c.Id);
            if (assignedTo != Guid.Empty && assignedTo != userId)
                Notify(data, c, [assignedTo], $"پرونده {c.Code} به شما سپرده شد", c.Subject, $"fu-owner:{c.Id}:{assignedTo}", nowUtc);
            return new FollowUpActionResult(c.Id, $"پرونده {c.Code} ثبت شد.");
        });
        return result;
    }

    // ───────────── Case page ─────────────

    public FollowUpCaseDto? GetCase(Guid userId, OrganizationSelection organization, Guid caseId, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        return store.Read(data =>
        {
            var c = data.Find<FollowUpCase>(x => x.Id == caseId).SingleOrDefault(x => CanSee(data, snapshot, organization, userId, x));
            if (c is null) return null;
            var template = data.Find<FollowUpTemplate>(x => x.Id == c.TemplateId).SingleOrDefault();
            var policy = c.SlaPolicyId is { } pid ? data.Find<SlaPolicy>(x => x.Id == pid).SingleOrDefault() : null;
            var stages = Stages(data, c.Id);
            var checklist = data.Find<FollowUpChecklistItem>(x => x.CaseId == c.Id);
            var items = data.Find<FollowUpItem>(x => x.CaseId == c.Id && !x.IsRemoved);
            var links = data.Find<FollowUpActivityLink>(x => x.CaseId == c.Id).ToDictionary(x => x.Id);
            var linkIds = links.Keys.ToArray();
            var acts = data.Find<CrmActivity>(x => x.RelatedKind == ActivityRelatedKind.FollowUpCase && x.RelatedId == c.Id);
            var referrals = data.Find<FollowUpReferral>(x => x.CaseId == c.Id).OrderByDescending(x => x.SentAtUtc).ToList();
            var documents = data.Find<FollowUpDocument>(x => x.CaseId == c.Id);
            var docIds = documents.Select(x => x.DocumentId).ToArray();
            var files = docIds.Length == 0 ? [] : data.Find<CrmDocument>(x => docIds.Contains(x.Id)).ToDictionary(x => x.Id);
            var approvals = data.Find<FollowUpApproval>(x => x.CaseId == c.Id).OrderByDescending(x => x.RequestedAtUtc).ToList();
            var events = data.Find<FollowUpEvent>(x => x.CaseId == c.Id).OrderByDescending(x => x.AtUtc).Take(300).ToList();
            var contactRows = data.Find<CustomerContact>(x => x.CustomerId == c.CustomerId);
            var contacts = contactRows.ToDictionary(x => x.Id, x => x.FullName);
            var userIds = new[] { c.OwnerUserId, c.NextActionOwnerUserId }.Where(x => x is not null).Select(x => x!.Value)
                .Concat(stages.Select(x => x.ResponsibleUserId).Where(x => x is not null).Select(x => x!.Value))
                .Concat(checklist.Select(x => x.DoneByUserId).Where(x => x is not null).Select(x => x!.Value))
                .Concat(acts.Select(x => x.OwnerUserId)).Concat(referrals.SelectMany(x => new[] { x.FromUserId, x.ToUserId }))
                .Concat(documents.Select(x => x.UploadedByUserId)).Concat(documents.Select(x => x.ReviewedByUserId ?? Guid.Empty))
                .Concat(approvals.SelectMany(x => new[] { x.RequestedByUserId, x.ApproverUserId, x.CorrectionOwnerUserId ?? Guid.Empty, x.DecidedByUserId ?? Guid.Empty }))
                .Concat(events.Select(x => x.ActorUserId ?? Guid.Empty));
            var names = AccountGuard.UserNames(data, userIds);
            string? Name(Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;
            var stageNames = stages.ToDictionary(x => x.Id, x => x.Name);
            var canUpdate = c.IsOpen && CanAct(data, snapshot, userId, c, P.Update);
            var pendingApprovalStages = approvals.Where(x => x.IsPending && x.StageId is not null).Select(x => x.StageId!.Value).ToHashSet();
            var totalWeight = stages.Where(x => x.Status != FollowUpStageStatus.Skipped).Sum(x => x.Weight);
            var stageDtos = stages.Select(s =>
            {
                var list = checklist.Where(x => x.StageId == s.Id).OrderBy(x => x.Order).ToList();
                var remaining = list.Count(x => !x.IsDone);
                var blocked = !s.IsWorking ? "مرحله فعال نیست" : remaining > 0 ? $"{remaining} مورد چک‌لیست باقی است" :
                    pendingApprovalStages.Contains(s.Id) ? "درخواست تأیید این مرحله باز است" : null;
                return new FollowUpStageDto(s.Id, s.Order, s.Name, s.Weight, s.Required, s.Status, s.Progress, s.ResponsibleRole, s.ResponsibleUserId,
                    Name(s.ResponsibleUserId), s.DueAtUtc, s.IsOverdue(nowUtc), s.StartedAtUtc, s.CompletedAtUtc, s.ReturnReason, s.SkipReason, s.Condition,
                    list.Select(x => new FollowUpChecklistDto(x.Id, x.Title, x.IsDone, Name(x.DoneByUserId), x.DoneAtUtc)).ToList(),
                    s.Status == FollowUpStageStatus.Skipped || totalWeight == 0 ? 0 : (int)Math.Round(s.Weight * (double)s.Progress / totalWeight),
                    canUpdate && blocked is null, blocked);
            }).ToList();
            var activityDtos = acts.OrderByDescending(x => x.StartAtUtc).Select(a =>
            {
                links.TryGetValue(a.Id, out var link);
                return new FollowUpActivityDto(a.Id, a.Type, a.Subject, a.Description, names.GetValueOrDefault(a.OwnerUserId, "—"),
                    a.ContactId is { } ct ? contacts.GetValueOrDefault(ct) : null, link?.Channel ?? FollowUpChannel.Phone, a.StartAtUtc, a.Status, a.IsOverdue(nowUtc),
                    link?.StageId is { } st ? stageNames.GetValueOrDefault(st) : null, link?.ResultCode, a.Outcome,
                    canUpdate && a.Status == ActivityStatus.Planned, a.Version,
                    (link?.PreChecklist ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }).ToList();
            var related = RelatedLink(data, c);
            var calendar = Calendar(policy);
            var checks = CloseChecks(data, c, template, stages, documents, approvals, referrals, acts);
            var permissions = snapshot.PermissionsFor(c.CompanyId);
            var branchName = data.Find<Crm.Domain.Organization.OrganizationUnit>(x => x.UnitId == c.BranchId).Select(x => x.Name).FirstOrDefault() ?? c.BranchId;
            return new FollowUpCaseDto(c.Id, c.Code, c.Subject, c.CustomerId, Customer(data, c.CustomerId), c.ContactId is { } cc ? contacts.GetValueOrDefault(cc) : null,
                c.CaseType, template?.Name ?? "—", c.TemplateVersion, c.Priority, c.PriorityReason, c.Channel, c.Status, c.ProgressPercent, c.BranchId, branchName,
                c.OwnerUserId, Name(c.OwnerUserId), c.QueueId is { } qid ? data.Find<FollowUpQueue>(x => x.Id == qid).Select(x => x.Name).FirstOrDefault() : null,
                c.Description, c.ExpectedOutcome,
                (c.ExtraFields ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':', 2)).Where(x => x.Length == 2)
                    .Select(x => (x[0].Trim(), x[1].Trim())).ToList(),
                c.RelatedKind, related.Label, related.Url,
                c.DealerId is { } did ? data.Find<Dealer>(x => x.Id == did).Select(x => x.TradeName).FirstOrDefault() : null,
                c.NextAction, c.NextActionAtUtc, Name(c.NextActionOwnerUserId), c.FirstResponseDueAtUtc, c.FirstRespondedAtUtc, c.ResolutionDueAtUtc, c.NearestDueUtc,
                c.IsOverdue(nowUtc), c.IsPaused, c.PausedMinutes, (int)calendar.WorkingMinutesBetween(c.OpenedAtUtc, c.ClosedAtUtc ?? nowUtc),
                c.WaitReason, c.WaitingOn, c.ReviewAtUtc, c.Outcome, c.OutcomeNote, c.ClosedAtUtc, c.ReopenCount, c.CancelReason, c.MergedIntoCaseId, c.ParentCaseId,
                c.OpenedAtUtc, policy?.Name ?? "پیش‌فرض (بدون سیاست)",
                stageDtos,
                items.Select(x => new FollowUpPartDto(x.Id, x.PartCode, x.AlternateCode, x.Description, x.Compatibility, x.Quantity, x.Unit, x.Warehouse, x.SerialOrBatch, x.Status,
                    x.DeliveredQuantity, x.Note)).ToList(),
                activityDtos,
                referrals.Select(r => Referral(r, c, names, stageNames, Customer(data, c.CustomerId), userId, nowUtc)).ToList(),
                documents.OrderBy(x => x.Kind).ThenByDescending(x => x.DocumentVersion).Select(d => new FollowUpDocumentDto(d.Id, d.DocumentId, d.Kind,
                    d.Title, d.DocumentVersion, d.Status, names.GetValueOrDefault(d.UploadedByUserId, "—"), d.CreatedAtUtc,
                    d.StageId is { } ds ? stageNames.GetValueOrDefault(ds) : null, Name(d.ReviewedByUserId), d.ReviewNote,
                    files.TryGetValue(d.DocumentId, out var f) ? f.FileName : "—")).ToList(),
                approvals.Select(a => new FollowUpApprovalDto(a.Id, a.StageId is { } st ? stageNames.GetValueOrDefault(st) : null, names.GetValueOrDefault(a.RequestedByUserId, "—"),
                    names.GetValueOrDefault(a.ApproverUserId, "—"), a.ApproverUserId, a.ApproverRole, a.ReviewItemList(), a.PassedItemList(), a.RequestedAtUtc, a.Decision,
                    a.DecisionNote, Name(a.CorrectionOwnerUserId), a.CorrectionDueAtUtc, Name(a.DecidedByUserId), a.DecidedAtUtc,
                    a.IsPending && c.IsOpen && permissions.Contains(P.Approve) && (a.ApproverUserId == userId || Supervises(snapshot, c.CompanyId)))).ToList(),
                events.Select(e => new FollowUpEventDto(e.Kind, e.Title, e.Detail, Name(e.ActorUserId), e.AtUtc)).ToList(),
                checks,
                (template?.RuleList() ?? []).Select((x, i) => new FollowUpRuleDto(i, x.Condition, x.Action)).ToList(),
                BranchUsers(data, c.CompanyId, c.BranchId, c.TerritoryId, nowUtc).Select(x => new FollowUpOptionDto(x.Id.ToString(), x.Name)).ToList(),
                c.Version)
            {
                CanUpdate = canUpdate,
                CanAssign = c.IsOpen && CanAct(data, snapshot, userId, c, P.Assign),
                CanApprove = c.IsOpen && permissions.Contains(P.Approve),
                CanClose = c.IsOpen && CanAct(data, snapshot, userId, c, P.Close),
                CanReopen = c.Status == FollowUpStatus.Closed && CanAct(data, snapshot, userId, c, P.Reopen),
                CanSupervise = Supervises(snapshot, c.CompanyId),
                Approvers = AccountGuard.UserNames(data, NotificationOutbox.UsersWithPermission(data, c.CompanyId, P.Approve, nowUtc))
                    .OrderBy(x => x.Value).Select(x => new FollowUpOptionDto(x.Key.ToString(), x.Value)).ToList(),
                Branches = data.Find<Crm.Domain.Organization.OrganizationUnit>(x => x.CompanyId == c.CompanyId && x.Type == Crm.Domain.Organization.OrganizationUnitType.Branch)
                    .OrderBy(x => x.Name).Select(x => new FollowUpOptionDto(x.UnitId, x.Name)).ToList(),
                NextTemplates = data.Find<FollowUpTemplate>(x => x.CompanyId == c.CompanyId && x.Status == FollowUpTemplateStatus.Published).OrderBy(x => x.Name)
                    .Select(x => new FollowUpOptionDto(x.Id.ToString(), $"{x.Name} · نسخه {x.TemplateVersion}")).ToList(),
                Contacts = contacts.OrderBy(x => x.Value).Select(x => new FollowUpOptionDto(x.Key.ToString(), x.Value)).ToList(),
                ContactPhones = contactRows.ToDictionary(x => x.Id.ToString(), x => x.Phone),
                Teams = data.Find<FollowUpQueue>(x => x.CompanyId == c.CompanyId && x.IsActive).OrderBy(x => x.RuleOrder)
                    .Select(x => new FollowUpOptionDto(x.Name, x.Name)).ToList(),
                PausesOnCustomer = policy?.PauseOnWaitingCustomer ?? true,
                PausesOnInternal = policy?.PauseOnWaitingInternal ?? false
            };
        });
    }

    private static (string? Label, string? Url) RelatedLink(CrmDataSet data, FollowUpCase c) => c.RelatedKind switch
    {
        FollowUpRelatedKind.Opportunity when c.RelatedId is { } id => ($"فرصت {data.Find<Opportunity>(x => x.Id == id).Select(x => x.Code).FirstOrDefault() ?? c.RelatedCode}", $"/opportunities/{id}"),
        FollowUpRelatedKind.Quote when c.RelatedId is { } id => ($"پیش‌فاکتور {data.Find<Quote>(x => x.Id == id).Select(x => x.Code).FirstOrDefault() ?? c.RelatedCode}", "/quotes"),
        FollowUpRelatedKind.Order when c.RelatedId is { } id => ($"سفارش {data.Find<OrderRequest>(x => x.Id == id).Select(x => x.Code).FirstOrDefault() ?? c.RelatedCode}", "/orders"),
        FollowUpRelatedKind.Invoice => ($"فاکتور {c.RelatedCode} (منبع: سامانهٔ حسابداری ارقام)", null),
        FollowUpRelatedKind.ServiceCase when c.RelatedId is { } id => ($"درخواست خدمات {c.RelatedCode}", $"/service/{id}"),
        _ when !string.IsNullOrWhiteSpace(c.RelatedCode) => (c.RelatedCode, null),
        _ => (null, null)
    };

    private static FollowUpReferralDto Referral(FollowUpReferral r, FollowUpCase c, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, string> stageNames,
        string customer, Guid userId, DateTimeOffset nowUtc) =>
        new(r.Id, c.Id, c.Code, c.Subject, customer, r.Scope, r.StageId is { } s ? stageNames.GetValueOrDefault(s) : null, names.GetValueOrDefault(r.FromUserId, "—"),
            names.GetValueOrDefault(r.ToUserId, "—"), r.ToUserId, r.ToBranchId, r.ToTeam, r.Reason, r.SentAtUtc, r.AcceptDueAtUtc, r.Status, r.ResponseNote,
            r.IsLate(nowUtc), r.Status == ReferralStatus.Pending && r.ToUserId == userId,
            string.Join("، ", new[] { r.IncludeHistory ? "سوابق تعاملات" : null, r.IncludeQuote ? "پیش‌فاکتور" : null, r.IncludeTechnical ? "مدارک فنی" : null }
                .Where(x => x is not null)));

    /// <summary>The checks shown on «بستن پرونده»; the case closes only when all of them pass.</summary>
    private static List<FollowUpCloseCheckDto> CloseChecks(CrmDataSet data, FollowUpCase c, FollowUpTemplate? template, IReadOnlyList<FollowUpStage> stages,
        IReadOnlyList<FollowUpDocument> documents, IReadOnlyList<FollowUpApproval> approvals, IReadOnlyList<FollowUpReferral> referrals, IReadOnlyList<CrmActivity> acts)
    {
        var checks = new List<FollowUpCloseCheckDto>();
        var openRequired = stages.Where(x => x.Required && x.Status != FollowUpStageStatus.Done).Select(x => x.Name).ToList();
        if (template?.RequireAllRequiredStages != false)
            checks.Add(new("تمام مراحل الزامی تکمیل شده", openRequired.Count == 0, openRequired.Count == 0 ? null : "باقی‌مانده: " + string.Join("، ", openRequired)));
        var latest = documents.GroupBy(x => (x.Kind, x.Title)).Select(g => g.OrderByDescending(x => x.DocumentVersion).First()).ToList();
        var blockingDocs = latest.Where(x => x.Status is FollowUpDocumentStatus.PendingApproval or FollowUpDocumentStatus.Rejected).Select(x => x.Title).ToList();
        checks.Add(new("مدارک لازم ثبت و تأیید شده", blockingDocs.Count == 0, blockingDocs.Count == 0 ? null : "در انتظار تأیید یا ردشده: " + string.Join("، ", blockingDocs)));
        if (template?.RequireCustomerApproval == true)
        {
            var approved = documents.Any(x => x.Kind == FollowUpDocumentKind.CustomerApproval && x.Status is FollowUpDocumentStatus.Uploaded or FollowUpDocumentStatus.Approved);
            checks.Add(new("تأیید مشتری دریافت شده", approved, approved ? null : "مدرک تأیید مشتری را در همین فرم پیوست کنید."));
        }
        var openActs = acts.Count(x => x.Status == ActivityStatus.Planned);
        checks.Add(new("وظیفه یا فعالیت باز وجود ندارد", openActs == 0, openActs == 0 ? null : $"{openActs} فعالیت برنامه‌ریزی‌شده باز است"));
        var pendingApprovals = approvals.Count(x => x.IsPending);
        checks.Add(new("درخواست تأیید باز وجود ندارد", pendingApprovals == 0, pendingApprovals == 0 ? null : $"{pendingApprovals} درخواست تأیید باز"));
        var pendingRefs = referrals.Count(x => x.Status == ReferralStatus.Pending);
        checks.Add(new("ارجاع در انتظار پذیرش وجود ندارد", pendingRefs == 0, pendingRefs == 0 ? null : $"{pendingRefs} ارجاع پاسخ داده نشده"));
        return checks;
    }

    /// <summary>Possible receivers of a referral in the target branch, with their open cases against their queue capacity.</summary>
    public IReadOnlyList<AssignmentCandidateDto> GetReceivers(Guid userId, OrganizationSelection organization, Guid caseId, string? branchId, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        return store.Read(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId);
            var branch = string.IsNullOrWhiteSpace(branchId) ? c.BranchId : branchId.Trim();
            var loads = Loads(data, c.CompanyId);
            var members = data.Find<FollowUpQueueMember>(x => true).GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (Capacity: g.Max(x => x.Capacity), Available: g.Any(x => x.IsAvailable), Note: g.Select(x => x.AvailabilityNote).FirstOrDefault(x => x != null)));
            return BranchUsers(data, c.CompanyId, branch, null, nowUtc).Select(u =>
            {
                var load = loads.GetValueOrDefault(u.Id);
                var member = members.TryGetValue(u.Id, out var m) ? m : (Capacity: 0, Available: true, Note: (string?)null);
                return new AssignmentCandidateDto(u.Id, u.Name, member.Capacity, load, member.Available, member.Note,
                    member.Available && (member.Capacity == 0 || load < member.Capacity));
            }).ToList();
        });
    }

    public IReadOnlyList<FollowUpReferralDto> GetInbox(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        if (!Has(snapshot, organization.CompanyId, P.Read)) return [];
        return store.Read(data =>
        {
            // Referrals addressed to the user are listed even before acceptance gives them the case's branch scope.
            var referrals = data.Find<FollowUpReferral>(x => x.CompanyId == organization.CompanyId && x.ToUserId == userId)
                .OrderBy(x => x.Status == ReferralStatus.Pending ? 0 : 1).ThenByDescending(x => x.SentAtUtc).Take(50).ToList();
            var caseIds = referrals.Select(x => x.CaseId).Distinct().ToArray();
            var cases = data.Find<FollowUpCase>(x => caseIds.Contains(x.Id)).ToDictionary(x => x.Id);
            var stageIds = referrals.Where(x => x.StageId is not null).Select(x => x.StageId!.Value).ToArray();
            var stageNames = stageIds.Length == 0 ? new Dictionary<Guid, string>() : data.Find<FollowUpStage>(x => stageIds.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Name);
            var names = AccountGuard.UserNames(data, referrals.SelectMany(x => new[] { x.FromUserId, x.ToUserId }));
            return referrals.Where(x => cases.ContainsKey(x.CaseId))
                .Select(r => Referral(r, cases[r.CaseId], names, stageNames, Customer(data, cases[r.CaseId].CustomerId), userId, nowUtc)).ToList();
        });
    }
}
