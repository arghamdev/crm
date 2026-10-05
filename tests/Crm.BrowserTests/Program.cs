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
    await VerifyAccountFile(page, baseUrl);
    await VerifyFollowUpCenter(page, baseUrl);
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

    // List workspace: select a row, the bulk bar appears, «افزودن فعالیت» plans the next action in place.
    var bulkBar = page.Locator("[data-bulk-bar]");
    Assert(!await bulkBar.IsVisibleAsync(), "The bulk bar must stay hidden until a row is selected.");
    await page.Locator("#leadList [data-bulk-item]").First.CheckAsync();
    await bulkBar.WaitForAsync();
    Assert((await page.Locator("[data-bulk-count]").InnerTextAsync()).Trim() == "1", "The bulk bar must count the selected rows.");
    await bulkBar.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "افزودن فعالیت" }).ClickAsync();
    var bulkForm = page.Locator("#drawerBody form[action='/leads/bulk/next-action']");
    await bulkForm.WaitForAsync();
    Assert(await bulkForm.Locator("input[name='Ids']").CountAsync() == 1, "The bulk drawer must carry the selected lead.");
    await bulkForm.Locator("input[name='NextAction']").FillAsync("ارسال پیشنهاد قیمت مرورگر");
    await bulkForm.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت فعالیت" }).ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.Locator("#leadList").GetByText("ارسال پیشنهاد قیمت مرورگر").WaitForAsync();
    Assert(!await page.Locator("[data-bulk-bar]").IsVisibleAsync(), "The refreshed list must clear the selection.");

    // Tabs, sort and layout are links that update the list in place and the address bar.
    await page.Locator(".list-tabs__tab", new PageLocatorOptions { HasText = "سرنخ‌های من" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("view=mine", StringComparison.Ordinal));
    await page.Locator(".list-tabs__tab.is-active", new PageLocatorOptions { HasText = "سرنخ‌های من" }).WaitForAsync();
    await page.Locator(".list-layout__btn[aria-label='نمای کارتی']").ClickAsync();
    await page.Locator("#leadList .list-cards").WaitForAsync();
    Assert(page.Url.Contains("layout=cards", StringComparison.Ordinal), "The card layout must be kept in the URL.");
    await page.GotoAsync(baseUrl + "/leads", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("#leadList details.row-menu summary").First.ClickAsync();
    await page.Locator("#leadList details.row-menu[open] .row-menu__list").WaitForAsync();
    await page.Locator(".list-head h1").ClickAsync();
    Assert(await page.Locator("details.row-menu[open]").CountAsync() == 0, "A row menu must close on an outside click.");

    // Customers use the same list kit: a counter tile filters the list.
    await page.GotoAsync(baseUrl + "/customers", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator(".list-tile", new PageLocatorOptions { HasText = "نیازمند تکمیل" }).ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("view=incomplete", StringComparison.Ordinal));
    await page.Locator(".list-tile.is-active", new PageLocatorOptions { HasText = "نیازمند تکمیل" }).WaitForAsync();
    await page.Locator("#customerFilters select[name='kind']").SelectOptionAsync("Legal");
    await page.WaitForURLAsync(url => url.Contains("kind=Legal", StringComparison.Ordinal));
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

static async Task VerifyAccountFile(IPage page, string baseUrl)
{
    const string accountPath = "/customers/20000000-0000-4000-8000-000000000001";
    var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var callSubject = "تماس مرورگر " + stamp;
    await page.GotoAsync(baseUrl + accountPath, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    // Side panel «پیگیری»: overdue follow-ups are listed with «انجام شد»; the overview no longer repeats the account info.
    var followUps = page.Locator("#followUpPanel");
    await followUps.Locator(".follow--overdue").First.WaitForAsync();
    Assert(await followUps.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "انجام شد" }).CountAsync() > 0, "Follow-up panel offers no «انجام شد» action.");
    Assert(await page.Locator("[data-tab-panel='overview'] .info-grid", new PageLocatorOptions { HasText = "نوع رابطه" }).CountAsync() == 0,
        "The overview still repeats the account information block.");
    // The activity panel lives in the «فعالیت‌ها و تعاملات» tab.
    await page.Locator("[data-tab='activities']").ClickAsync();
    var panel = page.Locator("#activityPanel");
    await panel.Locator(".act-group").First.WaitForAsync();

    // Quick action «برنامه‌ریزی تماس»: the account is preselected; a double click saves once and the panel refreshes in place.
    await page.Locator(".quick-action", new PageLocatorOptions { HasText = "برنامه‌ریزی تماس" }).ClickAsync();
    var form = page.Locator("#drawerBody form[action$='/activities']");
    await form.WaitForAsync();
    Assert(await page.Locator("#drawerBody .form-context").InnerTextAsync() is var context && context.Contains("صنایع غذایی سپهر"),
        "Account file call form does not show the preselected account.");
    await form.Locator("input[name='Subject']").FillAsync(callSubject);
    await form.Locator("button[type='submit']").DblClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await panel.GetByText(callSubject).First.WaitForAsync();
    Assert(await panel.GetByText(callSubject).CountAsync() == 1, "Double-clicked call was listed more than once.");
    await followUps.GetByText(callSubject).First.WaitForAsync();
    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("[data-tab='activities']").ClickAsync();
    await panel.GetByText(callSubject).First.WaitForAsync();

    // Record the outcome separately from planning, with a next action.
    var item = panel.Locator("article.act", new LocatorLocatorOptions { HasText = callSubject });
    await item.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت نتیجه" }).ClickAsync();
    var complete = page.Locator("#drawerBody form[action$='/complete']");
    await complete.WaitForAsync();
    await complete.Locator("select[name='CallResult']").SelectOptionAsync("Answered");
    await complete.Locator("textarea[name='Outcome']").FillAsync("مشتری درخواست نمونه داد");
    await complete.Locator("select[name='NextType']").SelectOptionAsync("Task");
    await complete.Locator("input[name='NextSubject']").FillAsync("ارسال نمونه " + stamp);
    await complete.Locator("input[name='NextDate']").FillAsync(JalaliInDays(2));
    await complete.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await panel.GetByText("ارسال نمونه " + stamp).First.WaitForAsync();
    await panel.Locator("article.act", new LocatorLocatorOptions { HasText = "مشتری درخواست نمونه داد" }).First.WaitForAsync();

    // Note from the quick actions.
    await page.Locator(".quick-action", new PageLocatorOptions { HasText = "ایجاد یادداشت" }).ClickAsync();
    var note = page.Locator("#drawerBody form[action$='/notes']");
    await note.WaitForAsync();
    await note.Locator("input[name='Title']").FillAsync("یادداشت مرورگر " + stamp);
    await note.Locator("textarea[name='Body']").FillAsync("متن آزمون");
    await note.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.Locator("[data-tab='side-notes']").ClickAsync();
    await page.Locator("#notesPanel").GetByText("یادداشت مرورگر " + stamp).WaitForAsync();

    // Related records: create a payment from its section, then cancel it through the reason prompt.
    await page.Locator("[data-tab='related']").ClickAsync();
    await page.Locator("#sec-payments .acc-section__title").ClickAsync();
    var payments = page.Locator("#sec-payments .acc-section__body");
    await payments.Locator(".rec-list").WaitForAsync();
    await payments.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت پرداخت / دریافت" }).ClickAsync();
    var payment = page.Locator("#drawerBody form[action$='/records/payments/new']");
    await payment.WaitForAsync();
    await payment.Locator("input[name='amount']").FillAsync("7770000");
    Assert(await payment.Locator("input[name='amount']").InputValueAsync() == "7,770,000", "Amount input is not formatted with thousands separators.");
    await payment.Locator("input[name='reference']").FillAsync("BRW-" + stamp);
    await payment.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    var row = payments.Locator("li.rec", new LocatorLocatorOptions { HasText = "BRW-" + stamp });
    await row.WaitForAsync();
    Assert((await row.InnerTextAsync()).Contains("ثبت‌شده"), "New payment is not in the «ثبت‌شده» status.");
    page.Dialog += Accept;
    await row.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "لغو" }).ClickAsync();
    await payments.Locator("li.rec", new LocatorLocatorOptions { HasText = "BRW-" + stamp }).Filter(new LocatorFilterOptions { HasText = "لغوشده" }).WaitForAsync();
    page.Dialog -= Accept;

    // History tab shows who did what.
    await page.Locator("[data-tab='history']").ClickAsync();
    await page.Locator("[data-tab-panel='history'] .timeline").GetByText("لغوشده").First.WaitForAsync();

    static string JalaliInDays(int days)
    {
        var tehran = DateTime.UtcNow.AddHours(3.5).AddDays(days);
        var calendar = new System.Globalization.PersianCalendar();
        return $"{calendar.GetYear(tehran):0000}/{calendar.GetMonth(tehran):00}/{calendar.GetDayOfMonth(tehran):00}";
    }

    static async void Accept(object? sender, IDialog dialog) => await dialog.AcceptAsync(dialog.Type == DialogType.Prompt ? "ثبت تکراری" : null);
}

static async Task VerifyFollowUpCenter(IPage page, string baseUrl)
{
    var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var subject = "پیگیری مرورگر " + stamp;
    // List: seeded cases, counters and the referral inbox.
    await page.GotoAsync(baseUrl + "/follow-ups", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("#followUpList").GetByText("RQ-24085").First.WaitForAsync();
    Assert(await page.Locator(".fu-inbox__list li").CountAsync() > 0, "Follow-up inbox does not list the pending referral.");

    // ۱. Registration: choosing the customer reloads contacts and related records; the case opens with its stages.
    await page.GotoAsync(baseUrl + "/follow-ups/new", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("#createForm select[name='CustomerId']").SelectOptionAsync("20000000-0000-4000-8000-000000000001");
    await page.Locator("#createForm select[name='ContactId'] option", new PageLocatorOptions { HasText = "علی رستگار" }).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
    await page.Locator("#createForm input[name='Subject']").FillAsync(subject);
    await page.Locator("#createForm input[name='Parts[0].PartCode']").FillAsync("SEAL-" + stamp);
    await page.Locator("#createForm input[name='Parts[0].Quantity']").FillAsync("4");
    await page.Locator("#createForm button[type='submit']").DblClickAsync();
    await page.WaitForURLAsync(url => url.Contains("/follow-ups/") && url.Contains("created=1"));
    await page.Locator(".fu-head h1", new PageLocatorOptions { HasText = subject }).WaitForAsync();
    Assert(await page.Locator(".fu-stage-table tbody tr").CountAsync() == 5, "The new case does not have the five template stages.");
    var caseUrl = page.Url.Split('?')[0];

    // ۲. Plan a call for the active stage.
    await page.Locator(".fu-head__actions button", new PageLocatorOptions { HasText = "برنامه‌ریزی اقدام" }).ClickAsync();
    var plan = page.Locator("#drawerBody form[action$='/plan']");
    await plan.WaitForAsync();
    await plan.Locator("input[name='Title']").FillAsync("تماس مرورگر " + stamp);
    await plan.Locator("button[type='submit']").DblClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.Locator(".fu-kpi", new PageLocatorOptions { HasText = "تماس مرورگر " + stamp }).WaitForAsync();

    // ۳. Record the result: logging the call does not complete the stage.
    await page.Locator("[data-fu-tab='activities']").ClickAsync();
    var activity = page.Locator(".fu-activity", new PageLocatorOptions { HasText = "تماس مرورگر " + stamp });
    Assert(await activity.CountAsync() == 1, "A double-clicked plan created the activity twice.");
    await activity.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "ثبت نتیجه" }).ClickAsync();
    var result = page.Locator("#drawerBody form[action$='/result']");
    await result.WaitForAsync();
    await result.Locator("select[name='ResultCode']").SelectOptionAsync("NeedsNextAction");
    await result.Locator("textarea[name='Outcome']").FillAsync("اقلام تأیید شد");
    await result.Locator("input[name='NextTitle']").FillAsync("ارسال لیست به فنی " + stamp);
    await result.Locator("input[name='RequestStageCompletion']").CheckAsync();
    await result.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.Locator(".fu-kpi", new PageLocatorOptions { HasText = "ارسال لیست به فنی " + stamp }).WaitForAsync();
    await page.Locator("[data-fu-tab='stages']").ClickAsync();
    Assert(!(await page.Locator(".fu-stage-table tbody tr").First.InnerTextAsync()).Contains("تکمیل‌شده"), "A logged call completed the stage.");

    // ۵. «تکمیل مرحله» unlocks only after the whole checklist is ticked; completing moves the weighted progress.
    var checklist = page.Locator("form[data-checklist]");
    var completeButton = checklist.Locator("[data-complete]");
    Assert(await completeButton.IsDisabledAsync(), "«تکمیل مرحله» is not locked while checklist items remain.");
    foreach (var box in await checklist.Locator("input[name='done']").AllAsync()) await box.CheckAsync();
    Assert(!await completeButton.IsDisabledAsync(), "«تکمیل مرحله» stays locked after the checklist is done.");
    await completeButton.ClickAsync();
    await page.Locator(".fu-total", new PageLocatorOptions { HasText = "10" }).WaitForAsync();

    // ۶. Waiting for the customer pauses the SLA; resuming needs a next action.
    await page.Locator(".fu-head__actions button", new PageLocatorOptions { HasText = "انتظار" }).ClickAsync();
    var wait = page.Locator("#drawerBody form[action$='/wait']");
    await wait.WaitForAsync();
    await wait.Locator("textarea[name='Reason']").FillAsync("منتظر نقشهٔ فنی");
    await wait.Locator("button[type='submit']").ClickAsync();
    await page.Locator(".fu-banner--wait").WaitForAsync();
    Assert((await page.Locator(".fu-head").InnerTextAsync()).Contains("مهلت متوقف"), "Waiting for the customer did not pause the SLA.");
    await page.Locator(".fu-head__actions button", new PageLocatorOptions { HasText = "ازسرگیری" }).ClickAsync();
    var resume = page.Locator("#drawerBody form[action$='/resume']");
    await resume.WaitForAsync();
    await resume.Locator("input[name='NextTitle']").FillAsync("بررسی نقشه " + stamp);
    await resume.Locator("button[type='submit']").ClickAsync();
    await page.Locator("#drawer[aria-hidden='true']").WaitForAsync();
    await page.Locator(".fu-banner--wait").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

    // ۸. The close form lists the controls and does not allow closing an incomplete case.
    await page.Locator(".fu-head__actions button", new PageLocatorOptions { HasText = "بستن پرونده" }).ClickAsync();
    var close = page.Locator("#drawerBody form[action$='/close']");
    await close.WaitForAsync();
    Assert(await close.Locator(".fu-checks .is-fail").CountAsync() > 0 && await close.Locator("button[type='submit']").IsDisabledAsync(),
        "An incomplete case can be closed from the form.");
    await page.Locator("#drawerClose").ClickAsync();
    await page.Locator("[data-fu-tab='history']").ClickAsync();
    await page.Locator("[data-fu-panel='history'] .fu-timeline").GetByText("پیشرفت پرونده").First.WaitForAsync();
    await page.GotoAsync(caseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

    // ۹–۱۲. Template designer (weights must total 100), SLA preview, supervision board.
    await page.GotoAsync(baseUrl + "/follow-ups/settings", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("a.cell-title[href^='/follow-ups/settings/templates/']", new PageLocatorOptions { HasText = "پیش‌فاکتور" }).ClickAsync();
    await page.Locator("[data-weight-sum].is-ok").WaitForAsync();
    await page.GotoAsync(baseUrl + "/follow-ups/settings?tab=policies", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("[data-fu-panel='policies'] a.cell-title").First.ClickAsync();
    await page.Locator("input[name='previewDate']").FillAsync("1405/07/15");
    await page.Locator("button", new PageLocatorOptions { HasText = "پیش‌نمایش محاسبه" }).ClickAsync();
    await page.Locator("#slaPreview .fu-facts").WaitForAsync();
    await page.GotoAsync(baseUrl + "/follow-ups/supervision", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator(".fu-super table").GetByText("RQ-24087").First.WaitForAsync();
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
