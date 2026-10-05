using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.FollowUps;
using Crm.Domain.Identity;
using Crm.Domain.Notifications;

namespace Crm.Application.Services;

public static class FollowUpPermissions
{
    public const string Read = "FollowUp.Read";
    public const string Create = "FollowUp.Create";
    public const string Update = "FollowUp.Update";
    public const string Assign = "FollowUp.Assign";
    public const string Approve = "FollowUp.Approve";
    public const string Close = "FollowUp.Close";
    public const string Reopen = "FollowUp.Reopen";
    public const string Supervise = "FollowUp.Supervise";
    public const string Configure = "FollowUp.Configure";
}

/// <summary>Rules shared by the follow-up services: who may see a case, SLA dates, progress and stage flow, assignment, history.</summary>
internal static class FollowUpSupport
{
    public static readonly FollowUpStatus[] ClosedStatuses = [FollowUpStatus.Closed, FollowUpStatus.Cancelled];

    public static string StatusLabel(FollowUpStatus status) => status switch
    {
        FollowUpStatus.AwaitingAssignment => "ثبت‌شده و در انتظار تخصیص",
        FollowUpStatus.InProgress => "در حال انجام",
        FollowUpStatus.WaitingCustomer => "در انتظار مشتری",
        FollowUpStatus.WaitingInternal => "در انتظار واحد داخلی / تأمین‌کننده",
        FollowUpStatus.OnHold => "متوقف",
        FollowUpStatus.ResolvedPendingApproval => "حل‌شده و در انتظار تأیید",
        FollowUpStatus.Closed => "بسته‌شده",
        _ => "لغوشده"
    };

    public static string PriorityLabel(FollowUpPriority priority) => priority switch
    {
        FollowUpPriority.Low => "کم", FollowUpPriority.Normal => "عادی", FollowUpPriority.High => "بالا", _ => "بحرانی (توقف فعالیت مشتری)"
    };

    public static bool Has(AccessSnapshot snapshot, string companyId, string permission) => snapshot.PermissionsFor(companyId).Contains(permission);

    public static bool Supervises(AccessSnapshot snapshot, string companyId) => Has(snapshot, companyId, FollowUpPermissions.Supervise);

    /// <summary>A user is involved when they own the case, created it, own its next action, run one of its stages or received a referral of it.</summary>
    public static bool Involved(CrmDataSet data, Guid userId, FollowUpCase c) =>
        c.OwnerUserId == userId || c.CreatedByUserId == userId || c.NextActionOwnerUserId == userId ||
        data.Find<FollowUpStage>(x => x.CaseId == c.Id && x.ResponsibleUserId == userId).Count > 0 ||
        data.Find<FollowUpReferral>(x => x.CaseId == c.Id && x.ToUserId == userId && x.Status != ReferralStatus.Cancelled).Count > 0 ||
        data.Find<FollowUpApproval>(x => x.CaseId == c.Id && x.ApproverUserId == userId).Count > 0;

    public static bool CanSee(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid userId, FollowUpCase c) =>
        AccountGuard.InContext(snapshot, organization, FollowUpPermissions.Read, c) && (Supervises(snapshot, c.CompanyId) || Involved(data, userId, c));

    /// <summary>Changing the case needs the permission in its branch and being involved in it (or supervising).</summary>
    public static bool CanAct(CrmDataSet data, AccessSnapshot snapshot, Guid userId, FollowUpCase c, string permission) =>
        Has(snapshot, c.CompanyId, permission) && snapshot.AllowsRecord(c.CompanyId, permission, c.BranchId, c.TerritoryId) &&
        (Supervises(snapshot, c.CompanyId) || Involved(data, userId, c));

    public static FollowUpCase Case(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid userId, Guid caseId, string? permission = null)
    {
        var c = data.Find<FollowUpCase>(x => x.Id == caseId).SingleOrDefault(x => CanSee(data, snapshot, organization, userId, x)) ??
            throw new KeyNotFoundException("پرونده پیگیری در دامنهٔ شما پیدا نشد.");
        if (permission is not null && !CanAct(data, snapshot, userId, c, permission))
            throw new UnauthorizedAccessException($"{permission} permission is required for this case.");
        return c;
    }

    public static void Log(CrmDataSet data, FollowUpCase c, string kind, string title, string? detail, Guid? actorUserId, DateTimeOffset nowUtc) =>
        data.Append(new FollowUpEvent(Guid.NewGuid(), c.Id, c.CompanyId, kind, title.Length <= 300 ? title : title[..300],
            detail is { Length: > 2000 } ? detail[..2000] : detail, actorUserId, nowUtc));

    /// <summary>Active internal users with a role on the company or on this branch/territory.</summary>
    public static List<(Guid Id, string Name)> BranchUsers(CrmDataSet data, string companyId, string? branchId, string? territoryId, DateTimeOffset nowUtc)
    {
        var ids = data.Find<UserRoleAssignment>(x => x.CompanyId == companyId && x.RoleKey != "DealerUser").Where(x => x.IsEffective(nowUtc) &&
            (AccountGuard.Same(x.ScopeType, "Company") || branchId is null || AccountGuard.Same(x.ScopeType, "Branch") && AccountGuard.Same(x.ScopeId, branchId) ||
             AccountGuard.Same(x.ScopeType, "Territory") && AccountGuard.Same(x.ScopeId, territoryId))).Select(x => x.CrmUserId).Distinct().ToArray();
        return data.Find<CrmUser>(x => ids.Contains(x.Id)).Where(x => x.IsActiveAt(nowUtc)).OrderBy(x => x.DisplayName).Select(x => (x.Id, x.DisplayName)).ToList();
    }

    public static void RequireBranchUser(CrmDataSet data, FollowUpCase c, Guid userId, DateTimeOffset nowUtc, string label)
    {
        if (!BranchUsers(data, c.CompanyId, c.BranchId, c.TerritoryId, nowUtc).Any(x => x.Id == userId))
            throw new InvalidOperationException($"{label} باید کاربر فعال در دامنهٔ شعبهٔ پرونده باشد.");
    }

    // ───────────── SLA ─────────────

    public static SlaPolicy? Policy(CrmDataSet data, string companyId, Guid? policyId, FollowUpPriority priority, string caseType)
    {
        if (policyId is { } id && data.Find<SlaPolicy>(x => x.Id == id && x.IsActive).SingleOrDefault() is { } fixedPolicy && fixedPolicy.Priority == priority)
            return fixedPolicy;
        var policies = data.Find<SlaPolicy>(x => x.CompanyId == companyId && x.IsActive);
        return policies.FirstOrDefault(x => x.Priority == priority && AccountGuard.Same(x.CaseType, caseType))
               ?? policies.FirstOrDefault(x => x.Priority == priority && x.CaseType is null)
               ?? (policyId is { } any ? policies.FirstOrDefault(x => x.Id == any) : null)
               ?? policies.FirstOrDefault(x => x.Priority == FollowUpPriority.Normal);
    }

    /// <summary>Without a policy: 4 calendar hours to the first response and 3 days to resolve.</summary>
    public static (DateTimeOffset FirstResponse, DateTimeOffset Resolution) Dues(SlaPolicy? policy, DateTimeOffset nowUtc)
    {
        if (policy is null) return (nowUtc.AddHours(4), nowUtc.AddDays(3));
        var calendar = policy.Calendar();
        return (calendar.AddWorkingMinutes(nowUtc, policy.FirstResponseHours * 60), calendar.AddWorkingMinutes(nowUtc, policy.ResolutionHours * 60));
    }

    public static DateTimeOffset StageDue(SlaPolicy? policy, int? stageHours, DateTimeOffset nowUtc) => policy is null
        ? nowUtc.AddHours(stageHours ?? 24)
        : policy.Calendar().AddWorkingMinutes(nowUtc, (stageHours ?? policy.StageHours) * 60);

    public static WorkCalendar Calendar(SlaPolicy? policy) => policy?.Calendar() ??
        new WorkCalendar(Enum.GetValues<DayOfWeek>(), TimeOnly.MinValue, new TimeOnly(23, 59), WorkCalendar.ResolveZone("Asia/Tehran"));

    // ───────────── Stages and progress ─────────────

    public static List<FollowUpStage> Stages(CrmDataSet data, Guid caseId) => data.Find<FollowUpStage>(x => x.CaseId == caseId).OrderBy(x => x.Order).ToList();

    /// <summary>Stages run in groups: a stage marked «parallel» runs together with the one before it; the next group starts when the previous is finished.</summary>
    public static List<List<FollowUpStage>> Groups(IEnumerable<FollowUpStage> stages)
    {
        var groups = new List<List<FollowUpStage>>();
        foreach (var stage in stages.OrderBy(x => x.Order))
        {
            if (groups.Count == 0 || !stage.ParallelWithPrevious) groups.Add([]);
            groups[^1].Add(stage);
        }
        return groups;
    }

    /// <summary>
    /// Recomputes stage and case progress from the checklists, starts the next stage group when the current one is finished,
    /// and moves the case to «حل‌شده و در انتظار تأیید» when every stage that applies is done. Progress changes are logged.
    /// </summary>
    public static void Recompute(CrmDataSet data, FollowUpCase c, Guid? actorUserId, DateTimeOffset nowUtc, string? reason = null,
        List<FollowUpStage>? created = null, List<FollowUpChecklistItem>? createdItems = null)
    {
        // Rows appended in this same write are not visible to a database query yet, so a new case passes them in.
        var stages = created ?? Stages(data, c.Id);
        var items = createdItems ?? data.Find<FollowUpChecklistItem>(x => x.CaseId == c.Id);
        var policy = c.SlaPolicyId is { } id ? data.Find<SlaPolicy>(x => x.Id == id).SingleOrDefault() : null;
        if (c.IsOpen)
            foreach (var group in Groups(stages))
            {
                if (group.All(x => x.Status is FollowUpStageStatus.Done or FollowUpStageStatus.Skipped)) continue;
                foreach (var stage in group.Where(x => x.Status == FollowUpStageStatus.Pending))
                {
                    stage.Activate(c.OwnerUserId, StageDue(policy, stage.DurationHours, nowUtc), nowUtc);
                    Log(data, c, "Stage", $"مرحلهٔ «{stage.Name}» شروع شد", null, actorUserId, nowUtc);
                }
                break;
            }
        foreach (var stage in stages)
        {
            var list = items.Where(x => x.StageId == stage.Id).ToList();
            stage.SetProgress(FollowUpProgress.StageProgress(stage.Status, list.Count(x => x.IsDone), list.Count, stage.Progress));
        }
        var before = c.ProgressPercent;
        var after = FollowUpProgress.Total(stages.Select(x => (x.Weight, x.Status, x.Progress)));
        if (after != before)
        {
            c.SetProgress(after);
            Log(data, c, "Progress", $"پیشرفت پرونده: {before}٪ → {after}٪", reason, actorUserId, nowUtc);
        }
        if (c.IsOpen && !c.IsWaiting && stages.Count > 0 && stages.All(x => x.Status is FollowUpStageStatus.Done or FollowUpStageStatus.Skipped) &&
            c.Status != FollowUpStatus.ResolvedPendingApproval)
        {
            c.MarkResolvedPendingApproval();
            Log(data, c, "Status", "همهٔ مراحل انجام شد؛ پرونده حل‌شده و در انتظار تأیید/بستن است", null, actorUserId, nowUtc);
            if (c.OwnerUserId is { } owner) c.PlanNextAction("بستن پرونده و ثبت نتیجهٔ نهایی", nowUtc.AddHours(4), owner, nowUtc);
        }
        else if (c.Status == FollowUpStatus.ResolvedPendingApproval && stages.Any(x => x.IsWorking)) c.BackToWork();
    }

    /// <summary>Creates the case stages and checklists from the template version the case uses.</summary>
    public static (List<FollowUpStage> Stages, List<FollowUpChecklistItem> Items) Instantiate(CrmDataSet data, FollowUpCase c,
        IReadOnlyList<FollowUpTemplateStage> templateStages)
    {
        var stages = new List<FollowUpStage>();
        var items = new List<FollowUpChecklistItem>();
        foreach (var t in templateStages.OrderBy(x => x.Order))
        {
            var stage = new FollowUpStage(Guid.NewGuid(), c.Id, t.Order, t.Name, t.Weight, t.Required, t.ResponsibleRole, t.ParallelWithPrevious, t.Condition, t.DurationHours);
            data.Append(stage);
            stages.Add(stage);
            var order = 0;
            foreach (var title in t.ChecklistItems())
            {
                var item = new FollowUpChecklistItem(Guid.NewGuid(), c.Id, stage.Id, ++order, title);
                data.Append(item);
                items.Add(item);
            }
        }
        return (stages, items);
    }

    // ───────────── Assignment ─────────────

    public static Dictionary<Guid, int> Loads(CrmDataSet data, string companyId) =>
        data.Find<FollowUpCase>(x => x.CompanyId == companyId && x.OwnerUserId != null && x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled)
            .GroupBy(x => x.OwnerUserId!.Value).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Picks the queue whose entry conditions match and, inside it, an available member with free capacity
    /// (most free capacity, or round robin). When nobody qualifies the overflow queue is tried once.
    /// </summary>
    public static AssignmentSuggestionDto Suggest(CrmDataSet data, string companyId, string branchId, string caseType, string? partFamily, string? language,
        DateTimeOffset nowUtc, Guid? queueId = null)
    {
        var queues = data.Find<FollowUpQueue>(x => x.CompanyId == companyId && x.IsActive).OrderBy(x => x.RuleOrder).ThenBy(x => x.Name).ToList();
        var queue = queueId is { } q ? queues.FirstOrDefault(x => x.Id == q) : queues.FirstOrDefault(x => x.Matches(branchId, caseType, partFamily, language));
        if (queue is null) return new AssignmentSuggestionDto(null, null, null, null, "هیچ صفی با شرایط این پرونده تعریف نشده است؛ مسئول را دستی انتخاب کنید.", [], false);
        var loads = Loads(data, companyId);
        var (pick, candidates) = Pick(data, queue, loads, nowUtc);
        if (pick is not null)
            return new AssignmentSuggestionDto(queue.Id, queue.Name, pick.UserId, pick.Name,
                queue.Method == AssignmentMethod.MostFreeCapacity ? $"بیشترین ظرفیت آزاد در صف «{queue.Name}» ({pick.Load} از {pick.Capacity})" : $"نوبت چرخشی در صف «{queue.Name}»",
                candidates, false);
        if (queue.OverflowQueueId is { } overflowId && queues.FirstOrDefault(x => x.Id == overflowId) is { } overflow)
        {
            var (backup, backupCandidates) = Pick(data, overflow, loads, nowUtc);
            if (backup is not null)
                return new AssignmentSuggestionDto(overflow.Id, overflow.Name, backup.UserId, backup.Name,
                    $"ظرفیت صف «{queue.Name}» پر است؛ از صف پشتیبان «{overflow.Name}» تخصیص داده شد.", candidates.Concat(backupCandidates).ToList(), true);
        }
        return new AssignmentSuggestionDto(queue.Id, queue.Name, null, null,
            $"در صف «{queue.Name}» فرد واجد شرایطی آزاد نیست؛ پرونده در انتظار تخصیص می‌ماند و سرپرست مطلع می‌شود.", candidates, true);
    }

    private static (AssignmentCandidateDto? Pick, List<AssignmentCandidateDto> Candidates) Pick(CrmDataSet data, FollowUpQueue queue, Dictionary<Guid, int> loads,
        DateTimeOffset nowUtc)
    {
        var members = data.Find<FollowUpQueueMember>(x => x.QueueId == queue.Id);
        var ids = members.Select(x => x.UserId).ToArray();
        var users = data.Find<CrmUser>(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var candidates = members.Select(m =>
        {
            var active = users.TryGetValue(m.UserId, out var u) && u.IsActiveAt(nowUtc);
            var load = loads.GetValueOrDefault(m.UserId);
            return (Member: m, Dto: new AssignmentCandidateDto(m.UserId, users.TryGetValue(m.UserId, out var user) ? user.DisplayName : "—", m.Capacity, load,
                m.IsAvailable && active, m.AvailabilityNote, m.IsAvailable && active && load < m.Capacity));
        }).OrderBy(x => x.Dto.Name).ToList();
        var eligible = candidates.Where(x => x.Dto.Eligible).ToList();
        var pick = queue.Method == AssignmentMethod.RoundRobin
            ? eligible.OrderBy(x => x.Member.LastAssignedAtUtc ?? DateTimeOffset.MinValue).ThenBy(x => x.Dto.Name).FirstOrDefault()
            : eligible.OrderByDescending(x => x.Dto.Capacity - x.Dto.Load).ThenBy(x => x.Member.LastAssignedAtUtc ?? DateTimeOffset.MinValue).ThenBy(x => x.Dto.Name).FirstOrDefault();
        return (pick.Dto, candidates.Select(x => x.Dto).ToList());
    }

    /// <summary>Running stages that the previous owner was responsible for follow the case to its new owner.</summary>
    public static void HandOverStages(CrmDataSet data, FollowUpCase c, Guid? previousOwner, Guid newOwner)
    {
        if (previousOwner is not { } previous || previous == newOwner) return;
        foreach (var stage in data.Find<FollowUpStage>(x => x.CaseId == c.Id && x.ResponsibleUserId == previous).Where(x => x.IsWorking || x.Status == FollowUpStageStatus.Pending))
            stage.AssignResponsible(newOwner);
    }

    public static void MarkAssigned(CrmDataSet data, Guid queueId, Guid userId, DateTimeOffset nowUtc)
    {
        foreach (var member in data.Find<FollowUpQueueMember>(x => x.QueueId == queueId && x.UserId == userId)) member.MarkAssigned(nowUtc);
    }

    // ───────────── Notifications ─────────────

    public static int Notify(CrmDataSet data, FollowUpCase c, IEnumerable<Guid> users, string subject, string body, string dedupKey, DateTimeOffset nowUtc) =>
        users.Where(x => x != Guid.Empty).Distinct()
            .Sum(user => NotificationOutbox.Enqueue(data, c.CompanyId, user, NotificationCategory.FollowUp, subject, body, $"/follow-ups/{c.Id}", dedupKey, nowUtc));

    public static IReadOnlyList<Guid> Supervisors(CrmDataSet data, FollowUpCase c, DateTimeOffset nowUtc) =>
        NotificationOutbox.UsersWithPermission(data, c.CompanyId, FollowUpPermissions.Supervise, nowUtc);

    public static IReadOnlyList<Guid> Managers(CrmDataSet data, FollowUpCase c, DateTimeOffset nowUtc) =>
        NotificationOutbox.UsersWithPermission(data, c.CompanyId, FollowUpPermissions.Configure, nowUtc);

    public static string Customer(CrmDataSet data, Guid customerId) =>
        data.Find<Crm.Domain.Customers.Customer>(x => x.Id == customerId).Select(x => x.Name).FirstOrDefault() ?? "—";

    public static DateTimeOffset RequiredTime(string? date, string? time, string label, DateTimeOffset nowUtc, bool future = true)
    {
        var at = TehranTime.ToUtc(date, string.IsNullOrWhiteSpace(time) ? "09:00" : time, label) ?? throw new InvalidOperationException($"{label} الزامی است.");
        if (future && at <= nowUtc) throw new InvalidOperationException($"{label} باید در آینده باشد.");
        return at;
    }
}
