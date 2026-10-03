# Runbook استقرار و بازگشت اولویت ۷ — Order/ERP Integration

## پیش‌شرط

1. Backup بازیابی‌پذیر و زمان بازگشت تأیید شود.
2. Workflow تجمعی `priority9-quality-gate.yml` روی Commit هدف سبز باشد؛ این Workflow کنترل‌های اولویت ۷ و ۸ را نیز اجرا می‌کند.
3. Migration `202609260005_QuotePricingGovernance` نصب باشد.
4. Endpoint، Timeout، Idempotency Contract و Mapping مشتری ERP در محیط هدف تأیید شوند.
5. نقش `FinanceManager` و Scope شرکت‌ها Provision شده باشد.
6. Worker واقعی تا زمان تأیید Smoke Test خاموش بماند.

## استقرار

روش استاندارد:

```bash
dotnet ef database update --project src/Crm.Infrastructure --startup-project src/Crm.Web
```

روش DBA پس از بازبینی:

```text
scripts/sql/priority7-order-integration-idempotent.sql
```

اسکریپت اگر Baseline 006 ثبت شده باشد بدون تغییر پایان می‌یابد؛ در Schema نیمه‌اعمال‌شده Fail-fast می‌کند.

## کنترل پس از استقرار

1. Migration 006 و پنج جدول Order/Integration وجود داشته باشند.
2. Unique Indexهای QuoteId، IdempotencyKey و `(MessageId, AttemptNumber)` فعال باشند.
3. Quote پذیرفته‌شده فقط یک Order Request ایجاد کند.
4. مشتری بدهکار به CreditHold برود و کارتابل مالی ساخته شود.
5. Override مالی فقط با دلیل و انقضا اجرا شود.
6. یک Transient Failure به RetryScheduled برود و کلید پیام ثابت بماند.
7. پاسخ Accepted شماره ERP بدهد و حرکت مرحله‌ای تا Paid ممکن باشد.
8. CorrelationId در Log درخواست و رکورد Integration یکسان باشد.

## پایش

- شمار `RetryScheduled` و `DeadLetter`؛
- سن قدیمی‌ترین پیام Pending/AwaitingExternal؛
- نرخ پذیرش/رد ERP و Latency؛
- سفارش CreditHold با مجوز منقضی؛
- اختلاف Projection CRM با Reconciliation ERP.

## پاسخ به خرابی

| نشانه | اقدام |
|---|---|
| Timeout ERP | پیام جدید نسازید؛ Retry همان Idempotency Key را بررسی کنید |
| رشد Dead Letter | Worker را محدود، Endpoint و Mapping را بررسی و سپس Requeue کنترل‌شده طراحی کنید |
| Duplicate در ERP | ارسال را متوقف و Contract Idempotency سمت ERP را بررسی کنید |
| Override نامعتبر | Scope/Permission و انقضای مجوز را بررسی کنید؛ رکورد Audit را حذف نکنید |
| Projection ناهماهنگ | CRM را دستی ویرایش نکنید؛ Reconciliation از Source of Truth اجرا شود |

## Rollback

Rollback جدول‌ها و تمام داده‌های اولویت ۷ را حذف می‌کند. گزینه ترجیحی Roll-forward و خاموش‌کردن Worker است. فایل `priority7-order-integration-rollback.sql` با `@ConfirmDestructiveRollback = 0` قفل است؛ فقط پس از Backup، خروجی‌گیری Audit و تأیید DBA/مالک داده مقدار ۱ شود.

پس از Rollback، نسخه برنامه به اولویت ۶ برگردد و وجود Migration 005 و جدول‌های Quote کنترل شود.
