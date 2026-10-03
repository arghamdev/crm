using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy="perm:Portal.Read")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class PortalController(ISelfServiceService service, ICurrentUserContext current) : SelfServiceControllerBase
{
    [HttpGet("/portal")]
    public IActionResult Index() => Run(() => View(service.Portal(current.CrmUserId, current.RequiredOrganization(), DateTimeOffset.UtcNow)));
    [HttpPost("/portal/requests"), Authorize(Policy="perm:Portal.Submit")]
    public IActionResult Create(SubmitPortalRequestCommand command) => Run(() => {
        service.Submit(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow); return Go("/portal#requests");
    });
    [HttpPost("/portal/requests/{id:guid}/cancel"), Authorize(Policy="perm:Portal.Submit")]
    public IActionResult Cancel(Guid id, long expectedVersion) => Run(() => {
        service.Cancel(current.CrmUserId, current.RequiredOrganization(), id, expectedVersion, DateTimeOffset.UtcNow); return Go("/portal#requests");
    });
    [HttpPost("/portal/invoices/{id}/export")]
    public IActionResult Invoice(string id) => Run(() => { var file=service.Invoice(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow); return File(file.Content,file.ContentType,file.FileName); });
}
