using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Notifications;
using Crm.Domain.Sales;
using Crm.Domain.SelfService;
using static Crm.Application.Services.AccountPermissions;

namespace Crm.Application.Services;

public interface IAccountActivityService
{
    ActivityPanelDto GetActivities(Guid userId, OrganizationSelection organization, Guid accountId, ActivityFilter filter, DateTimeOffset nowUtc, int pastLimit = 20);
    AccountFormOptions GetFormOptions(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc);
    ActivityFormDto GetActivityForm(Guid userId, OrganizationSelection organization, Guid accountId, Guid? activityId, ActivityType type, DateTimeOffset nowUtc);
    ActivityDetailsDto GetActivity(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, DateTimeOffset nowUtc);
    ActivityItemDto SaveActivity(Guid userId, OrganizationSelection organization, Guid accountId, Guid? activityId, SaveActivityCommand command, DateTimeOffset nowUtc);
    ActivityItemDto Reschedule(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, RescheduleActivityCommand command, DateTimeOffset nowUtc);
    (ActivityItemDto Completed, ActivityItemDto? FollowUp) Complete(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId,
        CompleteActivityCommand command, DateTimeOffset nowUtc);
    ActivityItemDto Cancel(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, CancelActivityCommand command, DateTimeOffset nowUtc);
    IReadOnlyList<TimelineItemDto> GetTimeline(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc, int take = 60);
}

/// <summary>
/// Calls, meetings and tasks of an account. The activity panel also shows (read-only) the mobile visits and the logged
/// opportunity activities of the account, each from its own table, so nothing is copied and nothing is shown twice.
/// </summary>
public sealed class AccountActivityService(ICrmDataStore store, IAccessSnapshotService access) : IAccountActivityService
{
    public ActivityPanelDto GetActivities(Guid userId, OrganizationSelection organization, Guid accountId, ActivityFilter filter, DateTimeOffset nowUtc,
        int pastLimit = 20)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, ActivityRead);
            var activities = data.Find<CrmActivity>(x => x.CustomerId == account.Id);
            if (filter.ContactId is { } contact) activities = activities.Where(x => x.ContactId == contact).ToList();
            var contacts = data.Find<CustomerContact>(x => x.CustomerId == account.Id).ToDictionary(x => x.Id, x => x.FullName);
            var users = AccountGuard.UserNames(data, activities.SelectMany(x => new[] { x.OwnerUserId, x.CompletedByUserId ?? Guid.Empty }));
            var related = RelatedNames(data, account, activities);
            var items = activities.Select(x => Map(x, contacts, users, related, snapshot, account, userId, nowUtc)).ToList();

            // Read-only engagement from other modules (mobile visits, logged opportunity activities).
            if (filter.Type is null && filter.ContactId is null)
            {
                var visits = data.Find<MobileVisit>(x => x.CustomerId == account.Id);
                var visitOwners = AccountGuard.UserNames(data, visits.Select(x => x.OwnerUserId));
                items.AddRange(visits.Select(v => new ActivityItemDto(v.Id, "Visit", null, v.Purpose, v.Outcome, visitOwners.GetValueOrDefault(v.OwnerUserId, "—"),
                    v.OwnerUserId, null, TehranTime.Format(v.PlannedAtUtc), v.CompletedAtUtc ?? v.PlannedAtUtc,
                    v.Status switch { VisitStatus.Completed => "Completed", VisitStatus.Cancelled => "Cancelled", _ => "Planned" },
                    v.Status is VisitStatus.Planned or VisitStatus.CheckedIn && v.PlannedAtUtc < nowUtc.AddHours(-2), "بازدید حضوری (موبایل)", "/mobile",
                    string.IsNullOrEmpty(v.Outcome) ? null : v.Outcome, null, false, false, v.Version)));
                var opportunityIds = data.Find<Opportunity>(x => x.CustomerId == account.Id).Select(x => x.Id).ToArray();
                if (opportunityIds.Length > 0 && AccountGuard.Allows(snapshot, account, "Opportunity.Read"))
                {
                    var logged = data.Find<OpportunityActivity>(x => opportunityIds.Contains(x.OpportunityId));
                    var actors = AccountGuard.UserNames(data, logged.Select(x => x.ActorUserId));
                    items.AddRange(logged.Select(x => new ActivityItemDto(x.Id, "OpportunityLog", null, x.Subject, null, actors.GetValueOrDefault(x.ActorUserId, "—"),
                        x.ActorUserId, null, TehranTime.Format(x.OccurredAtUtc), x.OccurredAtUtc, "Completed", false, "فعالیت ثبت‌شده در فرصت",
                        $"/opportunities/{x.OpportunityId}", x.Outcome, x.Type.ToString(), false, false, x.Version)));
                }
            }

            IEnumerable<ActivityItemDto> query = items;
            if (filter.Type is { } type) query = query.Where(x => x.Type == type);
            if (filter.OwnerUserId is { } owner) query = query.Where(x => x.OwnerUserId == owner);
            if (filter.From is { } from) query = query.Where(x => TehranTime.Today(x.SortAtUtc) >= from);
            if (filter.To is { } to) query = query.Where(x => TehranTime.Today(x.SortAtUtc) <= to);
            query = filter.State switch
            {
                "overdue" => query.Where(x => x.IsOverdue),
                "planned" => query.Where(x => x.State == "Planned"),
                "completed" => query.Where(x => x.State == "Completed"),
                "cancelled" => query.Where(x => x.State == "Cancelled"),
                _ => query
            };
            var list = query.ToList();
            var overdue = list.Where(x => x.IsOverdue).OrderBy(x => x.SortAtUtc).ToList();
            var upcoming = list.Where(x => x.State == "Planned" && !x.IsOverdue).OrderBy(x => x.SortAtUtc).ToList();
            var past = list.Where(x => x.State != "Planned").OrderByDescending(x => x.SortAtUtc).Take(pastLimit).ToList();
            return new ActivityPanelDto(overdue, upcoming, past, list.Count, filter, AccountGuard.Owners(data, account, nowUtc));
        });
    }

    public AccountFormOptions GetFormOptions(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data => Options(data, snapshot, AccountGuard.Account(data, snapshot, organization, accountId), userId, nowUtc));
    }

    public ActivityFormDto GetActivityForm(Guid userId, OrganizationSelection organization, Guid accountId, Guid? activityId, ActivityType type, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, activityId is null ? ActivityCreate : ActivityRead);
            var options = Options(data, snapshot, account, userId, nowUtc);
            if (activityId is not { } id)
            {
                AccountGuard.EnsureMutable(account);
                // Defaults: next round quarter hour today, 15-minute call, 1-hour meeting, task due tomorrow 10:00.
                var start = type == ActivityType.Task ? TehranTime.ToUtc(TehranTime.Date(nowUtc.AddDays(1)), "10:00", "")!.Value : RoundUp(nowUtc);
                var primary = data.Find<CustomerContact>(x => x.CustomerId == account.Id && x.IsActive && x.IsPrimary).Select(x => (Guid?)x.Id).FirstOrDefault();
                return new ActivityFormDto(account.Id, null, new SaveActivityCommand(type, null, null, primary, userId, TehranTime.Date(start), TehranTime.Clock(start),
                    type == ActivityType.Meeting ? TehranTime.Date(start.AddHours(1)) : null, type == ActivityType.Meeting ? TehranTime.Clock(start.AddHours(1)) : null,
                    type == ActivityType.Call ? 15 : null, type == ActivityType.Call ? CallDirection.Outbound : null, null, ActivityPriority.Normal,
                    type == ActivityType.Task ? null : 15, ActivityRelatedKind.None, null, null, null, Guid.NewGuid()), options);
            }
            var activity = data.Find<CrmActivity>(x => x.Id == id && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("فعالیت پیدا نشد.");
            if (!CanModify(snapshot, account, activity, userId)) throw new UnauthorizedAccessException("ویرایش این فعالیت مجاز نیست.");
            var participants = data.Find<ActivityParticipant>(x => x.ActivityId == activity.Id);
            return new ActivityFormDto(account.Id, activity.Id, new SaveActivityCommand(activity.Type, activity.Subject, activity.Description, activity.ContactId,
                activity.OwnerUserId, TehranTime.Date(activity.StartAtUtc), TehranTime.Clock(activity.StartAtUtc),
                activity.EndAtUtc is { } end ? TehranTime.Date(end) : null, activity.EndAtUtc is { } e ? TehranTime.Clock(e) : null, activity.DurationMinutes,
                activity.Direction, activity.Location, activity.Priority, activity.ReminderMinutesBefore, activity.RelatedKind, activity.RelatedId,
                participants.Where(x => x.Kind == ParticipantKind.Contact).Select(x => x.ParticipantId).ToList(),
                participants.Where(x => x.Kind == ParticipantKind.User).Select(x => x.ParticipantId).ToList(), Guid.NewGuid(), activity.Version), options);
        });
    }

    public ActivityDetailsDto GetActivity(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, ActivityRead);
            var activity = data.Find<CrmActivity>(x => x.Id == activityId && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("فعالیت پیدا نشد.");
            var contacts = data.Find<CustomerContact>(x => x.CustomerId == account.Id).ToDictionary(x => x.Id, x => x.FullName);
            var users = AccountGuard.UserNames(data, [activity.OwnerUserId, activity.CompletedByUserId ?? Guid.Empty, activity.CreatedByUserId]);
            var item = Map(activity, contacts, users, RelatedNames(data, account, [activity]), snapshot, account, userId, nowUtc);
            var previous = activity.FollowUpOfId is { } prior ? data.Find<CrmActivity>(x => x.Id == prior).Select(x => x.Subject).FirstOrDefault() : null;
            return new ActivityDetailsDto(item, new CrmActivitySnapshot(activity.Type, activity.Status, TehranTime.Date(activity.StartAtUtc), TehranTime.Clock(activity.StartAtUtc),
                activity.EndAtUtc is { } end ? TehranTime.Date(end) : null, activity.EndAtUtc is { } e ? TehranTime.Clock(e) : null, activity.DurationMinutes,
                activity.Direction, activity.Location, activity.Priority, activity.ReminderMinutesBefore, activity.CallResult,
                activity.CompletedByUserId is { } by ? users.GetValueOrDefault(by) : null, activity.CompletedAtUtc is { } at ? TehranTime.Format(at) : null,
                activity.CancelReason, previous),
                data.Find<ActivityParticipant>(x => x.ActivityId == activity.Id).Select(x => x.Name).ToList(), account.Id);
        });
    }

    public ActivityItemDto SaveActivity(Guid userId, OrganizationSelection organization, Guid accountId, Guid? activityId, SaveActivityCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        if (!Enum.IsDefined(command.Type)) throw new InvalidOperationException("نوع فعالیت معتبر نیست.");
        var start = TehranTime.ToUtc(command.StartDate, command.StartTime, command.Type == ActivityType.Task ? "مهلت" : "زمان شروع") ??
            throw new InvalidOperationException(command.Type == ActivityType.Task ? "مهلت وظیفه الزامی است." : "زمان شروع الزامی است.");
        var end = TehranTime.ToUtc(command.EndDate ?? command.StartDate, command.EndTime, "زمان پایان");
        var schedule = new ActivitySchedule(start, command.Type == ActivityType.Meeting ? end : null, command.DurationMinutes, command.Direction,
            command.Location, command.Priority, command.ReminderMinutesBefore);
        return store.Write(data =>
        {
            if (activityId is null && ClientOperations.Existing(data, userId, command.OperationId) is { } replayed)
                return Item(data, snapshot, organization, accountId, replayed, userId, nowUtc);
            var account = AccountGuard.Account(data, snapshot, organization, accountId, activityId is null ? ActivityCreate : ActivityRead);
            AccountGuard.EnsureMutable(account);
            ValidateLinks(data, snapshot, account, command, userId, nowUtc);
            CrmActivity activity;
            if (activityId is { } id)
            {
                activity = data.Find<CrmActivity>(x => x.Id == id && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("فعالیت پیدا نشد.");
                if (!CanModify(snapshot, account, activity, userId)) throw new UnauthorizedAccessException("ویرایش این فعالیت مجاز نیست.");
                if (activity.Version != command.ExpectedVersion) throw new InvalidOperationException("فعالیت تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
                activity.Update(command.Subject ?? string.Empty, command.Description, command.ContactId, command.OwnerUserId, schedule, command.RelatedKind, command.RelatedId);
                foreach (var old in data.Find<ActivityParticipant>(x => x.ActivityId == activity.Id)) data.ActivityParticipants.Remove(old);
                AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"{Label(activity.Type)} ویرایش شد", activity.Subject, userId, nowUtc, activity.Id.ToString("N"));
            }
            else
            {
                activity = new CrmActivity(Guid.NewGuid(), account.CompanyId, account.Id, command.Type, command.Subject ?? string.Empty, command.Description,
                    command.ContactId, command.OwnerUserId, schedule, command.RelatedKind, command.RelatedId, userId);
                data.CrmActivities.Add(activity);
                ClientOperations.Record(data, userId, command.OperationId, "Activity", activity.Id);
                if (command.AlreadyDone)
                {
                    // Logging something that already happened: the outcome is recorded at once, no reminder is scheduled.
                    if (activity.StartAtUtc > nowUtc.AddMinutes(5)) throw new InvalidOperationException("فعالیت انجام‌شده نمی‌تواند زمان آینده داشته باشد.");
                    activity.Complete(command.Outcome, command.CallResult, userId, nowUtc, command.Type == ActivityType.Call ? command.DurationMinutes : null);
                    AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"{Label(activity.Type)} انجام‌شده ثبت شد", $"{activity.Subject}: {activity.Outcome}",
                        userId, nowUtc, activity.Id.ToString("N"));
                }
                else
                    AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"{Label(activity.Type)} برنامه‌ریزی شد", activity.Subject, userId, nowUtc, activity.Id.ToString("N"));
            }
            AddParticipants(data, account, activity, command);
            if (activity.Status == ActivityStatus.Planned) ScheduleReminder(data, account, activity, nowUtc);
            return Item(data, snapshot, organization, account.Id, activity.Id, userId, nowUtc);
        });
    }

    public ActivityItemDto Reschedule(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, RescheduleActivityCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        var start = TehranTime.ToUtc(command.StartDate, command.StartTime, "زمان جدید") ?? throw new InvalidOperationException("زمان جدید الزامی است.");
        var end = TehranTime.ToUtc(command.EndDate ?? command.StartDate, command.EndTime, "زمان پایان");
        return Mutate(snapshot, organization, accountId, activityId, command.ExpectedVersion, userId, nowUtc, (data, account, activity) =>
        {
            var before = TehranTime.Format(activity.StartAtUtc);
            var length = activity.EndAtUtc - activity.StartAtUtc;
            activity.Reschedule(start, activity.Type == ActivityType.Meeting ? end ?? start + length : null);
            ScheduleReminder(data, account, activity, nowUtc);
            AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"زمان {Label(activity.Type)} تغییر کرد",
                $"{activity.Subject}: {before} → {TehranTime.Format(activity.StartAtUtc)}", userId, nowUtc, activity.Id.ToString("N"));
        });
    }

    public (ActivityItemDto Completed, ActivityItemDto? FollowUp) Complete(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId,
        CompleteActivityCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        var nextAt = TehranTime.ToUtc(command.NextDate, command.NextTime ?? "10:00", "زمان اقدام بعدی");
        if (command.NextType is not null && (string.IsNullOrWhiteSpace(command.NextSubject) || nextAt is null))
            throw new InvalidOperationException("برای اقدام بعدی، موضوع و زمان را وارد کنید.");
        if (nextAt is { } at && at <= nowUtc) throw new InvalidOperationException("زمان اقدام بعدی باید در آینده باشد.");
        Guid? followUpId = null;
        var completed = Mutate(snapshot, organization, accountId, activityId, command.ExpectedVersion, userId, nowUtc, (data, account, activity) =>
        {
            activity.Complete(command.Outcome, command.CallResult, userId, nowUtc, command.ActualDurationMinutes);
            WithdrawReminders(data, activity);
            AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"{Label(activity.Type)} انجام شد", $"{activity.Subject}: {activity.Outcome}",
                userId, nowUtc, activity.Id.ToString("N"));
            // Keep the linked opportunity's last-activity / next-action in step (single source: the activity itself is not copied).
            if (activity.RelatedKind == ActivityRelatedKind.Opportunity && activity.RelatedId is { } opportunityId)
            {
                var opportunity = data.Find<Opportunity>(x => x.Id == opportunityId && x.CustomerId == account.Id).SingleOrDefault();
                if (opportunity is { Stage: not (OpportunityStage.Won or OpportunityStage.Lost) })
                    opportunity.RecordActivity(nowUtc, command.NextType is null ? opportunity.NextAction : command.NextSubject,
                        command.NextType is null ? opportunity.NextActionAtUtc is { } next && next > nowUtc ? next : null : nextAt);
            }
            if (command.NextType is { } nextType)
            {
                var start = nextAt!.Value;
                var follow = new CrmActivity(Guid.NewGuid(), account.CompanyId, account.Id, nextType, command.NextSubject!, null, activity.ContactId,
                    activity.OwnerUserId, new ActivitySchedule(start, nextType == ActivityType.Meeting ? start.AddHours(1) : null,
                        nextType == ActivityType.Call ? 15 : null, nextType == ActivityType.Call ? CallDirection.Outbound : null, null, ActivityPriority.Normal,
                        nextType == ActivityType.Task ? null : 15), activity.RelatedKind, activity.RelatedId, userId, activity.Id);
                data.CrmActivities.Add(follow);
                ScheduleReminder(data, account, follow, nowUtc);
                AccountGuard.Log(data, account, CustomerTimelineType.Activity, "اقدام بعدی برنامه‌ریزی شد", follow.Subject, userId, nowUtc, follow.Id.ToString("N"));
                followUpId = follow.Id;
            }
        });
        var followUp = followUpId is { } id ? store.Read(data => Item(data, snapshot, organization, accountId, id, userId, nowUtc)) : null;
        return (completed, followUp);
    }

    public ActivityItemDto Cancel(Guid userId, OrganizationSelection organization, Guid accountId, Guid activityId, CancelActivityCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return Mutate(snapshot, organization, accountId, activityId, command.ExpectedVersion, userId, nowUtc, (data, account, activity) =>
        {
            activity.Cancel(command.Reason ?? string.Empty, userId, nowUtc);
            WithdrawReminders(data, activity);
            AccountGuard.Log(data, account, CustomerTimelineType.Activity, $"{Label(activity.Type)} لغو شد", $"{activity.Subject}: {activity.CancelReason}",
                userId, nowUtc, activity.Id.ToString("N"));
        });
    }

    /// <summary>
    /// Interaction timeline: activities and notes (from their own tables) plus the important sales/service events of the
    /// account's change log. Activity and note log entries are excluded so the same interaction is not listed twice.
    /// </summary>
    public IReadOnlyList<TimelineItemDto> GetTimeline(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc, int take = 60)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId);
            var items = new List<TimelineItemDto>();
            if (AccountGuard.Allows(snapshot, account, ActivityRead))
            {
                var activities = data.Find<CrmActivity>(x => x.CustomerId == account.Id && x.Status != ActivityStatus.Planned);
                var users = AccountGuard.UserNames(data, activities.Select(x => x.CompletedByUserId ?? x.OwnerUserId));
                items.AddRange(activities.Select(x => new TimelineItemDto(x.Type.ToString(),
                    $"{Label(x.Type)} {(x.Status == ActivityStatus.Completed ? "انجام شد" : "لغو شد")}: {x.Subject}",
                    x.Status == ActivityStatus.Completed ? x.Outcome : x.CancelReason, TehranTime.Format(x.CompletedAtUtc ?? x.StartAtUtc), x.CompletedAtUtc ?? x.StartAtUtc,
                    users.GetValueOrDefault(x.CompletedByUserId ?? x.OwnerUserId), $"/customers/{account.Id}/activities/{x.Id}", Icon(x.Type))));
            }
            if (AccountGuard.Allows(snapshot, account, NoteRead))
            {
                var notes = AccountNoteService.VisibleNotes(data, snapshot, account, userId);
                var authors = AccountGuard.UserNames(data, notes.Select(x => x.AuthorUserId));
                items.AddRange(notes.Select(x => new TimelineItemDto("Note", $"یادداشت: {x.Title}", x.Body.Length > 160 ? x.Body[..160] + "…" : x.Body,
                    TehranTime.Format(x.CreatedAtUtc), x.CreatedAtUtc, authors.GetValueOrDefault(x.AuthorUserId), null, "i-file")));
            }
            var important = new[] { CustomerTimelineType.Created, CustomerTimelineType.LeadConverted, CustomerTimelineType.OpportunityChanged, CustomerTimelineType.ServiceCase,
                CustomerTimelineType.VisitCompleted, CustomerTimelineType.Merge, CustomerTimelineType.Payment, CustomerTimelineType.Contract, CustomerTimelineType.FinancialSync };
            var events = data.Find<CustomerTimelineEvent>(x => x.CustomerId == account.Id).Where(x => important.Contains(x.Type)).ToList();
            var actors = AccountGuard.UserNames(data, events.Select(x => x.ActorUserId ?? Guid.Empty));
            items.AddRange(events.Select(x => new TimelineItemDto(x.Type.ToString(), x.Title, x.Description, TehranTime.Format(x.OccurredAtUtc), x.OccurredAtUtc,
                x.ActorUserId is { } actor ? actors.GetValueOrDefault(actor) : x.Source, null,
                x.Type switch { CustomerTimelineType.ServiceCase => "i-shield", CustomerTimelineType.OpportunityChanged => "i-target", _ => "i-grid" })));
            return items.OrderByDescending(x => x.AtUtc).Take(take).ToList();
        });
    }

    private ActivityItemDto Mutate(AccessSnapshot snapshot, OrganizationSelection organization, Guid accountId, Guid activityId, long expectedVersion, Guid userId,
        DateTimeOffset nowUtc, Action<CrmDataSet, Customer, CrmActivity> action) => store.Write(data =>
    {
        var account = AccountGuard.Account(data, snapshot, organization, accountId, ActivityRead);
        AccountGuard.EnsureMutable(account);
        var activity = data.Find<CrmActivity>(x => x.Id == activityId && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("فعالیت پیدا نشد.");
        if (!CanModify(snapshot, account, activity, userId)) throw new UnauthorizedAccessException("تغییر این فعالیت مجاز نیست.");
        if (activity.Version != expectedVersion) throw new InvalidOperationException("فعالیت تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
        action(data, account, activity);
        return Item(data, snapshot, organization, account.Id, activity.Id, userId, nowUtc);
    });

    /// <summary>Owners and creators manage their own activities; users with Activity.Update manage any activity of accounts in their scope.</summary>
    public static bool CanModify(AccessSnapshot snapshot, Customer account, CrmActivity activity, Guid userId) =>
        AccountGuard.Allows(snapshot, account, ActivityUpdate) ||
        AccountGuard.Allows(snapshot, account, ActivityCreate) && (activity.OwnerUserId == userId || activity.CreatedByUserId == userId);

    private static void ValidateLinks(CrmDataSet data, AccessSnapshot snapshot, Customer account, SaveActivityCommand command, Guid userId, DateTimeOffset nowUtc)
    {
        if (command.ContactId is { } contact && !data.Find<CustomerContact>(x => x.Id == contact && x.CustomerId == account.Id && x.IsActive).Any())
            throw new InvalidOperationException("فرد رابط انتخاب‌شده به این حساب تعلق ندارد.");
        if (!AccountGuard.Owners(data, account, nowUtc).Any(x => x.Id == command.OwnerUserId))
            throw new InvalidOperationException("مسئول انتخاب‌شده در دامنهٔ این حساب کاربر فعال نیست.");
        if (command.OwnerUserId != userId && !AccountGuard.Allows(snapshot, account, ActivityUpdate))
            throw new UnauthorizedAccessException("واگذاری فعالیت به کاربر دیگر نیازمند مجوز Activity.Update است.");
        var valid = command.RelatedKind switch
        {
            ActivityRelatedKind.None => command.RelatedId is null,
            ActivityRelatedKind.Opportunity => command.RelatedId is { } o && data.Find<Opportunity>(x => x.Id == o && x.CustomerId == account.Id).Any(),
            ActivityRelatedKind.Project => command.RelatedId is { } p && data.Find<AccountProject>(x => x.Id == p && x.CustomerId == account.Id).Any(),
            ActivityRelatedKind.Contract => command.RelatedId is { } c && data.Find<AccountContract>(x => x.Id == c && x.CustomerId == account.Id).Any(),
            _ => false
        };
        if (!valid) throw new InvalidOperationException("رکورد مرتبط باید فرصت، پروژه یا قرارداد همین حساب باشد.");
        var contacts = command.ParticipantContactIds ?? [];
        if (contacts.Count > 0 && data.Find<CustomerContact>(x => contacts.Contains(x.Id) && x.CustomerId == account.Id && x.IsActive).Count != contacts.Distinct().Count())
            throw new InvalidOperationException("شرکت‌کنندگان باید از افراد رابط همین حساب باشند.");
        var users = command.ParticipantUserIds ?? [];
        if (users.Count > 0)
        {
            var owners = AccountGuard.Owners(data, account, nowUtc).Select(x => x.Id).ToHashSet();
            if (users.Any(x => !owners.Contains(x))) throw new InvalidOperationException("شرکت‌کنندهٔ داخلی باید کاربر فعال همین دامنه باشد.");
        }
    }

    private static void AddParticipants(CrmDataSet data, Customer account, CrmActivity activity, SaveActivityCommand command)
    {
        if (activity.Type != ActivityType.Meeting) return;
        var contactIds = (command.ParticipantContactIds ?? []).Distinct().ToArray();
        var userIds = (command.ParticipantUserIds ?? []).Distinct().ToArray();
        var contacts = contactIds.Length == 0 ? [] : data.Find<CustomerContact>(x => contactIds.Contains(x.Id) && x.CustomerId == account.Id);
        foreach (var contact in contacts)
            data.ActivityParticipants.Add(new ActivityParticipant(Guid.NewGuid(), activity.Id, ParticipantKind.Contact, contact.Id, contact.FullName));
        foreach (var (id, name) in AccountGuard.UserNames(data, userIds))
            data.ActivityParticipants.Add(new ActivityParticipant(Guid.NewGuid(), activity.Id, ParticipantKind.User, id, name));
    }

    /// <summary>Reminder through the notification outbox, delivered at reminder time (email/SMS per the owner's settings).</summary>
    private static void ScheduleReminder(CrmDataSet data, Customer account, CrmActivity activity, DateTimeOffset nowUtc)
    {
        WithdrawReminders(data, activity);
        if (activity.ReminderAtUtc is not { } at || activity.Status != ActivityStatus.Planned || activity.StartAtUtc <= nowUtc) return;
        NotificationOutbox.Enqueue(data, account.CompanyId, activity.OwnerUserId, NotificationCategory.ActivityReminder,
            $"یادآور {Label(activity.Type)}: {activity.Subject}", $"{account.Name} · {TehranTime.Format(activity.StartAtUtc)}",
            $"/customers/{account.Id}?tab=activities", $"activity-reminder:{activity.Id:N}:{activity.StartAtUtc.UtcTicks}:{activity.ReminderMinutesBefore}", nowUtc, at);
    }

    private static void WithdrawReminders(CrmDataSet data, CrmActivity activity)
    {
        var prefix = $"activity-reminder:{activity.Id:N}:";
        foreach (var message in data.Find<NotificationMessage>(x => x.RecipientUserId == activity.OwnerUserId && x.Status == NotificationStatus.Pending &&
                     x.DedupKey.StartsWith(prefix)))
            message.Withdraw();
    }

    private ActivityItemDto Item(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid accountId, Guid activityId, Guid userId,
        DateTimeOffset nowUtc)
    {
        var account = AccountGuard.Account(data, snapshot, organization, accountId);
        var activity = data.Find<CrmActivity>(x => x.Id == activityId).Single();
        var contacts = data.Find<CustomerContact>(x => x.CustomerId == account.Id).ToDictionary(x => x.Id, x => x.FullName);
        return Map(activity, contacts, AccountGuard.UserNames(data, [activity.OwnerUserId]), RelatedNames(data, account, [activity]), snapshot, account, userId, nowUtc);
    }

    internal static AccountFormOptions Options(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId, DateTimeOffset nowUtc) => new(
        account.Id, account.Name,
        data.Find<CustomerContact>(x => x.CustomerId == account.Id && x.IsActive).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.FullName)
            .Select(x => (x.Id, string.IsNullOrEmpty(x.Role) ? x.FullName : $"{x.FullName} ({x.Role})")).ToList(),
        AccountGuard.Owners(data, account, nowUtc),
        data.Find<Opportunity>(x => x.CustomerId == account.Id).Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost))
            .Select(x => (x.Id, $"{x.Code} · {x.Title}")).ToList(),
        data.Find<AccountProject>(x => x.CustomerId == account.Id).Where(x => x.Status is not (ProjectStatus.Completed or ProjectStatus.Cancelled))
            .Select(x => (x.Id, $"{x.Code} · {x.Name}")).ToList(),
        data.Find<AccountContract>(x => x.CustomerId == account.Id).Where(x => x.Status != ContractStatus.Terminated)
            .Select(x => (x.Id, $"{x.Number} · {x.Title}")).ToList(),
        userId, AccountGuard.Allows(snapshot, account, ActivityUpdate));

    private static Dictionary<(ActivityRelatedKind, Guid), (string Name, string Url)> RelatedNames(CrmDataSet data, Customer account, IEnumerable<CrmActivity> activities)
    {
        var result = new Dictionary<(ActivityRelatedKind, Guid), (string, string)>();
        var list = activities.Where(x => x.RelatedId is not null).ToList();
        var opportunityIds = list.Where(x => x.RelatedKind == ActivityRelatedKind.Opportunity).Select(x => x.RelatedId!.Value).Distinct().ToArray();
        var projectIds = list.Where(x => x.RelatedKind == ActivityRelatedKind.Project).Select(x => x.RelatedId!.Value).Distinct().ToArray();
        var contractIds = list.Where(x => x.RelatedKind == ActivityRelatedKind.Contract).Select(x => x.RelatedId!.Value).Distinct().ToArray();
        if (opportunityIds.Length > 0)
            foreach (var x in data.Find<Opportunity>(x => opportunityIds.Contains(x.Id))) result[(ActivityRelatedKind.Opportunity, x.Id)] = ($"فرصت {x.Code}", $"/opportunities/{x.Id}");
        if (projectIds.Length > 0)
            foreach (var x in data.Find<AccountProject>(x => projectIds.Contains(x.Id))) result[(ActivityRelatedKind.Project, x.Id)] = ($"پروژه {x.Code}", $"/customers/{account.Id}?tab=related&open=projects");
        if (contractIds.Length > 0)
            foreach (var x in data.Find<AccountContract>(x => contractIds.Contains(x.Id))) result[(ActivityRelatedKind.Contract, x.Id)] = ($"قرارداد {x.Number}", $"/customers/{account.Id}?tab=related&open={(x.Kind == ContractKind.Sales ? "salesContracts" : "serviceContracts")}");
        return result;
    }

    private static ActivityItemDto Map(CrmActivity x, IReadOnlyDictionary<Guid, string> contacts, IReadOnlyDictionary<Guid, string> users,
        IReadOnlyDictionary<(ActivityRelatedKind, Guid), (string Name, string Url)> related, AccessSnapshot snapshot, Customer account, Guid userId, DateTimeOffset nowUtc)
    {
        var link = x.RelatedId is { } id && related.TryGetValue((x.RelatedKind, id), out var r) ? r : default;
        var when = x.Type == ActivityType.Meeting && x.EndAtUtc is { } end
            ? $"{TehranTime.Format(x.StartAtUtc)} تا {TehranTime.Clock(end)}"
            : TehranTime.Format(x.StartAtUtc);
        var details = x.Type switch
        {
            ActivityType.Call => $"{(x.Direction == CallDirection.Inbound ? "ورودی" : "خروجی")}{(x.DurationMinutes is { } d ? $" · {d} دقیقه" : "")}{(x.CallResult is { } cr ? $" · {CallResultLabel(cr)}" : "")}",
            ActivityType.Meeting => x.Location,
            _ => $"اولویت {PriorityLabel(x.Priority)}"
        };
        var canModify = x.Status == ActivityStatus.Planned && account.Status != CustomerStatus.Inactive && CanModify(snapshot, account, x, userId);
        return new ActivityItemDto(x.Id, x.Type.ToString(), x.Type, x.Subject, x.Description, users.GetValueOrDefault(x.OwnerUserId, "—"), x.OwnerUserId,
            x.ContactId is { } c ? contacts.GetValueOrDefault(c) : null, when, x.Status == ActivityStatus.Planned ? x.DueAtUtc : x.CompletedAtUtc ?? x.StartAtUtc,
            x.Status.ToString(), x.IsOverdue(nowUtc), link.Name, link.Url, x.Status == ActivityStatus.Cancelled ? x.CancelReason : x.Outcome, details,
            canModify, canModify, x.Version);
    }

    private static DateTimeOffset RoundUp(DateTimeOffset value)
    {
        var minutes = 15 - value.Minute % 15;
        return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset).AddMinutes(minutes);
    }

    public static string Label(ActivityType type) => type switch { ActivityType.Call => "تماس", ActivityType.Meeting => "جلسه", _ => "وظیفه" };

    public static string Icon(ActivityType type) => type switch { ActivityType.Call => "i-phone", ActivityType.Meeting => "i-users", _ => "i-check" };

    public static string CallResultLabel(CallResult value) => value switch
    {
        CallResult.Answered => "پاسخ داده شد",
        CallResult.NoAnswer => "بی‌پاسخ",
        CallResult.Busy => "مشغول",
        CallResult.LeftMessage => "پیام گذاشته شد",
        _ => "شماره اشتباه"
    };

    public static string PriorityLabel(ActivityPriority value) => value switch
    {
        ActivityPriority.Low => "کم",
        ActivityPriority.High => "زیاد",
        ActivityPriority.Urgent => "فوری",
        _ => "عادی"
    };
}
