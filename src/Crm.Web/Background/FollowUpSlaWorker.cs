using Crm.Application.Services;

namespace Crm.Web.Background;

/// <summary>
/// Runs the follow-up center monitor on the server: SLA escalation (stage owner → supervisor → branch manager),
/// reminders for late next actions and stages, unanswered referrals and assignment of waiting cases. Reminders never
/// depend on a browser being open; every message carries a dedup key, so overlapping runs or nodes never send twice.
/// </summary>
public sealed class FollowUpSlaWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<FollowUpSlaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("FollowUps:Monitor:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("FollowUps:Monitor:IntervalMinutes", 5)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = scope.ServiceProvider.GetRequiredService<IFollowUpService>().RunMonitor(DateTimeOffset.UtcNow);
                if (result.Escalations + result.Reminders + result.LateReferrals + result.AutoAssigned > 0)
                    logger.LogInformation("Follow-up monitor: {Escalations} escalations, {Reminders} reminders, {Referrals} late referrals, {Assigned} cases assigned.",
                        result.Escalations, result.Reminders, result.LateReferrals, result.AutoAssigned);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Follow-up monitor run failed; it will be retried on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
