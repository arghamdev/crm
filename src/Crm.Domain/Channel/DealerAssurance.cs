using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Channel;

public enum DealerGuaranteeType { BankGuarantee, Cheque, PromissoryNote, CashDeposit, PropertyCollateral }
public enum DealerGuaranteeStatus { Active, Released, Forfeited }
public enum DealerTrainingTopic { Product, Sales, AfterSales, Systems, Compliance }
public enum CommissionPayoutStatus { Pending, Sent, DeadLetter }

/// <summary>
/// Security a dealer has lodged against its credit (bank guarantee, cheque, promissory note, deposit, collateral).
/// A guarantee is effective while active and not past its expiry; releasing (returning) or forfeiting (cashing) it
/// is a recorded decision.
/// </summary>
public sealed class DealerGuarantee : Entity, IOrganizationScoped
{
    public DealerGuarantee(Guid id, Guid dealerId, string companyId, string branchId, string? territoryId, DealerGuaranteeType type,
        string number, string? issuer, decimal amount, DateOnly issuedOn, DateOnly? expiresOn, string? notes, Guid registeredByUserId) : base(id)
    {
        if (amount <= 0) throw new InvalidOperationException("مبلغ تضمین باید مثبت باشد.");
        if (type is DealerGuaranteeType.BankGuarantee or DealerGuaranteeType.Cheque && string.IsNullOrWhiteSpace(issuer))
            throw new InvalidOperationException("برای ضمانت‌نامه بانکی و چک، نام بانک صادرکننده الزامی است.");
        if (type == DealerGuaranteeType.BankGuarantee && expiresOn is null)
            throw new InvalidOperationException("ضمانت‌نامه بانکی تاریخ سررسید لازم دارد.");
        if (expiresOn is { } expiry && expiry <= issuedOn) throw new InvalidOperationException("سررسید باید بعد از تاریخ صدور باشد.");
        DealerId = dealerId;
        CompanyId = companyId;
        BranchId = branchId;
        TerritoryId = territoryId;
        Type = type;
        Number = Text(number, 60) ?? throw new InvalidOperationException("شمارهٔ تضمین الزامی است.");
        Issuer = Text(issuer, 120);
        Amount = amount;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Notes = Text(notes, 500);
        RegisteredByUserId = registeredByUserId;
    }

    private DealerGuarantee() : base(Guid.Empty)
    {
        CompanyId = BranchId = Number = "EF";
    }

    public Guid DealerId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public DealerGuaranteeType Type { get; private set; }
    public string Number { get; private set; }
    public string? Issuer { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly IssuedOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public string? Notes { get; private set; }
    public DealerGuaranteeStatus Status { get; private set; } = DealerGuaranteeStatus.Active;
    public Guid RegisteredByUserId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    public bool IsEffective(DateOnly today) => Status == DealerGuaranteeStatus.Active && (ExpiresOn is null || ExpiresOn >= today);

    public bool ExpiresWithin(DateOnly today, int days) => IsEffective(today) && ExpiresOn is { } expiry && expiry <= today.AddDays(days);

    /// <summary>Returns the guarantee to the dealer (e.g. contract ended or replaced).</summary>
    public void Release(Guid userId, string reason, DateTimeOffset nowUtc) => Decide(DealerGuaranteeStatus.Released, userId, reason, nowUtc);

    /// <summary>Cashes/claims the guarantee against unpaid receivables.</summary>
    public void Forfeit(Guid userId, string reason, DateTimeOffset nowUtc) => Decide(DealerGuaranteeStatus.Forfeited, userId, reason, nowUtc);

    private void Decide(DealerGuaranteeStatus status, Guid userId, string reason, DateTimeOffset nowUtc)
    {
        if (Status != DealerGuaranteeStatus.Active) throw new InvalidOperationException("این تضمین قبلاً آزاد یا ضبط شده است.");
        DecisionReason = Text(reason, 500) ?? throw new InvalidOperationException("ثبت دلیل الزامی است.");
        Status = status;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
        Touch();
    }

    internal static string? Text(string? value, int max)
    {
        var text = PersianText.NormalizeLetters(value);
        return text is null ? null : text.Length <= max ? text : throw new InvalidOperationException($"حداکثر طول مجاز {max} نویسه است.");
    }
}

/// <summary>A training session attended by a dealer's staff.</summary>
public sealed class DealerTraining : Entity, IOrganizationScoped
{
    public DealerTraining(Guid id, Guid dealerId, string companyId, string branchId, string? territoryId, string title,
        DealerTrainingTopic topic, DateOnly heldOn, decimal hours, int participants, decimal? score, DateOnly? certificateValidTo,
        Guid recordedByUserId, DateOnly today) : base(id)
    {
        if (heldOn > today) throw new InvalidOperationException("تاریخ برگزاری نمی‌تواند در آینده باشد.");
        if (hours is <= 0 or > 200) throw new InvalidOperationException("مدت آموزش باید بین ۰ تا ۲۰۰ ساعت باشد.");
        if (participants is < 1 or > 500) throw new InvalidOperationException("تعداد شرکت‌کنندگان باید بین ۱ تا ۵۰۰ باشد.");
        if (score is < 0 or > 100) throw new InvalidOperationException("نمرهٔ آزمون باید بین ۰ تا ۱۰۰ باشد.");
        if (certificateValidTo is { } valid && valid <= heldOn) throw new InvalidOperationException("اعتبار گواهی باید بعد از تاریخ برگزاری باشد.");
        DealerId = dealerId;
        CompanyId = companyId;
        BranchId = branchId;
        TerritoryId = territoryId;
        Title = DealerGuarantee.Text(title, 160) ?? throw new InvalidOperationException("عنوان دوره الزامی است.");
        Topic = topic;
        HeldOn = heldOn;
        Hours = hours;
        Participants = participants;
        Score = score;
        CertificateValidTo = certificateValidTo;
        RecordedByUserId = recordedByUserId;
    }

    private DealerTraining() : base(Guid.Empty)
    {
        CompanyId = BranchId = Title = "EF";
    }

    public Guid DealerId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public string Title { get; private set; }
    public DealerTrainingTopic Topic { get; private set; }
    public DateOnly HeldOn { get; private set; }
    public decimal Hours { get; private set; }
    public int Participants { get; private set; }
    public decimal? Score { get; private set; }
    public DateOnly? CertificateValidTo { get; private set; }
    public Guid RecordedByUserId { get; private set; }
}

/// <summary>
/// Outbox message handing an approved commission to accounting. The idempotency key is derived from the statement, so a
/// retry after a lost response cannot create a second payable. Failed sends back off; after the last attempt the message
/// is dead-lettered and needs a manual retry.
/// </summary>
public sealed class CommissionPayoutMessage : Entity, IOrganizationScoped
{
    public const int MaxAttempts = 6;

    public CommissionPayoutMessage(Guid id, DealerCommissionStatement statement, string payload, DateTimeOffset nowUtc) : base(id)
    {
        if (statement.Status != CommissionStatementStatus.Approved) throw new InvalidOperationException("فقط کمیسیون تأییدشده به حسابداری ارسال می‌شود.");
        StatementId = statement.Id;
        CompanyId = statement.CompanyId;
        BranchId = statement.BranchId;
        TerritoryId = statement.TerritoryId;
        IdempotencyKey = KeyFor(statement.Id);
        Payload = payload.Length <= 4000 ? payload : throw new ArgumentException("Payload too large.", nameof(payload));
        NextAttemptAtUtc = nowUtc;
    }

    private CommissionPayoutMessage() : base(Guid.Empty)
    {
        CompanyId = BranchId = IdempotencyKey = Payload = "EF";
    }

    public static string KeyFor(Guid statementId) => $"commission:{statementId:N}";

    public Guid StatementId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Payload { get; private set; }
    public CommissionPayoutStatus Status { get; private set; } = CommissionPayoutStatus.Pending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? ExternalReference { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsDue(DateTimeOffset nowUtc) => Status == CommissionPayoutStatus.Pending && NextAttemptAtUtc <= nowUtc;

    public void MarkSent(string externalReference, DateTimeOffset nowUtc)
    {
        if (Status == CommissionPayoutStatus.Sent) return;
        AttemptCount++;
        Status = CommissionPayoutStatus.Sent;
        ExternalReference = DealerGuarantee.Text(externalReference, 80) ?? throw new ArgumentException("Reference required.", nameof(externalReference));
        CompletedAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        LastError = null;
        Touch();
    }

    /// <summary>Backoff 1, 5, 15, 60, 240 minutes; then dead-letter.</summary>
    public void MarkFailed(string error, DateTimeOffset nowUtc)
    {
        if (Status != CommissionPayoutStatus.Pending) return;
        AttemptCount++;
        LastError = error.Length <= 500 ? error : error[..500];
        if (AttemptCount >= MaxAttempts)
        {
            Status = CommissionPayoutStatus.DeadLetter;
            NextAttemptAtUtc = null;
        }
        else NextAttemptAtUtc = nowUtc.AddMinutes(AttemptCount switch { 1 => 1, 2 => 5, 3 => 15, 4 => 60, _ => 240 });
        Touch();
    }

    /// <summary>Manual retry of a dead-lettered (or waiting) message.</summary>
    public void Retry(DateTimeOffset nowUtc)
    {
        if (Status == CommissionPayoutStatus.Sent) throw new InvalidOperationException("این کمیسیون قبلاً به حسابداری ارسال شده است.");
        Status = CommissionPayoutStatus.Pending;
        AttemptCount = Math.Min(AttemptCount, MaxAttempts - 1);
        NextAttemptAtUtc = nowUtc;
        Touch();
    }
}
