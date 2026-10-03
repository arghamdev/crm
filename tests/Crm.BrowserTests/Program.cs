using Microsoft.Playwright;

if (args.FirstOrDefault()?.Equals("install", StringComparison.OrdinalIgnoreCase) == true)
    return Microsoft.Playwright.Program.Main(args);

var baseUrl = (Environment.GetEnvironmentVariable("CRM_E2E_BASE_URL") ?? "http://localhost:5085").TrimEnd('/');
var artifacts = Environment.GetEnvironmentVariable("CRM_E2E_ARTIFACTS") ??
    Path.Combine(Path.GetTempPath(), "enterprise-crm-browser-tests");
Directory.CreateDirectory(artifacts);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,
    ExecutablePath = Environment.GetEnvironmentVariable("CRM_E2E_BROWSER_PATH")
});
await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    Locale = "fa-IR",
    IgnoreHTTPSErrors = new Uri(baseUrl).IsLoopback,
    ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
});
await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
var page = await context.NewPageAsync();
var browserErrors = new List<string>();
page.Console += (_, message) =>
{
    if (message.Type == "error") browserErrors.Add($"console: {message.Text} ({message.Location})");
};
page.PageError += (_, error) => browserErrors.Add($"page: {error}");

try
{
    await LoginAsManager(page, baseUrl);
    await VerifyLeadForm(page, baseUrl);
    await VerifyOpportunityForm(page, baseUrl);
    await VerifyQuoteForm(page, baseUrl);
    await VerifyOrderForm(page, baseUrl);
    await VerifyDealerForm(page, baseUrl);
    await VerifyActivityForm(page, baseUrl);
    await VerifyServiceDesk(page, baseUrl, artifacts);
    await VerifyReporting(page, baseUrl);
    await VerifyPortalAndMobile(page, baseUrl, artifacts);
    await VerifyUnifiedPreview(page, artifacts);
    Assert(browserErrors.Count == 0, "Browser console/page errors: " + string.Join(" | ", browserErrors));
    await context.Tracing.StopAsync();
    Console.WriteLine("Chromium cumulative forms, reporting, downloads and unified preview navigation passed.");
    return 0;
}
catch (Exception exception)
{
    await page.ScreenshotAsync(new PageScreenshotOptions
    {
        Path = Path.Combine(artifacts, "priority9-browser-failure.png"),
        FullPage = true
    });
    await context.Tracing.StopAsync(new TracingStopOptions
    {
        Path = Path.Combine(artifacts, "priority9-browser-trace.zip")
    });
    await File.WriteAllTextAsync(Path.Combine(artifacts, "priority9-browser-error.txt"),
        exception + Environment.NewLine + string.Join(Environment.NewLine, browserErrors));
    Console.Error.WriteLine(exception);
    return 1;
}

static async Task LoginAsManager(IPage page, string baseUrl)
{
    var response = await page.GotoAsync(baseUrl + "/account/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    Assert(response?.Ok == true, "Login page did not return a successful response.");
    await page.Locator("input[name='userName']").FillAsync("sales.manager");
    await page.Locator("input[name='password']").FillAsync("Demo@1405");
    await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "ورود به سامانه" }).ClickAsync();
    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

    if (page.Url.Contains("/context/select", StringComparison.OrdinalIgnoreCase))
    {
        var company = page.Locator("select[name='CompanyId']");
        await company.SelectOptionAsync("C01");
        await page.Locator("form.context-form").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "ورود به محیط کاری" }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/context/select", StringComparison.OrdinalIgnoreCase));
    }

    Assert(await page.Locator(".app-shell").CountAsync() == 1, "Authenticated application shell was not rendered.");
}

static async Task VerifyServiceDesk(IPage page, string baseUrl, string artifacts)
{
    await page.GotoAsync(baseUrl + "/service", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(await page.Locator(".main-nav a[href='/service'].is-active").CountAsync() == 1, "Service desk navigation item must be active.");
    await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "پرونده جدید" }).ClickAsync();
    var form = page.Locator("#drawerBody form[action='/service/create']");
    await form.WaitForAsync();
    var subject = "پرونده مرورگر " + Guid.NewGuid().ToString("N")[..6];
    await form.Locator("select[name='CustomerId']").SelectOptionAsync("20000000-0000-4000-8000-000000000001");
    await form.Locator("input[name='Subject']").FillAsync(subject);
    await form.Locator("select[name='Priority']").SelectOptionAsync("High");
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت پرونده" }).ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    var row = page.Locator("#serviceTable a.entity-link").Filter(new LocatorFilterOptions { HasText = subject });
    await row.WaitForAsync();
    await row.ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("/service/", StringComparison.OrdinalIgnoreCase));
    await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "تریاژ / تخصیص" }).ClickAsync();
    var triage = page.Locator("#drawerBody form[action$='/triage']");
    await triage.WaitForAsync();
    await triage.Locator("select[name='OwnerUserId']").SelectOptionAsync("10000000-0000-4000-8000-000000000001");
    await triage.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت تریاژ" }).ClickAsync();
    // The details block re-renders itself on serviceChanged; wait for the new status badge.
    await page.Locator("#caseDetails .title-line").GetByText("تریاژشده").WaitForAsync();
    await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "شروع رسیدگی" }).ClickAsync();
    var start = page.Locator("#drawerBody form[action$='/action']");
    await start.WaitForAsync();
    await start.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت" }).ClickAsync();
    await page.Locator("#caseDetails .title-line").GetByText("در حال رسیدگی").WaitForAsync();
    Assert(await page.Locator("#caseDetails tbody tr").CountAsync() >= 3, "Service case history must list create, triage and start.");
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifacts, "service-case.png"), FullPage = true });
}

static async Task VerifyReporting(IPage page, string baseUrl)
{
    await page.GotoAsync(baseUrl + "/reports", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("select[name='BranchId']").SelectOptionAsync("B01");
    await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "اعمال فیلتر" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("BranchId=B01"));
    await page.Locator("[data-report-metric='forecast'] a").ClickAsync();
    await page.Locator("#reportDrilldown").WaitForAsync();
    var download = await page.RunAndWaitForDownloadAsync(async () =>
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "دریافت CSV امن" }).ClickAsync());
    Assert(download.SuggestedFilename.Contains("forecast"), "P9: CSV download failed.");
}

static async Task VerifyPortalAndMobile(IPage page, string baseUrl, string artifacts)
{
    await page.SetViewportSizeAsync(390,844);
    await page.GotoAsync(baseUrl+"/mobile");
    var purpose="P10 browser "+Guid.NewGuid().ToString("N")[..8];
    var plan=page.Locator("form[action='/mobile/visits']");
    await plan.Locator("select[name='CustomerId']").SelectOptionAsync("20000000-0000-4000-8000-000000000001");
    await plan.Locator("input[name='PlannedAtUtc']").FillAsync(DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm"));
    await plan.Locator("input[name='Purpose']").FillAsync(purpose);
    await plan.Locator("button[type='submit']").ClickAsync();
    var card=page.Locator("[data-visit]").Filter(new LocatorFilterOptions { HasText=purpose });
    await card.WaitForAsync();
    await page.Context.SetOfflineAsync(true);
    await page.WaitForFunctionAsync("() => !navigator.onLine");
    await card.Locator("textarea[name='Outcome']").FillAsync("شروع بازدید آفلاین");
    await card.Locator("button[type='submit']").ClickAsync();
    await card.Locator("select[name='Status']").SelectOptionAsync("Completed");
    await card.Locator("textarea[name='Outcome']").FillAsync("نتیجه بازدید آفلاین");
    await card.Locator("button[type='submit']").ClickAsync();
    Assert((await page.Locator("#mobileSyncStatus").InnerTextAsync()).Contains("2"),"P10: two offline actions must be queued.");
    await page.Context.SetOfflineAsync(false);
    await page.Locator("#mobileSync").ClickAsync();
    await page.WaitForFunctionAsync("() => document.getElementById('mobileSyncDetail').textContent.includes('آخرین ارسال موفق')");
    await page.ReloadAsync();
    Assert((await card.Locator("[data-visit-status]").InnerTextAsync()).Contains("انجام‌شده"),"P10: replayed offline visit must persist after reload.");
    await page.ScreenshotAsync(new PageScreenshotOptions {Path=Path.Combine(artifacts,"priority10-mobile.png"),FullPage=true});
    await page.SetViewportSizeAsync(1440,1000);
    await page.Locator("form[action='/account/logout'] button").ClickAsync();
    await page.GotoAsync(baseUrl+"/account/login");
    await page.Locator("input[name='userName']").FillAsync("dealer.user");
    await page.Locator("input[name='password']").FillAsync("Demo@1405");
    await page.GetByRole(AriaRole.Button,new PageGetByRoleOptions{Name="ورود به سامانه"}).ClickAsync();
    await page.WaitForURLAsync("**/portal");
    await page.SetViewportSizeAsync(390,844);
    var request=page.Locator("form[action='/portal/requests']");
    var subject="P10 browser claim "+Guid.NewGuid().ToString("N")[..8];
    await request.Locator("select[name='Kind']").SelectOptionAsync("Claim");
    await request.Locator("input[name='Subject']").FillAsync(subject);
    await request.Locator("textarea[name='Description']").FillAsync("شرح ادعای آزمایشی");
    await request.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#requests h3").Filter(new LocatorFilterOptions{HasText=subject}).WaitForAsync();
    Assert(await page.Locator(".main-nav a[href='/partner-requests']").CountAsync()==0,"P10: dealer must not receive internal desk navigation.");
    var download=await page.RunAndWaitForDownloadAsync(async()=>await page.Locator("form[action^='/portal/invoices/'] button").First.ClickAsync());
    Assert(download.SuggestedFilename=="dealer-invoice.csv","P10: scoped invoice download.");
    await page.ScreenshotAsync(new PageScreenshotOptions{Path=Path.Combine(artifacts,"priority10-portal.png"),FullPage=true});
    await page.SetViewportSizeAsync(1440,1000);
}

static async Task VerifyUnifiedPreview(IPage page, string artifacts)
{
    var root = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "EnterpriseCrm.sln"))) root = root.Parent;
    Assert(root is not null, "Cannot locate solution root for unified preview.");
    var preview = Path.Combine(root!.FullName, "preview", "crm-unified.html");
    Assert(File.Exists(preview), "Unified preview must be built before running browser tests.");
    await page.GotoAsync(new Uri(preview).AbsoluteUri + "#/reports");
    await page.Locator("#loginSubmit").ClickAsync();
    await page.Locator("[data-report-metric='forecast']").WaitForAsync();
    Assert(await page.Locator("#appShell").IsVisibleAsync(), "Preview login must reveal the shared shell.");
    Assert(!page.Url.Contains("password=", StringComparison.OrdinalIgnoreCase), "Preview credentials must not enter the URL.");
    foreach (var route in new[] { "dashboard", "customers", "leads", "opportunities", "quotes", "orders", "dealers", "workqueue", "reports", "identity", "organization" })
    {
        await page.Locator($".main-nav [data-route='{route}']").ClickAsync();
        Assert(await page.Locator("#viewHost h1").CountAsync() == 1, $"Preview route {route} must render one heading.");
        Assert(!(await page.Locator("#viewHost").InnerTextAsync()).Contains("نمایش صفحه با خطا"), $"Preview route {route} failed.");
    }
    await page.Locator(".main-nav [data-route='reports']").ClickAsync();
    await page.Locator("[data-report-metric='forecast'] a").ClickAsync();
    var file = await page.RunAndWaitForDownloadAsync(async () => await page.Locator("[data-u-action='csv']").ClickAsync());
    Assert(file.SuggestedFilename == "crm-forecast.csv", "Unified preview CSV download failed.");
    await page.GoBackAsync();
    await page.Locator("[data-report-metric='forecast']").WaitForAsync();
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifacts, "unified-desktop.png"), FullPage = true });
    await page.ReloadAsync();
    if (await page.Locator("#loginScreen").IsVisibleAsync()) await page.Locator("#loginSubmit").ClickAsync();
    await page.Locator("[data-report-metric='forecast']").WaitForAsync();
    await page.SetViewportSizeAsync(390, 844);
    await page.Locator("#menuButton").ClickAsync();
    await page.Locator(".main-nav [data-route='customers']").ClickAsync();
    Assert(await page.Locator("#viewHost h1").IsVisibleAsync(), "Mobile navigation must reveal the requested page.");
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifacts, "unified-mobile.png"), FullPage = true });
    await page.SetViewportSizeAsync(1440, 1000);
}

static async Task VerifyLeadForm(IPage page, string baseUrl)
{
    var leadName = "سرنخ مرورگر اولویت پنج " + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await page.GotoAsync(baseUrl + "/leads", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("a[href='/leads/create']").ClickAsync();
    var form = page.Locator("#drawerBody form[action='/leads/create']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='__RequestVerificationToken']").CountAsync() == 1,
        "Lead HTMX form is missing its anti-forgery token.");
    await form.Locator("input[name='Name']").FillAsync(leadName);
    await form.Locator("input[name='Contact']").FillAsync("مدیر خرید تست مرورگر");
    await form.Locator("input[name='Phone']").FillAsync("09120005505");
    await form.Locator("input[name='Email']").FillAsync("browser-p5@test.local");
    await form.Locator("select[name='BranchId']").SelectOptionAsync("B01");
    await form.Locator("select[name='OwnerUserId'] option").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت و تخصیص" }).ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.GotoAsync(baseUrl + "/leads?q=" + Uri.EscapeDataString(leadName),
        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(await page.GetByText(leadName, new PageGetByTextOptions { Exact = true }).CountAsync() == 1,
        "Created Lead was not visible after the HTMX submission.");
}

static async Task VerifyOpportunityForm(IPage page, string baseUrl)
{
    var title = "فرصت مرورگر اولویت پنج " + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await page.GotoAsync(baseUrl + "/opportunities", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("a[href='/opportunities/create']").ClickAsync();
    var form = page.Locator("#drawerBody form[action='/opportunities/create']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='__RequestVerificationToken']").CountAsync() == 1,
        "Opportunity HTMX form is missing its anti-forgery token.");
    await form.Locator("input[name='Title']").FillAsync(title);
    await form.Locator("input[name='Value']").FillAsync("1500000000");
    await form.Locator("select[name='BranchId']").SelectOptionAsync("B01");
    await form.Locator("select[name='CustomerId'] option").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ایجاد فرصت" }).ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.GotoAsync(baseUrl + "/opportunities?q=" + Uri.EscapeDataString(title),
        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(await page.GetByText(title, new PageGetByTextOptions { Exact = true }).CountAsync() == 1,
        "Created Opportunity was not visible after the HTMX submission.");
}

static async Task VerifyActivityForm(IPage page, string baseUrl)
{
    const string opportunityId = "40000000-0000-4000-8000-000000000001";
    var subject = "فعالیت مرورگر " + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    await page.GotoAsync(baseUrl + "/opportunities/" + opportunityId,
        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator($"a[href='/opportunities/{opportunityId}/activities/create']").ClickAsync();
    var form = page.Locator($"#drawerBody form[action='/opportunities/{opportunityId}/activities/create']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='ExpectedVersion']").CountAsync() == 1,
        "Opportunity activity form is missing its concurrency token.");
    await form.Locator("input[name='Subject']").FillAsync(subject);
    await form.Locator("textarea[name='Outcome']").FillAsync("نتیجه تست مرورگر با موفقیت ثبت شد");
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت فعالیت" }).ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    Assert(await page.GetByText(subject, new PageGetByTextOptions { Exact = false }).CountAsync() >= 1,
        "Created activity was not visible after reloading Opportunity details.");
}

static async Task VerifyQuoteForm(IPage page, string baseUrl)
{
    await page.GotoAsync(baseUrl + "/quotes", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("a[href='/quotes/create']").ClickAsync();
    var form = page.Locator("#drawerBody form[action='/quotes/create']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='__RequestVerificationToken']").CountAsync() == 1,
        "Quote HTMX form is missing its anti-forgery token.");
    await form.Locator("select[name='CustomerId']").SelectOptionAsync("20000000-0000-4000-8000-000000000001");
    await form.Locator("select[name='OpportunityId'] option[value='40000000-0000-4000-8000-000000000001']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    await form.Locator("select[name='OpportunityId']").SelectOptionAsync("40000000-0000-4000-8000-000000000001");
    await form.Locator("select[name='BranchId']").SelectOptionAsync("B01");
    await form.Locator("select[name='ProductCode']").SelectOptionAsync("PRD-1002");
    await form.Locator("input[name='Quantity']").FillAsync("2");
    await form.Locator("input[name='DiscountPercent']").FillAsync("4");
    await form.Locator("input[name='ValidUntilUtc']").FillAsync(DateTimeOffset.UtcNow.AddDays(30).ToString("yyyy-MM-ddTHH:mm"));
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ساخت پیش‌نویس" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("/quotes/", StringComparison.OrdinalIgnoreCase) && !url.EndsWith("/quotes", StringComparison.OrdinalIgnoreCase));
    Assert(await page.GetByText("بسته مواد اولیه ویژه", new PageGetByTextOptions { Exact = true }).CountAsync() == 1,
        "Created Quote did not render its ERP price snapshot line.");
    Assert(await page.Locator("form[action$='/submit'] input[name='ExpectedVersion']").CountAsync() == 1,
        "Quote submission action is missing its concurrency token.");
}

static async Task VerifyOrderForm(IPage page, string baseUrl)
{
    const string orderId = "55000000-0000-4000-8000-000000000001";
    await page.GotoAsync(baseUrl + "/orders/" + orderId,
        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    var form = page.Locator($"form[action='/orders/{orderId}/credit-check']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='__RequestVerificationToken']").CountAsync() == 1,
        "Order credit-check form is missing its anti-forgery token.");
    Assert(await form.Locator("input[name='ExpectedVersion']").CountAsync() == 1,
        "Order credit-check form is missing its concurrency token.");
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "دریافت Snapshot اعتبار" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("/orders/" + orderId, StringComparison.OrdinalIgnoreCase));
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    Assert(await page.GetByText("بدهی سررسیدشده و Hold حسابداری", new PageGetByTextOptions { Exact = false }).CountAsync() >= 1,
        "Executed Order form did not render the accounting credit decision.");
}

static async Task VerifyDealerForm(IPage page, string baseUrl)
{
    const string dealerId = "a0000000-0000-4000-8000-000000000001";
    const string customerId = "20000000-0000-4000-8000-000000000001";
    await page.GotoAsync(baseUrl + "/dealers/" + dealerId,
        new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "تخصیص مشتری" }).ClickAsync();
    var form = page.Locator($"#drawerBody form[action='/dealers/{dealerId}/customers/assign']");
    await form.WaitForAsync();
    Assert(await form.Locator("input[name='__RequestVerificationToken']").CountAsync() == 1,
        "Dealer assignment form is missing its anti-forgery token.");
    await form.Locator("select[name='CustomerId']").SelectOptionAsync(customerId);
    await form.Locator("textarea[name='Reason']").FillAsync("تخصیص از آزمون مرورگر اولویت هشت");
    await form.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "افزودن به سبد نماینده" }).ClickAsync();
    // HX-Redirect targets the page we are already on, so the URL check passes before the reload; wait for the reloaded content.
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    await page.GetByText("صنایع غذایی سپهر", new PageGetByTextOptions { Exact = true }).First.WaitForAsync();
    await page.WaitForURLAsync(url => url.Contains("/dealers/" + dealerId, StringComparison.OrdinalIgnoreCase));
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    Assert(await page.GetByText("صنایع غذایی سپهر", new PageGetByTextOptions { Exact = true }).CountAsync() >= 1,
        "Executed Dealer assignment form did not render the assigned customer.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
