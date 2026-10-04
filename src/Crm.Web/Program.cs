using System.Threading.RateLimiting;
using System.Net;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Commercial;
using Crm.Infrastructure.Channel;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
// Persian text is written as-is (not as &#x...; entities): smaller pages, readable source. Encoding of markup characters is unchanged.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<UnauthorizedAccessExceptionFilter>();
});
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();

var authenticationMode = builder.Configuration["Authentication:Mode"] ?? "Demo";
var useOidc = authenticationMode.Equals("Oidc", StringComparison.OrdinalIgnoreCase);
if (useOidc)
{
    var authority = builder.Configuration["Authentication:Oidc:Authority"];
    var clientId = builder.Configuration["Authentication:Oidc:ClientId"];
    var clientSecret = builder.Configuration["Authentication:Oidc:ClientSecret"];
    var ipHashSalt = builder.Configuration["Security:IpHashSalt"];
    if (string.IsNullOrWhiteSpace(authority) || authority.Contains("example.test", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(clientId) || clientId.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(clientSecret) || clientSecret.StartsWith("replace-", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(ipHashSalt) || ipHashSalt.StartsWith("demo-", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("OIDC mode requires real Authority, client credential and IP hash salt from protected configuration.");
}
else if (!builder.Environment.IsDevelopment() &&
         !builder.Configuration.GetValue("Authentication:AllowDemoOutsideDevelopment", false))
{
    // Demo mode signs anyone in with one shared password; never let it reach a real environment by accident.
    throw new InvalidOperationException(
        "Authentication:Mode=Demo is only allowed in Development. Configure OIDC, or set " +
        "Authentication:AllowDemoOutsideDevelopment=true explicitly for isolated test environments.");
}
var idleMinutes = builder.Configuration.GetValue("Authentication:Session:IdleMinutes", 30);
var absoluteHours = builder.Configuration.GetValue("Authentication:Session:AbsoluteHours", 8);
var maxSessions = builder.Configuration.GetValue("Authentication:Session:MaxConcurrent", 3);
var reverseProxyEnabled = builder.Configuration.GetValue("ReverseProxy:Enabled", false);
if (reverseProxyEnabled)
{
    var knownProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    if (knownProxies.Length == 0) throw new InvalidOperationException("Reverse proxy mode requires at least one explicit KnownProxies IP address.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = builder.Configuration.GetValue("ReverseProxy:ForwardLimit", 1);
        foreach (var value in knownProxies)
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException($"Invalid KnownProxies address: {value}");
            options.KnownProxies.Add(address);
        }
    });
}
builder.Services.AddSingleton(new IdentityRuntimeOptions(
    builder.Configuration["Authentication:DemoPassword"] ?? "Demo@1405",
    TimeSpan.FromMinutes(idleMinutes),
    TimeSpan.FromHours(absoluteHours),
    TimeSpan.FromMinutes(1),
    maxSessions));

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EnterpriseCrm");
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var requireSharedKeyRing = builder.Configuration.GetValue("DataProtection:RequireSharedKeyRing", false);
if (requireSharedKeyRing && (string.IsNullOrWhiteSpace(keyRingPath) || !Path.IsPathRooted(keyRingPath)))
    throw new InvalidOperationException("A multi-node deployment requires an absolute shared Data Protection key-ring path.");
if (!string.IsNullOrWhiteSpace(keyRingPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

var authentication = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = useOidc ? OpenIdConnectDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-Crm.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(idleMinutes);
        options.SlidingExpiration = true;
        options.EventsType = typeof(HtmxCookieAuthenticationEvents);
    });

if (useOidc)
{
    authentication.AddOpenIdConnect(options =>
    {
        builder.Configuration.GetSection("Authentication:Oidc").Bind(options);
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        if (!options.Scope.Contains("email")) options.Scope.Add("email");
        options.ClaimActions.MapUniqueJsonKey("email", "email");
        options.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");
        options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
        options.EventsType = typeof(CrmOpenIdConnectEvents);
    });
}

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var persistenceMode = builder.Configuration["Persistence:Mode"] ?? "InMemory";
var useSqlServer = persistenceMode.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
if (useSqlServer)
{
    var connectionString = builder.Configuration.GetConnectionString("CrmDatabase");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Persistence:Mode=SqlServer requires ConnectionStrings:CrmDatabase from protected configuration.");
    builder.Services.AddDbContext<CrmDbContext>(options => options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        sql.CommandTimeout(builder.Configuration.GetValue("Persistence:CommandTimeoutSeconds", 30));
    }));
    builder.Services.AddScoped<ICrmDataStore, EfCoreCrmDataStore>();
    builder.Services.AddScoped<ICrmQuerySource, EfCoreCrmQuerySource>();
    builder.Services.AddScoped<ICustomerQueryStore, EfCoreCustomerQueryStore>();
}
else
{
    builder.Services.AddSingleton<InMemoryCrmDataStore>();
    builder.Services.AddSingleton<ICrmDataStore>(services => services.GetRequiredService<InMemoryCrmDataStore>());
    builder.Services.AddSingleton<ICrmQuerySource>(services => services.GetRequiredService<InMemoryCrmDataStore>());
    builder.Services.AddSingleton<ICustomerQueryStore, InMemoryCustomerQueryStore>();
}

var cacheMode = builder.Configuration["Cache:Mode"] ?? "Memory";
if (cacheMode.Equals("Redis", StringComparison.OrdinalIgnoreCase))
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis");
    if (string.IsNullOrWhiteSpace(redisConnection))
        throw new InvalidOperationException("Cache:Mode=Redis requires ConnectionStrings:Redis from protected configuration.");
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = builder.Configuration["Cache:InstanceName"] ?? "EnterpriseCrm:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddScoped<IAccessSnapshotService, DemoAccessSnapshotService>();
builder.Services.AddSingleton(new LoginLockoutOptions(
    builder.Configuration.GetValue("Authentication:Lockout:MaxFailures", 5),
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Authentication:Lockout:WindowMinutes", 15)),
    TimeSpan.FromMinutes(builder.Configuration.GetValue("Authentication:Lockout:LockoutMinutes", 15))));
builder.Services.AddSingleton<ILoginAttemptGuard, DistributedLoginAttemptGuard>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<IOrganizationContextService, OrganizationContextService>();
builder.Services.AddScoped<IUserContextSelector>(services => services.GetRequiredService<IOrganizationContextService>());
builder.Services.AddScoped<IOrganizationAdminService, OrganizationAdminService>();
builder.Services.AddScoped<ICrmApplicationService, CrmApplicationService>();
builder.Services.AddScoped<ISalesPipelineService, SalesPipelineService>();
builder.Services.AddScoped<IQuoteApplicationService, QuoteApplicationService>();
builder.Services.AddSingleton<IProductPriceCatalog, DemoProductPriceCatalog>();
builder.Services.AddScoped<IOrderApplicationService, OrderApplicationService>();
builder.Services.AddSingleton<IAccountingCreditProvider, DemoAccountingCreditProvider>();
builder.Services.AddSingleton<IErpOrderGateway, DemoErpOrderGateway>();
builder.Services.AddScoped<IDealerApplicationService, DealerApplicationService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<ISelfServiceService, SelfServiceService>();
builder.Services.AddScoped<IServiceCaseService, ServiceCaseService>();
builder.Services.AddScoped<IRoleAdministrationService, RoleAdministrationService>();
builder.Services.AddScoped<IDealerIncentiveService, DealerIncentiveService>();
builder.Services.AddScoped<IDealerAssuranceService, DealerAssuranceService>();
builder.Services.AddScoped<CommissionPayoutDispatcher>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAccountFileService, AccountFileService>();
builder.Services.AddScoped<IAccountActivityService, AccountActivityService>();
builder.Services.AddScoped<IAccountNoteService, AccountNoteService>();
builder.Services.AddScoped<IAccountRecordService, AccountRecordService>();
// Email: Demo (log only) or Smtp; SMS: Demo or Http gateway. Secrets come from configuration/environment, never source.
if (string.Equals(builder.Configuration["Notifications:Email:Mode"], "Smtp", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<INotificationSender>(_ => new Crm.Infrastructure.Notifications.SmtpEmailSender(new(
        builder.Configuration["Notifications:Email:Smtp:Host"] ?? throw new InvalidOperationException("Notifications:Email:Smtp:Host is required."),
        builder.Configuration.GetValue("Notifications:Email:Smtp:Port", 587), builder.Configuration.GetValue("Notifications:Email:Smtp:EnableSsl", true),
        builder.Configuration["Notifications:Email:Smtp:UserName"], builder.Configuration["Notifications:Email:Smtp:Password"],
        builder.Configuration["Notifications:Email:From"] ?? throw new InvalidOperationException("Notifications:Email:From is required."),
        builder.Configuration["Notifications:Email:FromName"])));
else
    builder.Services.AddSingleton<INotificationSender>(sp => new Crm.Infrastructure.Notifications.DemoNotificationSender(
        Crm.Domain.Notifications.NotificationChannel.Email, sp.GetRequiredService<ILogger<Crm.Infrastructure.Notifications.DemoNotificationSender>>()));
if (string.Equals(builder.Configuration["Notifications:Sms:Mode"], "Http", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient("sms", client => client.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddSingleton<INotificationSender>(sp => new Crm.Infrastructure.Notifications.HttpSmsSender(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("sms"), new(
            new Uri(builder.Configuration["Notifications:Sms:Endpoint"] ?? throw new InvalidOperationException("Notifications:Sms:Endpoint is required.")),
            builder.Configuration["Notifications:Sms:ApiKey"] ?? throw new InvalidOperationException("Notifications:Sms:ApiKey is required."),
            builder.Configuration["Notifications:Sms:Sender"])));
}
else
    builder.Services.AddSingleton<INotificationSender>(sp => new Crm.Infrastructure.Notifications.DemoNotificationSender(
        Crm.Domain.Notifications.NotificationChannel.Sms, sp.GetRequiredService<ILogger<Crm.Infrastructure.Notifications.DemoNotificationSender>>()));
builder.Services.AddSingleton<IAccountingCommissionGateway, Crm.Infrastructure.Channel.DemoAccountingCommissionGateway>();
builder.Services.AddHostedService<Crm.Web.Background.ServiceEscalationWorker>();
builder.Services.AddHostedService<Crm.Web.Background.CommissionPayoutWorker>();
builder.Services.AddHostedService<Crm.Web.Background.NotificationWorker>();
builder.Services.AddSingleton<IPortalReadSource, DemoPortalReadSource>();
builder.Services.AddSingleton<IReportingFinanceSource, Crm.Infrastructure.Reporting.DemoReportingFinanceSource>();
builder.Services.AddSingleton<IDealerFinancialProjectionProvider, DemoDealerFinancialProjectionProvider>();
builder.Services.AddSingleton<IDealerPerformanceProjectionProvider, DemoDealerPerformanceProjectionProvider>();
builder.Services.AddScoped<ICustomer360Service, Customer360Service>();
builder.Services.AddScoped<IIdentityApplicationService, IdentityApplicationService>();
builder.Services.AddScoped<HtmxCookieAuthenticationEvents>();
builder.Services.AddScoped<CrmOpenIdConnectEvents>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        // Without a body the browser shows its own bare "HTTP ERROR 429" page; explain what happened and when to retry.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        var response = context.HttpContext.Response;
        response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string message = "تعداد درخواست‌ها بیش از حد مجاز است؛ لطفاً کمی بعد دوباره تلاش کنید.";
        if (context.HttpContext.Request.IsHtmx())
        {
            response.Trigger("rateLimited", message);
            return;
        }
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync($$"""
            <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>درخواست‌های بیش از حد</title><link rel="stylesheet" href="/css/site.css"></head>
            <body><main class="login-panel"><div class="login-card"><span class="eyebrow">محدودیت امنیتی</span><h2>کمی صبر کنید</h2>
            <p class="muted">{{message}}</p><p class="muted">زمان تقریبی انتظار: {{seconds}} ثانیه</p>
            <a class="button button--primary" href="/account/login">بازگشت به صفحهٔ ورود</a></div></main></body></html>
            """, cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/signin-oidc")
            ? RateLimitPartition.GetFixedWindowLimiter(
                "oidc-callback:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })
            : RateLimitPartition.GetNoLimiter("other"));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

if (useSqlServer && builder.Configuration.GetValue("Persistence:AutoMigrate", false))
    await CrmDatabaseInitializer.MigrateAndSeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

if (reverseProxyEnabled) app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<OrganizationContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

public partial class Program;
