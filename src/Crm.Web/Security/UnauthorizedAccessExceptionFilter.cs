using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Crm.Web.Security;

/// <summary>
/// Application services signal scope/permission denials with <see cref="UnauthorizedAccessException"/>.
/// Actions that do not catch it would otherwise surface a 500; this maps it to 403 everywhere.
/// </summary>
public sealed class UnauthorizedAccessExceptionFilter(ILogger<UnauthorizedAccessExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not UnauthorizedAccessException) return;
        logger.LogInformation("Access denied for {Path}: {Reason}", context.HttpContext.Request.Path, context.Exception.Message);
        context.Result = new ForbidResult();
        context.ExceptionHandled = true;
    }
}
