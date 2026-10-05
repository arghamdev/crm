using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.FollowUps;
using Crm.Domain.Organization;
using static Crm.Application.Services.FollowUpSupport;
using P = Crm.Application.Services.FollowUpPermissions;

namespace Crm.Application.Services;

public interface IFollowUpSupervisionService
{
    FollowUpSupervisionDto Get(Guid userId, OrganizationSelection organization, FollowUpSupervisionQuery query, DateTimeOffset nowUtc);
    string ExportCsv(Guid userId, OrganizationSelection organization, FollowUpSupervisionQuery query, DateTimeOffset nowUtc);
    Guid SaveView(Guid userId, OrganizationSelection organization, string? name, FollowUpSupervisionQuery query);
    void DeleteView(Guid userId, OrganizationSelection organization, Guid viewId);
}

/// <summary>
/// ۱۲. نظارت مدیر: why each open case stopped and who owns its next step — cases without a next action, overdue work,
/// unanswered or rejected referrals, waits past their review date — plus SLA compliance, reopen rate, stage waits and workload.
/// </summary>
public sealed class FollowUpSupervisionService(ICrmDataStore store, IAccessSnapshotService access) : IFollowUpSupervisionService
{
    public const int AttentionLimit = 100;

    private AccessSnapshot Require(Guid userId, OrganizationSelection organization)
    {
        var snapshot = access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");
        if (!Supervises(snapshot, organization.CompanyId)) throw new UnauthorizedAccessException("FollowUp.Supervise permission is required.");
        return snapshot;
    }

    public static (DateTimeOffset From, string Label) PeriodStart(string? period, DateTimeOffset nowUtc) => period switch
    {
        "today" => (TehranTime.ToUtc(TehranTime.Date(nowUtc), "00:00", "امروز") ?? nowUtc.AddDays(-1), "امروز"),
        "week" => (nowUtc.AddDays(-7), "۷ روز اخیر"),
        "quarter" => (nowUtc.AddDays(-90), "۹۰ روز اخیر"),
        _ => (nowUtc.AddDays(-30), "۳۰ روز اخیر")
    };

    public FollowUpSupervisionDto Get(Guid userId, OrganizationSelection organization, FollowUpSupervisionQuery query, DateTimeOffset nowUtc)
    {
        var snapshot = Require(userId, organization);
        var request = Normalize(query);
        return store.Read(data => Build(data, snapshot, organization, userId, request, nowUtc));
    }

    private static FollowUpSupervisionQuery Normalize(FollowUpSupervisionQuery query) => new(
        string.IsNullOrWhiteSpace(query.BranchId) ? null : query.BranchId.Trim(), query.QueueId,
        query.Period is "today" or "week" or "quarter" ? query.Period : "month");

    private static FollowUpSupervisionDto Build(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid userId,
        FollowUpSupervisionQuery query, DateTimeOffset nowUtc)
    {
        var company = organization.CompanyId;
        var (from, _) = PeriodStart(query.Period, nowUtc);
        bool InView(FollowUpCase c) => AccountGuard.InContext(snapshot, organization, P.Read, c) &&
            (query.BranchId is null || AccountGuard.Same(c.BranchId, query.BranchId)) && (query.QueueId is null || c.QueueId == query.QueueId);

        var open = data.Find<FollowUpCase>(x => x.CompanyId == company && x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled)
            .Where(InView).ToList();
        var closed = data.Find<FollowUpCase>(x => x.CompanyId == company && x.Status == FollowUpStatus.Closed && x.ClosedAtUtc >= from).Where(InView).ToList();
        var ids = open.Select(x => x.Id).ToArray();
        var referrals = ids.Length == 0 ? [] : data.Find<FollowUpReferral>(x => ids.Contains(x.CaseId));
        var stages = ids.Length == 0 ? [] : data.Find<FollowUpStage>(x => ids.Contains(x.CaseId));
        var stageNames = stages.ToDictionary(x => x.Id, x => x.Name);
        var users = AccountGuard.UserNames(data, open.Select(x => x.OwnerUserId).Concat(open.Select(x => x.NextActionOwnerUserId))
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray());
        var customerIds = open.Select(c => c.CustomerId).Distinct().ToArray();
        var customers = data.Find<Crm.Domain.Customers.Customer>(x => customerIds.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Name);

        var attention = new List<FollowUpAttentionDto>();
        foreach (var c in open)
        {
            var owner = c.OwnerUserId is { } o ? users.GetValueOrDefault(o) : null;
            FollowUpAttentionDto Row(string reason, string tone, DateTimeOffset? due, int severity) =>
                new(c.Id, c.Code, customers.GetValueOrDefault(c.CustomerId, "—"), c.Subject, owner, reason, tone, due, severity);
            var caseReferrals = referrals.Where(x => x.CaseId == c.Id).OrderByDescending(x => x.SentAtUtc).ThenByDescending(x => x.CreatedAtUtc).ToList();
            var latest = caseReferrals.FirstOrDefault();
            if (c.OwnerUserId is null)
                attention.Add(Row("بدون مسئول — در انتظار تخصیص", "danger", c.FirstResponseDueAtUtc, c.Priority == FollowUpPriority.Critical ? 6 : 5));
            else if (c.NextActionAtUtc is null && !(c.IsWaiting && c.ReviewAtUtc is not null))
                attention.Add(Row("بدون اقدام بعدی", "danger", c.NearestDueUtc, 5));
            if (latest is { Status: ReferralStatus.Pending } pending && pending.IsLate(nowUtc))
                attention.Add(Row($"ارجاع پذیرفته نشده به {AccountGuard.UserNames(data, [pending.ToUserId]).GetValueOrDefault(pending.ToUserId, "—")}", "warning",
                    pending.AcceptDueAtUtc, 4));
            else if (latest is { Status: ReferralStatus.Rejected } rejected)
                attention.Add(Row($"ارجاع رد شد: {rejected.ResponseNote}", "warning", rejected.RespondedAtUtc, 3));
            if (c.IsWaiting && c.ReviewAtUtc < nowUtc)
                attention.Add(Row($"انتظار از موعد بازبینی گذشته ({c.WaitingOn ?? c.WaitReason ?? "—"})", "warning", c.ReviewAtUtc, 4));
            else if (c.IsOverdue(nowUtc))
                attention.Add(Row(c.NextActionAtUtc < nowUtc ? $"اقدام بعدی عقب‌افتاده: {c.NextAction}" : "مهلت پرونده گذشته", "danger", c.NearestDueUtc,
                    c.Priority >= FollowUpPriority.High ? 5 : 4));
            foreach (var stage in stages.Where(x => x.CaseId == c.Id && x.IsOverdue(nowUtc)))
                attention.Add(Row($"مرحلهٔ «{stage.Name}» عقب‌افتاده", "warning", stage.DueAtUtc, 3));
        }
        // One row per case: the most severe reason leads, the others follow it.
        var ordered = attention.GroupBy(x => x.Id).Select(g =>
            {
                var reasons = g.OrderByDescending(x => x.Severity).ThenBy(x => x.DueUtc ?? DateTimeOffset.MaxValue).ToList();
                var lead = reasons[0];
                return lead with { Reason = string.Join(" · ", reasons.Select(x => x.Reason).Distinct()) };
            })
            .OrderByDescending(x => x.Severity).ThenBy(x => x.DueUtc ?? DateTimeOffset.MaxValue).ThenBy(x => x.Code).ToList();

        // Average time in each stage (finished in the period, or still running) — where cases wait longest.
        var stageWindow = data.Find<FollowUpStage>(x => x.StartedAtUtc != null && (x.CompletedAtUtc == null || x.CompletedAtUtc >= from))
            .Where(x => x.Status != FollowUpStageStatus.Skipped).ToList();
        var stageCaseIds = stageWindow.Select(x => x.CaseId).Distinct().ToArray();
        var visibleCaseIds = data.Find<FollowUpCase>(x => stageCaseIds.Contains(x.Id)).Where(InView).Select(x => x.Id).ToHashSet();
        var waits = stageWindow.Where(x => visibleCaseIds.Contains(x.CaseId) && (x.CompletedAtUtc is not null || x.IsWorking))
            .GroupBy(x => x.Name)
            .Select(g => new FollowUpStageWaitDto(g.Key, Math.Round(g.Average(x => ((x.CompletedAtUtc ?? nowUtc) - x.StartedAtUtc!.Value).TotalHours), 1), g.Count()))
            .OrderByDescending(x => x.AverageHours).Take(6).ToList();

        var capacities = data.Find<FollowUpQueueMember>(x => true).GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Max(x => x.Capacity));
        var load = open.Where(x => x.OwnerUserId is not null).GroupBy(x => x.OwnerUserId!.Value)
            .Select(g => new FollowUpOwnerLoadDto(users.GetValueOrDefault(g.Key, "—"), g.Count(), g.Count(x => x.IsOverdue(nowUtc)),
                capacities.TryGetValue(g.Key, out var cap) ? cap : null))
            .OrderByDescending(x => x.Overdue).ThenByDescending(x => x.Open).ToList();

        var withDue = closed.Where(x => x.ResolutionDueAtUtc is not null).ToList();
        var slaMet = withDue.Count == 0 ? 100 : (int)Math.Round(100.0 * withDue.Count(x => x.ClosedAtUtc <= x.ResolutionDueAtUtc) / withDue.Count);
        var reopenRate = closed.Count == 0 ? 0 : (int)Math.Round(100.0 * closed.Count(x => x.ReopenCount > 0) / closed.Count);
        var branches = data.Find<OrganizationUnit>(x => x.CompanyId == company && x.Type == OrganizationUnitType.Branch)
            .Where(x => organization.BranchId is null || AccountGuard.Same(x.UnitId, organization.BranchId)).OrderBy(x => x.Name)
            .Select(x => new FollowUpOptionDto(x.UnitId, x.Name)).ToList();
        var queues = data.Find<FollowUpQueue>(x => x.CompanyId == company).OrderBy(x => x.RuleOrder).Select(x => new FollowUpOptionDto(x.Id.ToString(), x.Name)).ToList();
        var views = data.Find<FollowUpSavedView>(x => x.UserId == userId && x.CompanyId == company).OrderBy(x => x.Name).Select(x => (x.Id, x.Name, x.Query)).ToList();
        var latestReferrals = referrals.GroupBy(x => x.CaseId).Select(g => g.OrderByDescending(x => x.SentAtUtc).ThenByDescending(x => x.CreatedAtUtc).First()).ToList();
        return new FollowUpSupervisionDto(query, open.Count, open.Count(x => x.IsOverdue(nowUtc)),
            open.Count(x => x.NextActionAtUtc is null && !(x.IsWaiting && x.ReviewAtUtc is not null)),
            latestReferrals.Count(x => x.Status == ReferralStatus.Rejected), latestReferrals.Count(x => x.Status == ReferralStatus.Pending),
            closed.Count, slaMet, reopenRate, ordered.Take(AttentionLimit).ToList(), waits, load, branches, queues, views, nowUtc);
    }

    public string ExportCsv(Guid userId, OrganizationSelection organization, FollowUpSupervisionQuery query, DateTimeOffset nowUtc)
    {
        var dto = Get(userId, organization, query, nowUtc);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', new[] { "کد پرونده", "مشتری", "موضوع", "مسئول", "علت توجه", "سررسید" }.Select(ReportingService.CsvCell)));
        foreach (var row in dto.Attention)
            csv.AppendLine(string.Join(',', new[] { row.Code, row.CustomerName, row.Subject, row.Owner ?? "بدون مسئول", row.Reason,
                row.DueUtc is { } due ? TehranTime.Format(due) : "—" }.Select(ReportingService.CsvCell)));
        return csv.ToString();
    }

    public Guid SaveView(Guid userId, OrganizationSelection organization, string? name, FollowUpSupervisionQuery query)
    {
        Require(userId, organization);
        var normalized = Normalize(query);
        var text = $"branch={normalized.BranchId}&queue={normalized.QueueId}&period={normalized.Period}";
        return store.Write(data =>
        {
            var title = (name ?? string.Empty).Trim();
            var existing = data.Find<FollowUpSavedView>(x => x.UserId == userId && x.CompanyId == organization.CompanyId);
            if (existing.FirstOrDefault(x => x.Name == title) is { } same)
            {
                same.Update(title, text);
                return same.Id;
            }
            if (existing.Count >= 20) throw new InvalidOperationException("حداکثر ۲۰ نمای ذخیره‌شده مجاز است.");
            var view = new FollowUpSavedView(Guid.NewGuid(), userId, organization.CompanyId, title, text);
            data.Append(view);
            return view.Id;
        });
    }

    public void DeleteView(Guid userId, OrganizationSelection organization, Guid viewId)
    {
        Require(userId, organization);
        store.Write(data =>
        {
            var table = data.Table<FollowUpSavedView>();
            foreach (var view in data.Find<FollowUpSavedView>(x => x.Id == viewId && x.UserId == userId)) table.Remove(view);
            return true;
        });
    }
}
