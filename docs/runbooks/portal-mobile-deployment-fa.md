# راهنمای اجرای نمونه و ارتقای پرتال/موبایل

## اجرای سریع

۱. `index.html` را دانلود و در مرورگر باز کنید. برای پرتال: `dealer.user`، برای بررسی و بازدید: `sales.manager`؛ رمز مشترک نمونه `Demo@1405`. این فایل خودکفا و بدون CDN است. اگر نمایشگر پیوست اسکریپت را اجرا نکرد، فایل را مستقیماً در مرورگر باز کنید.

۲. برای backend واقعی نمونه، SDK .NET 10 و پیش‌نیازهای README را نصب کنید:

```sh
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln -c Release --no-restore
dotnet dev-certs https --trust
dotnet run --project src/Crm.Web --no-launch-profile --urls https://localhost:7085
```

حالت پیش‌فرض InMemory با دادهٔ ساختگی است. با توقف برنامه دادهٔ این حالت بازنشانی می‌شود. `/portal`، `/partner-requests` و `/mobile` زیر همان Layout و ناوبری قرار دارند. موقعیت‌یابی واقعی در HTTPS یا localhost و پس از رضایت مرورگر فعال است؛ دریافت‌نشدن GPS مانع ثبت بازدید نیست.

ورود را از `https://localhost:7085/account/login` آغاز کنید. کوکی `__Host-Crm.Auth` همواره Secure است؛ کلاینتی که آن را روی HTTP ارسال نکند دوباره به صفحهٔ ورود برمی‌گردد. گواهی توسعه باید مطابق پشتیبانی سیستم‌عامل مورد اعتماد قرار گیرد.

## ارتقای SQL

Migration جدید `202609270008_PortalMobileSelfService` است و baseline آن `202609270007_DealerChannelGovernance` است. اولویت ۹ migration مجزا ندارد. قبل از ارتقا، snapshot و backup قابل بازیابیِ DB تهیه و مسیر را روی نسخهٔ آزمایشی بازیابی‌شده اجرا کنید.

مسیر اصلی، migrationهای EF از همان پروژه است. اگر از CLI استفاده می‌کنید، نسخهٔ `dotnet-ef` هم‌نسخهٔ بسته‌های EF پروژه باشد؛ اتصال را در `CRM_SQLSERVER_CONNECTION` تنظیم کنید، نه در فایل منبع.

```sh
dotnet ef database update --project src/Crm.Infrastructure --startup-project src/Crm.Web
```

مسیر SQL مستقل نیز در `scripts/sql/priority10-portal-mobile-idempotent.sql` موجود است و از بدنهٔ migration ساخته می‌شود. اسکریپت transaction، lock و کنترل تاریخچه دارد؛ `GO` یا دستور SQLCMD ندارد. EF و اسکریپت مستقل را هم‌زمان اجرا نکنید. جدول‌های ناقصِ بدون تاریخچه به‌صورت خودکار تأیید نمی‌شوند؛ خطا باید بررسی شود.

فرمان `node scripts/build-self-service-sql.mjs` اکنون اسکریپت‌های P8 و P10 را از migrationها تولید می‌کند. نسخهٔ اصلاح‌شدهٔ P8 تاریخچهٔ EF را نیز ثبت می‌کند. اگر اسکریپت قدیمی P8 را بدون ثبت تاریخچه اجرا کرده‌اید، ابتدا مدل موجود را با migration تطبیق دهید؛ تاریخچه را بدون بررسی دستی ثبت نکنید. مسیر تازه روی DB آزمایشی با اجرای دوبارهٔ هر اسکریپت تأیید شده است.

نمونه‌های قدیمی شناسهٔ migration با ۱۲ رقم دارند. سازگارکنندهٔ `IMigrationsIdGenerator` برای شناسه و نام migration ثبت شده است؛ تاریخچه تغییر نمی‌کند. مرجع API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.migrations.imigrationsidgenerator?view=efcore-10.0

برای rollback هدف P8 را انتخاب کنید یا اسکریپت `priority10-portal-mobile-rollback.sql` را پس از بازبینی اجرا کنید. rollback فقط سه جدول P10 را حذف می‌کند و **دادهٔ آن سه جدول از بین می‌رود**؛ ابتدا خروجی/backup بگیرید. اسکریپت مستقل در حضور migration جدیدتر متوقف می‌شود.

```sh
dotnet ef database update 202609270007_DealerChannelGovernance --project src/Crm.Infrastructure --startup-project src/Crm.Web
```

Seed فقط برای پایگاه خالی اجرا می‌شود؛ ارتقای DB موجود کاربران یا داده‌های سازمان را بازنویسی نمی‌کند. در DB موجود باید کاربر دارای DealerUser با Scope نوع Dealer و ScopeId متناظر `Dealer.DealerId` موجود باشد. Cache مجوزهای این تحویل namespace نسخهٔ ۳ دارد؛ پس از تغییر نقش، invalidate و ابطال نشست را از IAM انجام دهید.

## بررسی پذیرش

۱. نماینده در `/portal` فقط مشتری‌های تخصیص‌یافته و دادهٔ مالی مجاز را ببیند؛ حساب مالی/مدیر فروش با Company grant به پرتال وارد نشود.
۲. درخواست سفارش ایجاد کنید؛ در `/partner-requests` ابتدا InReview و سپس پس از گردش Quote/Order و انتخاب سفارش همان مشتری، تأیید شود.
۳. سرنخ را تأیید کنید؛ یک Lead ایجاد شود و عنوان مشابه در بازهٔ حفاظت تعارض بدهد.
۴. درخواست کاربر جدید با مدیر فروش تأیید شود؛ حساب PendingActivation بماند. فعال‌سازی واقعی هویت، مرحلهٔ مستقل IAM است.
۵. در موبایل بازدید بسازید، شبکه را قطع کنید، شروع و پایان را ثبت کنید، شبکه را وصل و دکمهٔ ارسال را بزنید؛ reload باید نتیجهٔ ذخیره‌شده را نشان دهد.
۶. Version را با تغییر هم‌زمان یا Merge تغییر دهید؛ صف باید 409 بگیرد و هیچ بازنویسی خودکاری انجام ندهد.
۷. GPS بدون رضایت یا مختصات نامعتبر رد شود؛ عدم دریافت موقعیت، مسیر بدون GPS را مسدود نکند.

عکس، cache پایدار دستگاه، Push و اتصال واقعی ERP/IdP قبل از فعال‌شدن به زیرساخت و سیاست سازمانی معرفی‌شده در سند طراحی نیاز دارند.
