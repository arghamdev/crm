using Crm.Domain.Channel;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>Roadmap phase 7: dealer commission statements (calculate → finance approval) and periodic dealer ranking.</summary>
public sealed class DealerIncentivesController(IDealerIncentiveService incentives, ICurrentUserContext current) : Controller
{
    [Authorize(Policy = "perm:Dealer.Commission.Read")]
    [HttpGet("/dealers/commissions")]
    public IActionResult Commissions(string? period = null)
    {
        var from = Period(period);
        ViewBag.Period = from;
        return View(incentives.GetCommissionWorkspace(current.CrmUserId, current.RequiredOrganization(), from));
    }

    [Authorize(Policy = "perm:Dealer.Commission.Manage")]
    [HttpPost("/dealers/commissions/plan")]
    [ValidateAntiForgeryToken]
    public IActionResult SavePlan([FromForm] string? period, [FromForm] string? name, [FromForm] List<decimal?>? thresholds,
        [FromForm] List<decimal?>? rates, [FromForm] long expectedVersion)
    {
        var tiers = new List<CommissionTierDto>();
        for (var i = 0; i < Math.Max(thresholds?.Count ?? 0, rates?.Count ?? 0); i++)
        {
            var threshold = thresholds?.ElementAtOrDefault(i);
            var rate = rates?.ElementAtOrDefault(i);
            if (threshold is null && rate is null) continue;
            tiers.Add(new CommissionTierDto(threshold ?? -1, rate ?? -1));
        }
        return Mutate(period, () =>
        {
            var plan = incentives.SavePlan(current.CrmUserId, current.RequiredOrganization(),
                new SaveCommissionPlanCommand(name, tiers, expectedVersion), DateTimeOffset.UtcNow);
            return $"طرح «{plan.Name}» با {plan.Tiers.Count} پله ذخیره شد؛ محاسبه‌های بعدی با این طرح انجام می‌شود.";
        });
    }

    [Authorize(Policy = "perm:Dealer.Commission.Manage")]
    [HttpPost("/dealers/commissions/calculate")]
    [ValidateAntiForgeryToken]
    public IActionResult Calculate([FromForm] string? period) => Mutate(period, () =>
    {
        var result = incentives.CalculateCommissions(current.CrmUserId, current.RequiredOrganization(), Period(period), DateTimeOffset.UtcNow);
        var message = $"{result.Calculated} صورت کمیسیون محاسبه شد" + (result.Skipped > 0 ? $" و {result.Skipped} نماینده کنار گذاشته شد" : "") + ".";
        if (result.Issues.Count > 0) TempData["IncentiveIssues"] = string.Join("\n", result.Issues);
        return message;
    });

    [Authorize(Policy = "perm:Dealer.Commission.Approve")]
    [HttpPost("/dealers/commissions/{id:guid}/decide")]
    [ValidateAntiForgeryToken]
    public IActionResult Decide(Guid id, [FromForm] string? period, [FromForm] bool approve, [FromForm] string? note, [FromForm] long expectedVersion) =>
        Mutate(period, () =>
        {
            var statement = incentives.DecideCommission(current.CrmUserId, current.RequiredOrganization(), id,
                new DecideCommissionCommand(approve, note, expectedVersion), DateTimeOffset.UtcNow);
            return approve
                ? $"کمیسیون {statement.DealerName} به مبلغ {statement.CommissionAmount:N0} ریال تأیید شد."
                : $"صورت کمیسیون {statement.DealerName} رد شد.";
        });

    [Authorize(Policy = "perm:Dealer.Evaluation.Read")]
    [HttpGet("/dealers/ranking")]
    public IActionResult Ranking(string? period = null)
    {
        var from = Period(period);
        ViewBag.Period = from;
        return View(incentives.GetRanking(current.CrmUserId, current.RequiredOrganization(), from));
    }

    [Authorize(Policy = "perm:Dealer.Evaluation.Run")]
    [HttpPost("/dealers/ranking/run")]
    [ValidateAntiForgeryToken]
    public IActionResult RunEvaluation([FromForm] string? period)
    {
        try
        {
            var result = incentives.RunEvaluation(current.CrmUserId, current.RequiredOrganization(), Period(period), DateTimeOffset.UtcNow);
            TempData["IncentiveMessage"] = $"ارزیابی {result.Evaluated} نماینده انجام و رتبه‌بندی به‌روز شد.";
            if (result.Notes.Count > 0) TempData["IncentiveIssues"] = string.Join("\n", result.Notes);
        }
        catch (InvalidOperationException exception) { TempData["IncentiveError"] = exception.Message; }
        return Redirect($"/dealers/ranking?period={Key(Period(period))}");
    }

    /// <summary>Post/redirect/get: the outcome (or the business-rule refusal) is shown on the reloaded workspace.</summary>
    private IActionResult Mutate(string? period, Func<string> action)
    {
        try { TempData["IncentiveMessage"] = action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            TempData["IncentiveError"] = exception.Message;
        }
        return Redirect($"/dealers/commissions?period={Key(Period(period))}");
    }

    private static string Key(DateTimeOffset period) => ChannelPeriod.Key(period);

    /// <summary>Jalali "1405-07"; anything else falls back to the current month.</summary>
    private static DateTimeOffset Period(string? value) => ChannelPeriod.Parse(value) ?? ChannelPeriod.StartOf(DateTimeOffset.UtcNow);
}
