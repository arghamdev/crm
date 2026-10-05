using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.FollowUps;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// مرکز پیگیری: working calendar and weighted progress, case registration with duplicate detection and queue assignment,
/// «a logged call never finishes a stage», referral without an ownerless gap, SLA pause only where the policy allows it,
/// approvals with separation of duties, closing controls, reopen with history, the server monitor's idempotency,
/// template versioning, assignment tests and redistribution, supervision and scope isolation.
/// </summary>
internal static class FollowUpCenterChecks
{
    private static Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
    private static readonly Guid Sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Rostegar = Guid.Parse("21000000-0000-4000-8000-000000000001");

    internal static void Run(Action<bool, string> check)
    {
        Calendar(check);
        Progress(check);

        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var activities = new AccountActivityService(store, access);
        var service = new FollowUpService(store, access, activities);
        var config = new FollowUpConfigurationService(store, access);
        var supervision = new FollowUpSupervisionService(store, access);
        var manager = User(1); var expert = User(2); var supervisor = User(3); var agent = User(4); var finance = User(6); var dealer = User(8); var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;
        string Day(int offset) => TehranTime.Date(now.AddDays(offset));
        FollowUpCase Find(string code) => store.Read(d => d.FollowUpCases.Single(x => x.Code == code));
        bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }

        // ── Seed, list and scope ──
        var managerList = service.GetListAsync(manager, org, new FollowUpListQuery(PageSize: 50), now).GetAwaiter().GetResult();
        check(managerList.Items.Count == 5 && managerList.CanSupervise && managerList.CanConfigure && managerList.NoNextActionCount == 2 &&
              managerList.Items.Any(x => x.Code == "RQ-24089" && x.Owner is null),
            $"FU: the manager sees the five open seeded cases; the unassigned one and RQ-24087 have no next action (items {managerList.Items.Count}, no-next {managerList.NoNextActionCount}).");
        var expertList = service.GetListAsync(expert, org, new FollowUpListQuery(PageSize: 50), now).GetAwaiter().GetResult();
        check(expertList.Items.Select(x => x.Code).Order().SequenceEqual(["RQ-24085", "RQ-24088"]) && !expertList.CanSupervise && !expertList.CanConfigure,
            "FU: a sales expert sees only the cases they are involved in, not other branches' cases.");
        check(service.GetListAsync(expert, org, new FollowUpListQuery("overdue"), now).GetAwaiter().GetResult().Items.Any(x => x.Code == "RQ-24088" && x.IsOverdue),
            "FU: «عقب‌افتاده» is computed automatically from the late next action.");
        check(service.GetCase(expert, org, Find("RQ-24087").Id, now) is null && service.GetCase(dealer, org, Find("RQ-24085").Id, now) is null,
            "FU: a case outside the user's branch/involvement and any case for a dealer user are not visible.");
        check(service.GetListAsync(dealer, new OrganizationSelection("C01", null, null), new FollowUpListQuery(), now).GetAwaiter().GetResult().TotalCount == 0,
            "FU: dealer users (external) get an empty follow-up list.");
        var seeded = service.GetCase(manager, org, Find("RQ-24085").Id, now)!;
        check(seeded.Progress == 42 && seeded.Stages.Count(x => x.Status == FollowUpStageStatus.Done) == 2 && seeded.ActiveStage?.Name == "تأمین موجودی" &&
              seeded.Referrals.Any(x => x.Status == ReferralStatus.Pending && x.CanRespond) && seeded.Rules.Count == 2,
            $"FU: weighted progress of RQ-24085 = 10 + 25 + 20×⅓ ≈ 42٪ (got {seeded.Progress}) and the manager can answer its referral.");

        // ── ۱. Registration: template, SLA, owner from queue, duplicate detection, idempotency ──
        var op = Guid.NewGuid();
        CreateFollowUpCommand Create(string subject, Guid? operation = null, string? related = null, bool allow = false, string? reason = null) => new(subject, Sepehr,
            Rostegar, "Proforma", null, "B01", null, null, FollowUpChannel.Phone, FollowUpPriority.Normal, null,
            related is null ? FollowUpRelatedKind.None : FollowUpRelatedKind.Opportunity, null, related, null, "نیاز به ۴ قلم قطعه", null, null, "fa",
            [new FollowUpPartInput("SEAL-100", "کاسه‌نمد", 10, "عدد")], null, operation ?? Guid.NewGuid(), allow, reason);
        var created = service.Create(expert, org, Create("پیش‌فاکتور کاسه‌نمد خط ۳", op), now);
        var again = service.Create(expert, org, Create("پیش‌فاکتور کاسه‌نمد خط ۳", op), now);
        var fresh = service.GetCase(expert, org, created.CaseId, now)!;
        check(again.CaseId == created.CaseId && store.Read(d => d.FollowUpCases.Count(x => x.Subject == "پیش‌فاکتور کاسه‌نمد خط ۳")) == 1,
            "FU: a repeated submit (same operation id) does not create a duplicate case.");
        check(fresh.Code == "RQ-24090" && fresh.Owner is not null && fresh.Status == FollowUpStatus.InProgress && fresh.NextAction == "پاسخ اولیه به مشتری" &&
              fresh.FirstResponseDueAtUtc > now && fresh.ResolutionDueAtUtc > fresh.FirstResponseDueAtUtc && fresh.Stages.Count == 5 &&
              fresh.Stages[0].Status == FollowUpStageStatus.Active && fresh.Items.Count == 1 && fresh.TemplateVersion == 1,
            "FU: a new case gets a code, an owner from the queue, SLA due times, the template stages and a first next action.");
        check(Throws<InvalidOperationException>(() => service.Create(expert, org, Create("هر موضوع دیگری", related: "OP-2041"), now)) &&
              service.Create(expert, org, Create("سفارش جداگانهٔ قطعات کوره", related: "OP-2041", allow: true, reason: "قرارداد جداگانه"), now).CaseId != Guid.Empty,
            "FU: a case on the same related record is flagged as a duplicate and needs an explicit reason to be opened anyway.");
        check(Throws<InvalidOperationException>(() => service.Create(expert, org, Create("پیش‌فاکتور کاسه نمد خط۳"), now)),
            "FU: the same type with a near-identical subject is detected as a duplicate (normalized text).");

        // ── ۲ و ۳. Action and result: a logged call does not complete a stage ──
        var caseId = created.CaseId;
        var stage1 = fresh.Stages[0];
        service.PlanAction(expert, org, caseId, new PlanFollowUpActionCommand("Call", "تماس تأیید اقلام", stage1.Id, expert, Rostegar, FollowUpChannel.Phone,
            Day(1), "10:00", 15, 30, ActivityPriority.High, "پیش از تماس، لیست اقلام را کنترل کنید", null, ["کنترل لیست اقلام"], ["کنترل لیست اقلام"], Guid.NewGuid()), now);
        var planned = service.GetCase(expert, org, caseId, now)!;
        var call = planned.Activities.Single(x => x.Subject == "تماس تأیید اقلام");
        check(planned.NextAction == "تماس تأیید اقلام" && call.Stage == stage1.Name && call.PreChecklist.SequenceEqual(["✓ کنترل لیست اقلام"]) &&
              store.Read(d => d.CrmActivities.Single(x => x.Id == call.Id)).RelatedKind == ActivityRelatedKind.FollowUpCase,
            "FU: a planned call is linked to the case and stage, carries its pre-call checklist and becomes the next action.");
        check(Throws<InvalidOperationException>(() => service.RecordResult(expert, org, caseId, call.Id,
                new RecordFollowUpResultCommand("Answered", "پاسخ داد", CallResult.Answered, null, null, null, null, null, false, call.Version), now)),
            "FU: recording a result without a next action is rejected.");
        service.RecordResult(expert, org, caseId, call.Id, new RecordFollowUpResultCommand("NeedsNextAction", "اقلام تأیید شد؛ تعداد یک قلم تغییر کرد.",
            CallResult.Answered, "ارسال لیست نهایی به فنی", "Task", expert, Day(2), "09:00", true, call.Version), now);
        var afterCall = service.GetCase(expert, org, caseId, now)!;
        check(afterCall.Stages[0].Status == FollowUpStageStatus.Active && afterCall.Progress == 0 && afterCall.FirstRespondedAtUtc is not null &&
              afterCall.NextAction == "ارسال لیست نهایی به فنی" && afterCall.Activities.Any(x => x.ResultCode == "NeedsNextAction"),
            "FU: logging a call (even with «درخواست تکمیل مرحله») does not finish a stage whose checklist is open.");

        // ── ۵. Checklist and stage completion ──
        check(Throws<InvalidOperationException>(() => service.CompleteStage(expert, org, caseId, stage1.Id, now)),
            "FU: «تکمیل مرحله» is locked until the checklist is done.");
        service.SaveChecklist(expert, org, caseId, stage1.Id, stage1.Checklist.Select(x => x.Id).ToList(), now);
        service.CompleteStage(expert, org, caseId, stage1.Id, now);
        var afterStage = service.GetCase(expert, org, caseId, now)!;
        check(afterStage.Progress == 10 && afterStage.Stages[1].Status == FollowUpStageStatus.Active &&
              afterStage.History.Any(x => x.Kind == "Progress" && x.Title.Contains("10٪")),
            "FU: completing a stage moves the progress by its weight (10٪), starts the next stage and logs the change.");

        // ── ۴. Referral: the current owner stays responsible until acceptance ──
        var stage2 = afterStage.Stages[1];
        var ownerBefore = afterStage.OwnerUserId;
        service.Refer(manager, org, caseId, new ReferFollowUpCommand(ReferralScope.Case, null, expert, "B01", "فروش", "ادامهٔ پیگیری توسط کارشناس",
            Day(1), "12:00", true, false, false), now);
        var referred = service.GetCase(manager, org, caseId, now)!;
        var referral = referred.Referrals.Single(x => x.Status == ReferralStatus.Pending);
        check(referred.OwnerUserId == ownerBefore,
            "FU: until the receiver accepts, the case owner does not change (no ownerless gap).");
        check(Throws<UnauthorizedAccessException>(() => service.RespondReferral(supervisor, org, referral.Id, true, null, now)),
            "FU: only the receiver can accept a referral.");
        service.RespondReferral(expert, org, referral.Id, true, "پذیرفتم", now);
        var accepted = service.GetCase(expert, org, caseId, now)!;
        check(accepted.OwnerUserId == expert && accepted.Code == fresh.Code && accepted.History.Any(x => x.Kind == "Owner" && x.Detail!.Contains("پذیرش ارجاع")),
            "FU: on acceptance ownership moves to the receiver, the code stays and the change is audited.");
        service.Refer(expert, org, caseId, new ReferFollowUpCommand(ReferralScope.Stage, stage2.Id, manager, null, "فنی", "بررسی فنی تخصصی", Day(1), "12:00",
            true, true, true), now);
        var stageReferral = service.GetCase(expert, org, caseId, now)!.Referrals.Single(x => x.Status == ReferralStatus.Pending);
        check(Throws<InvalidOperationException>(() => service.RespondReferral(manager, org, stageReferral.Id, false, "", now)),
            "FU: rejecting a referral requires a reason.");
        service.RespondReferral(manager, org, stageReferral.Id, false, "ظرفیت ندارم", now);
        check(service.GetCase(expert, org, caseId, now)!.Stages[1].ResponsibleUserId == expert &&
              supervision.Get(manager, org, new FollowUpSupervisionQuery(), now).RejectedReferrals >= 1,
            "FU: a rejected referral leaves the stage with its current owner and appears in supervision.");

        // ── ۶. Waiting: pause only where the policy allows it; resume shifts the due times ──
        var dueBefore = service.GetCase(expert, org, caseId, now)!.ResolutionDueAtUtc!.Value;
        service.Wait(expert, org, caseId, new WaitFollowUpCommand(FollowUpStatus.WaitingInternal, stage2.Id, "منتظر پاسخ واحد فنی", "واحد فنی", Day(0), Day(1),
            "12:00", Day(1), "10:00", expert), now);
        check(!Find(fresh.Code).IsPaused && Find(fresh.Code).Status == FollowUpStatus.WaitingInternal,
            "FU: «در انتظار واحد داخلی» does not stop the SLA clock (policy).");
        service.Resume(expert, org, caseId, new ResumeFollowUpCommand(null, "ادامهٔ بررسی فنی", Day(1), "11:00"), now);
        var pauseStart = now;
        service.Wait(expert, org, caseId, new WaitFollowUpCommand(FollowUpStatus.WaitingCustomer, stage2.Id, "منتظر نقشهٔ فنی از مشتری", "علی رستگار", Day(0), Day(2),
            "12:00", Day(2), "10:00", expert), pauseStart);
        var waiting = service.GetCase(expert, org, caseId, now)!;
        check(waiting.IsPaused && waiting.NextAction!.StartsWith("بازبینی") && waiting.ReviewAtUtc is not null,
            "FU: «در انتظار مشتری» pauses the SLA and the review date becomes the next action.");
        var resumeAt = pauseStart.AddDays(1);
        service.Resume(expert, org, caseId, new ResumeFollowUpCommand("نقشه دریافت شد", "بررسی نقشه", TehranTime.Date(resumeAt.AddDays(1)), "10:00"), resumeAt);
        var resumed = service.GetCase(expert, org, caseId, resumeAt)!;
        check(!resumed.IsPaused && resumed.Status == FollowUpStatus.InProgress && resumed.ResolutionDueAtUtc >= dueBefore &&
              resumed.PausedMinutes == (int)Math.Round(store.Read(d => d.FollowUpSlaPolicies.ToList()).First(x => x.Id == Find(fresh.Code).SlaPolicyId).Calendar()
                  .WorkingMinutesBetween(pauseStart, resumeAt)),
            "FU: resuming adds the paused working minutes to the case and shifts the due times by them.");

        // ── ۷. Documents and approvals ──
        service.UploadDocument(expert, org, caseId, stage2.Id, FollowUpDocumentKind.Technical, "نقشهٔ فنی", true,
            new FollowUpDocumentUpload("drawing.pdf", "application/pdf", "%PDF-1.4 test"u8.ToArray()), resumeAt);
        service.UploadDocument(expert, org, caseId, stage2.Id, FollowUpDocumentKind.Technical, "نقشهٔ فنی", true,
            new FollowUpDocumentUpload("drawing-v2.pdf", "application/pdf", "%PDF-1.4 test 2"u8.ToArray()), resumeAt);
        var withDocs = service.GetCase(expert, org, caseId, resumeAt)!;
        check(withDocs.Documents.Select(x => x.Version).Order().SequenceEqual([1, 2]) &&
              store.Read(d => d.DocumentLinks.Count(x => x.CustomerId == Sepehr && withDocs.Documents.Select(f => f.DocumentId).Contains(x.DocumentId))) == 2,
            "FU: a new upload with the same title is a new version, and the files also appear in the customer's account file.");
        check(Throws<InvalidOperationException>(() => service.RequestApproval(expert, org, caseId, new RequestFollowUpApprovalCommand(stage2.Id, agent, null, null), resumeAt)),
            "FU: the approver must hold FollowUp.Approve.");
        service.SaveChecklist(expert, org, caseId, stage2.Id, stage2.Checklist.Select(x => x.Id).ToList(), resumeAt);
        service.RequestApproval(expert, org, caseId, new RequestFollowUpApprovalCommand(stage2.Id, manager, "مدیر فنی", null), resumeAt);
        var approval = service.GetCase(manager, org, caseId, resumeAt)!.Approvals.Single(x => x.Decision is null);
        check(approval.ReviewItems.Count == 3 && Throws<InvalidOperationException>(() => service.CompleteStage(expert, org, caseId, stage2.Id, resumeAt)),
            "FU: review items default to the stage checklist and an open approval blocks the stage completion.");
        check(Throws<InvalidOperationException>(() => service.DecideApproval(manager, org, caseId, approval.Id,
                new DecideFollowUpApprovalCommand(ApprovalDecision.NeedsCorrection, "سازگاری تأیید نشد", [approval.ReviewItems[0]], null, null, null), resumeAt)),
            "FU: «نیازمند اصلاح» needs a correction deadline.");
        service.DecideApproval(manager, org, caseId, approval.Id, new DecideFollowUpApprovalCommand(ApprovalDecision.NeedsCorrection, "سازگاری با دستگاه تأیید نشد",
            [approval.ReviewItems[0], approval.ReviewItems[2]], expert, TehranTime.Date(resumeAt.AddDays(1)), "15:00"), resumeAt);
        var corrected = service.GetCase(expert, org, caseId, resumeAt)!;
        check(corrected.Stages[1].Status == FollowUpStageStatus.Returned && corrected.Stages[1].Checklist.Count(x => !x.IsDone) == 1 && corrected.Progress < 35 &&
              corrected.NextAction!.StartsWith("اصلاح") && corrected.Documents.All(x => x.Status == FollowUpDocumentStatus.Rejected),
            "FU: a correction returns the stage, reopens the failed item, lowers the progress and sets the correction as the next action.");

        // ── ۸. Closing controls ──
        check(Throws<InvalidOperationException>(() => service.Close(expert, org, caseId, new CloseFollowUpCommand("موفق", null, false, null, null), null, resumeAt)),
            "FU: an incomplete case cannot be closed (required stages, documents, planned activities).");
        var checks = corrected.CloseChecks;
        check(checks.Any(x => !x.Passed && x.Label.Contains("مراحل")) && checks.Any(x => x.Label.Contains("تأیید مشتری")),
            "FU: the close panel lists each pre-close control with its state.");

        // ── Reopen keeps the closing history ──
        var closedCase = Find("RQ-24084");
        check(Throws<UnauthorizedAccessException>(() => service.Reopen(expert, org, closedCase.Id, new ReopenFollowUpCommand("دلیل", expert, Day(2), "10:00"), now)) &&
              Throws<InvalidOperationException>(() => service.Reopen(manager, org, closedCase.Id, new ReopenFollowUpCommand("", expert, Day(2), "10:00"), now)),
            "FU: reopening needs FollowUp.Reopen (not held by experts) and a reason.");
        service.Reopen(manager, org, closedCase.Id, new ReopenFollowUpCommand("مشتری قیمت را دوباره خواست", expert, Day(2), "10:00"), now);
        var reopened = service.GetCase(manager, org, closedCase.Id, now)!;
        check(reopened.Status == FollowUpStatus.InProgress && reopened.ReopenCount == 1 && reopened.History.Any(x => x.Kind == "Closed") &&
              reopened.History.Any(x => x.Kind == "Reopened" && x.Detail!.Contains("نتیجهٔ قبلی")),
            "FU: reopening keeps the earlier closing in the history and records reason, owner and new due time.");

        // ── Branch transfer keeps the code and history ──
        service.Reassign(manager, org, caseId, supervisor, "B02", "انتقال به شعبه اصفهان برای تأمین محلی", resumeAt);
        var moved = service.GetCase(manager, org, caseId, resumeAt)!;
        check(moved.Code == fresh.Code && moved.BranchId == "B02" && moved.OwnerUserId == supervisor && moved.ResolutionDueAtUtc == resumed.ResolutionDueAtUtc &&
              moved.History.Any(x => x.Kind == "Branch"),
            "FU: moving a case to another branch keeps its code, history and due times.");

        // ── Server monitor: escalation, reminders and assignment are idempotent ──
        var later = now.AddDays(5);
        var first = service.RunMonitor(later);
        var second = service.RunMonitor(later);
        check(first.Escalations > 0 && first.Reminders > 0 && second.Escalations == 0 && second.Reminders == 0 && second.LateReferrals == 0,
            $"FU: the server monitor escalates and reminds once; a second run sends nothing new (first {first}, second {second}).");
        check(Find("RQ-24089").OwnerUserId is null || first.AutoAssigned > 0,
            "FU: waiting cases are assigned by the monitor when someone in the queue (or the overflow queue) has capacity.");
        check(Find("RQ-24085").OwnerUserId == expert && store.Read(d => d.NotificationMessages.Any(x => x.Subject.Contains("هشدار مهلت"))),
            "FU: escalation notifies the next level but never changes the case owner.");

        // ── ۹. Template versioning ──
        var overview = config.GetOverview(manager, org, now);
        var proforma = overview.Templates.Single(x => x.CaseType == "Proforma");
        check(overview.Templates.Count == 7 && overview.Policies.Count == 4 && overview.Queues.Count == 4 && proforma.OpenCases >= 3,
            "FU: settings list one template per follow-up type, four SLA policies and the queues.");
        check(Throws<UnauthorizedAccessException>(() => config.GetOverview(expert, org, now)), "FU: only FollowUp.Configure opens the settings.");
        var draftId = config.NewTemplateVersion(manager, org, proforma.Id, now);
        check(config.NewTemplateVersion(manager, org, proforma.Id, now) == draftId, "FU: an open draft is reused instead of creating another.");
        var draft = config.GetTemplate(manager, org, draftId);
        var badStages = draft.Stages.Select(x => x with { Weight = 10 }).ToList();
        SaveFollowUpTemplateCommand Save(IReadOnlyList<FollowUpTemplateStageInput> stages, long version) => new(draft.Name, draft.CaseType, draft.Scope, draft.OwnerUnit,
            draft.Description, draft.ClosingCriteria, true, true, draft.Rules, draft.SlaPolicyId, draft.DefaultPriority, stages, version);
        config.SaveTemplate(manager, org, draftId, Save(badStages, draft.EntityVersion), now);
        check(Throws<InvalidOperationException>(() => config.PublishTemplate(manager, org, draftId, now)), "FU: a template whose weights do not total 100٪ cannot be published.");
        var saved = config.GetTemplate(manager, org, draftId);
        var goodStages = draft.Stages.Take(4).Select((x, i) => x with { Weight = i == 0 ? 25 : 25 }).ToList();
        config.SaveTemplate(manager, org, draftId, Save(goodStages, saved.EntityVersion), now);
        config.PublishTemplate(manager, org, draftId, now);
        var published = store.Read(d => d.FollowUpTemplates.Where(x => x.Code == proforma.Code).ToList());
        check(published.Single(x => x.TemplateVersion == 2).Status == FollowUpTemplateStatus.Published &&
              published.Single(x => x.TemplateVersion == 1).Status == FollowUpTemplateStatus.Retired &&
              Find("RQ-24085").TemplateVersion == 1 && store.Read(d => d.FollowUpStages.ToList()).Count(x => x.CaseId == Find("RQ-24085").Id) == 5,
            "FU: publishing version 2 retires version 1 for new cases while open cases keep the version they started with.");
        var v2 = service.Create(expert, org, Create("پیش‌فاکتور گیربکس خط ۵"), now);
        check(service.GetCase(expert, org, v2.CaseId, now)!.TemplateVersion == 2, "FU: new cases use the newly published version.");

        // ── ۱۰. SLA preview ──
        var policy = overview.Policies.Single(x => x.Priority == FollowUpPriority.Normal);
        var preview = config.PreviewPolicy(manager, org, new SaveSlaPolicyCommand(policy.Name, policy.Priority, null, "Asia/Tehran", policy.WorkDays, "08:00", "17:00", null,
            2, 8, 24, true, false, policy.Escalations, true, policy.Version), "1405/07/15", "16:30", now);
        check(preview.FirstResponseDue.EndsWith("09:30") && preview.EscalationTimes.Count == 3,
            $"FU: a case registered Wednesday 16:30 gets its 2-hour first response on Saturday 09:30 ({preview.FirstResponseDue}).");

        // ── ۱۱. Assignment test, overflow and redistribution ──
        var test = config.TestAssignment(manager, org, "B03", "Warranty", null, "fa", null, now);
        check(test.Overflowed && test.UserId == manager && test.Candidates.Any(x => x.UserId == agent && !x.Available),
            "FU: when the branch queue has nobody available (agent off shift), the overflow queue assigns the case.");
        var moves = config.Redistribute(manager, org, overview.Queues.Single(x => x.Name == "فروش قطعات صنعتی").Id, expert, "مرخصی", now);
        check(moves > 0 && store.Read(d => d.FollowUpCases.Where(x => x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled)
                  .All(x => x.OwnerUserId != expert)),
            "FU: an absent member's open cases are redistributed so none is left with an unavailable owner.");

        // ── ۱۲. Supervision ──
        var board = supervision.Get(manager, org, new FollowUpSupervisionQuery(), now);
        check(board.OpenCount > 0 && board.Attention.Any(x => x.Code == "RQ-24087" && x.Reason.StartsWith("بدون اقدام بعدی")) && board.Attention.Select(x => x.Id).Distinct().Count() == board.Attention.Count && board.LongestWaits.Count > 0 && board.Load.Count > 0,
            "FU: the supervision board explains why each case needs attention and who owns it.");
        check(Throws<UnauthorizedAccessException>(() => supervision.Get(expert, org, new FollowUpSupervisionQuery(), now)) &&
              supervision.Get(executive, org, new FollowUpSupervisionQuery(), now).OpenCount == board.OpenCount,
            "FU: supervision needs FollowUp.Supervise (executives have it; experts do not).");
        var csv = supervision.ExportCsv(manager, org, new FollowUpSupervisionQuery(), now);
        check(csv.StartsWith("\"کد پرونده\"") && csv.Contains("RQ-24087"), "FU: the supervision export lists the attention cases.");
        var viewId = supervision.SaveView(manager, org, "شعبه مرکزی", new FollowUpSupervisionQuery("B01", null, "week"));
        check(supervision.Get(manager, org, new FollowUpSupervisionQuery(), now).SavedViews.Any(x => x.Id == viewId && x.Query.Contains("branch=B01")),
            "FU: supervisors can save a view of the board.");
        _ = finance;
    }

    private static void Calendar(Action<bool, string> check)
    {
        var calendar = new WorkCalendar([DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday], new TimeOnly(8, 0),
            new TimeOnly(17, 0), WorkCalendar.ResolveZone("Asia/Tehran"));
        var wednesday = new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.Zero); // 16:30 Tehran
        var due = calendar.AddWorkingMinutes(wednesday, 120);
        check(due == new DateTimeOffset(2026, 10, 10, 6, 0, 0, TimeSpan.Zero) && calendar.WorkingMinutesBetween(wednesday, due) == 120,
            "FU: 2 working hours from Wednesday 16:30 (Tehran) end on Saturday 09:30; the calendar counts them back as 120 minutes.");
        var holiday = new WorkCalendar(calendar.Days, calendar.Start, calendar.End, calendar.Zone, [new DateOnly(2026, 10, 10)]);
        check(holiday.AddWorkingMinutes(wednesday, 120) == new DateTimeOffset(2026, 10, 11, 6, 0, 0, TimeSpan.Zero),
            "FU: holidays are skipped by the working calendar.");
    }

    private static void Progress(Action<bool, string> check)
    {
        var total = FollowUpProgress.Total([(10, FollowUpStageStatus.Done, 100), (40, FollowUpStageStatus.Skipped, 0), (50, FollowUpStageStatus.Active, 50)]);
        check(total == 58, $"FU: skipped conditional stages are left out of the weighted progress (10 + 25)/60 = 58٪ (got {total}).");
        check(FollowUpProgress.StageProgress(FollowUpStageStatus.Active, 3, 3, 0) < 100 && FollowUpProgress.StageProgress(FollowUpStageStatus.Done, 0, 3, 0) == 100,
            "FU: a stage reaches 100٪ only when it is completed, not just by ticking its checklist.");
    }
}
