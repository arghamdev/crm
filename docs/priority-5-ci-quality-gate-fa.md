# Quality Gate اولویت ۵

## هدف و وضعیت

این سند سابقه گیت اولویت ۵ را نگه می‌دارد. Workflow تجمعی فعال اکنون `.github/workflows/priority6-quality-gate.yml` است و علاوه بر کنترل‌های زیر، سناریوهای Quote اولویت ۶ را نیز اجرا می‌کند.

## اجزای Gate

| Gate | محیط | معیار موفقیت |
|---|---|---|
| Static | Node.js 22 | ساختار، جهت وابستگی، قرارداد HTMX/JavaScript و اسکن Secret معتبر باشد |
| Build | .NET SDK 10 | کل `EnterpriseCrm.sln` در Release بدون Warning/Error ساخته شود |
| Architecture | In-Memory | مرز لایه‌ها و قواعد Lifecycle/Permission/Closure پاس شود |
| MVC integration | WebApplicationFactory | Login، OIDC Code+PKCE، Anti-forgery، Context، IDOR، Ownership و فرم‌های داخلی پاس شود |
| Persistence | SQL Server 2022 | Migration، Seed، Query، rollback اولویت ۵ و migrate مجدد پاس شود |
| Browser | Chromium | Login، Context selection، HTMX create Lead/Opportunity و Activity پاس شود |

## گاردهای ایمنی پایگاه آزمون

- وقتی `CRM_REQUIRE_SQLSERVER=true` است، نبودن `CRM_TEST_SQLSERVER` فوراً Gate را شکست می‌دهد.
- نام Database باید شامل `Test` یا `Ci` باشد؛ در غیر این صورت عملیات `EnsureDeleted` متوقف می‌شود.
- SQL Server سرویس CI موقت است و Credential آن فقط برای همان Container آزمایشی تعریف شده است.
- `CRM_TEST_ROLLBACK=true` ابتدا همه Migrationها را اعمال می‌کند، سپس با `IMigrator` به `202609210003_CustomerRelationshipsAndMerge` برمی‌گردد، حذف schema اولویت ۵ را بررسی و دوباره به آخرین نسخه migrate می‌کند.

## تشخیص شکست

Artifact جاری با نام `priority6-quality-gate-<run-number>` تا ۱۴ روز نگه‌داری می‌شود و می‌تواند شامل این فایل‌ها باشد:

- HTML صفحه‌ای که assertion تست MVC روی آن شکست خورده است؛
- Screenshot و Playwright Trace تست مرورگر؛
- Log برنامه‌ای که برای Smoke مرورگر اجرا شده است.

OIDC تست از Configuration ثابت درون پردازش استفاده می‌کند و برای Provider جعلی درخواست شبکه نمی‌فرستد.

## اجرای محلی

```bash
node scripts/verify-structure.mjs
node scripts/scan-repository-secrets.mjs
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln --configuration Release --no-restore
dotnet run --project tests/Crm.ArchitectureTests --configuration Release --no-build
dotnet run --project tests/Crm.WebTests --configuration Release --no-build
```

Persistence فقط روی پایگاه اختصاصی و قابل حذف آزمون اجرا شود:

```bash
CRM_REQUIRE_SQLSERVER=true \
CRM_TEST_ROLLBACK=true \
CRM_TEST_SQLSERVER='<test-only-connection-string>' \
dotnet run --project tests/Crm.PersistenceTests --configuration Release --no-build
```

برای مرورگر، ابتدا برنامه روی پورت 5085 اجرا شود:

```bash
dotnet run --project tests/Crm.BrowserTests --configuration Release --no-build -- install chromium
dotnet run --project src/Crm.Web --configuration Release --no-build --no-launch-profile --urls http://localhost:5085
CRM_E2E_BASE_URL=http://localhost:5085 \
dotnet run --project tests/Crm.BrowserTests --configuration Release --no-build
```

## مرز تأیید

وجود Workflow و پاس‌شدن کنترل‌های محلی، پیاده‌سازی Gate را ثابت می‌کند؛ وضعیت سبز نهایی فقط پس از Push به مخزن دارای GitHub Actions و اجرای job با .NET 10، SQL Server و Chromium صادر می‌شود.
