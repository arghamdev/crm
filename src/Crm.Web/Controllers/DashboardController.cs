using Crm.Application.Abstractions;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Dashboard.Read")]
public sealed class DashboardController(ICrmApplicationService crm, ICurrentUserContext current) : Controller
{
    public IActionResult Index() => View(crm.GetDashboard(current.CrmUserId, current.RequiredOrganization()));
}
