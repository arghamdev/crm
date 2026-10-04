using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Crm.Application.Abstractions;
using Crm.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static readonly List<string> Failures = [];
    private static readonly string ArtifactsPath = Environment.GetEnvironmentVariable("CRM_WEB_TEST_ARTIFACTS")
        ?? Path.Combine(Path.GetTempPath(), "enterprise-crm-web-tests");

    public static async Task<int> Main()
    {
        var keyRingPath = Path.Combine(Path.GetTempPath(), "enterprise-crm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyRingPath);
        var sharedStore = new InMemoryCrmDataStore();

        using var firstNode = new DemoWebFactory(keyRingPath, sharedStore);
        using var manager = CreateClient(firstNode);

        await CheckAnonymousHtmxRedirect(manager);
        await CheckSecurityHeaders(manager);
        var managerCookie = await Login(manager, "sales.manager", "Demo@1405");
        await CheckManagerAccess(manager);
        await CheckVersion(firstNode, manager);
        await CheckBrandFont(manager);
        await CheckCustomer360(manager);
        await CheckCustomerForm(manager);
        await CheckSalesPipeline(manager);
        await CheckQuoteGovernance(manager);
        await CheckOrderVisibility(manager);
        await CheckAntiforgery(manager);
        await CheckOrganizationAdministration(manager);
        await CheckOrganizationSwitchAndIsolation(manager);

        using var expert = CreateClient(firstNode);
        await Login(expert, "sales.expert", "Demo@1405");
        await CheckExpertScopeAndPermission(expert);

        using var channelManager = CreateClient(firstNode);
        await Login(channelManager, "channel.manager", "Demo@1405");
        await CheckDealerGovernance(channelManager);

        using var dealerUser = CreateClient(firstNode);
        await Login(dealerUser, "dealer.user", "Demo@1405");
        await CheckDealerScope(dealerUser);

        using var financeUser = CreateClient(firstNode);
        await Login(financeUser, "finance.manager", "Demo@1405");
        await CheckReports(manager, expert, dealerUser, financeUser);
        await CheckSelfService(manager, expert, dealerUser);

        using var serviceManager = CreateClient(firstNode);
        await Login(serviceManager, "sales.manager", "Demo@1405");
        await CheckServiceDesk(serviceManager, expert, dealerUser, financeUser);
        await CheckRoleAdministration(serviceManager, expert);
        await CheckDealerIncentives(channelManager, financeUser, expert, dealerUser);
        await CheckDealerAssurance(channelManager, financeUser, expert);
        await CheckNotificationSettings(expert);
        await CheckAccountFile(serviceManager, expert, financeUser, dealerUser);
        await CheckListWorkspaces(serviceManager, expert, financeUser);

        using var secondNode = new DemoWebFactory(keyRingPath, sharedStore);
        await CheckSharedKeyRing(secondNode, managerCookie);
        await CheckLogoutRevokesLocalSession(firstNode);
        await CheckLoginRateLimit(firstNode);

        using var oidcNode = new OidcChallengeWebFactory();
        await CheckOidcChallengeUsesCodeAndPkce(oidcNode);

        if (Failures.Count > 0)
        {
            Console.Error.WriteLine("Priority-9 cumulative CRM and web/security checks failed:");
            foreach (var failure in Failures) Console.Error.WriteLine($"- {failure}");
            return 1;
        }

        Console.WriteLine("All priority-10 portal/mobile, reporting, dealer, order, quote, pipeline and web/security checks passed.");
        return 0;
    }

    private static HttpClient CreateClient(WebApplicationFactory<global::Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task CheckAnonymousHtmxRedirect(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("HX-Request", "true");
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK, "Anonymous HTMX request must return 200 with HX-Redirect.");
        Check(response.Headers.TryGetValues("HX-Redirect", out var values) && values.Single() == "/account/session-expired",
            "Expired HTMX session must redirect the browser instead of rendering Login inside a partial.");
    }

    private static async Task CheckSecurityHeaders(HttpClient client)
    {
        using var response = await client.GetAsync("/account/login");
        Check(response.StatusCode == HttpStatusCode.OK, "Login page must be reachable anonymously.");
        Check(HeaderContains(response, "Content-Security-Policy", "frame-ancestors 'none'"), "CSP frame protection is missing.");
        Check(HeaderContains(response, "X-Content-Type-Options", "nosniff"), "X-Content-Type-Options is missing.");
        Check(HeaderContains(response, "Cache-Control", "no-store"), "Identity pages must not be cached.");
    }

    private static async Task<string> Login(HttpClient client, string userName, string password)
    {
        var token = await GetAntiforgeryToken(client, "/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["userName"] = userName,
            ["password"] = password,
            ["returnUrl"] = "/"
        });
        using var response = await client.PostAsync("/account/login", content);
        Check(response.StatusCode == HttpStatusCode.Redirect, $"Demo login for {userName} must redirect after success.");
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var setCookies)
            ? setCookies.FirstOrDefault(x => x.StartsWith("__Host-Crm.Auth=", StringComparison.Ordinal))?.Split(';')[0]
            : null;
        Check(!string.IsNullOrWhiteSpace(cookie), "Successful login must issue the __Host-Crm.Auth cookie.");
        var redirectLocation = response.Headers.Location?.OriginalString;
        if (redirectLocation?.StartsWith("/context/select", StringComparison.OrdinalIgnoreCase) == true)
        {
            var tokenForContext = await GetAntiforgeryToken(client, redirectLocation);
            using var contextContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenForContext,
                ["CompanyId"] = "C01",
                ["BranchId"] = string.Empty,
                ["TerritoryId"] = string.Empty,
                ["ReturnUrl"] = "/"
            });
            using var contextResponse = await client.PostAsync("/context/select", contextContent);
            Check(contextResponse.StatusCode == HttpStatusCode.Redirect && contextResponse.Headers.Location?.OriginalString == "/",
                "A multi-company login must complete server-side context selection before entering the application.");
        }
        return cookie ?? string.Empty;
    }

    private static async Task CheckManagerAccess(HttpClient client)
    {
        using var identityResponse = await client.GetAsync("/identity/users");
        Check(identityResponse.StatusCode == HttpStatusCode.OK, "Sales manager must access identity administration.");

        using var customersResponse = await client.GetAsync("/customers");
        var html = await customersResponse.Content.ReadAsStringAsync();
        Check(customersResponse.StatusCode == HttpStatusCode.OK && HtmlContains(html, "از <b>4</b> حساب"),
            "Company-scoped manager must see all four seeded customers.");
        Check(html.Contains("/lib/htmx/htmx.min.js", StringComparison.Ordinal),
            "Authenticated pages must load the local HTMX asset without a CDN dependency.");
        using var page = await client.GetAsync("/customers/table?q=سپهر&page=1&pageSize=10");
        var pageHtml = await page.Content.ReadAsStringAsync();
        CheckHtml(page.StatusCode == HttpStatusCode.OK && HtmlContains(pageHtml, "صنایع غذایی سپهر") &&
              HtmlContains(pageHtml, "از <b>1</b> حساب"),
            "Customer table must apply server-side search and return pagination metadata.", "customer-search", pageHtml);
    }

    private static async Task CheckSalesPipeline(HttpClient client)
    {
        using (var leads = await client.GetAsync("/leads"))
        {
            var html = await leads.Content.ReadAsStringAsync();
            Check(leads.StatusCode == HttpStatusCode.OK && HtmlContains(html, "مدیریت سرنخ‌های فروش") && HtmlContains(html, "نیازمند اقدام") &&
                  HtmlContains(html, "مشکوک به تکرار") && html.Contains("data-bulk-item", StringComparison.Ordinal),
                "Lead workspace must render the view tabs, counters and selectable rows.");
        }

        const string qualifiedLeadId = "30000000-0000-4000-8000-000000000001";
        using (var details = await client.GetAsync($"/leads/{qualifiedLeadId}"))
        {
            var html = await details.Content.ReadAsStringAsync();
            Check(details.StatusCode == HttpStatusCode.OK && html.Contains("تاریخچه وضعیت", StringComparison.Ordinal) &&
                  html.Contains("تبدیل", StringComparison.Ordinal),
                "Lead details must expose audit history and eligible actions.");
        }
        using (var transition = await client.GetAsync($"/leads/{qualifiedLeadId}/transition"))
            Check(transition.StatusCode == HttpStatusCode.OK &&
                  (await transition.Content.ReadAsStringAsync()).Contains("ExpectedVersion", StringComparison.Ordinal),
                "Lead transition drawer must render a concurrency token and POST form.");
        using (var conversion = await client.GetAsync($"/leads/{qualifiedLeadId}/convert"))
            Check(conversion.StatusCode == HttpStatusCode.OK &&
                  (await conversion.Content.ReadAsStringAsync()).Contains("ExpectedCloseAtUtc", StringComparison.Ordinal),
                "Qualified lead conversion drawer must render opportunity inputs.");

        var token = await GetAntiforgeryToken(client, "/leads/create");
        using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Name"] = "سرنخ تست وب اولویت پنج",
            ["Contact"] = "کارشناس خرید",
            ["Phone"] = "09125550005",
            ["Email"] = "WEB-P5@TEST.LOCAL",
            ["Source"] = "وب‌سایت",
            ["Owner"] = "مهدی نادری",
            ["OwnerUserId"] = "10000000-0000-4000-8000-000000000001",
            ["BranchId"] = "B01",
            ["TerritoryId"] = "T01"
        }))
        using (var created = await client.PostAsync("/leads/create", content))
            Check(created.StatusCode == HttpStatusCode.Redirect && created.Headers.Location?.OriginalString == "/leads",
                "Lead create must retain a functional non-HTMX redirect fallback.");

        using (var pipeline = await client.GetAsync("/opportunities"))
        {
            var html = await pipeline.Content.ReadAsStringAsync();
            Check(pipeline.StatusCode == HttpStatusCode.OK && html.Contains("Sales Pipeline", StringComparison.Ordinal) &&
                  html.Contains("ارزش وزنی", StringComparison.Ordinal),
                "Opportunity workspace must render weighted pipeline metrics.");
        }
        const string opportunityId = "40000000-0000-4000-8000-000000000001";
        using (var details = await client.GetAsync($"/opportunities/{opportunityId}"))
        {
            var html = await details.Content.ReadAsStringAsync();
            Check(details.StatusCode == HttpStatusCode.OK && html.Contains("تاریخچه مرحله", StringComparison.Ordinal) &&
                  html.Contains("فعالیت‌ها", StringComparison.Ordinal),
                "Opportunity details must render stage history and activities.");
        }
        foreach (var formPath in new[]
        {
            $"/opportunities/{opportunityId}/edit",
            $"/opportunities/{opportunityId}/assign",
            $"/opportunities/{opportunityId}/move",
            $"/opportunities/{opportunityId}/activities/create"
        })
        {
            using var form = await client.GetAsync(formPath);
            var html = await form.Content.ReadAsStringAsync();
            Check(form.StatusCode == HttpStatusCode.OK && html.Contains("__RequestVerificationToken", StringComparison.Ordinal) &&
                  html.Contains("ExpectedVersion", StringComparison.Ordinal),
                $"Sales-pipeline internal form must be executable: {formPath}");
        }
    }

    private static async Task CheckCustomer360(HttpClient client)
    {
        const string customerId = "20000000-0000-4000-8000-000000000001";
        using (var details = await client.GetAsync($"/customers/{customerId}"))
        {
            var html = await details.Content.ReadAsStringAsync();
            CheckHtml(details.StatusCode == HttpStatusCode.OK &&
                  HtmlContains(html, "پرونده حساب") &&
                  HtmlContains(html, "افراد رابط") &&
                  HtmlContains(html, "رکوردهای مرتبط"),
                "Customer details must render the account file (پرونده حساب).", "customer-360", html);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/{customerId}/activity"))
        {
            request.Headers.Add("HX-Request", "true");
            using var activity = await client.SendAsync(request);
            var html = await activity.Content.ReadAsStringAsync();
            Check(activity.StatusCode == HttpStatusCode.OK &&
                  html.Contains("Timeline یکپارچه", StringComparison.Ordinal) &&
                  html.Contains("ارتباطات تجاری", StringComparison.Ordinal),
                "The heavy Customer 360 activity section must load through its scoped HTMX endpoint.");
        }

        using (var quality = await client.GetAsync("/customers/data-quality"))
        {
            var html = await quality.Content.ReadAsStringAsync();
            Check(quality.StatusCode == HttpStatusCode.OK && html.Contains("کیفیت داده مشتری", StringComparison.Ordinal),
                "A company manager must access the scoped customer data-quality dashboard.");
        }

        using (var duplicates = await client.GetAsync("/customers/duplicates"))
        {
            var html = await duplicates.Content.ReadAsStringAsync();
            Check(duplicates.StatusCode == HttpStatusCode.OK && html.Contains("بررسی رکوردهای مشابه", StringComparison.Ordinal),
                "A manager with Customer.MergeReview must access the duplicate queue.");
        }

        const string candidateId = "25000000-0000-4000-8000-000000000001";
        const string survivorId = "20000000-0000-4000-8000-000000000004";
        using (var pendingPreview = await client.GetAsync($"/customers/duplicates/{candidateId}/merge?survivorCustomerId={survivorId}"))
            Check(pendingPreview.StatusCode == HttpStatusCode.UnprocessableEntity,
                "Merge preview must be blocked until duplicate review is confirmed.");

        var reviewToken = await GetAntiforgeryToken(client, "/customers/duplicates");
        using (var reviewContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = reviewToken,
            ["Decision"] = "Confirmed",
            ["Note"] = "تأیید تست وب",
            ["ExpectedVersion"] = "1"
        }))
        using (var review = await client.PostAsync($"/customers/duplicates/{candidateId}/review", reviewContent))
            Check(review.StatusCode == HttpStatusCode.Redirect && review.Headers.Location?.OriginalString == "/customers/duplicates",
                "Duplicate review must retain a functional non-HTMX redirect fallback.");

        using (var preview = await client.GetAsync($"/customers/duplicates/{candidateId}/merge?survivorCustomerId={survivorId}"))
        {
            var html = await preview.Content.ReadAsStringAsync();
            Check(preview.StatusCode == HttpStatusCode.OK && html.Contains("پیش‌نمایش ادغام", StringComparison.Ordinal) &&
                  html.Contains("ExpectedSurvivorVersion", StringComparison.Ordinal),
                "Merge dry run must expose relationship impact and concurrency tokens.");
        }

        using (var history = await client.GetAsync("/customers/merges"))
            Check(history.StatusCode == HttpStatusCode.OK,
                "A manager with Customer.MergeReview must access merge audit history.");
        using (var historyTable = await client.GetAsync("/customers/merges/table"))
            Check(historyTable.StatusCode == HttpStatusCode.OK,
                "Merge audit history must provide an HTMX-refreshable table endpoint.");

        using (var opportunityOptions = await client.GetAsync($"/quotes/opportunity-options?customerId={customerId}"))
        {
            var html = await opportunityOptions.Content.ReadAsStringAsync();
            CheckHtml(opportunityOptions.StatusCode == HttpStatusCode.OK &&
                  HtmlContains(html, "تأمین سالانه مواد اولیه سپهر") &&
                  !HtmlContains(html, "قرارداد توزیع منطقه مرکز"),
                "Quote opportunity options must be filtered by the selected CustomerId.", "quote-opportunity-options", html);
        }

        using (var duplicateCheck = await client.GetAsync("/customers/duplicate-check?name=test&city=Tehran&nationalId=10101234567"))
        {
            var html = await duplicateCheck.Content.ReadAsStringAsync();
            CheckHtml(duplicateCheck.StatusCode == HttpStatusCode.OK && HtmlContains(html, "شناسه ملی تکراری"),
                "The HTMX duplicate-check endpoint must block an exact company-level national-id match.", "duplicate-check", html);
        }

        var token = await GetAntiforgeryToken(client, $"/customers/{customerId}/contacts/create");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Contact.Title"] = "خانم",
            ["Contact.FirstName"] = "نگار",
            ["Contact.LastName"] = "تست وب",
            ["Contact.Position"] = "خرید",
            ["Contact.Mobile"] = "۰۹۱۲۱۱۱۱۱۱۱",
            ["Contact.Extension"] = "۱۲",
            ["Contact.Email"] = string.Empty,
            ["Contact.IsPrimary"] = "false",
            ["ConsentStatus"] = "Unknown",
            ["ExpectedVersion"] = "0"
        });
        using var response = await client.PostAsync($"/customers/{customerId}/contacts/create", content);
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == $"/customers/{customerId}",
            "Customer contact creation must retain a functional non-HTMX redirect fallback.");
    }

    private static async Task CheckQuoteGovernance(HttpClient client)
    {
        using (var page = await client.GetAsync("/quotes"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Check(page.StatusCode == HttpStatusCode.OK && html.Contains("Commercial Control", StringComparison.Ordinal) &&
                  html.Contains("Margin", StringComparison.Ordinal),
                "Quote workspace must render pricing and approval governance metrics.");
        }
        using (var form = await client.GetAsync("/quotes/create"))
        {
            var html = await form.Content.ReadAsStringAsync();
            Check(form.StatusCode == HttpStatusCode.OK && html.Contains("ProductCode", StringComparison.Ordinal) &&
                  html.Contains("ValidUntilUtc", StringComparison.Ordinal) && html.Contains("__RequestVerificationToken", StringComparison.Ordinal),
                "Quote create drawer must render product snapshot, validity and anti-forgery inputs.");
        }

        var token = await GetAntiforgeryToken(client, "/quotes/create");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["CustomerId"] = "20000000-0000-4000-8000-000000000001",
            ["OpportunityId"] = "40000000-0000-4000-8000-000000000001",
            ["BranchId"] = "B01",
            ["CurrencyCode"] = "IRR",
            ["ValidUntilUtc"] = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
            ["PaymentTerms"] = "تسویه ۳۰ روزه",
            ["ProductCode"] = "PRD-1002",
            ["Quantity"] = "2",
            ["DiscountPercent"] = "4"
        });
        using var created = await client.PostAsync("/quotes/create", content);
        var location = created.Headers.Location?.OriginalString ?? string.Empty;
        Check(created.StatusCode == HttpStatusCode.Redirect && location.StartsWith("/quotes/", StringComparison.Ordinal),
            "Quote creation must preserve the executable non-HTMX redirect fallback.");
        if (string.IsNullOrWhiteSpace(location)) return;
        using var details = await client.GetAsync(location);
        var detailsHtml = await details.Content.ReadAsStringAsync();
        CheckHtml(details.StatusCode == HttpStatusCode.OK && HtmlContains(detailsHtml, "بسته مواد اولیه ویژه") &&
                  detailsHtml.Contains("ExpectedVersion", StringComparison.Ordinal),
            "Quote details must expose snapshotted line items and concurrency-protected actions.", "quote-details", detailsHtml);
    }

    private static async Task CheckOrderVisibility(HttpClient client)
    {
        using (var page = await client.GetAsync("/orders"))
        {
            var html = await page.Content.ReadAsStringAsync();
            CheckHtml(page.StatusCode == HttpStatusCode.OK && HtmlContains(html, "Order & ERP Visibility") &&
                      HtmlContains(html, "OR-1405-001") && HtmlContains(html, "توقف اعتباری"),
                "Order workspace must render the credit-hold seed and ERP visibility metrics.", "orders", html);
        }
        using (var create = await client.GetAsync("/orders/create"))
        {
            var html = await create.Content.ReadAsStringAsync();
            Check(create.StatusCode == HttpStatusCode.OK &&
                  html.Contains("پیشنهاد واجد شرایطی وجود ندارد", StringComparison.Ordinal),
                "Order drawer must explain when accepted quote revisions are already converted.");
        }

        const string orderId = "55000000-0000-4000-8000-000000000001";
        using var details = await client.GetAsync($"/orders/{orderId}");
        var detailsHtml = await details.Content.ReadAsStringAsync();
        var versionMatch = ExpectedVersionRegex().Match(detailsHtml);
        CheckHtml(details.StatusCode == HttpStatusCode.OK && HtmlContains(detailsHtml, "کنترل اعتبار") &&
                  HtmlContains(detailsHtml, "Outbox + Retry + Idempotency") && versionMatch.Success,
            "Order details must expose executable, concurrency-protected internal forms.", "order-details", detailsHtml);
        if (!versionMatch.Success) return;
        var token = await GetAntiforgeryToken(client, $"/orders/{orderId}");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ExpectedVersion"] = versionMatch.Groups[1].Value
        });
        using var response = await client.PostAsync($"/orders/{orderId}/credit-check", content);
        Check(response.StatusCode == HttpStatusCode.Redirect &&
              response.Headers.Location?.OriginalString == $"/orders/{orderId}",
            "Order credit-check form must execute with anti-forgery and non-HTMX redirect fallback.");
    }

    private static async Task CheckDealerGovernance(HttpClient client)
    {
        const string seededDealerId = "a0000000-0000-4000-8000-000000000001";
        using (var page = await client.GetAsync("/dealers"))
        {
            var html = await page.Content.ReadAsStringAsync();
            CheckHtml(page.StatusCode == HttpStatusCode.OK && HtmlContains(html, "Channel Governance") &&
                      HtmlContains(html, "نماینده پایلوت جنوب") && HtmlContains(html, "نماینده جدید"),
                "Dealer workspace must render scoped channel metrics and management actions.", "dealers", html);
        }
        using (var details = await client.GetAsync($"/dealers/{seededDealerId}"))
        {
            var html = await details.Content.ReadAsStringAsync();
            CheckHtml(details.StatusCode == HttpStatusCode.OK && HtmlContains(html, "Projection فقط‌خواندنی") &&
                      HtmlContains(html, "تفکیک وظیفه درخواست و تأیید") && html.Contains("ExpectedVersion", StringComparison.Ordinal),
                "Dealer details must expose read-only projections and concurrency-protected workflows.", "dealer-details", html);
        }
        using (var target = await client.GetAsync($"/dealers/{seededDealerId}/target"))
        {
            var html = await target.Content.ReadAsStringAsync();
            CheckHtml(target.StatusCode == HttpStatusCode.OK && html.Contains("name=\"ExpectedVersion\"", StringComparison.Ordinal) &&
                      !html.Contains("name=\"ExpectedVersion\" value=\"0\"", StringComparison.Ordinal),
                "Existing Dealer target form must carry its current optimistic-concurrency version.", "dealer-target", html);
        }

        var createPath = "/dealers/create";
        using var form = await client.GetAsync(createPath);
        var formHtml = await form.Content.ReadAsStringAsync();
        CheckHtml(form.StatusCode == HttpStatusCode.OK && formHtml.Contains("action=\"/dealers/save\"", StringComparison.Ordinal) &&
                  formHtml.Contains("__RequestVerificationToken", StringComparison.Ordinal),
            "Dealer drawer form must be executable and protected by anti-forgery.", "dealer-create", formHtml);
        var token = await GetAntiforgeryToken(client, createPath);
        var unique = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["DealerId"] = "P-WEB-" + unique,
            ["Code"] = "WEB-" + unique,
            ["LegalName"] = "شرکت آزمون وب نماینده",
            ["TradeName"] = "نماینده وب اولویت هشت " + unique,
            ["BranchId"] = "B01",
            ["TerritoryId"] = "T01",
            ["City"] = "تهران",
            ["NationalId"] = unique,
            ["Phone"] = "02188770000",
            ["Email"] = "dealer-web@test.local",
            ["ChannelManagerUserId"] = "10000000-0000-4000-8000-000000000007",
            ["ExpectedVersion"] = "0"
        });
        using var created = await client.PostAsync("/dealers/save", content);
        Check(created.StatusCode == HttpStatusCode.Redirect &&
              created.Headers.Location?.OriginalString.StartsWith("/dealers/", StringComparison.Ordinal) == true,
            "Dealer form must execute with the standard MVC redirect fallback.");
    }

    private static async Task CheckDealerScope(HttpClient client)
    {
        using (var page = await client.GetAsync("/dealers"))
        {
            var html = await page.Content.ReadAsStringAsync();
            CheckHtml(page.StatusCode == HttpStatusCode.OK && HtmlContains(html, "نماینده پایلوت جنوب") &&
                      !HtmlContains(html, "نماینده وب اولویت هشت") && !HtmlContains(html, "نماینده جدید"),
                "Dealer role must enumerate only its exact Dealer scope and hide management actions.", "dealer-scope", html);
        }
        using var create = await client.GetAsync("/dealers/create");
        Check(create.StatusCode == HttpStatusCode.Redirect &&
              create.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
            "Dealer role must be denied Dealer.Manage forms.");
    }

    private static async Task CheckExpertScopeAndPermission(HttpClient client)
    {
        using var identityResponse = await client.GetAsync("/identity/users");
        Check(identityResponse.StatusCode == HttpStatusCode.Redirect &&
              identityResponse.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
            "Sales expert must be denied identity administration.");

        using var customersResponse = await client.GetAsync("/customers");
        var html = await customersResponse.Content.ReadAsStringAsync();
        Check(customersResponse.StatusCode == HttpStatusCode.OK && HtmlContains(html, "از <b>1</b> حساب"),
            "Branch-scoped expert must only see the central-branch customer.");

        using (var details = await client.GetAsync("/customers/20000000-0000-4000-8000-000000000001"))
        {
            var detailsHtml = await details.Content.ReadAsStringAsync();
            Check(details.StatusCode == HttpStatusCode.OK && !detailsHtml.Contains("/customers/20000000-0000-4000-8000-000000000001/edit", StringComparison.Ordinal),
                "A read-only expert must see Customer 360 without update actions.");
        }

        using (var quality = await client.GetAsync("/customers/data-quality"))
            Check(quality.StatusCode == HttpStatusCode.OK,
                "The data-quality dashboard must be readable within the expert branch scope.");

        using (var duplicates = await client.GetAsync("/customers/duplicates"))
            Check(duplicates.StatusCode == HttpStatusCode.Redirect &&
                  duplicates.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
                "An expert without Customer.MergeReview must be denied the duplicate-review queue.");
        using (var merges = await client.GetAsync("/customers/merges"))
            Check(merges.StatusCode == HttpStatusCode.Redirect &&
                  merges.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
                "An expert without Customer.MergeReview must be denied merge audit history.");

        using (var leads = await client.GetAsync("/leads"))
        {
            var leadsHtml = await leads.Content.ReadAsStringAsync();
            CheckHtml(leads.StatusCode == HttpStatusCode.OK && HtmlContains(leadsHtml, "پایدار انرژی خاور") &&
                  !HtmlContains(leadsHtml, "تجارت نوین پارس"),
                "A sales expert must enumerate only owned Leads inside the selected branch.", "expert-leads", leadsHtml);
        }
        using (var otherLead = await client.GetAsync("/leads/30000000-0000-4000-8000-000000000002"))
            Check(otherLead.StatusCode == HttpStatusCode.NotFound,
                "Changing a Lead URL must not expose another owner's or branch's record.");
        using (var assignLead = await client.GetAsync("/leads/30000000-0000-4000-8000-000000000001/assign"))
            Check(assignLead.StatusCode == HttpStatusCode.Redirect &&
                  assignLead.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
                "A sales expert without Lead.Assign must not open the assignment form.");

        using (var opportunities = await client.GetAsync("/opportunities"))
        {
            var opportunitiesHtml = await opportunities.Content.ReadAsStringAsync();
            CheckHtml(opportunities.StatusCode == HttpStatusCode.OK &&
                  HtmlContains(opportunitiesHtml, "تأمین سالانه مواد اولیه سپهر") &&
                  !HtmlContains(opportunitiesHtml, "قرارداد توزیع منطقه مرکز"),
                "A sales expert must enumerate only owned Opportunities inside the selected branch.", "expert-opportunities", opportunitiesHtml);
        }
        using (var otherOpportunity = await client.GetAsync("/opportunities/40000000-0000-4000-8000-000000000002"))
            Check(otherOpportunity.StatusCode == HttpStatusCode.NotFound,
                "Changing an Opportunity URL must not expose another owner's or branch's record.");
        using (var assignOpportunity = await client.GetAsync("/opportunities/40000000-0000-4000-8000-000000000001/assign"))
            Check(assignOpportunity.StatusCode == HttpStatusCode.Redirect &&
                  assignOpportunity.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true,
                "A sales expert without Opportunity.Assign must not open the assignment form.");
    }

    private static async Task CheckAntiforgery(HttpClient client)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "درخواست بدون توکن",
            ["City"] = "تهران",
            ["Owner"] = "سارا احمدی",
            ["BranchId"] = "B01",
            ["Segment"] = "استاندارد"
        });
        using var response = await client.PostAsync("/customers/create", content);
        Check(response.StatusCode == HttpStatusCode.BadRequest, "Unsafe MVC request without anti-forgery token must return 400.");
    }

    private static async Task CheckOrganizationSwitchAndIsolation(HttpClient client)
    {
        var token = await GetAntiforgeryToken(client, "/context/select");
        using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["CompanyId"] = "C02",
            ["BranchId"] = string.Empty,
            ["TerritoryId"] = string.Empty,
            ["ReturnUrl"] = "/customers"
        }))
        using (var response = await client.PostAsync("/context/select", content))
        {
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/customers",
                "A permitted company switch must redirect to the local return URL.");
        }

        using (var customers = await client.GetAsync("/customers"))
        {
            var html = await customers.Content.ReadAsStringAsync();
            CheckHtml(customers.StatusCode == HttpStatusCode.OK && HtmlContains(html, "از <b>2</b> حساب") &&
                  HtmlContains(html, "بازرگانی دریا") &&
                  !HtmlContains(html, "صنایع غذایی سپهر"),
                "C02 context must expose only C02 customer data.", "company-c02-customers", html);
        }

        using (var crossCompanyRecord = await client.GetAsync("/customers/20000000-0000-4000-8000-000000000001"))
            Check(crossCompanyRecord.StatusCode == HttpStatusCode.NotFound,
                "Changing a record URL must not expose a customer from another company.");

        token = await GetAntiforgeryToken(client, "/context/select");
        using var tamperedContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["CompanyId"] = "C02",
            ["BranchId"] = "B01",
            ["TerritoryId"] = string.Empty,
            ["ReturnUrl"] = "/"
        });
        using var tamperedResponse = await client.PostAsync("/context/select", tamperedContent);
        Check(tamperedResponse.StatusCode == HttpStatusCode.UnprocessableEntity,
            "A cross-company branch selection must be rejected with 422.");
    }

    private static async Task CheckOrganizationAdministration(HttpClient client)
    {
        using (var page = await client.GetAsync("/organization"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Check(page.StatusCode == HttpStatusCode.OK && html.Contains("ساختار سازمانی و قلمروها", StringComparison.Ordinal),
                "Company manager must access organization administration.");
        }

        var token = await GetAntiforgeryToken(client, "/organization");
        using (var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["UnitId"] = "ST-WEB",
            ["Code"] = "P-ST-WEB",
            ["Name"] = "تیم فروش تست وب",
            ["Type"] = "SalesTeam",
            ["ParentUnitId"] = "B01",
            ["ExpectedVersion"] = "0"
        }))
        using (var response = await client.PostAsync("/organization/units/save", content))
        {
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/organization",
                "Valid organization unit creation must preserve the non-HTMX redirect fallback.");
        }

        using (var workspace = await client.GetAsync("/organization/workspace"))
        {
            var html = await workspace.Content.ReadAsStringAsync();
            CheckHtml(workspace.StatusCode == HttpStatusCode.OK && HtmlContains(html, "تیم فروش تست وب") &&
                  HtmlContains(html, "تاریخچه تغییرات"),
                "Organization workspace must reload the created unit and its history.", "organization-workspace", html);
        }

        token = await GetAntiforgeryToken(client, "/organization");
        using var tampered = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["UnitId"] = "ST-CROSS",
            ["Code"] = "P-ST-CROSS",
            ["Name"] = "تیم نامعتبر",
            ["Type"] = "SalesTeam",
            ["ParentUnitId"] = "B21",
            ["ExpectedVersion"] = "0"
        });
        using var tamperedResponse = await client.PostAsync("/organization/units/save", tampered);
        Check(tamperedResponse.StatusCode == HttpStatusCode.UnprocessableEntity,
            "A parent unit from another company must be rejected.");
    }

    private static async Task CheckSharedKeyRing(WebApplicationFactory<global::Program> secondNode, string cookie)
    {
        using var client = secondNode.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK,
            "A second node sharing the key ring and session store must decrypt and validate the first node cookie.");
    }

    private static async Task CheckLoginRateLimit(WebApplicationFactory<global::Program> factory)
    {
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryToken(client, "/account/login");
        var rateLimited = false;
        for (var index = 0; index < 12; index++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["userName"] = "invalid-user",
                ["password"] = "invalid-password"
            });
            using var response = await client.PostAsync("/account/login", content);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                var body = await response.Content.ReadAsStringAsync();
                Check(response.Headers.RetryAfter is not null && HtmlContains(body, "بیش از حد مجاز"),
                    "A rate-limited login must explain the limit and send Retry-After instead of an empty 429.");
                break;
            }
        }
        Check(rateLimited, "Repeated login attempts from one partition must eventually return 429.");
    }

    private static async Task CheckLogoutRevokesLocalSession(WebApplicationFactory<global::Program> factory)
    {
        using var client = CreateClient(factory);
        await Login(client, "sales.manager", "Demo@1405");
        var token = await GetAntiforgeryToken(client, "/account/security");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });
        using var response = await client.PostAsync("/account/logout", content);
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/account/signed-out",
            "Logout must revoke the local session and redirect to the signed-out page.");

        using var protectedResponse = await client.GetAsync("/");
        Check(protectedResponse.StatusCode == HttpStatusCode.Redirect,
            "A logged-out client must no longer access authenticated pages.");
    }

    private static async Task CheckOidcChallengeUsesCodeAndPkce(WebApplicationFactory<global::Program> factory)
    {
        using var client = CreateClient(factory);
        var token = await GetAntiforgeryToken(client, "/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["returnUrl"] = "/"
        });
        using var response = await client.PostAsync("/account/login/oidc", content);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Check(response.StatusCode == HttpStatusCode.Redirect && location.StartsWith("https://idp.test.local/authorize", StringComparison.Ordinal),
            "OIDC login must challenge the configured provider.");
        Check(location.Contains("response_type=code", StringComparison.Ordinal) &&
              location.Contains("code_challenge=", StringComparison.Ordinal) &&
              location.Contains("code_challenge_method=S256", StringComparison.Ordinal),
            "OIDC challenge must use Authorization Code with PKCE S256.");
        Check(location.Contains("state=", StringComparison.Ordinal) && location.Contains("nonce=", StringComparison.Ordinal),
            "OIDC challenge must contain protected state and nonce.");
    }

    private static async Task<string> GetAntiforgeryToken(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryRegex().Match(html);
        Check(match.Success, $"Anti-forgery token was not rendered on {path}.");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static bool HeaderContains(HttpResponseMessage response, string name, string expected) =>
        response.Headers.TryGetValues(name, out var values) && values.Any(x => x.Contains(expected, StringComparison.OrdinalIgnoreCase));

    private static bool HtmlContains(string html, string expected) =>
        WebUtility.HtmlDecode(html).Contains(expected, StringComparison.Ordinal);

    private static void Check(bool condition, string message)
    {
        if (!condition) Failures.Add(message);
    }

    private static void CheckHtml(bool condition, string message, string artifactName, string html)
    {
        if (condition) return;
        Directory.CreateDirectory(ArtifactsPath);
        File.WriteAllText(Path.Combine(ArtifactsPath, $"{artifactName}.html"), html);
        Failures.Add($"{message} Diagnostic HTML: {artifactName}.html");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryRegex();
    [GeneratedRegex("name=\"ExpectedVersion\"[^>]*value=\"([0-9]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ExpectedVersionRegex();
}

internal sealed class DemoWebFactory(string keyRingPath, ICrmDataStore sharedStore) : WebApplicationFactory<global::Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Authentication:Mode"] = "Demo",
        ["Authentication:DemoPassword"] = "Demo@1405",
        ["DataProtection:KeyRingPath"] = keyRingPath,
        ["DataProtection:RequireSharedKeyRing"] = "true",
        ["Security:IpHashSalt"] = "integration-test-ip-hash-salt",
        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning"
    };

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(_settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development").UseContentRoot(TestPaths.WebContentRoot);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICrmDataStore>();
            services.AddSingleton<ICrmDataStore>(sharedStore);
            services.RemoveAll<ICrmQuerySource>();
            services.AddSingleton((ICrmQuerySource)sharedStore);
        });
    }
}

internal sealed class OidcChallengeWebFactory : WebApplicationFactory<global::Program>
{
    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Authentication:Mode"] = "Oidc",
        ["Authentication:Oidc:Authority"] = "https://idp.test.local",
        ["Authentication:Oidc:ClientId"] = "crm-integration-tests",
        ["Authentication:Oidc:ClientSecret"] = "test-only-value-no-production-secret",
        ["Security:IpHashSalt"] = "integration-test-ip-hash-salt",
        ["Logging:LogLevel:Default"] = "Warning",
        ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning"
    };

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development").UseContentRoot(TestPaths.WebContentRoot);
        builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>(
            OpenIdConnectDefaults.AuthenticationScheme, StaticOidcConfiguration.Apply));
    }
}

internal static class TestPaths
{
    public static string WebContentRoot { get; } = ResolveWebContentRoot();

    private static string ResolveWebContentRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Crm.Web");
            if (File.Exists(Path.Combine(candidate, "Crm.Web.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate src/Crm.Web for integration-test content root.");
    }
}

internal static class StaticOidcConfiguration
{
    public static void Apply(OpenIdConnectOptions options)
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = "https://idp.test.local",
            AuthorizationEndpoint = "https://idp.test.local/authorize",
            TokenEndpoint = "https://idp.test.local/token",
            EndSessionEndpoint = "https://idp.test.local/logout"
        };
        options.Configuration = configuration;
        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }
}
