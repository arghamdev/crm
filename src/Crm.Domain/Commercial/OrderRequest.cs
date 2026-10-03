using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Commercial;

public enum OrderRequestStatus
{
    Draft,
    CreditApproved,
    CreditHold,
    SubmissionPending,
    IntegrationFailed,
    ErpRejected,
    ErpAccepted,
    Allocated,
    Delivered,
    Invoiced,
    Paid,
    Cancelled
}

public enum OrderCreditDecisionType { Approved, Held, Overridden }
public enum OrderIntegrationStatus { Pending, Processing, RetryScheduled, AwaitingExternal, Completed, DeadLetter }
public enum ErpSubmissionOutcome { Accepted, Pending, Rejected, TransientFailure }

public sealed class OrderRequest : Entity, IOrganizationScoped
{
    public string Code { get; private set; }
    public Guid QuoteId { get; private set; }
    public string QuoteCode { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Customer { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public string CurrencyCode { get; private set; }
    public decimal NetAmount { get; private set; }
    public OrderRequestStatus Status { get; private set; } = OrderRequestStatus.Draft;
    public string SubmissionIdempotencyKey { get; private set; }
    public string CorrelationId { get; private set; }
    public decimal CreditLimit { get; private set; }
    public decimal CreditUsed { get; private set; }
    public decimal OverdueAmount { get; private set; }
    public decimal AvailableCredit { get; private set; }
    public bool IsCreditHold { get; private set; }
    public string? CreditReason { get; private set; }
    public string? CreditSource { get; private set; }
    public DateTimeOffset? CreditSnapshotAtUtc { get; private set; }
    public DateTimeOffset? CreditOverrideExpiresAtUtc { get; private set; }
    public string? ErpOrderNumber { get; private set; }
    public string? DeliveryReference { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? LastIntegrationError { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? LastSynchronizedAtUtc { get; private set; }

    public OrderRequest(Guid id, string code, Guid quoteId, string quoteCode, Guid customerId, string customer,
        Guid? opportunityId, Guid? ownerUserId, string companyId, string branchId, string? territoryId,
        string currencyCode, decimal netAmount, string correlationId) : base(id)
    {
        Code = Required(code, nameof(code));
        QuoteId = Required(quoteId, nameof(quoteId));
        QuoteCode = Required(quoteCode, nameof(quoteCode));
        CustomerId = Required(customerId, nameof(customerId));
        Customer = Required(customer, nameof(customer));
        OpportunityId = opportunityId;
        OwnerUserId = ownerUserId;
        CompanyId = Required(companyId, nameof(companyId));
        BranchId = Required(branchId, nameof(branchId));
        TerritoryId = Optional(territoryId);
        CurrencyCode = Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        NetAmount = netAmount > 0 ? netAmount : throw new ArgumentOutOfRangeException(nameof(netAmount));
        CorrelationId = Required(correlationId, nameof(correlationId));
        SubmissionIdempotencyKey = $"crm-order-{id:N}";
    }

    private OrderRequest() : this(Guid.NewGuid(), "EF", Guid.NewGuid(), "EF", Guid.NewGuid(), "EF", null, null,
        "EF", "EF", null, "IRR", 1, "EF") { }

    public void ApplyCreditSnapshot(decimal creditLimit, decimal creditUsed, decimal overdueAmount, bool externalHold,
        string reason, string source, DateTimeOffset snapshotAtUtc, DateTimeOffset nowUtc)
    {
        if (Status is not (OrderRequestStatus.Draft or OrderRequestStatus.CreditHold))
            throw new InvalidOperationException("بررسی اعتبار فقط برای درخواست جدید یا متوقف‌شده مجاز است.");
        if (creditLimit < 0 || creditUsed < 0 || overdueAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(creditLimit), "مقادیر Snapshot اعتبار نمی‌توانند منفی باشند.");
        CreditLimit = creditLimit;
        CreditUsed = creditUsed;
        OverdueAmount = overdueAmount;
        AvailableCredit = Math.Max(0, creditLimit - creditUsed);
        CreditSource = Required(source, nameof(source));
        CreditSnapshotAtUtc = snapshotAtUtc;
        CreditReason = Required(reason, nameof(reason));
        IsCreditHold = externalHold || overdueAmount > 0 || AvailableCredit < NetAmount;
        Status = IsCreditHold ? OrderRequestStatus.CreditHold : OrderRequestStatus.CreditApproved;
        LastSynchronizedAtUtc = nowUtc;
        Touch();
    }

    public void OverrideCreditHold(string reason, DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc)
    {
        if (Status != OrderRequestStatus.CreditHold)
            throw new InvalidOperationException("فقط توقف اعتباری قابل Override است.");
        if (expiresAtUtc <= nowUtc) throw new InvalidOperationException("اعتبار موقت باید تاریخ انقضای آینده داشته باشد.");
        CreditReason = Required(reason, nameof(reason));
        CreditOverrideExpiresAtUtc = expiresAtUtc;
        IsCreditHold = false;
        Status = OrderRequestStatus.CreditApproved;
        Touch();
    }

    public void QueueSubmission(DateTimeOffset nowUtc)
    {
        if (Status is not (OrderRequestStatus.CreditApproved or OrderRequestStatus.IntegrationFailed))
            throw new InvalidOperationException("درخواست باید اعتبار تأییدشده داشته باشد.");
        if (CreditOverrideExpiresAtUtc.HasValue && CreditOverrideExpiresAtUtc <= nowUtc)
            throw new InvalidOperationException("اعتبار موقت منقضی شده است؛ بررسی اعتبار را تکرار کنید.");
        Status = OrderRequestStatus.SubmissionPending;
        SubmittedAtUtc ??= nowUtc;
        LastIntegrationError = null;
        Touch();
    }

    public void ApplySubmissionResult(ErpSubmissionOutcome outcome, string? externalReference, string message,
        DateTimeOffset nowUtc)
    {
        if (Status != OrderRequestStatus.SubmissionPending)
            throw new InvalidOperationException("درخواست در صف ارسال ERP نیست.");
        switch (outcome)
        {
            case ErpSubmissionOutcome.Accepted:
                ErpOrderNumber = Required(externalReference, nameof(externalReference));
                Status = OrderRequestStatus.ErpAccepted;
                LastIntegrationError = null;
                break;
            case ErpSubmissionOutcome.Pending:
                LastIntegrationError = null;
                break;
            case ErpSubmissionOutcome.Rejected:
                Status = OrderRequestStatus.ErpRejected;
                LastIntegrationError = Required(message, nameof(message));
                break;
            case ErpSubmissionOutcome.TransientFailure:
                Status = OrderRequestStatus.IntegrationFailed;
                LastIntegrationError = Required(message, nameof(message));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        LastSynchronizedAtUtc = nowUtc;
        Touch();
    }

    public void AdvanceProjection(OrderRequestStatus target, string externalReference, DateTimeOffset synchronizedAtUtc)
    {
        var valid = (Status, target) switch
        {
            (OrderRequestStatus.ErpAccepted, OrderRequestStatus.Allocated) => true,
            (OrderRequestStatus.Allocated, OrderRequestStatus.Delivered) => true,
            (OrderRequestStatus.Delivered, OrderRequestStatus.Invoiced) => true,
            (OrderRequestStatus.Invoiced, OrderRequestStatus.Paid) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("وضعیت ERP باید به‌ترتیب تخصیص، تحویل، فاکتور و پرداخت پیش برود.");
        var reference = Required(externalReference, nameof(externalReference));
        if (target == OrderRequestStatus.Delivered) DeliveryReference = reference;
        if (target == OrderRequestStatus.Invoiced) InvoiceNumber = reference;
        if (target == OrderRequestStatus.Paid) PaymentReference = reference;
        Status = target;
        LastSynchronizedAtUtc = synchronizedAtUtc;
        Touch();
    }

    public void ReassignCustomer(Guid customerId, string customerName)
    {
        CustomerId = Required(customerId, nameof(customerId));
        Customer = Required(customerName, nameof(customerName));
        Touch();
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException("Identifier is required.", name) : value;
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OrderCreditDecision(
    Guid id, string companyId, string branchId, string? territoryId, Guid orderRequestId,
    OrderCreditDecisionType decision, decimal creditLimit, decimal creditUsed, decimal overdueAmount,
    decimal availableCredit, string reason, string source, Guid decidedByUserId, DateTimeOffset decidedAtUtc,
    DateTimeOffset? expiresAtUtc = null) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid OrderRequestId { get; } = orderRequestId;
    public OrderCreditDecisionType Decision { get; } = decision;
    public decimal CreditLimit { get; } = creditLimit;
    public decimal CreditUsed { get; } = creditUsed;
    public decimal OverdueAmount { get; } = overdueAmount;
    public decimal AvailableCredit { get; } = availableCredit;
    public string Reason { get; } = Required(reason, nameof(reason));
    public string Source { get; } = Required(source, nameof(source));
    public Guid DecidedByUserId { get; } = decidedByUserId;
    public DateTimeOffset DecidedAtUtc { get; } = decidedAtUtc;
    public DateTimeOffset? ExpiresAtUtc { get; } = expiresAtUtc;
    private OrderCreditDecision() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, OrderCreditDecisionType.Held,
        0, 0, 0, 0, "EF", "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OrderStatusHistory(
    Guid id, string companyId, string branchId, string? territoryId, Guid orderRequestId,
    OrderRequestStatus? fromStatus, OrderRequestStatus toStatus, string reason, string source,
    Guid changedByUserId, DateTimeOffset changedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid OrderRequestId { get; } = orderRequestId;
    public OrderRequestStatus? FromStatus { get; } = fromStatus;
    public OrderRequestStatus ToStatus { get; } = toStatus;
    public string Reason { get; } = Required(reason, nameof(reason));
    public string Source { get; } = Required(source, nameof(source));
    public Guid ChangedByUserId { get; } = changedByUserId;
    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;
    private OrderStatusHistory() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, null, OrderRequestStatus.Draft,
        "EF", "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OrderIntegrationMessage : Entity, IOrganizationScoped
{
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public Guid OrderRequestId { get; private set; }
    public string MessageType { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string CorrelationId { get; private set; }
    public string PayloadFingerprint { get; private set; }
    public OrderIntegrationStatus Status { get; private set; } = OrderIntegrationStatus.Pending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? ExternalReference { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public OrderIntegrationMessage(Guid id, string companyId, string branchId, string? territoryId,
        Guid orderRequestId, string idempotencyKey, string correlationId, string payloadFingerprint) : base(id)
    {
        CompanyId = Required(companyId, nameof(companyId));
        BranchId = Required(branchId, nameof(branchId));
        TerritoryId = Optional(territoryId);
        OrderRequestId = orderRequestId;
        MessageType = "SubmitOrderRequest.v1";
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));
        CorrelationId = Required(correlationId, nameof(correlationId));
        PayloadFingerprint = Required(payloadFingerprint, nameof(payloadFingerprint));
    }

    private OrderIntegrationMessage() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, "EF", "EF", "EF") { }

    public int BeginAttempt(DateTimeOffset nowUtc)
    {
        if (Status is not (OrderIntegrationStatus.Pending or OrderIntegrationStatus.RetryScheduled or OrderIntegrationStatus.AwaitingExternal))
            throw new InvalidOperationException("این پیام در وضعیت قابل پردازش نیست.");
        if (NextAttemptAtUtc.HasValue && NextAttemptAtUtc > nowUtc)
            throw new InvalidOperationException("زمان تلاش بعدی هنوز فرا نرسیده است.");
        Status = OrderIntegrationStatus.Processing;
        AttemptCount++;
        NextAttemptAtUtc = null;
        Touch();
        return AttemptCount;
    }

    public void Complete(string? externalReference, DateTimeOffset nowUtc)
    {
        EnsureProcessing();
        Status = OrderIntegrationStatus.Completed;
        ExternalReference = Optional(externalReference);
        LastError = null;
        CompletedAtUtc = nowUtc;
        Touch();
    }

    public void AwaitExternal(DateTimeOffset nowUtc)
    {
        EnsureProcessing();
        Status = OrderIntegrationStatus.AwaitingExternal;
        NextAttemptAtUtc = nowUtc.AddSeconds(30);
        LastError = null;
        Touch();
    }

    public void Fail(string error, DateTimeOffset nowUtc, int maximumAttempts = 3)
    {
        EnsureProcessing();
        LastError = Required(error, nameof(error));
        if (AttemptCount >= maximumAttempts)
        {
            Status = OrderIntegrationStatus.DeadLetter;
            NextAttemptAtUtc = null;
        }
        else
        {
            Status = OrderIntegrationStatus.RetryScheduled;
            NextAttemptAtUtc = nowUtc.AddSeconds(AttemptCount * 15);
        }
        Touch();
    }

    private void EnsureProcessing()
    {
        if (Status != OrderIntegrationStatus.Processing)
            throw new InvalidOperationException("پیام در حال پردازش نیست.");
    }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OrderIntegrationAttempt(
    Guid id, string companyId, string branchId, string? territoryId, Guid messageId, Guid orderRequestId,
    int attemptNumber, ErpSubmissionOutcome outcome, string detail, string? externalReference,
    DateTimeOffset attemptedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid MessageId { get; } = messageId;
    public Guid OrderRequestId { get; } = orderRequestId;
    public int AttemptNumber { get; } = attemptNumber;
    public ErpSubmissionOutcome Outcome { get; } = outcome;
    public string Detail { get; } = Required(detail, nameof(detail));
    public string? ExternalReference { get; } = Optional(externalReference);
    public DateTimeOffset AttemptedAtUtc { get; } = attemptedAtUtc;
    private OrderIntegrationAttempt() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, Guid.Empty, 1,
        ErpSubmissionOutcome.Pending, "EF", null, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
