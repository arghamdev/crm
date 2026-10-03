# Quality Gate تجمعی اولویت ۷

این Quality Gate اکنون داخل Workflow تجمعی `.github/workflows/priority9-quality-gate.yml` روی Push و Pull Request شاخه‌های `main` و `develop` و به‌صورت دستی اجرا می‌شود.

| لایه | کنترل اجباری |
|---|---|
| Static | ساختار، قرارداد HTMX، JavaScript و اسکن Secret |
| Build | Restore و Build Release روی .NET 10 |
| Domain | State Machine سفارش، اعتبار، Override، Retry، Idempotency و Projection |
| MVC | Login، Scope، Anti-forgery، فرم واقعی Order و fallback غیر HTMX |
| SQL Server | Migration 006، Seed، Model، Rollback تا 005 و Roll-forward |
| Browser | Chromium برای Lead، Opportunity، Quote، Order و Activity |

سناریوی اصلی Architecture Test ابتدا پاسخ ERP را `TransientFailure` می‌کند و سپس بعد از Backoff همان پیام را با همان Idempotency Key به `Accepted` می‌رساند؛ بعد وضعیت تا `Paid` پیش می‌رود.

`CRM_REQUIRE_SQLSERVER=true` از Skip ناخواسته SQL در CI جلوگیری می‌کند و `CRM_TEST_ROLLBACK=true` Down/Up اولویت ۷ را فعال می‌کند. حذف دیتابیس فقط برای نام شامل `Test` یا `Ci` مجاز است.

Artifact تشخیصی `priority9-quality-gate-<run-number>` شامل Log برنامه و در شکست Browser شامل Screenshot، Trace و خطاست. Merge تنها پس از سبزشدن Job تجمعی مجاز است.
