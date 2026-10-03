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

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
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
    builder.Services.AddScoped<ICustomerQueryStore, EfCoreCustomerQueryStore>();
}
else
{
    builder.Services.AddSingleton<ICrmDataStore, InMemoryCrmDataStore>();
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
builder.Services.AddHostedService<Crm.Web.Background.ServiceEscalationWorker>();
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
