using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Sales;

public enum OpportunityStage { Identified, Discovery, Qualified, SolutionOffer, Negotiation, Commit, Won, Lost }
public enum OpportunityRiskLevel { Low, Medium, High }

public sealed class Opportunity(
    Guid id,
    string code,
    string title,
    string customer,
    Guid customerId,
    decimal value,
    string owner,
    string companyId,
    string branchId,
    string? territoryId,
    Guid? ownerUserId = null,
    Guid? originLeadId = null,
    DateTimeOffset? expectedCloseAtUtc = null,
    string source = "Direct") : Entity(id), IOrganizationScoped
{
    public string Code { get; } = Required(code, nameof(code));
    public string Title { get; private set; } = Required(title, nameof(title));
    public string Customer { get; private set; } = Required(customer, nameof(customer));
    public Guid CustomerId { get; private set; } = customerId;
    public decimal Value { get; private set; } = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    public string Owner { get; private set; } = owner?.Trim() ?? string.Empty;
    public Guid? OwnerUserId { get; private set; } = ownerUserId;
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid? OriginLeadId { get; } = originLeadId;
    public OpportunityStage Stage { get; private set; } = OpportunityStage.Identified;
    public int Probability { get; private set; } = 10;
    public DateTimeOffset ExpectedCloseAtUtc { get; private set; } = expectedCloseAtUtc ?? DateTimeOffset.UtcNow.AddDays(30);
    public string Source { get; private set; } = Required(source, nameof(source));
    public string? NextAction { get; private set; }
    public DateTimeOffset? NextActionAtUtc { get; private set; }
    public DateTimeOffset LastActivityAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public string? Competitor { get; private set; }
    public OpportunityRiskLevel RiskLevel { get; private set; } = OpportunityRiskLevel.Medium;
    public string? OutcomeReason { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    private Opportunity() : this(Guid.Empty, "EF", "EF", "EF", Guid.Empty, 0, string.Empty, "EF", "EF", null) { }

    public void Update(string title, decimal value, DateTimeOffset expectedCloseAtUtc, string source,
        string? competitor, OpportunityRiskLevel riskLevel, string nextAction, DateTimeOffset nextActionAtUtc)
    {
        EnsureOpen();
        if (value <= 0) throw new InvalidOperationException("ارزش فرصت باید بیشتر از صفر باشد.");
        if (expectedCloseAtUtc <= DateTimeOffset.UtcNow.AddMinutes(-1)) throw new InvalidOperationException("تاریخ بستن مورد انتظار باید در آینده باشد.");
        if (nextActionAtUtc <= DateTimeOffset.UtcNow.AddMinutes(-1)) throw new InvalidOperationException("زمان اقدام بعدی باید در آینده باشد.");
        Title = Required(title, nameof(title));
        Value = value;
        ExpectedCloseAtUtc = expectedCloseAtUtc;
        Source = Required(source, nameof(source));
        Competitor = Optional(competitor);
        RiskLevel = riskLevel;
        NextAction = Required(nextAction, nameof(nextAction));
        NextActionAtUtc = nextActionAtUtc;
        Touch();
    }

    public void Assign(Guid ownerUserId, string owner)
    {
        EnsureOpen();
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user is required.", nameof(ownerUserId));
        OwnerUserId = ownerUserId;
        Owner = Required(owner, nameof(owner));
        Touch();
    }

    public void MoveTo(OpportunityStage target, string reason, DateTimeOffset changedAtUtc)
    {
        EnsureOpen();
        if (target == Stage) throw new InvalidOperationException("مرحله مقصد با مرحله فعلی یکسان است.");
        if (target is OpportunityStage.Won or OpportunityStage.Lost && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("ثبت دلیل نتیجه فرصت الزامی است.");
        if (target == OpportunityStage.Won && Stage != OpportunityStage.Commit)
            throw new InvalidOperationException("فرصت فقط از مرحله تعهد قابل برنده‌شدن است.");
        if (target is not (OpportunityStage.Won or OpportunityStage.Lost) &&
            Math.Abs(StageOrder(target) - StageOrder(Stage)) > 1)
            throw new InvalidOperationException("جابجایی بین مراحل باز باید یک مرحله در هر عملیات باشد.");
        Stage = target;
        Probability = ProbabilityFor(target);
        LastActivityAtUtc = changedAtUtc;
        if (target is OpportunityStage.Won or OpportunityStage.Lost)
        {
            OutcomeReason = Required(reason, nameof(reason));
            ClosedAtUtc = changedAtUtc;
            NextAction = null;
            NextActionAtUtc = null;
        }
        Touch();
    }

    public void Advance()
    {
        var target = Stage switch
        {
            OpportunityStage.Identified => OpportunityStage.Discovery,
            OpportunityStage.Discovery => OpportunityStage.Qualified,
            OpportunityStage.Qualified => OpportunityStage.SolutionOffer,
            OpportunityStage.SolutionOffer => OpportunityStage.Negotiation,
            OpportunityStage.Negotiation => OpportunityStage.Commit,
            _ => Stage
        };
        if (target != Stage) MoveTo(target, "Advance", DateTimeOffset.UtcNow);
    }

    public void RecordActivity(DateTimeOffset occurredAtUtc, string? nextAction, DateTimeOffset? nextActionAtUtc)
    {
        EnsureOpen();
        if (nextActionAtUtc.HasValue && nextActionAtUtc <= occurredAtUtc)
            throw new InvalidOperationException("زمان اقدام بعدی باید بعد از فعالیت باشد.");
        LastActivityAtUtc = occurredAtUtc;
        NextAction = Optional(nextAction);
        NextActionAtUtc = nextActionAtUtc;
        Touch();
    }

    public void ReassignCustomer(Guid customerId, string customerName)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Customer = Required(customerName, nameof(customerName));
        Touch();
    }

    private void EnsureOpen()
    {
        if (Stage is OpportunityStage.Won or OpportunityStage.Lost)
            throw new InvalidOperationException("فرصت بسته‌شده قابل تغییر نیست.");
    }

    public static int ProbabilityFor(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Identified => 10,
        OpportunityStage.Discovery => 25,
        OpportunityStage.Qualified => 45,
        OpportunityStage.SolutionOffer => 60,
        OpportunityStage.Negotiation => 75,
        OpportunityStage.Commit => 90,
        OpportunityStage.Won => 100,
        _ => 0
    };

    private static int StageOrder(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Identified => 0,
        OpportunityStage.Discovery => 1,
        OpportunityStage.Qualified => 2,
        OpportunityStage.SolutionOffer => 3,
        OpportunityStage.Negotiation => 4,
        OpportunityStage.Commit => 5,
        _ => 6
    };
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
