using Crm.Web.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>Product version and release notes, readable by every signed-in user.</summary>
[Authorize]
public sealed class SystemController : Controller
{
    [HttpGet("/system/version")]
    public IActionResult Version() => View(SystemVersion.Releases);
}
