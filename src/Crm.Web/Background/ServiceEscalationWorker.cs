using Crm.Application.Services;

namespace Crm.Web.Background;

/// <summary>
/// Periodically escalates service cases whose SLA is breached. Escalation is idempotent per level, so
/// running on several nodes at once only produces duplicate work if two nodes race the same write;
/// the EF store's optimistic concurrency token rejects the losing write.
/// </summary>
public sealed class ServiceEscalationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ServiceEscalationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Service:Escalation:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Service:Escalation:IntervalMinutes", 5)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = scope.ServiceProvider.GetRequiredService<IServiceCaseService>().EscalateBreaches(DateTimeOffset.UtcNow);
                if (result.EscalatedCases > 0)
                    logger.LogInformation("Escalated {Cases} service cases and created {WorkItems} work items.",
                        result.EscalatedCases, result.WorkItemsCreated);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Service case escalation run failed; it will be retried on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
