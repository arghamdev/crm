# گزارش طراحی و پیاده‌سازی اولویت ۶ — پیشنهاد قیمت و حاکمیت تخفیف

**وضعیت:** پیاده‌سازی نمونه تکمیل شده  
**دامنه:** Quote/Pricing/Approval در معماری ASP.NET Core MVC، Razor، HTMX، Tailwind-compatible CSS و JavaScript  
**منبع تصمیم:** `phase-0-sample-closure-fa.md`

## ۱. مسئله و مرز سامانه

قیمت پایه و بهای استاندارد از Adapter نمونه ERP خوانده می‌شود. CRM مالک ردیف پیشنهاد، Snapshot قیمت، تخفیف، محاسبه Margin، مسیر تأیید، نسخه و Audit است. اتصال واقعی ERP، نرخ ارز و ثبت حسابداری خارج از نسخه نمونه باقی مانده‌اند.

## ۲. مدل دامنه

| Aggregate/Entity | مسئولیت |
|---|---|
| `Quote` | هویت پیشنهاد، مشتری/فرصت، مالک، اعتبار، Revision، جمع‌ها و State Machine |
| `QuoteLine` | Snapshot کالا، قیمت پایه، Cost، تخفیف و منبع قیمت |
| `QuoteApprovalDecision` | تصمیم مستقل هر نقش، توضیح، تصمیم‌گیرنده و زمان |
| `QuoteStatusHistory` | تاریخچه غیرقابل ویرایش انتقال وضعیت |
| `QuoteApprovalPolicy` | تعیین سطح اختیار از بیشترین تخفیف ردیف و Margin کل |

جریان وضعیت اجراشده:

`Draft → Submitted → PendingApproval/Approved → Rejected/Sent → Accepted/Expired`

`Submitted` به‌عنوان رخداد مستقل Audit ثبت می‌شود و وضعیت پایدار بعدی بر اساس ماتریس اختیار `PendingApproval` یا `Approved` است. نسخه ردشده یا ارسال‌شده با فرمان Revise به یک `Draft` جدید با `ParentQuoteId` و Revision افزایشی تبدیل می‌شود.

## ۳. ماتریس حاکمیت

| شرط | سطح | نقش‌های لازم |
|---|---|---|
| تخفیف ≤ ۵٪ و Margin ≥ ۲۵٪ | بدون تأیید | تأیید خودکار هنگام Submit |
| تخفیف ≤ ۸٪ و Margin ≥ ۲۲٪ | سرپرست | `SalesSupervisor` |
| تخفیف ≤ ۱۲٪ و Margin ≥ ۱۸٪ | مدیر تجاری | `CommercialManager` |
| تخفیف ≤ ۲۰٪ و Margin ≥ ۱۵٪ | مشترک | `SalesManager` و `FinanceManager` |
| تخفیف > ۲۰٪ یا Margin < ۱۵٪ | نامعتبر | Submit رد می‌شود |

در سطح مشترک، تأیید یک نقش پیشنهاد را نهایی نمی‌کند؛ هر دو تصمیم مستقل نگه‌داری می‌شوند. هر رد، وضعیت را فوراً `Rejected` می‌کند. Index یکتای `(QuoteId, Role)` از تصمیم تکراری یک نقش جلوگیری می‌کند.

## ۴. محاسبات

- `Gross = Quantity × ListUnitPrice`
- `DiscountAmount = Gross × DiscountPercent / 100`
- `Net = Gross − DiscountAmount`
- `Cost = Quantity × StandardUnitCost`
- `MarginPercent = (Net − Cost) × 100 / Net`

همه مبالغ از Snapshot ردیف محاسبه می‌شوند؛ تغییر بعدی کاتالوگ ERP پیشنهاد قبلی را تغییر نمی‌دهد. Decimalهای مالی در SQL Server با Precision صریح ذخیره می‌شوند.

## ۵. Application و امنیت

`QuoteApplicationService` تنها نقطه اجرای Use Caseهای Create، Add/Remove Line، Submit، Decide، Send، Outcome و Revise است. هر فرمان این کنترل‌ها را دارد:

- Permission و Scope شرکت/شعبه/قلمرو؛
- Ownership برای کارشناس و دید نظارتی برای سرپرست/مدیر؛
- `ExpectedVersion` برای Optimistic Concurrency؛
- تطابق Customer، Opportunity، Branch و بازبودن Opportunity؛
- اعتبار تاریخ، وجود ردیف و اجرای سیاست تخفیف؛
- ثبت Actor، زمان، دلیل و انتقال وضعیت.

نقش مالی نمونه با کاربر `finance.manager` و رمز Demo مشترک فراهم شده است. این کاربر فقط دسترسی خواندن لازم و `Quote.Approve.Finance` دارد.

## ۶. MVC، Razor و HTMX

- فهرست Quote با KPI مبلغ باز، Draft، Pending Approval و نزدیک انقضا؛
- Drawer ساخت پیش‌نویس با فیلتر HTMX فرصت بر اساس مشتری؛
- فرم داخلی افزودن ردیف با Anti-forgery و پاسخ 422؛
- صفحه Details شامل ردیف‌ها، محاسبات، نقش‌های در انتظار، تصمیم‌ها و Timeline؛
- Post عادی به‌عنوان fallback و `HX-Redirect` پس از Mutation موفق؛
- نمایش Action فقط هنگامی که DTO اجازه اجرای آن را اعلام می‌کند؛
- CSS موجود سازگار با Build اختیاری Tailwind و JavaScript بدون وابستگی CDN.

## ۷. Persistence و Migration

Migration `202609260005_QuotePricingGovernance` ستون‌های Quote، سه جدول جدید، Indexها، Foreign Keyها و Backfill پیشنهادهای قدیمی را اضافه می‌کند. Model Snapshot هم‌زمان به‌روزرسانی شده است.

اسکریپت‌های عملیاتی:

- `scripts/sql/priority6-quote-pricing-idempotent.sql`
- `scripts/sql/priority6-quote-pricing-rollback.sql`

Rollback مخرب و به‌صورت پیش‌فرض قفل است. ترتیب و کنترل‌های استقرار در Runbook اولویت ۶ آمده است.

## ۸. Acceptance و کنترل کیفیت

| سناریو | کنترل |
|---|---|
| تخفیف ۱۵٪ | دو تأیید مستقل فروش و مالی |
| تأیید فقط فروش | Quote همچنان PendingApproval |
| تأیید مالی دوم | Quote به Approved می‌رود |
| ارسال و پذیرش | Quote به Accepted می‌رود |
| Won کردن Opportunity | فقط با Quote پذیرفته‌شده همان Opportunity |
| نسخه قدیمی | Mutation با پیام Conflict/Refresh متوقف می‌شود |
| Scope نامجاز | خواندن/نوشتن رکورد رد می‌شود |
| Migration | Up، Seed، Down تا اولویت ۵ و Roll-forward آزموده می‌شود |
| UI | Login، Drawer، HTMX Quote و Concurrency token در Chromium آزموده می‌شود |

Workflow تجمعی `.github/workflows/priority6-quality-gate.yml` کنترل ساختار و Secret، Build، تست Domain/Architecture، Web، SQL Server Migration/Rollback و Playwright را اجرا می‌کند.

## ۹. موارد خارج از نمونه

- اتصال واقعی ERP و اعتبارسنجی لحظه‌ای موجودی/قیمت؛
- نرخ ارز، مالیات، حمل و گردکردن حقوقی؛
- Delegation و جانشین تأییدکننده در غیبت؛
- امضای دیجیتال یا ارسال PDF رسمی به مشتری؛
- SLA تأیید، Notification و Escalation واقعی؛
- Load Test و کالیبراسیون ماتریس با داده سازمانی.

این موارد مانع ارزیابی جریان نمونه نیستند، اما پیش از Production باید به Backlog قابل قبول و مالک مشخص تبدیل شوند.
