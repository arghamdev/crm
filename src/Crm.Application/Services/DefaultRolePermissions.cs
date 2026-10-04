namespace Crm.Application.Services;

/// <summary>
/// The shipped role → permission matrix. It is only the seed for the RoleDefinitions/RolePermissionGrants tables;
/// at runtime permissions come from data and are edited through role administration.
/// </summary>
public static class DefaultRolePermissions
{
    public static readonly IReadOnlyDictionary<string, (string Label, bool IsExternal, string ScopeTypes)> Roles =
        new Dictionary<string, (string Label, bool IsExternal, string ScopeTypes)>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyMember"] = ("عضو شرکت", false, "Company"),
            ["SalesManager"] = ("مدیر فروش", false, "Company,Branch"),
            ["SalesSupervisor"] = ("سرپرست فروش", false, "Branch,Territory"),
            ["SalesExpert"] = ("کارشناس فروش", false, "Branch,Territory"),
            ["FinanceManager"] = ("مدیر مالی", false, "Company"),
            ["ChannelManager"] = ("مدیر کانال", false, "Company"),
            ["Executive"] = ("مدیرعامل", false, "Company,Branch"),
            ["DealerUser"] = ("کاربر نماینده", true, "Dealer"),
            ["ServiceAgent"] = ("کارشناس خدمات", false, "Branch,Territory")
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Permissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyMember"] = Set(),
            ["SalesManager"] = Set(
                "Portal.Review", "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read", "Reporting.Export", "Reporting.BiExport",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Update", "Customer.MergeReview",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Lead.Read", "Lead.Create", "Lead.Assign", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Assign", "Opportunity.Close", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Approve",
                "Quote.Approve.Supervisor", "Quote.Approve.Commercial", "Quote.Approve.Sales",
                "Quote.Send", "Quote.Accept", "Quote.Expire",
                "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit", "Order.Integration.Process", "Order.Sync",
                "Dealer.Read", "Dealer.Contract.Request", "Dealer.Territory.Request", "Dealer.Customer.Assign",
                "Service.Read", "Service.Create", "Service.Update", "Service.Triage",
                "WorkQueue.Read", "WorkQueue.Complete", "Administration.Manage", "Dealer.Commission.Read", "Dealer.Evaluation.Read", "Dealer.Guarantee.Read", "Dealer.Training.Read",
                "Activity.Read", "Activity.Create", "Activity.Update", "Note.Read", "Note.Create", "Note.Restricted.Read",
                "Document.Read", "Document.Manage", "Document.Sensitive.Read", "Payment.Read", "Payment.Create", "Payment.Approve",
                "BankAccount.Read", "BankAccount.Manage", "Account.Guarantee.Read", "Account.Guarantee.Manage", "Contract.Read", "Contract.Manage",
                "Project.Read", "Project.Manage", "Campaign.Read", "Campaign.Manage", "Survey.Read", "Survey.Manage", "Account.Relation.Manage",
                "Customer.Status.Change"),
            ["SalesSupervisor"] = Set(
                "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read", "Reporting.Export",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Update",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Lead.Read", "Lead.Create", "Lead.Assign", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Assign", "Opportunity.Close", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Approve",
                "Quote.Approve.Supervisor", "Quote.Send", "Quote.Accept", "Quote.Expire",
                "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit",
                "Service.Read", "Service.Create", "Service.Update", "Service.Triage",
                "WorkQueue.Read", "WorkQueue.Complete",
                "Activity.Read", "Activity.Create", "Activity.Update", "Note.Read", "Note.Create", "Note.Restricted.Read",
                "Document.Read", "Document.Manage", "Document.Sensitive.Read", "Payment.Read", "Payment.Create",
                "BankAccount.Read", "Account.Guarantee.Read", "Contract.Read", "Contract.Manage",
                "Project.Read", "Project.Manage", "Campaign.Read", "Campaign.Manage", "Survey.Read", "Survey.Manage", "Account.Relation.Manage",
                "Customer.Status.Change"),
            ["SalesExpert"] = Set(
                "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Contact.Read",
                "Lead.Read", "Lead.Create", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Send",
                "Quote.Accept", "Quote.Expire", "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit",
                "Service.Read", "Service.Create", "Service.Update",
                "WorkQueue.Read", "WorkQueue.Complete",
                "Activity.Read", "Activity.Create", "Note.Read", "Note.Create", "Document.Read", "Document.Manage",
                "Payment.Read", "Payment.Create", "BankAccount.Read", "Account.Guarantee.Read", "Contract.Read", "Project.Read",
                "Campaign.Read", "Survey.Read", "Survey.Manage", "Account.Relation.Manage"),
            ["FinanceManager"] = Set(
                "Reporting.Read", "Reporting.Export", "Reporting.BiExport", "Reporting.Financial.Read",
                "Dashboard.Read", "Customer.Read", "Opportunity.Read", "Quote.Read",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Quote.Approve.Finance", "Order.Read", "Order.CreditOverride",
                "Dealer.Read", "Dealer.Financial.Read", "WorkQueue.Read", "WorkQueue.Complete", "Dealer.Commission.Read", "Dealer.Commission.Approve", "Dealer.Evaluation.Read", "Dealer.Guarantee.Read", "Dealer.Guarantee.Manage", "Dealer.Training.Read",
                "Activity.Read", "Note.Read", "Document.Read", "Document.Manage", "Document.Sensitive.Read",
                "Payment.Read", "Payment.Create", "Payment.Approve", "BankAccount.Read", "BankAccount.Manage",
                "Account.Guarantee.Read", "Account.Guarantee.Manage", "Contract.Read", "Project.Read"),
            ["ChannelManager"] = Set(
                "Portal.Review",
                "Reporting.Read", "Reporting.Export",
                "Dashboard.Read", "Customer.Read", "Dealer.Read", "Dealer.Manage", "Dealer.Submit", "Dealer.Approve",
                "Dealer.Contract.Request", "Dealer.Contract.Approve", "Dealer.Territory.Request", "Dealer.Territory.Approve",
                "Dealer.Customer.Assign", "Dealer.Target.Manage", "Dealer.Financial.Read", "Dealer.Financial.Sync",
                "Dealer.Performance.Sync", "WorkQueue.Read", "WorkQueue.Complete", "Dealer.Commission.Read", "Dealer.Commission.Manage", "Dealer.Evaluation.Read", "Dealer.Evaluation.Run", "Dealer.Guarantee.Read", "Dealer.Guarantee.Manage", "Dealer.Training.Read", "Dealer.Training.Manage",
                "Activity.Read", "Note.Read"),
            ["Executive"] = Set("Dashboard.Read", "Reporting.Read", "Reporting.Export", "Reporting.BiExport", "Reporting.Financial.Read",
                "Customer.Read", "Lead.Read", "Opportunity.Read", "Quote.Read", "Order.Read", "Dealer.Read", "Dealer.Financial.Read",
                "Customer.Financial.Read", "Quote.Margin.Read",
                "Service.Read", "Service.ReadAll", "WorkQueue.Read", "Dealer.Commission.Read", "Dealer.Evaluation.Read", "Dealer.Guarantee.Read", "Dealer.Training.Read",
                "Activity.Read", "Note.Read", "Note.Restricted.Read", "Document.Read", "Document.Sensitive.Read", "Payment.Read",
                "BankAccount.Read", "Account.Guarantee.Read", "Contract.Read", "Project.Read", "Campaign.Read", "Survey.Read"),
            ["ServiceAgent"] = Set(
                "Dashboard.Read", "Customer.Read", "Customer.Contact.Read", "Service.Read", "Service.Create", "Service.Update",
                "WorkQueue.Read", "WorkQueue.Complete",
                "Activity.Read", "Activity.Create", "Note.Read", "Note.Create", "Document.Read", "Document.Manage",
                "Contract.Read", "Survey.Read", "Survey.Manage"),
            ["DealerUser"] = Set(
                "Portal.Read", "Portal.Submit",
                "Dashboard.Read", "Dealer.Read", "Dealer.Portal", "Dealer.Financial.Read", "WorkQueue.Read")
        };

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
