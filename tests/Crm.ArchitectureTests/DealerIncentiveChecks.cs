using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Channel;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class DealerIncentiveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckPlanAndScoringRules(check);
        CheckCommissionWorkflow(check);
        CheckEvaluationAndRanking(check);
    }

    private static void CheckPlanAndScoringRules(Action<bool, string> check)
    {
        var user = Guid.NewGuid();
        var plan = new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(100, 2.5m), new(0, 0), new(80, 1.5m), new(120, 3.5m)], user);
        check(plan.TierDefinition == "0:0;80:1.5;100:2.5;120:3.5" && plan.RateFor(79.99m) == 0 && plan.RateFor(80) == 1.5m &&
              plan.RateFor(119) == 2.5m && plan.RateFor(250) == 3.5m,
            "DLR7: tiers are stored ordered and the rate is the highest tier reached.");
        Reject<InvalidOperationException>(() => new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(50, 1)], user), "plan without a 0% tier", check);
        Reject<InvalidOperationException>(() => new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(0, 2), new(100, 1)], user), "decreasing rates", check);
        Reject<InvalidOperationException>(() => new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(0, 1), new(0, 2)], user), "duplicate thresholds", check);
        Reject<InvalidOperationException>(() => new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(0, 25)], user), "rate above the cap", check);

        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var calculator = Guid.NewGuid();
        var approver = Guid.NewGuid();
        DealerCommissionStatement Statement(decimal sales, decimal overdue) => new(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null,
            from, from.AddMonths(1), sales, 1_000_000m, overdue, plan, calculator, from.AddDays(31));
        var clean = Statement(1_100_000m, 0);
        check(clean.AchievementPercent == 110 && clean.RatePercent == 2.5m && clean.CommissionAmount == 27_500m &&
              clean.Status == CommissionStatementStatus.Calculated,
            "DLR7: commission = net sales x rate of the tier reached.");
        Reject<InvalidOperationException>(() => clean.Approve(calculator, null, from.AddDays(32)), "approval by the calculator (SoD)", check);
        clean.Approve(approver, null, from.AddDays(32));
        check(clean.Status == CommissionStatementStatus.Approved && clean.IsFinal, "DLR7: finance approval finalises the statement.");
        Reject<InvalidOperationException>(() => clean.Reject(approver, "x", from.AddDays(33)), "deciding a final statement", check);

        var held = Statement(900_000m, 5_000m);
        check(held.Status == CommissionStatementStatus.OnHold, "DLR7: overdue receivables put the commission on hold.");
        Reject<InvalidOperationException>(() => held.Approve(approver, " ", from.AddDays(32)), "approving an on-hold statement without a note", check);
        Reject<InvalidOperationException>(() => Statement(1, 0).Reject(approver, "", from.AddDays(32)), "rejection without a reason", check);

        check(DealerEvaluation.AchievementScoreFor(1_200, 1_000) == 100 && DealerEvaluation.AchievementScoreFor(600, 1_000) == 50 &&
              DealerEvaluation.CollectionScoreFor(null, null) == 50 && DealerEvaluation.CollectionScoreFor(1_000, 250) == 75 &&
              DealerEvaluation.GrowthScoreFor(110, 100) == 60 && DealerEvaluation.GrowthScoreFor(110, null) == 50 &&
              DealerEvaluation.ServiceScoreFor(1, 4) == 80 && DealerEvaluation.ServiceScoreFor(0, null) == 100,
            "DLR7: scorecard components follow the documented formulas.");
        var evaluation = new DealerEvaluation(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null, from, from.AddMonths(1), 150, -10, 90, 80, user, from);
        check(evaluation.AchievementScore == 100 && evaluation.CollectionScore == 0 && evaluation.TotalScore == 69.5m &&
              evaluation.Tier == DealerTier.Silver && DealerEvaluation.TierFor(85) == DealerTier.Platinum && DealerEvaluation.TierFor(70) == DealerTier.Gold,
            "DLR7: scores are clamped to 0..100, weighted 40/25/15/20 and mapped to tiers.");
    }

    private static void CheckCommissionWorkflow(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var service = new DealerIncentiveService(store, new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>()));
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var expert = User(2); var finance = User(6); var channel = User(7); var dealerUser = User(8); var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        var workspace = service.GetCommissionWorkspace(channel, org, now);
        check(workspace.Plan is { Tiers.Count: 4 } && workspace.Statements.Count == 0 && workspace.CanManage && !workspace.CanApprove,
            "DLR7: the seeded plan is visible and the channel manager can manage but not approve.");
        Reject<UnauthorizedAccessException>(() => service.GetCommissionWorkspace(expert, org, now), "commission read by a sales expert", check);
        Reject<UnauthorizedAccessException>(() => service.GetCommissionWorkspace(dealerUser, org, now), "commission read by a dealer user", check);
        Reject<UnauthorizedAccessException>(() => service.CalculateCommissions(finance, org, now, now), "calculation by finance", check);
        Reject<UnauthorizedAccessException>(() => service.CalculateCommissions(channel, org with { CompanyId = "C09" }, now, now), "foreign company", check);

        var run = service.CalculateCommissions(channel, org, now, now);
        var statements = service.GetCommissionWorkspace(finance, org, now).Statements.ToDictionary(x => x.DealerCode);
        check(run.Calculated == 3 && run.Skipped == 0 && statements.Count == 3, "DLR7: every active dealer with target and ERP sales is calculated.");
        check(statements["DLR-0002"] is { RatePercent: 3.5m, CommissionAmount: 217_000_000m, Status: CommissionStatementStatus.Calculated, CanDecide: true },
            "DLR7: 124% achievement earns the 3.5% tier on the dealer's net sales.");
        check(statements["DLR-0001"] is { RatePercent: 1.5m, Status: CommissionStatementStatus.OnHold } &&
              statements["DLR-0003"] is { RatePercent: 0m, CommissionAmount: 0m, Status: CommissionStatementStatus.OnHold },
            "DLR7: dealers with overdue receivables are held and below-80% achievement earns nothing.");

        var d02 = statements["DLR-0002"];
        Reject<UnauthorizedAccessException>(() => service.DecideCommission(channel, org, d02.Id, new(true, null, d02.Version), now), "approval by the channel manager", check);
        Reject<InvalidOperationException>(() => service.DecideCommission(finance, org, d02.Id, new(true, null, d02.Version + 1), now), "a stale version", check);
        var approved = service.DecideCommission(finance, org, d02.Id, new(true, "کنترل شد", d02.Version), now);
        check(approved.Status == CommissionStatementStatus.Approved && approved.DecidedBy is not null, "DLR7: finance approves the statement.");

        var rerun = service.CalculateCommissions(channel, org, now, now.AddMinutes(5));
        var after = service.GetCommissionWorkspace(executive, org, now);
        check(rerun.Calculated == 2 && rerun.Skipped == 1 && rerun.Issues.Any(x => x.StartsWith("DLR-0002")) &&
              after.Statements.Count(x => x.DealerCode == "DLR-0002") == 1 &&
              after.Statements.Count(x => x.Status == CommissionStatementStatus.Rejected) == 2 &&
              after.ApprovedCommission == 217_000_000m && !after.CanManage && !after.CanApprove,
            "DLR7: recalculation keeps approved statements, supersedes open ones and executives only read.");

        Reject<UnauthorizedAccessException>(() => service.SavePlan(finance, org, new("طرح", [new(0, 1)], 1), now), "plan change by finance", check);
        var plan = service.GetCommissionWorkspace(channel, org, now).Plan!;
        Reject<InvalidOperationException>(() => service.SavePlan(channel, org, new("طرح", [new(0, 1)], plan.Version + 7), now), "plan with a stale version", check);
        Reject<InvalidOperationException>(() => service.SavePlan(channel, org, new("نام تازه", [new(10, 1)], plan.Version), now), "a plan without a 0% tier", check);
        check(service.GetCommissionWorkspace(channel, org, now).Plan is { Name: var unchangedName, Version: var unchangedVersion } &&
              unchangedName == plan.Name && unchangedVersion == plan.Version, "DLR7: a rejected plan change leaves the stored plan untouched.");
        var saved = service.SavePlan(channel, org, new("طرح جدید", [new(0, 0.5m), new(90, 2m)], plan.Version), now);
        check(saved.Name == "طرح جدید" && saved.Tiers.Count == 2 && saved.Version > plan.Version &&
              store.Read(d => d.SecurityAuditEvents.Count(x => x.EventType.StartsWith("Dealer.Commission"))) >= 4,
            "DLR7: the channel manager updates the plan and every step is audited.");
    }

    private static void CheckEvaluationAndRanking(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var service = new DealerIncentiveService(store, new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>()));
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var finance = User(6); var channel = User(7); var expert = User(2);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        var empty = service.GetRanking(manager, org, now);
        check(empty.Items.Count == 0 && empty.EvaluatedAtUtc is null && !empty.CanRun, "DLR7: no ranking before the first run; managers only read.");
        Reject<UnauthorizedAccessException>(() => service.RunEvaluation(finance, org, now, now), "evaluation run by finance", check);
        Reject<UnauthorizedAccessException>(() => service.GetRanking(expert, org, now), "ranking read by a sales expert", check);

        var result = service.RunEvaluation(channel, org, now, now);
        var ranking = service.GetRanking(finance, org, now);
        check(result.Evaluated == 3 && ranking.Items.Select(x => x.Rank).SequenceEqual([1, 2, 3]) && ranking.Items.All(x => x.RankedDealerCount == 3),
            "DLR7: all three dealers are ranked 1..3.");
        check(ranking.Items[0].DealerCode == "DLR-0002" && ranking.Items[0].AchievementScore == 100 && ranking.Items[^1].DealerCode == "DLR-0003" &&
              ranking.Items.Zip(ranking.Items.Skip(1)).All(p => p.First.TotalScore >= p.Second.TotalScore),
            "DLR7: the over-achieving dealer with clean receivables ranks first and the overdue under-performer last.");

        service.RunEvaluation(channel, org, now, now.AddMinutes(1));
        var latest = service.GetRanking(manager, org, now);
        check(latest.Items.Count == 3 && latest.EvaluatedAtUtc == now.AddMinutes(1) && store.Read(d => d.DealerEvaluations.Count) == 6,
            "DLR7: a re-run keeps history but only the latest run is shown.");
        check(service.GetRanking(manager, org, now.AddMonths(-3)).Items.Count == 0, "DLR7: rankings are per period.");
    }

    private static void Reject<T>(Action action, string name, Action<bool, string> check) where T : Exception
    {
        try { action(); check(false, "DLR7: " + name + " must be rejected."); }
        catch (T) { }
    }
}
