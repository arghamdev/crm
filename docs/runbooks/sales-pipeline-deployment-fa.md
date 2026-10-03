# Runbook استقرار اولویت ۵ — Sales Pipeline

## قبل از اجرا

1. Backup قابل بازیابی و خروجی migration history تهیه شود.
2. Quality Gate تجمعی فایل `.github/workflows/priority6-quality-gate.yml` باید روی commit مورد استقرار سبز باشد.
3. Migration `202609210003_CustomerRelationshipsAndMerge` باید اعمال شده باشد.
4. Ownerهای متنی Lead/Opportunity با DisplayName کاربر فعال مقایسه شوند؛ موارد بدون تطبیق مجازند ولی `OwnerUserId` آن‌ها Null باقی می‌ماند و باید بعداً تخصیص یابند.
5. Stageهای قدیمی خارج از مجموعه `Discovery/Solution/Negotiation/Quote/Won/Lost` گزارش و تعیین تکلیف شوند.
6. ابتدا Restore آخرین Backup در محیط آزمایش مهاجرت داده شود.

## اجرا

مسیر ترجیحی، Artifact خروجی `dotnet ef migrations script --idempotent` است. برای استقرار دستی کنترل‌شده می‌توان از زیر استفاده کرد:

```text
scripts/sql/priority5-sales-pipeline-idempotent.sql
```

اسکریپت در Transaction اجرا، baseline اولویت ۴ را کنترل و شناسه `202609220004_SalesPipelineGovernance` را ثبت می‌کند.

## Smoke test

1. `/health` موفق باشد.
2. مدیر در `/leads` هر سه KPI مالکیت/SLA/امتیاز را ببیند.
3. Lead جدید ثبت و به Owner مجاز تخصیص یابد.
4. انتقال `Assigned → Contacted → Qualified` با History ثبت شود.
5. Convert، Customer/Opportunity و شناسه‌های Lineage را بسازد.
6. Opportunity در Kanban جابه‌جا و StageHistory/Customer Timeline ایجاد شود.
7. Activity و Next Action ذخیره شود.
8. کارشناس نتواند Lead/Opportunity کارشناس دیگر را با URL مستقیم بخواند یا تغییر دهد.
9. مرورگر هیچ درخواست runtime به CDN برای HTMX نداشته باشد.

موارد 2 تا 8 به‌صورت خودکار نیز در Quality Gate با WebApplicationFactory، SQL Server و Chromium کنترل می‌شوند؛ Smoke دستی روی محیط مقصد همچنان لازم است.

## پایش

- نرخ Leadهای SLA گذشته؛
- Opportunityهای بدون فعالیت بیش از هفت روز؛
- Next Action گذشته؛
- خطاهای 409/422 ناشی از Version یا قواعد وضعیت؛
- خطاهای 403/404 روی endpointهای Sales برای بررسی Scope و IDOR؛
- زمان اجرای Queryهای Index جدید با حجم داده شبیه تولید.

## Rollback

فایل `priority5-sales-pipeline-rollback.sql` قفل است. فقط پس از Backup و بررسی اثر، مقدار `@ConfirmDestructiveRollback` به ۱ تغییر کند. Rollback تاریخچه مرحله/فعالیت و ستون‌های SLA/مالکیت را حذف می‌کند؛ برای تولید، Roll-forward یا بازیابی Backup ترجیح دارد.
