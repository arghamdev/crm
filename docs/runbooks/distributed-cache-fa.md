# راهنمای Cache توزیع‌شده و Snapshot دسترسی

`IAccessSnapshotService` اکنون Snapshot نقش/مجوز/Scope را از `IDistributedCache` می‌خواند. حالت پیش‌فرض نمونه Memory است؛ در استقرار چندگرهی Redis الزامی است.

## تنظیم Redis

```bash
export Cache__Mode=Redis
export Cache__InstanceName='EnterpriseCrm:'
export ConnectionStrings__Redis='redis-host:6380,password=...,ssl=True,abortConnect=False'
```

Connection String باید از Secret Store تزریق شود. Prefix هر محیط باید جدا باشد؛ مانند `EnterpriseCrm:Prod:` و `EnterpriseCrm:Stage:`.

## رفتار امنیتی

- کلید Snapshot: `crm:access:v1:{user-id}` زیر Prefix محیط
- عمر مطلق: ۵ دقیقه
- تغییر نقش/وضعیت کاربر: حذف فوری کلید
- Session و SecurityVersion در SQL Server باقی می‌مانند و Cache منبع حقیقت نیست
- نبود یا انقضای Cache باعث بازسازی Snapshot از Store می‌شود

## Smoke test چندگرهی

1. کاربر از Node A وارد شود و Cookie دریافت کند.
2. همان Cookie به Node B ارسال شود؛ Data Protection Key Ring و SQL Store مشترک باشند.
3. یک نقش از Node A تغییر کند.
4. درخواست بعدی Node B نباید مجوز قدیمی را استفاده کند.
5. در قطعی Redis، رفتار Fail-closed/availability طبق سیاست زیرساخت تصمیم‌گیری و تست شود. نمونهٔ فعلی خطای Cache را پنهان نمی‌کند تا مجوز stale مصرف نشود.

## پایش

- latency و error rate عملیات Cache
- نرخ hit/miss Snapshot
- تعداد Invalidation پس از تغییر نقش
- تفاوت SecurityVersion Cookie/Session/User
- خطاهای انتخاب Context شرکت پس از تغییر Scope

