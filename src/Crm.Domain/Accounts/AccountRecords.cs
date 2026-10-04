using Crm.Domain.Common;

namespace Crm.Domain.Accounts;

public enum AccountRelationship { Customer, Prospect, Supplier, Partner }
public enum PaymentDirection { Receipt, Payment }
public enum PaymentMethod { Cash, BankTransfer, Cheque, Card, Pos, Other }
public enum PaymentStatus { Registered, Approved, Returned, Cancelled }
public enum ContractKind { Sales, Service }
public enum ContractStatus { Draft, Active, Terminated }
public enum ProjectStatus { Planned, Active, OnHold, Completed, Cancelled }
public enum CampaignType { Email, Sms, Event, Webinar, Advertising, Other }
public enum CampaignStatus { Planned, Active, Completed, Cancelled }
public enum CampaignMemberStatus { Targeted, Responded, Converted, OptedOut }
public enum SurveyKind { Nps, Csat }
public enum ParticipationKind { Event, Program, Collaboration }
public enum ParticipationStatus { Planned, Confirmed, Attended, Cancelled }
public enum AllocationKind { Reservation, Gift, Sample }
public enum AllocationStatus { Requested, Delivered, Returned, Cancelled }

/// <summary>Shared rules of records that belong to one account (company copied from the account; access is checked via the account).</summary>
public abstract class AccountRecord(Guid id, string companyId, Guid customerId) : Entity(id)
{
    public string CompanyId { get; private set; } = companyId;
    public Guid CustomerId { get; private set; } = customerId;

    public void ReassignCustomer(Guid customerId)
    {
        CustomerId = customerId;
        Touch();
    }

    protected static string Required(string? value, int max, string message) => CrmActivity.Text(value, max) ?? throw new InvalidOperationException(message);
    protected static string? Optional(string? value, int max) => CrmActivity.Text(value, max);

    protected static string Currency(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        return value is { Length: 3 } && value.All(char.IsAsciiLetterUpper) ? value : throw new InvalidOperationException("واحد پول باید کد سه‌حرفی (مثل IRR) باشد.");
    }

    protected void Changed() => Touch();
}

/// <summary>
/// Receipt from or payment to the account, recorded in CRM. Only approved items count in totals; a bounced cheque or
/// reversed transfer is "returned", a mistaken entry "cancelled". The recorder cannot approve their own entry.
/// </summary>
public sealed class AccountPayment : AccountRecord
{
    public AccountPayment(Guid id, string companyId, Guid customerId, PaymentDirection direction, decimal amount, string currencyCode,
        DateOnly paidOn, PaymentMethod method, string? reference, string? invoiceReference, string? description, Guid createdByUserId, DateOnly today)
        : base(id, companyId, customerId)
    {
        if (amount <= 0) throw new InvalidOperationException("مبلغ باید مثبت باشد.");
        if (paidOn > today) throw new InvalidOperationException("تاریخ پرداخت نمی‌تواند در آینده باشد.");
        if (method is PaymentMethod.Cheque or PaymentMethod.BankTransfer && string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("برای چک و حواله، شماره یا کد پیگیری الزامی است.");
        Direction = direction;
        Amount = amount;
        CurrencyCode = Currency(currencyCode);
        PaidOn = paidOn;
        Method = method;
        Reference = Optional(reference, 80);
        InvoiceReference = Optional(invoiceReference, 40);
        Description = Optional(description, 500);
        CreatedByUserId = createdByUserId;
    }

    private AccountPayment() : base(Guid.Empty, "EF", Guid.Empty) => CurrencyCode = "IRR";

    public PaymentDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public string? InvoiceReference { get; private set; }
    public string? Description { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Registered;
    public Guid CreatedByUserId { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }

    public bool CountsInTotals => Status == PaymentStatus.Approved;

    public void Approve(Guid userId, DateTimeOffset nowUtc)
    {
        if (Status != PaymentStatus.Registered) throw new InvalidOperationException("فقط پرداخت ثبت‌شده قابل تأیید است.");
        if (userId == CreatedByUserId) throw new InvalidOperationException("ثبت‌کننده نمی‌تواند پرداخت خود را تأیید کند (تفکیک وظایف).");
        Decide(PaymentStatus.Approved, userId, null, nowUtc);
    }

    /// <summary>Bounced cheque / reversed transfer of an approved item.</summary>
    public void MarkReturned(Guid userId, string note, DateTimeOffset nowUtc)
    {
        if (Status != PaymentStatus.Approved) throw new InvalidOperationException("فقط پرداخت تأییدشده برگشت می‌خورد.");
        Decide(PaymentStatus.Returned, userId, Required(note, 500, "دلیل برگشت را ثبت کنید."), nowUtc);
    }

    public void Cancel(Guid userId, string note, DateTimeOffset nowUtc)
    {
        if (Status != PaymentStatus.Registered) throw new InvalidOperationException("فقط پرداخت تأییدنشده قابل لغو است؛ پرداخت تأییدشده را «برگشتی» ثبت کنید.");
        Decide(PaymentStatus.Cancelled, userId, Required(note, 500, "دلیل لغو را ثبت کنید."), nowUtc);
    }

    private void Decide(PaymentStatus status, Guid userId, string? note, DateTimeOffset nowUtc)
    {
        Status = status;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
        DecisionNote = note;
        Changed();
    }
}

/// <summary>An account's bank account (IBAN/Sheba), used for refunds and supplier payments.</summary>
public sealed class AccountBankAccount : AccountRecord
{
    public AccountBankAccount(Guid id, string companyId, Guid customerId, string bankName, string iban, string? accountNumber,
        string holderName, bool isPrimary) : base(id, companyId, customerId)
    {
        BankName = Required(bankName, 80, "نام بانک الزامی است.");
        var normalized = IranianIdentifiers.NormalizeIban(iban);
        Iban = IranianIdentifiers.IsValidIban(normalized) ? normalized! : throw new InvalidOperationException("شماره شبا معتبر نیست (IR و ۲۴ رقم با رقم کنترل).");
        AccountNumber = PersianText.Normalize(accountNumber) is { Length: > 0 and <= 30 } number ? number : null;
        HolderName = Required(holderName, 150, "نام صاحب حساب الزامی است.");
        IsPrimary = isPrimary;
    }

    private AccountBankAccount() : base(Guid.Empty, "EF", Guid.Empty) => BankName = Iban = HolderName = "EF";

    public string BankName { get; private set; }
    public string Iban { get; private set; }
    public string? AccountNumber { get; private set; }
    public string HolderName { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void SetPrimary(bool value) { IsPrimary = value; Changed(); }

    public void Deactivate()
    {
        IsActive = false;
        IsPrimary = false;
        Changed();
    }
}

/// <summary>Sales or service contract with an account. A sales contract comes from a won opportunity of the same account.</summary>
public sealed class AccountContract : AccountRecord
{
    public AccountContract(Guid id, string companyId, Guid customerId, ContractKind kind, string number, string title, DateOnly startOn,
        DateOnly endOn, decimal? amount, string currencyCode, Guid? opportunityId, string? serviceLevel, Guid createdByUserId) : base(id, companyId, customerId)
    {
        if (endOn <= startOn) throw new InvalidOperationException("پایان قرارداد باید بعد از شروع آن باشد.");
        if (amount is <= 0) throw new InvalidOperationException("مبلغ قرارداد باید مثبت باشد.");
        if (kind == ContractKind.Sales && opportunityId is null) throw new InvalidOperationException("قرارداد فروش از فرصت برنده‌شده ایجاد می‌شود.");
        Kind = kind;
        Number = number;
        Title = Required(title, 200, "عنوان قرارداد الزامی است.");
        StartOn = startOn;
        EndOn = endOn;
        Amount = amount;
        CurrencyCode = Currency(currencyCode);
        OpportunityId = opportunityId;
        ServiceLevel = kind == ContractKind.Service ? Optional(serviceLevel, 120) : null;
        CreatedByUserId = createdByUserId;
    }

    private AccountContract() : base(Guid.Empty, "EF", Guid.Empty) => Number = Title = CurrencyCode = "EF";

    public ContractKind Kind { get; private set; }
    public string Number { get; private set; }
    public string Title { get; private set; }
    public DateOnly StartOn { get; private set; }
    public DateOnly EndOn { get; private set; }
    public decimal? Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public string? ServiceLevel { get; private set; }
    public ContractStatus Status { get; private set; } = ContractStatus.Draft;
    public Guid CreatedByUserId { get; private set; }
    public string? TerminationReason { get; private set; }

    public bool IsExpired(DateOnly today) => Status == ContractStatus.Active && EndOn < today;

    public void Activate()
    {
        if (Status != ContractStatus.Draft) throw new InvalidOperationException("فقط قرارداد پیش‌نویس فعال می‌شود.");
        Status = ContractStatus.Active;
        Changed();
    }

    public void Terminate(string reason)
    {
        if (Status == ContractStatus.Terminated) throw new InvalidOperationException("قرارداد قبلاً خاتمه یافته است.");
        TerminationReason = Required(reason, 500, "دلیل خاتمه را ثبت کنید.");
        Status = ContractStatus.Terminated;
        Changed();
    }
}

public sealed class AccountProject : AccountRecord
{
    public AccountProject(Guid id, string companyId, Guid customerId, string code, string name, DateOnly startOn, DateOnly? endOn,
        Guid? managerUserId, Guid? opportunityId, Guid? contractId, decimal? budget, string currencyCode) : base(id, companyId, customerId)
    {
        if (endOn is { } end && end < startOn) throw new InvalidOperationException("پایان پروژه نمی‌تواند قبل از شروع باشد.");
        if (budget is <= 0) throw new InvalidOperationException("بودجه باید مثبت باشد.");
        Code = code;
        Name = Required(name, 200, "نام پروژه الزامی است.");
        StartOn = startOn;
        EndOn = endOn;
        ManagerUserId = managerUserId;
        OpportunityId = opportunityId;
        ContractId = contractId;
        Budget = budget;
        CurrencyCode = Currency(currencyCode);
    }

    private AccountProject() : base(Guid.Empty, "EF", Guid.Empty) => Code = Name = CurrencyCode = "EF";

    public string Code { get; private set; }
    public string Name { get; private set; }
    public DateOnly StartOn { get; private set; }
    public DateOnly? EndOn { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public Guid? ContractId { get; private set; }
    public decimal? Budget { get; private set; }
    public string CurrencyCode { get; private set; }
    public ProjectStatus Status { get; private set; } = ProjectStatus.Planned;

    public void ChangeStatus(ProjectStatus status)
    {
        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled) throw new InvalidOperationException("پروژهٔ بسته‌شده قابل تغییر نیست.");
        if (status == Status) return;
        Status = status;
        Changed();
    }
}

public sealed class AccountParticipation : AccountRecord
{
    public AccountParticipation(Guid id, string companyId, Guid customerId, ParticipationKind kind, string title, string? role,
        DateOnly startOn, DateOnly? endOn, string? notes) : base(id, companyId, customerId)
    {
        if (endOn is { } end && end < startOn) throw new InvalidOperationException("پایان نمی‌تواند قبل از شروع باشد.");
        Kind = kind;
        Title = Required(title, 200, "عنوان رویداد/برنامه الزامی است.");
        Role = Optional(role, 80);
        StartOn = startOn;
        EndOn = endOn;
        Notes = Optional(notes, 500);
    }

    private AccountParticipation() : base(Guid.Empty, "EF", Guid.Empty) => Title = "EF";

    public ParticipationKind Kind { get; private set; }
    public string Title { get; private set; }
    public string? Role { get; private set; }
    public DateOnly StartOn { get; private set; }
    public DateOnly? EndOn { get; private set; }
    public ParticipationStatus Status { get; private set; } = ParticipationStatus.Planned;
    public string? Notes { get; private set; }

    public void ChangeStatus(ParticipationStatus status)
    {
        if (Status is ParticipationStatus.Attended or ParticipationStatus.Cancelled) throw new InvalidOperationException("مشارکت بسته‌شده قابل تغییر نیست.");
        Status = status;
        Changed();
    }
}

/// <summary>Reservation, gift or product sample given to an account.</summary>
public sealed class AccountAllocation : AccountRecord
{
    public AccountAllocation(Guid id, string companyId, Guid customerId, AllocationKind kind, string? itemCode, string itemName,
        decimal quantity, DateOnly date, string? notes) : base(id, companyId, customerId)
    {
        if (quantity is <= 0 or > 1_000_000) throw new InvalidOperationException("مقدار باید مثبت باشد.");
        Kind = kind;
        ItemCode = Optional(itemCode, 40);
        ItemName = Required(itemName, 200, "نام کالا یا هدیه الزامی است.");
        Quantity = quantity;
        Date = date;
        Notes = Optional(notes, 500);
    }

    private AccountAllocation() : base(Guid.Empty, "EF", Guid.Empty) => ItemName = "EF";

    public AllocationKind Kind { get; private set; }
    public string? ItemCode { get; private set; }
    public string ItemName { get; private set; }
    public decimal Quantity { get; private set; }
    public DateOnly Date { get; private set; }
    public AllocationStatus Status { get; private set; } = AllocationStatus.Requested;
    public string? Notes { get; private set; }

    public void ChangeStatus(AllocationStatus status)
    {
        if (Status is AllocationStatus.Returned or AllocationStatus.Cancelled) throw new InvalidOperationException("این مورد بسته شده است.");
        if (status == AllocationStatus.Returned && Status != AllocationStatus.Delivered) throw new InvalidOperationException("فقط مورد تحویل‌شده برگشت داده می‌شود.");
        Status = status;
        Changed();
    }
}

/// <summary>Marketing campaign (company-wide); accounts join through <see cref="CampaignMember"/>.</summary>
public sealed class Campaign : Entity
{
    public Campaign(Guid id, string companyId, string name, CampaignType type, DateOnly startOn, DateOnly? endOn, Guid createdByUserId) : base(id)
    {
        if (endOn is { } end && end < startOn) throw new InvalidOperationException("پایان کمپین نمی‌تواند قبل از شروع باشد.");
        CompanyId = companyId;
        Name = CrmActivity.Text(name, 160) ?? throw new InvalidOperationException("نام کمپین الزامی است.");
        Type = type;
        StartOn = startOn;
        EndOn = endOn;
        CreatedByUserId = createdByUserId;
    }

    private Campaign() : base(Guid.Empty) => CompanyId = Name = "EF";

    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public CampaignType Type { get; private set; }
    public CampaignStatus Status { get; private set; } = CampaignStatus.Planned;
    public DateOnly StartOn { get; private set; }
    public DateOnly? EndOn { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public bool AcceptsMembers => Status is CampaignStatus.Planned or CampaignStatus.Active;

    public void ChangeStatus(CampaignStatus status)
    {
        if (Status is CampaignStatus.Completed or CampaignStatus.Cancelled) throw new InvalidOperationException("کمپین بسته شده است.");
        Status = status;
        Touch();
    }
}

public sealed class CampaignMember : Entity
{
    public CampaignMember(Guid id, Guid campaignId, string companyId, Guid customerId, Guid? contactId, Guid addedByUserId) : base(id)
    {
        CampaignId = campaignId;
        CompanyId = companyId;
        CustomerId = customerId;
        ContactId = contactId;
        AddedByUserId = addedByUserId;
    }

    private CampaignMember() : base(Guid.Empty) => CompanyId = "EF";

    public Guid CampaignId { get; private set; }
    public string CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? ContactId { get; private set; }
    public CampaignMemberStatus Status { get; private set; } = CampaignMemberStatus.Targeted;
    public Guid AddedByUserId { get; private set; }

    public void ChangeStatus(CampaignMemberStatus status) { Status = status; Touch(); }
    public void ReassignCustomer(Guid customerId) { CustomerId = customerId; Touch(); }
}

public sealed class TargetList : Entity
{
    public TargetList(Guid id, string companyId, string name, string? description, Guid createdByUserId) : base(id)
    {
        CompanyId = companyId;
        Name = CrmActivity.Text(name, 160) ?? throw new InvalidOperationException("نام لیست الزامی است.");
        Description = CrmActivity.Text(description, 500);
        CreatedByUserId = createdByUserId;
    }

    private TargetList() : base(Guid.Empty) => CompanyId = Name = "EF";

    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public Guid CreatedByUserId { get; private set; }
}

public sealed class TargetListMember : Entity
{
    public TargetListMember(Guid id, Guid targetListId, string companyId, Guid customerId, Guid addedByUserId) : base(id)
    {
        TargetListId = targetListId;
        CompanyId = companyId;
        CustomerId = customerId;
        AddedByUserId = addedByUserId;
    }

    private TargetListMember() : base(Guid.Empty) => CompanyId = "EF";

    public Guid TargetListId { get; private set; }
    public string CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid AddedByUserId { get; private set; }
    public void ReassignCustomer(Guid customerId) { CustomerId = customerId; Touch(); }
}

public sealed class Survey : Entity
{
    public Survey(Guid id, string companyId, string title, SurveyKind kind, Guid createdByUserId) : base(id)
    {
        CompanyId = companyId;
        Title = CrmActivity.Text(title, 160) ?? throw new InvalidOperationException("عنوان نظرسنجی الزامی است.");
        Kind = kind;
        CreatedByUserId = createdByUserId;
    }

    private Survey() : base(Guid.Empty) => CompanyId = Title = "EF";

    public string CompanyId { get; private set; }
    public string Title { get; private set; }
    public SurveyKind Kind { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid CreatedByUserId { get; private set; }

    public (int Min, int Max) ScoreRange => Kind == SurveyKind.Nps ? (0, 10) : (1, 5);

    public void Close() { IsActive = false; Touch(); }
}

public sealed class SurveyResponse : AccountRecord
{
    public SurveyResponse(Guid id, Survey survey, Guid customerId, Guid? contactId, int score, string? comment, DateOnly respondedOn,
        Guid recordedByUserId, DateOnly today) : base(id, survey.CompanyId, customerId)
    {
        if (!survey.IsActive) throw new InvalidOperationException("نظرسنجی بسته شده است.");
        var (min, max) = survey.ScoreRange;
        if (score < min || score > max) throw new InvalidOperationException($"امتیاز باید بین {min} تا {max} باشد.");
        if (respondedOn > today) throw new InvalidOperationException("تاریخ پاسخ نمی‌تواند در آینده باشد.");
        SurveyId = survey.Id;
        ContactId = contactId;
        Score = score;
        Comment = Optional(comment, 1000);
        RespondedOn = respondedOn;
        RecordedByUserId = recordedByUserId;
    }

    private SurveyResponse() : base(Guid.Empty, "EF", Guid.Empty) { }

    public Guid SurveyId { get; private set; }
    public Guid? ContactId { get; private set; }
    public int Score { get; private set; }
    public string? Comment { get; private set; }
    public DateOnly RespondedOn { get; private set; }
    public Guid RecordedByUserId { get; private set; }
}
