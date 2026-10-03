using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Channel;

public enum DealerStatus { Draft, PendingApproval, Active, Suspended, Terminated }
public enum DealerContractStatus { Draft, PendingApproval, Active, Expired, Terminated }
public enum DealerTerritoryStatus { Proposed, Active, Ended }

public sealed class Dealer(
    Guid id,
    string dealerId,
    string code,
    string legalName,
    string tradeName,
    string companyId,
    string branchId,
    string? territoryId,
    string city,
    string? nationalId,
    string? phone,
    string? email,
    Guid channelManagerUserId) : Entity(id), IOrganizationScoped
{
    public string DealerId { get; } = Required(dealerId, nameof(dealerId));
    public string Code { get; private set; } = Required(code, nameof(code));
    public string LegalName { get; private set; } = Required(legalName, nameof(legalName));
    public string TradeName { get; private set; } = Required(tradeName, nameof(tradeName));
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; private set; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; private set; } = Optional(territoryId);
    public string City { get; private set; } = Required(city, nameof(city));
    public string? NationalId { get; private set; } = Optional(nationalId);
    public string? Phone { get; private set; } = Optional(phone);
    public string? Email { get; private set; } = NormalizeEmail(email);
    public Guid ChannelManagerUserId { get; private set; } = channelManagerUserId;
    public DealerStatus Status { get; private set; } = DealerStatus.Draft;
    public string? StatusReason { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public DateTimeOffset? TerminatedAtUtc { get; private set; }

    private Dealer() : this(Guid.Empty, "EF", "EF", "EF", "EF", "EF", "EF", null,
        "EF", null, null, null, Guid.Empty) { }

    public void Update(string code, string legalName, string tradeName, string branchId, string? territoryId,
        string city, string? nationalId, string? phone, string? email, Guid channelManagerUserId)
    {
        if (Status == DealerStatus.Terminated)
            throw new InvalidOperationException("نماینده خاتمه‌یافته قابل ویرایش نیست.");
        Code = Required(code, nameof(code));
        LegalName = Required(legalName, nameof(legalName));
        TradeName = Required(tradeName, nameof(tradeName));
        BranchId = Required(branchId, nameof(branchId));
        TerritoryId = Optional(territoryId);
        City = Required(city, nameof(city));
        NationalId = Optional(nationalId);
        Phone = Optional(phone);
        Email = NormalizeEmail(email);
        ChannelManagerUserId = channelManagerUserId;
        Touch();
    }

    public void SubmitForApproval(string reason)
    {
        if (Status is not (DealerStatus.Draft or DealerStatus.Suspended))
            throw new InvalidOperationException("فقط نماینده پیش‌نویس یا تعلیق‌شده قابل ارسال برای تأیید است.");
        Status = DealerStatus.PendingApproval;
        StatusReason = Required(reason, nameof(reason));
        Touch();
    }

    public void Activate(string reason, DateTimeOffset nowUtc)
    {
        if (Status != DealerStatus.PendingApproval)
            throw new InvalidOperationException("فعال‌سازی فقط پس از ارسال برای تأیید مجاز است.");
        Status = DealerStatus.Active;
        StatusReason = Required(reason, nameof(reason));
        ActivatedAtUtc ??= nowUtc;
        Touch();
    }

    public void Suspend(string reason)
    {
        if (Status != DealerStatus.Active)
            throw new InvalidOperationException("فقط نماینده فعال قابل تعلیق است.");
        Status = DealerStatus.Suspended;
        StatusReason = Required(reason, nameof(reason));
        Touch();
    }

    public void Terminate(string reason, DateTimeOffset nowUtc)
    {
        if (Status == DealerStatus.Terminated) throw new InvalidOperationException("نماینده قبلاً خاتمه یافته است.");
        Status = DealerStatus.Terminated;
        StatusReason = Required(reason, nameof(reason));
        TerminatedAtUtc = nowUtc;
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeEmail(string? value) => Optional(value)?.ToUpperInvariant();
}

public sealed class DealerContract(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    string contractNumber,
    DateTimeOffset validFromUtc,
    DateTimeOffset validToUtc,
    decimal annualTarget,
    string paymentTerms,
    Guid requestedByUserId) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public string ContractNumber { get; private set; } = Required(contractNumber, nameof(contractNumber));
    public DateTimeOffset ValidFromUtc { get; private set; } = Window(validFromUtc, validToUtc).From;
    public DateTimeOffset ValidToUtc { get; private set; } = Window(validFromUtc, validToUtc).To;
    public decimal AnnualTarget { get; private set; } = Positive(annualTarget, nameof(annualTarget));
    public string PaymentTerms { get; private set; } = Required(paymentTerms, nameof(paymentTerms));
    public Guid RequestedByUserId { get; } = requestedByUserId;
    public DealerContractStatus Status { get; private set; } = DealerContractStatus.Draft;
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }

    private DealerContract() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, "EF",
        DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddDays(1), 1, "EF", Guid.Empty) { }

    public bool IsEffective(DateTimeOffset nowUtc) => Status == DealerContractStatus.Active &&
        ValidFromUtc <= nowUtc && nowUtc < ValidToUtc;

    public void Update(string contractNumber, DateTimeOffset validFromUtc, DateTimeOffset validToUtc,
        decimal annualTarget, string paymentTerms)
    {
        if (Status != DealerContractStatus.Draft) throw new InvalidOperationException("فقط قرارداد پیش‌نویس قابل ویرایش است.");
        var window = Window(validFromUtc, validToUtc);
        ContractNumber = Required(contractNumber, nameof(contractNumber));
        ValidFromUtc = window.From;
        ValidToUtc = window.To;
        AnnualTarget = Positive(annualTarget, nameof(annualTarget));
        PaymentTerms = Required(paymentTerms, nameof(paymentTerms));
        Touch();
    }

    public void Submit(string reason)
    {
        if (Status != DealerContractStatus.Draft) throw new InvalidOperationException("فقط قرارداد پیش‌نویس قابل ارسال است.");
        Status = DealerContractStatus.PendingApproval;
        DecisionReason = Required(reason, nameof(reason));
        Touch();
    }

    public void Approve(Guid actorUserId, DateTimeOffset nowUtc, string reason)
    {
        if (Status != DealerContractStatus.PendingApproval) throw new InvalidOperationException("قرارداد در انتظار تأیید نیست.");
        if (actorUserId == RequestedByUserId) throw new InvalidOperationException("درخواست‌کننده نمی‌تواند قرارداد خود را تأیید کند.");
        Status = DealerContractStatus.Active;
        ApprovedByUserId = actorUserId;
        ApprovedAtUtc = nowUtc;
        DecisionReason = Required(reason, nameof(reason));
        Touch();
    }

    public void End(DealerContractStatus target, string reason)
    {
        if (target is not (DealerContractStatus.Expired or DealerContractStatus.Terminated))
            throw new InvalidOperationException("وضعیت پایان قرارداد معتبر نیست.");
        if (Status != DealerContractStatus.Active) throw new InvalidOperationException("فقط قرارداد فعال قابل پایان است.");
        Status = target;
        DecisionReason = Required(reason, nameof(reason));
        Touch();
    }

    private static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset from, DateTimeOffset to) =>
        to <= from ? throw new ArgumentException("Contract end must be later than start.", nameof(to)) : (from, to);
    private static decimal Positive(decimal value, string name) =>
        value <= 0 ? throw new ArgumentOutOfRangeException(name, "Value must be positive.") : value;
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DealerTerritoryAssignment(
    Guid id,
    string companyId,
    string branchId,
    string territoryId,
    Guid dealerId,
    bool isExclusive,
    DateTimeOffset validFromUtc,
    DateTimeOffset? validToUtc,
    Guid requestedByUserId) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Required(territoryId, nameof(territoryId));
    public Guid DealerId { get; } = dealerId;
    public bool IsExclusive { get; } = isExclusive;
    public DateTimeOffset ValidFromUtc { get; } = validFromUtc;
    public DateTimeOffset? ValidToUtc { get; private set; } = ValidateEnd(validFromUtc, validToUtc);
    public Guid RequestedByUserId { get; } = requestedByUserId;
    public DealerTerritoryStatus Status { get; private set; } = DealerTerritoryStatus.Proposed;
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public string? Reason { get; private set; }

    private DealerTerritoryAssignment() : this(Guid.Empty, "EF", "EF", "EF", Guid.Empty, false,
        DateTimeOffset.MinValue, null, Guid.Empty) { }

    public bool Overlaps(DateTimeOffset fromUtc, DateTimeOffset? toUtc) =>
        (ValidToUtc is null || fromUtc < ValidToUtc) && (toUtc is null || ValidFromUtc < toUtc);

    public bool IsEffective(DateTimeOffset nowUtc) => Status == DealerTerritoryStatus.Active &&
        ValidFromUtc <= nowUtc && (ValidToUtc is null || nowUtc < ValidToUtc);

    public void Approve(Guid actorUserId, DateTimeOffset nowUtc, string reason)
    {
        if (Status != DealerTerritoryStatus.Proposed) throw new InvalidOperationException("تخصیص Territory در انتظار تأیید نیست.");
        if (actorUserId == RequestedByUserId) throw new InvalidOperationException("درخواست‌کننده نمی‌تواند تخصیص خود را تأیید کند.");
        Status = DealerTerritoryStatus.Active;
        ApprovedByUserId = actorUserId;
        ApprovedAtUtc = nowUtc;
        Reason = Required(reason, nameof(reason));
        Touch();
    }

    public void End(DateTimeOffset nowUtc, string reason)
    {
        if (Status != DealerTerritoryStatus.Active) throw new InvalidOperationException("فقط تخصیص فعال قابل پایان است.");
        if (nowUtc <= ValidFromUtc) throw new InvalidOperationException("زمان پایان باید بعد از شروع تخصیص باشد.");
        Status = DealerTerritoryStatus.Ended;
        ValidToUtc = nowUtc;
        Reason = Required(reason, nameof(reason));
        Touch();
    }

    private static DateTimeOffset? ValidateEnd(DateTimeOffset from, DateTimeOffset? to) =>
        to is not null && to <= from ? throw new ArgumentException("Assignment end must be later than start.", nameof(to)) : to;
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class DealerCustomerAssignment(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    Guid customerId,
    DateTimeOffset validFromUtc,
    Guid assignedByUserId,
    string reason) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public Guid CustomerId { get; private set; } = customerId;
    public DateTimeOffset ValidFromUtc { get; } = validFromUtc;
    public DateTimeOffset? ValidToUtc { get; private set; }
    public Guid AssignedByUserId { get; } = assignedByUserId;
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid? EndedByUserId { get; private set; }
    public string? EndReason { get; private set; }
    public bool IsActive => ValidToUtc is null;

    private DealerCustomerAssignment() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, Guid.Empty,
        DateTimeOffset.MinValue, Guid.Empty, "EF") { }

    public void End(DateTimeOffset nowUtc, Guid actorUserId, string reason)
    {
        if (!IsActive) throw new InvalidOperationException("ارتباط مشتری قبلاً پایان یافته است.");
        if (nowUtc <= ValidFromUtc) throw new InvalidOperationException("زمان پایان باید بعد از شروع ارتباط باشد.");
        ValidToUtc = nowUtc;
        EndedByUserId = actorUserId;
        EndReason = Required(reason, nameof(reason));
        Touch();
    }

    public void ReassignCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DealerTarget(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    DateTimeOffset periodFromUtc,
    DateTimeOffset periodToUtc,
    decimal amount,
    string source,
    Guid setByUserId) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public DateTimeOffset PeriodFromUtc { get; private set; } = Window(periodFromUtc, periodToUtc).From;
    public DateTimeOffset PeriodToUtc { get; private set; } = Window(periodFromUtc, periodToUtc).To;
    public decimal Amount { get; private set; } = Positive(amount, nameof(amount));
    public string Source { get; private set; } = Required(source, nameof(source));
    public Guid SetByUserId { get; private set; } = setByUserId;

    private DealerTarget() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, DateTimeOffset.MinValue,
        DateTimeOffset.MinValue.AddDays(1), 1, "EF", Guid.Empty) { }

    public void Update(DateTimeOffset fromUtc, DateTimeOffset toUtc, decimal amount, string source, Guid actorUserId)
    {
        var window = Window(fromUtc, toUtc);
        PeriodFromUtc = window.From;
        PeriodToUtc = window.To;
        Amount = Positive(amount, nameof(amount));
        Source = Required(source, nameof(source));
        SetByUserId = actorUserId;
        Touch();
    }

    private static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset from, DateTimeOffset to) =>
        to <= from ? throw new ArgumentException("Target period end must be later than start.", nameof(to)) : (from, to);
    private static decimal Positive(decimal value, string name) =>
        value <= 0 ? throw new ArgumentOutOfRangeException(name, "Value must be positive.") : value;
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DealerFinancialSnapshot(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    decimal creditLimit,
    decimal creditUsed,
    decimal balance,
    decimal overdueAmount,
    string source,
    DateTimeOffset synchronizedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public decimal CreditLimit { get; } = NonNegative(creditLimit, nameof(creditLimit));
    public decimal CreditUsed { get; } = NonNegative(creditUsed, nameof(creditUsed));
    public decimal Balance { get; } = NonNegative(balance, nameof(balance));
    public decimal OverdueAmount { get; } = NonNegative(overdueAmount, nameof(overdueAmount));
    public decimal AvailableCredit => Math.Max(0, CreditLimit - CreditUsed);
    public string Source { get; } = Required(source, nameof(source));
    public DateTimeOffset SynchronizedAtUtc { get; } = synchronizedAtUtc;

    private DealerFinancialSnapshot() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, 0, 0, 0, 0,
        "EF", DateTimeOffset.MinValue) { }
    private static decimal NonNegative(decimal value, string name) => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DealerPerformanceSnapshot(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    DateTimeOffset periodFromUtc,
    DateTimeOffset periodToUtc,
    decimal netSales,
    int orderCount,
    string source,
    DateTimeOffset synchronizedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public DateTimeOffset PeriodFromUtc { get; } = periodFromUtc;
    public DateTimeOffset PeriodToUtc { get; } = periodToUtc > periodFromUtc ? periodToUtc : throw new ArgumentException("Invalid period.", nameof(periodToUtc));
    public decimal NetSales { get; } = netSales >= 0 ? netSales : throw new ArgumentOutOfRangeException(nameof(netSales));
    public int OrderCount { get; } = orderCount >= 0 ? orderCount : throw new ArgumentOutOfRangeException(nameof(orderCount));
    public string Source { get; } = Required(source, nameof(source));
    public DateTimeOffset SynchronizedAtUtc { get; } = synchronizedAtUtc;

    private DealerPerformanceSnapshot() : this(Guid.Empty, "EF", "EF", null, Guid.Empty,
        DateTimeOffset.MinValue, DateTimeOffset.MinValue.AddDays(1), 0, 0, "EF", DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DealerStatusHistory(
    Guid id,
    string companyId,
    string branchId,
    string? territoryId,
    Guid dealerId,
    DealerStatus? fromStatus,
    DealerStatus toStatus,
    string reason,
    Guid changedByUserId,
    DateTimeOffset changedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid DealerId { get; } = dealerId;
    public DealerStatus? FromStatus { get; } = fromStatus;
    public DealerStatus ToStatus { get; } = toStatus;
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid ChangedByUserId { get; } = changedByUserId;
    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;

    private DealerStatusHistory() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, null,
        DealerStatus.Draft, "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
