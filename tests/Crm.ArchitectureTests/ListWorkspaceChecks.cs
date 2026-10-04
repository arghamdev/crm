using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Sales;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// List workspaces (فهرست سرنخ‌ها و حساب‌ها): views and counters agree with the rows, filters/sort/paging run in the
/// query, bulk actions check every row on its own, the import uses the normal create rules, and scope is never widened.
/// </summary>
internal static class ListWorkspaceChecks
{
    private static Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
    private static readonly Guid Sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Arya = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid LeadPaydar = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid LeadNovin = Guid.Parse("30000000-0000-4000-8000-000000000002");
    private static readonly Guid LeadRahkaran = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static readonly Guid LeadOtherCompany = Guid.Parse("30000000-0000-4000-8000-000000000004");

    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var sales = new SalesPipelineService(store, access);
        var activities = new AccountActivityService(store, access);
        var customers = new CustomerListService(store, access, activities);
        var manager = User(1); var expert = User(2); var finance = User(6);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;
        LeadListDto Leads(Guid user, LeadListQuery query) => sales.GetLeadListAsync(user, org, query, now).GetAwaiter().GetResult();
        CustomerListDto Accounts(Guid user, CustomerListQuery query) => customers.GetAsync(user, org, query, now).GetAwaiter().GetResult();

        // ── Leads: views, counters and filters ──
        var all = Leads(manager, new LeadListQuery());
        check(all.View == "all" && all.TotalCount == 3 && all.VisibleOpenCount == 3 && all.Items.All(x => x.CompanyId == "C01") && all.PageSize == 10,
            "LIST: the lead list shows the three open C01 leads (not the C02 one) with the default page size of 10.");
        var mine = Leads(manager, new LeadListQuery("mine"));
        check(mine.TotalCount == all.MineCount && mine.Items.All(x => x.OwnerUserId == manager) && mine.MineCount == 1,
            "LIST: «سرنخ‌های من» lists exactly the manager's own lead and matches its tab counter.");
        var overdue = Leads(manager, new LeadListQuery("overdue"));
        check(overdue.TotalCount == all.OverdueCount && overdue.Items.Any(x => x.Id == LeadNovin) &&
              overdue.Items.All(x => x.FirstContactAtUtc is null && x.FirstContactDueAtUtc < now || x.NextActionAtUtc < now),
            "LIST: «معوق» holds the lead whose first-contact SLA passed and agrees with its counter.");
        var noNext = Leads(manager, new LeadListQuery("nonext"));
        check(noNext.TotalCount == all.NoNextActionCount && noNext.Items.All(x => x.NextAction is null),
            "LIST: «بدون اقدام بعدی» lists only leads without a planned next action.");
        var needs = Leads(manager, new LeadListQuery("needs"));
        check(needs.TotalCount == all.NeedsActionCount && needs.Items.Any(x => x.Id == LeadNovin), "LIST: the «نیازمند اقدام» tile filters to its own count.");
        check(Leads(manager, new LeadListQuery("unknown")).View == "all", "LIST: an unknown view falls back to all leads.");
        check(Leads(manager, new LeadListQuery(Source: "نمایشگاه")).Items.Select(x => x.Id).SequenceEqual([LeadPaydar]) &&
              Leads(manager, new LeadListQuery(BranchId: "B03")).Items.All(x => x.BranchId == "B03") &&
              Leads(manager, new LeadListQuery(Status: LeadStatus.Qualified)).Items.Select(x => x.Id).SequenceEqual([LeadPaydar]) &&
              Leads(manager, new LeadListQuery(OwnerUserId: manager)).Items.Select(x => x.Id).SequenceEqual([LeadNovin]),
            "LIST: source, branch, status and owner filters narrow the lead rows.");
        var byScore = Leads(manager, new LeadListQuery(Sort: "score"));
        check(byScore.Sort == "score" && byScore.Items.Select(x => x.Score).SequenceEqual(byScore.Items.Select(x => x.Score).OrderByDescending(x => x)) &&
              Leads(manager, new LeadListQuery(Sort: "score-asc")).Items.First().Score == byScore.Items.Last().Score,
            "LIST: the score column sorts both ways.");
        var paged = Leads(manager, new LeadListQuery(PageSize: 1, Page: 2));
        check(paged.Items.Count == 1 && paged.TotalPages == 3 && paged.Page == 2 && Leads(manager, new LeadListQuery(PageSize: 1, Page: 99)).Page == 3,
            "LIST: lead paging is done in the query and clamps a too-large page to the last one.");
        check(all.Sources.Contains("وب‌سایت") && all.Owners.Any(x => x.Id == manager) && all.CanAssign && all.CanUpdate && all.CanCreate,
            "LIST: the lead filters offer the visible sources and owners; the manager may assign and update.");
        var expertList = Leads(expert, new LeadListQuery());
        check(expertList.Items.Select(x => x.Id).SequenceEqual([LeadPaydar]) && !expertList.CanAssign && expertList.Owners.All(x => x.Id == expert),
            "LIST: a sales expert sees only own leads, and the owner filter cannot reveal other owners.");

        // ── Duplicate suspicion: same name with only a contact person is allowed in, then flagged ──
        sales.CreateLead(manager, org, new CreateLeadCommand("تجارت نوین پارس", "رضا کاظمی", "تماس ورودی", "", "B03", manager), now);
        var duplicates = Leads(manager, new LeadListQuery("duplicates"));
        check(duplicates.TotalCount == 2 && duplicates.DuplicateCount == 2 && duplicates.Items.All(x => x.Name == "تجارت نوین پارس") &&
              duplicates.DuplicateIds.Contains(LeadNovin),
            "LIST: two open leads with the same name are both listed under «مشکوک به تکرار» and flagged on their rows.");

        // ── Bulk assign: each row is checked on its own; failures are reported, not hidden ──
        var b03Owner = sales.GetEligibleOwners(manager, org, "B03").First(x => x.UserId != manager).UserId;
        var assign = sales.BulkAssignLeads(manager, org, new BulkLeadAssignCommand([LeadNovin, LeadRahkaran, LeadOtherCompany], b03Owner, "ارجاع گروهی آزمون",
            now.AddHours(4)), now);
        check(assign.Succeeded == 2 && assign.Failures.Count == 1 &&
              Leads(manager, new LeadListQuery(OwnerUserId: b03Owner)).Items.Select(x => x.Id).Order().SequenceEqual(new[] { LeadNovin, LeadRahkaran }.Order()),
            "LIST: bulk assign moves the two in-scope leads and reports the other company's lead as a failure.");
        var expertAssignDenied = false;
        try { sales.BulkAssignLeads(expert, org, new BulkLeadAssignCommand([LeadPaydar], expert, "x", now.AddHours(4)), now); }
        catch (UnauthorizedAccessException) { expertAssignDenied = true; }
        check(expertAssignDenied, "LIST: bulk assign without Lead.Assign is refused by the service.");
        var emptyRejected = false;
        try { sales.BulkAssignLeads(manager, org, new BulkLeadAssignCommand([], b03Owner, "x", now.AddHours(4)), now); }
        catch (InvalidOperationException) { emptyRejected = true; }
        check(emptyRejected, "LIST: a bulk action with no selected rows is rejected.");

        // ── Bulk next action ──
        var at = now.AddDays(2);
        var plan = sales.BulkPlanLeadNextAction(manager, org, new BulkLeadNextActionCommand([LeadNovin, LeadPaydar], "ارسال پیشنهاد قیمت", at), now);
        var novin = sales.GetLead(manager, org, LeadNovin, now)!;
        check(plan.Succeeded == 2 && novin.Lead.NextAction == "ارسال پیشنهاد قیمت" && novin.Lead.NextActionAtUtc == at &&
              novin.History.Any(x => x.Reason.Contains("ارسال پیشنهاد قیمت")) && Leads(manager, new LeadListQuery("nonext")).Items.All(x => x.Id != LeadNovin),
            "LIST: «افزودن فعالیت» plans the next action on every selected lead, records it in the history and removes them from «بدون اقدام بعدی».");
        var pastRejected = false;
        try { sales.BulkPlanLeadNextAction(manager, org, new BulkLeadNextActionCommand([LeadNovin], "تماس", now.AddMinutes(-5)), now); }
        catch (InvalidOperationException) { pastRejected = true; }
        check(pastRejected, "LIST: a next action in the past is rejected.");

        // ── Import: the normal create rules per row, failures reported with the line number ──
        var before = Leads(manager, new LeadListQuery()).TotalCount;
        var import = sales.ImportLeads(manager, org, [
            new LeadImportRow(2, "صنایع آزمون واردات", "مینا شریفی", "02144556677", "import@test.example", "نمایشگاه"),
            new LeadImportRow(3, "", "بدون نام", "02100000000", null, null),
            new LeadImportRow(4, "سرنخ تکراری", "حسن", "09125550118", null, null)
        ], "B01", now);
        var imported = Leads(manager, new LeadListQuery(Query: "صنایع آزمون واردات"));
        check(import.Succeeded == 1 && import.Failures.Count == 2 && import.Failures.Any(x => x.StartsWith("ردیف 3")) && import.Failures.Any(x => x.StartsWith("ردیف 4")) &&
              Leads(manager, new LeadListQuery()).TotalCount == before + 1 && imported.Items.Single().Source == "نمایشگاه",
            "LIST: the import creates the valid row and reports the nameless and the duplicate-phone rows by line.");
        var importDenied = sales.ImportLeads(finance, org, [new LeadImportRow(2, "واردات مالی", "x", "02133333333", null, null)], "B01", now);
        check(importDenied.Succeeded == 0 && importDenied.Failures.Count == 1, "LIST: a user without Lead.Create imports nothing.");

        // ── Customers / accounts ──
        var accounts = Accounts(manager, new CustomerListQuery());
        check(accounts.TotalCount == accounts.VisibleCount && accounts.VisibleCount >= 4 && accounts.Items.Count <= 10 && accounts.CanCreate && accounts.CanPlanActivities,
            "LIST: the account list shows the visible accounts with matching counters.");
        var sepehr = accounts.Items.Single(x => x.Customer.Id == Sepehr);
        check(sepehr.NextActivity == "تماس پیگیری پرداخت فاکتور" && sepehr.NextActivityType == ActivityType.Call && sepehr.NextActivityAtUtc < now,
            "LIST: each account row shows its next planned activity (the overdue seeded call for Sepehr).");
        var followUp = Accounts(manager, new CustomerListQuery("followup"));
        check(followUp.TotalCount == accounts.FollowUpDueCount && followUp.Items.Any(x => x.Customer.Id == Sepehr),
            "LIST: «پیگیری امروز» lists accounts with a follow-up due today or overdue.");
        var incomplete = Accounts(manager, new CustomerListQuery("incomplete"));
        check(incomplete.TotalCount == accounts.IncompleteCount &&
              incomplete.Items.All(x => x.Customer.NationalId is null || x.Customer.PrimaryPhone is null || x.Customer.PrimaryEmail is null || x.Customer.NationalId.Contains('*') ||
                  x.Customer.PrimaryPhone.Contains('*')),
            "LIST: «نیازمند تکمیل» lists accounts missing an identifier, phone or email.");
        var duplicateAccounts = Accounts(manager, new CustomerListQuery("duplicates"));
        check(duplicateAccounts.TotalCount == accounts.DuplicateCount && duplicateAccounts.Items.All(x => x.PendingDuplicate),
            "LIST: «مشکوک به تکرار» lists accounts with a pending duplicate review.");
        var myAccounts = Accounts(manager, new CustomerListQuery("mine"));
        check(myAccounts.TotalCount == accounts.MineCount && myAccounts.Items.All(x => x.Customer.Owner == "مهدی نادری"),
            "LIST: «حساب‌های من» lists the accounts owned by the current user.");
        check(Accounts(manager, new CustomerListQuery(Kind: CustomerKind.Individual)).Items.All(x => x.Customer.Kind == CustomerKind.Individual) &&
              Accounts(manager, new CustomerListQuery(BranchId: "B01")).Items.All(x => x.Customer.BranchId == "B01") &&
              Accounts(manager, new CustomerListQuery(Query: "سپهر")).Items.Select(x => x.Customer.Id).SequenceEqual([Sepehr]),
            "LIST: kind, branch and text filters narrow the account rows.");
        var byName = Accounts(manager, new CustomerListQuery(Sort: "name"));
        check(byName.Items.Select(x => x.Customer.Name).SequenceEqual(byName.Items.Select(x => x.Customer.Name).OrderBy(x => x, StringComparer.Ordinal)),
            "LIST: accounts sort by name.");
        var expertAccounts = Accounts(expert, new CustomerListQuery());
        check(expertAccounts.Items.All(x => x.Customer.BranchId == "B01") && expertAccounts.VisibleCount < accounts.VisibleCount,
            "LIST: a branch expert's account list and counters stay inside the branch.");

        var noFollowUpBefore = accounts.NoFollowUpCount;
        var tasks = customers.PlanTasks(manager, org, new BulkAccountTaskCommand([Arya, Sepehr], "پیگیری تمدید قرارداد", TehranTime.Date(now.AddDays(3)), "11:00",
            ActivityPriority.High), now);
        var after = Accounts(manager, new CustomerListQuery());
        check(tasks.Succeeded == 2 && tasks.Failures.Count == 0 && after.NoFollowUpCount <= noFollowUpBefore &&
              activities.GetActivities(manager, org, Arya, new ActivityFilter(), now).Upcoming.Any(x => x.Subject == "پیگیری تمدید قرارداد"),
            "LIST: «ثبت وظیفه پیگیری» creates a task on every selected account through the activity rules.");
        var financeTasks = customers.PlanTasks(finance, org, new BulkAccountTaskCommand([Arya], "x", TehranTime.Date(now.AddDays(3)), "11:00", ActivityPriority.Normal), now);
        check(financeTasks.Succeeded == 0 && financeTasks.Failures.Count == 1, "LIST: a user without Activity.Create plans no task (reported per account).");
    }
}
