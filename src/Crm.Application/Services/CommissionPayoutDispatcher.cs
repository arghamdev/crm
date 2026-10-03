using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;

namespace Crm.Application.Services;

/// <summary>Sends due commission payout messages to accounting (outbox relay). Safe to run on several nodes: the idempotency key dedupes.</summary>
public sealed class CommissionPayoutDispatcher(ICrmDataStore store, IAccountingCommissionGateway gateway)
{
    public CommissionPayoutDispatchResult Dispatch(DateTimeOffset nowUtc, int batchSize = 50) => store.Write(data =>
    {
        int sent = 0, failed = 0, dead = 0;
        var due = data.Find<CommissionPayoutMessage>(x => x.Status == CommissionPayoutStatus.Pending && x.NextAttemptAtUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc).Take(batchSize).ToList();
        foreach (var message in due)
        {
            CommissionPayoutResult result;
            try { result = gateway.Post(message.IdempotencyKey, message.Payload); }
            catch (Exception exception) when (exception is not OperationCanceledException) { result = new(false, null, exception.Message); }
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.ExternalReference))
            {
                message.MarkSent(result.ExternalReference, nowUtc);
                sent++;
            }
            else
            {
                message.MarkFailed(result.Error ?? "پاسخ نامعتبر از حسابداری", nowUtc);
                if (message.Status == CommissionPayoutStatus.DeadLetter) dead++; else failed++;
            }
        }
        return new CommissionPayoutDispatchResult(sent, failed, dead);
    });
}
