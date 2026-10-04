using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Sales;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The account file «پیگیری» panel: planned activities and the next steps of open opportunities/leads in one list,
/// grouped by due time, without other users' sales records for a non-manager and without completed/closed items.
/// </summary>
internal static class FollowUpChecks
{
    private static Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
    private static readonly Guid Sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");

    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var activities = new AccountActivityService(store, access);
        var sales = new SalesPipelineService(store, access);
        var manager = User(1); var expert = User(2); var finance = User(6);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        var panel = activities.GetFollowUps(manager, org, Sepehr, now);
        var all = panel.Overdue.Concat(panel.Today).Concat(panel.Later).Concat(panel.Undated).ToList();
        check(panel.Overdue.Any(x => x.Title == "تماس پیگیری پرداخت فاکتور" && x.ActivityId is not null && x.CanComplete && x.IsOverdue),
            "FOLLOW: the overdue seeded call is listed under «معوق» and can be completed from the panel.");
        check(panel.Overdue.All(x => x.DueAtUtc < now) && panel.Today.Concat(panel.Later).All(x => x.DueAtUtc >= now) &&
              panel.Later.Select(x => x.DueAtUtc).SequenceEqual(panel.Later.Select(x => x.DueAtUtc).Order()),
            "FOLLOW: follow-ups are grouped by due time and ordered within each group.");
        check(all.Any(x => x.Source == "فرصت فروش" && x.Url!.StartsWith("/opportunities/")) && panel.CanPlan,
            "FOLLOW: open opportunities of the account contribute their next step; the manager may plan a new follow-up.");

        // A lead raised from the account appears with its next step; once converted it leaves the panel.
        var lead = sales.CreateLead(manager, org, new CreateLeadCommand("نیاز جدید سپهر", "علی رستگار", "مشتری فعلی", null, "B01", manager,
            Phone: "02100001111", CustomerId: Sepehr, NextAction: "ارسال کاتالوگ", NextActionAtUtc: now.AddDays(2)), now);
        var withLead = activities.GetFollowUps(manager, org, Sepehr, now);
        check(withLead.Overdue.Concat(withLead.Today).Concat(withLead.Later).Any(x => x.Source == "سرنخ" && x.Url == $"/leads/{lead.Id}"),
            "FOLLOW: an open lead of the account shows its next step.");
        var expertPanel = activities.GetFollowUps(expert, org, Sepehr, now);
        check(expertPanel.Overdue.Concat(expertPanel.Today).Concat(expertPanel.Later).Concat(expertPanel.Undated).All(x => x.Source != "سرنخ"),
            "FOLLOW: a sales expert does not see another owner's lead in the panel.");
        var financePanel = activities.GetFollowUps(finance, org, Sepehr, now);
        check(!financePanel.CanPlan, "FOLLOW: a user without Activity.Create cannot plan a follow-up from the panel.");
    }
}
