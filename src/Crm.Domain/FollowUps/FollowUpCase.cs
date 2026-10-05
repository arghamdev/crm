using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.FollowUps;

/// <summary>
/// Case status (وضعیت انجام کار) — separate from the stage (مرحله): a case can be in «بررسی فنی» and «در انتظار مشتری» at once.
/// «عقب‌افتاده» is not a status; it is computed from the due times.
/// </summary>
public enum FollowUpStatus { AwaitingAssignment, InProgress, WaitingCustomer, WaitingInternal, OnHold, ResolvedPendingApproval, Closed, Cancelled }
public enum FollowUpChannel { Phone, Email, Message, Visit, Portal, InPerson }
public enum FollowUpRelatedKind { None, Opportunity, Quote, Order, Invoice, ServiceCase }
public enum FollowUpStageStatus { Pending, Active, Waiting, Done, Skipped, Returned }
public enum ReferralScope { Stage, Case }
public enum ReferralStatus { Pending, Accepted, Rejected, Cancelled }
public enum FollowUpDocumentKind { Proforma, Technical, StockConfirmation, CustomerApproval, Invoice, Photo, Other }
public enum FollowUpDocumentStatus { Uploaded, PendingApproval, Approved, Rejected }
public enum ApprovalDecision { Approved, NeedsCorrection, Rejected }
public enum FollowUpItemStatus { Pending, Partial, Delivered, Short, Returned, Replaced }

/// <summary>Follow-up types; each brings its own workflow template, extra fields and closing rule.</summary>
public static class FollowUpCaseTypes
{
    public static readonly IReadOnlyList<(string Key, string Label, string ExtraFields)> All =
    [
        ("Proforma", "پیش‌فاکتور", ""),
        ("Inquiry", "استعلام قیمت", ""),
        ("Supply", "تأمین قطعه", ""),
        ("ShipmentDiscrepancy", "مغایرت ارسال", "شماره محموله,نوع مغایرت"),
        ("Warranty", "گارانتی", "شماره سریال / بچ,علت خرابی,تاریخ خرید"),
        ("Collection", "وصول مطالبات", "شماره فاکتور,مبلغ معوق"),
        ("General", "عمومی", "")
    ];

    public static string Label(string? key) => All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)).Label ?? key ?? "—";
    public static IReadOnlyList<string> ExtraFields(string? key) =>
        (All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)).ExtraFields ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static bool Exists(string? key) => All.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Outcome of an activity (ثبت نتیجه); every finished call, meeting or task ends with one of these.</summary>
public static class FollowUpResults
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("Answered", "پاسخ داد"), ("NoAnswer", "پاسخ نداد"), ("IncompleteDocs", "مدرک ناقص"), ("Approved", "تأیید شد"),
        ("NeedsCorrection", "نیازمند اصلاح"), ("NeedsNextAction", "نیازمند اقدام بعدی"), ("Declined", "مشتری منصرف شد")
    ];

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? key ?? "—";
    public static bool Exists(string? key) => All.Any(x => x.Key == key);
}

/// <summary>
/// پرونده پیگیری: the unit of work. It always says what is followed up, who answers for it, which stage it is in, what the
/// next action is and when. Its code never changes, also when the case moves to another branch.
/// </summary>
public sealed class FollowUpCase : Entity, IOrganizationScoped
{
    public FollowUpCase(Guid id, string code, string companyId, string branchId, string? territoryId, Guid customerId, string subject, string caseType,
        Guid templateId, int templateVersion, FollowUpPriority priority, FollowUpChannel channel, Guid createdByUserId, DateTimeOffset nowUtc) : base(id)
    {
        Code = SlaPolicy.Req(code, 24, "شناسه پرونده");
        CompanyId = SlaPolicy.Req(companyId, 32, "شرکت");
        BranchId = SlaPolicy.Req(branchId, 32, "شعبه");
        TerritoryId = SlaPolicy.Opt(territoryId, 32);
        CustomerId = customerId != Guid.Empty ? customerId : throw new InvalidOperationException("مشتری / حساب الزامی است.");
        Subject = SlaPolicy.Req(subject, 200, "موضوع پیگیری");
        CaseType = FollowUpCaseTypes.Exists(caseType) ? caseType : throw new InvalidOperationException("نوع پیگیری معتبر نیست.");
        TemplateId = templateId;
        TemplateVersion = templateVersion;
        Priority = priority;
        Channel = channel;
        CreatedByUserId = createdByUserId;
        OpenedAtUtc = nowUtc;
    }

    private FollowUpCase() : base(Guid.Empty) => Code = CompanyId = BranchId = Subject = CaseType = "EF";

    public string Code { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? ContactId { get; private set; }
    /// <summary>External dealership (نمایندگی) involved in the case; it only sees cases linked to it.</summary>
    public Guid? DealerId { get; private set; }
    public string Subject { get; private set; }
    public string? Description { get; private set; }
    /// <summary>The result that ends the case (معیار پایان), fixed at the start.</summary>
    public string? ExpectedOutcome { get; private set; }
    public string CaseType { get; private set; }
    /// <summary>Type-specific fields ("label: value" per line), e.g. serial number and failure cause for a warranty.</summary>
    public string? ExtraFields { get; private set; }
    public Guid TemplateId { get; private set; }
    public int TemplateVersion { get; private set; }
    public FollowUpPriority Priority { get; private set; }
    /// <summary>Why the priority was chosen (impact on the customer's operation, contractual commitment).</summary>
    public string? PriorityReason { get; private set; }
    public FollowUpChannel Channel { get; private set; }
    public string Language { get; private set; } = "fa";
    public string? PartFamily { get; private set; }
    public FollowUpRelatedKind RelatedKind { get; private set; }
    public Guid? RelatedId { get; private set; }
    public string? RelatedCode { get; private set; }
    public Guid? QueueId { get; private set; }
    /// <summary>مسئول پاسخ‌گوی نهایی پرونده — the customer's single point of contact; stage owners are separate.</summary>
    public Guid? OwnerUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public FollowUpStatus Status { get; private set; } = FollowUpStatus.AwaitingAssignment;
    public int ProgressPercent { get; private set; }

    public Guid? SlaPolicyId { get; private set; }
    public DateTimeOffset? FirstResponseDueAtUtc { get; private set; }
    public DateTimeOffset? FirstRespondedAtUtc { get; private set; }
    public DateTimeOffset? ResolutionDueAtUtc { get; private set; }
    /// <summary>Set while the SLA clock is stopped by an allowed waiting state.</summary>
    public DateTimeOffset? PausedSinceUtc { get; private set; }
    /// <summary>Working minutes the clock was stopped in total (reported separately from the elapsed time).</summary>
    public int PausedMinutes { get; private set; }
    /// <summary>How many escalation steps of the current due time have been raised (idempotency of the monitor).</summary>
    public int EscalationLevel { get; private set; }

    public string? NextAction { get; private set; }
    public DateTimeOffset? NextActionAtUtc { get; private set; }
    public Guid? NextActionOwnerUserId { get; private set; }
    public DateTimeOffset? ReviewAtUtc { get; private set; }
    public string? WaitReason { get; private set; }
    public string? WaitingOn { get; private set; }
    public Guid? WaitStageId { get; private set; }

    public string? Outcome { get; private set; }
    public string? OutcomeNote { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public int ReopenCount { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? MergedIntoCaseId { get; private set; }
    /// <summary>The case this one continues (e.g. order follow-up started when a proforma case closed).</summary>
    public Guid? ParentCaseId { get; private set; }

    public bool IsOpen => Status is not (FollowUpStatus.Closed or FollowUpStatus.Cancelled);
    public bool IsWaiting => Status is FollowUpStatus.WaitingCustomer or FollowUpStatus.WaitingInternal or FollowUpStatus.OnHold;
    public bool IsPaused => PausedSinceUtc is not null;

    /// <summary>Overdue is a label: the first response, the next action or (while the clock runs) the resolution is late.</summary>
    public bool IsOverdue(DateTimeOffset nowUtc) => IsOpen && (
        FirstRespondedAtUtc is null && FirstResponseDueAtUtc < nowUtc && !IsPaused ||
        ResolutionDueAtUtc < nowUtc && !IsPaused ||
        NextActionAtUtc < nowUtc);

    public DateTimeOffset? NearestDueUtc => new[] { FirstRespondedAtUtc is null ? FirstResponseDueAtUtc : null, NextActionAtUtc, IsPaused ? null : ResolutionDueAtUtc }
        .Where(x => x is not null).Min();

    public void Describe(string? description, string? expectedOutcome, Guid? contactId, Guid? dealerId, FollowUpRelatedKind relatedKind, Guid? relatedId,
        string? relatedCode, string? partFamily, string? language, string? extraFields, string? priorityReason, Guid? parentCaseId = null)
    {
        Description = SlaPolicy.Opt(description, 4000);
        ExpectedOutcome = SlaPolicy.Opt(expectedOutcome, 500);
        ContactId = contactId;
        DealerId = dealerId;
        RelatedKind = relatedId is null && string.IsNullOrWhiteSpace(relatedCode) ? FollowUpRelatedKind.None : relatedKind;
        RelatedId = relatedId;
        RelatedCode = SlaPolicy.Opt(relatedCode, 40);
        PartFamily = SlaPolicy.Opt(partFamily, 80);
        Language = SlaPolicy.Opt(language, 16) ?? "fa";
        ExtraFields = SlaPolicy.Opt(extraFields, 2000);
        PriorityReason = SlaPolicy.Opt(priorityReason, 300);
        ParentCaseId = parentCaseId;
        Touch();
    }

    public void ChangeSubject(string subject, FollowUpPriority priority, string? priorityReason)
    {
        EnsureOpen();
        Subject = SlaPolicy.Req(subject, 200, "موضوع پیگیری");
        Priority = priority;
        PriorityReason = SlaPolicy.Opt(priorityReason, 300);
        Touch();
    }

    public void AssignOwner(Guid ownerUserId, Guid? queueId)
    {
        EnsureOpen();
        if (ownerUserId == Guid.Empty) throw new InvalidOperationException("مسئول پرونده الزامی است.");
        OwnerUserId = ownerUserId;
        QueueId = queueId ?? QueueId;
        if (Status == FollowUpStatus.AwaitingAssignment) Status = FollowUpStatus.InProgress;
        Touch();
    }

    public void LeaveUnassigned(Guid? queueId)
    {
        QueueId = queueId;
        OwnerUserId = null;
        Status = FollowUpStatus.AwaitingAssignment;
        Touch();
    }

    /// <summary>Moving to another branch keeps the code, the history and the due times.</summary>
    public void MoveBranch(string branchId, string? territoryId)
    {
        BranchId = SlaPolicy.Req(branchId, 32, "شعبه");
        TerritoryId = SlaPolicy.Opt(territoryId, 32);
        Touch();
    }

    public void ApplySla(Guid? policyId, DateTimeOffset firstResponseDueUtc, DateTimeOffset resolutionDueUtc)
    {
        SlaPolicyId = policyId;
        FirstResponseDueAtUtc = firstResponseDueUtc;
        ResolutionDueAtUtc = resolutionDueUtc;
        EscalationLevel = 0;
        Touch();
    }

    public void SetResolutionDue(DateTimeOffset dueUtc)
    {
        ResolutionDueAtUtc = dueUtc;
        EscalationLevel = 0;
        Touch();
    }

    public void MarkFirstResponse(DateTimeOffset nowUtc)
    {
        if (FirstRespondedAtUtc is not null) return;
        FirstRespondedAtUtc = nowUtc;
        // Escalation now follows the resolution due time, so its steps start over.
        EscalationLevel = 0;
        Touch();
    }

    /// <summary>Every open case has a next action with a due time and an owner.</summary>
    public void PlanNextAction(string nextAction, DateTimeOffset atUtc, Guid ownerUserId, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        if (atUtc <= nowUtc.AddMinutes(-1)) throw new InvalidOperationException("موعد اقدام بعدی باید در آینده باشد.");
        if (ownerUserId == Guid.Empty) throw new InvalidOperationException("مسئول اقدام بعدی الزامی است.");
        NextAction = SlaPolicy.Req(nextAction, 200, "عنوان اقدام بعدی");
        NextActionAtUtc = atUtc;
        NextActionOwnerUserId = ownerUserId;
        Touch();
    }

    public void ClearNextAction()
    {
        NextAction = null;
        NextActionAtUtc = null;
        NextActionOwnerUserId = null;
        Touch();
    }

    /// <summary>
    /// Waiting needs a reason and a review date; the review becomes the next action. The SLA clock stops only when the policy
    /// allows it for this state, so «در انتظار مشتری» cannot hide an internal delay.
    /// </summary>
    public void EnterWait(FollowUpStatus status, string reason, string? waitingOn, Guid? stageId, DateTimeOffset reviewAtUtc, Guid reviewOwnerUserId,
        bool pauseClock, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        if (status is not (FollowUpStatus.WaitingCustomer or FollowUpStatus.WaitingInternal or FollowUpStatus.OnHold))
            throw new InvalidOperationException("وضعیت انتظار معتبر نیست.");
        if (reviewAtUtc <= nowUtc) throw new InvalidOperationException("موعد بازبینی باید در آینده باشد.");
        if (reviewOwnerUserId == Guid.Empty) throw new InvalidOperationException("مسئول بازبینی الزامی است.");
        WaitReason = SlaPolicy.Req(reason, 500, "علت انتظار");
        WaitingOn = SlaPolicy.Opt(waitingOn, 120);
        WaitStageId = stageId;
        ReviewAtUtc = reviewAtUtc;
        Status = status;
        PlanNextAction("بازبینی: " + (WaitReason.Length > 150 ? WaitReason[..150] : WaitReason), reviewAtUtc, reviewOwnerUserId, nowUtc);
        if (pauseClock) PausedSinceUtc ??= nowUtc;
        else PausedSinceUtc = null;
        Touch();
    }

    /// <summary>Ends the wait; the stopped working minutes are added to <see cref="PausedMinutes"/> and the due times move by that much.</summary>
    public void Resume(int pausedWorkingMinutes, DateTimeOffset? newFirstResponseDueUtc, DateTimeOffset? newResolutionDueUtc)
    {
        if (!IsWaiting) throw new InvalidOperationException("پرونده در انتظار نیست.");
        if (PausedSinceUtc is not null)
        {
            PausedMinutes += Math.Max(0, pausedWorkingMinutes);
            if (newFirstResponseDueUtc is { } f && FirstRespondedAtUtc is null) FirstResponseDueAtUtc = f;
            if (newResolutionDueUtc is { } r) ResolutionDueAtUtc = r;
            EscalationLevel = 0;
        }
        PausedSinceUtc = null;
        Status = OwnerUserId is null ? FollowUpStatus.AwaitingAssignment : FollowUpStatus.InProgress;
        WaitReason = null;
        WaitingOn = null;
        WaitStageId = null;
        ReviewAtUtc = null;
        Touch();
    }

    public void SetProgress(int percent)
    {
        ProgressPercent = Math.Clamp(percent, 0, 100);
        Touch();
    }

    public void MarkResolvedPendingApproval()
    {
        EnsureOpen();
        Status = FollowUpStatus.ResolvedPendingApproval;
        Touch();
    }

    public void BackToWork()
    {
        if (Status == FollowUpStatus.ResolvedPendingApproval) Status = FollowUpStatus.InProgress;
        Touch();
    }

    public void Close(string outcome, string? note, Guid userId, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        Outcome = SlaPolicy.Req(outcome, 200, "نتیجه نهایی");
        OutcomeNote = SlaPolicy.Opt(note, 2000);
        Status = FollowUpStatus.Closed;
        ClosedAtUtc = nowUtc;
        ClosedByUserId = userId;
        PausedSinceUtc = null;
        ClearNextAction();
        Touch();
    }

    /// <summary>Reopening keeps the previous closing in the history; it needs a reason, an owner and a new resolution due time.</summary>
    public void Reopen(Guid ownerUserId, DateTimeOffset newResolutionDueUtc, DateTimeOffset nowUtc)
    {
        if (Status != FollowUpStatus.Closed) throw new InvalidOperationException("فقط پرونده بسته‌شده بازگشایی می‌شود.");
        if (newResolutionDueUtc <= nowUtc) throw new InvalidOperationException("مهلت جدید باید در آینده باشد.");
        Status = FollowUpStatus.InProgress;
        OwnerUserId = ownerUserId;
        ResolutionDueAtUtc = newResolutionDueUtc;
        EscalationLevel = 0;
        ReopenCount++;
        Outcome = null;
        OutcomeNote = null;
        ClosedAtUtc = null;
        ClosedByUserId = null;
        Touch();
    }

    public void Cancel(string reason)
    {
        EnsureOpen();
        CancelReason = SlaPolicy.Req(reason, 500, "علت لغو");
        Status = FollowUpStatus.Cancelled;
        PausedSinceUtc = null;
        ClearNextAction();
        Touch();
    }

    public void MergeInto(Guid targetCaseId)
    {
        EnsureOpen();
        if (targetCaseId == Id) throw new InvalidOperationException("پرونده را نمی‌توان با خودش ادغام کرد.");
        MergedIntoCaseId = targetCaseId;
        CancelReason = "ادغام در پرونده دیگر";
        Status = FollowUpStatus.Cancelled;
        ClearNextAction();
        Touch();
    }

    public void RaiseEscalation(int level)
    {
        if (level <= EscalationLevel) return;
        EscalationLevel = level;
        Touch();
    }

    private void EnsureOpen()
    {
        if (!IsOpen) throw new InvalidOperationException("پرونده بسته یا لغو شده است؛ ابتدا آن را بازگشایی کنید.");
    }
}

/// <summary>A stage of the case (مرحله). Its progress comes from its checklist, never from elapsed time or the number of calls.</summary>
public sealed class FollowUpStage : Entity
{
    public FollowUpStage(Guid id, Guid caseId, int order, string name, int weight, bool required, string responsibleRole, bool parallelWithPrevious,
        string? condition, int? durationHours) : base(id)
    {
        CaseId = caseId;
        Order = order;
        Name = SlaPolicy.Req(name, 120, "نام مرحله");
        Weight = Math.Clamp(weight, 0, 100);
        Required = required;
        ResponsibleRole = SlaPolicy.Req(responsibleRole, 60, "نقش مسئول مرحله");
        ParallelWithPrevious = parallelWithPrevious;
        Condition = SlaPolicy.Opt(condition, 200);
        DurationHours = durationHours;
    }

    private FollowUpStage() : base(Guid.Empty) => Name = ResponsibleRole = "EF";

    public Guid CaseId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; }
    public int Weight { get; private set; }
    public bool Required { get; private set; }
    public string ResponsibleRole { get; private set; }
    /// <summary>مسئول اجرای مرحله — may differ from the case owner (technical engineer, warehouse…).</summary>
    public Guid? ResponsibleUserId { get; private set; }
    public FollowUpStageStatus Status { get; private set; } = FollowUpStageStatus.Pending;
    public bool ParallelWithPrevious { get; private set; }
    public string? Condition { get; private set; }
    public int? DurationHours { get; private set; }
    public int Progress { get; private set; }
    public DateTimeOffset? DueAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public string? ReturnReason { get; private set; }
    public string? SkipReason { get; private set; }

    public bool IsWorking => Status is FollowUpStageStatus.Active or FollowUpStageStatus.Returned or FollowUpStageStatus.Waiting;
    public bool IsOverdue(DateTimeOffset nowUtc) => IsWorking && DueAtUtc < nowUtc;

    public void Activate(Guid? responsibleUserId, DateTimeOffset? dueUtc, DateTimeOffset nowUtc)
    {
        if (Status != FollowUpStageStatus.Pending) return;
        Status = FollowUpStageStatus.Active;
        ResponsibleUserId ??= responsibleUserId;
        DueAtUtc = dueUtc;
        StartedAtUtc = nowUtc;
        Touch();
    }

    public void AssignResponsible(Guid userId)
    {
        ResponsibleUserId = userId;
        Touch();
    }

    public void SetDue(DateTimeOffset? dueUtc)
    {
        DueAtUtc = dueUtc;
        Touch();
    }

    public void SetWaiting(bool waiting)
    {
        if (waiting && Status is FollowUpStageStatus.Active or FollowUpStageStatus.Returned) Status = FollowUpStageStatus.Waiting;
        else if (!waiting && Status == FollowUpStageStatus.Waiting) Status = ReturnReason is null ? FollowUpStageStatus.Active : FollowUpStageStatus.Returned;
        Touch();
    }

    public void SetProgress(int percent)
    {
        Progress = Status == FollowUpStageStatus.Done ? 100 : Math.Clamp(percent, 0, 100);
        Touch();
    }

    public void Complete(Guid userId, DateTimeOffset nowUtc)
    {
        if (!IsWorking) throw new InvalidOperationException("فقط مرحلهٔ فعال قابل تکمیل است.");
        Status = FollowUpStageStatus.Done;
        Progress = 100;
        CompletedAtUtc = nowUtc;
        CompletedByUserId = userId;
        ReturnReason = null;
        Touch();
    }

    /// <summary>A stage whose condition does not apply is left out of the progress; required stages cannot be skipped.</summary>
    public void Skip(string reason)
    {
        if (Required) throw new InvalidOperationException("مرحلهٔ الزامی را نمی‌توان کنار گذاشت.");
        if (Status == FollowUpStageStatus.Done) throw new InvalidOperationException("مرحلهٔ تکمیل‌شده را نمی‌توان کنار گذاشت.");
        SkipReason = SlaPolicy.Req(reason, 300, "دلیل کنار گذاشتن مرحله");
        Status = FollowUpStageStatus.Skipped;
        Touch();
    }

    /// <summary>Sending a stage back for correction lowers the progress; the reason is kept.</summary>
    public void Return(string reason, int progress, DateTimeOffset? dueUtc, DateTimeOffset nowUtc)
    {
        if (Status is FollowUpStageStatus.Pending or FollowUpStageStatus.Skipped) throw new InvalidOperationException("این مرحله هنوز شروع نشده است.");
        ReturnReason = SlaPolicy.Req(reason, 500, "دلیل برگشت");
        Status = FollowUpStageStatus.Returned;
        Progress = Math.Clamp(progress, 0, 99);
        CompletedAtUtc = null;
        CompletedByUserId = null;
        StartedAtUtc ??= nowUtc;
        if (dueUtc is not null) DueAtUtc = dueUtc;
        Touch();
    }
}

public sealed class FollowUpChecklistItem : Entity
{
    public FollowUpChecklistItem(Guid id, Guid caseId, Guid stageId, int order, string title) : base(id)
    {
        CaseId = caseId;
        StageId = stageId;
        Order = order;
        Title = SlaPolicy.Req(title, 200, "عنوان مورد چک‌لیست");
    }

    private FollowUpChecklistItem() : base(Guid.Empty) => Title = "EF";

    public Guid CaseId { get; private set; }
    public Guid StageId { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; }
    public bool IsDone { get; private set; }
    public Guid? DoneByUserId { get; private set; }
    public DateTimeOffset? DoneAtUtc { get; private set; }

    public void Check(Guid userId, DateTimeOffset nowUtc)
    {
        if (IsDone) return;
        IsDone = true;
        DoneByUserId = userId;
        DoneAtUtc = nowUtc;
        Touch();
    }

    public void Uncheck()
    {
        if (!IsDone) return;
        IsDone = false;
        DoneByUserId = null;
        DoneAtUtc = null;
        Touch();
    }
}

/// <summary>A part line of the case (قطعه مرتبط) with its own delivery status, so a partial delivery never closes the whole case.</summary>
public sealed class FollowUpItem : Entity
{
    public FollowUpItem(Guid id, Guid caseId, string partCode, string? description, decimal quantity, string? unit) : base(id)
    {
        CaseId = caseId;
        PartCode = SlaPolicy.Req(partCode, 60, "کد قطعه");
        Description = SlaPolicy.Opt(description, 200);
        if (quantity <= 0 || quantity > 1_000_000) throw new InvalidOperationException("تعداد باید بیشتر از صفر باشد.");
        Quantity = quantity;
        Unit = SlaPolicy.Opt(unit, 20) ?? "عدد";
    }

    private FollowUpItem() : base(Guid.Empty) => PartCode = Unit = "EF";

    public Guid CaseId { get; private set; }
    public string PartCode { get; private set; }
    public string? AlternateCode { get; private set; }
    public string? Description { get; private set; }
    public string? Compatibility { get; private set; }
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; }
    public string? Warehouse { get; private set; }
    public string? SerialOrBatch { get; private set; }
    public FollowUpItemStatus Status { get; private set; } = FollowUpItemStatus.Pending;
    public decimal DeliveredQuantity { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Removed lines stay in the database for the history and are hidden from the case.</summary>
    public bool IsRemoved { get; private set; }

    public void Remove()
    {
        IsRemoved = true;
        Touch();
    }

    public void Describe(string? alternateCode, string? compatibility, string? warehouse, string? serialOrBatch)
    {
        AlternateCode = SlaPolicy.Opt(alternateCode, 60);
        Compatibility = SlaPolicy.Opt(compatibility, 200);
        Warehouse = SlaPolicy.Opt(warehouse, 60);
        SerialOrBatch = SlaPolicy.Opt(serialOrBatch, 60);
        Touch();
    }

    public void RecordDelivery(decimal deliveredQuantity, FollowUpItemStatus status, string? note)
    {
        if (deliveredQuantity < 0 || deliveredQuantity > Quantity * 2) throw new InvalidOperationException("مقدار تحویل معتبر نیست.");
        DeliveredQuantity = deliveredQuantity;
        Status = status == FollowUpItemStatus.Pending && deliveredQuantity > 0
            ? deliveredQuantity >= Quantity ? FollowUpItemStatus.Delivered : FollowUpItemStatus.Partial
            : status;
        Note = SlaPolicy.Opt(note, 300);
        Touch();
    }
}

/// <summary>
/// ارجاع: who sends what to whom, why, and by when it must be accepted. Until the receiver accepts, the current owner stays
/// responsible, so work is never left without an owner while it moves.
/// </summary>
public sealed class FollowUpReferral : Entity
{
    public FollowUpReferral(Guid id, Guid caseId, string companyId, ReferralScope scope, Guid? stageId, Guid fromUserId, Guid toUserId, string? toBranchId,
        string? toTeam, string reason, DateTimeOffset acceptDueAtUtc, bool includeHistory, bool includeQuote, bool includeTechnical, DateTimeOffset nowUtc) : base(id)
    {
        if (scope == ReferralScope.Stage && stageId is null) throw new InvalidOperationException("برای ارجاع مرحله، مرحله را انتخاب کنید.");
        if (toUserId == Guid.Empty) throw new InvalidOperationException("گیرنده ارجاع الزامی است.");
        if (toUserId == fromUserId) throw new InvalidOperationException("ارجاع به خود مجاز نیست.");
        if (acceptDueAtUtc <= nowUtc) throw new InvalidOperationException("مهلت پذیرش باید در آینده باشد.");
        CaseId = caseId;
        CompanyId = companyId;
        Scope = scope;
        StageId = scope == ReferralScope.Stage ? stageId : null;
        FromUserId = fromUserId;
        ToUserId = toUserId;
        ToBranchId = SlaPolicy.Opt(toBranchId, 32);
        ToTeam = SlaPolicy.Opt(toTeam, 120);
        Reason = SlaPolicy.Req(reason, 500, "دلیل ارجاع");
        AcceptDueAtUtc = acceptDueAtUtc;
        IncludeHistory = includeHistory;
        IncludeQuote = includeQuote;
        IncludeTechnical = includeTechnical;
        SentAtUtc = nowUtc;
    }

    private FollowUpReferral() : base(Guid.Empty) => CompanyId = Reason = "EF";

    public Guid CaseId { get; private set; }
    public string CompanyId { get; private set; }
    public ReferralScope Scope { get; private set; }
    public Guid? StageId { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid ToUserId { get; private set; }
    public string? ToBranchId { get; private set; }
    public string? ToTeam { get; private set; }
    public string Reason { get; private set; }
    public DateTimeOffset SentAtUtc { get; private set; }
    public DateTimeOffset AcceptDueAtUtc { get; private set; }
    public bool IncludeHistory { get; private set; }
    public bool IncludeQuote { get; private set; }
    public bool IncludeTechnical { get; private set; }
    public ReferralStatus Status { get; private set; } = ReferralStatus.Pending;
    public DateTimeOffset? RespondedAtUtc { get; private set; }
    public string? ResponseNote { get; private set; }
    public bool LateReminderSent { get; private set; }

    public bool IsLate(DateTimeOffset nowUtc) => Status == ReferralStatus.Pending && AcceptDueAtUtc < nowUtc;

    public void Accept(Guid userId, string? note, DateTimeOffset nowUtc)
    {
        EnsurePendingFor(userId);
        Status = ReferralStatus.Accepted;
        RespondedAtUtc = nowUtc;
        ResponseNote = SlaPolicy.Opt(note, 500);
        Touch();
    }

    public void Reject(Guid userId, string reason, DateTimeOffset nowUtc)
    {
        EnsurePendingFor(userId);
        var note = SlaPolicy.Req(reason, 500, "دلیل رد ارجاع");
        Status = ReferralStatus.Rejected;
        RespondedAtUtc = nowUtc;
        ResponseNote = note;
        Touch();
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        if (Status != ReferralStatus.Pending) return;
        Status = ReferralStatus.Cancelled;
        RespondedAtUtc = nowUtc;
        Touch();
    }

    public void MarkLateReminderSent()
    {
        LateReminderSent = true;
        Touch();
    }

    private void EnsurePendingFor(Guid userId)
    {
        if (Status != ReferralStatus.Pending) throw new InvalidOperationException("به این ارجاع قبلاً پاسخ داده شده است.");
        if (userId != ToUserId) throw new UnauthorizedAccessException("فقط گیرندهٔ ارجاع می‌تواند آن را بپذیرد یا رد کند.");
    }
}

/// <summary>A versioned document of the case; the file itself is a <c>CrmDocument</c> (also linked to the customer's account file).</summary>
public sealed class FollowUpDocument : Entity
{
    public FollowUpDocument(Guid id, Guid caseId, Guid? stageId, FollowUpDocumentKind kind, string title, int documentVersion, Guid documentId,
        Guid uploadedByUserId, bool needsApproval) : base(id)
    {
        CaseId = caseId;
        StageId = stageId;
        Kind = kind;
        Title = SlaPolicy.Req(title, 200, "عنوان مدرک");
        DocumentVersion = Math.Max(1, documentVersion);
        DocumentId = documentId;
        UploadedByUserId = uploadedByUserId;
        Status = needsApproval ? FollowUpDocumentStatus.PendingApproval : FollowUpDocumentStatus.Uploaded;
    }

    private FollowUpDocument() : base(Guid.Empty) => Title = "EF";

    public Guid CaseId { get; private set; }
    public Guid? StageId { get; private set; }
    public FollowUpDocumentKind Kind { get; private set; }
    public string Title { get; private set; }
    public int DocumentVersion { get; private set; }
    public Guid DocumentId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public FollowUpDocumentStatus Status { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewNote { get; private set; }

    public void RequestApproval()
    {
        if (Status == FollowUpDocumentStatus.Approved) return;
        Status = FollowUpDocumentStatus.PendingApproval;
        Touch();
    }

    public void Review(bool approved, Guid userId, string? note, DateTimeOffset nowUtc)
    {
        Status = approved ? FollowUpDocumentStatus.Approved : FollowUpDocumentStatus.Rejected;
        ReviewedByUserId = userId;
        ReviewedAtUtc = nowUtc;
        ReviewNote = SlaPolicy.Opt(note, 500);
        Touch();
    }
}

/// <summary>Approval request of a stage (مدارک و تأییدها). The decision is recorded with the approver, time and reason.</summary>
public sealed class FollowUpApproval : Entity
{
    public FollowUpApproval(Guid id, Guid caseId, Guid? stageId, Guid requestedByUserId, Guid approverUserId, string approverRole, string? reviewItems,
        DateTimeOffset nowUtc) : base(id)
    {
        if (approverUserId == Guid.Empty) throw new InvalidOperationException("تأییدکننده الزامی است.");
        if (approverUserId == requestedByUserId) throw new InvalidOperationException("درخواست‌کننده نمی‌تواند تأییدکنندهٔ همان درخواست باشد.");
        CaseId = caseId;
        StageId = stageId;
        RequestedByUserId = requestedByUserId;
        ApproverUserId = approverUserId;
        ApproverRole = SlaPolicy.Opt(approverRole, 60) ?? "تأییدکننده";
        ReviewItems = SlaPolicy.Opt(reviewItems, 1000);
        RequestedAtUtc = nowUtc;
    }

    private FollowUpApproval() : base(Guid.Empty) => ApproverRole = "EF";

    public Guid CaseId { get; private set; }
    public Guid? StageId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid ApproverUserId { get; private set; }
    public string ApproverRole { get; private set; }
    /// <summary>Review items, one per line; the approver ticks the ones that pass.</summary>
    public string? ReviewItems { get; private set; }
    public string? PassedItems { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public ApprovalDecision? Decision { get; private set; }
    public string? DecisionNote { get; private set; }
    public Guid? CorrectionOwnerUserId { get; private set; }
    public DateTimeOffset? CorrectionDueAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public bool IsPending => Decision is null;

    public void Decide(ApprovalDecision decision, string note, IEnumerable<string> passedItems, Guid? correctionOwnerUserId, DateTimeOffset? correctionDueUtc,
        Guid userId, DateTimeOffset nowUtc)
    {
        if (!IsPending) throw new InvalidOperationException("برای این درخواست قبلاً تصمیم ثبت شده است.");
        if (decision == ApprovalDecision.NeedsCorrection && (correctionOwnerUserId is null || correctionDueUtc is null || correctionDueUtc <= nowUtc))
            throw new InvalidOperationException("برای «نیازمند اصلاح»، مسئول و مهلت اصلاح (در آینده) الزامی است.");
        DecisionNote = SlaPolicy.Req(note, 1000, "توضیح تصمیم");
        Decision = decision;
        PassedItems = SlaPolicy.Opt(string.Join('\n', passedItems), 1000);
        CorrectionOwnerUserId = decision == ApprovalDecision.NeedsCorrection ? correctionOwnerUserId : null;
        CorrectionDueAtUtc = decision == ApprovalDecision.NeedsCorrection ? correctionDueUtc : null;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
        Touch();
    }

    public IReadOnlyList<string> ReviewItemList() => (ReviewItems ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public IReadOnlyList<string> PassedItemList() => (PassedItems ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Append-only history: every change of owner, due time, status, stage, progress and result with who, when and why.</summary>
public sealed class FollowUpEvent : Entity
{
    public FollowUpEvent(Guid id, Guid caseId, string companyId, string kind, string title, string? detail, Guid? actorUserId, DateTimeOffset atUtc) : base(id)
    {
        CaseId = caseId;
        CompanyId = companyId;
        Kind = SlaPolicy.Req(kind, 32, "نوع رویداد");
        Title = SlaPolicy.Req(title, 300, "عنوان رویداد");
        Detail = SlaPolicy.Opt(detail, 2000);
        ActorUserId = actorUserId;
        AtUtc = atUtc;
    }

    private FollowUpEvent() : base(Guid.Empty) => CompanyId = Kind = Title = "EF";

    public Guid CaseId { get; private set; }
    public string CompanyId { get; private set; }
    public string Kind { get; private set; }
    public string Title { get; private set; }
    public string? Detail { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }
}

/// <summary>Links an account activity (call, meeting, task) to a case and a stage, with the channel and the coded result.</summary>
public sealed class FollowUpActivityLink : Entity
{
    public FollowUpActivityLink(Guid activityId, Guid caseId, Guid? stageId, FollowUpChannel channel, string? preChecklist) : base(activityId)
    {
        CaseId = caseId;
        StageId = stageId;
        Channel = channel;
        PreChecklist = SlaPolicy.Opt(preChecklist, 1000);
    }

    private FollowUpActivityLink() : base(Guid.Empty) { }

    public Guid ActivityId => Id;
    public Guid CaseId { get; private set; }
    public Guid? StageId { get; private set; }
    public FollowUpChannel Channel { get; private set; }
    /// <summary>Checklist before the contact, one item per line; checked items start with "✓ ".</summary>
    public string? PreChecklist { get; private set; }
    public string? ResultCode { get; private set; }
    public bool StageCompletionRequested { get; private set; }

    public void RecordResult(string resultCode, bool requestStageCompletion)
    {
        if (!FollowUpResults.Exists(resultCode)) throw new InvalidOperationException("نتیجهٔ پیگیری را انتخاب کنید.");
        ResultCode = resultCode;
        StageCompletionRequested = requestStageCompletion;
        Touch();
    }
}

/// <summary>A saved manager view (نمای نظارت) — the filter of the oversight dashboard.</summary>
public sealed class FollowUpSavedView : Entity
{
    public FollowUpSavedView(Guid id, Guid userId, string companyId, string name, string query) : base(id)
    {
        UserId = userId;
        CompanyId = companyId;
        Name = SlaPolicy.Req(name, 80, "نام نما");
        Query = SlaPolicy.Opt(query, 500) ?? string.Empty;
    }

    private FollowUpSavedView() : base(Guid.Empty) => CompanyId = Name = Query = "EF";

    public Guid UserId { get; private set; }
    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public string Query { get; private set; }

    public void Update(string name, string query)
    {
        Name = SlaPolicy.Req(name, 80, "نام نما");
        Query = SlaPolicy.Opt(query, 500) ?? string.Empty;
        Touch();
    }
}

/// <summary>
/// Server-side draft of a form (پیش‌نویس) for one user and company. The payload is the form's own fields as JSON, so the form can be
/// restored on any device; it is deleted when the record is created from it.
/// </summary>
public sealed class FollowUpDraft : Entity
{
    public const int MaxPayload = 16000;

    public FollowUpDraft(Guid id, Guid userId, string companyId, string kind, string payload, DateTimeOffset nowUtc) : base(id)
    {
        UserId = userId;
        CompanyId = SlaPolicy.Req(companyId, 32, "شرکت");
        Kind = SlaPolicy.Req(kind, 32, "نوع پیش‌نویس");
        Payload = string.Empty;
        Update(payload, nowUtc);
    }

    private FollowUpDraft() : base(Guid.Empty) => CompanyId = Kind = Payload = "EF";

    public Guid UserId { get; private set; }
    public string CompanyId { get; private set; }
    public string Kind { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset SavedAtUtc { get; private set; }

    public void Update(string payload, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new InvalidOperationException("پیش‌نویس خالی است.");
        if (payload.Length > MaxPayload) throw new InvalidOperationException("پیش‌نویس بیش از حد بزرگ است.");
        Payload = payload;
        SavedAtUtc = nowUtc;
        Touch();
    }
}

/// <summary>
/// Weighted progress: Σ(stage weight × stage progress) ÷ Σ(weights of the stages that apply). Skipped conditional stages are
/// left out; a cancelled case keeps its progress (cancelling is not completing).
/// </summary>
public static class FollowUpProgress
{
    public static int StageProgress(FollowUpStageStatus status, int checklistDone, int checklistTotal, int current) => status switch
    {
        FollowUpStageStatus.Done => 100,
        FollowUpStageStatus.Pending or FollowUpStageStatus.Skipped => 0,
        _ when checklistTotal > 0 => Math.Min(99, (int)Math.Round(checklistDone * 100.0 / checklistTotal)),
        _ => Math.Min(current, 99)
    };

    public static int Total(IEnumerable<(int Weight, FollowUpStageStatus Status, int Progress)> stages)
    {
        var counted = stages.Where(x => x.Status != FollowUpStageStatus.Skipped).ToList();
        var weights = counted.Sum(x => x.Weight);
        return weights == 0 ? 0 : (int)Math.Round(counted.Sum(x => x.Weight * (double)x.Progress) / weights);
    }
}
