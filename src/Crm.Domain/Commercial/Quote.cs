using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Commercial;

public enum QuoteStatus { Draft, Submitted, PendingApproval, Approved, Rejected, Sent, Accepted, Expired }
public enum QuoteApprovalLevel { None, SalesSupervisor, CommercialManager, JointSalesAndFinance }
public enum QuoteApprovalRole { SalesSupervisor, CommercialManager, SalesManager, FinanceManager }
public enum QuoteDecision { Approved, Rejected }

public sealed class Quote : Entity, IOrganizationScoped
{
    public string Code { get; private set; }
    public string Customer { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Opportunity { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public string CurrencyCode { get; private set; }
    public DateTimeOffset ValidUntilUtc { get; private set; }
    public string PaymentTerms { get; private set; }
    public int Revision { get; private set; }
    public Guid? ParentQuoteId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal CostAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal MarginPercent { get; private set; }
    public QuoteApprovalLevel ApprovalLevel { get; private set; }
    public QuoteStatus Status { get; private set; } = QuoteStatus.Draft;
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public Quote(Guid id, string code, string customer, Guid customerId, string opportunity, Guid? opportunityId,
        Guid? ownerUserId, string companyId, string branchId, string? territoryId, string currencyCode,
        DateTimeOffset validUntilUtc, string paymentTerms, int revision = 1, Guid? parentQuoteId = null) : base(id)
    {
        Code = Required(code, nameof(code));
        Customer = Required(customer, nameof(customer));
        CustomerId = customerId != Guid.Empty ? customerId : throw new ArgumentException("CustomerId is required.", nameof(customerId));
        Opportunity = Required(opportunity, nameof(opportunity));
        OpportunityId = opportunityId;
        OwnerUserId = ownerUserId;
        CompanyId = Required(companyId, nameof(companyId));
        BranchId = Required(branchId, nameof(branchId));
        TerritoryId = Optional(territoryId);
        CurrencyCode = Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ValidUntilUtc = validUntilUtc;
        PaymentTerms = Required(paymentTerms, nameof(paymentTerms));
        Revision = revision > 0 ? revision : throw new ArgumentOutOfRangeException(nameof(revision));
        ParentQuoteId = parentQuoteId;
    }

    public Quote(Guid id, string code, string customer, Guid customerId, string opportunity, Guid? opportunityId,
        decimal amount, decimal discount, decimal margin, string companyId, string branchId, string? territoryId)
        : this(id, code, customer, customerId, opportunity, opportunityId, null, companyId, branchId, territoryId,
            "IRR", DateTimeOffset.UtcNow.AddDays(30), "نقدی")
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (discount is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(discount));
        if (margin is < 15 or > 100) throw new ArgumentOutOfRangeException(nameof(margin));
        GrossAmount = Amount = amount;
        DiscountPercent = discount;
        DiscountAmount = amount * discount / 100m;
        NetAmount = amount - DiscountAmount;
        MarginPercent = margin;
        CostAmount = NetAmount * (1 - margin / 100m);
        ApprovalLevel = QuoteApprovalPolicy.Resolve(discount, margin);
        Status = ApprovalLevel == QuoteApprovalLevel.None ? QuoteStatus.Draft : QuoteStatus.PendingApproval;
    }

    private Quote() : this(Guid.Empty, "EF", "EF", Guid.NewGuid(), "EF", null, null, "EF", "EF", null,
        "IRR", DateTimeOffset.MinValue, "EF") { }

    public bool IsEditable => Status == QuoteStatus.Draft;

    public void RefreshTotals(IReadOnlyCollection<QuoteLine> lines)
    {
        EnsureDraft();
        var quoteLines = lines.Where(x => x.QuoteId == Id).ToArray();
        GrossAmount = quoteLines.Sum(x => x.GrossAmount);
        DiscountAmount = quoteLines.Sum(x => x.DiscountAmount);
        NetAmount = quoteLines.Sum(x => x.NetAmount);
        CostAmount = quoteLines.Sum(x => x.CostAmount);
        Amount = GrossAmount;
        DiscountPercent = GrossAmount == 0 ? 0 : decimal.Round(DiscountAmount * 100m / GrossAmount, 2);
        MarginPercent = NetAmount == 0 ? 0 : decimal.Round((NetAmount - CostAmount) * 100m / NetAmount, 2);
        Touch();
    }

    public void UpdateTerms(DateTimeOffset validUntilUtc, string paymentTerms)
    {
        EnsureDraft();
        ValidUntilUtc = validUntilUtc;
        PaymentTerms = Required(paymentTerms, nameof(paymentTerms));
        Touch();
    }

    public void Submit(QuoteApprovalLevel approvalLevel, DateTimeOffset nowUtc)
    {
        EnsureDraft();
        if (NetAmount <= 0) throw new InvalidOperationException("پیشنهاد باید حداقل یک ردیف با مبلغ خالص مثبت داشته باشد.");
        if (ValidUntilUtc <= nowUtc) throw new InvalidOperationException("تاریخ اعتبار پیشنهاد باید در آینده باشد.");
        ApprovalLevel = approvalLevel;
        SubmittedAtUtc = nowUtc;
        Status = approvalLevel == QuoteApprovalLevel.None ? QuoteStatus.Approved : QuoteStatus.PendingApproval;
        if (Status == QuoteStatus.Approved) ApprovedAtUtc = nowUtc;
        Touch();
    }

    public void ApplyDecision(QuoteDecision decision, bool approvalsComplete, DateTimeOffset nowUtc)
    {
        if (Status != QuoteStatus.PendingApproval) throw new InvalidOperationException("این پیشنهاد در انتظار تأیید نیست.");
        if (decision == QuoteDecision.Rejected)
        {
            Status = QuoteStatus.Rejected;
            Touch();
            return;
        }
        if (approvalsComplete)
        {
            Status = QuoteStatus.Approved;
            ApprovedAtUtc = nowUtc;
            Touch();
        }
    }

    public void MarkSent(DateTimeOffset nowUtc)
    {
        if (Status != QuoteStatus.Approved) throw new InvalidOperationException("فقط پیشنهاد تأییدشده قابل ارسال است.");
        if (ValidUntilUtc <= nowUtc) throw new InvalidOperationException("پیشنهاد منقضی‌شده قابل ارسال نیست.");
        Status = QuoteStatus.Sent;
        SentAtUtc = nowUtc;
        Touch();
    }

    public void Accept(DateTimeOffset nowUtc)
    {
        if (Status != QuoteStatus.Sent) throw new InvalidOperationException("فقط پیشنهاد ارسال‌شده قابل پذیرش است.");
        if (ValidUntilUtc <= nowUtc) throw new InvalidOperationException("اعتبار پیشنهاد پایان یافته است.");
        Status = QuoteStatus.Accepted;
        AcceptedAtUtc = nowUtc;
        Touch();
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status is QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired)
            throw new InvalidOperationException("وضعیت نهایی پیشنهاد قابل انقضا نیست.");
        if (ValidUntilUtc > nowUtc) throw new InvalidOperationException("تاریخ اعتبار پیشنهاد هنوز پایان نیافته است.");
        Status = QuoteStatus.Expired;
        Touch();
    }

    public void Approve()
    {
        if (DiscountPercent > 20 || MarginPercent < 15) throw new InvalidOperationException("Quote is outside the demo approval policy.");
        Status = QuoteStatus.Approved;
        ApprovedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Reject()
    {
        if (Status is not (QuoteStatus.PendingApproval or QuoteStatus.Submitted))
            throw new InvalidOperationException("Only a submitted quote can be rejected.");
        Status = QuoteStatus.Rejected;
        Touch();
    }

    public void ReassignCustomer(Guid customerId, string customerName)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Customer = Required(customerName, nameof(customerName));
        Touch();
    }

    private void EnsureDraft()
    {
        if (Status != QuoteStatus.Draft) throw new InvalidOperationException("فقط نسخه پیش‌نویس قابل ویرایش است.");
    }
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class QuoteApprovalPolicy
{
    public static QuoteApprovalLevel Resolve(decimal maximumLineDiscount, decimal marginPercent)
    {
        if (maximumLineDiscount is < 0 or > 20 || marginPercent < 15)
            throw new InvalidOperationException("تخفیف بیش از ۲۰٪ یا Margin کمتر از ۱۵٪ در نسخه نمونه مجاز نیست.");
        if (maximumLineDiscount <= 5 && marginPercent >= 25) return QuoteApprovalLevel.None;
        if (maximumLineDiscount <= 8 && marginPercent >= 22) return QuoteApprovalLevel.SalesSupervisor;
        if (maximumLineDiscount <= 12 && marginPercent >= 18) return QuoteApprovalLevel.CommercialManager;
        return QuoteApprovalLevel.JointSalesAndFinance;
    }

    public static IReadOnlyList<QuoteApprovalRole> RequiredRoles(QuoteApprovalLevel level) => level switch
    {
        QuoteApprovalLevel.SalesSupervisor => [QuoteApprovalRole.SalesSupervisor],
        QuoteApprovalLevel.CommercialManager => [QuoteApprovalRole.CommercialManager],
        QuoteApprovalLevel.JointSalesAndFinance => [QuoteApprovalRole.SalesManager, QuoteApprovalRole.FinanceManager],
        _ => []
    };
}

public sealed class QuoteLine(
    Guid id, string companyId, string branchId, string? territoryId, Guid quoteId, string productCode,
    string productName, string unit, decimal quantity, decimal listUnitPrice, decimal standardUnitCost,
    decimal discountPercent, string priceSource, DateTimeOffset priceEffectiveAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid QuoteId { get; } = quoteId;
    public string ProductCode { get; } = Required(productCode, nameof(productCode));
    public string ProductName { get; } = Required(productName, nameof(productName));
    public string Unit { get; } = Required(unit, nameof(unit));
    public decimal Quantity { get; } = quantity > 0 ? quantity : throw new ArgumentOutOfRangeException(nameof(quantity));
    public decimal ListUnitPrice { get; } = listUnitPrice > 0 ? listUnitPrice : throw new ArgumentOutOfRangeException(nameof(listUnitPrice));
    public decimal StandardUnitCost { get; } = standardUnitCost >= 0 ? standardUnitCost : throw new ArgumentOutOfRangeException(nameof(standardUnitCost));
    public decimal DiscountPercent { get; } = discountPercent is >= 0 and <= 20 ? discountPercent : throw new ArgumentOutOfRangeException(nameof(discountPercent));
    public string PriceSource { get; } = Required(priceSource, nameof(priceSource));
    public DateTimeOffset PriceEffectiveAtUtc { get; } = priceEffectiveAtUtc;
    public decimal GrossAmount => Quantity * ListUnitPrice;
    public decimal DiscountAmount => GrossAmount * DiscountPercent / 100m;
    public decimal NetAmount => GrossAmount - DiscountAmount;
    public decimal CostAmount => Quantity * StandardUnitCost;
    public decimal MarginPercent => NetAmount == 0 ? 0 : decimal.Round((NetAmount - CostAmount) * 100m / NetAmount, 2);

    private QuoteLine() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, "EF", "EF", "EF", 1, 1, 0, 0, "EF", DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class QuoteStatusHistory(
    Guid id, string companyId, string branchId, string? territoryId, Guid quoteId, QuoteStatus? fromStatus,
    QuoteStatus toStatus, string reason, Guid changedByUserId, DateTimeOffset changedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid QuoteId { get; } = quoteId;
    public QuoteStatus? FromStatus { get; } = fromStatus;
    public QuoteStatus ToStatus { get; } = toStatus;
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid ChangedByUserId { get; } = changedByUserId;
    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;
    private QuoteStatusHistory() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, null, QuoteStatus.Draft, "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class QuoteApprovalDecision(
    Guid id, string companyId, string branchId, string? territoryId, Guid quoteId, QuoteApprovalRole role,
    QuoteDecision decision, string comment, Guid decidedByUserId, DateTimeOffset decidedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid QuoteId { get; } = quoteId;
    public QuoteApprovalRole Role { get; } = role;
    public QuoteDecision Decision { get; } = decision;
    public string Comment { get; } = Required(comment, nameof(comment));
    public Guid DecidedByUserId { get; } = decidedByUserId;
    public DateTimeOffset DecidedAtUtc { get; } = decidedAtUtc;
    private QuoteApprovalDecision() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, QuoteApprovalRole.SalesSupervisor, QuoteDecision.Approved, "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
