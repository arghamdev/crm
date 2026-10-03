using Crm.Application.Services;

namespace Crm.Web.Background;

/// <summary>Delivers queued email/SMS notifications and, once an hour, raises guarantee-expiry reminders.</summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Max(10, configuration.GetValue("Notifications:IntervalSeconds", 30)));
        var nextScan = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                var now = DateTimeOffset.UtcNow;
                if (now >= nextScan)
                {
                    var raised = dispatcher.ScanGuaranteeExpiry(now);
                    if (raised > 0) logger.LogInformation("Raised {Count} guarantee-expiry notifications.", raised);
                    nextScan = now.AddHours(1);
                }
                var result = dispatcher.Dispatch(now);
                if (result.Sent + result.Failed + result.DeadLettered > 0)
                    logger.LogInformation("Notifications: {Sent} sent, {Failed} to retry, {Dead} dead-lettered.", result.Sent, result.Failed, result.DeadLettered);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Notification relay failed; it will be retried on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
