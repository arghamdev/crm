using Crm.Domain.Channel;

namespace Crm.Web.Presentation;

public static class DealerIncentiveLabels
{
    public static string Label(this CommissionStatementStatus value) => value switch
    {
        CommissionStatementStatus.Calculated => "در انتظار تأیید مالی",
        CommissionStatementStatus.OnHold => "معلق (مطالبات سررسیدشده)",
        CommissionStatementStatus.Approved => "تأییدشده",
        CommissionStatementStatus.Rejected => "ردشده",
        _ => "—"
    };

    public static string Badge(this CommissionStatementStatus value) => value switch
    {
        CommissionStatementStatus.Approved => "badge--success",
        CommissionStatementStatus.OnHold => "badge--warning",
        CommissionStatementStatus.Rejected => "badge--neutral",
        _ => "badge--info"
    };

    public static string Label(this DealerTier value) => value switch
    {
        DealerTier.Platinum => "پلاتینی",
        DealerTier.Gold => "طلایی",
        DealerTier.Silver => "نقره‌ای",
        _ => "برنزی"
    };

    public static string Badge(this DealerTier value) => value switch
    {
        DealerTier.Platinum => "badge--success",
        DealerTier.Gold => "badge--warning",
        DealerTier.Silver => "badge--info",
        _ => "badge--neutral"
    };

    public static string Label(this DealerGuaranteeType value) => value switch
    {
        DealerGuaranteeType.BankGuarantee => "ضمانت‌نامه بانکی",
        DealerGuaranteeType.Cheque => "چک تضمین",
        DealerGuaranteeType.PromissoryNote => "سفته",
        DealerGuaranteeType.CashDeposit => "سپرده نقدی",
        _ => "وثیقه ملکی"
    };

    public static string Label(this DealerGuaranteeStatus value) => value switch
    {
        DealerGuaranteeStatus.Active => "فعال",
        DealerGuaranteeStatus.Released => "آزادشده",
        _ => "ضبط‌شده"
    };

    public static string Label(this DealerTrainingTopic value) => value switch
    {
        DealerTrainingTopic.Product => "محصول",
        DealerTrainingTopic.Sales => "فروش",
        DealerTrainingTopic.AfterSales => "خدمات پس از فروش",
        DealerTrainingTopic.Systems => "سامانه‌ها",
        _ => "مقررات و انطباق"
    };

    public static string Label(this CommissionPayoutStatus value) => value switch
    {
        CommissionPayoutStatus.Pending => "در صف ارسال به حسابداری",
        CommissionPayoutStatus.Sent => "ارسال‌شده به حسابداری",
        _ => "ارسال ناموفق (نیاز به بررسی)"
    };

    public static string Badge(this CommissionPayoutStatus value) => value switch
    {
        CommissionPayoutStatus.Sent => "badge--success",
        CommissionPayoutStatus.Pending => "badge--info",
        _ => "badge--danger"
    };
}
