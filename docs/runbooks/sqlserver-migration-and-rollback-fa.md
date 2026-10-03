# راهنمای اجرای SQL Server، Migration و بازگشت

این راهنما برای حالت نمونهٔ `Persistence:Mode=SqlServer` است. اطلاعات اتصال نباید در مخزن ثبت شود و باید از Secret Store یا متغیر محیطی تزریق شود.

## پیش‌نیاز

- .NET SDK 10 مطابق `global.json`
- SQL Server 2022 یا Azure SQL
- دسترسی حساب Migration به ایجاد Schema، Table و Index
- نسخه پشتیبان تأییدشده قبل از استقرار تولید

## تنظیم امن

```bash
export Persistence__Mode=SqlServer
export Persistence__AutoMigrate=false
export ConnectionStrings__CrmDatabase='Server=...;Database=...;...'
```

در محیط توسعهٔ خالی می‌توان `Persistence__AutoMigrate=true` را موقتاً فعال کرد. در تولید این گزینه باید `false` بماند تا تغییر Schema از Pipeline کنترل‌شده عبور کند.

## تولید اسکریپت Idempotent

```bash
dotnet restore EnterpriseCrm.sln
dotnet ef migrations script --idempotent \
  --project src/Crm.Infrastructure \
  --startup-project src/Crm.Web \
  --output artifacts/sql/enterprise-crm.sql
```

اسکریپت را ابتدا روی Restore آزمایشی آخرین Backup اجرا کنید. نتیجه، زمان قفل، اندازه Log و Queryهای Smoke را ثبت کنید. سپس همان فایل Artifact تأییدشده در تولید اجرا شود.

## کنترل پس از Migration

1. جدول `__EFMigrationsHistory` شامل `202609210001_InitialEnterpriseCrm` باشد.
2. Schemaهای `org`, `crm`, `sales`, `commercial`, `iam` وجود داشته باشند.
3. Endpoint `/health` موفق باشد.
4. ورود مدیر نمونه/آزمایشی، انتخاب شرکت و صفحه `/organization` بررسی شود.
5. ایجاد و ویرایش یک واحد آزمایشی باید رکورد متناظر در `org.OrganizationChanges` بسازد.
6. Migrationهای `202609210003_CustomerRelationshipsAndMerge` و `202609220004_SalesPipelineGovernance` ثبت شده باشند.
7. جدول‌های `sales.LeadStatusHistory`، `sales.OpportunityStageHistory` و `sales.OpportunityActivities` و Indexهای Owner/Stage/SLA وجود داشته باشند.

## Rollback

Migration اولیه عمداً Down دارد، اما اجرای Down در تولید داده‌ها را حذف می‌کند و راه بازگشت ترجیحی نیست. راه بازگشت تولید:

1. ترافیک نسخه جدید متوقف شود.
2. نسخه قبلی برنامه Deploy شود.
3. Backup قبل از Migration روی پایگاه مقصد بازیابی شود.
4. Cache با Prefix برنامه پاک‌سازی شود.
5. ورود، Scope شرکت و IDOR smoke test دوباره اجرا شود.

Down فقط برای پایگاه موقت CI/توسعه مجاز است.

## نکته معماری نمونه

`EfCoreCrmDataStore` برای حفظ قرارداد فعلی نمونه، Aggregateهای کوچک را در هر عملیات بارگذاری می‌کند. برای حجم تولید، مرحله بعدی تبدیل سرویس‌ها به Repository/Query اختصاصی و اجرای Filter شرکت در Query دیتابیس است؛ مرز امنیتی فعلی همچنان در Application Service برقرار است.
