using Crm.Domain.Accounts;
using Crm.Domain.Common;

namespace Crm.Domain.FollowUps;

public enum FollowUpPriority { Low, Normal, High, Critical }
public enum EscalationTarget { StageOwner, CaseOwner, Supervisor, BranchManager }
public enum FollowUpTemplateStatus { Draft, Published, Retired }
public enum AssignmentMethod { MostFreeCapacity, RoundRobin }
public enum QueueOrdering { UrgencyThenDue, DueFirst, FirstIn }

/// <summary>
/// Working calendar of a branch: working days, daily hours, time zone and holidays. Due times are counted in working
/// minutes, so a 2-hour response target set on Wednesday 16:30 falls on Saturday 09:30 (Tehran calendar).
/// </summary>
public sealed class WorkCalendar
{
    private const int MaxDays = 3660;

    public WorkCalendar(IEnumerable<DayOfWeek> days, TimeOnly start, TimeOnly end, TimeZoneInfo zone, IEnumerable<DateOnly>? holidays = null)
    {
        Days = days.ToHashSet();
        if (Days.Count == 0) throw new InvalidOperationException("حداقل یک روز کاری لازم است.");
        if (end <= start) throw new InvalidOperationException("پایان ساعت کاری باید بعد از شروع آن باشد.");
        Start = start;
        End = end;
        Zone = zone;
        Holidays = (holidays ?? []).ToHashSet();
    }

    public IReadOnlySet<DayOfWeek> Days { get; }
    public TimeOnly Start { get; }
    public TimeOnly End { get; }
    public TimeZoneInfo Zone { get; }
    public IReadOnlySet<DateOnly> Holidays { get; }

    public static TimeZoneInfo ResolveZone(string? id)
    {
        var zoneId = string.IsNullOrWhiteSpace(id) ? "Asia/Tehran" : id.Trim();
        try { return TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (zoneId == "Asia/Tehran") return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromMinutes(210), "Tehran", "Tehran");
            throw new InvalidOperationException($"منطقهٔ زمانی «{zoneId}» شناخته‌شده نیست.");
        }
    }

    public bool IsWorkingDay(DateOnly date) => Days.Contains(date.DayOfWeek) && !Holidays.Contains(date);

    private DateTimeOffset ToUtc(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone.GetUtcOffset(local)).ToUniversalTime();

    /// <summary>The moment reached after <paramref name="minutes"/> working minutes from <paramref name="fromUtc"/>.</summary>
    public DateTimeOffset AddWorkingMinutes(DateTimeOffset fromUtc, double minutes)
    {
        if (minutes < 0) throw new ArgumentOutOfRangeException(nameof(minutes));
        var local = TimeZoneInfo.ConvertTime(fromUtc, Zone).DateTime;
        for (var day = 0; day < MaxDays; day++)
        {
            var date = DateOnly.FromDateTime(local);
            if (IsWorkingDay(date))
            {
                var open = date.ToDateTime(Start);
                var close = date.ToDateTime(End);
                if (local < open) local = open;
                if (local < close)
                {
                    var available = (close - local).TotalMinutes;
                    if (minutes <= available) return ToUtc(local.AddMinutes(minutes));
                    minutes -= available;
                }
            }
            local = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }
        throw new InvalidOperationException("مهلت در تقویم کاری قابل محاسبه نیست.");
    }

    /// <summary>Working minutes between two moments (zero when <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>).</summary>
    public double WorkingMinutesBetween(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc) return 0;
        var from = TimeZoneInfo.ConvertTime(fromUtc, Zone).DateTime;
        var to = TimeZoneInfo.ConvertTime(toUtc, Zone).DateTime;
        double total = 0;
        for (var date = DateOnly.FromDateTime(from); date <= DateOnly.FromDateTime(to) && total >= 0; date = date.AddDays(1))
        {
            if (!IsWorkingDay(date)) continue;
            var open = date.ToDateTime(Start);
            var close = date.ToDateTime(End);
            var a = from > open ? from : open;
            var b = to < close ? to : close;
            if (b > a) total += (b - a).TotalMinutes;
        }
        return total;
    }
}

/// <summary>
/// Response and resolution policy (سیاست مهلت): independent targets for the first response, each stage and the whole
/// case, counted in working hours of its calendar; which waiting states may pause the clock; and escalation steps.
/// Escalation reaches the next level of management; it never changes the case owner.
/// </summary>
public sealed class SlaPolicy : Entity
{
    public SlaPolicy(Guid id, string companyId, string name, FollowUpPriority priority, string? caseType = null) : base(id)
    {
        CompanyId = Req(companyId, 32, "شرکت");
        Name = Req(name, 120, "نام سیاست");
        Priority = priority;
        CaseType = Opt(caseType, 40);
    }

    private SlaPolicy() : base(Guid.Empty) => CompanyId = Name = "EF";

    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public FollowUpPriority Priority { get; private set; }
    /// <summary>Case type the policy is for; null means every type of that priority.</summary>
    public string? CaseType { get; private set; }
    public string TimeZoneId { get; private set; } = "Asia/Tehran";
    public string WorkDays { get; private set; } = "Saturday,Sunday,Monday,Tuesday,Wednesday";
    public string WorkStart { get; private set; } = "08:00";
    public string WorkEnd { get; private set; } = "17:00";
    /// <summary>Holidays as Jalali dates separated by commas or new lines (e.g. 1405/01/01).</summary>
    public string? Holidays { get; private set; }
    public int FirstResponseHours { get; private set; } = 2;
    public int StageHours { get; private set; } = 8;
    public int ResolutionHours { get; private set; } = 24;
    public bool PauseOnWaitingCustomer { get; private set; } = true;
    public bool PauseOnWaitingInternal { get; private set; }
    /// <summary>Escalation steps "offsetMinutes:Target" separated by ';' (negative = before the due time).</summary>
    public string Escalations { get; private set; } = "-30:StageOwner;0:Supervisor;120:BranchManager";
    public bool IsActive { get; private set; } = true;

    public void Update(string name, FollowUpPriority priority, string? caseType, string timeZoneId, IEnumerable<DayOfWeek> workDays, string workStart,
        string workEnd, string? holidays, int firstResponseHours, int stageHours, int resolutionHours, bool pauseOnWaitingCustomer,
        bool pauseOnWaitingInternal, IEnumerable<SlaEscalation> escalations, bool isActive)
    {
        if (firstResponseHours is < 1 or > 720 || stageHours is < 1 or > 2000 || resolutionHours is < 1 or > 5000)
            throw new InvalidOperationException("مهلت‌ها باید بین ۱ ساعت و حداکثر مجاز باشند.");
        if (firstResponseHours > resolutionHours) throw new InvalidOperationException("مهلت پاسخ اولیه نمی‌تواند از مهلت حل پرونده بیشتر باشد.");
        var days = workDays.Distinct().ToList();
        var steps = escalations.OrderBy(x => x.OffsetMinutes).ToList();
        if (steps.Count > 10) throw new InvalidOperationException("حداکثر ۱۰ مرحلهٔ هشدار مجاز است.");
        if (steps.Any(x => x.OffsetMinutes is < -10080 or > 43200)) throw new InvalidOperationException("زمان هشدار باید حداکثر یک هفته قبل یا یک ماه بعد از سررسید باشد.");
        var start = Clock(workStart);
        var end = Clock(workEnd);
        _ = new WorkCalendar(days, start, end, WorkCalendar.ResolveZone(timeZoneId), ParseHolidays(holidays));
        Name = Req(name, 120, "نام سیاست");
        Priority = priority;
        CaseType = Opt(caseType, 40);
        TimeZoneId = timeZoneId.Trim();
        WorkDays = string.Join(',', days);
        WorkStart = start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        WorkEnd = end.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Holidays = Opt(holidays, 2000);
        FirstResponseHours = firstResponseHours;
        StageHours = stageHours;
        ResolutionHours = resolutionHours;
        PauseOnWaitingCustomer = pauseOnWaitingCustomer;
        PauseOnWaitingInternal = pauseOnWaitingInternal;
        Escalations = string.Join(';', steps.Select(x => $"{x.OffsetMinutes}:{x.Target}"));
        IsActive = isActive;
        Touch();
    }

    public WorkCalendar Calendar() => new(WorkDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Enum.Parse<DayOfWeek>),
        Clock(WorkStart), Clock(WorkEnd), WorkCalendar.ResolveZone(TimeZoneId), ParseHolidays(Holidays));

    public IReadOnlyList<SlaEscalation> EscalationSteps() => Escalations.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.Split(':')).Where(x => x.Length == 2 && int.TryParse(x[0], out _) && Enum.TryParse<EscalationTarget>(x[1], out _))
        .Select(x => new SlaEscalation(int.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), Enum.Parse<EscalationTarget>(x[1])))
        .OrderBy(x => x.OffsetMinutes).ToList();

    public bool Pauses(FollowUpStatus status) => status switch
    {
        FollowUpStatus.WaitingCustomer => PauseOnWaitingCustomer,
        FollowUpStatus.WaitingInternal => PauseOnWaitingInternal,
        _ => false
    };

    public static IReadOnlyList<DateOnly> ParseHolidays(string? text) =>
        (text ?? string.Empty).Split([',', '\n', '\r', '،', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => JalaliDate.TryParse(x, out var d) ? d : throw new InvalidOperationException($"تاریخ تعطیل «{x}» معتبر نیست (نمونه: ۱۴۰۵/۰۱/۰۱)."))
        .ToList();

    private static TimeOnly Clock(string? text) =>
        TimeOnly.TryParseExact(PersianText.Normalize(text) ?? string.Empty, ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t) ? t : throw new InvalidOperationException("ساعت کاری را به شکل ۰۸:۰۰ وارد کنید.");

    internal static string Req(string? value, int max, string label) => CrmActivity.Text(value, max) ?? throw new InvalidOperationException($"{label} الزامی است.");
    internal static string? Opt(string? value, int max) => CrmActivity.Text(value, max);
}

public sealed record SlaEscalation(int OffsetMinutes, EscalationTarget Target);

/// <summary>
/// Workflow template (الگوی گردش کار) of one follow-up type. Templates are versioned: a published version is never edited,
/// a new draft copies it, and publishing the draft retires the previous version. Open cases keep the version they started
/// with; only new cases use the new one.
/// </summary>
public sealed class FollowUpTemplate : Entity
{
    public FollowUpTemplate(Guid id, string companyId, string code, string name, string caseType, int templateVersion) : base(id)
    {
        CompanyId = SlaPolicy.Req(companyId, 32, "شرکت");
        Code = SlaPolicy.Req(code, 40, "کد الگو");
        Name = SlaPolicy.Req(name, 160, "نام الگو");
        CaseType = SlaPolicy.Req(caseType, 40, "نوع پیگیری");
        TemplateVersion = templateVersion < 1 ? 1 : templateVersion;
    }

    private FollowUpTemplate() : base(Guid.Empty) => CompanyId = Code = Name = CaseType = "EF";

    public string CompanyId { get; private set; }
    /// <summary>Stable key shared by all versions of the template.</summary>
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string CaseType { get; private set; }
    public int TemplateVersion { get; private set; }
    public FollowUpTemplateStatus Status { get; private set; } = FollowUpTemplateStatus.Draft;
    /// <summary>"Company" or a branch id.</summary>
    public string Scope { get; private set; } = "Company";
    public string OwnerUnit { get; private set; } = "مدیریت فروش";
    public string? Description { get; private set; }
    /// <summary>Result the case must reach to be closed (معیار پایان).</summary>
    public string? ClosingCriteria { get; private set; }
    public bool RequireAllRequiredStages { get; private set; } = true;
    public bool RequireCustomerApproval { get; private set; }
    /// <summary>Rules "condition =&gt; action", one per line (e.g. «موجودی ناکافی بود =&gt; ایجاد وظیفه تأمین»).</summary>
    public string? Rules { get; private set; }
    public Guid? SlaPolicyId { get; private set; }
    public FollowUpPriority DefaultPriority { get; private set; } = FollowUpPriority.Normal;
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public Guid? PublishedByUserId { get; private set; }

    public void Update(string name, string caseType, string scope, string ownerUnit, string? description, string? closingCriteria, bool requireAllRequiredStages,
        bool requireCustomerApproval, string? rules, Guid? slaPolicyId, FollowUpPriority defaultPriority)
    {
        EnsureDraft();
        Name = SlaPolicy.Req(name, 160, "نام الگو");
        CaseType = SlaPolicy.Req(caseType, 40, "نوع پیگیری");
        Scope = SlaPolicy.Opt(scope, 32) ?? "Company";
        OwnerUnit = SlaPolicy.Req(ownerUnit, 120, "واحد مالک");
        Description = SlaPolicy.Opt(description, 1000);
        ClosingCriteria = SlaPolicy.Opt(closingCriteria, 500);
        RequireAllRequiredStages = requireAllRequiredStages;
        RequireCustomerApproval = requireCustomerApproval;
        Rules = SlaPolicy.Opt(rules, 2000);
        SlaPolicyId = slaPolicyId;
        DefaultPriority = defaultPriority;
        Touch();
    }

    /// <summary>Weights must total 100 so the case progress is explainable.</summary>
    public void Publish(IReadOnlyCollection<FollowUpTemplateStage> stages, Guid userId, DateTimeOffset nowUtc)
    {
        EnsureDraft();
        if (stages.Count == 0) throw new InvalidOperationException("الگو باید حداقل یک مرحله داشته باشد.");
        if (stages.Sum(x => x.Weight) != 100) throw new InvalidOperationException($"مجموع وزن مراحل باید ۱۰۰٪ باشد (اکنون {stages.Sum(x => x.Weight)}٪).");
        if (stages.GroupBy(x => x.Name.Trim()).Any(g => g.Count() > 1)) throw new InvalidOperationException("نام مراحل الگو نباید تکراری باشد.");
        Status = FollowUpTemplateStatus.Published;
        PublishedAtUtc = nowUtc;
        PublishedByUserId = userId;
        Touch();
    }

    public void Retire()
    {
        if (Status != FollowUpTemplateStatus.Published) return;
        Status = FollowUpTemplateStatus.Retired;
        Touch();
    }

    public FollowUpTemplate NewDraft(Guid id) => new(id, CompanyId, Code, Name, CaseType, TemplateVersion + 1)
    {
        Scope = Scope, OwnerUnit = OwnerUnit, Description = Description, ClosingCriteria = ClosingCriteria, RequireAllRequiredStages = RequireAllRequiredStages,
        RequireCustomerApproval = RequireCustomerApproval, Rules = Rules, SlaPolicyId = SlaPolicyId, DefaultPriority = DefaultPriority
    };

    public IReadOnlyList<(string Condition, string Action)> RuleList() => (Rules ?? string.Empty)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.Split("=>", 2, StringSplitOptions.TrimEntries)).Where(x => x.Length == 2 && x[0].Length > 0 && x[1].Length > 0)
        .Select(x => (x[0], x[1])).ToList();

    private void EnsureDraft()
    {
        if (Status != FollowUpTemplateStatus.Draft) throw new InvalidOperationException("نسخهٔ منتشرشده قابل ویرایش نیست؛ یک پیش‌نویس جدید بسازید.");
    }
}

public sealed class FollowUpTemplateStage : Entity
{
    public FollowUpTemplateStage(Guid id, Guid templateId, int order, string name, int weight, string responsibleRole, bool required = true,
        string? checklist = null, string? condition = null, bool parallelWithPrevious = false, int? durationHours = null) : base(id)
    {
        TemplateId = templateId;
        Order = order;
        Name = SlaPolicy.Req(name, 120, "نام مرحله");
        if (weight is < 0 or > 100) throw new InvalidOperationException("وزن مرحله باید بین ۰ و ۱۰۰ باشد.");
        Weight = weight;
        ResponsibleRole = SlaPolicy.Req(responsibleRole, 60, "نقش مسئول مرحله");
        Required = required;
        Checklist = SlaPolicy.Opt(checklist, 2000);
        Condition = SlaPolicy.Opt(condition, 200);
        ParallelWithPrevious = parallelWithPrevious;
        DurationHours = durationHours is > 0 ? durationHours : null;
    }

    private FollowUpTemplateStage() : base(Guid.Empty) => Name = ResponsibleRole = "EF";

    public Guid TemplateId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; }
    public int Weight { get; private set; }
    public string ResponsibleRole { get; private set; }
    public bool Required { get; private set; }
    /// <summary>Checklist items, one per line; the stage's progress is measured by them.</summary>
    public string? Checklist { get; private set; }
    /// <summary>Condition under which the stage is needed; when the case says it is not needed, the stage is skipped and left out of the progress.</summary>
    public string? Condition { get; private set; }
    public bool ParallelWithPrevious { get; private set; }
    /// <summary>Stage target in working hours; empty uses the SLA policy's stage target.</summary>
    public int? DurationHours { get; private set; }

    public IReadOnlyList<string> ChecklistItems() => (Checklist ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Work queue (صف کار) with entry conditions and an assignment rule. Cases that match are offered to the available
/// members with free capacity; when nobody qualifies they go to the overflow queue and the supervisor is told.
/// </summary>
public sealed class FollowUpQueue : Entity
{
    public FollowUpQueue(Guid id, string companyId, string name) : base(id)
    {
        CompanyId = SlaPolicy.Req(companyId, 32, "شرکت");
        Name = SlaPolicy.Req(name, 120, "نام صف");
    }

    private FollowUpQueue() : base(Guid.Empty) => CompanyId = Name = "EF";

    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public string Country { get; private set; } = "ایران";
    public string? BranchId { get; private set; }
    public string? CaseType { get; private set; }
    public string? PartFamily { get; private set; }
    public string? Language { get; private set; }
    public AssignmentMethod Method { get; private set; } = AssignmentMethod.MostFreeCapacity;
    public QueueOrdering Ordering { get; private set; } = QueueOrdering.UrgencyThenDue;
    public Guid? OverflowQueueId { get; private set; }
    public bool NotifySupervisorOnOverflow { get; private set; } = true;
    /// <summary>Lower numbers are checked first when several queues match.</summary>
    public int RuleOrder { get; private set; } = 100;
    public bool IsActive { get; private set; } = true;

    public void Update(string name, string? branchId, string? caseType, string? partFamily, string? language, AssignmentMethod method, QueueOrdering ordering,
        Guid? overflowQueueId, bool notifySupervisorOnOverflow, int ruleOrder, bool isActive)
    {
        if (overflowQueueId == Id) throw new InvalidOperationException("صف پشتیبان نمی‌تواند خود همین صف باشد.");
        Name = SlaPolicy.Req(name, 120, "نام صف");
        BranchId = SlaPolicy.Opt(branchId, 32);
        CaseType = SlaPolicy.Opt(caseType, 40);
        PartFamily = SlaPolicy.Opt(partFamily, 80);
        Language = SlaPolicy.Opt(language, 16);
        Method = method;
        Ordering = ordering;
        OverflowQueueId = overflowQueueId;
        NotifySupervisorOnOverflow = notifySupervisorOnOverflow;
        RuleOrder = Math.Clamp(ruleOrder, 1, 9999);
        IsActive = isActive;
        Touch();
    }

    /// <summary>A condition left empty matches everything.</summary>
    public bool Matches(string branchId, string caseType, string? partFamily, string? language) =>
        IsActive && (BranchId is null || string.Equals(BranchId, branchId, StringComparison.OrdinalIgnoreCase)) &&
        (CaseType is null || string.Equals(CaseType, caseType, StringComparison.OrdinalIgnoreCase)) &&
        (PartFamily is null || string.Equals(PartFamily, partFamily?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (Language is null || string.Equals(Language, language?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class FollowUpQueueMember : Entity
{
    public FollowUpQueueMember(Guid id, Guid queueId, Guid userId, int capacity) : base(id)
    {
        QueueId = queueId;
        UserId = userId;
        SetCapacity(capacity);
    }

    private FollowUpQueueMember() : base(Guid.Empty) { }

    public Guid QueueId { get; private set; }
    public Guid UserId { get; private set; }
    /// <summary>Maximum open cases owned at the same time.</summary>
    public int Capacity { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    public string? AvailabilityNote { get; private set; }
    public string? Skills { get; private set; }
    public DateTimeOffset? LastAssignedAtUtc { get; private set; }

    public void SetCapacity(int capacity)
    {
        if (capacity is < 1 or > 500) throw new InvalidOperationException("ظرفیت باید بین ۱ تا ۵۰۰ پرونده باشد.");
        Capacity = capacity;
        Touch();
    }

    public void SetAvailability(bool available, string? note, string? skills)
    {
        IsAvailable = available;
        AvailabilityNote = SlaPolicy.Opt(note, 200);
        Skills = SlaPolicy.Opt(skills, 200);
        Touch();
    }

    public void MarkAssigned(DateTimeOffset nowUtc)
    {
        LastAssignedAtUtc = nowUtc;
        Touch();
    }
}
