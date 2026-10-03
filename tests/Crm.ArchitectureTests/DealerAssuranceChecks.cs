using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Channel;
using Crm.Infrastructure.Channel;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class DealerAssuranceChecks
{
    private sealed class FailingGateway : IAccountingCommissionGateway
    {
        public int Calls;
        public CommissionPayoutResult Post(string idempotencyKey, string payload) { Calls++; return new(false, null, "سرویس حسابداری در دسترس نیست"); }
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckDomainRules(check);
        CheckGuaranteesAndTraining(check);
        CheckSplitAndPayout(check);
    }

    private static void CheckDomainRules(Action<bool, string> check)
    {
        var today = new DateOnly(2026, 10, 3);
        var user = Guid.NewGuid();
        DealerGuarantee Guarantee(DealerGuaranteeType type, string? issuer, DateOnly? expires, decimal amount = 1000) =>
            new(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null, type, "G-1", issuer, amount, today.AddMonths(-1), expires, null, user);
        Reject<InvalidOperationException>(() => Guarantee(DealerGuaranteeType.BankGuarantee, null, today.AddMonths(6)), "bank guarantee without issuer", check);
        Reject<InvalidOperationException>(() => Guarantee(DealerGuaranteeType.BankGuarantee, "بانک", null), "bank guarantee without expiry", check);
        Reject<InvalidOperationException>(() => Guarantee(DealerGuaranteeType.PromissoryNote, null, null, 0), "zero amount", check);
        Reject<InvalidOperationException>(() => Guarantee(DealerGuaranteeType.Cheque, "بانک", today.AddMonths(-2)), "expiry before issue", check);
        var guarantee = Guarantee(DealerGuaranteeType.BankGuarantee, "بانک ملت", today.AddDays(10));
        check(guarantee.IsEffective(today) && guarantee.ExpiresWithin(today, 30) && !guarantee.IsEffective(today.AddDays(11)),
            "DLR7G: a guarantee is effective until expiry and flagged within 30 days of it.");
        Reject<InvalidOperationException>(() => guarantee.Release(user, " ", DateTimeOffset.UtcNow), "release without reason", check);
        guarantee.Forfeit(user, "عدم پرداخت مطالبات", DateTimeOffset.UtcNow);
        check(guarantee.Status == DealerGuaranteeStatus.Forfeited && !guarantee.IsEffective(today), "DLR7G: a forfeited guarantee no longer counts.");
        Reject<InvalidOperationException>(() => guarantee.Release(user, "x", DateTimeOffset.UtcNow), "deciding a guarantee twice", check);
        Reject<InvalidOperationException>(() => new DealerTraining(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null, "دوره", DealerTrainingTopic.Sales,
            today.AddDays(1), 4, 3, null, null, user, today), "training in the future", check);
        Reject<InvalidOperationException>(() => new DealerTraining(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null, "دوره", DealerTrainingTopic.Sales,
            today, 4, 3, 120, null, user, today), "score above 100", check);

        var plan = new DealerCommissionPlan(Guid.NewGuid(), "C01", "طرح", [new(0, 2)], user);
        var from = new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
        var statement = new DealerCommissionStatement(Guid.NewGuid(), Guid.NewGuid(), "C01", "B01", null, from, from.AddDays(30), 1_000_001m, 1_000_000m, 0,
            plan, user, from.AddDays(31));
        var coSeller = Guid.NewGuid();
        Reject<InvalidOperationException>(() => statement.SetSplit(coSeller, 60), "an internal share above 50%", check);
        statement.SetSplit(coSeller, 25);
        var lines = statement.Lines();
        check(statement.CommissionAmount == 20_000 && lines.Count == 2 && lines[0] is { UserId: null, SharePercent: 75, Amount: 15_000 } &&
              lines[1].UserId == coSeller && lines.Sum(x => x.Amount) == statement.CommissionAmount,
            "DLR7G: split credit gives the co-seller its share and the dealer the rest, adding up to the commission.");
        statement.SetSplit(null, 0);
        check(statement.Lines().Single().SharePercent == 100, "DLR7G: clearing the split returns 100% to the dealer.");
        Reject<InvalidOperationException>(() => new CommissionPayoutMessage(Guid.NewGuid(), statement, "{}", DateTimeOffset.UtcNow), "a payout for an unapproved statement", check);
        statement.Approve(Guid.NewGuid(), null, from.AddDays(32));
        Reject<InvalidOperationException>(() => statement.SetSplit(coSeller, 10), "splitting an approved statement", check);

        var now = DateTimeOffset.UtcNow;
        var message = new CommissionPayoutMessage(Guid.NewGuid(), statement, "{\"lines\":[]}", now);
        for (var i = 0; i < CommissionPayoutMessage.MaxAttempts - 1; i++) message.MarkFailed("x", now);
        check(message.Status == CommissionPayoutStatus.Pending && message.NextAttemptAtUtc == now.AddMinutes(240) && !message.IsDue(now),
            "DLR7G: failed sends back off before retrying.");
        message.MarkFailed("x", now);
        check(message.Status == CommissionPayoutStatus.DeadLetter && message.IdempotencyKey == CommissionPayoutMessage.KeyFor(statement.Id),
            "DLR7G: after the last attempt the payout is dead-lettered.");
        message.Retry(now);
        message.MarkSent("ACC-1", now);
        message.MarkSent("ACC-2", now);
        check(message is { Status: CommissionPayoutStatus.Sent, ExternalReference: "ACC-1" }, "DLR7G: a manual retry can still succeed and a duplicate ack is ignored.");
        Reject<InvalidOperationException>(() => message.Retry(now), "retrying a sent payout", check);
    }

    private static void CheckGuaranteesAndTraining(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var service = new DealerAssuranceService(store, new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>()));
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var expert = User(2); var finance = User(6); var channel = User(7); var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;
        var pilot = store.Read(d => d.Dealers.Single(x => x.DealerId == "P-D01").Id);

        var view = service.Get(channel, org, pilot, now)!;
        check(view.Guarantees.Count == 2 && view.EffectiveGuaranteeAmount == 8_000_000_000m && view.CreditLimit == 12_000_000_000m &&
              view.CoveragePercent == 66.7m && view.ExpiringSoonCount == 1 && view.Trainings.Single().Hours == 8 && view.CanManageGuarantees && view.CanManageTrainings,
            "DLR7G: the pilot's coverage is 8bn of a 12bn credit limit with one guarantee near expiry.");
        var financeView = service.Get(finance, org, pilot, now)!;
        var executiveView = service.Get(executive, org, pilot, now)!;
        check(financeView.CanManageGuarantees && !financeView.CanManageTrainings && !executiveView.CanManageGuarantees && executiveView.Guarantees.Count == 2,
            "DLR7G: finance manages guarantees, the executive only reads.");
        Reject<UnauthorizedAccessException>(() => service.Get(expert, org, pilot, now), "a sales expert reading guarantees", check);
        Reject<UnauthorizedAccessException>(() => service.AddTraining(finance, org, pilot,
            new SaveDealerTrainingCommand("دوره", DealerTrainingTopic.Sales, "1405/06/01", 2, 2, null, null), now), "finance recording training", check);

        var added = service.AddGuarantee(finance, org, pilot, new SaveDealerGuaranteeCommand(DealerGuaranteeType.BankGuarantee, "BG-NEW-1", "بانک ملی",
            4_000_000_000m, "۱۴۰۵/۰۶/۰۱", "1406/06/01", "تمدید"), now);
        check(added is { IssuedOn: "1405/06/01", ExpiresOn: "1406/06/01", IsEffective: true } &&
              service.Get(channel, org, pilot, now)!.CoveragePercent == 100m, "DLR7G: a new guarantee with Jalali dates raises coverage to 100%.");
        Reject<InvalidOperationException>(() => service.AddGuarantee(finance, org, pilot, new SaveDealerGuaranteeCommand(DealerGuaranteeType.BankGuarantee,
            "BG-NEW-1", "بانک ملی", 1, "1405/06/01", "1406/06/01", null), now), "a duplicate active guarantee number", check);
        Reject<InvalidOperationException>(() => service.AddGuarantee(finance, org, pilot, new SaveDealerGuaranteeCommand(DealerGuaranteeType.Cheque,
            "CH-1", "بانک", 1, "2026-01-01", null, null), now), "a Gregorian date in the Jalali field", check);
        var released = service.DecideGuarantee(channel, org, pilot, added.Id, new DecideDealerGuaranteeCommand(false, "جایگزین شد", added.Version), now);
        check(released.Status == DealerGuaranteeStatus.Released && store.Read(d => d.SecurityAuditEvents.Count(x => x.EventType.StartsWith("Dealer.Guarantee"))) == 2,
            "DLR7G: releasing a guarantee is recorded and audited.");
        var training = service.AddTraining(channel, org, pilot, new SaveDealerTrainingCommand("خدمات پس از فروش", DealerTrainingTopic.AfterSales,
            "1405/06/15", 6, 5, 78, "1406/06/15"), now);
        check(training.HeldOn == "1405/06/15" && service.Get(channel, org, pilot, now)!.TrainingHoursLast12Months == 14,
            "DLR7G: training hours of the last 12 months add up.");
    }

    private static void CheckSplitAndPayout(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var incentives = new DealerIncentiveService(store, new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>()));
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var expert = User(2); var finance = User(6); var channel = User(7); var dealerUser = User(8);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        incentives.CalculateCommissions(channel, org, now, now);
        var workspace = incentives.GetCommissionWorkspace(channel, org, now);
        var d02 = workspace.Statements.Single(x => x.DealerCode == "DLR-0002");
        check(workspace.InternalUsers is { } people && people.Any(x => x.Id == expert) && people.All(x => x.Id != dealerUser) && d02.CanSplit,
            "DLR7G: the split offers internal users only.");
        Reject<InvalidOperationException>(() => incentives.SetSplit(channel, org, d02.Id, new SetCommissionSplitCommand(dealerUser, 10, d02.Version), now),
            "a dealer user as co-seller", check);
        Reject<UnauthorizedAccessException>(() => incentives.SetSplit(finance, org, d02.Id, new SetCommissionSplitCommand(expert, 10, d02.Version), now),
            "finance changing the split", check);
        var split = incentives.SetSplit(channel, org, d02.Id, new SetCommissionSplitCommand(expert, 20, d02.Version), now);
        check(split.Lines is [{ Amount: 173_600_000m, SharePercent: 80 }, { Amount: 43_400_000m, UserId: var coSeller }] && coSeller == expert,
            "DLR7G: 20% of 217,000,000 goes to the sales expert.");

        var approved = incentives.DecideCommission(finance, org, d02.Id, new DecideCommissionCommand(true, "کنترل شد", split.Version), now);
        var message = store.Read(d => d.CommissionPayoutMessages.Single());
        check(approved.Payout is { Status: CommissionPayoutStatus.Pending } && message.Payload.Contains("\"InternalUser\"") &&
              message.Payload.Contains("43400000") && message.Payload.Contains(ChannelPeriod.Key(now)),
            "DLR7G: approval queues the payout with both commission lines in the same write.");

        var failing = new FailingGateway();
        var failed = new CommissionPayoutDispatcher(store, failing).Dispatch(now);
        var again = new CommissionPayoutDispatcher(store, failing).Dispatch(now);
        check(failed.Failed == 1 && again.Failed == 0 && failing.Calls == 1 && store.Read(d => d.CommissionPayoutMessages.Single().AttemptCount) == 1,
            "DLR7G: a failed send waits for its backoff instead of retrying immediately.");
        var gateway = new DemoAccountingCommissionGateway();
        var sent = new CommissionPayoutDispatcher(store, gateway).Dispatch(now.AddMinutes(2));
        var view = incentives.GetCommissionWorkspace(finance, org, now).Statements.Single(x => x.Id == d02.Id);
        check(sent.Sent == 1 && view.Payout is { Status: CommissionPayoutStatus.Sent, ExternalReference: var reference } && reference!.StartsWith("ACC-CM-") &&
              gateway.Post(message.IdempotencyKey, message.Payload).ExternalReference == reference && !view.CanRetryPayout,
            "DLR7G: the outbox relay delivers the payout once; the same key returns the same voucher.");
        Reject<InvalidOperationException>(() => incentives.RetryPayout(finance, org, d02.Id, now), "retrying a delivered payout", check);
        Reject<UnauthorizedAccessException>(() => incentives.RetryPayout(channel, org, d02.Id, now), "retry without approval permission", check);
    }

    private static void Reject<T>(Action action, string name, Action<bool, string> check) where T : Exception
    {
        try { action(); check(false, "DLR7G: " + name + " must be rejected."); }
        catch (T) { }
    }
}
