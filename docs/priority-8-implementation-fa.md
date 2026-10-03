# اولویت ۸ — حاکمیت نماینده و کانال فروش

## نتیجه تحلیل

اولویت ۸ به‌عنوان قابلیت «Channel / Dealer Management» پیاده‌سازی شده است. هدف، ساخت Portal عمومی مستقل نیست؛ هدف، ایجاد پروندهٔ مرجع نماینده، کنترل قرارداد و قلمرو، سبد مشتری، هدف فروش و نمایش Projectionهای فقط‌خواندنی مالی و عملکرد در همان معماری ASP.NET Core MVC است.

چهار تصمیم معماری که پیش از پیاده‌سازی تثبیت شدند:

1. `Dealer` یک Scope مستقل در RBAC است و با Branch یا Territory جایگزین نمی‌شود.
2. اعتبار، مانده و فروش خالص مالکیت CRM ندارند؛ Snapshot فقط‌خواندنی از Accounting و ERP/BI هستند.
3. فعال‌سازی نماینده بدون قرارداد فعال و حداقل یک تخصیص Territory فعال ممکن نیست.
4. Merge/Unmerge مشتری باید رابطهٔ `DealerCustomerAssignment` را نیز منتقل و بازگردانی کند.

## مدل دامنه

| Aggregate / Entity | مسئولیت | مالک داده |
|---|---|---|
| `Dealer` | شناسه پایدار، اطلاعات حقوقی/تجاری، مدیر کانال و وضعیت | CRM |
| `DealerContract` | بازه اعتبار، هدف سالانه، شرایط پرداخت و گردش تأیید | CRM |
| `DealerTerritoryAssignment` | تخصیص زمانی، انحصار و کنترل تعارض | CRM |
| `DealerCustomerAssignment` | سبد مشتری معتبر و قابل انتقال در Merge | CRM |
| `DealerTarget` | هدف دوره و نسخه همزمانی | CRM |
| `DealerFinancialSnapshot` | سقف/مصرف اعتبار، مانده و سررسیدشده | Accounting projection |
| `DealerPerformanceSnapshot` | فروش خالص و تعداد سفارش | ERP/BI projection |
| `DealerStatusHistory` | Actor، وضعیت قبلی/بعدی، دلیل و زمان | CRM Audit |

وضعیت نماینده به ترتیب `Draft → PendingApproval → Active` پیش می‌رود. `Active` می‌تواند `Suspended` یا `Terminated` شود و `Suspended` پس از کنترل دوبارهٔ پیش‌نیازها به گردش تأیید بازمی‌گردد. خاتمه نماینده، قراردادها، Territoryها و روابط فعال سبد مشتری را اتمیک می‌بندد. پایان مستقل آخرین قرارداد یا Territory نماینده فعال نیز تا زمان تعلیق یا ایجاد جایگزین رد می‌شود. همهٔ Mutationها از `ExpectedVersion` استفاده می‌کنند.

## مجوزها و تفکیک وظیفه

| Permission | کاربرد |
|---|---|
| `Dealer.Read` | فهرست و جزئیات در Scope مجاز |
| `Dealer.Manage` | ایجاد/ویرایش پرونده در Company Scope |
| `Dealer.Submit` / `Dealer.Approve` | گردش وضعیت نماینده |
| `Dealer.Contract.Request` / `Dealer.Contract.Approve` | درخواست و تأیید مستقل قرارداد |
| `Dealer.Territory.Request` / `Dealer.Territory.Approve` | درخواست و تأیید مستقل قلمرو |
| `Dealer.Customer.Assign` | تخصیص مشتری مجاز به سبد نماینده |
| `Dealer.Target.Manage` | ایجاد یا ویرایش هدف دوره |
| `Dealer.Financial.Read` / `Dealer.Financial.Sync` | مشاهده یا همگام‌سازی Projection مالی |
| `Dealer.Performance.Sync` | همگام‌سازی Snapshot عملکرد |

در قرارداد و Territory، درخواست‌کننده نمی‌تواند همان درخواست را تأیید کند. تخصیص انحصاری هم‌پوشان پیش از تأیید رد می‌شود. `dealer.user` فقط Scope دقیق `Dealer/P-D01` را می‌بیند و هیچ فرم مدیریتی دریافت نمی‌کند.

## MVC، Razor و HTMX

- Controller: `DealersController`
- صفحات: Workspace، جدول Partial، جزئیات و Drawerهای پرونده، قرارداد، Territory، هدف و مشتری
- تمام POSTها Anti-forgery دارند و بدون JavaScript نیز Redirect استاندارد MVC دارند.
- خطای اعتبارسنجی یا قاعدهٔ دامنه با HTTP 422 در همان Drawer جایگزین می‌شود.
- موفقیت HTMX رویداد `dealerChanged` و `HX-Redirect` تولید می‌کند.
- فرم هدف نسخه و مقدار هدف جاری را می‌خواند تا Update به‌اشتباه Create یا Conflict نشود.
- CSS با ساختار utility-friendly و بدون وابستگی به Inline Style نوشته شده و در کنار Tailwind v4 قابل نگهداری است.

## پایداری و Integration Boundary

Migration `202609270007_DealerChannelGovernance` Schema مستقل `channel` و هشت جدول را ایجاد می‌کند. Adapterهای نمونه، قراردادهای `IDealerFinancialProjectionProvider` و `IDealerPerformanceProjectionProvider` را پیاده می‌کنند؛ در محیط واقعی باید با Adapterهای Accounting و ERP/BI جایگزین شوند و مدل CRM همچنان فقط Snapshot را ذخیره کند.

ایندکس‌های اصلی شامل شناسه و کد یکتا در شرکت، شماره قرارداد یکتا، دوره هدف یکتا، Timeline وضعیت و مسیرهای جست‌وجوی Scope هستند. Rollback فقط اشیای اولویت ۸ را حذف می‌کند و جداول سفارش اولویت ۷ باقی می‌مانند.

## داده نمونه

- `channel.manager / Demo@1405`: مدیریت کامل کانال در C01
- `dealer.user / Demo@1405`: مشاهده فقط نماینده `P-D01`
- نماینده فعال `DLR-0001` با قرارداد معتبر، Territory انحصاری T02، دو مشتری، هدف دوره و Snapshotهای مالی/عملکرد

## معیار پایان

- Scope دقیق نماینده و عدم نشت داده کنترل شده است.
- فعال‌سازی ناقص، تأیید خودی و تعارض Territory رد می‌شوند.
- پایان روابط سبد مشتری همراه Actor، دلیل، زمان و نسخه ثبت می‌شود.
- مالی برای کاربر فاقد مجوز Mask می‌شود.
- Customer Merge/Unmerge روابط نماینده را انتقال و بازگردانی می‌کند.
- Migration، Snapshot، Store حافظه‌ای و EF، MVC، Browser smoke و CI تجمعی پوشش داده شده‌اند.
