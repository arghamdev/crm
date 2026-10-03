# گزارش تحلیل و پیاده‌سازی اولویت ۴ — Customer 360

**وضعیت:** پیاده‌سازی نمونه تکمیل؛ Build باینری وابسته به CI دارای .NET 10  
**دامنه:** Customer Master، Contact، Address، Ownership، Timeline، Data Quality، CustomerId، Merge/Unmerge و Query صفحه‌بندی‌شده

## نتیجه

مدل تخت Customer به Customer Master قابل حاکمیت گسترش یافت. صفحه Details اکنون نمای ۳۶۰، منبع داده و تازگی ERP را نشان می‌دهد. Timeline و روابط تجاری با HTMX به‌صورت Lazy Load دریافت می‌شوند و صفحه fallback مستقل دارند.

## اجزای اجراشده

| لایه | خروجی |
|---|---|
| Domain | Customer توسعه‌یافته، CustomerId روابط و `CustomerMergeOperation` |
| Application | `Customer360Service`، Match، Quality Score، Merge/Unmerge و Query صفحه‌بندی‌شده |
| Security | Scope روی هر Customer و دو طرف Duplicate؛ Permissionهای Update و MergeReview |
| MVC | Details، Activity، Data Quality، Duplicate Queue، Merge Preview و Merge History |
| HTMX | Duplicate Check، Lazy Activity، فیلتر فرصت Quote، بازآوری Merge History، 422 Validation، Event و HX-Redirect |
| Persistence | DbSet، Mapping، Seed، Migration، Snapshot و SQLهای idempotent/rollback |
| Tests | Architecture، Web integration و SQL Server persistence scenarios |

## قواعد کلیدی

- شناسه ملی یکسان Create/Update را مسدود می‌کند.
- Match احتمالی در Create دلیل Override می‌خواهد.
- انتقال Owner/Branch/Territory دلیل می‌خواهد و تاریخچه جدید می‌سازد.
- Update با Version قدیمی رد می‌شود.
- اطلاعات مالی فقط ERP Projection است.
- تصمیم Confirmed پیش‌نیاز Merge است؛ Merge هیچ رکوردی را حذف نمی‌کند و رکورد دوم را غیرفعال می‌سازد.
- Unmerge فقط Manifest همان عملیات را برمی‌گرداند و در صورت تغییر بعدی رابطه متوقف می‌شود.
- Preview و Merge روی Candidate تأییدنشده، Customer غیرفعال، Merge تکراری یا زنجیره Merge فعال متوقف می‌شوند.
- اگر هر دو Customer تماس/آدرس اصلی داشته باشند، مقدار رکورد مغلوب موقتاً عادی و در Unmerge دقیقاً بازگردانی می‌شود.
- صف Duplicate پس از Merge وضعیت «ادغام‌شده» نشان می‌دهد و عملیات تکراری ارائه نمی‌کند.
- Customer مغلوب تا زمان Unmerge فقط خواندنی است و در فرم Quote/تبدیل Lead قابل انتخاب نیست.
- Customer list در SQL Server با فیلتر Scope، Search، Count و Skip/Take سمت سرور اجرا می‌شود.
- ترتیب Pagination با `CreatedAtUtc + Id` پایدار است و Backfill فقط تطبیق یکتای قطعی را اعمال می‌کند.
- Data Quality و Activity در Company/Branch/Territory جاری محدود می‌شوند.

## نتیجه ممیزی تکمیلی

| شکاف کشف‌شده | اصلاح اعمال‌شده |
|---|---|
| دسترسی مستقیم به Dry Run قبل از Confirm | رد با 422 در سرویس/Controller |
| امکان نمایش دوباره عملیات Merge | وضعیت Active Merge در DTO و صف Duplicate |
| Primary دوگانه Contact/Address | Demote کنترل‌شده و Restore از Manifest |
| Mutation روی Customer مغلوب | فقط‌خواندنی‌شدن تا Unmerge و حذف از انتخاب Quote/Lead |
| Unmerge با Manifest ناقص یا رابطه جابه‌جا | اعتبارسنجی کامل پیش از نخستین Mutation |
| Merge تکراری همزمان | کنترل Version و Unique Filtered Index در SQL Server |
| Pagination ناپایدار در زمان برابر | Tie-breaker قطعی با `Id` |
| Backfill نام‌محور مبهم | تطبیق یکتای شعبه‌محور و توقف در ابهام |
| Opportunity نامرتبط در فرم Quote | endpoint وابسته HTMX براساس CustomerId |

## کنترل‌ها

```bash
node scripts/verify-structure.mjs
node scripts/scan-repository-secrets.mjs
node --check src/Crm.Web/wwwroot/js/site.js
dotnet build EnterpriseCrm.sln --configuration Release
dotnet run --project tests/Crm.ArchitectureTests --configuration Release
dotnet run --project tests/Crm.WebTests --configuration Release
CRM_TEST_SQLSERVER='...' dotnet run --project tests/Crm.PersistenceTests --configuration Release
```

در استقرار دستی، اسکریپت Idempotent نبود baseline معتبر `__EFMigrationsHistory` را رد و پس از موفقیت شناسه Migration را ثبت می‌کند؛ Rollback قفل‌شده همان رکورد را در Transaction حذف می‌کند.

## محدودیت محیط فعلی

.NET SDK در محیط فعلی نصب نیست. کنترل ساختاری، JavaScript، JSON و Secret Scan محلی اجرا می‌شوند؛ Build و تست‌های C# باید در CI دارای .NET 10 اجرا شوند. تا دریافت نتیجه CI، وضعیت «تکمیل فنی نمونه، نیازمند تأیید باینری» است.

## خارج از محدوده نمونه

- اتصال واقعی ERP/MDM؛
- کالیبراسیون Match با داده واقعی؛
- سیاست Retention، Masking و Consent سازمانی.

برای روند عملیاتی صف Duplicate و استقرار به `docs/runbooks/customer-duplicate-review-fa.md` و `docs/runbooks/customer-merge-deployment-fa.md` مراجعه شود.
