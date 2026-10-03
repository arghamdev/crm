using Crm.Application.Services;

namespace Crm.Web.Background;

/// <summary>Relays approved dealer commissions from the outbox to accounting.</summary>
public sealed class CommissionPayoutWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<CommissionPayoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Channel:CommissionPayout:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Max(10, configuration.GetValue("Channel:CommissionPayout:IntervalSeconds", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = scope.ServiceProvider.GetRequiredService<CommissionPayoutDispatcher>().Dispatch(DateTimeOffset.UtcNow);
                if (result.Sent + result.Failed + result.DeadLettered > 0)
                    logger.LogInformation("Commission payouts: {Sent} sent, {Failed} to retry, {Dead} dead-lettered.",
                        result.Sent, result.Failed, result.DeadLettered);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Commission payout relay failed; it will be retried on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
