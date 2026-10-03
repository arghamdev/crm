using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Reporting.Read")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReportsController(IReportingService reports, ICurrentUserContext current) : Controller
{
    [HttpGet("/reports")]
    public IActionResult Index(ReportQuery query) => Execute(() =>
    {
        var model = reports.Get(current.CrmUserId, current.RequiredOrganization(), query, DateTimeOffset.UtcNow);
        return Request.IsHtmx() ? PartialView("_Dashboard", model) : View(model);
    });

    [HttpGet("/reports/drilldown")]
    public IActionResult Drilldown(ReportQuery query) => Execute(() =>
    {
        var model = reports.Drilldown(current.CrmUserId, current.RequiredOrganization(), query, DateTimeOffset.UtcNow);
        return Request.IsHtmx() ? PartialView("_Drilldown", model) : View(model);
    });

    [Authorize(Policy = "perm:Reporting.Export")]
    [ValidateAntiForgeryToken]
    [HttpPost("/reports/export")]
    public IActionResult Export(ReportQuery query) => Execute(() =>
    {
        var result = reports.Export(current.CrmUserId, current.RequiredOrganization(), query, DateTimeOffset.UtcNow, HttpContext.TraceIdentifier);
        return File(result.Content, result.ContentType, result.FileName);
    });

    [Authorize(Policy = "perm:Reporting.BiExport")]
    [ValidateAntiForgeryToken]
    [HttpPost("/reports/bi/v1")]
    public IActionResult Bi(ReportQuery query) => Execute(() =>
    {
        var result = reports.ExportBi(current.CrmUserId, current.RequiredOrganization(), query, DateTimeOffset.UtcNow, HttpContext.TraceIdentifier);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return File(bytes, "application/json; charset=utf-8", "crm-reporting-v1.json");
    });

    private IActionResult Execute(Func<IActionResult> operation)
    {
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("قالب فیلتر یا تاریخ معتبر نیست؛ تاریخ‌ها را به صورت میلادی YYYY-MM-DD وارد کنید.");
            return operation();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            if (Request.IsHtmx()) { Response.Headers["HX-Retarget"] = "#reportErrors"; Response.Headers["HX-Reswap"] = "innerHTML"; }
            return Request.IsHtmx() ? PartialView("_Error", exception.Message) : View("Error", exception.Message);
        }
    }
}
