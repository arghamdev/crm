using Crm.Application.Abstractions;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:WorkQueue.Read")]
public sealed class WorkQueueController(ICrmApplicationService crm, ICurrentUserContext current) : Controller
{
    [HttpGet("/work-queue")]
    public IActionResult Index() => View(crm.GetWorkItems(current.CrmUserId, current.RequiredOrganization()));

    [HttpGet("/work-queue/list")]
    public IActionResult List() => PartialView("_List", crm.GetWorkItems(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:WorkQueue.Complete")]
    [HttpPost("/work-queue/{id:guid}/complete")]
    public IActionResult Complete(Guid id)
    {
        try { crm.CompleteWorkItem(current.CrmUserId, current.RequiredOrganization(), id); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        Response.Trigger("workItemChanged", "اقدام انجام شد.");
        return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
    }
}
