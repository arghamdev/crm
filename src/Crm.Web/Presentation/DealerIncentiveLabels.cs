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
}
