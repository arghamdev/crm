# Runbook استقرار و بازگشت اولویت ۶ — Quote/Pricing

## پیش‌شرط

1. از پایگاه داده Backup قابل بازیابی گرفته شود.
2. Workflow `priority6-quality-gate.yml` روی Commit هدف سبز باشد.
3. مالک کسب‌وکار ماتریس تخفیف ۵/۸/۱۲/۲۰ و Margin ۲۵/۲۲/۱۸/۱۵ را تأیید کند.
4. کاربر یا Role متناظر `FinanceManager` با Scope شرکت هدف Provision شده باشد.
5. نسخه برنامه و Migration در یک Change Window منتشر شوند.

## استقرار استاندارد

در Pipeline از EF Migration استفاده شود:

```bash
dotnet ef database update --project src/Crm.Infrastructure --startup-project src/Crm.Web
```

برای محیطی که Migration Bundle ندارد، پس از بازبینی DBA اسکریپت زیر اجرا شود:

```text
scripts/sql/priority6-quote-pricing-idempotent.sql
```

اسکریپت فقط روی Baseline اولویت ۵ اجرا می‌شود و اگر بخشی از Schema اولویت ۶ از قبل وجود داشته باشد Fail-fast می‌کند تا حالت نیمه‌اعمال‌شده پنهان نماند.

## کنترل پس از استقرار

1. Migration `202609260005_QuotePricingGovernance` در `__EFMigrationsHistory` وجود دارد.
2. جدول‌های `commercial.QuoteLines`، `commercial.QuoteStatusHistory` و `commercial.QuoteApprovalDecisions` وجود دارند.
3. Quoteهای قدیمی یک ردیف `LEGACY` و حداقل یک Status History دارند.
4. مدیر فروش بتواند Quote با تخفیف ۱۵٪ بسازد و Submit کند.
5. مدیر مالی فقط مرحله مالی همان Quote را تأیید کند.
6. پس از دو تأیید، Send و Accept موفق باشد.
7. Opportunity مرتبط فقط پس از Quote پذیرفته‌شده به Won منتقل شود.
8. Logها برای خطای FK، Concurrency و پاسخ 5xx بررسی شوند.

## Rollback تصمیمی

Rollback Schema داده‌های Line، Decision و History را حذف می‌کند و فقط با Backup و تأیید DBA/مالک داده مجاز است. گزینه ترجیحی در Production، Roll-forward و خاموش‌کردن قابلیت از مسیر Release است.

فایل `scripts/sql/priority6-quote-pricing-rollback.sql` با متغیر `@ConfirmDestructiveRollback = 0` قفل شده است. فقط پس از تأیید رسمی مقدار ۱ شود.

پس از Rollback:

- نسخه برنامه نیز به Release اولویت ۵ بازگردد؛
- وجود Migration 005 و ستون‌های `ApprovalLevel`/`Revision` کنترل شود که حذف شده باشند؛
- Migration 004 و داده‌های Pipeline بدون تغییر باقی مانده باشند؛
- در صورت نیاز به داده تصمیم‌های حذف‌شده، Backup بازیابی شود.

## پاسخ به خرابی

| نشانه | اقدام اولیه |
|---|---|
| Migration نیمه‌کاره | توقف Rollout، بررسی Transaction و اجرای مجدد فقط پس از تطبیق Schema |
| Finance approval قابل اجرا نیست | Role/Scope و Cache دسترسی کاربر مالی بررسی و Snapshot باطل شود |
| مبلغ قدیمی نادرست | Backfill ردیف `LEGACY` و Gross/Cost بررسی شود؛ تصمیم خودکار اصلاح داده اجرا نشود |
| Conflict زیاد | نسخه صفحه و `ExpectedVersion` بررسی؛ از دورزدن Concurrency خودداری شود |
| Quote بدون History | Rollout متوقف و Backfill/Audit قبل از ادامه اصلاح شود |
