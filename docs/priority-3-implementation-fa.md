# گزارش تحلیل و پیاده‌سازی اولویت ۳ — Organization Context

**وضعیت:** پیاده‌سازی فنی نمونه تکمیل؛ Build باینری وابسته به CI دارای .NET 10  
**داده:** حالت پیش‌فرض In-Memory؛ حالت اختیاری SQL Server/EF Core با Migration و Seed نمونه

## نتیجه بررسی قبل از اجرا

نسخه اولویت ۲ قرارداد `SelectedCompanyId` را داشت، اما Context واقعی نبود: مقدار شرکت از اولین Scope حدس زده می‌شد، Company/Branch رابطه دامنه‌ای نداشتند، Permission در سطح کاربر Union می‌شد و فقط Customer محدودسازی داده داشت. این وضعیت برای کاربر چندشرکتی و Object-level Authorization کافی نبود.

## موارد اجراشده

| الزام | پیاده‌سازی |
|---|---|
| مدل سازمان | Company، OrganizationUnit و Territory با Status، Parent و Dimension |
| Context سمت سرور | Company/Branch/Territory در `UserSession`؛ بدون Claim مجوز در Cookie |
| کاربر چندشرکتی | Redirect بعد از Login به `/context/select` |
| کاربر تک‌شرکتی | انتخاب خودکار Company و در صورت یکتایی Branch |
| Selector | صفحه Razor و Partial Drawer با HTMX و fallback استاندارد MVC |
| اعتبارسنجی | بررسی دوباره Company–Branch–Territory سمت سرور و رد Tampering |
| Access Snapshot | Permissionهای تفکیک‌شده به تفکیک Company و PermissionScopeGrant |
| جلوگیری از Scope Union | Permission فقط با Scope همان Role Assignment معتبر است |
| Query Scope | Customer، Lead، Opportunity، Quote، Dashboard و WorkQueue |
| Mutation Scope | Create، Convert، Advance، Approve و Complete با کنترل Context |
| IDOR | شناسه رکورد خارج از Company جاری قابل مشاهده یا تغییر نیست |
| فرم‌ها | Branch Options از Context جاری و نه فهرست Hard-coded |
| Audit | `UserContext.CompanyChanged` با Session و Correlation ID |
| مدیریت دسترسی | CompanyId در Assignment و Scopeهای Company/Branch/Territory واقعی |
| Responsive | Selector نوار بالا در Desktop و دکمه قابل دسترس در Mobile |
| مدیریت ساختار | صفحه `/organization` برای Company، Region، Branch، SalesTeam و Territory |
| فرم‌های مدیریتی | Razor Partial در Drawer، HTMX، Anti-forgery، پاسخ 422 و fallback استاندارد MVC |
| قواعد سلسله‌مراتب | کنترل والد، منع چرخه، منع والد بین‌شرکتی و جلوگیری از غیرفعال‌سازی دارای وابستگی فعال |
| اعتبار و همزمانی | بازه زمانی Territory و `Version` به‌عنوان Concurrency Token |
| تاریخچه تغییر | `OrganizationChange` با Before/After، Actor، زمان و Correlation ID |
| ماندگاری | `CrmDbContext`، SQL Server Provider، Initial Migration، Snapshot و Seed آزمایشی |
| Cache | `IDistributedCache`؛ Memory برای نمونه و Redis قابل تنظیم برای چند Node |
| CI پایگاه داده | SQL Server 2022 Service و تست Migration/Seed/Write/Read |

## داده نمونه

- C01 / P-C01: سه شعبه، دو Territory و چهار مشتری.
- C02 / P-C02: یک شعبه، یک Territory و دو مشتری.
- `sales.manager`: مدیر هر دو شرکت و نیازمند انتخاب Context پس از ورود.
- `sales.expert`: کارشناس C01/B01 با Context خودکار.

## تست‌های اضافه‌شده

- انتخاب اجباری برای کاربر چندشرکتی؛
- انتخاب خودکار برای کاربر تک‌شرکتی و تک‌شعبه‌ای؛
- شمارش شرکت‌های مجاز؛
- رد Branch متعلق به Company دیگر؛
- جداسازی Customerهای C01 و C02؛
- جلوگیری از IDOR با شناسه Customer شرکت دیگر؛
- عدم گسترش SalesExpert@B01 توسط CompanyMember@C01؛
- Audit تغییر Context؛
- Redirect ورود، Anti-forgery و Shared Session در دو Node.

## کنترل‌های قابل اجرا

```bash
node scripts/verify-structure.mjs
node scripts/scan-repository-secrets.mjs
node --check src/Crm.Web/wwwroot/js/site.js
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln --configuration Release
dotnet run --project tests/Crm.ArchitectureTests --configuration Release
dotnet run --project tests/Crm.WebTests --configuration Release
CRM_TEST_SQLSERVER='...' dotnet run --project tests/Crm.PersistenceTests --configuration Release
```

## محدودیت محیط فعلی

.NET SDK در محیط فعلی نصب نیست؛ بنابراین کنترل ساختاری، syntax جاوااسکریپت، JSON و Secret Scan محلی اجرا می‌شوند، اما Build و اجرای Testهای C# باید در CI دارای .NET 10 انجام شوند. این محدودیت به‌صورت شفاف مانع اعلام «تست باینری سبز» است، نه مانع تحویل کد و سناریوهای تست.

## موارد باقی‌مانده پیش از Production

- اجرای Build و Testهای C# در CI و رفع هر خطای احتمالی ناشی از نبود SDK محلی؛
- اعتبارسنجی Migration روی Restore واقعی Backup و اجرای اسکریپت Idempotent؛
- چارت واقعی Company/Region/Branch/Team و قواعد Territory؛
- سیاست مشتری ملی، Dealer، Delegation و انتقال مالکیت؛
- Redis، SQL Server HA و Secret Store واقعی؛
- تست Load، Failover و چند Node روی زیرساخت واقعی؛
- OIDC، MFA، Shared Key Ring و Secret Store واقعی مطابق خروجی اولویت ۲.

## تصمیم اجرایی Persistence

`Persistence:Mode=InMemory` اجرای سریع و تکرارپذیر Demo را حفظ می‌کند. با `Persistence:Mode=SqlServer`، همان قرارداد Application از `EfCoreCrmDataStore` استفاده می‌کند. این Adapter برای اندازه نمونه همه Aggregateها را در هر عملیات بارگذاری می‌کند؛ بنابراین مرز انتقالی است و برای حجم واقعی باید به Query/Repositoryهای Use-case محور تبدیل شود. این محدودیت پنهان نشده و در Runbook Migration ثبت شده است.
