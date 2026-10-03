using Crm.Application.Contracts;

namespace Crm.Application.Services;

/// <summary>Roadmap phase 7: tiered dealer commission with finance approval, and periodic dealer scorecards/ranking.</summary>
public interface IDealerIncentiveService
{
    CommissionWorkspaceDto GetCommissionWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period);
    CommissionPlanDto SavePlan(Guid currentUserId, OrganizationSelection organization, SaveCommissionPlanCommand command, DateTimeOffset nowUtc);
    CommissionRunResult CalculateCommissions(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period, DateTimeOffset nowUtc);
    CommissionStatementDto DecideCommission(Guid currentUserId, OrganizationSelection organization, Guid statementId,
        DecideCommissionCommand command, DateTimeOffset nowUtc);
    CommissionStatementDto SetSplit(Guid currentUserId, OrganizationSelection organization, Guid statementId, SetCommissionSplitCommand command, DateTimeOffset nowUtc);
    void RetryPayout(Guid currentUserId, OrganizationSelection organization, Guid statementId, DateTimeOffset nowUtc);
    DealerRankingDto GetRanking(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period);
    DealerEvaluationRunResult RunEvaluation(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period, DateTimeOffset nowUtc);
}
