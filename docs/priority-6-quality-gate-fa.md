# Quality Gate تجمعی اولویت ۶

Workflow فعال `.github/workflows/priority6-quality-gate.yml` روی Push و Pull Request شاخه‌های `main` و `develop` و به‌صورت دستی اجرا می‌شود.

| لایه | کنترل اجباری |
|---|---|
| Static | ساختار معماری، قرارداد HTMX، JavaScript و اسکن Secret |
| Build | Restore و Build در Release با .NET 10 |
| Domain | ماتریس اختیار، تأیید مشترک، Audit، Revision و گارد Won |
| MVC | Login، Scope، Anti-forgery، فرم Quote و fallback غیر HTMX |
| SQL Server | Migration 005، Seed، Rollback تا Migration 004 و Roll-forward |
| Browser | Chromium برای Drawerهای Lead، Opportunity، Quote و Activity |

متغیر `CRM_REQUIRE_SQLSERVER=true` مانع Skip شدن ناخواسته تست Persistence در CI می‌شود. `CRM_TEST_ROLLBACK=true` مسیر Down/Up اولویت ۶ را فعال می‌کند. تست حذف دیتابیس فقط روی نامی اجرا می‌شود که شامل `Test` یا `Ci` باشد.

Artifact تشخیصی `priority6-quality-gate-<run-number>` شامل Log برنامه و در شکست Browser شامل Screenshot، Trace و متن خطاست و ۱۴ روز نگه‌داری می‌شود.

موفقیت کنترل‌های محلی جایگزین اجرای CI نیست. Merge زمانی مجاز است که Job تجمعی کامل سبز باشد.
