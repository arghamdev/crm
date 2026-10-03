# Quality Gate اولویت ۸

## دروازه‌های اجباری

1. `node scripts/verify-structure.mjs`
2. `node scripts/scan-repository-secrets.mjs`
3. `node --check src/Crm.Web/wwwroot/js/site.js`
4. `dotnet restore EnterpriseCrm.sln`
5. `dotnet build EnterpriseCrm.sln -c Release --no-restore`
6. اجرای `Crm.ArchitectureTests`
7. اجرای `Crm.WebTests`
8. اجرای `Crm.PersistenceTests` با SQL Server و `CRM_TEST_ROLLBACK=true`
9. اجرای `Crm.BrowserTests` با Chromium

Workflow تجمعی فعلی در `.github/workflows/priority9-quality-gate.yml` قرار دارد و کنترل‌های اولویت ۸ را نیز اجرا می‌کند.

## سناریوهای پذیرش نماینده

| سناریو | انتظار |
|---|---|
| Company scope مدیر کانال | مشاهده و مدیریت همه نمایندگان C01 |
| Dealer scope کاربر نماینده | فقط `P-D01` و بدون فرم مدیریتی |
| مشاهده مالی بدون مجوز | مقدارها Mask و Details مالی null |
| ارسال نماینده ناقص | رد تا وجود قرارداد و Territory فعال |
| تأیید قرارداد/قلمرو توسط درخواست‌کننده | رد تفکیک وظیفه |
| Territory انحصاری هم‌پوشان | رد پیش از فعال‌شدن |
| پایان آخرین قرارداد/Territory نماینده فعال | رد تا تعلیق یا جایگزینی |
| خاتمه نماینده | بستن اتمیک قرارداد، Territory و سبد مشتری |
| فرم هدف موجود | حمل ExpectedVersion فعلی |
| POST استاندارد MVC | Redirect به Details |
| POST با HTMX | Anti-forgery، 422 قابل Swap، سپس HX-Redirect |
| Merge/Unmerge مشتری | انتقال و بازگردانی DealerCustomerAssignment |
| Migration rollback | حذف فقط Schema objects اولویت ۸ |

## شواهد شکست

در CI، خروجی Web/Browser و Trace/Screenshot در Artifact با نام `priority9-quality-gate-*` نگهداری می‌شود. هیچ استقرار آزمایشی نباید با شکست Model validation، Migration rollback یا Browser form smoke ادامه پیدا کند.
