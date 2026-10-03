using System.Globalization;
using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Channel;

public enum CommissionStatementStatus { Calculated, OnHold, Approved, Rejected }
public enum DealerTier { Bronze, Silver, Gold, Platinum }

/// <summary>One step of a tiered plan: from this target-achievement percentage upward, this commission rate applies.</summary>
public sealed record CommissionTier(decimal MinAchievementPercent, decimal RatePercent);

/// <summary>
/// Company-wide tiered commission plan: the rate applied to a dealer's net sales depends on how much of the
/// period target the dealer achieved. Tiers are stored compactly ("0:0;80:1.5;100:2.5") and validated here.
/// </summary>
public sealed class DealerCommissionPlan : Entity
{
    public const decimal MaxRatePercent = 20m;

    public DealerCommissionPlan(Guid id, string companyId, string name, IEnumerable<CommissionTier> tiers, Guid updatedByUserId) : base(id)
    {
        CompanyId = Required(companyId, nameof(companyId));
        Name = PlanName(name);
        TierDefinition = Serialize(Validate(tiers));
        UpdatedByUserId = updatedByUserId;
    }

    private DealerCommissionPlan() : base(Guid.Empty)
    {
        CompanyId = Name = "EF";
        TierDefinition = "0:0";
    }

    public string CompanyId { get; private set; }
    public string Name { get; private set; }
    public string TierDefinition { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    public IReadOnlyList<CommissionTier> Tiers => Parse(TierDefinition);

    public void Update(string name, IEnumerable<CommissionTier> tiers, Guid updatedByUserId)
    {
        // Validate everything before assigning so a rejected change leaves the plan untouched.
        var validName = PlanName(name);
        var definition = Serialize(Validate(tiers));
        Name = validName;
        TierDefinition = definition;
        UpdatedByUserId = updatedByUserId;
        Touch();
    }

    /// <summary>Rate of the highest tier whose threshold the achievement reaches.</summary>
    public decimal RateFor(decimal achievementPercent) =>
        Tiers.Where(x => achievementPercent >= x.MinAchievementPercent).Select(x => x.RatePercent).DefaultIfEmpty(0).Last();

    private static string PlanName(string name)
    {
        var value = Required(name, nameof(name));
        return value.Length <= 160 ? value : throw new InvalidOperationException("نام طرح کمیسیون حداکثر ۱۶۰ نویسه است.");
    }

    private static List<CommissionTier> Validate(IEnumerable<CommissionTier> tiers)
    {
        var ordered = tiers.OrderBy(x => x.MinAchievementPercent).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException("حداقل یک پله کمیسیون لازم است.");
        if (ordered[0].MinAchievementPercent != 0) throw new InvalidOperationException("اولین پله باید از تحقق ۰٪ شروع شود.");
        if (ordered.Select(x => x.MinAchievementPercent).Distinct().Count() != ordered.Count)
            throw new InvalidOperationException("آستانهٔ پله‌ها نباید تکراری باشد.");
        if (ordered.Any(x => x.MinAchievementPercent < 0 || x.MinAchievementPercent > 300))
            throw new InvalidOperationException("آستانهٔ تحقق باید بین ۰ تا ۳۰۰ درصد باشد.");
        if (ordered.Any(x => x.RatePercent < 0 || x.RatePercent > MaxRatePercent))
            throw new InvalidOperationException($"نرخ کمیسیون باید بین ۰ تا {MaxRatePercent} درصد باشد.");
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].RatePercent < ordered[i - 1].RatePercent)
                throw new InvalidOperationException("نرخ پله‌های بالاتر نباید کمتر از پله‌های پایین‌تر باشد.");
        return ordered;
    }

    private static string Serialize(IEnumerable<CommissionTier> tiers) => string.Join(";", tiers.Select(x =>
        x.MinAchievementPercent.ToString(CultureInfo.InvariantCulture) + ":" + x.RatePercent.ToString(CultureInfo.InvariantCulture)));

    private static IReadOnlyList<CommissionTier> Parse(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split(':'))
        .Select(x => new CommissionTier(decimal.Parse(x[0], CultureInfo.InvariantCulture), decimal.Parse(x[1], CultureInfo.InvariantCulture)))
        .ToList();

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

/// <summary>
/// Monthly commission for one dealer. Inputs (net sales from the ERP projection, target, overdue receivables) are
/// frozen at calculation so the statement stays explainable after source data changes. Separation of duties:
/// the user who calculated a statement cannot approve it.
/// </summary>
public sealed class DealerCommissionStatement : Entity, IOrganizationScoped
{
    public DealerCommissionStatement(Guid id, Guid dealerId, string companyId, string branchId, string? territoryId,
        DateTimeOffset periodFromUtc, DateTimeOffset periodToUtc, decimal netSales, decimal targetAmount, decimal overdueAmount,
        DealerCommissionPlan plan, Guid calculatedByUserId, DateTimeOffset calculatedAtUtc) : base(id)
    {
        if (periodToUtc <= periodFromUtc) throw new ArgumentException("Invalid period.", nameof(periodToUtc));
        if (targetAmount <= 0) throw new InvalidOperationException("برای محاسبهٔ کمیسیون، هدف دورهٔ نماینده لازم است.");
        if (netSales < 0 || overdueAmount < 0) throw new ArgumentOutOfRangeException(nameof(netSales));
        DealerId = dealerId;
        CompanyId = companyId;
        BranchId = branchId;
        TerritoryId = territoryId;
        PeriodFromUtc = periodFromUtc;
        PeriodToUtc = periodToUtc;
        NetSales = netSales;
        TargetAmount = targetAmount;
        OverdueAmount = overdueAmount;
        PlanId = plan.Id;
        AchievementPercent = Math.Round(netSales / targetAmount * 100m, 2);
        RatePercent = plan.RateFor(AchievementPercent);
        CommissionAmount = Math.Round(netSales * RatePercent / 100m, 0);
        Status = overdueAmount > 0 ? CommissionStatementStatus.OnHold : CommissionStatementStatus.Calculated;
        CalculatedByUserId = calculatedByUserId;
        CalculatedAtUtc = calculatedAtUtc;
    }

    private DealerCommissionStatement() : base(Guid.Empty)
    {
        CompanyId = BranchId = "EF";
    }

    public Guid DealerId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public DateTimeOffset PeriodFromUtc { get; private set; }
    public DateTimeOffset PeriodToUtc { get; private set; }
    public decimal NetSales { get; private set; }
    public decimal TargetAmount { get; private set; }
    public decimal OverdueAmount { get; private set; }
    public Guid PlanId { get; private set; }
    public decimal AchievementPercent { get; private set; }
    public decimal RatePercent { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public CommissionStatementStatus Status { get; private set; }
    public Guid CalculatedByUserId { get; private set; }
    public DateTimeOffset CalculatedAtUtc { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public string? DecisionNote { get; private set; }

    public bool IsFinal => Status is CommissionStatementStatus.Approved or CommissionStatementStatus.Rejected;

    /// <summary>Approves the statement. A statement on hold (overdue receivables) needs an explicit justification.</summary>
    public void Approve(Guid approverUserId, string? note, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        if (approverUserId == CalculatedByUserId)
            throw new InvalidOperationException("محاسبه‌کنندهٔ کمیسیون نمی‌تواند آن را تأیید کند (تفکیک وظایف).");
        if (Status == CommissionStatementStatus.OnHold && string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("نماینده مطالبهٔ معوق دارد؛ تأیید کمیسیون معلق نیازمند توضیح است.");
        Decide(CommissionStatementStatus.Approved, approverUserId, note, nowUtc);
    }

    public void Reject(Guid approverUserId, string note, DateTimeOffset nowUtc)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("دلیل رد کمیسیون الزامی است.");
        Decide(CommissionStatementStatus.Rejected, approverUserId, note, nowUtc);
    }

    private void Decide(CommissionStatementStatus status, Guid userId, string? note, DateTimeOffset nowUtc)
    {
        Status = status;
        DecidedByUserId = userId;
        DecidedAtUtc = nowUtc;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
        Touch();
    }

    private void EnsureOpen()
    {
        if (IsFinal) throw new InvalidOperationException("صورت کمیسیون قبلاً نهایی شده است.");
    }
}

/// <summary>Weighted scorecard for one dealer and period; persisted so rankings stay comparable over time.</summary>
public sealed class DealerEvaluation : Entity, IOrganizationScoped
{
    public const decimal AchievementWeight = 0.40m, CollectionWeight = 0.25m, GrowthWeight = 0.15m, ServiceWeight = 0.20m;

    public DealerEvaluation(Guid id, Guid dealerId, string companyId, string branchId, string? territoryId,
        DateTimeOffset periodFromUtc, DateTimeOffset periodToUtc, decimal achievementScore, decimal collectionScore,
        decimal growthScore, decimal serviceScore, Guid evaluatedByUserId, DateTimeOffset evaluatedAtUtc) : base(id)
    {
        DealerId = dealerId;
        CompanyId = companyId;
        BranchId = branchId;
        TerritoryId = territoryId;
        PeriodFromUtc = periodFromUtc;
        PeriodToUtc = periodToUtc;
        AchievementScore = Score(achievementScore);
        CollectionScore = Score(collectionScore);
        GrowthScore = Score(growthScore);
        ServiceScore = Score(serviceScore);
        TotalScore = Math.Round(AchievementScore * AchievementWeight + CollectionScore * CollectionWeight +
            GrowthScore * GrowthWeight + ServiceScore * ServiceWeight, 1);
        Tier = TierFor(TotalScore);
        EvaluatedByUserId = evaluatedByUserId;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    private DealerEvaluation() : base(Guid.Empty)
    {
        CompanyId = BranchId = "EF";
    }

    public Guid DealerId { get; private set; }
    public string CompanyId { get; private set; }
    public string BranchId { get; private set; }
    public string? TerritoryId { get; private set; }
    public DateTimeOffset PeriodFromUtc { get; private set; }
    public DateTimeOffset PeriodToUtc { get; private set; }
    public decimal AchievementScore { get; private set; }
    public decimal CollectionScore { get; private set; }
    public decimal GrowthScore { get; private set; }
    public decimal ServiceScore { get; private set; }
    public decimal TotalScore { get; private set; }
    public DealerTier Tier { get; private set; }
    public int Rank { get; private set; }
    public int RankedDealerCount { get; private set; }
    public Guid EvaluatedByUserId { get; private set; }
    public DateTimeOffset EvaluatedAtUtc { get; private set; }

    public void AssignRank(int rank, int dealerCount)
    {
        if (rank < 1 || rank > dealerCount) throw new ArgumentOutOfRangeException(nameof(rank));
        Rank = rank;
        RankedDealerCount = dealerCount;
    }

    public static DealerTier TierFor(decimal totalScore) => totalScore switch
    {
        >= 85 => DealerTier.Platinum,
        >= 70 => DealerTier.Gold,
        >= 50 => DealerTier.Silver,
        _ => DealerTier.Bronze
    };

    /// <summary>Target achievement: 120% of target or more earns the full score.</summary>
    public static decimal AchievementScoreFor(decimal netSales, decimal targetAmount) =>
        targetAmount <= 0 ? 0 : Math.Min(netSales / targetAmount * 100m, 120m) / 120m * 100m;

    /// <summary>Collection health: share of the balance that is not overdue. Unknown (no ledger data) scores neutral 50.</summary>
    public static decimal CollectionScoreFor(decimal? balance, decimal? overdue) =>
        balance is null || overdue is null ? 50 : balance <= 0 ? (overdue > 0 ? 0 : 100) : 100m * (1 - Math.Min(overdue.Value / balance.Value, 1m));

    /// <summary>Growth versus the previous period: flat = 50, +50% or more = 100, -50% or worse = 0. Unknown history = 50.</summary>
    public static decimal GrowthScoreFor(decimal netSales, decimal? previousNetSales) =>
        previousNetSales is null or <= 0 ? 50 : 50m + (netSales - previousNetSales.Value) / previousNetSales.Value * 100m;

    /// <summary>Service quality of the dealer's customers: half CSAT, half SLA breaches (each breach costs 20 points).</summary>
    public static decimal ServiceScoreFor(int breachedCases, decimal? averageSatisfaction)
    {
        var sla = 100m - Math.Min(breachedCases * 20m, 100m);
        return averageSatisfaction is { } csat ? (sla + csat / 5m * 100m) / 2m : sla;
    }

    private static decimal Score(decimal value) => Math.Round(Math.Clamp(value, 0m, 100m), 1);
}
