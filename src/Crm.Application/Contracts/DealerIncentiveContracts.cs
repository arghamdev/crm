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
    long Version,
    IReadOnlyList<CommissionSplitLineDto>? Lines = null,
    CommissionPayoutDto? Payout = null,
    bool CanSplit = false,
    bool CanRetryPayout = false);

public sealed record CommissionWorkspaceDto(
    DateTimeOffset PeriodFromUtc,
    CommissionPlanDto? Plan,
    IReadOnlyList<CommissionStatementDto> Statements,
    decimal TotalCommission,
    decimal ApprovedCommission,
    int OnHoldCount,
    bool CanManage,
    bool CanApprove,
    IReadOnlyList<UserOptionDto>? InternalUsers = null);

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

public sealed record SetCommissionSplitCommand(Guid? InternalUserId, decimal InternalSharePercent, long ExpectedVersion);

public sealed record CommissionSplitLineDto(string Beneficiary, Guid? UserId, decimal SharePercent, decimal Amount);

public sealed record CommissionPayoutDto(CommissionPayoutStatus Status, int AttemptCount, string? ExternalReference, string? LastError,
    DateTimeOffset? CompletedAtUtc);

public sealed record UserOptionDto(Guid Id, string Name);

public sealed record CommissionPayoutDispatchResult(int Sent, int Failed, int DeadLettered);

public sealed record CommissionPayoutResult(bool Succeeded, string? ExternalReference, string? Error);

public sealed record DealerGuaranteeDto(Guid Id, DealerGuaranteeType Type, string Number, string? Issuer, decimal Amount, string IssuedOn,
    string? ExpiresOn, DealerGuaranteeStatus Status, bool IsEffective, bool ExpiresSoon, string? Notes, string? DecisionReason, long Version);

public sealed record DealerTrainingDto(Guid Id, string Title, DealerTrainingTopic Topic, string HeldOn, decimal Hours, int Participants,
    decimal? Score, string? CertificateValidTo, bool CertificateExpired);

public sealed record DealerAssuranceDto(Guid DealerId, string DealerCode, string DealerName, IReadOnlyList<DealerGuaranteeDto> Guarantees,
    IReadOnlyList<DealerTrainingDto> Trainings, decimal EffectiveGuaranteeAmount, decimal? CreditLimit, decimal? CoveragePercent,
    int ExpiringSoonCount, decimal TrainingHoursLast12Months, bool CanReadGuarantees, bool CanManageGuarantees, bool CanReadTrainings,
    bool CanManageTrainings);

/// <summary>Dates are Jalali strings (1405/07/01) as typed in the form.</summary>
public sealed record SaveDealerGuaranteeCommand(DealerGuaranteeType Type, string? Number, string? Issuer, decimal Amount, string? IssuedOn,
    string? ExpiresOn, string? Notes);

public sealed record DecideDealerGuaranteeCommand(bool Forfeit, string? Reason, long ExpectedVersion);

public sealed record SaveDealerTrainingCommand(string? Title, DealerTrainingTopic Topic, string? HeldOn, decimal Hours, int Participants,
    decimal? Score, string? CertificateValidTo);
