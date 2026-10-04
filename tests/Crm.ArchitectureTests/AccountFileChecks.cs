using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Sales;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Reporting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Scenarios of the account file (پرونده یکپارچه حساب): quick actions persist, outcome + next action, link/unlink without
/// deletion, totals per currency from approved payments only, double-submit protection, scope and permission isolation.
/// </summary>
internal static class AccountFileChecks
{
    private static Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
    private static readonly Guid Sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Arya = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Nakhl = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid Mahan = Guid.Parse("20000000-0000-4000-8000-000000000004");
    private static readonly Guid Rostegar = Guid.Parse("21000000-0000-4000-8000-000000000001");

    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var sales = new SalesPipelineService(store, access);
        var notes = new AccountNoteService(store, access);
        var activities = new AccountActivityService(store, access);
        var records = new AccountRecordService(store, access, sales, notes);
        var files = new AccountFileService(store, access, new DemoReportingFinanceSource());
        var customers = new Customer360Service(store, access);
        var manager = User(1); var expert = User(2); var supervisor = User(3); var finance = User(6); var dealer = User(8); var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;
        string Day(int offset) => TehranTime.Date(now.AddDays(offset));
        AccountRecordCommand Cmd(Guid op, params (string Key, string? Value)[] fields) =>
            new(fields.ToDictionary(x => x.Key, x => x.Value), op);

        // ── File, KPIs and sections ──
        var file = files.GetFile(manager, org, Sepehr, now);
        check(file.Quick is { Opportunity: true, Call: true, Meeting: true, Task: true, Note: true, Lead: true } && file.CanChangeStatus,
            "ACC: the sales manager gets all six quick actions (incl. ایجاد سرنخ) and the status action.");
        check(file.Kpis.Any(x => x.Key == "overdueActivities" && x.Value == "1") && file.Kpis.All(x => !string.IsNullOrWhiteSpace(x.Definition)),
            "ACC: KPIs carry definitions; the seeded overdue call is counted.");
        check(file.Sections.Any(x => x.Key == "accessGroups") && !files.GetFile(expert, org, Sepehr, now).Sections.Any(x => x.Key == "accessGroups"),
            "ACC: access groups are listed only for administrators.");
        var expertFile = files.GetFile(expert, org, Sepehr, now);
        check(expertFile.Quick.Opportunity && expertFile.Sections.Single(x => x.Key == "payments").CanCreate && !expertFile.CanChangeStatus,
            "ACC: a sales expert may register payments but cannot change the account status.");

        // ── Quick actions (account preselected) persist and are visible after a fresh read ──
        var oppId = records.Create(expert, org, Sepehr, "opportunities", Cmd(Guid.NewGuid(), ("title", "خط بسته‌بندی دوم"), ("contactId", Rostegar.ToString()),
            ("stage", "Qualified"), ("value", "2,500,000"), ("currency", "USD"), ("probability", "45"), ("expectedClose", Day(40)), ("ownerUserId", expert.ToString()),
            ("nextAction", "ارسال پروپوزال فنی"), ("nextActionDate", Day(3)), ("nextActionTime", "11:00")), now);
        var opportunity = store.Read(d => d.Opportunities.Single(x => x.Id == oppId));
        check(opportunity is { CustomerId: var c, Stage: OpportunityStage.Qualified, CurrencyCode: "USD", Probability: 45, Value: 2_500_000m, NextAction: "ارسال پروپوزال فنی" } &&
              c == Sepehr && opportunity.ContactId == Rostegar && opportunity.BranchId == "B01",
            "ACC: quick opportunity keeps account, branch, contact, stage, amount+currency, probability and next action.");
        var opOp = Guid.NewGuid();
        var lead1 = records.Create(manager, org, Sepehr, "leads", Cmd(opOp, ("name", "نیاز خط تولید کنسرو ماهی"), ("contactId", Rostegar.ToString()), ("source", "مشتری فعلی"),
            ("ownerUserId", expert.ToString())), now);
        var lead2 = records.Create(manager, org, Sepehr, "leads", Cmd(opOp, ("name", "نیاز خط تولید کنسرو ماهی"), ("contactId", Rostegar.ToString()), ("source", "مشتری فعلی"),
            ("ownerUserId", expert.ToString())), now);
        check(lead1 == lead2 && store.Read(d => d.Leads.Count(x => x.Name == "نیاز خط تولید کنسرو ماهی")) == 1 &&
              store.Read(d => d.Leads.Single(x => x.Id == lead1)).CustomerId == Sepehr,
            "ACC: «ایجاد سرنخ» links the lead to the account and a double submit creates it once.");

        SaveActivityCommand Activity(ActivityType type, string subject, Guid op, int dayOffset = 1, string time = "10:00") => new(type, subject, null,
            type == ActivityType.Meeting ? null : Rostegar, expert, Day(dayOffset), time, type == ActivityType.Meeting ? Day(dayOffset) : null,
            type == ActivityType.Meeting ? "11:00" : null, type == ActivityType.Call ? 15 : null, type == ActivityType.Call ? CallDirection.Outbound : null,
            type == ActivityType.Meeting ? "https://meet.example/abc" : null, ActivityPriority.High, type == ActivityType.Task ? null : 30,
            type == ActivityType.Task ? ActivityRelatedKind.Opportunity : ActivityRelatedKind.None, type == ActivityType.Task ? oppId : null,
            type == ActivityType.Meeting ? [Rostegar] : null, type == ActivityType.Meeting ? [manager] : null, op);
        var callOp = Guid.NewGuid();
        var call = activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Call, "تماس معرفی خط دوم", callOp), now);
        var again = activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Call, "تماس معرفی خط دوم", callOp), now);
        var meeting = activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Meeting, "جلسهٔ فنی خط دوم", Guid.NewGuid(), 2), now);
        var task = activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Task, "تهیهٔ نقشهٔ استقرار", Guid.NewGuid(), 4), now);
        check(call.Id == again.Id && store.Read(d => d.CrmActivities.Count(x => x.Subject == "تماس معرفی خط دوم")) == 1,
            "ACC: double-clicking «ذخیره» on a call creates one activity.");
        var fresh = new AccountActivityService(store, access).GetActivities(expert, org, Sepehr, new ActivityFilter(), now);
        check(new[] { call.Id, meeting.Id, task.Id }.All(id => fresh.Upcoming.Any(x => x.Id == id)) &&
              fresh.Upcoming.Single(x => x.Id == task.Id).Related?.Contains(opportunity.Code) == true &&
              store.Read(d => d.ActivityParticipants.Count(x => x.ActivityId == meeting.Id)) == 2,
            "ACC: call, meeting (with participants) and task (linked to the opportunity) are listed after a fresh read.");
        check(store.Read(d => d.NotificationMessages.Any(x => x.DedupKey.StartsWith($"activity-reminder:{call.Id:N}:") && x.Status == Crm.Domain.Notifications.NotificationStatus.Pending)),
            "ACC: a call reminder is queued in the notification outbox for its reminder time.");
        Reject(() => activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Call, "خارج از دامنه", Guid.NewGuid()) with { ContactId = Guid.Parse("21000000-0000-4000-8000-000000000003") }, now),
            "a contact of another account", check);
        Reject(() => activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Call, "واگذاری", Guid.NewGuid()) with { OwnerUserId = manager }, now),
            "assigning to another user without Activity.Update", check);

        var note = notes.SaveNote(expert, org, Sepehr, null, new SaveNoteCommand("جمع‌بندی بازدید", "خط دوم تا پایان سال نیاز است.", NoteVisibility.Private, Guid.NewGuid()),
            [new FileUpload("visit.txt", Encoding.UTF8.GetBytes("visit report"))], now);
        check(notes.GetNotes(expert, org, Sepehr).Any(x => x.Id == note.Id && x.Attachments.Count == 1) && notes.GetNotes(manager, org, Sepehr).All(x => x.Id != note.Id),
            "ACC: a private note (with attachment) is visible to its author only.");
        var restricted = notes.SaveNote(expert, org, Sepehr, null, new SaveNoteCommand("محدود", "برای مدیران", NoteVisibility.Restricted, Guid.NewGuid()), [], now);
        check(notes.GetNotes(manager, org, Sepehr).Any(x => x.Id == restricted.Id), "ACC: a restricted note is visible to managers.");
        Reject(() => notes.Download(manager, org, Sepehr, store.Read(d => d.CrmDocuments.Single(x => x.NoteId == note.Id).Id)), "downloading another user's private-note attachment", check);

        // ── Outcome is separate from planning; completing records the outcome and the next action ──
        Reject(() => activities.Complete(expert, org, Sepehr, call.Id, new CompleteActivityCommand(null, CallResult.Answered, 10, null, null, null, null, call.Version), now),
            "completing a call without an outcome", check);
        Reject(() => activities.Complete(expert, org, Sepehr, call.Id, new CompleteActivityCommand("ok", null, 10, null, null, null, null, call.Version), now),
            "completing a call without its answer status", check);
        var (completed, followUp) = activities.Complete(expert, org, Sepehr, call.Id, new CompleteActivityCommand("مشتری برای جلسهٔ فنی آماده است.", CallResult.Answered, 12,
            ActivityType.Meeting, "جلسهٔ پیگیری", Day(6), "14:00", call.Version), now);
        check(completed.State == "Completed" && completed.Outcome is not null && followUp is { State: "Planned" } &&
              store.Read(d => d.CrmActivities.Single(x => x.Id == followUp.Id)).FollowUpOfId == call.Id &&
              store.Read(d => d.NotificationMessages.Where(x => x.DedupKey.StartsWith($"activity-reminder:{call.Id:N}:")).All(x => x.Status == Crm.Domain.Notifications.NotificationStatus.Withdrawn)),
            "ACC: completing a call stores outcome + result, withdraws its reminder and plans the next action.");
        var meetingNow = store.Read(d => d.CrmActivities.Single(x => x.Id == meeting.Id));
        Reject(() => activities.Cancel(expert, org, Sepehr, meeting.Id, new CancelActivityCommand("", meetingNow.Version), now), "cancelling without a reason", check);
        var moved = activities.Reschedule(expert, org, Sepehr, meeting.Id, new RescheduleActivityCommand(Day(8), "09:30", null, "10:30", meetingNow.Version), now);
        check(moved.When.StartsWith(Day(8)) && store.Read(d => d.CrmActivities.Single(x => x.Id == meeting.Id)).EndAtUtc is { } end &&
              TehranTime.Clock(end) == "10:30", "ACC: rescheduling a meeting moves start and end.");
        Reject(() => activities.Reschedule(expert, org, Sepehr, meeting.Id, new RescheduleActivityCommand(Day(9), "09:30", null, null, meetingNow.Version), now),
            "a stale version on reschedule", check);
        var logged = activities.SaveActivity(expert, org, Sepehr, null, Activity(ActivityType.Call, "تماس ورودی مشتری", Guid.NewGuid(), -1) with
            { AlreadyDone = true, Outcome = "درخواست کاتالوگ", CallResult = CallResult.Answered }, now);
        check(logged.State == "Completed", "ACC: a call that already happened is logged as completed with its outcome.");
        var overdueOnly = activities.GetActivities(expert, org, Sepehr, new ActivityFilter(State: "overdue"), now);
        var callsOnly = activities.GetActivities(expert, org, Sepehr, new ActivityFilter(ActivityType.Call), now);
        check(overdueOnly.Total == overdueOnly.Overdue.Count && overdueOnly.Total >= 1 &&
              callsOnly.Overdue.Concat(callsOnly.Upcoming).Concat(callsOnly.Past).All(x => x.Type == ActivityType.Call),
            "ACC: activity filters (state, type) return only matching items.");
        var timeline = activities.GetTimeline(expert, org, Sepehr, now);
        check(timeline.Count(x => x.Title.Contains("تماس معرفی خط دوم")) == 1, "ACC: the interaction timeline lists a completed activity once.");

        // ── Payments: statuses, separation of duties, totals per currency from approved only ──
        var paymentId = records.Create(expert, org, Sepehr, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "BankTransfer"), ("amount", "500,000,000"),
            ("currency", "IRR"), ("paidOn", Day(0)), ("reference", "TRX-1")), now);
        Reject(() => records.Act(expert, org, Sepehr, "payments", paymentId, "approve", Cmd(Guid.Empty), now), "an expert approving a payment", check);
        var managerPayment = records.Create(manager, org, Sepehr, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cash"), ("amount", "1000"),
            ("currency", "IRR"), ("paidOn", Day(0))), now);
        Reject(() => records.Act(manager, org, Sepehr, "payments", managerPayment, "approve", Cmd(Guid.Empty), now), "approving one's own payment", check);
        Reject(() => records.Create(expert, org, Sepehr, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cheque"), ("amount", "10"), ("currency", "IRR"),
            ("paidOn", Day(0))), now), "a cheque without its number", check);
        records.Act(finance, org, Sepehr, "payments", paymentId, "approve", Cmd(Guid.Empty), now);
        var section = files.GetSection(manager, org, Sepehr, "payments", null, null, 1, now);
        check(section.Totals.Count == 1 && section.Totals[0] is { Currency: "IRR", Amount: 1_700_000_000m },
            "ACC: payment total = approved IRR only (seeded 1.2bn + new 0.5bn); registered, returned and USD items are not mixed in.");
        check(files.GetSection(manager, org, Sepehr, "payments", null, "Approved", 1, now).Total == 2 &&
              files.GetSection(manager, org, Sepehr, "payments", "TRX-1", null, 1, now).Total == 1,
            "ACC: section status filter and search narrow the list.");
        Reject(() => records.Act(finance, org, Sepehr, "payments", managerPayment, "cancel", Cmd(Guid.Empty), now), "cancelling without a reason", check);

        // ── Link existing / unlink never deletes ──
        var looseLead = store.Write(d =>
        {
            var lead = new Lead(Guid.NewGuid(), "LD-T-1", "سرنخ آزاد شعبه مرکزی", "فرد", "وب‌سایت", "سارا احمدی", "C01", "B01", "T01", ownerUserId: expert);
            d.Leads.Add(lead);
            return lead.Id;
        });
        check(records.GetLinkForm(expert, org, Sepehr, "leads", "آزاد", now).Options.Any(x => x.Id == looseLead), "ACC: link picker lists unlinked leads of the account's branch.");
        records.Link(expert, org, Sepehr, "leads", looseLead, Cmd(Guid.Empty), now);
        check(store.Read(d => d.Leads.Single(x => x.Id == looseLead).CustomerId) == Sepehr, "ACC: linking an existing lead sets its account.");
        records.Act(expert, org, Sepehr, "leads", looseLead, "unlink", Cmd(Guid.Empty, ("expectedVersion", store.Read(d => d.Leads.Single(x => x.Id == looseLead).Version).ToString())), now);
        check(store.Read(d => d.Leads.SingleOrDefault(x => x.Id == looseLead)) is { CustomerId: null },
            "ACC: unlinking a lead keeps the lead (only the link is removed).");

        var campaign = store.Read(d => d.Campaigns.First(x => x.Name.StartsWith("پیامک")));
        records.Link(manager, org, Sepehr, "campaigns", campaign.Id, Cmd(Guid.Empty, ("contactId", Rostegar.ToString())), now);
        Reject(() => records.Link(manager, org, Sepehr, "campaigns", campaign.Id, Cmd(Guid.Empty), now), "joining the same campaign twice", check);
        var membership = store.Read(d => d.CampaignMembers.Single(x => x.CampaignId == campaign.Id && x.CustomerId == Sepehr));
        records.Act(manager, org, Sepehr, "campaigns", membership.Id, "unlink", Cmd(Guid.Empty, ("expectedVersion", membership.Version.ToString())), now);
        check(store.Read(d => d.Campaigns.Any(x => x.Id == campaign.Id) && !d.CampaignMembers.Any(x => x.Id == membership.Id)),
            "ACC: leaving a campaign removes the membership, not the campaign.");

        var docId = records.Create(expert, org, Sepehr, "documents", new AccountRecordCommand(new Dictionary<string, string?> { ["title"] = "قرارداد اسکن" }, Guid.NewGuid(),
            [new FileUpload("contract.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 test"))]), now);
        notes.LinkDocument(manager, org, Nakhl, docId, now);
        Reject(() => records.Act(expert, org, Sepehr, "documents", docId, "delete", Cmd(Guid.Empty), now), "deleting a document still linked to another account", check);
        records.Act(expert, org, Sepehr, "documents", docId, "unlink", Cmd(Guid.Empty), now);
        check(store.Read(d => d.CrmDocuments.Any(x => x.Id == docId && !x.IsDeleted) && d.DocumentLinks.Count(x => x.DocumentId == docId) == 1) &&
              notes.Download(manager, org, Nakhl, docId).Content.Length > 0,
            "ACC: unlinking a document from one account keeps the file and its other links.");

        // ── Contracts follow sales rules; hierarchy has no cycles ──
        Reject(() => records.Create(manager, org, Sepehr, "salesContracts", Cmd(Guid.NewGuid(), ("opportunityId", oppId.ToString()), ("title", "x"), ("startOn", Day(0)),
            ("endOn", Day(300))), now), "a sales contract from an opportunity that is not won", check);
        var serviceContract = records.Create(manager, org, Sepehr, "serviceContracts", Cmd(Guid.NewGuid(), ("title", "پشتیبانی خط دوم"), ("startOn", Day(0)), ("endOn", Day(365)),
            ("amount", "120,000,000"), ("currency", "IRR")), now);
        check(store.Read(d => d.AccountContracts.Single(x => x.Id == serviceContract).Number.StartsWith("SV-")), "ACC: service contract gets a generated number.");
        records.Link(manager, org, Nakhl, "hierarchy", Mahan, Cmd(Guid.Empty, ("role", "child")), now);
        Reject(() => records.Link(manager, org, Mahan, "hierarchy", Nakhl, Cmd(Guid.Empty, ("role", "child")), now), "a parent/child cycle", check);
        check(files.GetSection(manager, org, Nakhl, "hierarchy", null, null, 1, now).Total == 1, "ACC: the parent lists its child account.");

        // ── Every section renders, every create form opens, and each section's own create/status flow works ──
        var failedSections = AccountFileService.Sections.Where(meta =>
        {
            try { files.GetSection(manager, org, Sepehr, meta.Key, null, null, 1, now); return false; } catch { return true; }
        }).Select(x => x.Key).ToList();
        check(failedSections.Count == 0, "ACC: all related sections render for the manager. Failed: " + string.Join(",", failedSections));
        var creatable = new[] { "opportunities", "leads", "payments", "guarantees", "bankAccounts", "salesContracts", "serviceContracts", "projects", "participations",
            "reservations", "gifts", "samples", "campaigns", "targetLists", "surveys", "documents" };
        var formFailures = creatable.Where(key =>
        {
            try { return records.GetCreateForm(manager, org, Sepehr, key, null, now).Fields.Count == 0; } catch { return true; }
        }).ToList();
        check(formFailures.Count == 0, "ACC: every create form opens with the account preselected. Failed: " + string.Join(",", formFailures));
        check(records.GetAccountForm(manager, org, Sepehr, "status", null).Fields.Any(x => x.Name == "reason") &&
              records.GetAccountForm(manager, org, Sepehr, "classify", null).Fields.Any(x => x.Name == "relationship"), "ACC: account status and classification forms open.");
        Reject(() => records.GetCreateForm(expert, org, Sepehr, "projects", null, now), "an expert opening the project form (no Project.Manage)", check);

        var guarantee = records.Create(manager, org, Sepehr, "guarantees", Cmd(Guid.NewGuid(), ("type", "BankGuarantee"), ("number", "BG-T-1"), ("issuer", "بانک ملی"),
            ("amount", "300,000,000"), ("issuedOn", Day(0)), ("expiresOn", Day(200))), now);
        records.Act(manager, org, Sepehr, "guarantees", guarantee, "release", Cmd(Guid.Empty, ("reason", "پایان تعهد")), now);
        check(store.Read(d => d.DealerGuarantees.Single(x => x.Id == guarantee)) is { Status: Crm.Domain.Channel.DealerGuaranteeStatus.Released, CustomerId: var gc, DealerId: null } &&
              gc == Sepehr, "ACC: an account guarantee is registered and released with a reason.");
        Reject(() => records.Create(manager, org, Sepehr, "bankAccounts", Cmd(Guid.NewGuid(), ("bankName", "ملت"), ("iban", "IR000000000000000000000000"),
            ("holderName", "x")), now), "an invalid IBAN", check);
        var bank = records.Create(manager, org, Sepehr, "bankAccounts", Cmd(Guid.NewGuid(), ("bankName", "بانک ملی"), ("iban", "IR490170000000001234567890"),
            ("holderName", "سپهر")), now);
        bank = store.Read(d => d.AccountBankAccounts.Single(x => x.Id == bank)).Id;
        records.Act(manager, org, Sepehr, "bankAccounts", bank, "primary", Cmd(Guid.Empty), now);
        check(store.Read(d => d.AccountBankAccounts.Count(x => x.CustomerId == Sepehr && x.IsPrimary && x.IsActive)) == 1, "ACC: only one bank account is primary.");
        records.Act(manager, org, Sepehr, "bankAccounts", bank, "deactivate", Cmd(Guid.Empty), now);

        var project = records.Create(manager, org, Sepehr, "projects", Cmd(Guid.NewGuid(), ("name", "استقرار ERP"), ("startOn", Day(0)), ("managerUserId", expert.ToString()),
            ("opportunityId", oppId.ToString()), ("budget", "50,000,000"), ("currency", "IRR")), now);
        records.Act(manager, org, Sepehr, "projects", project, "status", Cmd(Guid.Empty, ("status", "Active")), now);
        records.Act(manager, org, Sepehr, "serviceContracts", serviceContract, "activate", Cmd(Guid.Empty), now);
        records.Act(manager, org, Sepehr, "serviceContracts", serviceContract, "terminate", Cmd(Guid.Empty, ("reason", "پایان همکاری")), now);
        check(store.Read(d => d.AccountProjects.Single(x => x.Id == project).Status == ProjectStatus.Active &&
                              d.AccountContracts.Single(x => x.Id == serviceContract).Status == ContractStatus.Terminated),
            "ACC: project and contract status actions persist.");
        var participation = records.Create(manager, org, Sepehr, "participations", Cmd(Guid.NewGuid(), ("kind", "Program"), ("title", "باشگاه مشتریان"),
            ("startOn", Day(0))), now);
        records.Act(manager, org, Sepehr, "participations", participation, "status", Cmd(Guid.Empty, ("status", "Confirmed")), now);
        var gift = records.Create(expert, org, Sepehr, "gifts", Cmd(Guid.NewGuid(), ("itemName", "سبد نوروزی"), ("quantity", "2"), ("date", Day(0))), now);
        records.Act(expert, org, Sepehr, "gifts", gift, "status", Cmd(Guid.Empty, ("status", "Delivered")), now);
        var newCampaign = records.Create(manager, org, Sepehr, "campaigns", Cmd(Guid.NewGuid(), ("name", "وبینار آزمون"), ("type", "Webinar"), ("startOn", Day(0))), now);
        records.Act(manager, org, Sepehr, "campaigns", newCampaign, "status", Cmd(Guid.Empty, ("status", "Converted")), now);
        var list = records.Create(manager, org, Sepehr, "targetLists", Cmd(Guid.NewGuid(), ("name", "لیست آزمون")), now);
        check(records.GetLinkForm(manager, org, Nakhl, "targetLists", "آزمون", now).Options.Count == 1, "ACC: an existing target list can be linked from another account.");
        records.Act(manager, org, Sepehr, "targetLists", list, "unlink", Cmd(Guid.Empty), now);
        Reject(() => records.Create(manager, org, Sepehr, "surveys", Cmd(Guid.NewGuid(), ("score", "4"), ("respondedOn", Day(0))), now), "a survey response without a survey", check);
        records.Create(manager, org, Sepehr, "surveys", Cmd(Guid.NewGuid(), ("newSurveyTitle", "رضایت از نصب"), ("newSurveyKind", "Nps"), ("score", "9"),
            ("contactId", Rostegar.ToString()), ("respondedOn", Day(0))), now);
        Reject(() => records.Create(manager, org, Sepehr, "surveys", Cmd(Guid.NewGuid(), ("surveyId", store.Read(d => d.Surveys.Single(x => x.Title == "رضایت از نصب").Id.ToString())),
            ("score", "11"), ("respondedOn", Day(0))), now), "an NPS score above 10", check);
        var classifyVersion = store.Read(d => d.Customers.Single(x => x.Id == Sepehr).Version);
        records.UpdateAccount(manager, org, Sepehr, "classify", Cmd(Guid.Empty, ("relationship", "Partner"), ("tags", "کلیدی، صادراتی"), ("expectedVersion", classifyVersion.ToString())), now);
        check(store.Read(d => d.Customers.Single(x => x.Id == Sepehr)) is { RelationshipType: AccountRelationship.Partner } c2 && c2.TagList.Contains("صادراتی"),
            "ACC: relationship type and tags are saved from the account file.");
        Reject(() => records.UpdateAccount(manager, org, Sepehr, "classify", Cmd(Guid.Empty, ("relationship", "Customer"), ("expectedVersion", classifyVersion.ToString())), now),
            "a stale account version", check);
        check(files.GetSection(manager, org, Sepehr, "gifts", null, "Delivered", 1, now).Total == 1 && files.GetSection(manager, org, Sepehr, "surveys", null, null, 1, now).Total >= 2,
            "ACC: allocation and survey sections show the new records.");

        // ── Scope and permission isolation (server side) ──
        Reject(() => files.GetFile(expert, org, Arya, now), "an expert opening an account of another branch", check);
        Reject(() => files.GetFile(manager, new OrganizationSelection("C02", null, null), Sepehr, now), "opening a C01 account in the C02 context", check);
        Reject(() => files.GetFile(dealer, org, Sepehr, now), "a dealer user opening an internal account file", check);
        Reject(() => activities.SaveActivity(finance, org, Sepehr, null, Activity(ActivityType.Call, "x", Guid.NewGuid()) with { OwnerUserId = finance }, now),
            "a finance user creating an activity (no Activity.Create)", check);
        Reject(() => files.GetSection(expert, org, Sepehr, "accessGroups", null, null, 1, now), "a non-admin reading access groups", check);
        Reject(() => records.Create(executive, org, Sepehr, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cash"), ("amount", "1"), ("paidOn", Day(0))), now),
            "a read-only executive registering a payment", check);
        Reject(() => records.Act(supervisor, org, Sepehr, "payments", paymentId, "return", Cmd(Guid.Empty, ("reason", "x")), now),
            "a supervisor of another branch acting on the account", check);

        // ── Deactivate instead of delete; merge moves account-file records and unmerge returns them ──
        var version = store.Read(d => d.Customers.Single(x => x.Id == Arya).Version);
        records.UpdateAccount(manager, org, Arya, "status", Cmd(Guid.Empty, ("reason", "توقف همکاری"), ("expectedVersion", version.ToString())), now);
        Reject(() => records.Create(manager, org, Arya, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cash"), ("amount", "1"), ("paidOn", Day(0))), now),
            "registering a record on an inactive account", check);
        check(files.GetFile(manager, org, Arya, now).Quick is { Call: false, Opportunity: false } && store.Read(d => d.Customers.Any(x => x.Id == Arya)),
            "ACC: a deactivated account stays (no delete) and its quick actions are closed.");

        var mahanCall = activities.SaveActivity(manager, org, Mahan, null, Activity(ActivityType.Call, "تماس ماهان", Guid.NewGuid()) with { ContactId = null, OwnerUserId = manager }, now);
        var mahanPay = records.Create(manager, org, Mahan, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cash"), ("amount", "1000"), ("paidOn", Day(0))), now);
        var pending = store.Read(d => d.CustomerDuplicateCandidates.Single());
        customers.ReviewDuplicate(manager, org, pending.Id, new ReviewDuplicateCommand(DuplicateReviewStatus.Confirmed, "تأیید تکراری", pending.Version), now);
        var candidate = store.Read(d => d.CustomerDuplicateCandidates.Single());
        var versions = store.Read(d => (d.Customers.Single(x => x.Id == Nakhl).Version, d.Customers.Single(x => x.Id == Mahan).Version));
        var merge = customers.Merge(manager, org, candidate.Id, new MergeCustomerCommand(Nakhl, "حساب تکراری", candidate.Version, versions.Item1, versions.Item2), now);
        check(store.Read(d => d.CrmActivities.Single(x => x.Id == mahanCall.Id).CustomerId == Nakhl && d.AccountPayments.Single(x => x.Id == mahanPay).CustomerId == Nakhl),
            "ACC: merging moves activities and payments to the surviving account.");
        customers.Unmerge(manager, org, merge.Id, new UnmergeCustomerCommand("اشتباه", merge.Version), now);
        check(store.Read(d => d.CrmActivities.Single(x => x.Id == mahanCall.Id).CustomerId == Mahan && d.AccountPayments.Single(x => x.Id == mahanPay).CustomerId == Mahan),
            "ACC: unmerging returns exactly those records.");

        // ── Many records: paging keeps the section light ──
        for (var i = 0; i < 25; i++)
            records.Create(expert, org, Sepehr, "payments", Cmd(Guid.NewGuid(), ("direction", "Receipt"), ("method", "Cash"), ("amount", (1000 + i).ToString()), ("paidOn", Day(0))), now);
        var paged = files.GetSection(manager, org, Sepehr, "payments", null, null, 3, now);
        check(paged.Total == 31 && paged.Rows.Count == 10 && paged.TotalPages == 4 && paged.Page == 3, "ACC: sections page at 10 rows with an accurate total.");
        check(files.GetHistory(manager, org, Sepehr).Count(x => x.Kind == nameof(CustomerTimelineType.Payment)) >= 27,
            "ACC: the change log records who created/approved payments and when.");
    }

    private static void Reject(Action action, string what, Action<bool, string> check)
    {
        var rejected = false;
        try { action(); }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException) { rejected = true; }
        check(rejected, $"ACC: {what} is rejected.");
    }
}
