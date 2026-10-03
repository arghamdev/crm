# گزارش تحلیل و پیاده‌سازی اولویت ۵ — Lead و Opportunity Pipeline

**وضعیت:** پیاده‌سازی نمونه و Quality Gate تکمیل؛ اجرای نهایی workflow در مخزن متصل، تأیید بیرونی آن است.  
**دامنه:** Lead Management، SLA اولین تماس، مالکیت، تبدیل کنترل‌شده، Opportunity Pipeline، Stage History، Activity و Next Action.

## جمع‌بندی تحلیل

نسخه قبلی فقط ثبت/تبدیل ساده Lead و انتقال خطی Opportunity داشت. مالک کاربر مرجع نبود، SLA و تاریخچه وجود نداشت، مرحله‌ها ناقص بودند و فرم‌های جزئیات endpoint اجرایی نداشتند. همچنین HTMX از CDN دریافت می‌شد و در محیط محدود باعث تأخیر یا بی‌اثرشدن تعامل‌های داخلی می‌شد.

اولویت ۵ براساس چرخه‌های مصوب نمونه اجرا شد:

- Lead: `New → Assigned → Contacted → Qualified → Converted` با خروجی‌های `Nurture / Disqualified / Duplicate / Invalid`؛
- Opportunity: `Identified → Discovery → Qualified → SolutionOffer → Negotiation → Commit → Won/Lost`؛
- Quote، قیمت و ماتریس تخفیف عمداً در اولویت بعدی باقی ماند تا تصمیم‌های قیمت و اختیار تأیید با Pipeline مخلوط نشود.

## خروجی‌های اجراشده

| لایه | خروجی |
|---|---|
| Domain | State rule کامل Lead/Opportunity، OwnerUserId، SLA، Next Action، Risk، Close Reason و ارتباط مبدأ Lead |
| Application | `SalesPipelineService`، DTO/Commandهای جزئیات، فیلتر، تخصیص، انتقال، تبدیل، فعالیت و KPI |
| Security | Permissionهای ریزدانه، Scope شرکت/شعبه/قلمرو و Ownership برای کارشناس؛ دید نظارتی مدیر/سرپرست |
| Persistence | سه جدول تاریخچه/فعالیت، Mapping، Seed، Snapshot، Migration `202609220004` و SQLهای apply/rollback |
| MVC/Razor | Lead List/Details و Pipeline/Opportunity Details به همراه همه فرم‌های داخلی |
| HTMX/JS | Partialهای Drawer، 422 Validation، Event Refresh، fallback عادی و HTMX محلی بدون CDN |
| Tests | سناریوهای Lifecycle، Ownership، Lineage، Audit، Web forms، Migration/rollback، Persistence seed و Chromium |

## Quality Gate اجرایی

Workflow تجمعی فعلی در `.github/workflows/priority6-quality-gate.yml` روی Runner لینوکسی این موارد را اجباری می‌کند:

- Restore و Build کامل Solution با .NET 10؛
- کنترل معماری و قواعد Domain؛
- تست یکپارچه MVC/OIDC/HTMX همراه ثبت HTML تشخیصی در صورت شکست؛
- SQL Server 2022 واقعی، اعمال Migrationها، Seed، rollback تا Migration اولویت ۴ و migrate مجدد؛
- مرورگر واقعی Chromium برای ورود، انتخاب Context، ایجاد Lead/Opportunity و ثبت Activity؛
- نگه‌داری Screenshot، Trace و Log به‌عنوان Artifact شکست.

اتصال Persistence فقط باید به پایگاه اختصاصی آزمون با نام شامل `Test` یا `Ci` داده شود؛ در غیر این صورت حذف پایگاه و rollback متوقف می‌شود.

## قواعد Lead

- ثبت حداقل یکی از شخص تماس، تلفن یا ایمیل را می‌خواهد.
- Duplicate باز با تلفن/ایمیل یا نام همسان رد می‌شود.
- امتیاز نمونه از کامل‌بودن راه ارتباطی، منبع، قلمرو و مالک محاسبه می‌شود.
- تخصیص فقط به کاربر فعال دارای نقش فروش مؤثر در شرکت/شعبه مجاز است.
- SLA تماس اول چهار ساعت است؛ وضعیت‌های OnTrack، DueSoon، Overdue و Completed گزارش می‌شوند.
- `Qualified` فقط پس از `Contacted` و با امتیاز حداقل ۶۰ مجاز است.
- خروجی بسته دلیل می‌خواهد و رکورد بسته قابل Mutation نیست.
- Convert فقط از Qualified و با Version جاری انجام می‌شود؛ Customer/Contact/Opportunity، Lineage، Timeline و History در یک Write ساخته می‌شوند.
- اتصال به Customer موجود هم‌شرکتی، هم‌شعبه و مجاز کنترل می‌شود.

## قواعد Opportunity

- ایجاد، Customer فعال، Owner مجاز، ارزش مثبت، Close Date و Next Action آینده می‌خواهد.
- مرحله‌های باز فقط یک گام جابه‌جا می‌شوند؛ Won فقط از Commit و Won/Lost فقط با دلیل مجاز است.
- Probability پیش‌فرض از Stage محاسبه و ارزش وزنی در Pipeline گزارش می‌شود.
- تغییر Stage و Activity به رکورد append-only جداگانه تبدیل می‌شود.
- فرصت باز بدون Next Action گذشته و فرصت بدون فعالیت هفت‌روزه در KPI هشدار داده می‌شود.
- تغییر مالک برای مدیر/سرپرست مجاز و دلیل آن به Activity ممیزی افزوده می‌شود.
- Version در تمام Mutationهای حساس از Lost Update جلوگیری می‌کند.

## ماتریس مجوز نمونه

| نقش | Lead | Opportunity |
|---|---|---|
| SalesManager | مشاهده، ایجاد، تخصیص، تغییر، تبدیل | مشاهده، ایجاد، تغییر، تخصیص، بستن |
| SalesSupervisor | همان مدیر در Scope مؤثر | همان مدیر در Scope مؤثر |
| SalesExpert | مشاهده/تغییر/تبدیل رکوردهای خود | مشاهده/ایجاد/تغییر رکوردهای خود؛ بدون تخصیص و بستن |

داشتن Permission به‌تنهایی کافی نیست؛ `Company + Branch + Territory + Ownership` در سرویس دوباره کنترل می‌شود. همان کنترل برای روابط Lead/Opportunity در Customer 360 و سرویس قدیمی Dashboard/Quote نیز اعمال شده تا مسیر فرعی باعث دورزدن مالکیت نشود.

## رابط و endpointها

- `/leads` فهرست، جست‌وجو، فیلتر وضعیت و KPI SLA؛
- `/leads/{id}` جزئیات، تاریخچه و عملیات مجاز؛
- فرم‌های `/assign`، `/transition` و `/convert` با Anti-forgery و ExpectedVersion؛
- `/opportunities` Kanban شش‌مرحله‌ای، ارزش کل/وزنی و هشدار Aging؛
- `/opportunities/{id}` تاریخچه مرحله، فعالیت، ریسک و Next Action؛
- فرم‌های `/create`، `/edit`، `/assign`، `/move` و `/activities/create`؛
- همه فرم‌ها `method/action` عادی دارند و HTMX تنها Progressive Enhancement است.

فایل `wwwroot/lib/htmx/htmx.min.js` نسخه 2.0.10 داخل برنامه سرو می‌شود؛ وابستگی CDN حذف شده است.

## استقرار داده

Migration `202609220004_SalesPipelineGovernance`:

1. ستون‌های جدید را اضافه و داده قدیمی Owner/Stage/Date را Backfill می‌کند؛
2. نام Stageهای قدیمی `Solution` و `Quote` را به `SolutionOffer` و `Commit` تبدیل می‌کند؛
3. جداول `LeadStatusHistory`، `OpportunityStageHistory` و `OpportunityActivities` را می‌سازد؛
4. Indexهای Queue/Pipeline و Foreign Keyهای User/Lead/Opportunity را اضافه می‌کند.

اسکریپت دستی `priority5-sales-pipeline-idempotent.sql` baseline اولویت ۴ را کنترل و Migration History را ثبت می‌کند. Rollback به‌صورت پیش‌فرض قفل است، زیرا تاریخچه و ستون‌های عملیاتی را حذف می‌کند.

## کنترل‌ها

```bash
node scripts/verify-structure.mjs
node scripts/scan-repository-secrets.mjs
node --check src/Crm.Web/wwwroot/js/site.js
dotnet build EnterpriseCrm.sln --configuration Release
dotnet run --project tests/Crm.ArchitectureTests --configuration Release
dotnet run --project tests/Crm.WebTests --configuration Release
CRM_TEST_SQLSERVER='...' dotnet run --project tests/Crm.PersistenceTests --configuration Release
dotnet run --project tests/Crm.BrowserTests --configuration Release -- install chromium
CRM_E2E_BASE_URL=http://localhost:5085 dotnet run --project tests/Crm.BrowserTests --configuration Release
```

## موارد وابسته به تصمیم سازمانی، نه نقص نمونه

- Assignment خودکار براساس ظرفیت، تعطیلات، Channel و Territory واقعی؛
- Stage قابل پیکربندی و Versioned Workflow؛ در نمونه برای حفظ گزارش تاریخی ثابت است؛
- Line Item محصول، ارز، تخفیف و مبلغ خالص که با Quote/Pricing تکمیل می‌شود؛
- شرط Won مبتنی بر Quote پذیرفته‌شده؛ به اجرای Quote Lifecycle وابسته است؛
- Forecast Accuracy، Pipeline Coverage، Velocity و Slippage سازمانی؛ نیازمند Target، دوره مالی و Snapshot تاریخی است؛
- Notification/Job واقعی برای SLA و Reassignment؛ نمونه KPI و عملیات دستی را فراهم می‌کند.

این موارد مانع نمایش و آزمون نمونه نیستند، اما پیش از Production Ready باید با Process Owner و داده واقعی تثبیت شوند.
