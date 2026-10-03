using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy="perm:Mobile.Visit.Read")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class MobileController(ISelfServiceService service, ICurrentUserContext current) : SelfServiceControllerBase
{
    [HttpGet("/mobile")]
    public IActionResult Index(DateOnly? day) => Run(() => View(service.Mobile(current.CrmUserId,current.RequiredOrganization(),day,DateTimeOffset.UtcNow)));
    [HttpPost("/mobile/visits"), Authorize(Policy="perm:Mobile.Visit.Write")]
    public IActionResult Plan(CreateVisitCommand command) => Run(() => {
        var ack=service.PlanVisit(current.CrmUserId,current.RequiredOrganization(),command,DateTimeOffset.UtcNow);
        return WantsJson ? Json(ack) : Go("/mobile?day="+ack.Visit.PlannedAtUtc.ToString("yyyy-MM-dd"));
    });
    [HttpPost("/mobile/visits/{id:guid}"), Authorize(Policy="perm:Mobile.Visit.Write")]
    public IActionResult Change(Guid id, VisitActionCommand command) => Run(() => {
        var ack=service.VisitAction(current.CrmUserId,current.RequiredOrganization(),id,command,DateTimeOffset.UtcNow);
        return WantsJson ? Json(ack) : Go("/mobile?day="+ack.Visit.PlannedAtUtc.ToString("yyyy-MM-dd"));
    });
}
