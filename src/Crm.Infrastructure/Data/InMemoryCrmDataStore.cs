using System.Threading;
using Crm.Application.Abstractions;
using Crm.Domain.Commercial;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.Service;
using Crm.Domain.Work;

namespace Crm.Infrastructure.Data;

public sealed class InMemoryCrmDataStore : ICrmDataStore, ICrmQuerySource, IDisposable
{
    private readonly ReaderWriterLockSlim _gate = new();
    private readonly CrmDataSet _data = SampleData.Create();

    public TResult Read<TResult>(Func<CrmDataSet, TResult> query)
    {
        _gate.EnterReadLock();
        try { return query(_data); }
        finally { _gate.ExitReadLock(); }
    }

    public TResult Write<TResult>(Func<CrmDataSet, TResult> command)
    {
        _gate.EnterWriteLock();
        try { return command(_data); }
        finally { _gate.ExitWriteLock(); }
    }

    // Queries are composed lazily and only enumerated inside the read lock, so writers never mutate a list mid-scan.
    public IQueryable<T> Query<T>() where T : class => _data.Table<T>().AsQueryable();

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(_ => query.ToList()));

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(_ => query.Count()));

    public Task<double?> AverageAsync(IQueryable<int> query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(_ => query.Any() ? (double?)query.Average() : null));

    public Task<decimal> SumAsync(IQueryable<decimal> query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(_ => query.Sum()));

    public void Dispose() => _gate.Dispose();
}

internal static class SampleData
{
    internal static readonly Guid DemoManagerId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    internal static readonly Guid DemoExpertId = Guid.Parse("10000000-0000-4000-8000-000000000002");
    internal static readonly Guid DemoFinanceManagerId = Guid.Parse("10000000-0000-4000-8000-000000000006");
    internal static readonly Guid DemoChannelManagerId = Guid.Parse("10000000-0000-4000-8000-000000000007");
    internal static readonly Guid DemoDealerUserId = Guid.Parse("10000000-0000-4000-8000-000000000008");
    internal static readonly Guid DemoExecutiveId = Guid.Parse("10000000-0000-4000-8000-000000000009");

    internal static CrmDataSet Create()
    {
        var data = new CrmDataSet();
        SeedOrganization(data);
        SeedRoles(data);
        SeedUsers(data);
        SeedIdentity(data);
        SeedCustomers(data);
        SeedCustomer360(data);
        SeedLeads(data);
        SeedOpportunities(data);
        SeedQuotes(data);
        SeedOrders(data);
        SeedDealers(data);
        SeedSelfService(data);
        SeedServiceCases(data);
        SeedWorkItems(data);
        return data;
    }

    private static void SeedOrganization(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow.AddYears(-1);
        data.Companies.AddRange([
            new Company(Guid.Parse("90000000-0000-4000-8000-000000000001"), "C01", "P-C01", "شرکت تولیدی ارقام"),
            new Company(Guid.Parse("90000000-0000-4000-8000-000000000002"), "C02", "P-C02", "شرکت بازرگانی افق")
        ]);
        data.OrganizationUnits.AddRange([
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000001"), "R01", "C01", "P-R01", "منطقه مرکز", OrganizationUnitType.Region),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000002"), "B01", "C01", "P-B01", "شعبه مرکزی", OrganizationUnitType.Branch, "R01"),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000003"), "B02", "C01", "P-B02", "شعبه اصفهان", OrganizationUnitType.Branch, "R01"),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000004"), "R02", "C01", "P-R02", "منطقه جنوب", OrganizationUnitType.Region),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000005"), "B03", "C01", "P-B03", "شعبه جنوب", OrganizationUnitType.Branch, "R02"),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000006"), "R21", "C02", "P-R21", "منطقه بازرگانی", OrganizationUnitType.Region),
            new OrganizationUnit(Guid.Parse("91000000-0000-4000-8000-000000000007"), "B21", "C02", "P-B21", "شعبه بازرگانی تهران", OrganizationUnitType.Branch, "R21")
        ]);
        data.Territories.AddRange([
            new Territory(Guid.Parse("92000000-0000-4000-8000-000000000001"), "T01", "C01", "P-T01", "صنایع کلیدی", TerritoryDimension.Industry, now),
            new Territory(Guid.Parse("92000000-0000-4000-8000-000000000002"), "T02", "C01", "P-T02", "کانال مستقیم", TerritoryDimension.Channel, now),
            new Territory(Guid.Parse("92000000-0000-4000-8000-000000000003"), "T21", "C02", "P-T21", "کانال بازرگانی", TerritoryDimension.Channel, now)
        ]);
    }

    private static void SeedUsers(CrmDataSet data)
    {
        data.Users.AddRange([
            new CrmUser(DemoExecutiveId, "مدیرعامل نمونه", "reporting.ceo", "CEO@DEMO.CRM", "EMP-1009", UserStatus.Active),
            new CrmUser(DemoManagerId, "مهدی نادری", "sales.manager", "MANAGER@DEMO.CRM", "EMP-1001", UserStatus.Active),
            new CrmUser(DemoExpertId, "سارا احمدی", "sales.expert", "EXPERT@DEMO.CRM", "EMP-1002", UserStatus.Active),
            new CrmUser(Guid.Parse("10000000-0000-4000-8000-000000000003"), "محمد رضایی", "sales.supervisor", "SUPERVISOR@DEMO.CRM", "EMP-1003", UserStatus.Active),
            new CrmUser(Guid.Parse("10000000-0000-4000-8000-000000000004"), "نرگس یوسفی", "sales.agent", "AGENT@DEMO.CRM", "EMP-1004", UserStatus.Active),
            new CrmUser(Guid.Parse("10000000-0000-4000-8000-000000000005"), "کاربر در انتظار", "pending.user", "PENDING@DEMO.CRM", "EMP-1005"),
            new CrmUser(DemoFinanceManagerId, "لیلا کریمی", "finance.manager", "FINANCE.MANAGER@DEMO.CRM", "EMP-1006", UserStatus.Active),
            new CrmUser(DemoChannelManagerId, "امیرحسین مرادی", "channel.manager", "CHANNEL.MANAGER@DEMO.CRM", "EMP-1007", UserStatus.Active),
            new CrmUser(DemoDealerUserId, "کاربر نماینده پایلوت", "dealer.user", "DEALER.USER@DEMO.CRM", "DLR-1001", UserStatus.Active)
        ]);
    }

    private static void SeedIdentity(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow.AddDays(-30);
        var assignments = new[]
        {
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000019"), DemoExecutiveId, "Executive", "مدیرعامل", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "نمونه گزارش فقط‌خواندنی اولویت ۹"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000001"), DemoManagerId, "SalesManager", "مدیر فروش", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "داده نمونه اولیه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000002"), DemoExpertId, "SalesExpert", "کارشناس فروش", "C01", "Branch", "B01", "شعبه مرکزی", now, null, DemoManagerId, "داده نمونه اولیه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000003"), Guid.Parse("10000000-0000-4000-8000-000000000003"), "SalesSupervisor", "سرپرست فروش", "C01", "Branch", "B02", "شعبه اصفهان", now, null, DemoManagerId, "داده نمونه اولیه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000004"), Guid.Parse("10000000-0000-4000-8000-000000000004"), "SalesExpert", "کارشناس فروش", "C01", "Branch", "B03", "شعبه جنوب", now, null, DemoManagerId, "داده نمونه اولیه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000005"), DemoExpertId, "CompanyMember", "عضو شرکت", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "عضویت پایه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000006"), Guid.Parse("10000000-0000-4000-8000-000000000003"), "CompanyMember", "عضو شرکت", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "عضویت پایه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000007"), Guid.Parse("10000000-0000-4000-8000-000000000004"), "CompanyMember", "عضو شرکت", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "عضویت پایه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000008"), Guid.Parse("10000000-0000-4000-8000-000000000005"), "CompanyMember", "عضو شرکت", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "عضویت پایه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000009"), Guid.Parse("10000000-0000-4000-8000-000000000005"), "SalesExpert", "کارشناس فروش", "C01", "Branch", "B01", "شعبه مرکزی", now, null, DemoManagerId, "داده نمونه اولیه"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000010"), DemoManagerId, "SalesManager", "مدیر فروش", "C02", "Company", "C02", "شرکت بازرگانی افق", now, null, DemoManagerId, "نمونه کاربر چندشرکتی"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000011"), DemoFinanceManagerId, "FinanceManager", "مدیر مالی", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "تأیید مشترک فروش و مالی"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000012"), DemoChannelManagerId, "ChannelManager", "مدیر کانال", "C01", "Company", "C01", "شرکت تولیدی ارقام", now, null, DemoManagerId, "مدیریت شبکه نمایندگان"),
            new UserRoleAssignment(Guid.Parse("70000000-0000-4000-8000-000000000013"), DemoDealerUserId, "DealerUser", "کاربر نماینده", "C01", "Dealer", "P-D01", "نماینده پایلوت جنوب", now, null, DemoChannelManagerId, "دسترسی محدود به نماینده پایلوت")
        };
        data.UserRoleAssignments.AddRange(assignments);
        data.ExternalIdentities.AddRange([
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000001"), DemoManagerId, "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-manager-sub", "MANAGER@DEMO.CRM", now),
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000002"), DemoExpertId, "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-expert-sub", "EXPERT@DEMO.CRM", now),
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000003"), Guid.Parse("10000000-0000-4000-8000-000000000003"), "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-supervisor-sub", "SUPERVISOR@DEMO.CRM", now),
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000004"), Guid.Parse("10000000-0000-4000-8000-000000000004"), "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-agent-sub", "AGENT@DEMO.CRM", now),
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000005"), DemoChannelManagerId, "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-channel-manager-sub", "CHANNEL.MANAGER@DEMO.CRM", now),
            new ExternalIdentity(Guid.Parse("80000000-0000-4000-8000-000000000006"), DemoDealerUserId, "CorporateOidc", "https://login.example.test/tenant/v2.0", "demo-dealer-user-sub", "DEALER.USER@DEMO.CRM", now)
        ]);
    }

    private static void SeedCustomers(CrmDataSet data)
    {
        var items = new[]
        {
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000001"), "CUS-00481", "صنایع غذایی سپهر", "تهران", "سارا احمدی", "C01", "B01", "شعبه مرکزی", "T01", "کلیدی", 3_500_000_000m, CustomerKind.Legal, "10101234567", "02188776655", "INFO@SEPEHR.TEST"),
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000002"), "CUS-00480", "گروه تولیدی آریا", "اصفهان", "محمد رضایی", "C01", "B02", "شعبه اصفهان", "T02", "رشد", 2_000_000_000m, CustomerKind.Legal, "10207654321", "03132221100", "SALES@ARYA.TEST"),
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000003"), "CUS-00479", "بازرگانی نخل جنوب", "اهواز", "مهدی نادری", "C01", "B03", "شعبه جنوب", "T02", "استاندارد", 2_500_000_000m),
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000004"), "CUS-00478", "فروشگاه زنجیره‌ای ماهان", "شیراز", "نرگس یوسفی", "C01", "B03", "شعبه جنوب", "T01", "کلیدی", 1_800_000_000m),
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000005"), "CUS-00601", "بازرگانی دریا", "تهران", "مهدی نادری", "C02", "B21", "شعبه بازرگانی تهران", "T21", "کلیدی", 4_000_000_000m),
            new Customer(Guid.Parse("20000000-0000-4000-8000-000000000006"), "CUS-00602", "تجارت آفتاب", "کرج", "مهدی نادری", "C02", "B21", "شعبه بازرگانی تهران", "T21", "رشد", 1_500_000_000m)
        };
        foreach (var customer in items) customer.Activate();
        items[0].SetFinancialProjection(1_840_000_000m, 3_500_000_000m);
        items[1].SetFinancialProjection(720_000_000m, 2_000_000_000m);
        items[2].SetFinancialProjection(2_480_000_000m, 2_500_000_000m);
        items[0].MarkSynchronized("ERP Projection", DateTimeOffset.UtcNow.AddHours(-2));
        items[1].MarkSynchronized("ERP Projection", DateTimeOffset.UtcNow.AddHours(-6));
        items[2].MarkSynchronized("ERP Projection", DateTimeOffset.UtcNow.AddDays(-4));
        items[4].MarkSynchronized("ERP Projection", DateTimeOffset.UtcNow.AddHours(-3));
        data.Customers.AddRange(items);
    }

    private static void SeedCustomer360(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var manager = DemoManagerId;
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var arya = Guid.Parse("20000000-0000-4000-8000-000000000002");
        var nakhl = Guid.Parse("20000000-0000-4000-8000-000000000003");
        var mahan = Guid.Parse("20000000-0000-4000-8000-000000000004");
        data.CustomerContacts.AddRange([
            new CustomerContact(Guid.Parse("21000000-0000-4000-8000-000000000001"), "C01", sepehr, "علی رستگار", "مدیر تدارکات", "02188776655", "A.ROSTEGAR@SEPEHR.TEST", true, ContactConsentStatus.Granted,
                new ContactPersonDetails("آقای", "علی", "رستگار", "09121234567", "214", "هماهنگی خرید فصلی")),
            new CustomerContact(Guid.Parse("21000000-0000-4000-8000-000000000002"), "C01", sepehr, "مینا کاظمی", "مالی", "09123334455", "FINANCE@SEPEHR.TEST", false, ContactConsentStatus.Unknown),
            new CustomerContact(Guid.Parse("21000000-0000-4000-8000-000000000003"), "C01", arya, "رضا محمودی", "مدیر فروش", "09131112233", "R.MAHMOUDI@ARYA.TEST", true, ContactConsentStatus.Granted)
        ]);
        data.CustomerProfiles.Add(new CustomerProfile(sepehr, "C01", CustomerKind.Legal, new CustomerProfileData(
            "تولیدی", "آقای", "علی", "رستگار", "مدیر خرید", "09121234567", null, "02188776655", "02188776656", "تهران",
            "مشتری کلیدی خط تولید کنسرو", "شرکت صنایع غذایی سپهر (سهامی خاص)", "411111111111", "123456", null, null, null,
            "۵۱ تا ۲۰۰ نفر", "1101-481", new DateOnly(2019, 5, 31), null, "BR-01-481", "نمایشگاه", "https://sepehr.test")));
        data.CustomerAddresses.AddRange([
            new CustomerAddress(Guid.Parse("22000000-0000-4000-8000-000000000001"), "C01", sepehr, CustomerAddressType.Registered, "دفتر مرکزی", "تهران", "تهران", "خیابان ولیعصر، پلاک ۱۲۰", "1599911111", true),
            new CustomerAddress(Guid.Parse("22000000-0000-4000-8000-000000000002"), "C01", sepehr, CustomerAddressType.Shipping, "انبار", "البرز", "کرج", "شهرک صنعتی بهارستان", "3187612345", false),
            new CustomerAddress(Guid.Parse("22000000-0000-4000-8000-000000000003"), "C01", arya, CustomerAddressType.Registered, "کارخانه", "اصفهان", "اصفهان", "شهرک صنعتی محمودآباد", "8168912345", true)
        ]);
        data.CustomerOwnershipHistory.AddRange([
            new CustomerOwnershipHistory(Guid.Parse("23000000-0000-4000-8000-000000000001"), "C01", sepehr, "B01", "T01", "سارا احمدی", now.AddMonths(-12), "داده پایه", manager),
            new CustomerOwnershipHistory(Guid.Parse("23000000-0000-4000-8000-000000000002"), "C01", arya, "B02", "T02", "محمد رضایی", now.AddMonths(-10), "داده پایه", manager),
            new CustomerOwnershipHistory(Guid.Parse("23000000-0000-4000-8000-000000000003"), "C01", nakhl, "B03", "T02", "مهدی نادری", now.AddMonths(-8), "داده پایه", manager)
        ]);
        data.CustomerTimelineEvents.AddRange([
            new CustomerTimelineEvent(Guid.Parse("24000000-0000-4000-8000-000000000001"), "C01", sepehr, CustomerTimelineType.Created, "مشتری ایجاد شد", "ثبت اولیه Master Data", now.AddMonths(-12), "CRM", "CUS-00481", manager),
            new CustomerTimelineEvent(Guid.Parse("24000000-0000-4000-8000-000000000002"), "C01", sepehr, CustomerTimelineType.FinancialSync, "اطلاعات مالی همگام شد", "مانده و سقف اعتبار از ERP", now.AddHours(-2), "ERP Projection", "ERP-CUS-481", null),
            new CustomerTimelineEvent(Guid.Parse("24000000-0000-4000-8000-000000000003"), "C01", arya, CustomerTimelineType.Created, "مشتری ایجاد شد", "ثبت اولیه Master Data", now.AddMonths(-10), "CRM", "CUS-00480", manager)
        ]);
        data.CustomerDuplicateCandidates.Add(new CustomerDuplicateCandidate(
            Guid.Parse("25000000-0000-4000-8000-000000000001"), "C01", nakhl, mahan, 62,
            "شماره تماس تاریخی مشابه، آدرس حمل مشترک", now.AddDays(-2)));
    }

    private static void SeedLeads(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var agentId = Guid.Parse("10000000-0000-4000-8000-000000000004");
        var first = new Lead(Guid.Parse("30000000-0000-4000-8000-000000000001"), "LD-1405-118",
            "پایدار انرژی خاور", "سامان رستگار", "نمایشگاه", "سارا احمدی", "C01", "B01", "T01",
            ownerUserId: DemoExpertId, phone: "09125550118", email: "INFO@PAYDAR.TEST", firstContactDueAtUtc: now.AddHours(-20));
        first.Assign(DemoExpertId, "سارا احمدی", now.AddDays(-1), now.AddHours(-20), "تخصیص نمونه");
        first.MarkContacted(now.AddHours(-18), "جلسه معرفی راهکار", now.AddDays(1), "تماس موفق");
        first.Qualify(86, "بودجه و نیاز تأیید شد");

        var second = new Lead(Guid.Parse("30000000-0000-4000-8000-000000000002"), "LD-1405-117",
            "تجارت نوین پارس", "علی محرابی", "وب‌سایت", "مهدی نادری", "C01", "B03", "T02",
            ownerUserId: DemoManagerId, phone: "09123330117", firstContactDueAtUtc: now.AddHours(-2));
        second.Assign(DemoManagerId, "مهدی نادری", now.AddHours(-6), now.AddHours(-2), "توزیع خودکار");

        var third = new Lead(Guid.Parse("30000000-0000-4000-8000-000000000003"), "LD-1405-116",
            "راهکاران صنعت شرق", "شیوا رستمی", "معرفی مشتری", "نرگس یوسفی", "C01", "B03", "T01",
            ownerUserId: agentId, phone: "09131110116", email: "OFFICE@RAHKARAN.TEST", firstContactDueAtUtc: now.AddHours(-8));
        third.Assign(agentId, "نرگس یوسفی", now.AddHours(-12), now.AddHours(-8), "تخصیص شعبه جنوب");
        third.MarkContacted(now.AddHours(-7), "ارسال مطالعه موردی", now.AddHours(18), "نیاز به پرورش");

        var fourth = new Lead(Guid.Parse("30000000-0000-4000-8000-000000000004"), "LD-1405-201",
            "پخش ساحل", "آرش کاویانی", "کمپین بازرگانی", "مهدی نادری", "C02", "B21", "T21",
            ownerUserId: DemoManagerId, phone: "09127770201", firstContactDueAtUtc: now.AddHours(2));
        fourth.Assign(DemoManagerId, "مهدی نادری", now.AddHours(-2), now.AddHours(2), "توزیع خودکار");
        data.Leads.AddRange([first, second, third, fourth]);

        data.LeadStatusHistory.AddRange([
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000001"), "C01", "B01", "T01", first.Id, null, LeadStatus.New, "ثبت نمونه", DemoManagerId, now.AddDays(-1).AddMinutes(-5)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000002"), "C01", "B01", "T01", first.Id, LeadStatus.New, LeadStatus.Assigned, "تخصیص نمونه", DemoManagerId, now.AddDays(-1)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000003"), "C01", "B01", "T01", first.Id, LeadStatus.Assigned, LeadStatus.Contacted, "تماس موفق", DemoExpertId, now.AddHours(-18)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000004"), "C01", "B01", "T01", first.Id, LeadStatus.Contacted, LeadStatus.Qualified, "بودجه و نیاز تأیید شد", DemoExpertId, now.AddHours(-17)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000005"), "C01", "B03", "T02", second.Id, LeadStatus.New, LeadStatus.Assigned, "توزیع خودکار", DemoManagerId, now.AddHours(-6)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000006"), "C01", "B03", "T01", third.Id, LeadStatus.New, LeadStatus.Assigned, "تخصیص شعبه جنوب", DemoManagerId, now.AddHours(-12)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000007"), "C01", "B03", "T01", third.Id, LeadStatus.Assigned, LeadStatus.Contacted, "نیاز به پرورش", agentId, now.AddHours(-7)),
            new LeadStatusHistory(Guid.Parse("31000000-0000-4000-8000-000000000008"), "C02", "B21", "T21", fourth.Id, LeadStatus.New, LeadStatus.Assigned, "توزیع خودکار", DemoManagerId, now.AddHours(-2))
        ]);
    }

    private static void SeedOpportunities(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var supervisorId = Guid.Parse("10000000-0000-4000-8000-000000000003");
        var solution = new Opportunity(Guid.Parse("40000000-0000-4000-8000-000000000001"), "OP-2041", "تأمین سالانه مواد اولیه سپهر", "صنایع غذایی سپهر", Guid.Parse("20000000-0000-4000-8000-000000000001"), 14_800_000_000m, "سارا احمدی", "C01", "B01", "T01", DemoExpertId, expectedCloseAtUtc: now.AddDays(18), source: "نمایشگاه");
        solution.Advance(); solution.Advance(); solution.Advance();
        solution.RecordActivity(now.AddDays(-2), "دموی فنی", now.AddDays(1));
        var negotiation = new Opportunity(Guid.Parse("40000000-0000-4000-8000-000000000002"), "OP-2040", "قرارداد توزیع منطقه مرکز", "گروه تولیدی آریا", Guid.Parse("20000000-0000-4000-8000-000000000002"), 9_200_000_000m, "محمد رضایی", "C01", "B02", "T02", supervisorId, expectedCloseAtUtc: now.AddDays(10), source: "معرفی مشتری");
        negotiation.Advance(); negotiation.Advance(); negotiation.Advance(); negotiation.Advance();
        negotiation.RecordActivity(now.AddDays(-9), "بازبینی شرایط قرارداد", now.AddDays(2));
        var quote = new Opportunity(Guid.Parse("40000000-0000-4000-8000-000000000003"), "OP-2039", "فروش ویژه فصل پاییز", "بازرگانی نخل جنوب", Guid.Parse("20000000-0000-4000-8000-000000000003"), 4_100_000_000m, "مهدی نادری", "C01", "B03", "T02", DemoManagerId, expectedCloseAtUtc: now.AddDays(5), source: "کمپین پاییز");
        quote.Advance(); quote.Advance(); quote.Advance(); quote.Advance(); quote.Advance();
        quote.RecordActivity(now.AddDays(-1), "پیگیری تصمیم نهایی", now.AddHours(20));
        var commerce = new Opportunity(Guid.Parse("40000000-0000-4000-8000-000000000004"), "OP-2201", "قرارداد تأمین بازرگانی", "بازرگانی دریا", Guid.Parse("20000000-0000-4000-8000-000000000005"), 7_300_000_000m, "مهدی نادری", "C02", "B21", "T21", DemoManagerId, expectedCloseAtUtc: now.AddDays(25), source: "مراجعه مستقیم");
        commerce.Advance(); commerce.Advance();
        commerce.RecordActivity(now.AddDays(-3), "جلسه کشف نیاز", now.AddDays(3));
        data.Opportunities.AddRange([solution, negotiation, quote, commerce]);
        data.OpportunityStageHistory.AddRange([
            new OpportunityStageHistory(Guid.Parse("41000000-0000-4000-8000-000000000001"), "C01", "B01", "T01", solution.Id, OpportunityStage.Qualified, solution.Stage, solution.Probability, "ارائه راهکار", DemoExpertId, now.AddDays(-3)),
            new OpportunityStageHistory(Guid.Parse("41000000-0000-4000-8000-000000000002"), "C01", "B02", "T02", negotiation.Id, OpportunityStage.SolutionOffer, negotiation.Stage, negotiation.Probability, "ورود به مذاکره", supervisorId, now.AddDays(-10)),
            new OpportunityStageHistory(Guid.Parse("41000000-0000-4000-8000-000000000003"), "C01", "B03", "T02", quote.Id, OpportunityStage.Negotiation, quote.Stage, quote.Probability, "تعهد خرید شفاهی", DemoManagerId, now.AddDays(-2)),
            new OpportunityStageHistory(Guid.Parse("41000000-0000-4000-8000-000000000004"), "C02", "B21", "T21", commerce.Id, OpportunityStage.Discovery, commerce.Stage, commerce.Probability, "نیاز تأیید شد", DemoManagerId, now.AddDays(-4))
        ]);
        data.OpportunityActivities.AddRange([
            new OpportunityActivity(Guid.Parse("42000000-0000-4000-8000-000000000001"), "C01", "B01", "T01", solution.Id, OpportunityActivityType.Meeting, "جلسه راهکار", "نیازهای فنی جمع‌بندی شد", now.AddDays(-2), DemoExpertId, "دموی فنی", now.AddDays(1)),
            new OpportunityActivity(Guid.Parse("42000000-0000-4000-8000-000000000002"), "C01", "B02", "T02", negotiation.Id, OpportunityActivityType.Email, "ارسال پیش‌نویس قرارداد", "نسخه حقوقی ارسال شد", now.AddDays(-9), supervisorId, "بازبینی شرایط قرارداد", now.AddDays(2)),
            new OpportunityActivity(Guid.Parse("42000000-0000-4000-8000-000000000003"), "C01", "B03", "T02", quote.Id, OpportunityActivityType.Call, "تماس تصمیم نهایی", "خرید در حال تأیید مدیرعامل است", now.AddDays(-1), DemoManagerId, "پیگیری تصمیم نهایی", now.AddHours(20)),
            new OpportunityActivity(Guid.Parse("42000000-0000-4000-8000-000000000004"), "C02", "B21", "T21", commerce.Id, OpportunityActivityType.Visit, "بازدید مشتری", "دامنه نیاز مشخص شد", now.AddDays(-3), DemoManagerId, "جلسه کشف نیاز", now.AddDays(3))
        ]);
    }

    private static void SeedQuotes(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var approved = new Quote(Guid.Parse("50000000-0000-4000-8000-000000000001"), "Q-1405-030", "صنایع غذایی سپهر", Guid.Parse("20000000-0000-4000-8000-000000000001"), "تأمین سالانه مواد اولیه سپهر", Guid.Parse("40000000-0000-4000-8000-000000000001"), 14_800_000_000m, 9, 22, "C01", "B01", "T01");
        approved.Approve();
        approved.MarkSent(now.AddDays(-9));
        approved.Accept(now.AddDays(-8));
        data.Quotes.AddRange([
            new Quote(Guid.Parse("50000000-0000-4000-8000-000000000002"), "Q-1405-031", "بازرگانی نخل جنوب", Guid.Parse("20000000-0000-4000-8000-000000000003"), "فروش ویژه فصل پاییز", Guid.Parse("40000000-0000-4000-8000-000000000003"), 4_100_000_000m, 12, 18, "C01", "B03", "T02"),
            approved,
            new Quote(Guid.Parse("50000000-0000-4000-8000-000000000003"), "Q-1405-029", "گروه تولیدی آریا", Guid.Parse("20000000-0000-4000-8000-000000000002"), "قرارداد توزیع منطقه مرکز", Guid.Parse("40000000-0000-4000-8000-000000000002"), 9_200_000_000m, 5, 28, "C01", "B02", "T02"),
            new Quote(Guid.Parse("50000000-0000-4000-8000-000000000004"), "Q-1405-101", "بازرگانی دریا", Guid.Parse("20000000-0000-4000-8000-000000000005"), "قرارداد تأمین بازرگانی", Guid.Parse("40000000-0000-4000-8000-000000000004"), 7_300_000_000m, 8, 24, "C02", "B21", "T21")
        ]);
        foreach (var quote in data.Quotes)
        {
            var cost = quote.NetAmount * (1 - quote.MarginPercent / 100m);
            data.QuoteLines.Add(new QuoteLine(Guid.NewGuid(), quote.CompanyId, quote.BranchId, quote.TerritoryId,
                quote.Id, "PRD-1001", "بسته مواد اولیه استاندارد", "بسته", 1, quote.GrossAmount, cost,
                quote.DiscountPercent, "ERP Mock / Seed Snapshot", now.AddDays(-20)));
            data.QuoteStatusHistory.Add(new QuoteStatusHistory(Guid.NewGuid(), quote.CompanyId, quote.BranchId,
                quote.TerritoryId, quote.Id, null, quote.Status, "داده نمونه اولیه", DemoManagerId, now.AddDays(-10)));
        }
        data.QuoteApprovalDecisions.Add(new QuoteApprovalDecision(Guid.NewGuid(), approved.CompanyId, approved.BranchId,
            approved.TerritoryId, approved.Id, QuoteApprovalRole.CommercialManager, QuoteDecision.Approved,
            "تأیید نمونه مدیر تجاری", DemoManagerId, now.AddDays(-8)));
    }

    private static void SeedOrders(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var quote = data.Quotes.Single(x => x.Id == Guid.Parse("50000000-0000-4000-8000-000000000001"));
        var order = new OrderRequest(Guid.Parse("55000000-0000-4000-8000-000000000001"), "OR-1405-001",
            quote.Id, $"{quote.Code}/R{quote.Revision}", quote.CustomerId, quote.Customer, quote.OpportunityId,
            DemoExpertId, quote.CompanyId, quote.BranchId, quote.TerritoryId, quote.CurrencyCode, quote.NetAmount,
            "crm-seed-order-1405-001");
        data.OrderRequests.Add(order);
        data.OrderStatusHistory.Add(new OrderStatusHistory(Guid.Parse("55100000-0000-4000-8000-000000000001"),
            order.CompanyId, order.BranchId, order.TerritoryId, order.Id, null, OrderRequestStatus.Draft,
            "ایجاد از پیشنهاد پذیرفته‌شده نمونه", "CRM Seed", DemoManagerId, now.AddDays(-7)));
        order.ApplyCreditSnapshot(20_000_000_000m, 1_840_000_000m, 420_000_000m, true,
            "بدهی سررسیدشده و Hold حسابداری", "Accounting Mock / Credit API v1", now.AddHours(-3), now.AddHours(-3));
        data.OrderCreditDecisions.Add(new OrderCreditDecision(Guid.Parse("55200000-0000-4000-8000-000000000001"),
            order.CompanyId, order.BranchId, order.TerritoryId, order.Id, OrderCreditDecisionType.Held,
            order.CreditLimit, order.CreditUsed, order.OverdueAmount, order.AvailableCredit,
            order.CreditReason!, order.CreditSource!, DemoManagerId, now.AddHours(-3)));
        data.OrderStatusHistory.Add(new OrderStatusHistory(Guid.Parse("55100000-0000-4000-8000-000000000002"),
            order.CompanyId, order.BranchId, order.TerritoryId, order.Id, OrderRequestStatus.Draft,
            OrderRequestStatus.CreditHold, order.CreditReason!, order.CreditSource!, DemoManagerId, now.AddHours(-3)));
    }

    private static void SeedDealers(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        // Channel periods are Jalali months (ChannelPeriod).
        var (monthStart, monthEnd) = ChannelPeriod.MonthOf(now);
        var dealer = new Dealer(Guid.Parse("a0000000-0000-4000-8000-000000000001"), "P-D01", "DLR-0001",
            "شرکت پخش نماینده جنوب", "نماینده پایلوت جنوب", "C01", "B03", "T02", "اهواز",
            "14001234567", "06133334444", "INFO@PILOT-DEALER.TEST", DemoChannelManagerId);
        data.Dealers.Add(dealer);

        var contract = new DealerContract(Guid.Parse("a1000000-0000-4000-8000-000000000001"), "C01", "B03", "T02",
            dealer.Id, "CNT-D01-1405", now.AddMonths(-2), now.AddMonths(10), 84_000_000_000m,
            "تسویه ۳۰ روزه پس از تأیید فاکتور", DemoManagerId);
        contract.Submit("ارسال قرارداد پایلوت برای تأیید کانال");
        contract.Approve(DemoChannelManagerId, now.AddMonths(-2).AddHours(2), "قرارداد و تضمین‌ها بررسی شد");
        data.DealerContracts.Add(contract);

        var territory = new DealerTerritoryAssignment(Guid.Parse("a2000000-0000-4000-8000-000000000001"),
            "C01", "B03", "T02", dealer.Id, true, now.AddMonths(-2), now.AddMonths(10), DemoManagerId);
        territory.Approve(DemoChannelManagerId, now.AddMonths(-2).AddHours(3), "Territory کانال جنوب به‌صورت انحصاری تخصیص یافت");
        data.DealerTerritoryAssignments.Add(territory);

        dealer.SubmitForApproval("پرونده، قرارداد و Territory تکمیل شد");
        data.DealerStatusHistory.Add(new DealerStatusHistory(Guid.Parse("a3000000-0000-4000-8000-000000000001"),
            "C01", "B03", "T02", dealer.Id, null, DealerStatus.Draft, "ایجاد پرونده نماینده پایلوت",
            DemoManagerId, now.AddMonths(-2).AddHours(-1)));
        data.DealerStatusHistory.Add(new DealerStatusHistory(Guid.Parse("a3000000-0000-4000-8000-000000000002"),
            "C01", "B03", "T02", dealer.Id, DealerStatus.Draft, DealerStatus.PendingApproval,
            "پرونده، قرارداد و Territory تکمیل شد", DemoManagerId, now.AddMonths(-2).AddHours(4)));
        dealer.Activate("تأیید نهایی مدیر کانال", now.AddMonths(-2).AddHours(5));
        data.DealerStatusHistory.Add(new DealerStatusHistory(Guid.Parse("a3000000-0000-4000-8000-000000000003"),
            "C01", "B03", "T02", dealer.Id, DealerStatus.PendingApproval, DealerStatus.Active,
            "تأیید نهایی مدیر کانال", DemoChannelManagerId, now.AddMonths(-2).AddHours(5)));

        data.DealerTargets.Add(new DealerTarget(Guid.Parse("a4000000-0000-4000-8000-000000000001"),
            "C01", "B03", "T02", dealer.Id, monthStart, monthEnd, 7_000_000_000m,
            "برنامه فروش مصوب ۱۴۰۵", DemoChannelManagerId));
        data.DealerFinancialSnapshots.Add(new DealerFinancialSnapshot(Guid.Parse("a5000000-0000-4000-8000-000000000001"),
            "C01", "B03", "T02", dealer.Id, 12_000_000_000m, 4_350_000_000m, 3_980_000_000m,
            620_000_000m, "Accounting Mock / Dealer Ledger v1", now.AddMinutes(-8)));
        data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.Parse("a6000000-0000-4000-8000-000000000001"),
            "C01", "B03", "T02", dealer.Id, monthStart, monthEnd, 6_850_000_000m, 18,
            "ERP/BI Mock / Dealer Sales v1", now.AddMinutes(-12)));
        data.DealerCustomerAssignments.AddRange([
            new DealerCustomerAssignment(Guid.Parse("a7000000-0000-4000-8000-000000000001"), "C01", "B03", "T02",
                dealer.Id, Guid.Parse("20000000-0000-4000-8000-000000000003"), now.AddMonths(-2),
                DemoChannelManagerId, "سبد مشتریان نماینده پایلوت"),
            new DealerCustomerAssignment(Guid.Parse("a7000000-0000-4000-8000-000000000002"), "C01", "B03", "T01",
                dealer.Id, Guid.Parse("20000000-0000-4000-8000-000000000004"), now.AddMonths(-1),
                DemoChannelManagerId, "تخصیص مشتری کلیدی کانال")
        ]);
        SeedDealerIncentives(data, dealer, monthStart, monthEnd, now);
    }

    /// <summary>Two more active dealers and last month's sales so the commission run and the ranking have something to compare.</summary>
    private static void SeedDealerIncentives(CrmDataSet data, Dealer pilot, DateTimeOffset monthStart, DateTimeOffset monthEnd, DateTimeOffset now)
    {
        var previousMonth = ChannelPeriod.Previous(monthStart).From;
        data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.Parse("a6000000-0000-4000-8000-000000000002"),
            "C01", "B03", "T02", pilot.Id, previousMonth, monthStart, 6_100_000_000m, 16, "ERP/BI Mock / Dealer Sales v1", monthStart.AddHours(2)));

        var others = new[]
        {
            (Id: "b0000000-0000-4000-8000-000000000002", Business: "P-D02", Code: "DLR-0002", Legal: "شرکت توزیع مرکز", Trade: "نماینده مرکز", Branch: "B01", Territory: "T01",
             City: "تهران", Target: 5_000_000_000m, Sales: 6_200_000_000m, Previous: 5_000_000_000m, Balance: 2_400_000_000m, Overdue: 0m),
            (Id: "b0000000-0000-4000-8000-000000000003", Business: "P-D03", Code: "DLR-0003", Legal: "شرکت بازرگانی زاینده", Trade: "نماینده اصفهان", Branch: "B02", Territory: "T02",
             City: "اصفهان", Target: 4_000_000_000m, Sales: 2_400_000_000m, Previous: 3_100_000_000m, Balance: 2_100_000_000m, Overdue: 900_000_000m)
        };
        var index = 2;
        foreach (var item in others)
        {
            var dealer = new Dealer(Guid.Parse(item.Id), item.Business, item.Code, item.Legal, item.Trade, "C01", item.Branch, item.Territory,
                item.City, null, null, null, DemoChannelManagerId);
            dealer.SubmitForApproval("پرونده نمونه رتبه‌بندی");
            dealer.Activate("فعال‌سازی نمونه", now.AddMonths(-3));
            data.Dealers.Add(dealer);
            var contract = new DealerContract(Guid.Parse($"b1000000-0000-4000-8000-00000000000{index}"), "C01", item.Branch, item.Territory,
                dealer.Id, $"CNT-D0{index}-1405", now.AddMonths(-3), now.AddMonths(9), item.Target * 12, "تسویه ۴۵ روزه", DemoManagerId);
            contract.Submit("قرارداد نمونه");
            contract.Approve(DemoChannelManagerId, now.AddMonths(-3).AddHours(1), "تأیید نمونه");
            data.DealerContracts.Add(contract);
            data.DealerTargets.Add(new DealerTarget(Guid.Parse($"b4000000-0000-4000-8000-00000000000{index}"), "C01", item.Branch, item.Territory,
                dealer.Id, monthStart, monthEnd, item.Target, "برنامه فروش مصوب ۱۴۰۵", DemoChannelManagerId));
            data.DealerFinancialSnapshots.Add(new DealerFinancialSnapshot(Guid.Parse($"b5000000-0000-4000-8000-00000000000{index}"), "C01", item.Branch,
                item.Territory, dealer.Id, item.Balance * 3, item.Balance, item.Balance, item.Overdue, "Accounting Mock / Dealer Ledger v1", now.AddMinutes(-9)));
            data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.Parse($"b6000000-0000-4000-8000-00000000000{index}"), "C01", item.Branch,
                item.Territory, dealer.Id, monthStart, monthEnd, item.Sales, 9, "ERP/BI Mock / Dealer Sales v1", now.AddMinutes(-11)));
            data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.Parse($"b6100000-0000-4000-8000-00000000000{index}"), "C01", item.Branch,
                item.Territory, dealer.Id, previousMonth, monthStart, item.Previous, 8, "ERP/BI Mock / Dealer Sales v1", monthStart.AddHours(2)));
            index++;
        }

        data.DealerCommissionPlans.Add(new DealerCommissionPlan(Guid.Parse("b9000000-0000-4000-8000-000000000001"), "C01",
            "طرح کمیسیون پله‌ای ۱۴۰۵",
            [new CommissionTier(0, 0), new CommissionTier(80, 1.5m), new CommissionTier(100, 2.5m), new CommissionTier(120, 3.5m)],
            DemoChannelManagerId));
    }

    private static void SeedSelfService(CrmDataSet data)
    {
        var dealer = data.Dealers.Single(x => x.DealerId == "P-D01");
        data.PortalRequests.Add(new(Guid.Parse("71000000-0000-4000-8000-000000000001"), dealer.CompanyId,
            dealer.BranchId, dealer.TerritoryId, dealer.Id, DemoDealerUserId, Guid.Parse("71000000-0000-4000-8000-000000000002"),
            new string('0',64), Crm.Domain.SelfService.PortalRequestKind.Complaint, "پیگیری تحویل نمونه", "لطفاً زمان تحویل سفارش آزمایشی اعلام شود.",
            null,null,0,0,null,null,null,null));
        var customer = data.Customers.Single(x => x.Id == Guid.Parse("20000000-0000-4000-8000-000000000001"));
        foreach (var owner in new[] { DemoManagerId, DemoExpertId })
            data.MobileVisits.Add(new(Guid.NewGuid(), customer.CompanyId, customer.BranchId, customer.TerritoryId,
                customer.Id, owner, new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero), "بررسی نیاز مشتری و پیگیری همکاری"));
    }

    private static void SeedRoles(CrmDataSet data)
    {
        data.RoleDefinitions.AddRange(Crm.Infrastructure.Identity.RoleCatalogSeed.Roles());
        data.RolePermissionGrants.AddRange(Crm.Infrastructure.Identity.RoleCatalogSeed.Grants());
    }

    private static void SeedServiceCases(CrmDataSet data)
    {
        var now = DateTimeOffset.UtcNow;
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var nakhl = Guid.Parse("20000000-0000-4000-8000-000000000003");
        var mahan = Guid.Parse("20000000-0000-4000-8000-000000000004");

        // Breached critical complaint: already escalated to level 1 so the background job has nothing new to do at startup.
        var breached = new ServiceCase(Guid.Parse("a0000000-0000-4000-8000-000000000001"), "CS-1405-1001",
            "تأخیر سه‌روزه در تحویل محموله اقلام یخچالی", "محموله سفارش اخیر با تأخیر و دمای نامناسب تحویل شده است.",
            sepehr, "C01", "B01", "T01", ServiceCaseCategory.Delivery, ServiceCaseChannel.Phone, ServiceCasePriority.Critical,
            DemoManagerId, now.AddHours(-10));
        breached.Triage(ServiceCasePriority.Critical, DemoExpertId, "سارا احمدی", now.AddHours(-9.5));
        breached.StartWork(now.AddHours(-9));
        breached.Escalate(now);

        var waiting = new ServiceCase(Guid.Parse("a0000000-0000-4000-8000-000000000002"), "CS-1405-1002",
            "مغایرت مبلغ فاکتور با پیشنهاد قیمت", "مشتری مستندات پرداخت را ارسال نکرده است.",
            nakhl, "C01", "B03", "T02", ServiceCaseCategory.Invoice, ServiceCaseChannel.Email, ServiceCasePriority.Medium,
            DemoManagerId, now.AddHours(-20));
        waiting.Triage(ServiceCasePriority.Medium, DemoManagerId, "مهدی نادری", now.AddHours(-19));
        waiting.StartWork(now.AddHours(-18));
        waiting.WaitOnCustomer(now.AddHours(-6));

        var fresh = new ServiceCase(Guid.Parse("a0000000-0000-4000-8000-000000000003"), "CS-1405-1003",
            "درخواست راهنمای نگهداری تجهیزات", "پرسش درباره دوره سرویس دوره‌ای.",
            mahan, "C01", "B03", "T01", ServiceCaseCategory.Inquiry, ServiceCaseChannel.Portal, ServiceCasePriority.Low,
            DemoManagerId, now.AddMinutes(-40));

        var closed = new ServiceCase(Guid.Parse("a0000000-0000-4000-8000-000000000004"), "CS-1405-1000",
            "نقص بسته‌بندی در محموله قبلی", "دو کارتن آسیب‌دیده گزارش شد.",
            sepehr, "C01", "B01", "T01", ServiceCaseCategory.ProductDefect, ServiceCaseChannel.Visit, ServiceCasePriority.High,
            DemoExpertId, now.AddDays(-5));
        closed.Triage(ServiceCasePriority.High, DemoExpertId, "سارا احمدی", now.AddDays(-5).AddHours(1));
        closed.StartWork(now.AddDays(-5).AddHours(2));
        closed.Resolve("ضعف استحکام کارتن در مسیر حمل طولانی", "تغییر مشخصات کارتن و افزودن کنترل کیفیت قبل از بارگیری",
            "ارسال جایگزین اقلام آسیب‌دیده", now.AddDays(-4.5));
        closed.Close(4, "رسیدگی سریع بود.", now.AddDays(-4));

        data.ServiceCases.AddRange([breached, waiting, fresh, closed]);
        foreach (var item in data.ServiceCases)
            data.ServiceCaseHistory.Add(new ServiceCaseHistory(Guid.NewGuid(), item.Id, item.CompanyId, item.BranchId, item.TerritoryId,
                null, item.Status, "داده نمونه", "پرونده نمونه اولویت خدمات", DemoManagerId, item.OpenedAtUtc));
    }

    private static void SeedWorkItems(CrmDataSet data)
    {
        data.WorkItems.AddRange([
            new CrmWorkItem(Guid.Parse("60000000-0000-4000-8000-000000000001"), "تأیید تخفیف پیشنهاد Q-1405-031", "فوری", DateTimeOffset.UtcNow.AddHours(2), DemoManagerId, "C01", "B03", "T02"),
            new CrmWorkItem(Guid.Parse("60000000-0000-4000-8000-000000000002"), "تماس پیگیری با تجارت نوین پارس", "بالا", DateTimeOffset.UtcNow.AddHours(4), DemoManagerId, "C01", "B03", "T02"),
            new CrmWorkItem(Guid.Parse("60000000-0000-4000-8000-000000000003"), "بررسی اعتبار مشتری نخل جنوب", "بالا", DateTimeOffset.UtcNow.AddHours(5), DemoManagerId, "C01", "B03", "T02"),
            new CrmWorkItem(Guid.Parse("60000000-0000-4000-8000-000000000004"), "بازبینی قرارداد بازرگانی دریا", "بالا", DateTimeOffset.UtcNow.AddHours(6), DemoManagerId, "C02", "B21", "T21")
        ]);
    }
}
