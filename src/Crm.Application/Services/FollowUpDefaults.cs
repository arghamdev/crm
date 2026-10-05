using Crm.Application.Abstractions;
using Crm.Domain.FollowUps;

namespace Crm.Application.Services;

/// <summary>
/// Starting settings of the follow-up center for a company: SLA policies per priority, a published workflow template for every
/// follow-up type and nothing else. Installing them twice changes nothing.
/// </summary>
public static class FollowUpDefaults
{
    public sealed record StageSeed(string Name, int Weight, string Role, string Checklist, bool Required = true, string? Condition = null, bool Parallel = false,
        int? Hours = null);

    public sealed record TemplateSeed(string CaseType, string Name, string ClosingCriteria, bool CustomerApproval, string? Rules, FollowUpPriority Priority,
        IReadOnlyList<StageSeed> Stages);

    public static readonly IReadOnlyList<TemplateSeed> Templates =
    [
        new("Proforma", "پیگیری پیش‌فاکتور قطعات", "پیش‌فاکتور صادر و توسط مشتری تأیید شده باشد", true,
            "موجودی ناکافی بود => ایجاد وظیفه تأمین موجودی\nمشتری توقف تولید اعلام کرد => افزایش اولویت", FollowUpPriority.Normal,
            [
                new("ثبت درخواست", 10, "کارشناس فروش", "دریافت درخواست مشتری\nثبت اقلام و تعداد\nتأیید شخص تماس"),
                new("بررسی فنی", 25, "کارشناس فنی", "کنترل کد قطعه و قطعهٔ جایگزین\nکنترل سازگاری با دستگاه مشتری\nتأیید مدارک فنی"),
                new("تأمین موجودی", 20, "کارشناس انبار", "استعلام موجودی انبار\nرزرو موجودی\nهماهنگی تأمین کسری"),
                new("صدور پیش‌فاکتور", 30, "کارشناس فروش", "محاسبهٔ قیمت و تخفیف\nتأیید مالی\nصدور و ارسال پیش‌فاکتور"),
                new("تأیید مشتری", 15, "کارشناس فروش", "پیگیری پاسخ مشتری\nدریافت تأیید کتبی مشتری")
            ]),
        new("Inquiry", "پاسخ به استعلام قیمت", "پاسخ قیمت برای مشتری ارسال و دریافت آن تأیید شده باشد", false, null, FollowUpPriority.Normal,
            [
                new("دریافت استعلام", 20, "کارشناس فروش", "ثبت اقلام استعلام\nتأیید مشخصات با مشتری"),
                new("بررسی و قیمت‌گذاری", 50, "کارشناس فروش", "استعلام موجودی و زمان تحویل\nقیمت‌گذاری\nتأیید سرپرست"),
                new("ارسال پاسخ", 30, "کارشناس فروش", "ارسال پاسخ به مشتری\nتأیید دریافت توسط مشتری")
            ]),
        new("Supply", "تأمین قطعه", "قطعه تحویل و دریافت آن توسط مشتری تأیید شده باشد", false, "تأمین‌کننده پاسخ نداد => ایجاد وظیفه پیگیری تأمین‌کنندهٔ جایگزین",
            FollowUpPriority.Normal,
            [
                new("ثبت نیاز", 15, "کارشناس فروش", "ثبت کد قطعه و تعداد\nکنترل قطعهٔ جایگزین"),
                new("استعلام تأمین‌کننده", 35, "کارشناس تأمین", "استعلام از حداقل دو تأمین‌کننده\nانتخاب تأمین‌کننده"),
                new("سفارش و پیگیری ارسال", 35, "کارشناس تأمین", "ثبت سفارش خرید\nپیگیری حمل\nدریافت در انبار"),
                new("تحویل به مشتری", 15, "کارشناس فروش", "ارسال به مشتری\nتأیید تحویل")
            ]),
        new("ShipmentDiscrepancy", "رسیدگی به مغایرت ارسال", "مغایرت جبران و توسط مشتری تأیید شده باشد", true, null, FollowUpPriority.High,
            [
                new("ثبت مغایرت", 20, "کارشناس فروش", "ثبت شمارهٔ محموله\nدریافت عکس و مدارک"),
                new("بررسی انبار و حمل", 40, "کارشناس انبار", "تطبیق با سند خروج انبار\nاستعلام از شرکت حمل"),
                new("جبران مغایرت", 30, "کارشناس فروش", "ارسال تکمیلی یا مرجوعی\nصدور سند اصلاحی"),
                new("تأیید مشتری", 10, "کارشناس فروش", "دریافت تأیید مشتری")
            ]),
        new("Warranty", "رسیدگی به گارانتی", "تصمیم گارانتی اجرا و قطعه تحویل شده باشد", false, null, FollowUpPriority.Normal,
            [
                new("ثبت و بررسی مدارک", 20, "کارشناس خدمات", "کنترل شمارهٔ سریال / بچ\nکنترل تاریخ خرید و برگهٔ گارانتی"),
                new("ارزیابی فنی", 40, "کارشناس فنی", "بازدید یا دریافت قطعه\nتعیین علت خرابی"),
                new("تصمیم گارانتی", 25, "سرپرست خدمات", "ثبت تصمیم (تعویض / تعمیر / رد)\nاطلاع به مشتری"),
                new("اجرا و تحویل", 15, "کارشناس خدمات", "تحویل قطعهٔ جایگزین یا تعمیرشده")
            ]),
        new("Collection", "پیگیری وصول مطالبات", "مبلغ معوق دریافت و با حسابداری تطبیق شده باشد", false, null, FollowUpPriority.Normal,
            [
                new("یادآوری سررسید", 30, "کارشناس فروش", "تماس یادآوری\nارسال صورت‌حساب"),
                new("توافق پرداخت", 40, "کارشناس فروش", "مذاکره و ثبت توافق\nتأیید مدیر مالی"),
                new("دریافت و تطبیق", 30, "کارشناس مالی", "دریافت وجه\nتطبیق با سیستم حسابداری ارقام")
            ]),
        new("General", "پیگیری عمومی", "نتیجهٔ مورد انتظار حاصل شده باشد", false, null, FollowUpPriority.Normal,
            [
                new("بررسی", 40, "کارشناس فروش", "بررسی درخواست\nتعیین اقدام‌ها"),
                new("انجام", 40, "کارشناس فروش", "انجام اقدام‌ها"),
                new("جمع‌بندی", 20, "کارشناس فروش", "ثبت نتیجه\nاطلاع به مشتری")
            ])
    ];

    /// <summary>Creates the default SLA policies and templates when the company has none. Returns false when they already exist.</summary>
    public static bool Install(CrmDataSet data, string companyId, Guid userId, DateTimeOffset nowUtc)
    {
        var created = false;
        var policies = data.Find<SlaPolicy>(x => x.CompanyId == companyId);
        if (policies.Count == 0)
        {
            static IEnumerable<DayOfWeek> Days() => [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday];
            SlaEscalation[] standard = [new(-30, EscalationTarget.StageOwner), new(0, EscalationTarget.Supervisor), new(120, EscalationTarget.BranchManager)];
            foreach (var (name, priority, first, stage, resolution, steps) in new[]
                     {
                         ("مهلت کم‌فوریت", FollowUpPriority.Low, 4, 16, 48, standard),
                         ("مهلت استاندارد فروش قطعات", FollowUpPriority.Normal, 2, 8, 24, standard),
                         ("مهلت اولویت بالا", FollowUpPriority.High, 1, 4, 16, standard),
                         ("توقف تولید مشتری", FollowUpPriority.Critical, 1, 2, 8,
                             new SlaEscalation[] { new(-15, EscalationTarget.StageOwner), new(0, EscalationTarget.Supervisor), new(60, EscalationTarget.BranchManager) })
                     })
            {
                var policy = new SlaPolicy(Guid.NewGuid(), companyId, name, priority);
                policy.Update(name, priority, null, "Asia/Tehran", Days(), "08:00", "17:00", null, first, stage, resolution, true, false, steps, true);
                data.Append(policy);
                policies.Add(policy);
            }
            created = true;
        }
        if (data.Find<FollowUpTemplate>(x => x.CompanyId == companyId).Count == 0)
        {
            var number = 100;
            foreach (var seed in Templates)
            {
                var template = new FollowUpTemplate(Guid.NewGuid(), companyId, $"WF-{++number}", seed.Name, seed.CaseType, 1);
                template.Update(seed.Name, seed.CaseType, "Company", "مدیریت فروش", null, seed.ClosingCriteria, true, seed.CustomerApproval, seed.Rules,
                    policies.FirstOrDefault(x => x.Priority == seed.Priority)?.Id, seed.Priority);
                var stages = seed.Stages.Select((s, i) => new FollowUpTemplateStage(Guid.NewGuid(), template.Id, i + 1, s.Name, s.Weight, s.Role, s.Required, s.Checklist,
                    s.Condition, s.Parallel, s.Hours)).ToList();
                template.Publish(stages, userId, nowUtc);
                data.Append(template);
                foreach (var stage in stages) data.Append(stage);
            }
            created = true;
        }
        return created;
    }

    /// <summary>Creates the stages of a case from its template and computes the starting progress (used by seeding and imports).</summary>
    public static void Start(CrmDataSet data, FollowUpCase followUp, Guid? actorUserId, DateTimeOffset nowUtc)
    {
        var (stages, items) = FollowUpSupport.Instantiate(data, followUp, data.Find<FollowUpTemplateStage>(x => x.TemplateId == followUp.TemplateId));
        FollowUpSupport.Log(data, followUp, "Created", $"پرونده {followUp.Code} ثبت شد", null, actorUserId, nowUtc);
        FollowUpSupport.Recompute(data, followUp, actorUserId, nowUtc, null, stages, items);
    }

    /// <summary>Recomputes stage flow and progress after checklist or stage changes made outside the service.</summary>
    public static void Refresh(CrmDataSet data, FollowUpCase followUp, Guid? actorUserId, DateTimeOffset nowUtc) =>
        FollowUpSupport.Recompute(data, followUp, actorUserId, nowUtc);
}
