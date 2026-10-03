using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.SelfService;
using Crm.Domain.Service;
using Crm.Infrastructure.Commercial;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Reporting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Portal complaints/claims become service cases; service KPIs appear in reports for users who may read service data.</summary>
internal static class PortalServiceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var portal = new SelfServiceService(store, access, new DemoPortalReadSource(new DemoProductPriceCatalog()));
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var finance = User(6); var dealerUser = User(8);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var nakhl = Guid.Parse("20000000-0000-4000-8000-000000000003");
        var mahan = Guid.Parse("20000000-0000-4000-8000-000000000004");
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");

        var complaint = portal.Submit(dealerUser, org, new SubmitPortalRequestCommand(Guid.NewGuid(), PortalRequestKind.Complaint,
            "تأخیر در تحویل محموله", "محموله سه روز دیرتر رسید.", nakhl), now);
        check(complaint.CustomerId == nakhl && complaint.CustomerName == "بازرگانی نخل جنوب", "PSVC: a complaint keeps the dealer customer it concerns.");
        try
        {
            portal.Submit(dealerUser, org, new SubmitPortalRequestCommand(Guid.NewGuid(), PortalRequestKind.Complaint, "مشتری دیگر", "شرح", sepehr), now);
            check(false, "PSVC: a complaint about another dealer's customer must be rejected.");
        }
        catch (UnauthorizedAccessException) { }

        var accepted = portal.Review(manager, org, complaint.Id, new ReviewPortalRequestCommand(complaint.Version, PortalRequestStatus.Accepted, "پرونده خدمات ثبت شد"), now);
        var opened = store.Read(d => d.ServiceCases.Single(x => x.Id == accepted.LinkedRecordId));
        var history = store.Read(d => d.ServiceCaseHistory.Single(x => x.CaseId == opened.Id));
        check(accepted.ServiceCaseCode == opened.Code && opened.Channel == ServiceCaseChannel.Portal && opened.Category == ServiceCaseCategory.Complaint &&
              opened.CustomerId == nakhl && opened.Status == ServiceCaseStatus.New && history.Note.Contains("پرتال نماینده"),
            "PSVC: accepting a portal complaint opens a portal-channel service case for that customer.");
        check(portal.Portal(dealerUser, org, now).Requests.Single(x => x.Id == complaint.Id).ServiceCaseCode == opened.Code,
            "PSVC: the dealer sees the service case code on the request.");
        check(store.Read(d => d.CustomerTimelineEvents.Any(x => x.CustomerId == nakhl && x.SourceReference == opened.Code)),
            "PSVC: the case is recorded on the customer timeline.");

        var claim = portal.Submit(dealerUser, org, new SubmitPortalRequestCommand(Guid.NewGuid(), PortalRequestKind.Claim, "شکستگی کالا", "۱۰ کارتن آسیب دیده"), now);
        check(portal.ReviewForm(manager, org, claim.Id, now).DealerCustomers?.Count == 2, "PSVC: a claim without customer offers the dealer's customers to the reviewer.");
        try
        {
            portal.Review(manager, org, claim.Id, new ReviewPortalRequestCommand(claim.Version, PortalRequestStatus.Accepted, "تأیید"), now);
            check(false, "PSVC: accepting a claim without a customer must be rejected.");
        }
        catch (ArgumentException) { }
        try
        {
            portal.Review(manager, org, claim.Id, new ReviewPortalRequestCommand(claim.Version, PortalRequestStatus.Accepted, "تأیید", CustomerId: sepehr), now);
            check(false, "PSVC: a claim cannot be filed against another dealer's customer.");
        }
        catch (UnauthorizedAccessException) { }
        var claimed = portal.Review(manager, org, claim.Id, new ReviewPortalRequestCommand(claim.Version, PortalRequestStatus.Accepted, "تأیید", CustomerId: mahan), now);
        var claimCase = store.Read(d => d.ServiceCases.Single(x => x.Id == claimed.LinkedRecordId));
        check(claimCase is { Category: ServiceCaseCategory.ProductDefect, Priority: ServiceCasePriority.High } && claimCase.CustomerId == mahan,
            "PSVC: a damage claim becomes a high-priority product-defect case.");

        var reporting = new ReportingService(store, access, new DemoReportingFinanceSource());
        var query = new ReportQuery(DateOnly.FromDateTime(now.UtcDateTime.AddDays(-300)), DateOnly.FromDateTime(now.UtcDateTime));
        var report = reporting.Get(manager, org, query, now);
        var openCases = store.Read(d => d.ServiceCases.Count(x => x.CompanyId == "C01" && x.Status is not (ServiceCaseStatus.Resolved or ServiceCaseStatus.Closed)));
        check(report.Kpis.Single(x => x.Definition.Key == "serviceOpen").Value == openCases &&
              report.Kpis.Single(x => x.Definition.Key == "serviceCsat").Value == 80 &&
              report.Kpis.Any(x => x.Definition.Key == "serviceSla") && report.Facts.Any(x => x.Metric == "serviceOpen" && x.Url == $"/service/{opened.Id}"),
            "PSVC: reports show open cases, CSAT (4/5 = 80%) and SLA compliance with drill-down links.");
        check(!reporting.Get(finance, org, query with { Profile = "finance" }, now).Kpis.Any(x => x.Definition.Key.StartsWith("service")),
            "PSVC: service KPIs need Service.Read; finance does not get them.");
    }
}
