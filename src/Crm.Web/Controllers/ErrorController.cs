using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[AllowAnonymous]
public sealed class ErrorController : Controller
{
    [Route("/error")]
    public IActionResult Index() => View("Error");
}
