using Crm.Domain.Common;

namespace Crm.Domain.Accounts;

public enum ActivityType { Call, Meeting, Task }
public enum ActivityStatus { Planned, Completed, Cancelled }
public enum CallDirection { Outbound, Inbound }
public enum CallResult { Answered, NoAnswer, Busy, LeftMessage, WrongNumber }
public enum ActivityPriority { Low, Normal, High, Urgent }
public enum ActivityRelatedKind { None, Opportunity, Project, Contract, FollowUpCase }
public enum ParticipantKind { Contact, User }

/// <summary>Planning data of a call, meeting or task, validated together because the rules depend on the type.</summary>
public sealed record ActivitySchedule(
    DateTimeOffset StartAtUtc,
    DateTimeOffset? EndAtUtc = null,
    int? DurationMinutes = null,
    CallDirection? Direction = null,
    string? Location = null,
    ActivityPriority Priority = ActivityPriority.Normal,
    int? ReminderMinutesBefore = null);

/// <summary>
/// One engagement with an account: a call, a meeting or a task. It is stored once and linked to the account and,
/// optionally, a contact person and one related record (opportunity, project or contract), so the same activity is
/// never duplicated between the account and contact histories. Planning and doing are separate states: an activity is
/// planned until it is completed with an outcome or cancelled with a reason.
/// </summary>
public sealed class CrmActivity : Entity
{
    public CrmActivity(Guid id, string companyId, Guid customerId, ActivityType type, string subject, string? description,
        Guid? contactId, Guid ownerUserId, ActivitySchedule schedule, ActivityRelatedKind relatedKind, Guid? relatedId,
        Guid createdByUserId, Guid? followUpOfId = null) : base(id)
    {
        CompanyId = Text(companyId, 32) ?? throw new ArgumentException("Company required.", nameof(companyId));
        CustomerId = customerId != Guid.Empty ? customerId : throw new ArgumentException("Account required.", nameof(customerId));
        Type = type;
        CreatedByUserId = createdByUserId;
        FollowUpOfId = followUpOfId;
        Apply(subject, description, contactId, ownerUserId, schedule, relatedKind, relatedId);
    }

    private CrmActivity() : base(Guid.Empty)
    {
        CompanyId = Subject = "EF";
    }

    public string CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public ActivityType Type { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    /// <summary>Call/meeting start, or the task's due time.</summary>
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset? EndAtUtc { get; private set; }
    public int? DurationMinutes { get; private set; }
    public CallDirection? Direction { get; private set; }
    /// <summary>Meeting place or online link.</summary>
    public string? Location { get; private set; }
    public ActivityPriority Priority { get; private set; } = ActivityPriority.Normal;
    public int? ReminderMinutesBefore { get; private set; }
    public ActivityRelatedKind RelatedKind { get; private set; }
    public Guid? RelatedId { get; private set; }
    public ActivityStatus Status { get; private set; } = ActivityStatus.Planned;
    public CallResult? CallResult { get; private set; }
    public string? Outcome { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? FollowUpOfId { get; private set; }

    /// <summary>When the activity is due: the meeting end (or start), the call start, or the task deadline.</summary>
    public DateTimeOffset DueAtUtc => Type == ActivityType.Meeting ? EndAtUtc ?? StartAtUtc : StartAtUtc;

    public bool IsOverdue(DateTimeOffset nowUtc) => Status == ActivityStatus.Planned && DueAtUtc < nowUtc;

    public DateTimeOffset? ReminderAtUtc => ReminderMinutesBefore is { } minutes ? StartAtUtc.AddMinutes(-minutes) : null;

    public void Update(string subject, string? description, Guid? contactId, Guid ownerUserId, ActivitySchedule schedule,
        ActivityRelatedKind relatedKind, Guid? relatedId)
    {
        EnsurePlanned();
        Apply(subject, description, contactId, ownerUserId, schedule, relatedKind, relatedId);
        Touch();
    }

    public void Reschedule(DateTimeOffset startAtUtc, DateTimeOffset? endAtUtc)
    {
        EnsurePlanned();
        var schedule = new ActivitySchedule(startAtUtc, Type == ActivityType.Meeting ? endAtUtc : null, DurationMinutes, Direction, Location,
            Priority, ReminderMinutesBefore);
        Validate(Type, schedule);
        StartAtUtc = startAtUtc;
        EndAtUtc = schedule.EndAtUtc;
        Touch();
    }

    /// <summary>Records what happened. Calls and meetings require an outcome; a call also records its result.</summary>
    public void Complete(string? outcome, CallResult? callResult, Guid userId, DateTimeOffset nowUtc, int? actualDurationMinutes = null)
    {
        EnsurePlanned();
        var text = Text(outcome, 4000);
        if (Type is ActivityType.Call or ActivityType.Meeting && text is null)
            throw new InvalidOperationException(Type == ActivityType.Call ? "نتیجهٔ تماس را ثبت کنید." : "نتیجهٔ جلسه را ثبت کنید.");
        if (Type == ActivityType.Call && callResult is null) throw new InvalidOperationException("وضعیت پاسخ تماس را انتخاب کنید.");
        if (actualDurationMinutes is < 1 or > 600) throw new InvalidOperationException("مدت تماس باید بین ۱ تا ۶۰۰ دقیقه باشد.");
        if (nowUtc < StartAtUtc.AddDays(-30)) throw new InvalidOperationException("فعالیتی که بیش از ۳۰ روز بعد برنامه‌ریزی شده است هنوز قابل تکمیل نیست.");
        Outcome = text;
        CallResult = Type == ActivityType.Call ? callResult : null;
        if (Type == ActivityType.Call && actualDurationMinutes is { } duration) DurationMinutes = duration;
        Status = ActivityStatus.Completed;
        CompletedAtUtc = nowUtc;
        CompletedByUserId = userId;
        Touch();
    }

    public void Cancel(string reason, Guid userId, DateTimeOffset nowUtc)
    {
        EnsurePlanned();
        CancelReason = Text(reason, 500) ?? throw new InvalidOperationException("دلیل لغو را ثبت کنید.");
        Status = ActivityStatus.Cancelled;
        CompletedAtUtc = nowUtc;
        CompletedByUserId = userId;
        Touch();
    }

    public void ReassignCustomer(Guid customerId)
    {
        CustomerId = customerId;
        Touch();
    }

    private void Apply(string subject, string? description, Guid? contactId, Guid ownerUserId, ActivitySchedule schedule,
        ActivityRelatedKind relatedKind, Guid? relatedId)
    {
        if (ownerUserId == Guid.Empty) throw new InvalidOperationException("مسئول فعالیت را انتخاب کنید.");
        if (relatedKind == ActivityRelatedKind.None != (relatedId is null))
            throw new InvalidOperationException("رکورد مرتبط ناقص است.");
        Validate(Type, schedule);
        Subject = Text(subject, 200) ?? throw new InvalidOperationException(Type == ActivityType.Task ? "عنوان وظیفه الزامی است." : "موضوع الزامی است.");
        Description = Text(description, 4000);
        ContactId = contactId;
        OwnerUserId = ownerUserId;
        StartAtUtc = schedule.StartAtUtc;
        EndAtUtc = Type == ActivityType.Meeting ? schedule.EndAtUtc : null;
        DurationMinutes = Type == ActivityType.Call ? schedule.DurationMinutes : null;
        Direction = Type == ActivityType.Call ? schedule.Direction ?? CallDirection.Outbound : null;
        Location = Type == ActivityType.Meeting ? Text(schedule.Location, 300) : null;
        Priority = schedule.Priority;
        ReminderMinutesBefore = Type == ActivityType.Task ? null : schedule.ReminderMinutesBefore;
        RelatedKind = relatedKind;
        RelatedId = relatedId;
    }

    private static void Validate(ActivityType type, ActivitySchedule schedule)
    {
        if (type == ActivityType.Meeting)
        {
            if (schedule.EndAtUtc is not { } end) throw new InvalidOperationException("زمان پایان جلسه الزامی است.");
            if (end <= schedule.StartAtUtc) throw new InvalidOperationException("پایان جلسه باید بعد از شروع آن باشد.");
            if (end - schedule.StartAtUtc > TimeSpan.FromHours(12)) throw new InvalidOperationException("مدت جلسه حداکثر ۱۲ ساعت است.");
        }
        if (type == ActivityType.Call && schedule.DurationMinutes is < 1 or > 600) throw new InvalidOperationException("مدت تماس باید بین ۱ تا ۶۰۰ دقیقه باشد.");
        if (schedule.ReminderMinutesBefore is < 0 or > 10080) throw new InvalidOperationException("یادآور باید حداکثر یک هفته قبل باشد.");
    }

    private void EnsurePlanned()
    {
        if (Status != ActivityStatus.Planned) throw new InvalidOperationException("فعالیت انجام‌شده یا لغوشده قابل تغییر نیست.");
    }

    internal static string? Text(string? value, int max)
    {
        var text = PersianText.NormalizeLetters(value);
        return text is null ? null : text.Length <= max ? text : throw new InvalidOperationException($"حداکثر طول مجاز {max} نویسه است.");
    }
}

/// <summary>A meeting participant: a contact person of the account or an internal user (many-to-many).</summary>
public sealed class ActivityParticipant : Entity
{
    public ActivityParticipant(Guid id, Guid activityId, ParticipantKind kind, Guid participantId, string name) : base(id)
    {
        ActivityId = activityId;
        Kind = kind;
        ParticipantId = participantId;
        Name = CrmActivity.Text(name, 200) ?? "—";
    }

    private ActivityParticipant() : base(Guid.Empty) => Name = "EF";

    public Guid ActivityId { get; private set; }
    public ParticipantKind Kind { get; private set; }
    public Guid ParticipantId { get; private set; }
    public string Name { get; private set; }
}

/// <summary>Idempotency record: a repeated submit (double click, retry) with the same operation id returns the first result.</summary>
public sealed class ClientOperation : Entity
{
    public ClientOperation(Guid id, Guid userId, Guid operationId, string kind, Guid entityId) : base(id)
    {
        UserId = userId;
        OperationId = operationId != Guid.Empty ? operationId : throw new ArgumentException("Operation id required.", nameof(operationId));
        Kind = kind;
        EntityId = entityId;
    }

    private ClientOperation() : base(Guid.Empty) => Kind = "EF";

    public Guid UserId { get; private set; }
    public Guid OperationId { get; private set; }
    public string Kind { get; private set; }
    public Guid EntityId { get; private set; }
}
