# Enterprise CRM — نمونه مرحله صفر تا اولویت ۱۰

این مخزن یک اسکلت اجرایی و آزمایشی برای CRM سازمانی فارسی است. داده‌ها ساختگی‌اند؛ حالت پیش‌فرض In-Memory و حالت اختیاری SQL Server/EF Core است.

## وضعیت

| بخش | وضعیت نمونه |
|---|---|
| مرحله صفر | بسته‌شده؛ تصمیم‌ها در docs/phase-0-sample-closure-fa.md ثبت شده‌اند |
| اولویت ۱ | پیاده‌سازی‌شده؛ Foundation و Modular Monolith |
| اولویت ۲ | پیاده‌سازی نمونه؛ OIDC-ready، نشست سمت سرور، RBAC/Scope و Audit |
| اولویت ۳ | پیاده‌سازی نمونه؛ Context چندشرکتی، مدیریت ساختار، EF Core/SQL Server و Cache قابل توزیع |
| اولویت ۴ | پیاده‌سازی نمونه؛ Customer Master، Customer 360، کیفیت داده و Duplicate Review |
| اولویت ۵ | پیاده‌سازی نمونه؛ Lead Lifecycle، SLA، Ownership، Opportunity Pipeline، Activity و Quality Gate خودکار |
| اولویت ۶ | پیاده‌سازی نمونه؛ Quote Line، ERP Price Snapshot، Margin، Approval Matrix، Revision و Audit |
| اولویت ۷ | پیاده‌سازی نمونه؛ Order Request، Credit Hold/Override، ERP Outbox/Retry/DLQ و Delivery/Invoice/Payment Projection |
| اولویت ۸ | پیاده‌سازی نمونه؛ Dealer Master، قرارداد، Territory، سبد مشتری، هدف و Projection مالی/عملکرد |
| اولویت ۹ | پیاده‌سازی نمونه؛ KPI Catalog، داشبورد نقش‌محور، Forecast، قیف تبدیل، وصول، کیفیت داده، Drilldown و خروجی امن CSV/BI |
| اولویت ۱۰ | پیاده‌سازی نمونه؛ پرتال نماینده، درخواست سفارش/سرنخ/شکایت/Claim/حساب کاربری، بررسی داخلی، بازدید موبایل و صف موقت آفلاین |
| مرحله ۸ نقشهٔ راه | پیاده‌سازی نمونه؛ خدمات و SLA: پرونده، تریاژ، SLA پاسخ/حل با توقف، ارجاع خودکار، علت ریشه‌ای، CSAT و Reopen — [مستند](docs/phase-8-service-desk-fa.md) |
| Production Readiness | نیازمند IdP/Redis/SQL HA واقعی، Secret Store، داده سازمانی و آزمون چند Node |

> گزارش تحلیل ضعف‌ها، اصلاحات انجام‌شده و تطبیق با نقشهٔ راه: [docs/analysis-report-fa.md](docs/analysis-report-fa.md)

## معماری

    Crm.Web              MVC + Razor + HTMX + CSS/JS
        ↓
    Crm.Application      Use case، DTO و Port
        ↓
    Crm.Domain           Entity، State و Business Rule

    Crm.Infrastructure   In-Memory/EF Core adapters و Access Snapshot توزیع‌پذیر

جهت وابستگی با یک Console Test کنترل می‌شود. Web فقط از پکیج رسمی OpenID Connect مایکروسافت استفاده می‌کند؛ Tailwind برای فرایند ساخت اختیاری CSS است و فایل CSS آماده نیز در مخزن وجود دارد.

## قابلیت‌های نمونه

- انتخاب‌پذیری Demo/OIDC از Configuration و OIDC Code Flow + PKCE
- Cookie حداقلی بدون Role/Permission/Scope
- نشست سمت سرور با Idle ۳۰ دقیقه، Absolute ۸ ساعت و سقف ۳ نشست
- اتصال هویت خارجی با کلید `Issuer + Subject` و Binding یکتای Pending User
- مدیریت وضعیت کاربر، نقش/Scope، نشست، هویت خارجی و Audit
- Access Snapshot پنج‌دقیقه‌ای با Invalidaton فوری و SecurityVersion
- Organization Context سمت سرور با انتخاب Company/Branch/Territory در هر نشست
- پنل مدیریت Company/Region/Branch/SalesTeam/Territory با تاریخچه و Concurrency
- SQL Server/EF Core، Migration اولیه، Model Snapshot و Seed نمونه
- Distributed Cache قابل انتخاب بین Memory و Redis
- Permission + Scope همان Role Assignment و جلوگیری از گسترش ناخواسته دامنه
- Resource Scope واقعی روی Query و Mutation همه ماژول‌های نمونه
- Rate Limit ورود، Data Protection قابل پیکربندی و Security Headers
- Dashboard عملیاتی فارسی و RTL
- Customer Master شامل Contact، Address، مالکیت و Timeline
- Customer 360 با منبع/تازگی داده و Lazy Load بخش‌های سنگین
- Data Quality Dashboard و Duplicate Check/Review کنترل‌شده
- Lead Management با امتیاز، مالک مرجع، SLA تماس، تاریخچه و تبدیل اتمیک
- Opportunity Pipeline شش‌مرحله‌ای با ارزش وزنی، ریسک، Activity و Next Action
- پیشنهاد قیمت ردیفی با Snapshot قیمت ERP Mock، محاسبه Margin و Revision
- تأیید تخفیف سرپرست/تجاری یا تصمیم مشترک مدیر فروش و مالی
- درخواست سفارش از Quote پذیرفته‌شده با کنترل اعتبار حسابداری
- ارسال Idempotent به ERP Mock، Retry/Backoff، Dead Letter، Correlation و Attempt History
- Projection مرحله‌ای تخصیص، تحویل، فاکتور و پرداخت در CRM
- مدیریت نماینده با Scope مستقل Dealer، قرارداد و Territory کنترل‌شده
- هدف فروش CRM-owned و Projection فقط‌خواندنی مالی/عملکرد از Adapterهای نمونه
- انتقال و بازگردانی رابطه نماینده-مشتری در Merge/Unmerge
- خدمات و SLA: صف پرونده، تریاژ، SLA پاسخ و حل با قاعدهٔ توقف، ارجاع خودکار به کارتابل، علت ریشه‌ای/اقدام اصلاحی و رضایت مشتری
- کارتابل اقدامات
- صفحه خودکاربر برای مشاهده نشست‌ها و ابطال سایر نشست‌ها
- فرم‌های Partial در Drawer با HTMX محلی، fallback عادی، Anti-forgery و مدیریت پاسخ 422
- Correlation ID و Health Check
- تم روشن/تیره و رابط Responsive

## اجرا

برای نمایش فوری و بدون نصب، فایل **`index.html`** در ریشه بسته را با Chrome یا Edge باز کنید. همان فایل در `preview/crm-unified.html` نیز قرار دارد. ورود، منوی مشترک و صفحات مشتری، سرنخ، فرصت، پیشنهاد، سفارش، نماینده، کارتابل، گزارش، هویت و سازمان در همین فایل اجرا می‌شوند. نام کاربری `sales.manager` و رمز نمونه `Demo@1405` است. اگر نمایشگر فایل داخل گفتگو فرم‌ها را اجرا نمی‌کند، ابتدا HTML را دانلود و در مرورگر باز کنید.

این پیش‌نمایش با داده محلی کار می‌کند. برای اجرای واقعی ASP.NET Core MVC و کنترل دسترسی سمت سرور، مراحل زیر را انجام دهید.

پیش‌نیاز: .NET SDK 10.

    dotnet dev-certs https --trust
    dotnet restore EnterpriseCrm.sln
    dotnet run --project src/Crm.Web

سپس آدرس https://localhost:7141 را باز کنید.

ورود نمونه:

    نام کاربری: sales.manager
    رمز: Demo@1405

کاربر مدیر به دو شرکت نمونه دسترسی دارد و پس از ورود باید محیط کاری را انتخاب کند. برای مشاهده ورود خودکار کاربر تک‌شرکتی/تک‌شعبه‌ای از `sales.expert` با همان رمز نمونه استفاده کنید. تأیید دوم پیشنهادهای سطح مشترک با کاربر `finance.manager` انجام می‌شود. مدیریت شبکه نمایندگان با `channel.manager` و نمای محدود دقیق یک نماینده با `dealer.user` در دسترس است؛ رمز همهٔ کاربران نمونه `Demo@1405` است.

### فعال‌کردن SQL Server و Redis

مقادیر اتصال را فقط از Environment یا Secret Store تأمین کنید:

    Persistence__Mode=SqlServer
    Persistence__AutoMigrate=false
    ConnectionStrings__CrmDatabase=<from-secret-store>
    Cache__Mode=Redis
    ConnectionStrings__Redis=<from-secret-store>

حالت Demo فقط در محیط Development اجرا می‌شود؛ برای محیط آزمون مجزا باید `Authentication__AllowDemoOutsideDevelopment=true` صریحاً تنظیم شود.

در توسعهٔ خالی می‌توان AutoMigrate را موقتاً فعال کرد. در Production از اسکریپت Idempotent و Runbook `docs/runbooks/sqlserver-migration-and-rollback-fa.md` استفاده کنید.

ورودی واحد پیش‌نمایش بدون نصب .NET:

    index.html

فایل‌های پیش‌نمایش جداگانه مراحل قبل برای سوابق نگه داشته شده‌اند؛ ورودی جاری سامانه `index.html` یا `preview/crm-unified.html` است. بازسازی و آزمون منطق پیش‌نمایش:

    node scripts/build-unified-preview.mjs
    node scripts/test-unified-preview.mjs

این آزمون JavaScript جایگزین تست بصری مرورگر نیست. Gate مرورگر پروژه، ناوبری، بازخوانی، دانلود و نمای موبایل این فایل را نیز کنترل می‌کند.

### فعال‌کردن OIDC

مقادیر واقعی را از Secret Store/Environment تأمین کنید و هیچ Client Secret واقعی را در فایل‌ها ننویسید:

    Authentication__Mode=Oidc
    Authentication__Oidc__Authority=https://idp.example/tenant/v2.0
    Authentication__Oidc__ClientId=crm-client
    Authentication__Oidc__ClientSecret=<from-secret-store>
    Security__IpHashSalt=<from-secret-store>
    DataProtection__KeyRingPath=/protected/shared/crm-keyring

در استقرار پشت Load Balancer، `ReverseProxy__Enabled=true` و IPهای دقیق Proxy در `ReverseProxy__KnownProxies__0...` تنظیم شوند. برنامه بدون Known Proxy صریح شروع نمی‌شود.

Redirect URI برابر `/signin-oidc` و Post Logout Redirect باید در IdP ثبت شوند.

## کنترل‌ها

    node scripts/verify-structure.mjs
    node --check src/Crm.Web/wwwroot/js/site.js
    node scripts/scan-repository-secrets.mjs
    dotnet build EnterpriseCrm.sln --configuration Release
    dotnet run --project tests/Crm.ArchitectureTests --configuration Release
    dotnet run --project tests/Crm.WebTests --configuration Release
    CRM_REQUIRE_SQLSERVER=true CRM_TEST_ROLLBACK=true CRM_TEST_SQLSERVER=<test-only-connection> dotnet run --project tests/Crm.PersistenceTests --configuration Release
    dotnet run --project tests/Crm.BrowserTests --configuration Release -- install chromium
    CRM_E2E_BASE_URL=http://localhost:5085 dotnet run --project tests/Crm.BrowserTests --configuration Release

Quality Gate تجمعی جاری در `.github/workflows/priority10-quality-gate.yml` برای .NET 10، SQL Server 2022 و Chromium تعریف شده است. نتایج واقعی و موارد اجرا‌نشده در [گزارش تحویل اولویت ۱۰](docs/priority-10-delivery-fa.md) ثبت شده‌اند؛ تعریف Workflow به معنی اجرای موفق آن نیست. گزارش‌های اولویت‌های قبل، سابقهٔ همان تحویل‌ها هستند.

## اولویت ۱۰ — پرتال و بازدید موبایل

- `dealer.user` با رمز نمونه `Demo@1405`: ورود مستقیم به `/portal`؛ کاتالوگ و موجودی مجاز، حساب و فاکتور، درخواست سفارش، سرنخ، شکایت، Claim و مدیریت درخواست دسترسی.
- `sales.manager`: مسیر `/partner-requests` برای بررسی و `/mobile` برای بازدید؛ `sales.expert` بازدیدهای خودش در شعبهٔ مجاز را می‌بیند.
- `channel.manager`: رسیدگی اولیه به درخواست‌ها؛ مجوز ایجاد سرنخ یا مدیریت هویت از مجوز رسیدگی به دست نمی‌آید.
- تکمیل بازدید با Customer 360 مرتبط است؛ Merge/Unmerge روابط پرتال و موبایل را هم منتقل و نسخهٔ آن‌ها را تغییر می‌دهد.
- صف آفلاین MVC فقط در حافظهٔ همان صفحه است: ۵۰ عملیات / ۸ ساعت؛ ارسال ترتیبی، رسید تکرارناپذیر و توقف روی تعارض. بازکردن مجدد صفحه آفلاین یا نصب اپ بومی جزو این نمونه نیست.
- پیش‌نمایش همان `index.html` است؛ برای پرتال با `dealer.user` و برای بررسی با `sales.manager` وارد شوید. تغییر نقش با خروج و ورود انجام می‌شود. دادهٔ ذخیره‌شده در مرورگر صرفاً ساختگی است و امنیت واقعی از backend MVC تأمین می‌شود.

مستندات: [تحلیل و مدل](docs/priority-10-implementation-fa.md)، [آزمون‌ها](docs/priority-10-quality-gate-fa.md)، [راهنمای اجرا و migration](docs/runbooks/portal-mobile-deployment-fa.md).

    node scripts/build-self-service-sql.mjs
    node scripts/build-unified-preview.mjs
    node scripts/test-unified-preview.mjs
    node scripts/test-mobile-outbox.mjs

## اولویت ۹ — گزارش‌ها و تحلیل

مسیر `/reports` شامل نماهای مدیرعامل، فروش، منطقه، شعبه، مالی و نمایندگان است. نمای انتخابی فقط چیدمان است و مجوزها را افزایش نمی‌دهد. کاربر نمونه `reporting.ceo` با رمز `Demo@1405` به داشبورد مدیرعامل فقط‌خواندنی دسترسی دارد. `sales.expert` گزارش شعبه‌ای بدون Export، `sales.supervisor` گزارش و Export در شعبه، `finance.manager` گزارش وصول و `channel.manager` عملکرد شبکه را می‌بینند.

این مرحله تغییر Schema ندارد؛ گزارش‌ها خواندنی‌اند و Audit خروجی در جدول موجود ثبت می‌شود. فایل JSON نسخه ۱ قرارداد نمونهٔ BI است و اتصال زنده به Warehouse، تاریخچه As-of و ETL سازمانی محسوب نمی‌شود. برای دیتابیس نمونهٔ از قبل ایجادشده، کاربر Executive خودکار اضافه نمی‌شود؛ نقش را از مدیریت هویت به کاربر آزمایشی موجود تخصیص دهید یا از Seed دیتابیس آزمایشی جدید استفاده کنید. هیچ Seed مجددی روی دیتابیس کاری انجام ندهید.

ساخت اختیاری Tailwind:

    cd src/Crm.Web
    npm install
    npm run css:build

جزئیات Identity در `docs/priority-2-implementation-fa.md`، Organization Context در `docs/priority-3-implementation-fa.md`، Customer 360 در `docs/priority-4-implementation-fa.md`، Sales Pipeline در `docs/priority-5-implementation-fa.md`، Quote Governance در `docs/priority-6-implementation-fa.md`، Order/ERP در `docs/priority-7-implementation-fa.md` و Dealer Governance در `docs/priority-8-implementation-fa.md` آمده است.

## قابلیت‌های تکمیلی اولویت ۸

- Dealer Master با شناسه پایدار، Company/Branch/Territory و مدیر کانال؛
- Scope مستقل `Dealer` برای کاربر نماینده و جلوگیری از Enumeration سایر نمایندگان؛
- قرارداد و Territory با درخواست/تأیید جدا، Actor/Reason و کنترل انحصار هم‌پوشان؛
- فعال‌سازی فقط پس از قرارداد و Territory فعال؛
- سبد مشتری نماینده و اتصال آن به Customer 360 و Merge/Unmerge؛
- پایان کنترل‌شدهٔ رابطه مشتری و بستن اتمیک روابط فعال هنگام خاتمه نماینده؛
- هدف دوره با Optimistic Concurrency و Snapshot عملکرد ERP/BI؛
- Projection مالی Accounting با Mask مجوزی و Freshness؛
- فرم‌های اجرایی Razor/HTMX، Migration شماره `202609270007`، SQLهای DBA و Quality Gate تجمعی.

## قابلیت‌های تکمیلی اولویت ۷

- تبدیل یکتای Quote پذیرفته‌شده به Order Request؛
- Snapshot اعتبار از Accounting Port و تصمیم Approved/Held؛
- Override مالی زمان‌دار با دلیل و Audit؛
- Outbox با Idempotency Key، CorrelationId و Payload Fingerprint؛
- Retry/Backoff، AwaitingExternal، Dead Letter و تاریخچه Attempt؛
- Projection خواندنی ERP از Accepted تا Paid؛
- Order در Customer 360 و Manifest امن Merge/Unmerge؛
- Migration شماره `202609260006`، اسکریپت DBA، Runbook و Quality Gate تجمعی.

## قابلیت‌های تکمیلی اولویت ۶

- Quote Line با Snapshot کالا، قیمت، Cost، منبع و تاریخ مؤثر ERP Mock؛
- محاسبه خودکار Gross، Discount، Net و Margin با Decimalهای مالی؛
- ماتریس اختیار ۵/۸/۱۲/۲۰ درصد و حداقل Margin متناظر؛
- Approval مستقل سرپرست، مدیر تجاری و تأیید مشترک فروش/مالی؛
- State Machine، Status History، Actor/Reason و Optimistic Concurrency؛
- Revision با Parent Quote و جلوگیری از تغییر نسخه غیر Draft؛
- وابستگی Won شدن Opportunity به Quote پذیرفته‌شده همان فرصت؛
- فرم‌های داخلی Razor/HTMX برای پیش‌نویس و ردیف‌ها با fallback عادی؛
- Migration شماره `202609260005`، SQLهای Idempotent/Rollback و Quality Gate تجمعی.

## قابلیت‌های تکمیلی اولویت ۵

- وضعیت‌های کامل Lead با شرط انتقال، دلیل، SLA اولین تماس و تاریخچه تغییر؛
- Deduplication ورودی و Lead Scoring قابل تکرار در نمونه؛
- Permission + Scope + Ownership و دید نظارتی مدیر/سرپرست؛
- تبدیل Qualified Lead به Customer/Contact/Opportunity همراه Lineage و Customer Timeline؛
- Pipeline از Identified تا Won/Lost، Probability مرحله، ارزش وزنی، Aging و هشدار Next Action؛
- Opportunity Stage History، Activity، Risk، Competitor، Close Reason و Concurrency؛
- صفحات Details و فرم‌های داخلی اجرایی برای Lead و Opportunity؛
- HTMX 2.0.10 محلی برای حذف وابستگی runtime به CDN؛
- Migration شماره `202609220004` و SQLهای idempotent/rollback قفل‌شده.

## قابلیت‌های تکمیلی اولویت ۴

- `CustomerId` مرجع در Lead، Opportunity و Quote و `OpportunityId` در Quote
- تبدیل Lead همراه با ایجاد/اتصال Customer و حفظ زنجیره شناسه‌ها
- Merge با Dry Run، انتخاب Survivor، انتقال روابط، کنترل همزمانی و Audit Manifest
- جلوگیری از Merge تکراری/زنجیره‌ای، نمایش وضعیت ادغام‌شده و مدیریت تماس/آدرس اصلی متعارض
- Unmerge کنترل‌شده با بازیابی Primaryها تا زمانی که روابط منتقل‌شده بعداً تغییر نکرده باشند
- جست‌وجو و Pagination پایدار سمت سرور؛ در SQL Server با `IQueryable`، `Skip/Take` و Tie-breaker شناسه
- فیلتر HTMX فرصت‌های Quote براساس CustomerId و بازآوری HTMX تاریخچه Merge
- Migration شماره `202609210003` و اسکریپت‌های استقرار idempotent و rollback قفل‌شده

## محدودیت آگاهانه نسخه نمونه

- فرمان‌های نمونه هنوز از DataStore سازگار استفاده می‌کنند؛ فهرست Customer مسیر Query اختصاصی SQL دارد.
- OIDC در کد آماده است، اما Authority/Client واقعی، MFA و Secret Store وابسته به محیط سازمان‌اند.
- ERP و Accounting با Adapter نمونه اجرا می‌شوند؛ اتصال واقعی، Broker و Reconciliation محیط Production لازم است.
- Restore خودکار در صورت تغییر روابط پس از Merge عمداً متوقف می‌شود و نیاز به بررسی Steward دارد.
- وزن‌های Match و Data Quality نمونه‌اند و باید با داده واقعی کالیبره شوند.
- در حالت InMemory، Session/Audit بازنشانی می‌شوند؛ در حالت SQL Server پایدارند. Append-only سخت‌گیرانه و WORM وابسته به زیرساخت تولید است.
- Load Test واقعی انجام نشده و SLAهای سند مرحله صفر Performance Budget هستند.

Runbookهای Migration/بازگشت SQL Server، Cache توزیع‌شده، اختلال IdP، چرخش Credential، ابطال Session و Shared Key Ring در `docs/runbooks` قرار دارند. چک‌لیست اتصال Provider آزمایشی نیز در `docs/test-oidc-environment-fa.md` است.
