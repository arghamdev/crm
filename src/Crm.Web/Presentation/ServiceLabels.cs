using Crm.Application.Contracts;
using Crm.Domain.Service;

namespace Crm.Web.Presentation;

public static class ServiceLabels
{
    public static string Label(this ServiceCaseStatus value) => value switch
    {
        ServiceCaseStatus.New => "جدید",
        ServiceCaseStatus.Triaged => "تریاژشده",
        ServiceCaseStatus.InProgress => "در حال رسیدگی",
        ServiceCaseStatus.WaitingOnCustomer => "در انتظار مشتری",
        ServiceCaseStatus.Resolved => "حل‌شده",
        _ => "بسته"
    };

    public static string Label(this ServiceCasePriority value) => value switch
    {
        ServiceCasePriority.Critical => "بحرانی",
        ServiceCasePriority.High => "بالا",
        ServiceCasePriority.Medium => "متوسط",
        _ => "پایین"
    };

    public static string Label(this ServiceCaseCategory value) => value switch
    {
        ServiceCaseCategory.Complaint => "شکایت",
        ServiceCaseCategory.ProductDefect => "نقص کالا",
        ServiceCaseCategory.Delivery => "تحویل و ارسال",
        ServiceCaseCategory.Invoice => "فاکتور و مالی",
        ServiceCaseCategory.Warranty => "گارانتی",
        _ => "پرسش و راهنمایی"
    };

    public static string Label(this ServiceCaseChannel value) => value switch
    {
        ServiceCaseChannel.Phone => "تلفن",
        ServiceCaseChannel.Email => "ایمیل",
        ServiceCaseChannel.Portal => "پرتال",
        ServiceCaseChannel.Visit => "بازدید",
        _ => "داخلی"
    };

    public static string Label(this ServiceSlaState value) => value switch
    {
        ServiceSlaState.OnTrack => "در مهلت",
        ServiceSlaState.AtRisk => "در خطر",
        ServiceSlaState.Breached => "نقض‌شده",
        ServiceSlaState.Met => "رعایت‌شده",
        ServiceSlaState.Paused => "متوقف",
        _ => "—"
    };

    public static string Badge(this ServiceSlaState value) => value switch
    {
        ServiceSlaState.Breached => "badge--danger",
        ServiceSlaState.AtRisk => "badge--warning",
        ServiceSlaState.Met => "badge--success",
        ServiceSlaState.OnTrack => "badge--info",
        _ => "badge--neutral"
    };

    public static string Badge(this ServiceCasePriority value) => value switch
    {
        ServiceCasePriority.Critical => "badge--danger",
        ServiceCasePriority.High => "badge--warning",
        ServiceCasePriority.Medium => "badge--info",
        _ => "badge--neutral"
    };

    public static string Badge(this ServiceCaseStatus value) => value switch
    {
        ServiceCaseStatus.Resolved or ServiceCaseStatus.Closed => "badge--success",
        ServiceCaseStatus.WaitingOnCustomer => "badge--warning",
        ServiceCaseStatus.New => "badge--neutral",
        _ => "badge--info"
    };

    public static string Label(this ServiceCaseAction value) => value switch
    {
        ServiceCaseAction.StartWork => "شروع / ادامه رسیدگی",
        ServiceCaseAction.WaitOnCustomer => "در انتظار مشتری",
        ServiceCaseAction.Resolve => "حل پرونده",
        ServiceCaseAction.Close => "بستن و ثبت رضایت",
        _ => "بازگشایی"
    };
}
