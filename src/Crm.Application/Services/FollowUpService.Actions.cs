using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.FollowUps;
using static Crm.Application.Services.FollowUpSupport;
using P = Crm.Application.Services.FollowUpPermissions;

namespace Crm.Application.Services;

public sealed partial class FollowUpService
{
    private FollowUpActionResult Act(Guid userId, OrganizationSelection organization, Guid caseId, string permission,
        Func<CrmDataSet, AccessSnapshot, FollowUpCase, FollowUpActionResult> action)
    {
        var snapshot = Snapshot(userId);
        return store.Write(data => action(data, snapshot, Case(data, snapshot, organization, userId, caseId, permission)));
    }

    private static FollowUpStage Stage(CrmDataSet data, FollowUpCase c, Guid stageId) =>
        data.Find<FollowUpStage>(x => x.Id == stageId && x.CaseId == c.Id).SingleOrDefault() ?? throw new KeyNotFoundException("مرحله در این پرونده پیدا نشد.");

    private static ActivityType ActivityKind(string? kind) => kind switch
    {
        "Meeting" or "Visit" => ActivityType.Meeting,
        "Task" => ActivityType.Task,
        _ => ActivityType.Call
    };

    // ───────────── ۲. برنامه‌ریزی اقدام ─────────────

    public FollowUpActionResult PlanAction(Guid userId, OrganizationSelection organization, Guid caseId, PlanFollowUpActionCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        var (customerId, stageName) = store.Read(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId, P.Update);
            if (!c.IsOpen) throw new InvalidOperationException("پرونده بسته است؛ ابتدا آن را بازگشایی کنید.");
            var stage = command.StageId is { } sid ? Stage(data, c, sid) : null;
            // «مخاطب»: a call, meeting or visit is with someone at the customer — required when the customer has active contacts.
            if (command.Kind != "Task" && command.ContactId is null &&
                data.Find<Crm.Domain.Customers.CustomerContact>(x => x.CustomerId == c.CustomerId && x.IsActive).Count > 0)
                throw new InvalidOperationException("مخاطب اقدام را انتخاب کنید.");
            if (command.ContactId is { } contactId &&
                data.Find<Crm.Domain.Customers.CustomerContact>(x => x.Id == contactId && x.CustomerId == c.CustomerId).Count == 0)
                throw new InvalidOperationException("مخاطب انتخاب‌شده متعلق به این مشتری نیست.");
            return (c.CustomerId, stage?.Name);
        });
        if (string.IsNullOrWhiteSpace(command.Title)) throw new InvalidOperationException("عنوان اقدام الزامی است.");
        var type = ActivityKind(command.Kind);
        // Date and time are entered in the chosen time zone (default: the branch's Tehran time) and stored in UTC.
        var start = ZonedTime(command.Date, command.Time, command.TimeZone, "تاریخ و ساعت اقدام", nowUtc);
        var duration = command.DurationMinutes is > 0 and <= 600 ? command.DurationMinutes : type == ActivityType.Task ? null : 15;
        var end = type == ActivityType.Meeting ? start.AddMinutes(duration ?? 60) : (DateTimeOffset?)null;
        var location = command.Kind == "Visit" ? "بازدید: " + (string.IsNullOrWhiteSpace(command.Location) ? "محل مشتری" : command.Location!.Trim())
            : type == ActivityType.Call && !string.IsNullOrWhiteSpace(command.Phone) ? "شماره تماس: " + command.Phone.Trim() : command.Location;
        var activity = activities.SaveActivity(userId, organization, customerId, null, new SaveActivityCommand(type, command.Title, command.Instructions, command.ContactId,
            command.OwnerUserId, TehranTime.Date(start), TehranTime.Clock(start), end is { } e ? TehranTime.Date(e) : null, end is { } e2 ? TehranTime.Clock(e2) : null,
            duration, type == ActivityType.Call ? CallDirection.Outbound : null, location, command.Priority, command.ReminderMinutes, ActivityRelatedKind.FollowUpCase,
            caseId, null, null, command.OperationId), nowUtc);
        var done = (command.PreChecklistDone ?? []).ToHashSet();
        var checklist = string.Join('\n', (command.PreChecklist ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => (done.Contains(x) ? "✓ " : "") + x.Trim()));
        return Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            if (data.Find<FollowUpActivityLink>(x => x.Id == activity.Id).Count == 0)
                data.Append(new FollowUpActivityLink(activity.Id, c.Id, command.StageId, command.Channel, checklist));
            c.PlanNextAction(command.Title!, start, command.OwnerUserId, nowUtc);
            Log(data, c, "Action", $"اقدام برنامه‌ریزی شد: {command.Title}", $"{ActivityLabel(command.Kind)} · {TehranTime.Format(start)} · مسئول: " +
                AccountGuard.UserNames(data, [command.OwnerUserId]).GetValueOrDefault(command.OwnerUserId, "—") + (stageName is null ? "" : $" · مرحله: {stageName}"), userId, nowUtc);
            return new FollowUpActionResult(c.Id, "اقدام برنامه‌ریزی شد و اقدام بعدی پرونده به‌روز شد.");
        });
    }

    private static DateTimeOffset ZonedTime(string? date, string? time, string? zoneId, string label, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(zoneId) || zoneId == "Asia/Tehran") return RequiredTime(date, time, label, nowUtc);
        if (!JalaliDate.TryParse(date, out var day)) throw new InvalidOperationException($"{label}: تاریخ را به شکل شمسی ۱۴۰۵/۰۷/۱۲ وارد کنید.");
        if (!TimeOnly.TryParseExact(PersianText.Normalize(string.IsNullOrWhiteSpace(time) ? "09:00" : time), ["HH:mm", "H:mm"],
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var clock))
            throw new InvalidOperationException($"{label}: ساعت را به شکل ۱۴:۳۰ وارد کنید.");
        var zone = WorkCalendar.ResolveZone(zoneId);
        var local = day.ToDateTime(clock, DateTimeKind.Unspecified);
        var at = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        if (at <= nowUtc) throw new InvalidOperationException($"{label} باید در آینده باشد.");
        return at;
    }

    private static string ActivityLabel(string? kind) => kind switch { "Meeting" => "جلسه", "Visit" => "بازدید", "Task" => "کار داخلی", _ => "تماس" };

    // ───────────── ۳. ثبت نتیجه ─────────────

    public FollowUpActionResult RecordResult(Guid userId, OrganizationSelection organization, Guid caseId, Guid activityId, RecordFollowUpResultCommand command,
        DateTimeOffset nowUtc)
    {
        if (!FollowUpResults.Exists(command.ResultCode)) throw new InvalidOperationException("نتیجهٔ پیگیری را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(command.Outcome)) throw new InvalidOperationException("شرح نتیجه الزامی است.");
        var snapshot = Snapshot(userId);
        var (customerId, stageId, nextOwner) = store.Read(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId, P.Update);
            var link = data.Find<FollowUpActivityLink>(x => x.Id == activityId && x.CaseId == c.Id).SingleOrDefault() ??
                throw new KeyNotFoundException("این فعالیت به پرونده متصل نیست.");
            return (c.CustomerId, link.StageId, command.NextOwnerUserId ?? c.OwnerUserId ?? userId);
        });
        // Every finished activity of an open case is followed by the next action, unless the user asks to finish the stage.
        var hasNext = !string.IsNullOrWhiteSpace(command.NextTitle) && !string.IsNullOrWhiteSpace(command.NextDate);
        if (!hasNext && !command.RequestStageCompletion) throw new InvalidOperationException("اقدام بعدی (عنوان، مسئول و موعد) را تعیین کنید.");
        var nextAt = hasNext ? RequiredTime(command.NextDate, command.NextTime, "موعد اقدام بعدی", nowUtc) : (DateTimeOffset?)null;
        // A call needs a call result; when it was not picked it follows the follow-up result («پاسخ نداد» → no answer).
        var callResult = command.CallResult ?? (command.ResultCode == "NoAnswer" ? CallResult.NoAnswer : CallResult.Answered);
        // «تاریخ انجام» and «ساعت»: when the activity actually happened (not in the future).
        var doneAt = string.IsNullOrWhiteSpace(command.DoneDate) ? nowUtc
            : TehranTime.ToUtc(command.DoneDate, command.DoneTime, "تاریخ انجام") ?? nowUtc;
        if (doneAt > nowUtc.AddMinutes(5)) throw new InvalidOperationException("تاریخ انجام نمی‌تواند در آینده باشد.");
        activities.Complete(userId, organization, customerId, activityId, new CompleteActivityCommand(command.Outcome, callResult, null, null, null, null, null,
            command.ExpectedVersion), doneAt);
        Guid? nextActivityId = null;
        if (hasNext)
        {
            var type = ActivityKind(command.NextKind);
            nextActivityId = activities.SaveActivity(userId, organization, customerId, null, new SaveActivityCommand(type, command.NextTitle, null, null, nextOwner,
                TehranTime.Date(nextAt!.Value), TehranTime.Clock(nextAt.Value), type == ActivityType.Meeting ? TehranTime.Date(nextAt.Value.AddHours(1)) : null,
                type == ActivityType.Meeting ? TehranTime.Clock(nextAt.Value.AddHours(1)) : null, type == ActivityType.Call ? 15 : null,
                type == ActivityType.Call ? CallDirection.Outbound : null, null, ActivityPriority.Normal, type == ActivityType.Task ? null : 15,
                ActivityRelatedKind.FollowUpCase, caseId, null, null, Guid.NewGuid()), nowUtc).Id;
        }
        return Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var link = data.Find<FollowUpActivityLink>(x => x.Id == activityId).Single();
            link.RecordResult(command.ResultCode!, command.RequestStageCompletion);
            c.MarkFirstResponse(nowUtc);
            var doer = command.DoneByUserId is { } by ? AccountGuard.UserNames(data, [by]).GetValueOrDefault(by) : null;
            Log(data, c, "Result", $"نتیجه ثبت شد: {FollowUpResults.Label(command.ResultCode)}",
                command.Outcome + (doer is null ? "" : $" · انجام‌دهنده: {doer}") + $" · زمان انجام: {TehranTime.Format(doneAt)}", userId, nowUtc);
            var message = "نتیجه ثبت شد.";
            if (nextActivityId is { } nextId)
            {
                data.Append(new FollowUpActivityLink(nextId, c.Id, stageId, link.Channel, null));
                c.PlanNextAction(command.NextTitle!, nextAt!.Value, nextOwner, nowUtc);
                Log(data, c, "NextAction", $"اقدام بعدی: {command.NextTitle}", TehranTime.Format(nextAt.Value), userId, nowUtc);
            }
            if (command.RequestStageCompletion && stageId is { } sid)
            {
                // Logging a call never finishes a stage on its own: the stage's checklist decides.
                var stage = Stage(data, c, sid);
                var remaining = data.Find<FollowUpChecklistItem>(x => x.StageId == sid && !x.IsDone).Count;
                if (stage.IsWorking && remaining == 0 && !data.Find<FollowUpApproval>(x => x.StageId == sid && x.Decision == null).Any())
                {
                    stage.Complete(userId, nowUtc);
                    Log(data, c, "Stage", $"مرحلهٔ «{stage.Name}» تکمیل شد", "به درخواست ثبت نتیجه، پس از تکمیل چک‌لیست", userId, nowUtc);
                    message = "نتیجه ثبت شد و مرحله تکمیل شد.";
                }
                else
                {
                    Log(data, c, "Stage", $"درخواست تکمیل مرحلهٔ «{stage.Name}» ثبت شد", remaining > 0 ? $"{remaining} مورد چک‌لیست باقی است" : "تأیید مرحله باز است", userId, nowUtc);
                    message = remaining > 0 ? $"نتیجه ثبت شد؛ مرحله تا انجام {remaining} مورد چک‌لیست تکمیل نمی‌شود." : "نتیجه ثبت شد؛ مرحله در انتظار تأیید است.";
                }
                Recompute(data, c, userId, nowUtc);
            }
            if (!hasNext && c.IsOpen && c.NextActionAtUtc is null && c.OwnerUserId is { } owner)
                c.PlanNextAction("تعیین اقدام بعدی پرونده", nowUtc.AddHours(4), owner, nowUtc);
            return new FollowUpActionResult(c.Id, message);
        });
    }

    // ───────────── ۴. ارجاع و پذیرش مسئولیت ─────────────

    public FollowUpActionResult Refer(Guid userId, OrganizationSelection organization, Guid caseId, ReferFollowUpCommand command, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, snapshot, c) =>
        {
            if (!c.IsOpen) throw new InvalidOperationException("پرونده بسته است.");
            var stage = command.Scope == ReferralScope.Stage ? Stage(data, c, command.StageId ?? Guid.Empty) : null;
            var holder = stage is null ? c.OwnerUserId : stage.ResponsibleUserId ?? c.OwnerUserId;
            if (holder != userId && !Has(snapshot, c.CompanyId, P.Assign))
                throw new UnauthorizedAccessException("فقط مسئول فعلی یا کاربر دارای مجوز ارجاع می‌تواند این کار را ارجاع دهد.");
            if (data.Find<FollowUpReferral>(x => x.CaseId == c.Id && x.Status == ReferralStatus.Pending && x.StageId == (stage == null ? null : stage.Id)).Any())
                throw new InvalidOperationException("برای همین کار یک ارجاع در انتظار پذیرش وجود دارد.");
            var targetBranch = string.IsNullOrWhiteSpace(command.ToBranchId) ? c.BranchId : command.ToBranchId.Trim();
            if (!BranchUsers(data, c.CompanyId, targetBranch, null, nowUtc).Any(x => x.Id == command.ToUserId))
                throw new InvalidOperationException("گیرنده باید کاربر فعال شعبهٔ مقصد باشد.");
            var due = RequiredTime(command.AcceptDate, command.AcceptTime, "مهلت پذیرش", nowUtc);
            var referral = new FollowUpReferral(Guid.NewGuid(), c.Id, c.CompanyId, command.Scope, stage?.Id, userId, command.ToUserId, targetBranch, command.ToTeam,
                command.Reason ?? string.Empty, due, command.IncludeHistory, command.IncludeQuote, command.IncludeTechnical, nowUtc);
            data.Append(referral);
            var names = AccountGuard.UserNames(data, [command.ToUserId]);
            Log(data, c, "Referral", $"ارجاع {(stage is null ? "کل پرونده" : $"مرحلهٔ «{stage.Name}»")} به {names.GetValueOrDefault(command.ToUserId, "—")}",
                $"دلیل: {referral.Reason} · مهلت پذیرش: {TehranTime.Format(due)} · تا پذیرش، مسئول فعلی پاسخ‌گو می‌ماند.", userId, nowUtc);
            Notify(data, c, [command.ToUserId], $"ارجاع پرونده {c.Code}", $"{c.Subject} — {referral.Reason} (مهلت پذیرش {TehranTime.Format(due)})",
                $"fu-ref:{referral.Id}", nowUtc);
            return new FollowUpActionResult(c.Id, "ارجاع ثبت شد؛ تا پذیرش گیرنده، مسئول فعلی پاسخ‌گو می‌ماند.");
        });

    public FollowUpActionResult RespondReferral(Guid userId, OrganizationSelection organization, Guid referralId, bool accept, string? note, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        if (!Has(snapshot, organization.CompanyId, P.Read)) throw new UnauthorizedAccessException("FollowUp.Read permission is required.");
        return store.Write(data =>
        {
            var referral = data.Find<FollowUpReferral>(x => x.Id == referralId && x.CompanyId == organization.CompanyId).SingleOrDefault() ??
                throw new KeyNotFoundException("ارجاع پیدا نشد.");
            var c = data.Find<FollowUpCase>(x => x.Id == referral.CaseId).Single();
            if (!c.IsOpen) throw new InvalidOperationException("پرونده بسته شده است.");
            var names = AccountGuard.UserNames(data, [referral.FromUserId, referral.ToUserId]);
            if (!accept)
            {
                referral.Reject(userId, note ?? string.Empty, nowUtc);
                Log(data, c, "Referral", $"ارجاع توسط {names.GetValueOrDefault(userId, "—")} رد شد", referral.ResponseNote, userId, nowUtc);
                Notify(data, c, [referral.FromUserId], $"ارجاع پرونده {c.Code} رد شد", referral.ResponseNote ?? "", $"fu-ref-no:{referral.Id}", nowUtc);
                return new FollowUpActionResult(c.Id, "ارجاع رد شد؛ مسئول قبلی همچنان پاسخ‌گوست.");
            }
            referral.Accept(userId, note, nowUtc);
            if (referral.Scope == ReferralScope.Stage && referral.StageId is { } sid)
            {
                var stage = data.Find<FollowUpStage>(x => x.Id == sid).Single();
                stage.AssignResponsible(userId);
                Log(data, c, "Owner", $"مسئول مرحلهٔ «{stage.Name}»: {names.GetValueOrDefault(userId, "—")}", $"پذیرش ارجاع · {referral.Reason}", userId, nowUtc);
            }
            else
            {
                var previous = c.OwnerUserId;
                if (referral.ToBranchId is { } branch && !AccountGuard.Same(branch, c.BranchId))
                {
                    var before = c.BranchId;
                    c.MoveBranch(branch, null);
                    Log(data, c, "Branch", $"انتقال پرونده از شعبهٔ {before} به {branch}", "شناسه، سوابق و مهلت‌ها بدون تغییر ماند.", userId, nowUtc);
                }
                c.AssignOwner(userId, c.QueueId);
                HandOverStages(data, c, previous, userId);
                if (c.NextActionOwnerUserId == previous && c.NextAction is not null && c.NextActionAtUtc is { } at && at > nowUtc)
                    c.PlanNextAction(c.NextAction, at, userId, nowUtc);
                Log(data, c, "Owner", $"مسئول پرونده: {names.GetValueOrDefault(userId, "—")}", $"پذیرش ارجاع از {names.GetValueOrDefault(referral.FromUserId, "—")} · {referral.Reason}",
                    userId, nowUtc);
            }
            Notify(data, c, [referral.FromUserId], $"ارجاع پرونده {c.Code} پذیرفته شد", c.Subject, $"fu-ref-ok:{referral.Id}", nowUtc);
            return new FollowUpActionResult(c.Id, "ارجاع پذیرفته شد و مسئولیت به شما منتقل شد.");
        });
    }

    // ───────────── ۵. مراحل و وظایف ─────────────

    public FollowUpActionResult SaveChecklist(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, IReadOnlyCollection<Guid> doneItemIds,
        DateTimeOffset nowUtc) => Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var stage = Stage(data, c, stageId);
            if (!stage.IsWorking) throw new InvalidOperationException("فقط چک‌لیست مرحلهٔ فعال قابل تغییر است.");
            var changes = new List<string>();
            foreach (var item in data.Find<FollowUpChecklistItem>(x => x.StageId == stageId))
            {
                var done = doneItemIds.Contains(item.Id);
                if (done && !item.IsDone) { item.Check(userId, nowUtc); changes.Add("✓ " + item.Title); }
                else if (!done && item.IsDone) { item.Uncheck(); changes.Add("✗ " + item.Title); }
            }
            if (changes.Count == 0) return new FollowUpActionResult(c.Id, "تغییری در چک‌لیست نبود.");
            Log(data, c, "Checklist", $"چک‌لیست مرحلهٔ «{stage.Name}» به‌روز شد", string.Join("، ", changes), userId, nowUtc);
            Recompute(data, c, userId, nowUtc, $"چک‌لیست مرحلهٔ «{stage.Name}»");
            return new FollowUpActionResult(c.Id, "وضعیت وظایف ذخیره شد.");
        });

    public FollowUpActionResult CompleteStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var stage = Stage(data, c, stageId);
            var remaining = data.Find<FollowUpChecklistItem>(x => x.StageId == stageId && !x.IsDone).Count;
            if (remaining > 0) throw new InvalidOperationException($"برای تکمیل مرحله، {remaining} مورد باقی‌مانده انجام شود.");
            if (data.Find<FollowUpApproval>(x => x.StageId == stageId && x.Decision == null).Any())
                throw new InvalidOperationException("درخواست تأیید این مرحله هنوز باز است.");
            stage.Complete(userId, nowUtc);
            Log(data, c, "Stage", $"مرحلهٔ «{stage.Name}» تکمیل شد", null, userId, nowUtc);
            Recompute(data, c, userId, nowUtc, $"تکمیل مرحلهٔ «{stage.Name}»");
            return new FollowUpActionResult(c.Id, $"مرحلهٔ «{stage.Name}» تکمیل شد.");
        });

    /// <summary>Leaving a conditional stage out of the case changes how progress is measured, so it needs FollowUp.Assign and a reason.</summary>
    public FollowUpActionResult SkipStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, string? reason, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Assign, (data, _, c) =>
        {
            var stage = Stage(data, c, stageId);
            stage.Skip(reason ?? string.Empty);
            Log(data, c, "Stage", $"مرحلهٔ «{stage.Name}» از محاسبه خارج شد", reason, userId, nowUtc);
            Recompute(data, c, userId, nowUtc, $"کنار گذاشتن مرحلهٔ «{stage.Name}»");
            return new FollowUpActionResult(c.Id, "مرحله کنار گذاشته شد و پیشرفت دوباره محاسبه شد.");
        });

    public FollowUpActionResult AssignStage(Guid userId, OrganizationSelection organization, Guid caseId, Guid stageId, Guid responsibleUserId, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Assign, (data, _, c) =>
        {
            var stage = Stage(data, c, stageId);
            RequireBranchUser(data, c, responsibleUserId, nowUtc, "مسئول مرحله");
            stage.AssignResponsible(responsibleUserId);
            var name = AccountGuard.UserNames(data, [responsibleUserId]).GetValueOrDefault(responsibleUserId, "—");
            Log(data, c, "Owner", $"مسئول مرحلهٔ «{stage.Name}»: {name}", "تعیین مستقیم توسط سرپرست", userId, nowUtc);
            Notify(data, c, [responsibleUserId], $"مرحلهٔ «{stage.Name}» از پرونده {c.Code} به شما سپرده شد", c.Subject, $"fu-stage-owner:{stage.Id}:{responsibleUserId}", nowUtc);
            return new FollowUpActionResult(c.Id, "مسئول مرحله تعیین شد.");
        });

    // ───────────── ۶. انتظار و ازسرگیری ─────────────

    public FollowUpActionResult Wait(Guid userId, OrganizationSelection organization, Guid caseId, WaitFollowUpCommand command, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var stage = command.StageId is { } sid ? Stage(data, c, sid) : null;
            RequireBranchUser(data, c, command.ReviewOwnerUserId, nowUtc, "مسئول بازبینی");
            var review = RequiredTime(command.ReviewDate, command.ReviewTime, "موعد بازبینی", nowUtc);
            var expected = TehranTime.ToUtc(command.ExpectedDate, command.ExpectedTime, "زمان مورد انتظار پاسخ");
            var policy = c.SlaPolicyId is { } pid ? data.Find<SlaPolicy>(x => x.Id == pid).SingleOrDefault() : null;
            var pause = policy?.Pauses(command.Status) ?? command.Status == FollowUpStatus.WaitingCustomer;
            c.EnterWait(command.Status, command.Reason ?? string.Empty, command.WaitingOn, stage?.Id, review, command.ReviewOwnerUserId, pause, nowUtc);
            stage?.SetWaiting(true);
            Log(data, c, "Wait", $"وضعیت: {StatusLabel(command.Status)}", $"علت: {c.WaitReason}" +
                (string.IsNullOrWhiteSpace(command.WaitingOn) ? "" : $" · منتظر پاسخ از: {command.WaitingOn}") +
                (expected is { } e ? $" · پاسخ مورد انتظار: {TehranTime.Format(e)}" : "") + $" · بازبینی: {TehranTime.Format(review)}" +
                (pause ? " · مهلت این مرحله تا ازسرگیری متوقف است" : " · طبق سیاست مهلت، زمان متوقف نمی‌شود"), userId, nowUtc);
            return new FollowUpActionResult(c.Id, pause ? "وضعیت انتظار ثبت شد و مهلت متوقف شد." : "وضعیت انتظار ثبت شد؛ این وضعیت مهلت را متوقف نمی‌کند.");
        });

    public FollowUpActionResult Resume(Guid userId, OrganizationSelection organization, Guid caseId, ResumeFollowUpCommand command, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            if (string.IsNullOrWhiteSpace(command.NextTitle)) throw new InvalidOperationException("اقدام بعدی پس از ازسرگیری را تعیین کنید.");
            var next = RequiredTime(command.NextDate, command.NextTime, "موعد اقدام بعدی", nowUtc);
            var policy = c.SlaPolicyId is { } pid ? data.Find<SlaPolicy>(x => x.Id == pid).SingleOrDefault() : null;
            var calendar = Calendar(policy);
            var paused = c.PausedSinceUtc is { } since ? (int)Math.Round(calendar.WorkingMinutesBetween(since, nowUtc)) : 0;
            DateTimeOffset? Shift(DateTimeOffset? due) => due is { } d && paused > 0 ? calendar.AddWorkingMinutes(d, paused) : due;
            var stage = c.WaitStageId is { } sid ? data.Find<FollowUpStage>(x => x.Id == sid).SingleOrDefault() : null;
            var reason = c.WaitReason;
            c.Resume(paused, Shift(c.FirstResponseDueAtUtc), Shift(c.ResolutionDueAtUtc));
            if (stage is not null)
            {
                stage.SetWaiting(false);
                if (paused > 0) stage.SetDue(Shift(stage.DueAtUtc));
            }
            c.PlanNextAction(command.NextTitle!, next, c.OwnerUserId ?? userId, nowUtc);
            Log(data, c, "Resume", "کار از سر گرفته شد", $"انتظار: {reason}" + (paused > 0 ? $" · {paused} دقیقهٔ کاری توقف به مهلت‌ها افزوده شد" : "") +
                (string.IsNullOrWhiteSpace(command.Note) ? "" : $" · {command.Note}"), userId, nowUtc);
            return new FollowUpActionResult(c.Id, "پرونده از سر گرفته شد.");
        });

    // ───────────── ۷. مدارک و تأییدها ─────────────

    public FollowUpActionResult UploadDocument(Guid userId, OrganizationSelection organization, Guid caseId, Guid? stageId, FollowUpDocumentKind kind, string? title,
        bool needsApproval, FollowUpDocumentUpload file, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var stage = stageId is { } sid ? Stage(data, c, sid) : null;
            AddDocument(data, c, stage?.Id, kind, title, needsApproval, file, userId, nowUtc);
            return new FollowUpActionResult(c.Id, "مدرک بارگذاری شد.");
        });

    private static FollowUpDocument AddDocument(CrmDataSet data, FollowUpCase c, Guid? stageId, FollowUpDocumentKind kind, string? title, bool needsApproval,
        FollowUpDocumentUpload file, Guid userId, DateTimeOffset nowUtc)
    {
        if (file.Content.Length == 0) throw new InvalidOperationException("فایل خالی است.");
        var name = string.IsNullOrWhiteSpace(title) ? DocumentKindLabel(kind) : title.Trim();
        var document = new CrmDocument(Guid.NewGuid(), c.CompanyId, name, file.FileName, file.Content.LongLength, CrmDocument.Hash(file.Content), false, userId, null);
        data.Append(document);
        data.Append(new DocumentContent(document.Id, file.Content));
        // The file also appears in the customer's account file.
        data.Append(new DocumentLink(Guid.NewGuid(), document.Id, c.CompanyId, c.CustomerId, userId));
        var version = data.Find<FollowUpDocument>(x => x.CaseId == c.Id && x.Kind == kind).Where(x => AccountGuard.Same(x.Title, name))
            .Select(x => x.DocumentVersion).DefaultIfEmpty(0).Max() + 1;
        var doc = new FollowUpDocument(Guid.NewGuid(), c.Id, stageId, kind, name, version, document.Id, userId, needsApproval);
        data.Append(doc);
        Log(data, c, "Document", $"مدرک «{name}» نسخهٔ {version} ثبت شد", needsApproval ? "در انتظار تأیید" : null, userId, nowUtc);
        return doc;
    }

    public static string DocumentKindLabel(FollowUpDocumentKind kind) => kind switch
    {
        FollowUpDocumentKind.Proforma => "پیش‌فاکتور", FollowUpDocumentKind.Technical => "مدارک فنی", FollowUpDocumentKind.StockConfirmation => "تأییدیهٔ موجودی",
        FollowUpDocumentKind.CustomerApproval => "تأییدیهٔ مشتری", FollowUpDocumentKind.Invoice => "فاکتور", FollowUpDocumentKind.Photo => "تصویر قطعه", _ => "سایر مدارک"
    };

    public (byte[] Content, string ContentType, string FileName)? DownloadDocument(Guid userId, OrganizationSelection organization, Guid caseId, Guid followUpDocumentId)
    {
        var snapshot = Snapshot(userId);
        return store.Read(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId);
            var doc = data.Find<FollowUpDocument>(x => x.Id == followUpDocumentId && x.CaseId == c.Id).SingleOrDefault();
            if (doc is null) return ((byte[], string, string)?)null;
            var file = data.Find<CrmDocument>(x => x.Id == doc.DocumentId).SingleOrDefault();
            var content = data.Find<DocumentContent>(x => x.Id == doc.DocumentId).SingleOrDefault();
            return file is null || content is null ? null : (content.Bytes, file.ContentType, file.FileName);
        });
    }

    public FollowUpActionResult RequestApproval(Guid userId, OrganizationSelection organization, Guid caseId, RequestFollowUpApprovalCommand command, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            if (!c.IsOpen) throw new InvalidOperationException("پرونده بسته است.");
            var stage = command.StageId is { } sid ? Stage(data, c, sid) : null;
            if (!NotificationOutbox.UsersWithPermission(data, c.CompanyId, P.Approve, nowUtc).Contains(command.ApproverUserId))
                throw new InvalidOperationException("تأییدکننده باید مجوز تأیید پیگیری‌ها را داشته باشد.");
            if (data.Find<FollowUpApproval>(x => x.CaseId == c.Id && x.StageId == (stage == null ? null : stage.Id) && x.Decision == null).Any())
                throw new InvalidOperationException("برای این مرحله درخواست تأیید باز وجود دارد.");
            var items = command.ReviewItems is { Count: > 0 } list ? list : stage is null ? [] :
                data.Find<FollowUpChecklistItem>(x => x.StageId == stage.Id).OrderBy(x => x.Order).Select(x => x.Title).ToList();
            var approval = new FollowUpApproval(Guid.NewGuid(), c.Id, stage?.Id, userId, command.ApproverUserId, command.ApproverRole ?? "تأییدکننده",
                string.Join('\n', items), nowUtc);
            data.Append(approval);
            foreach (var doc in data.Find<FollowUpDocument>(x => x.CaseId == c.Id && x.StageId == (stage == null ? null : stage.Id) && x.Status == FollowUpDocumentStatus.Uploaded))
                doc.RequestApproval();
            var name = AccountGuard.UserNames(data, [command.ApproverUserId]).GetValueOrDefault(command.ApproverUserId, "—");
            Log(data, c, "Approval", $"درخواست تأیید{(stage is null ? "" : $" مرحلهٔ «{stage.Name}»")} برای {name}", string.Join("، ", items), userId, nowUtc);
            Notify(data, c, [command.ApproverUserId], $"درخواست تأیید پرونده {c.Code}", c.Subject, $"fu-approval:{approval.Id}", nowUtc);
            return new FollowUpActionResult(c.Id, "درخواست تأیید ارسال شد.");
        });

    public FollowUpActionResult DecideApproval(Guid userId, OrganizationSelection organization, Guid caseId, Guid approvalId, DecideFollowUpApprovalCommand command,
        DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        return store.Write(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId);
            if (!Has(snapshot, c.CompanyId, P.Approve)) throw new UnauthorizedAccessException("FollowUp.Approve permission is required.");
            var approval = data.Find<FollowUpApproval>(x => x.Id == approvalId && x.CaseId == c.Id).SingleOrDefault() ?? throw new KeyNotFoundException("درخواست تأیید پیدا نشد.");
            if (approval.ApproverUserId != userId && !Supervises(snapshot, c.CompanyId))
                throw new UnauthorizedAccessException("فقط تأییدکنندهٔ تعیین‌شده تصمیم می‌گیرد.");
            if (approval.RequestedByUserId == userId) throw new UnauthorizedAccessException("درخواست‌کننده نمی‌تواند درخواست خود را تأیید کند.");
            var correctionDue = command.Decision == ApprovalDecision.NeedsCorrection
                ? RequiredTime(command.CorrectionDate, command.CorrectionTime, "مهلت اصلاح", nowUtc) : (DateTimeOffset?)null;
            if (command.CorrectionOwnerUserId is { } owner) RequireBranchUser(data, c, owner, nowUtc, "مسئول اصلاح");
            var passed = (command.PassedItems ?? []).Where(x => approval.ReviewItemList().Contains(x)).ToList();
            approval.Decide(command.Decision, command.Note ?? string.Empty, passed, command.CorrectionOwnerUserId, correctionDue, userId, nowUtc);
            var stage = approval.StageId is { } sid ? data.Find<FollowUpStage>(x => x.Id == sid).SingleOrDefault() : null;
            var docs = data.Find<FollowUpDocument>(x => x.CaseId == c.Id && x.StageId == approval.StageId && x.Status == FollowUpDocumentStatus.PendingApproval);
            var label = command.Decision switch { ApprovalDecision.Approved => "تأیید شد", ApprovalDecision.NeedsCorrection => "نیازمند اصلاح", _ => "رد شد" };
            foreach (var doc in docs) doc.Review(command.Decision == ApprovalDecision.Approved, userId, approval.DecisionNote, nowUtc);
            Log(data, c, "Approval", $"تصمیم تأیید{(stage is null ? "" : $" مرحلهٔ «{stage.Name}»")}: {label}", approval.DecisionNote, userId, nowUtc);
            if (command.Decision == ApprovalDecision.Approved)
            {
                if (stage is { IsWorking: true } && data.Find<FollowUpChecklistItem>(x => x.StageId == stage.Id && !x.IsDone).Count == 0)
                {
                    stage.Complete(userId, nowUtc);
                    Log(data, c, "Stage", $"مرحلهٔ «{stage.Name}» تکمیل شد", "پس از تأیید", userId, nowUtc);
                }
                Recompute(data, c, userId, nowUtc, "تأیید مرحله");
                return new FollowUpActionResult(c.Id, "تصمیم «تأیید» ثبت شد.");
            }
            // Correction or rejection sends the stage back: the items that did not pass are reopened, so the progress drops for a recorded reason.
            if (stage is not null)
            {
                foreach (var item in data.Find<FollowUpChecklistItem>(x => x.StageId == stage.Id && x.IsDone).Where(x => approval.ReviewItemList().Contains(x.Title) && !passed.Contains(x.Title)))
                    item.Uncheck();
                var list = data.Find<FollowUpChecklistItem>(x => x.StageId == stage.Id);
                var progress = list.Count == 0 ? 50 : (int)Math.Round(list.Count(x => x.IsDone) * 100.0 / list.Count);
                stage.Return($"{label}: {approval.DecisionNote}", progress, correctionDue, nowUtc);
                if (command.CorrectionOwnerUserId is { } correctionOwner) stage.AssignResponsible(correctionOwner);
            }
            c.BackToWork();
            var nextOwner = command.CorrectionOwnerUserId ?? c.OwnerUserId ?? userId;
            c.PlanNextAction(command.Decision == ApprovalDecision.NeedsCorrection ? "اصلاح: " + approval.DecisionNote : "بررسی رد تأیید و تصمیم بعدی",
                correctionDue ?? nowUtc.AddHours(8), nextOwner, nowUtc);
            Recompute(data, c, userId, nowUtc, $"برگشت برای اصلاح ({label})");
            Notify(data, c, [nextOwner], $"پرونده {c.Code}: {label}", approval.DecisionNote ?? "", $"fu-decision:{approval.Id}", nowUtc);
            return new FollowUpActionResult(c.Id, $"تصمیم «{label}» ثبت شد و مرحله برای اصلاح برگشت.");
        });
    }

    // ───────────── ۸. بستن و بازگشایی ─────────────

    public FollowUpActionResult Close(Guid userId, OrganizationSelection organization, Guid caseId, CloseFollowUpCommand command, FollowUpDocumentUpload? customerApproval,
        DateTimeOffset nowUtc)
    {
        Guid? nextCase = null;
        var result = Act(userId, organization, caseId, P.Close, (data, _, c) =>
        {
            if (string.IsNullOrWhiteSpace(command.Outcome)) throw new InvalidOperationException("نتیجهٔ نهایی را انتخاب کنید.");
            if (customerApproval is { Content.Length: > 0 } file)
            {
                var doc = AddDocument(data, c, null, FollowUpDocumentKind.CustomerApproval, "تأییدیهٔ مشتری", false, file, userId, nowUtc);
                doc.Review(true, userId, "دریافت در بستن پرونده", nowUtc);
            }
            var template = data.Find<FollowUpTemplate>(x => x.Id == c.TemplateId).SingleOrDefault();
            var stages = Stages(data, c.Id);
            var documents = data.Find<FollowUpDocument>(x => x.CaseId == c.Id);
            if (customerApproval is { Content.Length: > 0 }) documents = [.. documents, .. data.Table<FollowUpDocument>().Where(x => x.CaseId == c.Id && !documents.Contains(x))];
            var checks = CloseChecks(data, c, template, stages, documents, data.Find<FollowUpApproval>(x => x.CaseId == c.Id),
                data.Find<FollowUpReferral>(x => x.CaseId == c.Id), data.Find<CrmActivity>(x => x.RelatedKind == ActivityRelatedKind.FollowUpCase && x.RelatedId == c.Id));
            var failed = checks.Where(x => !x.Passed).ToList();
            if (failed.Count > 0) throw new InvalidOperationException("پرونده هنوز آمادهٔ بستن نیست: " + string.Join(" · ", failed.Select(x => x.Label + (x.Hint is null ? "" : $" ({x.Hint})"))));
            c.Close(command.Outcome!, command.Note, userId, nowUtc);
            Log(data, c, "Closed", $"پرونده بسته شد: {c.Outcome}", c.OutcomeNote, userId, nowUtc);
            return new FollowUpActionResult(c.Id, "پرونده بسته شد.");
        });
        if (command.StartNextCase)
        {
            if (command.NextTemplateId is null || command.NextOwnerUserId is null) throw new InvalidOperationException("برای ادامهٔ ارتباط، الگو و مسئول پرونده بعدی را انتخاب کنید.");
            var source = GetCase(userId, organization, caseId, nowUtc)!;
            var template = store.Read(data => data.Find<FollowUpTemplate>(x => x.Id == command.NextTemplateId).SingleOrDefault()) ?? throw new InvalidOperationException("الگوی پرونده بعدی پیدا نشد.");
            var created = Create(userId, organization, new CreateFollowUpCommand($"{template.Name}: {source.Subject}", source.CustomerId, null, template.CaseType, template.Id,
                source.BranchId, command.NextOwnerUserId, null, source.Channel, source.Priority, null, source.RelatedKind, null, null, null, null, null, null, null,
                null, null, Guid.NewGuid(), true, $"ادامهٔ پرونده {source.Code}", source.Id), nowUtc);
            nextCase = created.CaseId;
            store.Write(data =>
            {
                var c = data.Find<FollowUpCase>(x => x.Id == caseId).Single();
                var next = data.Find<FollowUpCase>(x => x.Id == created.CaseId).Single();
                Log(data, c, "Linked", $"پیگیری ادامه یافت در پرونده {next.Code}", template.Name, userId, nowUtc);
                return 0;
            });
        }
        return result with { NewCaseId = nextCase, Message = nextCase is null ? result.Message : "پرونده بسته شد و پیگیری بعدی آغاز شد." };
    }

    public FollowUpActionResult Reopen(Guid userId, OrganizationSelection organization, Guid caseId, ReopenFollowUpCommand command, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Reopen, (data, _, c) =>
        {
            if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل بازگشایی الزامی است.");
            RequireBranchUser(data, c, command.OwnerUserId, nowUtc, "مسئول پرونده");
            var due = RequiredTime(command.DueDate, command.DueTime, "مهلت جدید", nowUtc);
            var previous = $"نتیجهٔ قبلی: {c.Outcome} · بسته‌شده در {(c.ClosedAtUtc is { } closed ? TehranTime.Format(closed) : "—")}";
            c.Reopen(command.OwnerUserId, due, nowUtc);
            c.PlanNextAction("رسیدگی به بازگشایی: " + (command.Reason.Length > 150 ? command.Reason[..150] : command.Reason), due, command.OwnerUserId, nowUtc);
            Log(data, c, "Reopened", $"پرونده بازگشایی شد (بار {c.ReopenCount})", $"دلیل: {command.Reason} · {previous}", userId, nowUtc);
            Notify(data, c, [command.OwnerUserId], $"پرونده {c.Code} بازگشایی شد", command.Reason, $"fu-reopen:{c.Id}:{c.ReopenCount}", nowUtc);
            return new FollowUpActionResult(c.Id, "پرونده بازگشایی شد؛ سابقهٔ بستن قبلی در تاریخچه باقی است.");
        });

    public FollowUpActionResult Cancel(Guid userId, OrganizationSelection organization, Guid caseId, string? reason, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Close, (data, _, c) =>
        {
            c.Cancel(reason ?? string.Empty);
            foreach (var referral in data.Find<FollowUpReferral>(x => x.CaseId == c.Id && x.Status == ReferralStatus.Pending)) referral.Cancel(nowUtc);
            Log(data, c, "Cancelled", "پرونده لغو شد", reason, userId, nowUtc);
            return new FollowUpActionResult(c.Id, "پرونده لغو شد (لغو به معنی پیشرفت ۱۰۰٪ نیست).");
        });

    // ───────────── اقلام، قواعد، ارجاع مستقیم و ادغام ─────────────

    public FollowUpActionResult AddItem(Guid userId, OrganizationSelection organization, Guid caseId, FollowUpPartInput item, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var line = new FollowUpItem(Guid.NewGuid(), c.Id, item.PartCode ?? string.Empty, item.Description, item.Quantity ?? 1, item.Unit);
            data.Append(line);
            Log(data, c, "Item", $"قطعه {line.PartCode} افزوده شد", $"{line.Quantity:0.##} {line.Unit} · {line.Description}", userId, nowUtc);
            return new FollowUpActionResult(c.Id, "قطعه افزوده شد.");
        });

    public FollowUpActionResult UpdateItem(Guid userId, OrganizationSelection organization, Guid caseId, Guid itemId, decimal deliveredQuantity, FollowUpItemStatus status,
        string? note, bool remove, DateTimeOffset nowUtc) => Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var line = data.Find<FollowUpItem>(x => x.Id == itemId && x.CaseId == c.Id && !x.IsRemoved).SingleOrDefault() ?? throw new KeyNotFoundException("قطعه پیدا نشد.");
            if (remove)
            {
                line.Remove();
                Log(data, c, "Item", $"قطعه {line.PartCode} حذف شد", note, userId, nowUtc);
                return new FollowUpActionResult(c.Id, "قطعه از پرونده حذف شد.");
            }
            line.RecordDelivery(deliveredQuantity, status, note);
            Log(data, c, "Item", $"وضعیت قطعه {line.PartCode}: {line.Status}", $"تحویل‌شده {line.DeliveredQuantity:0.##} از {line.Quantity:0.##} · {note}", userId, nowUtc);
            return new FollowUpActionResult(c.Id, "وضعیت قطعه ثبت شد؛ تحویل جزئی پرونده را نمی‌بندد.");
        });

    /// <summary>Runs one «شرط و اقدام» of the template when its condition happened (e.g. «موجودی ناکافی بود» → «ایجاد وظیفه تأمین»).</summary>
    public FollowUpActionResult TriggerRule(Guid userId, OrganizationSelection organization, Guid caseId, int ruleIndex, DateTimeOffset nowUtc)
    {
        var snapshot = Snapshot(userId);
        var (customerId, owner, rule) = store.Read(data =>
        {
            var c = Case(data, snapshot, organization, userId, caseId, P.Update);
            var rules = data.Find<FollowUpTemplate>(x => x.Id == c.TemplateId).SingleOrDefault()?.RuleList() ?? [];
            if (ruleIndex < 0 || ruleIndex >= rules.Count) throw new KeyNotFoundException("قاعده پیدا نشد.");
            return (c.CustomerId, c.OwnerUserId ?? userId, rules[ruleIndex]);
        });
        var action = rule.Action.Trim();
        Guid? task = null;
        if (action.StartsWith("ایجاد وظیفه", StringComparison.Ordinal))
        {
            var due = nowUtc.AddHours(8);
            task = activities.SaveActivity(userId, organization, customerId, null, new SaveActivityCommand(ActivityType.Task, action, $"به دلیل: {rule.Condition}", null, owner,
                TehranTime.Date(due), TehranTime.Clock(due), null, null, null, null, null, ActivityPriority.High, null, ActivityRelatedKind.FollowUpCase, caseId, null, null,
                Guid.NewGuid()), nowUtc).Id;
        }
        return Act(userId, organization, caseId, P.Update, (data, _, c) =>
        {
            var message = $"قاعده اجرا شد: {action}";
            if (task is { } taskId) data.Append(new FollowUpActivityLink(taskId, c.Id, null, FollowUpChannel.InPerson, null));
            else if (action.StartsWith("کنار گذاشتن مرحله", StringComparison.Ordinal) || action.StartsWith("شروع مرحله", StringComparison.Ordinal) ||
                     action.StartsWith("فعال‌سازی مرحله", StringComparison.Ordinal))
            {
                var name = action.Split(' ', 3).Last().Trim().Trim('«', '»');
                var stage = Stages(data, c.Id).FirstOrDefault(x => x.Name == name || name.Contains(x.Name)) ?? throw new InvalidOperationException($"مرحلهٔ «{name}» در پرونده نیست.");
                if (action.StartsWith("کنار گذاشتن", StringComparison.Ordinal)) stage.Skip(rule.Condition);
                else
                {
                    var policy = c.SlaPolicyId is { } pid ? data.Find<SlaPolicy>(x => x.Id == pid).SingleOrDefault() : null;
                    stage.Activate(c.OwnerUserId, StageDue(policy, stage.DurationHours, nowUtc), nowUtc);
                }
            }
            else if (action.Contains("اولویت", StringComparison.Ordinal) && c.Priority < FollowUpPriority.Critical)
                c.ChangeSubject(c.Subject, c.Priority + 1, rule.Condition);
            Log(data, c, "Rule", $"شرط «{rule.Condition}» رخ داد", $"اقدام: {action}", userId, nowUtc);
            Recompute(data, c, userId, nowUtc, $"قاعدهٔ «{rule.Condition}»");
            return new FollowUpActionResult(c.Id, message);
        });
    }

    public FollowUpActionResult Reassign(Guid userId, OrganizationSelection organization, Guid caseId, Guid ownerUserId, string? branchId, string? reason, DateTimeOffset nowUtc) =>
        Act(userId, organization, caseId, P.Assign, (data, snapshot, c) =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("دلیل تغییر مسئول الزامی است.");
            if (!string.IsNullOrWhiteSpace(branchId) && !AccountGuard.Same(branchId, c.BranchId))
            {
                if (!snapshot.AllowsRecord(c.CompanyId, P.Assign, branchId.Trim(), null)) throw new UnauthorizedAccessException("انتقال به این شعبه مجاز نیست.");
                var before = c.BranchId;
                c.MoveBranch(branchId.Trim(), null);
                Log(data, c, "Branch", $"انتقال پرونده از شعبهٔ {before} به {c.BranchId}", "شناسه، سوابق و مهلت‌ها بدون تغییر ماند.", userId, nowUtc);
            }
            RequireBranchUser(data, c, ownerUserId, nowUtc, "مسئول پرونده");
            var names = AccountGuard.UserNames(data, [ownerUserId, c.OwnerUserId ?? Guid.Empty]);
            var previous = c.OwnerUserId is { } p ? names.GetValueOrDefault(p, "—") : "بدون مسئول";
            var previousOwner = c.OwnerUserId;
            c.AssignOwner(ownerUserId, c.QueueId);
            HandOverStages(data, c, previousOwner, ownerUserId);
            if (c.NextActionOwnerUserId == previousOwner && c.NextAction is not null && c.NextActionAtUtc is { } at && at > nowUtc) c.PlanNextAction(c.NextAction, at, ownerUserId, nowUtc);
            Log(data, c, "Owner", $"مسئول پرونده: {previous} → {names.GetValueOrDefault(ownerUserId, "—")}", reason, userId, nowUtc);
            Notify(data, c, [ownerUserId], $"پرونده {c.Code} به شما سپرده شد", $"{c.Subject} — {reason}", $"fu-owner:{c.Id}:{ownerUserId}:{nowUtc:yyyyMMddHHmm}", nowUtc);
            return new FollowUpActionResult(c.Id, "مسئول پرونده تغییر کرد.");
        });

    /// <summary>Merging keeps both histories: the duplicate is cancelled with a pointer to the case that continues.</summary>
    public FollowUpActionResult Merge(Guid userId, OrganizationSelection organization, Guid sourceCaseId, string? targetCode, string? reason, DateTimeOffset nowUtc) =>
        Act(userId, organization, sourceCaseId, P.Assign, (data, snapshot, source) =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("دلیل ادغام الزامی است.");
            var target = data.Find<FollowUpCase>(x => x.CompanyId == source.CompanyId && x.Code == (targetCode ?? "").Trim()).SingleOrDefault(x => CanSee(data, snapshot, organization, userId, x))
                ?? throw new InvalidOperationException("پرونده مقصد پیدا نشد.");
            if (!target.IsOpen) throw new InvalidOperationException("پرونده مقصد باید باز باشد.");
            if (target.CustomerId != source.CustomerId) throw new InvalidOperationException("فقط پرونده‌های یک مشتری ادغام می‌شوند.");
            source.MergeInto(target.Id);
            foreach (var referral in data.Find<FollowUpReferral>(x => x.CaseId == source.Id && x.Status == ReferralStatus.Pending)) referral.Cancel(nowUtc);
            Log(data, source, "Merged", $"در پرونده {target.Code} ادغام شد", reason, userId, nowUtc);
            Log(data, target, "Merged", $"پرونده {source.Code} در این پرونده ادغام شد", $"{reason} · سوابق پرونده {source.Code} در همان پرونده محفوظ است.", userId, nowUtc);
            return new FollowUpActionResult(target.Id, $"پرونده {source.Code} در {target.Code} ادغام شد.");
        });

    // ───────────── پایش سرور: مهلت‌ها، هشدار، یادآوری و تخصیص ─────────────

    /// <summary>
    /// Runs on the server (not in a browser): escalates breached due times level by level, reminds owners of late next actions and
    /// stages, reports late referrals and assigns waiting cases. Every message has a dedup key, so re-running never sends twice.
    /// </summary>
    public FollowUpMonitorResult RunMonitor(DateTimeOffset nowUtc) => store.Write(data =>
    {
        int escalations = 0, reminders = 0, late = 0, assigned = 0;
        var open = data.Find<FollowUpCase>(x => x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled);
        var policies = data.Find<SlaPolicy>(x => x.IsActive).ToDictionary(x => x.Id);
        foreach (var c in open)
        {
            if (c.Status == FollowUpStatus.AwaitingAssignment)
            {
                var suggestion = Suggest(data, c.CompanyId, c.BranchId, c.CaseType, c.PartFamily, c.Language, nowUtc, c.QueueId);
                if (suggestion.UserId is { } user)
                {
                    c.AssignOwner(user, suggestion.QueueId);
                    if (suggestion.QueueId is { } q) MarkAssigned(data, q, user, nowUtc);
                    if (c.NextActionAtUtc is null) c.PlanNextAction("پاسخ اولیه به مشتری", c.FirstResponseDueAtUtc is { } f && f > nowUtc ? f : nowUtc.AddHours(1), user, nowUtc);
                    Log(data, c, "Owner", $"تخصیص خودکار به {suggestion.UserName}", suggestion.Explanation, null, nowUtc);
                    Notify(data, c, [user], $"پرونده {c.Code} به شما سپرده شد", c.Subject, $"fu-owner:{c.Id}:{user}", nowUtc);
                    assigned++;
                }
                else if (nowUtc - c.OpenedAtUtc > TimeSpan.FromMinutes(30))
                    reminders += Notify(data, c, Supervisors(data, c, nowUtc), $"پرونده {c.Code} هنوز مسئول ندارد", c.Subject, $"fu-unassigned:{c.Id}", nowUtc);
            }
            var policy = c.SlaPolicyId is { } pid ? policies.GetValueOrDefault(pid) : null;
            if (policy is not null && !c.IsPaused && !c.IsWaiting)
            {
                var due = c.FirstRespondedAtUtc is null && c.FirstResponseDueAtUtc is { } first && first < (c.ResolutionDueAtUtc ?? first) ? first : c.ResolutionDueAtUtc;
                if (due is { } d)
                {
                    var steps = policy.EscalationSteps();
                    var reached = steps.Count(x => d.AddMinutes(x.OffsetMinutes) <= nowUtc);
                    for (var level = c.EscalationLevel; level < reached; level++)
                    {
                        var step = steps[level];
                        var stageOwners = data.Find<FollowUpStage>(x => x.CaseId == c.Id && x.ResponsibleUserId != null).Where(x => x.IsWorking).Select(x => x.ResponsibleUserId!.Value).ToList();
                        IEnumerable<Guid> targets = step.Target switch
                        {
                            EscalationTarget.StageOwner => stageOwners.Count > 0 ? stageOwners : c.OwnerUserId is { } o ? [o] : [],
                            EscalationTarget.CaseOwner => c.OwnerUserId is { } o2 ? [o2] : [],
                            EscalationTarget.Supervisor => Supervisors(data, c, nowUtc),
                            _ => Managers(data, c, nowUtc)
                        };
                        var when = step.OffsetMinutes < 0 ? $"{-step.OffsetMinutes} دقیقه تا سررسید" : step.OffsetMinutes == 0 ? "در زمان سررسید" : $"{step.OffsetMinutes} دقیقه پس از سررسید";
                        Notify(data, c, targets, $"هشدار مهلت پرونده {c.Code} ({when})", $"{c.Subject} — سررسید {TehranTime.Format(d)}", $"fu-esc:{c.Id}:{d:yyyyMMddHHmm}:{level}", nowUtc);
                        Log(data, c, "Escalation", $"هشدار سطح {level + 1}: {when}", $"گیرنده: {step.Target} · مالک پرونده تغییر نکرد.", null, nowUtc);
                        escalations++;
                    }
                    c.RaiseEscalation(reached);
                }
            }
            if (c.NextActionAtUtc is { } next && next <= nowUtc && c.NextActionOwnerUserId is { } nextOwner)
                reminders += Notify(data, c, [nextOwner], $"موعد اقدام پرونده {c.Code} گذشته است", $"{c.NextAction} — {TehranTime.Format(next)}", $"fu-next:{c.Id}:{next:yyyyMMddHHmm}", nowUtc);
            foreach (var stage in data.Find<FollowUpStage>(x => x.CaseId == c.Id && x.DueAtUtc < nowUtc).Where(x => x.IsWorking && x.ResponsibleUserId is not null))
                reminders += Notify(data, c, [stage.ResponsibleUserId!.Value], $"مهلت مرحلهٔ «{stage.Name}» پرونده {c.Code} گذشته است", c.Subject,
                    $"fu-stage:{stage.Id}:{stage.DueAtUtc:yyyyMMddHHmm}", nowUtc);
        }
        foreach (var referral in data.Find<FollowUpReferral>(x => x.Status == ReferralStatus.Pending && x.AcceptDueAtUtc < nowUtc && !x.LateReminderSent))
        {
            var c = data.Find<FollowUpCase>(x => x.Id == referral.CaseId).Single();
            Notify(data, c, Supervisors(data, c, nowUtc).Append(referral.FromUserId), $"ارجاع پرونده {c.Code} در مهلت پذیرفته نشد",
                $"{referral.Reason} — مسئول فعلی همچنان پاسخ‌گوست.", $"fu-ref-late:{referral.Id}", nowUtc);
            referral.MarkLateReminderSent();
            Log(data, c, "Referral", "مهلت پذیرش ارجاع گذشت", "سرپرست و فرستنده مطلع شدند؛ مسئولیت تغییر نکرد.", null, nowUtc);
            late++;
        }
        return new FollowUpMonitorResult(escalations, reminders, late, assigned);
    });
}
