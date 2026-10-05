using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.FollowUps;

namespace Crm.Web.Presentation;

/// <summary>Persian labels, tones and icons of the follow-up center (مرکز پیگیری).</summary>
public static class FollowUpLabels
{
    public static string Status(FollowUpStatus status) => status switch
    {
        FollowUpStatus.AwaitingAssignment => "در انتظار تخصیص",
        FollowUpStatus.InProgress => "در حال انجام",
        FollowUpStatus.WaitingCustomer => "در انتظار مشتری",
        FollowUpStatus.WaitingInternal => "در انتظار واحد داخلی",
        FollowUpStatus.OnHold => "متوقف",
        FollowUpStatus.ResolvedPendingApproval => "حل‌شده، در انتظار تأیید",
        FollowUpStatus.Closed => "بسته‌شده",
        _ => "لغوشده"
    };

    public static string StatusTone(FollowUpStatus status) => status switch
    {
        FollowUpStatus.AwaitingAssignment => "warning",
        FollowUpStatus.InProgress => "info",
        FollowUpStatus.WaitingCustomer or FollowUpStatus.WaitingInternal => "violet",
        FollowUpStatus.OnHold => "warning",
        FollowUpStatus.ResolvedPendingApproval => "primary",
        FollowUpStatus.Closed => "success",
        _ => "neutral"
    };

    public static string Priority(FollowUpPriority priority) => priority switch
    {
        FollowUpPriority.Low => "کم", FollowUpPriority.Normal => "عادی", FollowUpPriority.High => "بالا", _ => "بحرانی"
    };

    public static string PriorityTone(FollowUpPriority priority) => priority switch
    {
        FollowUpPriority.Low => "neutral", FollowUpPriority.Normal => "info", FollowUpPriority.High => "warning", _ => "danger"
    };

    public static string PriorityHint(FollowUpPriority priority) => priority switch
    {
        FollowUpPriority.Critical => "توقف تولید یا فعالیت مشتری",
        FollowUpPriority.High => "اثر جدی بر مشتری",
        FollowUpPriority.Low => "بدون فوریت",
        _ => "روال عادی"
    };

    public static string Type(string? caseType) => FollowUpCaseTypes.Label(caseType);

    public static string TypeIcon(string? caseType) => caseType switch
    {
        "Proforma" or "Inquiry" => "i-file",
        "Supply" => "i-store",
        "ShipmentDiscrepancy" => "i-alert",
        "Warranty" => "i-shield",
        "Collection" => "i-check",
        _ => "i-task"
    };

    public static string Channel(FollowUpChannel channel) => channel switch
    {
        FollowUpChannel.Phone => "تلفن", FollowUpChannel.Email => "ایمیل", FollowUpChannel.Message => "پیام‌رسان", FollowUpChannel.Visit => "بازدید حضوری",
        FollowUpChannel.Portal => "پرتال", _ => "مراجعه حضوری"
    };

    public static string ChannelIcon(FollowUpChannel channel) => channel switch
    {
        FollowUpChannel.Phone => "i-phone", FollowUpChannel.Email => "i-mail", FollowUpChannel.Message => "i-chat", FollowUpChannel.Visit => "i-pin",
        FollowUpChannel.Portal => "i-globe", _ => "i-building"
    };

    public static string Related(FollowUpRelatedKind kind) => kind switch
    {
        FollowUpRelatedKind.Opportunity => "فرصت فروش", FollowUpRelatedKind.Quote => "پیش‌فاکتور", FollowUpRelatedKind.Order => "سفارش",
        FollowUpRelatedKind.Invoice => "فاکتور (حسابداری)", FollowUpRelatedKind.ServiceCase => "درخواست خدمات", _ => "بدون رکورد مرتبط"
    };

    public static string Stage(FollowUpStageStatus status) => status switch
    {
        FollowUpStageStatus.Pending => "شروع‌نشده", FollowUpStageStatus.Active => "در حال انجام", FollowUpStageStatus.Waiting => "در انتظار",
        FollowUpStageStatus.Done => "تکمیل‌شده", FollowUpStageStatus.Skipped => "کنار گذاشته", _ => "برگشت برای اصلاح"
    };

    public static string StageTone(FollowUpStageStatus status) => status switch
    {
        FollowUpStageStatus.Done => "success", FollowUpStageStatus.Active => "info", FollowUpStageStatus.Waiting => "violet",
        FollowUpStageStatus.Returned => "danger", _ => "neutral"
    };

    public static string Referral(ReferralStatus status) => status switch
    {
        ReferralStatus.Pending => "در انتظار پذیرش", ReferralStatus.Accepted => "پذیرفته شد", ReferralStatus.Rejected => "رد شد", _ => "لغو شد"
    };

    public static string ReferralTone(ReferralStatus status) => status switch
    {
        ReferralStatus.Pending => "warning", ReferralStatus.Accepted => "success", ReferralStatus.Rejected => "danger", _ => "neutral"
    };

    public static string Document(FollowUpDocumentKind kind) => FollowUpService.DocumentKindLabel(kind);

    public static string DocumentStatus(FollowUpDocumentStatus status) => status switch
    {
        FollowUpDocumentStatus.Uploaded => "ثبت‌شده", FollowUpDocumentStatus.PendingApproval => "در انتظار تأیید", FollowUpDocumentStatus.Approved => "تأییدشده",
        _ => "ردشده"
    };

    public static string DocumentTone(FollowUpDocumentStatus status) => status switch
    {
        FollowUpDocumentStatus.Approved => "success", FollowUpDocumentStatus.PendingApproval => "warning", FollowUpDocumentStatus.Rejected => "danger", _ => "info"
    };

    public static string Decision(ApprovalDecision? decision) => decision switch
    {
        ApprovalDecision.Approved => "تأیید شد", ApprovalDecision.NeedsCorrection => "نیازمند اصلاح", ApprovalDecision.Rejected => "رد شد", _ => "در انتظار تصمیم"
    };

    public static string DecisionTone(ApprovalDecision? decision) => decision switch
    {
        ApprovalDecision.Approved => "success", ApprovalDecision.NeedsCorrection => "warning", ApprovalDecision.Rejected => "danger", _ => "info"
    };

    public static string Item(FollowUpItemStatus status) => status switch
    {
        FollowUpItemStatus.Pending => "در انتظار", FollowUpItemStatus.Partial => "تحویل ناقص", FollowUpItemStatus.Delivered => "تحویل کامل",
        FollowUpItemStatus.Short => "کسری", FollowUpItemStatus.Returned => "مرجوعی", _ => "تعویض‌شده"
    };

    public static string ItemTone(FollowUpItemStatus status) => status switch
    {
        FollowUpItemStatus.Delivered => "success", FollowUpItemStatus.Partial or FollowUpItemStatus.Short => "warning", FollowUpItemStatus.Returned => "danger", _ => "neutral"
    };

    public static string Activity(ActivityType type) => type switch { ActivityType.Call => "تماس", ActivityType.Meeting => "جلسه", ActivityType.Task => "کار داخلی", _ => "فعالیت" };

    public static string ActivityIcon(ActivityType type) => type switch { ActivityType.Call => "i-phone", ActivityType.Meeting => "i-calendar", _ => "i-task" };

    public static string Result(string? code) => FollowUpResults.Label(code);

    public static string EventIcon(string kind) => kind switch
    {
        "Created" => "i-plus", "Owner" or "Referral" => "i-send", "Branch" => "i-building", "Stage" or "Checklist" => "i-task", "Progress" => "i-chevrons",
        "Wait" or "Resume" => "i-clock", "Document" => "i-file", "Approval" => "i-shield", "Closed" => "i-check", "Reopened" => "i-sort",
        "Escalation" => "i-alert", "Action" or "NextAction" => "i-calendar", "Result" => "i-phone", _ => "i-info"
    };

    public static string Escalation(EscalationTarget target) => FollowUpConfigurationService.EscalationLabel(target);

    public static string Method(AssignmentMethod method) => method == AssignmentMethod.MostFreeCapacity ? "بیشترین ظرفیت آزاد" : "نوبت چرخشی";

    public static string Ordering(QueueOrdering ordering) => ordering switch
    {
        QueueOrdering.UrgencyThenDue => "فوریت و نزدیکی مهلت", QueueOrdering.DueFirst => "نزدیک‌ترین مهلت", _ => "ترتیب ورود"
    };

    public static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "شنبه", DayOfWeek.Sunday => "یکشنبه", DayOfWeek.Monday => "دوشنبه", DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه", DayOfWeek.Thursday => "پنجشنبه", _ => "جمعه"
    };

    public static readonly DayOfWeek[] WeekDays =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static string Template(FollowUpTemplateStatus status) => status switch
    {
        FollowUpTemplateStatus.Draft => "پیش‌نویس", FollowUpTemplateStatus.Published => "منتشرشده", _ => "بایگانی"
    };

    /// <summary>Western digits to Persian digits («۱۴۰۵/۰۷/۱۳ ۱۶:۳۰»).</summary>
    public static string Fa(object? value)
    {
        var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++) if (chars[i] is >= '0' and <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
        return new string(chars);
    }

    private static readonly string[] Ones = ["صفر", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه", "ده", "یازده", "دوازده", "سیزده", "چهارده",
        "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
    private static readonly string[] Tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];

    /// <summary>0–100 in Persian words («هفتاد و پنج») as the case page reads them («هفتاد درصد»).</summary>
    public static string Words(int value)
    {
        if (value is < 0 or > 100) return Fa(value);
        if (value == 100) return "صد";
        if (value < 20) return Ones[value];
        return value % 10 == 0 ? Tens[value / 10] : $"{Tens[value / 10]} و {Ones[value % 10]}";
    }

    /// <summary>Tehran date and time relative to today («امروز ۱۶:۳۰», «فردا ۱۰:۰۰», «۱۴۰۵/۰۷/۱۶ ۱۰:۰۰»).</summary>
    public static string When(DateTimeOffset? utc, DateTimeOffset nowUtc)
    {
        if (utc is not { } at) return "—";
        var day = Crm.Domain.Common.TehranTime.Today(at);
        var today = Crm.Domain.Common.TehranTime.Today(nowUtc);
        var clock = Fa(Crm.Domain.Common.TehranTime.Clock(at));
        return day == today ? $"امروز {clock}" : day == today.AddDays(1) ? $"فردا {clock}" : day == today.AddDays(-1) ? $"دیروز {clock}" :
            Fa(Crm.Domain.Common.TehranTime.Format(at));
    }

    public static string StageIcon(string name) => name switch
    {
        _ when name.Contains("نیاز") || name.Contains("دریافت") => "i-inbox",
        _ when name.Contains("فنی") || name.Contains("بررسی") => "i-flask",
        _ when name.Contains("موجودی") || name.Contains("انبار") || name.Contains("تأمین") => "i-box",
        _ when name.Contains("پیش‌فاکتور") || name.Contains("قیمت") => "i-file",
        _ when name.Contains("تأیید") => "i-shield",
        _ when name.Contains("ارسال") || name.Contains("تحویل") => "i-send",
        _ when name.Contains("پرداخت") || name.Contains("وصول") => "i-check",
        _ => "i-task"
    };

    /// <summary>Working minutes as «۲ روز و ۳ ساعت کاری» (a working day is nine hours by default).</summary>
    public static string Minutes(int minutes)
    {
        if (minutes <= 0) return "۰";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest} دقیقه" : rest == 0 ? $"{hours} ساعت" : $"{hours} ساعت و {rest} دقیقه";
    }
}

/// <summary>The follow-up list workspace (مرکز پیگیری).</summary>
public sealed record FollowUpListPage(FollowUpListDto List, ListState State, IReadOnlyList<OrganizationUnitOptionDto> Branches,
    IReadOnlyList<FollowUpReferralDto> Inbox, DateTimeOffset NowUtc)
{
    public const string Target = "followUpList";
    public const string FormId = "followUpFilters";

    public static FollowUpListPage Create(FollowUpListDto list, IReadOnlyList<OrganizationUnitOptionDto> branches, IReadOnlyList<FollowUpReferralDto> inbox,
        DateTimeOffset nowUtc) => new(list, new ListState("/follow-ups", "/follow-ups/table", new Dictionary<string, string?>
        {
            ["view"] = list.View == "all" ? null : list.View,
            ["q"] = list.Query,
            ["type"] = list.CaseType,
            ["status"] = list.Status?.ToString(),
            ["priority"] = list.Priority?.ToString(),
            ["branchId"] = list.BranchId,
            ["owner"] = list.OwnerUserId?.ToString(),
            ["sort"] = list.Sort == "due" ? null : list.Sort,
            ["pageSize"] = list.PageSize == 10 ? null : list.PageSize.ToString(),
            ["page"] = list.Page > 1 ? list.Page.ToString() : null
        }), branches, inbox, nowUtc);

    public string BranchName(string branchId) => Branches.FirstOrDefault(x => string.Equals(x.Id, branchId, StringComparison.OrdinalIgnoreCase))?.Name ?? branchId;
}

/// <summary>Header of a follow-up form sheet (v1.11.0): the numbered badge of the form image, the title, a subtitle and the case chip.</summary>
public sealed record FollowUpSheetHead(int Number, string Title, string? Subtitle = null, string? Chip = null, string ChipTone = "", string? CloseHref = null)
{
    public static string CaseChip(FollowUpCaseDto c) => $"{c.Code} / {c.CustomerName}";
}
