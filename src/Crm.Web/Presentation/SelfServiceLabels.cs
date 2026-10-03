using Crm.Domain.SelfService;
namespace Crm.Web.Presentation;
public static class SelfServiceLabels
{
    public static string Label(this PortalRequestKind kind) => kind switch { PortalRequestKind.Order=>"درخواست سفارش", PortalRequestKind.Lead=>"ثبت سرنخ", PortalRequestKind.Complaint=>"شکایت", PortalRequestKind.Claim=>"ادعای خسارت", PortalRequestKind.AccessInvite=>"درخواست کاربر جدید", _=>"درخواست لغو دسترسی" };
    public static string Label(this PortalRequestStatus state) => state switch { PortalRequestStatus.Submitted=>"ثبت‌شده", PortalRequestStatus.InReview=>"در حال بررسی", PortalRequestStatus.Accepted=>"تأییدشده", PortalRequestStatus.Rejected=>"ردشده", _=>"لغوشده" };
    public static string Label(this VisitStatus state) => state switch { VisitStatus.Planned=>"برنامه‌ریزی‌شده", VisitStatus.CheckedIn=>"در حال بازدید", VisitStatus.Completed=>"انجام‌شده", _=>"لغوشده" };
}
