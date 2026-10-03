# Runbook استقرار Channel / Dealer

## پیش از استقرار

- Backup و نقطه بازگشت دیتابیس ثبت شود.
- Adapterهای Accounting و ERP/BI در محیط مقصد Health Check موفق داشته باشند.
- Role mapping برای `ChannelManager` و `DealerUser` بازبینی شود.
- Scope هر DealerUser دقیقاً با `DealerId` تجاری، مانند `P-D01`، تطبیق داشته باشد.
- هیچ Projection مالی به‌عنوان منبع مرجع قابل ویرایش در CRM معرفی نشود.

## استقرار

1. Quality Gate اولویت ۸ را روی همان Commit اجرا کنید.
2. Migration تا `202609270007_DealerChannelGovernance` را اعمال کنید.
3. وجود هشت جدول در Schema `channel` و ایندکس‌های یکتا را کنترل کنید.
4. برنامه را بالا بیاورید و `/health` را بررسی کنید.
5. با `channel.manager` Workspace و یک Drawer را باز و ارسال کنید.
6. با `dealer.user` عدم نمایش سایر نمایندگان و فرم‌های مدیریتی را کنترل کنید.
7. Sync مالی و عملکرد را اجرا و Source/Freshness را بازبینی کنید.

## کنترل پس از استقرار

- شمار خطاهای 403، 409/422 و خطاهای Adapter را پایش کنید.
- وجود رکوردهای فعال بدون قرارداد یا Territory معتبر را گزارش‌گیری کنید.
- هم‌پوشانی Assignmentهای انحصاری فعال باید صفر باشد.
- Projection قدیمی‌تر از SLA توافق‌شده باید Alert ایجاد کند.

## Rollback

اگر هنوز داده تولیدی اولویت ۸ ایجاد نشده است، Migration را به `202609260006_OrderIntegrationVisibility` برگردانید. این عملیات فقط جداول `channel` را حذف می‌کند. اگر داده واقعی ایجاد شده، ابتدا Export/Audit و تأیید مالک کسب‌وکار الزامی است؛ اجرای مستقیم Script حذف بدون Backup مجاز نیست.

Scriptهای مرجع:

- `scripts/sql/priority8-dealer-channel-idempotent.sql`
- `scripts/sql/priority8-dealer-channel-rollback.sql`

