using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Service;

namespace Crm.Application.Services;

/// <summary>
/// Dealer commission and evaluation. Inputs come from the read-only ERP/accounting projections already held by the
/// channel module (performance and financial snapshots) plus CRM-owned targets; results are persisted so a statement
/// or a ranking stays explainable after the projections are re-synced. Periods are calendar months in UTC.
/// </summary>
public sealed class DealerIncentiveService(ICrmDataStore store, IAccessSnapshotService access) : IDealerIncentiveService
{
    /// <summary>Start of the Jalali month containing the value (see <see cref="ChannelPeriod"/>).</summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset value) => ChannelPeriod.StartOf(value);

    public CommissionWorkspaceDto GetCommissionWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Read");
        var from = PeriodStart(period);
        return store.Read(data =>
        {
            var plan = data.Find<DealerCommissionPlan>(x => x.CompanyId == organization.CompanyId).SingleOrDefault();
            var canApprove = Has(snapshot, organization.CompanyId, "Dealer.Commission.Approve");
            var statements = data.Find<DealerCommissionStatement>(x => x.CompanyId == organization.CompanyId && x.PeriodFromUtc == from)
                .Where(x => InContext(snapshot, organization, "Dealer.Commission.Read", x))
                .OrderBy(x => x.Status == CommissionStatementStatus.Rejected).ThenByDescending(x => x.CommissionAmount).ToList();
            var dealers = Dealers(data, statements.Select(x => x.DealerId));
            var users = Users(data, statements.SelectMany(x => new[] { x.CalculatedByUserId, x.DecidedByUserId ?? Guid.Empty, x.InternalSplit?.UserId ?? Guid.Empty }));
            var live = statements.Where(x => x.Status != CommissionStatementStatus.Rejected).ToList();
            var ids = statements.Select(x => x.Id).ToArray();
            var payouts = data.Find<CommissionPayoutMessage>(x => ids.Contains(x.StatementId)).ToDictionary(x => x.StatementId);
            var canManage = Has(snapshot, organization.CompanyId, "Dealer.Commission.Manage");
            return new CommissionWorkspaceDto(from, plan is null ? null : Map(plan),
                statements.Select(x => Map(x, dealers, users, canApprove && currentUserId != x.CalculatedByUserId, payouts.GetValueOrDefault(x.Id),
                    canManage, canApprove)).ToList(),
                live.Sum(x => x.CommissionAmount),
                live.Where(x => x.Status == CommissionStatementStatus.Approved).Sum(x => x.CommissionAmount),
                live.Count(x => x.Status == CommissionStatementStatus.OnHold),
                canManage,
                canApprove,
                canManage ? InternalUsers(data, organization.CompanyId, DateTimeOffset.UtcNow) : []);
        });
    }

    /// <summary>Split credit on an open statement: an internal co-seller receives a share, the dealer the rest.</summary>
    public CommissionStatementDto SetSplit(Guid currentUserId, OrganizationSelection organization, Guid statementId,
        SetCommissionSplitCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Manage");
        return store.Write(data =>
        {
            var statement = data.Find<DealerCommissionStatement>(x => x.Id == statementId).SingleOrDefault(x =>
                InContext(snapshot, organization, "Dealer.Commission.Manage", x)) ?? throw new KeyNotFoundException("صورت کمیسیون پیدا نشد.");
            if (statement.Version != command.ExpectedVersion) throw new InvalidOperationException("صورت کمیسیون تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            if (command.InternalUserId is { } userId && !InternalUsers(data, organization.CompanyId, nowUtc).Any(x => x.Id == userId))
                throw new InvalidOperationException("همکار انتخاب‌شده کاربر داخلی فعال این شرکت نیست.");
            statement.SetSplit(command.InternalUserId, command.InternalUserId is null ? 0 : command.InternalSharePercent);
            Audit(data, currentUserId, "Dealer.CommissionSplit", $"{statement.Id:N} {statement.SplitDefinition ?? "dealer:100"}", nowUtc);
            return Map(statement, Dealers(data, [statement.DealerId]), Users(data, [statement.CalculatedByUserId, command.InternalUserId ?? Guid.Empty]),
                false, null, true, false);
        });
    }

    /// <summary>Re-queues a payout that accounting rejected or that ran out of attempts.</summary>
    public void RetryPayout(Guid currentUserId, OrganizationSelection organization, Guid statementId, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Approve");
        store.Write(data =>
        {
            var message = data.Find<CommissionPayoutMessage>(x => x.StatementId == statementId).SingleOrDefault(x =>
                InContext(snapshot, organization, "Dealer.Commission.Approve", x)) ?? throw new KeyNotFoundException("پیام ارسال کمیسیون پیدا نشد.");
            message.Retry(nowUtc);
            Audit(data, currentUserId, "Dealer.CommissionPayoutRetried", message.IdempotencyKey, nowUtc);
            return true;
        });
    }

    private static List<UserOptionDto> InternalUsers(CrmDataSet data, string companyId, DateTimeOffset nowUtc)
    {
        var ids = data.Find<UserRoleAssignment>(x => x.CompanyId == companyId && x.RoleKey != "DealerUser").Where(x => x.IsEffective(nowUtc))
            .Select(x => x.CrmUserId).Distinct().ToArray();
        return data.Find<CrmUser>(x => ids.Contains(x.Id)).Where(x => x.IsActiveAt(nowUtc)).OrderBy(x => x.DisplayName)
            .Select(x => new UserOptionDto(x.Id, x.DisplayName)).ToList();
    }

    public CommissionPlanDto SavePlan(Guid currentUserId, OrganizationSelection organization, SaveCommissionPlanCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Manage");
        if (string.IsNullOrWhiteSpace(command.Name)) throw new InvalidOperationException("نام طرح کمیسیون الزامی است.");
        var tiers = (command.Tiers ?? []).Select(x => new CommissionTier(x.MinAchievementPercent, x.RatePercent)).ToList();
        return store.Write(data =>
        {
            var plan = data.Find<DealerCommissionPlan>(x => x.CompanyId == organization.CompanyId).SingleOrDefault();
            if (plan is null)
            {
                if (command.ExpectedVersion != 0) throw new InvalidOperationException("طرح کمیسیون تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
                plan = new DealerCommissionPlan(Guid.NewGuid(), organization.CompanyId, command.Name, tiers, currentUserId);
                data.DealerCommissionPlans.Add(plan);
            }
            else
            {
                if (plan.Version != command.ExpectedVersion) throw new InvalidOperationException("طرح کمیسیون تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
                plan.Update(command.Name, tiers, currentUserId);
            }
            Audit(data, currentUserId, "Dealer.CommissionPlanChanged", $"{organization.CompanyId}: {plan.TierDefinition}", nowUtc);
            return Map(plan);
        });
    }

    /// <summary>
    /// Calculates the period for every active dealer in scope. An approved statement is final and is never recalculated;
    /// an open one is superseded (rejected with a note) so the history of figures is kept.
    /// </summary>
    public CommissionRunResult CalculateCommissions(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Manage");
        var (from, to) = ChannelPeriod.MonthOf(period);
        return store.Write(data =>
        {
            var plan = data.Find<DealerCommissionPlan>(x => x.CompanyId == organization.CompanyId).SingleOrDefault() ??
                throw new InvalidOperationException("ابتدا طرح کمیسیون شرکت را تعریف کنید.");
            var issues = new List<string>();
            int calculated = 0, skipped = 0;
            foreach (var dealer in ActiveDealers(data, snapshot, organization, "Dealer.Commission.Manage"))
            {
                var existing = data.Find<DealerCommissionStatement>(x => x.DealerId == dealer.Id && x.PeriodFromUtc == from)
                    .Where(x => x.Status != CommissionStatementStatus.Rejected).ToList();
                if (existing.Any(x => x.Status == CommissionStatementStatus.Approved))
                {
                    skipped++;
                    issues.Add($"{dealer.Code}: صورت کمیسیون این دوره قبلاً تأیید شده است.");
                    continue;
                }
                var target = Target(data, dealer, from, to);
                var sales = Sales(data, dealer, from, to);
                if (target is null || sales is null)
                {
                    skipped++;
                    issues.Add($"{dealer.Code}: {(target is null ? "هدف دوره" : "فروش همگام‌شده از ERP")} موجود نیست.");
                    continue;
                }
                foreach (var old in existing) old.Reject(currentUserId, "جایگزین با محاسبهٔ جدید", nowUtc);
                var overdue = LatestFinancial(data, dealer)?.OverdueAmount ?? 0;
                data.DealerCommissionStatements.Add(new DealerCommissionStatement(Guid.NewGuid(), dealer.Id, dealer.CompanyId,
                    dealer.BranchId, dealer.TerritoryId, from, to, sales.NetSales, target.Amount, overdue, plan, currentUserId, nowUtc));
                calculated++;
            }
            if (calculated > 0)
                foreach (var approver in NotificationOutbox.UsersWithPermission(data, organization.CompanyId, "Dealer.Commission.Approve", nowUtc)
                             .Where(x => x != currentUserId))
                    NotificationOutbox.Enqueue(data, organization.CompanyId, approver, Crm.Domain.Notifications.NotificationCategory.CommissionApproval,
                        $"کمیسیون {ChannelPeriod.Label(from)} در انتظار تأیید",
                        $"{calculated} صورت کمیسیون نماینده محاسبه شد و منتظر تأیید مالی است.",
                        $"/dealers/commissions?period={ChannelPeriod.Key(from)}",
                        $"commission-run:{organization.CompanyId}:{ChannelPeriod.Key(from)}:{nowUtc:yyyyMMddHHmmss}", nowUtc);
            Audit(data, currentUserId, "Dealer.CommissionCalculated", $"{organization.CompanyId} {ChannelPeriod.Key(from)}: {calculated} calculated, {skipped} skipped", nowUtc);
            return new CommissionRunResult(calculated, skipped, issues);
        });
    }

    public CommissionStatementDto DecideCommission(Guid currentUserId, OrganizationSelection organization, Guid statementId,
        DecideCommissionCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Commission.Approve");
        return store.Write(data =>
        {
            var statement = data.Find<DealerCommissionStatement>(x => x.Id == statementId).SingleOrDefault(x =>
                InContext(snapshot, organization, "Dealer.Commission.Approve", x)) ?? throw new KeyNotFoundException("صورت کمیسیون پیدا نشد.");
            if (statement.Version != command.ExpectedVersion) throw new InvalidOperationException("صورت کمیسیون تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            if (command.Approve) statement.Approve(currentUserId, command.Note, nowUtc);
            else statement.Reject(currentUserId, command.Note ?? string.Empty, nowUtc);
            Audit(data, currentUserId, "Dealer.CommissionDecided", $"{statement.Id:N} {statement.Status} {statement.CommissionAmount}", nowUtc);
            var dealers = Dealers(data, [statement.DealerId]);
            var users = Users(data, [statement.CalculatedByUserId, currentUserId, statement.InternalSplit?.UserId ?? Guid.Empty]);
            CommissionPayoutMessage? payout = null;
            if (statement.Status == CommissionStatementStatus.Approved)
            {
                // Handed to accounting through the outbox, in the same write as the approval (no approval without a payable).
                payout = new CommissionPayoutMessage(Guid.NewGuid(), statement, Payload(statement, dealers, users, nowUtc), nowUtc);
                data.CommissionPayoutMessages.Add(payout);
            }
            return Map(statement, dealers, users, false, payout, false, false);
        });
    }

    public DealerRankingDto GetRanking(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Evaluation.Read");
        var from = PeriodStart(period);
        return store.Read(data =>
        {
            var evaluations = data.Find<DealerEvaluation>(x => x.CompanyId == organization.CompanyId && x.PeriodFromUtc == from)
                .Where(x => InContext(snapshot, organization, "Dealer.Evaluation.Read", x)).ToList();
            // Each run ranks the whole set together; show only the latest run so ranks are consistent.
            var latest = evaluations.Select(x => (DateTimeOffset?)x.EvaluatedAtUtc).DefaultIfEmpty(null).Max();
            var current = evaluations.Where(x => x.EvaluatedAtUtc == latest).OrderBy(x => x.Rank).ToList();
            var dealers = Dealers(data, current.Select(x => x.DealerId));
            return new DealerRankingDto(from, current.Select(x => Map(x, dealers)).ToList(), latest,
                Has(snapshot, organization.CompanyId, "Dealer.Evaluation.Run"));
        });
    }

    public DealerEvaluationRunResult RunEvaluation(Guid currentUserId, OrganizationSelection organization, DateTimeOffset period, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        Require(snapshot, organization.CompanyId, "Dealer.Evaluation.Run");
        var (from, to) = ChannelPeriod.MonthOf(period);
        var evaluatedAt = nowUtc;
        return store.Write(data =>
        {
            var notes = new List<string>();
            var evaluations = new List<DealerEvaluation>();
            foreach (var dealer in ActiveDealers(data, snapshot, organization, "Dealer.Evaluation.Run"))
            {
                var target = Target(data, dealer, from, to);
                var sales = Sales(data, dealer, from, to);
                var (previousFrom, previousTo) = ChannelPeriod.Previous(from);
                var previous = Sales(data, dealer, previousFrom, previousTo);
                var financial = LatestFinancial(data, dealer);
                if (target is null || sales is null) notes.Add($"{dealer.Code}: هدف یا فروش دوره موجود نیست؛ امتیاز تحقق صفر منظور شد.");
                var (breaches, satisfaction) = ServiceQuality(data, dealer, from, to, nowUtc);
                evaluations.Add(new DealerEvaluation(Guid.NewGuid(), dealer.Id, dealer.CompanyId, dealer.BranchId, dealer.TerritoryId, from, to,
                    DealerEvaluation.AchievementScoreFor(sales?.NetSales ?? 0, target?.Amount ?? 0),
                    DealerEvaluation.CollectionScoreFor(financial?.Balance, financial?.OverdueAmount),
                    DealerEvaluation.GrowthScoreFor(sales?.NetSales ?? 0, previous?.NetSales),
                    DealerEvaluation.ServiceScoreFor(breaches, satisfaction), currentUserId, evaluatedAt));
            }
            var dealers = Dealers(data, evaluations.Select(x => x.DealerId));
            var ranked = evaluations.OrderByDescending(x => x.TotalScore).ThenByDescending(x => x.AchievementScore)
                .ThenBy(x => dealers.GetValueOrDefault(x.DealerId).Code, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ranked.Count; i++) ranked[i].AssignRank(i + 1, ranked.Count);
            data.DealerEvaluations.AddRange(ranked);
            Audit(data, currentUserId, "Dealer.EvaluationRun", $"{organization.CompanyId} {ChannelPeriod.Key(from)}: {ranked.Count} dealers", nowUtc);
            return new DealerEvaluationRunResult(ranked.Count, notes);
        });
    }

    /// <summary>SLA breaches and CSAT of service cases opened in the period for the dealer's assigned customers.</summary>
    private static (int Breaches, decimal? Satisfaction) ServiceQuality(CrmDataSet data, Dealer dealer, DateTimeOffset from, DateTimeOffset to, DateTimeOffset nowUtc)
    {
        var customers = data.Find<DealerCustomerAssignment>(x => x.DealerId == dealer.Id)
            .Where(x => x.ValidFromUtc < to && (x.ValidToUtc == null || x.ValidToUtc > from)).Select(x => x.CustomerId).Distinct().ToArray();
        if (customers.Length == 0) return (0, null);
        var cases = data.Find<ServiceCase>(x => customers.Contains(x.CustomerId) && x.OpenedAtUtc >= from && x.OpenedAtUtc < to);
        var at = nowUtc < to ? nowUtc : to;
        var breaches = cases.Count(x => x.FirstResponseSla(at) == ServiceSlaState.Breached || x.ResolutionSla(at) == ServiceSlaState.Breached);
        var rated = cases.Where(x => x.SatisfactionScore.HasValue).Select(x => (decimal)x.SatisfactionScore!.Value).ToList();
        return (breaches, rated.Count == 0 ? null : rated.Average());
    }

    private static IEnumerable<Dealer> ActiveDealers(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, string permission) =>
        data.Find<Dealer>(x => x.CompanyId == organization.CompanyId && x.Status == DealerStatus.Active)
            .Where(x => InContext(snapshot, organization, permission, x)).OrderBy(x => x.Code).ToList();

    private static DealerTarget? Target(CrmDataSet data, Dealer dealer, DateTimeOffset from, DateTimeOffset to) =>
        data.Find<DealerTarget>(x => x.DealerId == dealer.Id && x.PeriodFromUtc == from && x.PeriodToUtc == to)
            .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();

    /// <summary>Latest ERP sales snapshot for exactly this period; snapshots carrying another branch than the dealer's are ignored.</summary>
    private static DealerPerformanceSnapshot? Sales(CrmDataSet data, Dealer dealer, DateTimeOffset from, DateTimeOffset to) =>
        data.Find<DealerPerformanceSnapshot>(x => x.DealerId == dealer.Id && x.PeriodFromUtc == from && x.PeriodToUtc == to)
            .Where(x => Same(x.BranchId, dealer.BranchId)).OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();

    private static DealerFinancialSnapshot? LatestFinancial(CrmDataSet data, Dealer dealer) =>
        data.Find<DealerFinancialSnapshot>(x => x.DealerId == dealer.Id).Where(x => Same(x.BranchId, dealer.BranchId))
            .OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();

    private static Dictionary<Guid, (string Code, string Name, string BranchId)> Dealers(CrmDataSet data, IEnumerable<Guid> ids)
    {
        var set = ids.Distinct().ToArray();
        return data.Find<Dealer>(x => set.Contains(x.Id)).ToDictionary(x => x.Id, x => (x.Code, x.TradeName, x.BranchId));
    }

    private static Dictionary<Guid, string> Users(CrmDataSet data, IEnumerable<Guid> ids)
    {
        var set = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        return data.Find<CrmUser>(x => set.Contains(x.Id)).ToDictionary(x => x.Id, x => x.DisplayName);
    }

    private static void Audit(CrmDataSet data, Guid actor, string eventType, string reason, DateTimeOffset nowUtc) =>
        data.Append(new SecurityAuditEvent(Guid.NewGuid(), nowUtc, eventType, "Success", actor, null, null,
            Guid.NewGuid().ToString("N"), reason.Length <= 1000 ? reason : reason[..1000], string.Empty, string.Empty));

    private static CommissionPlanDto Map(DealerCommissionPlan x) =>
        new(x.Id, x.Name, x.Tiers.Select(t => new CommissionTierDto(t.MinAchievementPercent, t.RatePercent)).ToList(), x.Version);

    private static string Payload(DealerCommissionStatement x, IReadOnlyDictionary<Guid, (string Code, string Name, string BranchId)> dealers,
        IReadOnlyDictionary<Guid, string> users, DateTimeOffset nowUtc) => System.Text.Json.JsonSerializer.Serialize(new
    {
        schema = "crm.commission-payout.v1",
        statementId = x.Id,
        company = x.CompanyId,
        branch = x.BranchId,
        period = ChannelPeriod.Key(x.PeriodFromUtc),
        dealerCode = dealers.GetValueOrDefault(x.DealerId).Code,
        currency = "IRR",
        total = x.CommissionAmount,
        lines = x.Lines().Select(l => new { beneficiary = l.UserId is null ? "Dealer" : "InternalUser", userId = l.UserId,
            name = l.UserId is { } id ? users.GetValueOrDefault(id, "—") : dealers.GetValueOrDefault(x.DealerId).Name, share = l.SharePercent, amount = l.Amount }),
        approvedAtUtc = nowUtc
    });

    private static CommissionStatementDto Map(DealerCommissionStatement x, IReadOnlyDictionary<Guid, (string Code, string Name, string BranchId)> dealers,
        IReadOnlyDictionary<Guid, string> users, bool canDecide, CommissionPayoutMessage? payout = null, bool canManage = false, bool canApprove = false) => new(
        x.Id, x.DealerId, dealers.GetValueOrDefault(x.DealerId).Code ?? "—", dealers.GetValueOrDefault(x.DealerId).Name ?? "—",
        x.PeriodFromUtc, x.NetSales, x.TargetAmount, x.AchievementPercent, x.RatePercent, x.CommissionAmount, x.OverdueAmount, x.Status,
        users.GetValueOrDefault(x.CalculatedByUserId, "—"), x.CalculatedAtUtc,
        x.DecidedByUserId is { } decidedBy ? users.GetValueOrDefault(decidedBy, "—") : null, x.DecidedAtUtc, x.DecisionNote,
        canDecide && !x.IsFinal, x.Version,
        x.Lines().Select(l => new CommissionSplitLineDto(l.UserId is { } id ? users.GetValueOrDefault(id, "—") : "نماینده", l.UserId, l.SharePercent, l.Amount)).ToList(),
        payout is null ? null : new CommissionPayoutDto(payout.Status, payout.AttemptCount, payout.ExternalReference, payout.LastError, payout.CompletedAtUtc),
        canManage && !x.IsFinal,
        canApprove && payout is { Status: not CommissionPayoutStatus.Sent });

    private static DealerEvaluationDto Map(DealerEvaluation x, IReadOnlyDictionary<Guid, (string Code, string Name, string BranchId)> dealers) => new(
        x.DealerId, dealers.GetValueOrDefault(x.DealerId).Code ?? "—", dealers.GetValueOrDefault(x.DealerId).Name ?? "—", x.BranchId,
        x.Rank, x.RankedDealerCount, x.AchievementScore, x.CollectionScore, x.GrowthScore, x.ServiceScore, x.TotalScore, x.Tier);

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("No active access snapshot was found.");

    private static void Require(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!Has(snapshot, companyId, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
    }

    private static bool Has(AccessSnapshot snapshot, string companyId, string permission) => snapshot.PermissionsFor(companyId).Contains(permission);

    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        Same(entity.CompanyId, organization.CompanyId) && (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
