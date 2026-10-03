using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy="perm:Portal.Review")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class PartnerInboxController(ISelfServiceService service, ICurrentUserContext current) : SelfServiceControllerBase
{
    [HttpGet("/partner-requests")]
    public IActionResult Index() => Run(() => View(service.Inbox(current.CrmUserId,current.RequiredOrganization(),DateTimeOffset.UtcNow)));
    [HttpGet("/partner-requests/{id:guid}")]
    public IActionResult Review(Guid id) => Run(() => View(service.ReviewForm(current.CrmUserId,current.RequiredOrganization(),id,DateTimeOffset.UtcNow)));
    [HttpPost("/partner-requests/{id:guid}")]
    public IActionResult Review(Guid id, ReviewPortalRequestCommand command) => Run(() => {
        service.Review(current.CrmUserId,current.RequiredOrganization(),id,command,DateTimeOffset.UtcNow); return Go("/partner-requests");
    });
}
