using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Service;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class ServiceDeskChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckSlaRules(check);
        CheckServiceScopeAndEscalation(check);
    }

    private static void CheckSlaRules(Action<bool, string> check)
    {
        var opened = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var customer = Guid.NewGuid();
        var owner = Guid.NewGuid();
        ServiceCase New(ServiceCasePriority priority) => new(Guid.NewGuid(), "CS-T", "موضوع", "", customer, "C01", "B01", null,
            ServiceCaseCategory.Complaint, ServiceCaseChannel.Phone, priority, owner, opened);

        var item = New(ServiceCasePriority.High);
        check(item.FirstResponseDueAtUtc == opened.AddHours(4) && item.ResolutionDueAtUtc == opened.AddHours(24),
            "SVC: High priority gets 4h response / 24h resolution targets.");
        check(item.FirstResponseSla(opened.AddHours(3.5)) == ServiceSlaState.AtRisk && item.FirstResponseSla(opened.AddHours(1)) == ServiceSlaState.OnTrack,
            "SVC: response SLA is at risk in the last quarter of its window.");
        Reject<InvalidOperationException>(() => item.StartWork(opened.AddMinutes(5)), "start work without an owner", check);

        item.Triage(ServiceCasePriority.Critical, owner, "مسئول", opened.AddMinutes(10));
        check(item.Status == ServiceCaseStatus.Triaged && item.ResolutionDueAtUtc == opened.AddHours(8),
            "SVC: raising priority re-baselines the SLA from the open time.");
        item.StartWork(opened.AddMinutes(30));
        check(item.FirstResponseSla(opened.AddHours(50)) == ServiceSlaState.Met, "SVC: a timely first response stays Met.");

        item.WaitOnCustomer(opened.AddHours(2));
        check(item.ResolutionSla(opened.AddHours(20)) == ServiceSlaState.Paused && item.RequiredEscalationLevel(opened.AddHours(20)) == 0,
            "SVC: a paused case shows Paused and never escalates.");
        item.StartWork(opened.AddHours(5));
        check(item.PausedMinutes == 180 && item.ResolutionDueAtUtc == opened.AddHours(11),
            "SVC: resuming adds the paused duration to the resolution due time.");

        check(item.RequiredEscalationLevel(opened.AddHours(12)) == 1 && item.RequiredEscalationLevel(opened.AddHours(16)) == 2,
            "SVC: escalation reaches level 1 on breach and level 2 after half the window again.");
        check(item.Escalate(opened.AddHours(12)) && !item.Escalate(opened.AddHours(13)) && item.EscalationLevel == 1,
            "SVC: escalation is idempotent within a level.");

        Reject<InvalidOperationException>(() => item.Resolve("", "اقدام", "راه‌حل", opened.AddHours(10)), "resolve without root cause", check);
        item.Resolve("علت", "اقدام", "راه‌حل", opened.AddHours(10));
        check(item.ResolutionSla(opened.AddHours(100)) == ServiceSlaState.Met, "SVC: resolution within the extended window is Met.");
        Reject<InvalidOperationException>(() => item.Close(6, null, opened.AddHours(11)), "CSAT outside 1..5", check);
        item.Close(4, "خوب", opened.AddHours(11));
        Reject<InvalidOperationException>(() => item.Reopen(opened.AddHours(11).AddDays(15)), "reopen after the 14-day window", check);
        item.Reopen(opened.AddDays(2));
        check(item.Status == ServiceCaseStatus.InProgress && item.ReopenCount == 1 && item.SatisfactionScore is null &&
              item.ResolutionDueAtUtc == opened.AddDays(2).AddHours(8),
            "SVC: reopen clears closure data and opens a fresh resolution window.");
    }

    private static void CheckServiceScopeAndEscalation(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var service = new ServiceCaseService(store, access);
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var expert = User(2); var finance = User(6); var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        var managerList = service.GetCases(manager, org, includeClosed: true, nowUtc: now);
        check(managerList.Items.Count == 4 && managerList.BreachedCount >= 1 && managerList.AverageSatisfaction == 4,
            "SVC: manager sees every seeded case with breach and CSAT metrics.");
        var executiveList = service.GetCases(executive, org, includeClosed: true, nowUtc: now);
        check(executiveList.Items.Count == 4 && !executiveList.CanTriage && !executiveList.CanCreate,
            "SVC: executive reads all cases company-wide but cannot triage or create.");
        var expertList = service.GetCases(expert, org, includeClosed: true, nowUtc: now);
        check(expertList.Items.All(x => x.BranchId == "B01" && x.OwnerUserId == expert), "SVC: expert sees only owned cases in their branch.");
        Reject<UnauthorizedAccessException>(() => service.GetCases(finance, org), "finance read", check);
        Reject<UnauthorizedAccessException>(() => service.GetCases(manager, org with { CompanyId = "C09" }), "foreign company", check);

        var created = service.CreateCase(expert, org, new CreateServiceCaseCommand(
            Guid.Parse("20000000-0000-4000-8000-000000000001"), "پرونده آزمون", null,
            ServiceCaseCategory.Warranty, ServiceCaseChannel.Email, ServiceCasePriority.Critical), now.AddHours(-30));
        check(created.Code.StartsWith("CS-") && created.Status == ServiceCaseStatus.New, "SVC: case is created in New status with a sequential code.");
        check(service.GetCases(expert, org, nowUtc: now).Items.Any(x => x.Id == created.Id), "SVC: creator can see the case before triage.");
        Reject<UnauthorizedAccessException>(() => service.CreateCase(expert, org, new CreateServiceCaseCommand(
            Guid.Parse("20000000-0000-4000-8000-000000000003"), "خارج از شعبه", null,
            ServiceCaseCategory.Complaint, ServiceCaseChannel.Phone, ServiceCasePriority.Low), now), "out-of-branch customer", check);
        Reject<UnauthorizedAccessException>(() => service.RunEscalation(expert, org, now), "expert escalation", check);

        var workItemsBefore = store.Read(d => d.WorkItems.Count);
        var result = service.EscalateBreaches(now);
        var second = service.EscalateBreaches(now);
        var escalated = store.Read(d => d.ServiceCases.Single(x => x.Id == created.Id));
        check(result.EscalatedCases >= 1 && result.WorkItemsCreated >= 1 && second.EscalatedCases == 0 &&
              escalated.EscalationLevel == 2 && store.Read(d => d.WorkItems.Count) == workItemsBefore + result.WorkItemsCreated,
            "SVC: system escalation raises the breached case once and adds work items to the queue.");
        check(store.Read(d => d.CustomerTimelineEvents.Any(x => x.SourceReference == created.Code)),
            "SVC: case creation is recorded on the Customer 360 timeline.");
    }

    private static void Reject<T>(Action action, string name, Action<bool, string> check) where T : Exception
    {
        try { action(); check(false, "SVC: " + name + " must be rejected."); }
        catch (T) { }
    }
}
