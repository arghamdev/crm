using Crm.Domain.Channel;

namespace Crm.Application.Contracts;

public sealed record CommissionTierDto(decimal MinAchievementPercent, decimal RatePercent);

public sealed record CommissionPlanDto(Guid Id, string Name, IReadOnlyList<CommissionTierDto> Tiers, long Version);

public sealed record SaveCommissionPlanCommand(string? Name, IReadOnlyList<CommissionTierDto>? Tiers, long ExpectedVersion);

public sealed record CommissionStatementDto(
    Guid Id,
    Guid DealerId,
    string DealerCode,
    string DealerName,
    DateTimeOffset PeriodFromUtc,
    decimal NetSales,
    decimal TargetAmount,
    decimal AchievementPercent,
    decimal RatePercent,
    decimal CommissionAmount,
    decimal OverdueAmount,
    CommissionStatementStatus Status,
    string CalculatedBy,
    DateTimeOffset CalculatedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    bool CanDecide,
    long Version);

public sealed record CommissionWorkspaceDto(
    DateTimeOffset PeriodFromUtc,
    CommissionPlanDto? Plan,
    IReadOnlyList<CommissionStatementDto> Statements,
    decimal TotalCommission,
    decimal ApprovedCommission,
    int OnHoldCount,
    bool CanManage,
    bool CanApprove);

public sealed record CommissionRunResult(int Calculated, int Skipped, IReadOnlyList<string> Issues);

public sealed record DecideCommissionCommand(bool Approve, string? Note, long ExpectedVersion);

public sealed record DealerEvaluationDto(
    Guid DealerId,
    string DealerCode,
    string DealerName,
    string BranchId,
    int Rank,
    int RankedDealerCount,
    decimal AchievementScore,
    decimal CollectionScore,
    decimal GrowthScore,
    decimal ServiceScore,
    decimal TotalScore,
    DealerTier Tier);

public sealed record DealerRankingDto(
    DateTimeOffset PeriodFromUtc,
    IReadOnlyList<DealerEvaluationDto> Items,
    DateTimeOffset? EvaluatedAtUtc,
    bool CanRun);

public sealed record DealerEvaluationRunResult(int Evaluated, IReadOnlyList<string> Notes);
