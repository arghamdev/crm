using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>Roadmap phase 7: dealer guarantees and training, shown as a panel on the dealer page.</summary>
[Authorize(Policy = "perm:Dealer.Read")]
public sealed class DealerAssuranceController(IDealerAssuranceService assurance, ICurrentUserContext current) : Controller
{
    [HttpGet("/dealers/{id:guid}/assurance")]
    public IActionResult Panel(Guid id)
    {
        try
        {
            var model = assurance.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
            return model is null ? NotFound() : PartialView("~/Views/Dealers/_Assurance.cshtml", model);
        }
        catch (UnauthorizedAccessException) { return NoContent(); }
    }

    [Authorize(Policy = "perm:Dealer.Guarantee.Manage")]
    [HttpPost("/dealers/{id:guid}/guarantees")]
    [ValidateAntiForgeryToken]
    public IActionResult AddGuarantee(Guid id, SaveDealerGuaranteeCommand command) => Mutate(id, () =>
    {
        var guarantee = assurance.AddGuarantee(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
        return $"تضمین {guarantee.Number} به مبلغ {guarantee.Amount:N0} ریال ثبت شد.";
    });

    [Authorize(Policy = "perm:Dealer.Guarantee.Manage")]
    [HttpPost("/dealers/{id:guid}/guarantees/{guaranteeId:guid}/decide")]
    [ValidateAntiForgeryToken]
    public IActionResult DecideGuarantee(Guid id, Guid guaranteeId, DecideDealerGuaranteeCommand command) => Mutate(id, () =>
    {
        var guarantee = assurance.DecideGuarantee(current.CrmUserId, current.RequiredOrganization(), id, guaranteeId, command, DateTimeOffset.UtcNow);
        return command.Forfeit ? $"تضمین {guarantee.Number} ضبط شد." : $"تضمین {guarantee.Number} آزاد شد.";
    });

    [Authorize(Policy = "perm:Dealer.Training.Manage")]
    [HttpPost("/dealers/{id:guid}/trainings")]
    [ValidateAntiForgeryToken]
    public IActionResult AddTraining(Guid id, SaveDealerTrainingCommand command) => Mutate(id, () =>
    {
        var training = assurance.AddTraining(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
        return $"آموزش «{training.Title}» ثبت شد.";
    });

    private IActionResult Mutate(Guid id, Func<string> action)
    {
        try { TempData["AssuranceMessage"] = action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { TempData["AssuranceError"] = exception.Message; }
        return Redirect($"/dealers/{id}?panel=assurance#assurance");
    }
}
