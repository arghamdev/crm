# Runbook استقرار CustomerId و Merge

## پیش‌نیاز

1. از پایگاه داده پشتیبان قابل‌بازیابی تهیه کنید.
2. نام و شعبه مشتری در Opportunity و Quoteهای قدیمی را بررسی کنید؛ Backfill ابتدا تطبیق یکتای Company+Name+Branch و سپس تطبیق یکتای Company+Name را می‌پذیرد و در ابهام متوقف می‌شود.
3. در پنجره نگهداری، ایجاد Quote و تبدیل Lead را موقتاً متوقف کنید.

## اجرا

1. ترجیحاً Migration `202609210003_CustomerRelationshipsAndMerge` را با فرایند استاندارد EF اجرا کنید.
2. برای استقرار دستی از `scripts/sql/priority4-relationships-merge-idempotent.sql` استفاده کنید؛ وجود baseline معتبر `__EFMigrationsHistory` پیش‌نیاز است، اجرای مجدد امن است و پس از موفقیت Migration History را ثبت می‌کند.
3. تعداد رکوردهای بدون `CustomerId` در Opportunity و Quote باید صفر باشد.
4. صفحه Customers، جست‌وجوی صفحه‌بندی‌شده، فیلتر فرصت Quote براساس CustomerId، تبدیل Lead، Merge Dry Run و Refresh تاریخچه را Smoke Test کنید.

## بازگشت

ابتدا همه Mergeهای فعال را از صفحه تاریخچه Unmerge کنید. سپس فایل rollback را باز کرده، فقط پس از پشتیبان‌گیری مقدار تأیید را به `1` تغییر دهید. اسکریپت در صورت وجود Merge فعال عمداً متوقف می‌شود.

## کنترل‌های امنیتی

- فقط مجوز `Customer.MergeReview` به Preview، Merge، تاریخچه و Unmerge دسترسی دارد.
- هر دو Customer باید در Scope کاربر باشند.
- نسخه Candidate، Survivor، Merged و MergeOperation پیش از تغییر کنترل می‌شود.
- Candidate باید Confirmed و هر دو Customer باید فعال/درحال‌بررسی و خارج از Merge فعال دیگر باشند.
- Company تمام روابط پیش از انتقال کنترل می‌شود.
- Manifest شناسه روابط و Primaryهای منتقل‌شده را نگه می‌دارد؛ Unmerge در صورت حذف، جایگزینی یا انتقال بعدی رابطه متوقف می‌شود.
