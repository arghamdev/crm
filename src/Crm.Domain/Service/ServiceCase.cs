using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Service;

public enum ServiceCaseStatus { New, Triaged, InProgress, WaitingOnCustomer, Resolved, Closed }
public enum ServiceCasePriority { Low, Medium, High, Critical }
public enum ServiceCaseCategory { Complaint, ProductDefect, Delivery, Invoice, Warranty, Inquiry }
public enum ServiceCaseChannel { Phone, Email, Portal, Visit, Internal }
public enum ServiceSlaState { OnTrack, AtRisk, Breached, Met, Paused, NotApplicable }

/// <summary>Response and resolution targets per priority. Calendar hours; business calendars are a later step.</summary>
public static class ServiceSlaPolicy
{
    public static TimeSpan FirstResponse(ServiceCasePriority priority) => priority switch
    {
        ServiceCasePriority.Critical => TimeSpan.FromHours(1),
        ServiceCasePriority.High => TimeSpan.FromHours(4),
        ServiceCasePriority.Medium => TimeSpan.FromHours(8),
        _ => TimeSpan.FromHours(24)
    };

    public static TimeSpan Resolution(ServiceCasePriority priority) => priority switch
    {
        ServiceCasePriority.Critical => TimeSpan.FromHours(8),
        ServiceCasePriority.High => TimeSpan.FromHours(24),
        ServiceCasePriority.Medium => TimeSpan.FromHours(72),
        _ => TimeSpan.FromHours(120)
    };

    /// <summary>A case is "at risk" when less than this share of its SLA window remains.</summary>
    public const double AtRiskRemainingShare = 0.25;

    /// <summary>Closed cases can be reopened only within this window.</summary>
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromDays(14);

    public const int MaxEscalationLevel = 2;
}

public sealed class ServiceCase : Entity, IOrganizationScoped
{
    public ServiceCase(
        Guid id,
        string code,
        string subject,
        string description,
        Guid customerId,
        string companyId,
        string branchId,
        string? territoryId,
        ServiceCaseCategory category,
        ServiceCaseChannel channel,
        ServiceCasePriority priority,
        Guid createdByUserId,
        DateTimeOffset openedAtUtc) : base(id)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        Code = Required(code, nameof(code));
        Subject = Limit(Required(subject, nameof(subject)), 200);
        Description = Limit(description?.Trim() ?? string.Empty, 4000);
        CustomerId = customerId;
        CompanyId = Required(companyId, nameof(companyId));
        BranchId = Required(branchId, nameof(branchId));
        TerritoryId = Optional(territoryId);
        Category = category;
        Channel = channel;
        Priority = priority;
        CreatedByUserId = createdByUserId;
        OpenedAtUtc = openedAtUtc;
        FirstResponseDueAtUtc = openedAtUtc.Add(ServiceSlaPolicy.FirstResponse(priority));
        ResolutionDueAtUtc = openedAtUtc.Add(ServiceSlaPolicy.Resolution(priority));
    }

    private ServiceCase() : base(Guid.Empty)
    {
        Code = Subject = Description = CompanyId = BranchId = "EF";
    }

    public string Code { get; private set; }
    public string Subject { get; private set; }
    public string Description { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public ServiceCaseCategory Category { get; private set; }
    public ServiceCaseChannel Channel { get; private set; }
    public ServiceCasePriority Priority { get; private set; }
    public ServiceCaseStatus Status { get; private set; } = ServiceCaseStatus.New;
    public Guid CreatedByUserId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string Owner { get; private set; } = string.Empty;
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset FirstResponseDueAtUtc { get; private set; }
    public DateTimeOffset ResolutionDueAtUtc { get; private set; }
    public DateTimeOffset? FirstRespondedAtUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public long PausedMinutes { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public int EscalationLevel { get; private set; }
    public DateTimeOffset? LastEscalatedAtUtc { get; private set; }
    public string? RootCause { get; private set; }
    public string? CorrectiveAction { get; private set; }
    public string? Resolution { get; private set; }
    public int ReopenCount { get; private set; }
    public int? SatisfactionScore { get; private set; }
    public string? SatisfactionComment { get; private set; }

    public bool IsOpen => Status is not (ServiceCaseStatus.Resolved or ServiceCaseStatus.Closed);

    /// <summary>Sets priority and owner. Changing priority re-baselines the SLA from the original open time.</summary>
    public void Triage(ServiceCasePriority priority, Guid ownerUserId, string owner, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerUserId));
        var pause = TimeSpan.FromMinutes(PausedMinutes);
        if (priority != Priority)
        {
            Priority = priority;
            if (FirstRespondedAtUtc is null)
                FirstResponseDueAtUtc = OpenedAtUtc.Add(ServiceSlaPolicy.FirstResponse(priority)).Add(pause);
            ResolutionDueAtUtc = OpenedAtUtc.Add(ServiceSlaPolicy.Resolution(priority)).Add(pause);
        }
        OwnerUserId = ownerUserId;
        Owner = Required(owner, nameof(owner));
        if (Status == ServiceCaseStatus.New) Status = ServiceCaseStatus.Triaged;
        Touch();
    }

    /// <summary>The owner starts work; the first such action satisfies the first-response SLA.</summary>
    public void StartWork(DateTimeOffset nowUtc)
    {
        if (Status is not (ServiceCaseStatus.New or ServiceCaseStatus.Triaged or ServiceCaseStatus.WaitingOnCustomer))
            throw new InvalidOperationException("شروع رسیدگی فقط برای پرونده جدید، تریاژشده یا در انتظار مشتری مجاز است.");
        if (OwnerUserId is null) throw new InvalidOperationException("پیش از شروع رسیدگی، مسئول پرونده باید تعیین شود.");
        if (Status == ServiceCaseStatus.WaitingOnCustomer) ResumeClock(nowUtc);
        FirstRespondedAtUtc ??= nowUtc;
        Status = ServiceCaseStatus.InProgress;
        Touch();
    }

    /// <summary>Pause rule: time spent waiting on the customer does not count against the SLA.</summary>
    public void WaitOnCustomer(DateTimeOffset nowUtc)
    {
        if (Status != ServiceCaseStatus.InProgress)
            throw new InvalidOperationException("فقط پرونده در حال رسیدگی می‌تواند در انتظار مشتری قرار گیرد.");
        Status = ServiceCaseStatus.WaitingOnCustomer;
        PausedAtUtc = nowUtc;
        Touch();
    }

    public void Resolve(string rootCause, string correctiveAction, string resolution, DateTimeOffset nowUtc)
    {
        if (Status != ServiceCaseStatus.InProgress)
            throw new InvalidOperationException("فقط پرونده در حال رسیدگی قابل حل است.");
        RootCause = Limit(Required(rootCause, nameof(rootCause), "علت ریشه‌ای الزامی است."), 1000);
        CorrectiveAction = Limit(Required(correctiveAction, nameof(correctiveAction), "اقدام اصلاحی الزامی است."), 1000);
        Resolution = Limit(Required(resolution, nameof(resolution), "شرح راه‌حل الزامی است."), 2000);
        ResolvedAtUtc = nowUtc;
        Status = ServiceCaseStatus.Resolved;
        Touch();
    }

    public void Close(int? satisfactionScore, string? satisfactionComment, DateTimeOffset nowUtc)
    {
        if (Status != ServiceCaseStatus.Resolved)
            throw new InvalidOperationException("فقط پرونده حل‌شده قابل بستن است.");
        if (satisfactionScore is < 1 or > 5)
            throw new InvalidOperationException("امتیاز رضایت باید بین ۱ تا ۵ باشد.");
        SatisfactionScore = satisfactionScore;
        SatisfactionComment = string.IsNullOrWhiteSpace(satisfactionComment) ? null : Limit(satisfactionComment.Trim(), 1000);
        ClosedAtUtc = nowUtc;
        Status = ServiceCaseStatus.Closed;
        Touch();
    }

    /// <summary>Reopening keeps the history but gives the case a fresh resolution window.</summary>
    public void Reopen(DateTimeOffset nowUtc)
    {
        if (Status is not (ServiceCaseStatus.Resolved or ServiceCaseStatus.Closed))
            throw new InvalidOperationException("فقط پرونده حل‌شده یا بسته قابل بازگشایی است.");
        if (ClosedAtUtc is { } closed && nowUtc - closed > ServiceSlaPolicy.ReopenWindow)
            throw new InvalidOperationException("مهلت بازگشایی پرونده (۱۴ روز پس از بستن) گذشته است؛ پرونده جدید ثبت کنید.");
        ReopenCount++;
        Status = ServiceCaseStatus.InProgress;
        ResolvedAtUtc = null;
        ClosedAtUtc = null;
        SatisfactionScore = null;
        SatisfactionComment = null;
        ResolutionDueAtUtc = nowUtc.Add(ServiceSlaPolicy.Resolution(Priority));
        Touch();
    }

    /// <summary>
    /// Returns the escalation level the case should be at now: 1 once any SLA is breached,
    /// 2 once the resolution SLA is overdue by half its window again. Paused cases never escalate.
    /// </summary>
    public int RequiredEscalationLevel(DateTimeOffset nowUtc)
    {
        if (!IsOpen || Status == ServiceCaseStatus.WaitingOnCustomer) return 0;
        var resolutionOverdue = nowUtc - ResolutionDueAtUtc;
        if (resolutionOverdue > ServiceSlaPolicy.Resolution(Priority) / 2) return 2;
        if (resolutionOverdue > TimeSpan.Zero) return 1;
        return FirstRespondedAtUtc is null && nowUtc > FirstResponseDueAtUtc ? 1 : 0;
    }

    public bool Escalate(DateTimeOffset nowUtc)
    {
        var required = Math.Min(RequiredEscalationLevel(nowUtc), ServiceSlaPolicy.MaxEscalationLevel);
        if (required <= EscalationLevel) return false;
        EscalationLevel = required;
        LastEscalatedAtUtc = nowUtc;
        Touch();
        return true;
    }

    public ServiceSlaState FirstResponseSla(DateTimeOffset nowUtc)
    {
        if (FirstRespondedAtUtc is { } responded) return responded <= FirstResponseDueAtUtc ? ServiceSlaState.Met : ServiceSlaState.Breached;
        if (!IsOpen) return ServiceSlaState.NotApplicable;
        return Evaluate(OpenedAtUtc, FirstResponseDueAtUtc, nowUtc);
    }

    public ServiceSlaState ResolutionSla(DateTimeOffset nowUtc)
    {
        if (ResolvedAtUtc is { } resolved) return resolved <= ResolutionDueAtUtc ? ServiceSlaState.Met : ServiceSlaState.Breached;
        if (Status == ServiceCaseStatus.WaitingOnCustomer) return ServiceSlaState.Paused;
        return Evaluate(OpenedAtUtc, ResolutionDueAtUtc, nowUtc);
    }

    private static ServiceSlaState Evaluate(DateTimeOffset startUtc, DateTimeOffset dueUtc, DateTimeOffset nowUtc)
    {
        if (nowUtc > dueUtc) return ServiceSlaState.Breached;
        var window = dueUtc - startUtc;
        return window > TimeSpan.Zero && (dueUtc - nowUtc) < window * ServiceSlaPolicy.AtRiskRemainingShare
            ? ServiceSlaState.AtRisk
            : ServiceSlaState.OnTrack;
    }

    /// <summary>Moves the case to another customer during customer merge/unmerge.</summary>
    public void ReassignCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        CustomerId = customerId;
        Touch();
    }

    private void ResumeClock(DateTimeOffset nowUtc)
    {
        if (PausedAtUtc is not { } pausedAt) return;
        var paused = nowUtc > pausedAt ? nowUtc - pausedAt : TimeSpan.Zero;
        PausedMinutes += (long)Math.Ceiling(paused.TotalMinutes);
        ResolutionDueAtUtc = ResolutionDueAtUtc.Add(paused);
        if (FirstRespondedAtUtc is null) FirstResponseDueAtUtc = FirstResponseDueAtUtc.Add(paused);
        PausedAtUtc = null;
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw new InvalidOperationException("پرونده حل‌شده یا بسته قابل تریاژ نیست.");
    }

    private static string Required(string? value, string name, string? message = null) =>
        string.IsNullOrWhiteSpace(value)
            ? throw (message is null ? new ArgumentException("Value is required.", name) : new InvalidOperationException(message))
            : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}

public sealed class ServiceCaseHistory(
    Guid id,
    Guid caseId,
    string companyId,
    string branchId,
    string? territoryId,
    ServiceCaseStatus? fromStatus,
    ServiceCaseStatus toStatus,
    string action,
    string note,
    Guid? actorUserId,
    DateTimeOffset occurredAtUtc) : Entity(id), IOrganizationScoped
{
    public Guid CaseId { get; } = caseId;
    public string CompanyId { get; } = companyId;
    public string BranchId { get; } = branchId;
    public string? TerritoryId { get; } = territoryId;
    public ServiceCaseStatus? FromStatus { get; } = fromStatus;
    public ServiceCaseStatus ToStatus { get; } = toStatus;
    public string Action { get; } = action;
    public string Note { get; } = note.Length <= 1000 ? note : note[..1000];
    public Guid? ActorUserId { get; } = actorUserId;
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;

    private ServiceCaseHistory() : this(Guid.Empty, Guid.Empty, "EF", "EF", null, null, ServiceCaseStatus.New,
        "EF", string.Empty, null, DateTimeOffset.MinValue) { }
}
