using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Channel;
using Crm.Domain.Commercial;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Sales;
using Crm.Domain.Service;
using static Crm.Application.Services.AccountPermissions;

namespace Crm.Application.Services;

public interface IAccountFileService
{
    AccountFileDto GetFile(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc);
    AccountSectionDto GetSection(Guid userId, OrganizationSelection organization, Guid accountId, string key, string? query, string? status, int page,
        DateTimeOffset nowUtc);
    IReadOnlyList<TimelineItemDto> GetHistory(Guid userId, OrganizationSelection organization, Guid accountId, int take = 100);
}

/// <summary>Static description of a related-records section.</summary>
public sealed record AccountSectionMeta(string Key, string Title, string Group, string Icon, string ReadPermission, string? CreatePermission,
    string? LinkPermission, bool AdminOnly = false);

/// <summary>
/// Read side of the account file (پرونده حساب): header, summary, KPIs and the related-records sections. Every section
/// reads the module's own table filtered by the account (single source of truth); nothing is copied into the account.
/// </summary>
public sealed class AccountFileService(ICrmDataStore store, IAccessSnapshotService access, IReportingFinanceSource finance) : IAccountFileService
{
    public const int PageSize = 10;

    public static readonly IReadOnlyList<AccountSectionMeta> Sections =
    [
        new("contacts", "افراد رابط", "ارتباطات", "i-users", "Customer.Contact.Read", "Customer.Update", null),
        new("opportunities", "فرصت‌های فروش", "فروش", "i-target", "Opportunity.Read", "Opportunity.Create", null),
        new("quotes", "پیش‌فاکتورها", "فروش", "i-file", "Quote.Read", "Quote.Create", null),
        new("orders", "سفارش‌های فروش", "فروش", "i-inbox", "Order.Read", null, null),
        new("leads", "سرنخ‌ها", "فروش", "i-lead", "Lead.Read", "Lead.Create", RelationManage),
        new("salesContracts", "قراردادهای فروش", "فروش", "i-file", ContractRead, ContractManage, null),
        new("projects", "پروژه‌ها", "فروش", "i-grid", ProjectRead, ProjectManage, null),
        new("invoices", "فاکتورها", "مالی", "i-file", "Customer.Financial.Read", null, null),
        new("payments", "پرداخت‌ها و دریافت‌ها", "مالی", "i-check", PaymentRead, PaymentCreate, null),
        new("guarantees", "ودیعه‌ها و تضمین‌ها", "مالی", "i-shield", GuaranteeRead, GuaranteeManage, null),
        new("bankAccounts", "حساب‌های بانکی", "مالی", "i-building", BankRead, BankManage, null),
        new("serviceCases", "سرویس‌ها و درخواست‌های خدمات", "خدمات", "i-shield", "Service.Read", "Service.Create", null),
        new("serviceContracts", "قراردادهای خدمات", "خدمات", "i-file", ContractRead, ContractManage, null),
        new("campaigns", "کمپین‌ها", "بازاریابی", "i-bell", CampaignRead, CampaignManage, CampaignManage),
        new("targetLists", "لیست‌های هدف", "بازاریابی", "i-users", CampaignRead, CampaignManage, CampaignManage),
        new("surveys", "نظرسنجی‌ها و پاسخ‌ها", "بازاریابی", "i-check", SurveyRead, SurveyManage, null),
        new("participations", "مشارکت‌ها", "بازاریابی", "i-target", "Customer.Read", RelationManage, null),
        new("documents", "اسناد و پیوست‌ها", "اسناد", "i-file", DocumentRead, DocumentManage, DocumentManage),
        new("hierarchy", "حساب مادر و حساب‌های زیرمجموعه", "ساختار", "i-building", "Customer.Read", null, RelationManage),
        new("branches", "شعب", "ساختار", "i-building", "Customer.Read", "Customer.Update", null),
        new("serviceCenters", "مراکز خدمات", "ساختار", "i-shield", "Customer.Read", "Customer.Update", null),
        new("salesCenters", "مراکز فروش", "ساختار", "i-building", "Customer.Read", "Customer.Update", null),
        new("dealers", "نمایندگان مرتبط", "ساختار", "i-building", "Dealer.Read", null, null),
        new("reservations", "رزروها", "سایر", "i-clock", "Customer.Read", RelationManage, null),
        new("gifts", "هدایا", "سایر", "i-bell", "Customer.Read", RelationManage, null),
        new("samples", "نمونه محصولات", "سایر", "i-inbox", "Customer.Read", RelationManage, null),
        new("accessGroups", "گروه‌های دسترسی", "سایر", "i-shield", "Administration.Manage", null, null, AdminOnly: true)
    ];

    public AccountFileDto GetFile(Guid userId, OrganizationSelection organization, Guid accountId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId);
            bool Can(string permission) => AccountGuard.Allows(snapshot, account, permission);
            var active = account.Status != CustomerStatus.Inactive;
            var summary = Summary(data, snapshot, account);
            var kpis = Kpis(data, snapshot, account, userId, nowUtc);
            var sections = Sections.Where(x => Can(x.ReadPermission)).Select(x => new AccountSectionInfo(x.Key, x.Title, x.Group, x.Icon,
                Count(data, snapshot, account, userId, x.Key, nowUtc), active && x.CreatePermission is { } create && Can(create),
                active && x.LinkPermission is { } link && Can(link))).ToList();
            var quick = new AccountQuickActions(active && Can("Opportunity.Create"), active && Can(ActivityCreate), active && Can(ActivityCreate),
                active && Can(ActivityCreate), active && Can(NoteCreate), active && Can("Lead.Create"));
            return new AccountFileDto(summary, kpis, quick, sections, active && Can("Customer.Update"), Can(AccountRecordService.StatusChange), Can(ActivityRead), Can(NoteRead),
                CustomerDataQualityRules.Issues(data, account));
        });
    }

    private static AccountSummaryDto Summary(CrmDataSet data, AccessSnapshot snapshot, Customer account)
    {
        var permissions = snapshot.PermissionsFor(account.CompanyId);
        var contact = permissions.Contains(FieldMasking.ContactPermission);
        var identity = permissions.Contains(FieldMasking.NationalIdPermission);
        string? Phone(string? value) => contact ? value : FieldMasking.MaskPhone(value);
        var profile = data.Find<CustomerProfile>(x => x.Id == account.Id).SingleOrDefault();
        var phones = new List<(string, string, bool)>();
        void AddPhone(string label, string? value) { if (Phone(value) is { } shown && !phones.Any(x => x.Item2 == shown)) phones.Add((label, shown, true)); }
        AddPhone("تلفن اصلی", account.PrimaryPhone);
        AddPhone("همراه", profile?.Mobile1);
        AddPhone("همراه دوم", profile?.Mobile2);
        AddPhone("تلفن دوم", profile?.Phone2);
        var address = data.Find<CustomerAddress>(x => x.CustomerId == account.Id && x.IsActive).OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Type).FirstOrDefault();
        var identifiers = new List<(string, string)>();
        void AddId(string label, string? value, bool sensitive = true)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            identifiers.Add((label, sensitive && !identity ? FieldMasking.MaskIdentifier(value)! : value));
        }
        if (account.Kind == CustomerKind.Legal)
        {
            AddId("شناسه ملی", account.NationalId);
            AddId("شماره ثبت", profile?.RegistrationNumber, false);
            AddId("کد اقتصادی", profile?.EconomicCode, false);
        }
        else
        {
            AddId("کد ملی", account.NationalId);
            AddId("کد اقتصادی", profile?.EconomicCode, false);
        }
        AddId("کد حسابداری", profile?.AccountingCode, false);
        var parent = account.ParentCustomerId is { } parentId ? data.Find<Customer>(x => x.Id == parentId).SingleOrDefault() : null;
        var company = data.Find<Crm.Domain.Organization.Company>(x => x.CompanyId == account.CompanyId).Select(x => x.Name).FirstOrDefault() ?? account.CompanyId;
        return new AccountSummaryDto(account.Id, account.Code, account.Name, account.Kind, account.Status, account.RelationshipType, account.TagList, account.Owner,
            account.CompanyId, company, account.Branch, account.TerritoryId, account.Segment, profile?.LogoContentType is not null, account.Version,
            parent?.Id, parent?.Name, phones, contact ? account.PrimaryEmail?.ToLowerInvariant() : FieldMasking.MaskEmail(account.PrimaryEmail), profile?.Website,
            address is null ? null : string.Join("، ", new[] { address.Province, address.City, address.AddressLine }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            profile?.ActivityType, identifiers, !contact || !identity,
            string.IsNullOrWhiteSpace(profile?.Province) ? account.City : profile.Province);
    }

    /// <summary>Summary indicators; each carries its definition so the number is never ambiguous.</summary>
    private List<AccountKpiDto> Kpis(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId, DateTimeOffset nowUtc)
    {
        bool Can(string permission) => AccountGuard.Allows(snapshot, account, permission);
        var kpis = new List<AccountKpiDto>();
        if (Can("Opportunity.Read"))
        {
            var open = VisibleOpportunities(data, snapshot, account, userId).Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).ToList();
            kpis.Add(new("openOpportunities", "فرصت‌های باز", open.Count.ToString(), Money(open.GroupBy(x => x.CurrencyCode).Select(g => new MoneyDto(g.Sum(x => x.Value), g.Key))),
                "فرصت‌هایی که هنوز برنده یا ازدست‌رفته نشده‌اند؛ مبلغ جمع ارزش آن‌ها به تفکیک واحد پول است.", "?tab=related&open=opportunities"));
        }
        if (Can(ActivityRead))
        {
            var planned = data.Find<CrmActivity>(x => x.CustomerId == account.Id && x.Status == ActivityStatus.Planned);
            var overdue = planned.Count(x => x.IsOverdue(nowUtc));
            kpis.Add(new("overdueActivities", "فعالیت‌های معوق", overdue.ToString(), $"{planned.Count} فعالیت برنامه‌ریزی‌شده",
                "تماس، جلسه یا وظیفهٔ انجام‌نشده‌ای که زمان یا مهلت آن گذشته است.", "?tab=activities&state=overdue", overdue > 0 ? "danger" : "neutral"));
            var completed = data.Find<CrmActivity>(x => x.CustomerId == account.Id && x.Status == ActivityStatus.Completed)
                .Select(x => (At: x.CompletedAtUtc ?? x.StartAtUtc, Text: $"{AccountActivityService.Label(x.Type)}: {x.Subject}"));
            var notes = Can(NoteRead) ? AccountNoteService.VisibleNotes(data, snapshot, account, userId).Select(x => (At: x.CreatedAtUtc, Text: $"یادداشت: {x.Title}")) : [];
            var visits = data.Find<Crm.Domain.SelfService.MobileVisit>(x => x.CustomerId == account.Id && x.CompletedAtUtc != null)
                .Select(x => (At: x.CompletedAtUtc!.Value, Text: $"بازدید: {x.Purpose}"));
            var last = completed.Concat(notes).Concat(visits).OrderByDescending(x => x.At).FirstOrDefault();
            kpis.Add(new("lastInteraction", "آخرین تعامل", last == default ? "—" : TehranTime.Date(last.At), last == default ? "تعاملی ثبت نشده" : last.Text,
                "آخرین تماس، جلسه یا وظیفهٔ انجام‌شده، یادداشت یا بازدید حضوری ثبت‌شده برای حساب."));
            var next = planned.Where(x => !x.IsOverdue(nowUtc)).OrderBy(x => x.StartAtUtc).FirstOrDefault();
            kpis.Add(new("nextAction", "اقدام بعدی", next is null ? "—" : TehranTime.Format(next.StartAtUtc), next is null ? "اقدامی برنامه‌ریزی نشده" :
                $"{AccountActivityService.Label(next.Type)}: {next.Subject}", "نزدیک‌ترین فعالیت برنامه‌ریزی‌شده‌ای که هنوز معوق نشده است.", "?tab=activities&state=planned",
                next is null ? "warning" : "neutral"));
        }
        if (Can("Customer.Financial.Read"))
        {
            var since = nowUtc.AddYears(-1);
            var invoices = Invoices(account, nowUtc).Where(x => x.InvoiceAtUtc >= since).ToList();
            var source = invoices.OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
            kpis.Add(new("sales", "فروش ۱۲ ماه (فاکتورشده)", Money(invoices.GroupBy(x => x.Currency).Select(g => new MoneyDto(g.Sum(x => x.InvoicedAmount), g.Key))) ?? "—",
                source is null ? "فاکتوری از حسابداری دریافت نشده" : $"منبع {source.Source} · همگام‌سازی {TehranTime.Format(source.SynchronizedAtUtc)}",
                "جمع مبلغ فاکتورهای ۱۲ ماه اخیر از سامانهٔ حسابداری، به تفکیک واحد پول.", "?tab=related&open=invoices"));
        }
        if (Can(PaymentRead))
        {
            var since = DateOnly.FromDateTime(nowUtc.UtcDateTime.AddYears(-1));
            var approved = data.Find<AccountPayment>(x => x.CustomerId == account.Id && x.Status == PaymentStatus.Approved && x.PaidOn >= since).ToList();
            var receipts = approved.Where(x => x.Direction == PaymentDirection.Receipt).GroupBy(x => x.CurrencyCode).Select(g => new MoneyDto(g.Sum(x => x.Amount), g.Key));
            var pending = data.Find<AccountPayment>(x => x.CustomerId == account.Id && x.Status == PaymentStatus.Registered).Count;
            kpis.Add(new("receipts", "دریافت‌های تأییدشده ۱۲ ماه", Money(receipts) ?? "—", pending > 0 ? $"{pending} مورد در انتظار تأیید" : "ثبت‌شده در CRM",
                "جمع دریافت‌های تأییدشده؛ موارد ثبت‌شده (تأییدنشده)، برگشتی و لغوشده در جمع نیستند.", "?tab=related&open=payments"));
        }
        if (Can("Customer.Financial.Read"))
            kpis.Add(new("balance", "مانده حساب", $"{account.Balance:N0} IRR", account.LastSynchronizedAtUtc is { } synced ? $"{account.DataSource} · {TehranTime.Format(synced)}" : "زمان همگام‌سازی نامشخص",
                "ماندهٔ بدهی حساب از سامانهٔ مالی (Projection)، با منبع و زمان آخرین همگام‌سازی."));
        return kpis;
    }

    private static string? Money(IEnumerable<MoneyDto> values)
    {
        var list = values.Where(x => x.Amount != 0).ToList();
        return list.Count == 0 ? null : string.Join(" + ", list.Select(x => $"{x.Amount:N0} {x.Currency}"));
    }

    private IEnumerable<ReceivableFact> Invoices(Customer account, DateTimeOffset nowUtc) =>
        finance.Read(account.CompanyId, new HashSet<Guid> { account.Id }, nowUtc).Where(x => x.CustomerId == account.Id && x.CompanyId == account.CompanyId)
            .GroupBy(x => x.InvoiceId).Select(x => x.OrderByDescending(i => i.SynchronizedAtUtc).First());

    private static IEnumerable<Opportunity> VisibleOpportunities(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var all = data.Find<Opportunity>(x => x.CustomerId == account.Id);
        return AccountGuard.ManagerWide(snapshot, account.CompanyId) ? all : all.Where(x => x.OwnerUserId == userId);
    }

    private static IEnumerable<Lead> VisibleLeads(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var all = data.Find<Lead>(x => x.CustomerId == account.Id);
        return AccountGuard.ManagerWide(snapshot, account.CompanyId) ? all : all.Where(x => x.OwnerUserId == userId);
    }

    private static IEnumerable<Quote> VisibleQuotes(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var all = data.Find<Quote>(x => x.CustomerId == account.Id);
        if (AccountGuard.ManagerWide(snapshot, account.CompanyId) || AccountGuard.Has(snapshot, account.CompanyId, "Quote.Approve.Finance")) return all;
        var mine = VisibleOpportunities(data, snapshot, account, userId).Select(x => x.Id).ToHashSet();
        return all.Where(x => x.OwnerUserId == userId || x.OpportunityId is { } o && mine.Contains(o));
    }

    private static IEnumerable<ServiceCase> VisibleCases(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var all = data.Find<ServiceCase>(x => x.CustomerId == account.Id);
        return AccountGuard.Allows(snapshot, account, "Service.Triage") || AccountGuard.Allows(snapshot, account, "Service.ReadAll")
            ? all : all.Where(x => x.OwnerUserId == userId || x.CreatedByUserId == userId);
    }

    private int Count(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId, string key, DateTimeOffset nowUtc) => key switch
    {
        "contacts" => data.Find<CustomerContact>(x => x.CustomerId == account.Id && x.IsActive).Count,
        "opportunities" => VisibleOpportunities(data, snapshot, account, userId).Count(),
        "quotes" => VisibleQuotes(data, snapshot, account, userId).Count(),
        "orders" => data.Find<OrderRequest>(x => x.CustomerId == account.Id).Count,
        "leads" => VisibleLeads(data, snapshot, account, userId).Count(),
        "salesContracts" => data.Find<AccountContract>(x => x.CustomerId == account.Id && x.Kind == ContractKind.Sales).Count,
        "serviceContracts" => data.Find<AccountContract>(x => x.CustomerId == account.Id && x.Kind == ContractKind.Service).Count,
        "projects" => data.Find<AccountProject>(x => x.CustomerId == account.Id).Count,
        "invoices" => Invoices(account, nowUtc).Count(),
        "payments" => data.Find<AccountPayment>(x => x.CustomerId == account.Id).Count,
        "guarantees" => data.Find<DealerGuarantee>(x => x.CustomerId == account.Id).Count,
        "bankAccounts" => data.Find<AccountBankAccount>(x => x.CustomerId == account.Id && x.IsActive).Count,
        "serviceCases" => VisibleCases(data, snapshot, account, userId).Count(),
        "campaigns" => data.Find<CampaignMember>(x => x.CustomerId == account.Id).Count,
        "targetLists" => data.Find<TargetListMember>(x => x.CustomerId == account.Id).Count,
        "surveys" => data.Find<SurveyResponse>(x => x.CustomerId == account.Id).Count +
                     (AccountGuard.Allows(snapshot, account, "Service.Read") ? VisibleCases(data, snapshot, account, userId).Count(x => x.SatisfactionScore != null) : 0),
        "participations" => data.Find<AccountParticipation>(x => x.CustomerId == account.Id).Count,
        "documents" => VisibleDocuments(data, snapshot, account, userId).Count,
        "hierarchy" => (account.ParentCustomerId is null ? 0 : 1) + data.Find<Customer>(x => x.ParentCustomerId == account.Id).Count,
        "branches" => data.Find<CustomerAddress>(x => x.CustomerId == account.Id && x.IsActive && x.Type == CustomerAddressType.Branch).Count,
        "serviceCenters" => data.Find<CustomerAddress>(x => x.CustomerId == account.Id && x.IsActive && x.Type == CustomerAddressType.ServiceCenter).Count,
        "salesCenters" => data.Find<CustomerAddress>(x => x.CustomerId == account.Id && x.IsActive && x.Type == CustomerAddressType.SalesCenter).Count,
        "dealers" => data.Find<DealerCustomerAssignment>(x => x.CustomerId == account.Id).Count,
        "reservations" => data.Find<AccountAllocation>(x => x.CustomerId == account.Id && x.Kind == AllocationKind.Reservation).Count,
        "gifts" => data.Find<AccountAllocation>(x => x.CustomerId == account.Id && x.Kind == AllocationKind.Gift).Count,
        "samples" => data.Find<AccountAllocation>(x => x.CustomerId == account.Id && x.Kind == AllocationKind.Sample).Count,
        "accessGroups" => AccessGroups(data, account, nowUtc).Count,
        _ => 0
    };

    private static List<(CrmDocument Document, DocumentLink Link)> VisibleDocuments(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var links = data.Find<DocumentLink>(x => x.CustomerId == account.Id);
        var ids = links.Select(x => x.DocumentId).ToArray();
        if (ids.Length == 0) return [];
        var sensitive = AccountGuard.Allows(snapshot, account, DocumentSensitive);
        var notes = AccountGuard.Allows(snapshot, account, NoteRead) ? AccountNoteService.VisibleNotes(data, snapshot, account, userId).Select(x => x.Id).ToHashSet() : [];
        return data.Find<CrmDocument>(x => ids.Contains(x.Id) && !x.IsDeleted)
            .Where(x => (!x.IsSensitive || sensitive || x.UploadedByUserId == userId) && (x.NoteId is null || notes.Contains(x.NoteId.Value)))
            .Select(x => (x, links.First(l => l.DocumentId == x.Id))).ToList();
    }

    public AccountSectionDto GetSection(Guid userId, OrganizationSelection organization, Guid accountId, string key, string? query, string? status, int page,
        DateTimeOffset nowUtc)
    {
        var meta = Sections.SingleOrDefault(x => x.Key == key) ?? throw new KeyNotFoundException("بخش پیدا نشد.");
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            var context = new SectionContext(data, snapshot, account, userId, nowUtc, meta, PersianText.NormalizeLetters(query), status,
                Math.Max(1, page), account.Status != CustomerStatus.Inactive);
            return Build(context);
        });
    }

    private sealed record SectionContext(CrmDataSet Data, AccessSnapshot Snapshot, Customer Account, Guid UserId, DateTimeOffset NowUtc,
        AccountSectionMeta Meta, string? Query, string? Status, int Page, bool Active)
    {
        public bool Can(string permission) => AccountGuard.Allows(Snapshot, Account, permission);
        public bool CanCreate => Active && Meta.CreatePermission is { } p && Can(p);
        public bool CanLink => Active && Meta.LinkPermission is { } p && Can(p);
        public string Base => $"/customers/{Account.Id}/records/{Meta.Key}";
        public bool Matches(params string?[] values) => Query is null || values.Any(v => v?.Contains(Query, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static readonly Dictionary<string, string> NoFields = [];

    private static Dictionary<string, string> Version(long version) => new() { ["expectedVersion"] = version.ToString() };

    private AccountSectionDto Build(SectionContext c) => c.Meta.Key switch
    {
        "contacts" => Contacts(c),
        "opportunities" => Opportunities(c),
        "quotes" => Quotes(c),
        "orders" => Orders(c),
        "leads" => Leads(c),
        "salesContracts" or "serviceContracts" => Contracts(c),
        "projects" => Projects(c),
        "invoices" => InvoiceSection(c),
        "payments" => Payments(c),
        "guarantees" => Guarantees(c),
        "bankAccounts" => BankAccounts(c),
        "serviceCases" => ServiceCases(c),
        "campaigns" => Campaigns(c),
        "targetLists" => TargetLists(c),
        "surveys" => Surveys(c),
        "participations" => Participations(c),
        "documents" => Documents(c),
        "hierarchy" => Hierarchy(c),
        "branches" or "serviceCenters" or "salesCenters" => Sites(c),
        "dealers" => Dealers(c),
        "reservations" or "gifts" or "samples" => Allocations(c),
        "accessGroups" => AccessGroupSection(c),
        _ => throw new KeyNotFoundException()
    };

    private static AccountSectionDto Result(SectionContext c, IEnumerable<AccountRowDto> rows, IReadOnlyList<string> columns, string emptyText,
        IReadOnlyList<(string, string)>? statusOptions = null, IEnumerable<MoneyDto>? totals = null, string? totalsLabel = null, string? createPath = null,
        string? createLabel = null, string? linkPath = null, string? linkLabel = null, string? note = null)
    {
        var all = rows.ToList();
        var page = Math.Min(c.Page, Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize)));
        return new AccountSectionDto(c.Meta.Key, c.Meta.Title, all.Count, all.Skip((page - 1) * PageSize).Take(PageSize).ToList(), page, PageSize, c.Query, c.Status,
            statusOptions ?? [], totals?.Where(x => x.Amount != 0).ToList() ?? [], totalsLabel,
            c.CanCreate ? createPath ?? $"{c.Base}/new" : null, createLabel ?? "ایجاد", c.CanLink ? linkPath ?? $"{c.Base}/link" : null, linkLabel ?? "اتصال رکورد موجود",
            emptyText, note, columns);
    }

    private static string? Date(DateTimeOffset? value) => value is { } v ? TehranTime.Date(v) : null;
    private static string? Date(DateOnly? value) => value is { } v ? JalaliDate.Format(v) : null;

    private AccountSectionDto Contacts(SectionContext c)
    {
        var mask = !c.Snapshot.PermissionsFor(c.Account.CompanyId).Contains(FieldMasking.ContactPermission);
        string? Phone(string? v) => mask ? FieldMasking.MaskPhone(v) : v;
        var canEdit = c.Active && c.Can("Customer.Update") && !mask;
        var rows = c.Data.Find<CustomerContact>(x => x.CustomerId == c.Account.Id && x.IsActive).Where(x => c.Matches(x.FullName, x.Role, x.Mobile, x.Phone, x.Email))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.FullName)
            .Select(x => new AccountRowDto(x.Id, x.FullName, x.Role, x.IsPrimary ? "رابط اصلی" : null, x.IsPrimary ? "success" : "neutral", null, null, null, null,
                canEdit ? [new AccountRowAction("ویرایش", $"/customers/{c.Account.Id}/contacts/{x.Id}/edit", Drawer: true),
                    new AccountRowAction("فعالیت‌ها", $"/customers/{c.Account.Id}?tab=activities&contact={x.Id}"),
                    new AccountRowAction("قطع ارتباط (غیرفعال)", $"/customers/{c.Account.Id}/contacts/{x.Id}/deactivate", Version(x.Version), true, "این رابط غیرفعال شود؟ سوابق او حذف نمی‌شود.")]
                    : [new AccountRowAction("فعالیت‌ها", $"/customers/{c.Account.Id}?tab=activities&contact={x.Id}")],
                [Phone(x.Mobile) ?? "—", Phone(x.Phone) is { } p ? x.Extension is null ? p : $"{p} داخلی {x.Extension}" : "—", mask ? FieldMasking.MaskEmail(x.Email) ?? "—" : x.Email?.ToLowerInvariant() ?? "—"]));
        return Result(c, rows, ["نام", "سمت", "همراه", "تلفن", "ایمیل"], "فرد رابطی ثبت نشده است.", createPath: $"/customers/{c.Account.Id}/contacts/create",
            createLabel: "تعریف رابط", note: "هر رابط به یک حساب تعلق دارد؛ «قطع ارتباط» رابط را غیرفعال می‌کند و سوابقش می‌ماند.");
    }

    private AccountSectionDto Opportunities(SectionContext c)
    {
        var users = AccountGuard.UserNames(c.Data, []);
        var canQuote = c.Active && c.Can("Quote.Create");
        var canContract = c.Active && c.Can(ContractManage);
        var all = VisibleOpportunities(c.Data, c.Snapshot, c.Account, c.UserId).ToList();
        var rows = all.Where(x => c.Matches(x.Title, x.Code) && (c.Status switch
            {
                "open" => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost),
                "won" => x.Stage == OpportunityStage.Won,
                "lost" => x.Stage == OpportunityStage.Lost,
                _ => true
            })).OrderBy(x => x.Stage is OpportunityStage.Won or OpportunityStage.Lost).ThenBy(x => x.ExpectedCloseAtUtc)
            .Select(x =>
            {
                var actions = new List<AccountRowAction>();
                if (canQuote && x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost))
                    actions.Add(new("ایجاد پیش‌فاکتور", $"/quotes/create?customerId={c.Account.Id}&opportunityId={x.Id}", Drawer: true));
                if (canContract && x.Stage == OpportunityStage.Won)
                    actions.Add(new("ایجاد قرارداد فروش", $"/customers/{c.Account.Id}/records/salesContracts/new?opportunityId={x.Id}", Drawer: true));
                return new AccountRowDto(x.Id, x.Title, $"{x.Code} · {StageLabel(x.Stage)} · احتمال {x.Probability}٪", StageLabel(x.Stage),
                    x.Stage == OpportunityStage.Won ? "success" : x.Stage == OpportunityStage.Lost ? "neutral" : "info", new MoneyDto(x.Value, x.CurrencyCode),
                    Date(x.ExpectedCloseAtUtc), $"/opportunities/{x.Id}", x.Owner, actions, [x.NextAction ?? "—"]);
            });
        var open = all.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).GroupBy(x => x.CurrencyCode).Select(g => new MoneyDto(g.Sum(x => x.Value), g.Key));
        return Result(c, rows, ["فرصت", "مرحله", "مبلغ", "بسته‌شدن", "مسئول", "اقدام بعدی"], "فرصتی برای این حساب ثبت نشده است.",
            [("open", "باز"), ("won", "برنده"), ("lost", "ازدست‌رفته")], open, "ارزش فرصت‌های باز", $"/customers/{c.Account.Id}/quick/opportunity", "ایجاد فرصت",
            note: "فرصت همیشه به یک حساب تعلق دارد؛ جابه‌جایی آن بین حساب‌ها فقط با ادغام حساب‌ها انجام می‌شود.");
    }

    public static string StageLabel(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Identified => "شناسایی",
        OpportunityStage.Discovery => "نیازسنجی",
        OpportunityStage.Qualified => "احراز صلاحیت",
        OpportunityStage.SolutionOffer => "ارائه راهکار",
        OpportunityStage.Negotiation => "مذاکره",
        OpportunityStage.Commit => "تعهد",
        OpportunityStage.Won => "برنده",
        _ => "ازدست‌رفته"
    };

    private AccountSectionDto Quotes(SectionContext c)
    {
        var rows = VisibleQuotes(c.Data, c.Snapshot, c.Account, c.UserId).Where(x => c.Matches(x.Code, x.Opportunity) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AccountRowDto(x.Id, x.Code, x.Opportunity, QuoteLabel(x.Status), x.Status switch
                {
                    QuoteStatus.Accepted or QuoteStatus.Approved => "success", QuoteStatus.Rejected or QuoteStatus.Expired => "neutral", _ => "info"
                }, new MoneyDto(x.NetAmount, x.CurrencyCode), Date(x.ValidUntilUtc), $"/quotes/{x.Id}", null, []));
        return Result(c, rows, ["پیش‌فاکتور", "فرصت", "وضعیت", "مبلغ خالص", "اعتبار تا"], "پیش‌فاکتوری ثبت نشده است.",
            Enum.GetValues<QuoteStatus>().Select(x => (x.ToString(), QuoteLabel(x))).ToList(), createPath: $"/quotes/create?customerId={c.Account.Id}",
            createLabel: "ایجاد پیش‌فاکتور", note: "تبدیل فرصت به پیش‌فاکتور با همان گردش تأیید و قیمت‌گذاری ماژول پیش‌فاکتور انجام می‌شود.");
    }

    private static string QuoteLabel(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "پیش‌نویس", QuoteStatus.Submitted => "ارسال برای بررسی", QuoteStatus.PendingApproval => "در انتظار تأیید",
        QuoteStatus.Approved => "تأییدشده", QuoteStatus.Rejected => "ردشده", QuoteStatus.Sent => "ارسال به مشتری", QuoteStatus.Accepted => "پذیرفته‌شده", _ => "منقضی"
    };

    private AccountSectionDto Orders(SectionContext c)
    {
        var rows = c.Data.Find<OrderRequest>(x => x.CustomerId == c.Account.Id).Where(x => c.Matches(x.Code, x.QuoteCode, x.ErpOrderNumber))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AccountRowDto(x.Id, x.Code, $"پیش‌فاکتور {x.QuoteCode}{(x.ErpOrderNumber is null ? "" : $" · ERP {x.ErpOrderNumber}")}", x.Status.ToString(), "info",
                new MoneyDto(x.NetAmount, x.CurrencyCode), Date(x.CreatedAtUtc), $"/orders/{x.Id}", null, []));
        return Result(c, rows, ["سفارش", "مرجع", "وضعیت", "مبلغ", "تاریخ"], "سفارش فروشی ثبت نشده است.",
            note: "سفارش فروش از پیش‌فاکتور پذیرفته‌شده ساخته می‌شود. سفارش خرید (تأمین‌کننده) ماژول ندارد و خارج از دامنه است.");
    }

    private AccountSectionDto Leads(SectionContext c)
    {
        var canUnlink = c.CanLink;
        var rows = VisibleLeads(c.Data, c.Snapshot, c.Account, c.UserId).Where(x => c.Matches(x.Name, x.Code, x.Contact, x.Phone, x.Email))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AccountRowDto(x.Id, x.Name, $"{x.Code} · منبع {x.Source}", x.Status.ToString(), x.Status == LeadStatus.Converted ? "success" : "info", null,
                Date(x.CreatedAtUtc), $"/leads/{x.Id}", x.Owner,
                canUnlink && x.Status != LeadStatus.Converted
                    ? [new AccountRowAction("قطع ارتباط", $"{c.Base}/{x.Id}/unlink", Version(x.Version), true, "ارتباط سرنخ با حساب قطع شود؟ خود سرنخ حذف نمی‌شود.")]
                    : [], [x.Contact]));
        return Result(c, rows, ["سرنخ", "منبع", "وضعیت", "تاریخ", "مسئول", "تماس"], "سرنخی به این حساب متصل نیست.",
            createPath: $"/customers/{c.Account.Id}/quick/lead", createLabel: "ایجاد سرنخ",
            note: "سرنخ تبدیل‌شده به حساب و فرصت متصل می‌ماند تا منشأ حفظ شود.");
    }

    private AccountSectionDto Contracts(SectionContext c)
    {
        var kind = c.Meta.Key == "salesContracts" ? ContractKind.Sales : ContractKind.Service;
        var today = TehranTime.Today(c.NowUtc);
        var canManage = c.Active && c.Can(ContractManage);
        var rows = c.Data.Find<AccountContract>(x => x.CustomerId == c.Account.Id && x.Kind == kind).Where(x => c.Matches(x.Number, x.Title) &&
                (c.Status is null || (c.Status == "Expired" ? x.IsExpired(today) : x.Status.ToString() == c.Status)))
            .OrderByDescending(x => x.StartOn)
            .Select(x =>
            {
                var actions = new List<AccountRowAction>();
                if (canManage && x.Status == ContractStatus.Draft) actions.Add(new("فعال‌سازی", $"{c.Base}/{x.Id}/activate", Version(x.Version)));
                if (canManage && x.Status != ContractStatus.Terminated) actions.Add(new("خاتمه", $"{c.Base}/{x.Id}/terminate", Version(x.Version), true, Prompt: "دلیل خاتمه"));
                var state = x.IsExpired(today) ? "منقضی" : x.Status switch { ContractStatus.Draft => "پیش‌نویس", ContractStatus.Active => "فعال", _ => "خاتمه‌یافته" };
                return new AccountRowDto(x.Id, x.Title, $"{x.Number}{(x.ServiceLevel is null ? "" : $" · سطح خدمت {x.ServiceLevel}")}", state,
                    x.IsExpired(today) ? "warning" : x.Status == ContractStatus.Active ? "success" : "neutral", x.Amount is { } a ? new MoneyDto(a, x.CurrencyCode) : null,
                    $"{JalaliDate.Format(x.StartOn)} تا {JalaliDate.Format(x.EndOn)}", null, null, actions);
            });
        return Result(c, rows, ["قرارداد", "شماره", "وضعیت", "مبلغ", "اعتبار"], kind == ContractKind.Sales ? "قرارداد فروشی ثبت نشده است." : "قرارداد خدماتی ثبت نشده است.",
            [("Draft", "پیش‌نویس"), ("Active", "فعال"), ("Expired", "منقضی"), ("Terminated", "خاتمه‌یافته")],
            createLabel: kind == ContractKind.Sales ? "ایجاد قرارداد فروش" : "ایجاد قرارداد خدمات",
            note: kind == ContractKind.Sales ? "قرارداد فروش فقط از فرصت برنده‌شدهٔ همین حساب ایجاد می‌شود." : null);
    }

    private AccountSectionDto Projects(SectionContext c)
    {
        var canManage = c.Active && c.Can(ProjectManage);
        var users = AccountGuard.UserNames(c.Data, c.Data.Find<AccountProject>(x => x.CustomerId == c.Account.Id).Select(x => x.ManagerUserId ?? Guid.Empty));
        var rows = c.Data.Find<AccountProject>(x => x.CustomerId == c.Account.Id).Where(x => c.Matches(x.Code, x.Name) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderByDescending(x => x.StartOn)
            .Select(x => new AccountRowDto(x.Id, x.Name, x.Code, ProjectLabel(x.Status), x.Status switch
                {
                    ProjectStatus.Active => "info", ProjectStatus.Completed => "success", ProjectStatus.OnHold => "warning", _ => "neutral"
                }, x.Budget is { } b ? new MoneyDto(b, x.CurrencyCode) : null, $"{JalaliDate.Format(x.StartOn)}{(x.EndOn is { } e ? $" تا {JalaliDate.Format(e)}" : "")}",
                null, x.ManagerUserId is { } m ? users.GetValueOrDefault(m) : null,
                canManage && x.Status is not (ProjectStatus.Completed or ProjectStatus.Cancelled)
                    ? Enum.GetValues<ProjectStatus>().Where(s => s != x.Status && s != ProjectStatus.Planned)
                        .Select(s => new AccountRowAction(ProjectLabel(s), $"{c.Base}/{x.Id}/status", new Dictionary<string, string> { ["status"] = s.ToString(), ["expectedVersion"] = x.Version.ToString() },
                            s == ProjectStatus.Cancelled, s == ProjectStatus.Cancelled ? "پروژه لغو شود؟" : null)).ToList()
                    : []));
        return Result(c, rows, ["پروژه", "کد", "وضعیت", "بودجه", "بازه", "مدیر"], "پروژه‌ای ثبت نشده است.",
            Enum.GetValues<ProjectStatus>().Select(x => (x.ToString(), ProjectLabel(x))).ToList(), createLabel: "ایجاد پروژه");
    }

    public static string ProjectLabel(ProjectStatus status) => status switch
    {
        ProjectStatus.Planned => "برنامه‌ریزی", ProjectStatus.Active => "در حال اجرا", ProjectStatus.OnHold => "متوقف",
        ProjectStatus.Completed => "تکمیل", _ => "لغو"
    };

    private AccountSectionDto InvoiceSection(SectionContext c)
    {
        var invoices = Invoices(c.Account, c.NowUtc).ToList();
        string State(ReceivableFact x) => x.CollectedAmount >= x.InvoicedAmount ? "paid" : x.DueAtUtc < c.NowUtc ? "overdue" : x.CollectedAmount > 0 ? "partial" : "open";
        var rows = invoices.Where(x => c.Matches(x.InvoiceId) && (c.Status is null || State(x) == c.Status)).OrderByDescending(x => x.InvoiceAtUtc)
            .Select(x => new AccountRowDto(Guid.Empty, x.InvoiceId, $"سررسید {TehranTime.Date(x.DueAtUtc)} · وصول {x.CollectedAmount:N0} {x.Currency}",
                State(x) switch { "paid" => "تسویه", "overdue" => "سررسید گذشته", "partial" => "پرداخت جزئی", _ => "باز" },
                State(x) switch { "paid" => "success", "overdue" => "danger", _ => "info" }, new MoneyDto(x.InvoicedAmount, x.Currency), Date(x.InvoiceAtUtc), null, null, []));
        var source = invoices.OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
        return Result(c, rows, ["فاکتور", "سررسید / وصول", "وضعیت", "مبلغ", "تاریخ"], "فاکتوری از حسابداری دریافت نشده است.",
            [("open", "باز"), ("partial", "پرداخت جزئی"), ("overdue", "سررسید گذشته"), ("paid", "تسویه")],
            invoices.GroupBy(x => x.Currency).Select(g => new MoneyDto(g.Sum(x => x.InvoicedAmount - x.CollectedAmount), g.Key)), "ماندهٔ فاکتورها",
            note: source is null ? "فاکتورها از سامانهٔ حسابداری خوانده می‌شوند." :
                $"فقط‌خواندنی؛ منبع: {source.Source} · آخرین همگام‌سازی {TehranTime.Format(source.SynchronizedAtUtc)}. صدور فاکتور در سامانهٔ حسابداری انجام می‌شود.");
    }

    private AccountSectionDto Payments(SectionContext c)
    {
        var canApprove = c.Active && c.Can(PaymentApprove);
        var all = c.Data.Find<AccountPayment>(x => x.CustomerId == c.Account.Id);
        var users = AccountGuard.UserNames(c.Data, all.Select(x => x.CreatedByUserId));
        var rows = all.Where(x => c.Matches(x.Reference, x.InvoiceReference, x.Description) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderByDescending(x => x.PaidOn).ThenByDescending(x => x.CreatedAtUtc)
            .Select(x =>
            {
                var actions = new List<AccountRowAction>();
                if (canApprove && x.Status == PaymentStatus.Registered && x.CreatedByUserId != c.UserId) actions.Add(new("تأیید", $"{c.Base}/{x.Id}/approve", Version(x.Version)));
                if (canApprove && x.Status == PaymentStatus.Registered) actions.Add(new("لغو", $"{c.Base}/{x.Id}/cancel", Version(x.Version), true, Prompt: "دلیل لغو"));
                if (canApprove && x.Status == PaymentStatus.Approved) actions.Add(new("ثبت برگشتی", $"{c.Base}/{x.Id}/return", Version(x.Version), true, Prompt: "دلیل برگشت (مثلاً برگشت چک)"));
                return new AccountRowDto(x.Id, $"{(x.Direction == PaymentDirection.Receipt ? "دریافت" : "پرداخت")} · {MethodLabel(x.Method)}",
                    string.Join(" · ", new[] { x.Reference, x.InvoiceReference is null ? null : $"فاکتور {x.InvoiceReference}", x.Description }.Where(v => v is not null)),
                    PaymentLabel(x.Status), x.Status switch { PaymentStatus.Approved => "success", PaymentStatus.Registered => "warning", _ => "neutral" },
                    new MoneyDto(x.Amount, x.CurrencyCode), JalaliDate.Format(x.PaidOn), null, users.GetValueOrDefault(x.CreatedByUserId), actions,
                    [x.DecisionNote ?? "—"]);
            });
        var approved = all.Where(x => x.CountsInTotals).GroupBy(x => (x.Direction, x.CurrencyCode))
            .Select(g => new MoneyDto(g.Key.Direction == PaymentDirection.Receipt ? g.Sum(x => x.Amount) : -g.Sum(x => x.Amount), g.Key.CurrencyCode))
            .GroupBy(x => x.Currency).Select(g => new MoneyDto(g.Sum(x => x.Amount), g.Key));
        return Result(c, rows, ["نوع", "مرجع", "وضعیت", "مبلغ", "تاریخ", "ثبت‌کننده", "توضیح تصمیم"], "پرداخت یا دریافتی ثبت نشده است.",
            Enum.GetValues<PaymentStatus>().Select(x => (x.ToString(), PaymentLabel(x))).ToList(), approved, "خالص دریافت‌های تأییدشده",
            createLabel: "ثبت پرداخت / دریافت",
            note: "جمع‌ها فقط از وضعیت «تأییدشده» و به تفکیک واحد پول محاسبه می‌شوند. ثبت‌کننده نمی‌تواند پرداخت خود را تأیید کند. ارسال به حسابداری متصل نیست.");
    }

    public static string PaymentLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Registered => "ثبت‌شده", PaymentStatus.Approved => "تأییدشده", PaymentStatus.Returned => "برگشتی", _ => "لغوشده"
    };

    public static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقد", PaymentMethod.BankTransfer => "حواله بانکی", PaymentMethod.Cheque => "چک", PaymentMethod.Card => "کارت به کارت",
        PaymentMethod.Pos => "کارتخوان", _ => "سایر"
    };

    private AccountSectionDto Guarantees(SectionContext c)
    {
        var today = TehranTime.Today(c.NowUtc);
        var canManage = c.Active && c.Can(GuaranteeManage);
        var all = c.Data.Find<DealerGuarantee>(x => x.CustomerId == c.Account.Id);
        var rows = all.Where(x => c.Matches(x.Number, x.Issuer, x.Notes) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderBy(x => x.Status).ThenBy(x => x.ExpiresOn ?? DateOnly.MaxValue)
            .Select(x => new AccountRowDto(x.Id, $"{GuaranteeLabel(x.Type)} {x.Number}", x.Issuer,
                x.Status == DealerGuaranteeStatus.Active ? x.IsEffective(today) ? x.ExpiresWithin(today, 30) ? "نزدیک سررسید" : "فعال" : "منقضی" : x.Status == DealerGuaranteeStatus.Released ? "آزادشده" : "ضبط‌شده",
                x.IsEffective(today) ? x.ExpiresWithin(today, 30) ? "warning" : "success" : "neutral", new MoneyDto(x.Amount, "IRR"),
                $"{JalaliDate.Format(x.IssuedOn)}{(x.ExpiresOn is { } e ? $" تا {JalaliDate.Format(e)}" : "")}", null, null,
                canManage && x.Status == DealerGuaranteeStatus.Active
                    ? [new AccountRowAction("آزادسازی", $"{c.Base}/{x.Id}/release", Version(x.Version), Prompt: "دلیل آزادسازی"),
                       new AccountRowAction("ضبط", $"{c.Base}/{x.Id}/forfeit", Version(x.Version), true, Prompt: "دلیل ضبط")]
                    : [], [x.DecisionReason ?? x.Notes ?? "—"]));
        return Result(c, rows, ["تضمین", "صادرکننده", "وضعیت", "مبلغ", "اعتبار", "توضیح"], "ودیعه یا تضمینی ثبت نشده است.",
            Enum.GetValues<DealerGuaranteeStatus>().Select(x => (x.ToString(), x switch { DealerGuaranteeStatus.Active => "فعال", DealerGuaranteeStatus.Released => "آزادشده", _ => "ضبط‌شده" })).ToList(),
            [new MoneyDto(all.Where(x => x.IsEffective(today)).Sum(x => x.Amount), "IRR")], "تضمین‌های معتبر", createLabel: "ثبت تضمین / ودیعه");
    }

    public static string GuaranteeLabel(DealerGuaranteeType type) => type switch
    {
        DealerGuaranteeType.BankGuarantee => "ضمانت‌نامه بانکی", DealerGuaranteeType.Cheque => "چک تضمین", DealerGuaranteeType.PromissoryNote => "سفته",
        DealerGuaranteeType.CashDeposit => "ودیعه نقدی", _ => "وثیقه ملکی"
    };

    private AccountSectionDto BankAccounts(SectionContext c)
    {
        var canManage = c.Active && c.Can(BankManage);
        var rows = c.Data.Find<AccountBankAccount>(x => x.CustomerId == c.Account.Id && x.IsActive).Where(x => c.Matches(x.BankName, x.Iban, x.HolderName, x.AccountNumber))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.BankName)
            .Select(x => new AccountRowDto(x.Id, x.BankName, x.HolderName, x.IsPrimary ? "اصلی" : null, x.IsPrimary ? "success" : "neutral", null, null, null, null,
                BankActions(c, x, canManage),
                [x.Iban, x.AccountNumber ?? "—"]));
        return Result(c, rows, ["بانک", "صاحب حساب", "وضعیت", "شبا", "شماره حساب"], "حساب بانکی ثبت نشده است.", createLabel: "افزودن حساب بانکی");
    }

    private static List<AccountRowAction> BankActions(SectionContext c, AccountBankAccount x, bool canManage)
    {
        var actions = new List<AccountRowAction>();
        if (!canManage) return actions;
        if (!x.IsPrimary) actions.Add(new AccountRowAction("انتخاب به‌عنوان اصلی", $"{c.Base}/{x.Id}/primary", Version(x.Version)));
        actions.Add(new AccountRowAction("غیرفعال", $"{c.Base}/{x.Id}/deactivate", Version(x.Version), true, "این حساب بانکی غیرفعال شود؟"));
        return actions;
    }

    private AccountSectionDto ServiceCases(SectionContext c)
    {
        var rows = VisibleCases(c.Data, c.Snapshot, c.Account, c.UserId).Where(x => c.Matches(x.Code, x.Subject) && (c.Status switch
            {
                "open" => x.IsOpen, "closed" => !x.IsOpen, _ => true
            })).OrderByDescending(x => x.OpenedAtUtc)
            .Select(x => new AccountRowDto(x.Id, x.Subject, $"{x.Code} · اولویت {x.Priority}", x.Status.ToString(), x.IsOpen ? x.ResolutionSla(c.NowUtc) == ServiceSlaState.Breached ? "danger" : "info" : "success",
                null, Date(x.OpenedAtUtc), $"/service/{x.Id}", x.Owner, [], [x.SatisfactionScore is { } s ? $"رضایت {s}/۵" : "—"]));
        return Result(c, rows, ["موضوع", "کد", "وضعیت", "ثبت", "مسئول", "رضایت"], "درخواست خدمتی ثبت نشده است.", [("open", "باز"), ("closed", "بسته")],
            createPath: $"/service/create?customerId={c.Account.Id}", createLabel: "ثبت درخواست خدمات");
    }

    private AccountSectionDto Campaigns(SectionContext c)
    {
        var members = c.Data.Find<CampaignMember>(x => x.CustomerId == c.Account.Id);
        var ids = members.Select(x => x.CampaignId).ToArray();
        var campaigns = ids.Length == 0 ? [] : c.Data.Find<Campaign>(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var canManage = c.Active && c.Can(CampaignManage);
        var rows = members.Where(x => campaigns.ContainsKey(x.CampaignId) && c.Matches(campaigns[x.CampaignId].Name) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderByDescending(x => campaigns[x.CampaignId].StartOn)
            .Select(x =>
            {
                var campaign = campaigns[x.CampaignId];
                var actions = canManage ? Enum.GetValues<CampaignMemberStatus>().Where(s => s != x.Status)
                    .Select(s => new AccountRowAction(MemberLabel(s), $"{c.Base}/{x.Id}/status", new Dictionary<string, string> { ["status"] = s.ToString(), ["expectedVersion"] = x.Version.ToString() }))
                    .Append(new AccountRowAction("حذف از کمپین", $"{c.Base}/{x.Id}/unlink", Version(x.Version), true, "حساب از این کمپین خارج شود؟ کمپین حذف نمی‌شود.")).ToList() : [];
                return new AccountRowDto(x.Id, campaign.Name, $"{CampaignTypeLabel(campaign.Type)} · {CampaignStatusLabel(campaign.Status)}", MemberLabel(x.Status),
                    x.Status == CampaignMemberStatus.Converted ? "success" : x.Status == CampaignMemberStatus.OptedOut ? "neutral" : "info", null,
                    $"{JalaliDate.Format(campaign.StartOn)}{(campaign.EndOn is { } e ? $" تا {JalaliDate.Format(e)}" : "")}", null, null, actions);
            });
        return Result(c, rows, ["کمپین", "نوع / وضعیت کمپین", "وضعیت عضویت", "بازه"], "حساب در کمپینی عضو نیست.",
            Enum.GetValues<CampaignMemberStatus>().Select(x => (x.ToString(), MemberLabel(x))).ToList(), createLabel: "کمپین جدید", linkLabel: "افزودن به کمپین موجود",
            note: "عضویت در کمپین با «مشارکت در رویداد/برنامه» متفاوت است.");
    }

    public static string MemberLabel(CampaignMemberStatus status) => status switch
    {
        CampaignMemberStatus.Targeted => "هدف‌گذاری‌شده", CampaignMemberStatus.Responded => "پاسخ داده", CampaignMemberStatus.Converted => "تبدیل‌شده", _ => "انصراف"
    };

    public static string CampaignTypeLabel(CampaignType type) => type switch
    {
        CampaignType.Email => "ایمیل", CampaignType.Sms => "پیامک", CampaignType.Event => "رویداد", CampaignType.Webinar => "وبینار", CampaignType.Advertising => "تبلیغات", _ => "سایر"
    };

    public static string CampaignStatusLabel(CampaignStatus status) => status switch
    {
        CampaignStatus.Planned => "برنامه‌ریزی", CampaignStatus.Active => "فعال", CampaignStatus.Completed => "پایان‌یافته", _ => "لغو"
    };

    private AccountSectionDto TargetLists(SectionContext c)
    {
        var members = c.Data.Find<TargetListMember>(x => x.CustomerId == c.Account.Id);
        var ids = members.Select(x => x.TargetListId).ToArray();
        var lists = ids.Length == 0 ? [] : c.Data.Find<TargetList>(x => ids.Contains(x.Id)).ToDictionary(x => x.Id);
        var canManage = c.Active && c.Can(CampaignManage);
        var rows = members.Where(x => lists.ContainsKey(x.TargetListId) && c.Matches(lists[x.TargetListId].Name))
            .Select(x => new AccountRowDto(x.Id, lists[x.TargetListId].Name, lists[x.TargetListId].Description, null, "neutral", null, Date(x.CreatedAtUtc), null, null,
                canManage ? [new AccountRowAction("حذف از لیست", $"{c.Base}/{x.Id}/unlink", Version(x.Version), true, "حساب از این لیست خارج شود؟ لیست حذف نمی‌شود.")] : []));
        return Result(c, rows, ["لیست", "توضیح", "", "افزوده‌شده"], "حساب در لیست هدفی نیست.", createLabel: "لیست جدید", linkLabel: "افزودن به لیست موجود");
    }

    private AccountSectionDto Surveys(SectionContext c)
    {
        var responses = c.Data.Find<SurveyResponse>(x => x.CustomerId == c.Account.Id);
        var surveyIds = responses.Select(x => x.SurveyId).Distinct().ToArray();
        var surveys = surveyIds.Length == 0 ? [] : c.Data.Find<Survey>(x => surveyIds.Contains(x.Id)).ToDictionary(x => x.Id);
        var contacts = c.Data.Find<CustomerContact>(x => x.CustomerId == c.Account.Id).ToDictionary(x => x.Id, x => x.FullName);
        var rows = responses.Where(x => surveys.ContainsKey(x.SurveyId)).Select(x => new AccountRowDto(x.Id, surveys[x.SurveyId].Title,
                x.ContactId is { } ct ? contacts.GetValueOrDefault(ct) : null, $"امتیاز {x.Score} از {surveys[x.SurveyId].ScoreRange.Max}",
                x.Score >= surveys[x.SurveyId].ScoreRange.Max * 0.8 ? "success" : x.Score <= surveys[x.SurveyId].ScoreRange.Max * 0.4 ? "danger" : "info", null,
                JalaliDate.Format(x.RespondedOn), null, null, [], [x.Comment ?? "—"])).ToList();
        if (c.Can("Service.Read"))
            rows.AddRange(VisibleCases(c.Data, c.Snapshot, c.Account, c.UserId).Where(x => x.SatisfactionScore is not null)
                .Select(x => new AccountRowDto(x.Id, $"رضایت از خدمات ({x.Code})", "ثبت هنگام بستن پرونده", $"امتیاز {x.SatisfactionScore} از 5",
                    x.SatisfactionScore >= 4 ? "success" : x.SatisfactionScore <= 2 ? "danger" : "info", null, Date(x.ClosedAtUtc ?? x.UpdatedAtUtc), $"/service/{x.Id}", null, [],
                    [x.SatisfactionComment ?? "—"])));
        return Result(c, rows.Where(x => c.Matches(x.Title, x.Subtitle, x.Extra?.FirstOrDefault())).OrderByDescending(x => x.Date), ["نظرسنجی", "پاسخ‌دهنده", "امتیاز", "تاریخ", "نظر"],
            "پاسخ نظرسنجی ثبت نشده است.", createLabel: "ثبت پاسخ نظرسنجی", note: "امتیاز رضایت پرونده‌های خدمات هم (فقط‌خواندنی) در این فهرست است.");
    }

    private AccountSectionDto Participations(SectionContext c)
    {
        var canManage = c.Active && c.Can(RelationManage);
        var rows = c.Data.Find<AccountParticipation>(x => x.CustomerId == c.Account.Id).Where(x => c.Matches(x.Title, x.Role, x.Notes) && (c.Status is null || x.Status.ToString() == c.Status))
            .OrderByDescending(x => x.StartOn)
            .Select(x => new AccountRowDto(x.Id, x.Title, $"{ParticipationKindLabel(x.Kind)}{(x.Role is null ? "" : $" · نقش {x.Role}")}", ParticipationLabel(x.Status),
                x.Status == ParticipationStatus.Attended ? "success" : x.Status == ParticipationStatus.Cancelled ? "neutral" : "info", null,
                $"{JalaliDate.Format(x.StartOn)}{(x.EndOn is { } e ? $" تا {JalaliDate.Format(e)}" : "")}", null, null,
                canManage && x.Status is not (ParticipationStatus.Attended or ParticipationStatus.Cancelled)
                    ? Enum.GetValues<ParticipationStatus>().Where(s => s != x.Status && s != ParticipationStatus.Planned)
                        .Select(s => new AccountRowAction(ParticipationLabel(s), $"{c.Base}/{x.Id}/status", new Dictionary<string, string> { ["status"] = s.ToString(), ["expectedVersion"] = x.Version.ToString() },
                            s == ParticipationStatus.Cancelled)).ToList()
                    : [], [x.Notes ?? "—"]));
        return Result(c, rows, ["رویداد / برنامه", "نوع و نقش", "وضعیت", "بازه", "توضیح"], "مشارکتی ثبت نشده است.",
            Enum.GetValues<ParticipationStatus>().Select(x => (x.ToString(), ParticipationLabel(x))).ToList(), createLabel: "ثبت مشارکت",
            note: "مشارکت یعنی حضور یا نقش حساب در رویداد، برنامه یا همکاری تجاری (مثل نمایشگاه، برنامهٔ وفاداری یا همکاری مشترک).");
    }

    public static string ParticipationKindLabel(ParticipationKind kind) => kind switch { ParticipationKind.Event => "رویداد", ParticipationKind.Program => "برنامه", _ => "همکاری تجاری" };
    public static string ParticipationLabel(ParticipationStatus status) => status switch
    {
        ParticipationStatus.Planned => "برنامه‌ریزی", ParticipationStatus.Confirmed => "تأییدشده", ParticipationStatus.Attended => "انجام‌شده", _ => "لغو"
    };

    private AccountSectionDto Documents(SectionContext c)
    {
        var canManage = c.Active && c.Can(DocumentManage);
        var docs = VisibleDocuments(c.Data, c.Snapshot, c.Account, c.UserId);
        var users = AccountGuard.UserNames(c.Data, docs.Select(x => x.Document.UploadedByUserId));
        var rows = docs.Where(x => c.Matches(x.Document.Title, x.Document.FileName) && (c.Status switch
            {
                "attachments" => x.Document.NoteId is not null, "files" => x.Document.NoteId is null, _ => true
            })).OrderByDescending(x => x.Document.CreatedAtUtc)
            .Select(x =>
            {
                var d = x.Document;
                var actions = new List<AccountRowAction>();
                if (canManage && d.NoteId is null)
                {
                    actions.Add(new("قطع ارتباط", $"{c.Base}/{d.Id}/unlink", null, true, "ارتباط این سند با حساب قطع شود؟ فایل حذف نمی‌شود."));
                    if (d.UploadedByUserId == c.UserId || AccountGuard.ManagerWide(c.Snapshot, c.Account.CompanyId))
                        actions.Add(new("حذف سند", $"{c.Base}/{d.Id}/delete", null, true, "سند حذف شود؟ فقط سندی که به حساب دیگری متصل نیست حذف می‌شود."));
                }
                return new AccountRowDto(d.Id, d.Title, $"{d.FileName} · {Size(d.SizeBytes)}{(d.NoteId is null ? "" : " · پیوست یادداشت")}", d.IsSensitive ? "محرمانه" : null,
                    d.IsSensitive ? "warning" : "neutral", null, TehranTime.Format(d.CreatedAtUtc), $"/customers/{c.Account.Id}/documents/{d.Id}", users.GetValueOrDefault(d.UploadedByUserId), actions);
            });
        return Result(c, rows, ["سند", "فایل", "حساسیت", "تاریخ", "بارگذاری"], "سندی به این حساب متصل نیست.", [("files", "اسناد"), ("attachments", "پیوست یادداشت‌ها")],
            createLabel: "بارگذاری سند", linkLabel: "اتصال سند موجود",
            note: "قطع ارتباط فقط پیوند را برمی‌دارد و فایل باقی می‌ماند؛ سند محرمانه فقط برای دارندگان مجوز نمایش داده می‌شود.");
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private AccountSectionDto Hierarchy(SectionContext c)
    {
        var canManage = c.CanLink;
        var rows = new List<AccountRowDto>();
        if (c.Account.ParentCustomerId is { } parentId && c.Data.Find<Customer>(x => x.Id == parentId).SingleOrDefault() is { } parent)
        {
            var visible = AccountGuard.InContext(c.Snapshot, new OrganizationSelection(c.Account.CompanyId, null, null), "Customer.Read", parent);
            rows.Add(new AccountRowDto(parent.Id, visible ? parent.Name : "حساب مادر (خارج از دامنهٔ شما)", visible ? parent.Code : null, "حساب مادر", "info", null, null,
                visible ? $"/customers/{parent.Id}" : null, null,
                canManage ? [new AccountRowAction("جدا کردن از حساب مادر", $"{c.Base}/{parent.Id}/unlink-parent", Version(c.Account.Version), true, "ارتباط با حساب مادر قطع شود؟")] : []));
        }
        foreach (var child in c.Data.Find<Customer>(x => x.ParentCustomerId == c.Account.Id).Where(x => c.Matches(x.Name, x.Code)).OrderBy(x => x.Name))
        {
            var visible = AccountGuard.InContext(c.Snapshot, new OrganizationSelection(c.Account.CompanyId, null, null), "Customer.Read", child);
            rows.Add(new AccountRowDto(child.Id, visible ? child.Name : "حساب زیرمجموعه (خارج از دامنهٔ شما)", visible ? child.Code : null, "زیرمجموعه", "neutral", null, null,
                visible ? $"/customers/{child.Id}" : null, null,
                canManage && visible ? [new AccountRowAction("جدا کردن زیرمجموعه", $"{c.Base}/{child.Id}/unlink-child", Version(child.Version), true, "این حساب از زیرمجموعه خارج شود؟")] : []));
        }
        return Result(c, rows, ["حساب", "کد", "نقش"], "حساب مادر یا زیرمجموعه‌ای تعریف نشده است.", linkLabel: "انتخاب حساب مادر / افزودن زیرمجموعه");
    }

    private AccountSectionDto Sites(SectionContext c)
    {
        var type = c.Meta.Key switch { "branches" => CustomerAddressType.Branch, "serviceCenters" => CustomerAddressType.ServiceCenter, _ => CustomerAddressType.SalesCenter };
        var canManage = c.Active && c.Can("Customer.Update");
        var rows = c.Data.Find<CustomerAddress>(x => x.CustomerId == c.Account.Id && x.IsActive && x.Type == type).Where(x => c.Matches(x.Title, x.City, x.AddressLine))
            .OrderBy(x => x.Title)
            .Select(x => new AccountRowDto(x.Id, x.Title, string.Join("، ", new[] { x.Province, x.City }.Where(v => !string.IsNullOrWhiteSpace(v))), null, "neutral", null, null, null, null,
                canManage ? [new AccountRowAction("غیرفعال", $"{c.Base}/{x.Id}/deactivate", Version(x.Version), true, "این مورد غیرفعال شود؟")] : [], [x.AddressLine, x.PostalCode ?? "—"]));
        return Result(c, rows, ["عنوان", "استان / شهر", "", "نشانی", "کد پستی"], "موردی ثبت نشده است.",
            createPath: $"/customers/{c.Account.Id}/addresses/create?type={type}", createLabel: "افزودن");
    }

    private AccountSectionDto Dealers(SectionContext c)
    {
        var assignments = c.Data.Find<DealerCustomerAssignment>(x => x.CustomerId == c.Account.Id);
        var ids = assignments.Select(x => x.DealerId).Distinct().ToArray();
        var dealers = ids.Length == 0 ? [] : c.Data.Find<Dealer>(x => ids.Contains(x.Id)).Where(x => c.Snapshot.AllowsRecord(x.CompanyId, "Dealer.Read", x.BranchId, x.TerritoryId) ||
            c.Snapshot.Allows(x.CompanyId, "Dealer.Read", "Dealer", x.DealerId)).ToDictionary(x => x.Id);
        var rows = assignments.Where(x => dealers.ContainsKey(x.DealerId) && c.Matches(dealers[x.DealerId].TradeName, dealers[x.DealerId].Code))
            .Select(x => new AccountRowDto(x.Id, dealers[x.DealerId].TradeName, dealers[x.DealerId].Code, x.IsActive ? "فعال" : "پایان‌یافته", x.IsActive ? "success" : "neutral", null,
                $"{TehranTime.Date(x.ValidFromUtc)}{(x.ValidToUtc is { } to ? $" تا {TehranTime.Date(to)}" : "")}", $"/dealers/{x.DealerId}", null, []));
        return Result(c, rows, ["نماینده", "کد", "وضعیت", "بازه"], "نماینده‌ای به این حساب تخصیص داده نشده است.",
            note: "تخصیص و پایان تخصیص از پروندهٔ نماینده انجام می‌شود.");
    }

    private AccountSectionDto Allocations(SectionContext c)
    {
        var kind = c.Meta.Key switch { "reservations" => AllocationKind.Reservation, "gifts" => AllocationKind.Gift, _ => AllocationKind.Sample };
        var canManage = c.Active && c.Can(RelationManage);
        var rows = c.Data.Find<AccountAllocation>(x => x.CustomerId == c.Account.Id && x.Kind == kind).Where(x => c.Matches(x.ItemName, x.ItemCode, x.Notes) &&
                (c.Status is null || x.Status.ToString() == c.Status)).OrderByDescending(x => x.Date)
            .Select(x => new AccountRowDto(x.Id, x.ItemName, $"{x.ItemCode ?? "بدون کد"} · مقدار {x.Quantity:0.###}", AllocationLabel(x.Status),
                x.Status == AllocationStatus.Delivered ? "success" : x.Status == AllocationStatus.Requested ? "info" : "neutral", null, JalaliDate.Format(x.Date), null, null,
                canManage && x.Status is not (AllocationStatus.Returned or AllocationStatus.Cancelled)
                    ? Enum.GetValues<AllocationStatus>().Where(s => s != x.Status && s != AllocationStatus.Requested && (s != AllocationStatus.Returned || x.Status == AllocationStatus.Delivered))
                        .Select(s => new AccountRowAction(AllocationLabel(s), $"{c.Base}/{x.Id}/status", new Dictionary<string, string> { ["status"] = s.ToString(), ["expectedVersion"] = x.Version.ToString() },
                            s == AllocationStatus.Cancelled)).ToList()
                    : [], [x.Notes ?? "—"]));
        return Result(c, rows, ["کالا / خدمت", "کد و مقدار", "وضعیت", "تاریخ", "توضیح"], "موردی ثبت نشده است.",
            Enum.GetValues<AllocationStatus>().Select(x => (x.ToString(), AllocationLabel(x))).ToList(),
            createLabel: kind switch { AllocationKind.Reservation => "ثبت رزرو", AllocationKind.Gift => "ثبت هدیه", _ => "ثبت نمونه محصول" });
    }

    public static string AllocationLabel(AllocationStatus status) => status switch
    {
        AllocationStatus.Requested => "درخواست‌شده", AllocationStatus.Delivered => "تحویل‌شده", AllocationStatus.Returned => "برگشتی", _ => "لغو"
    };

    /// <summary>Who can see this account: role assignments covering its company/branch/territory and dealer users of assigned dealers.</summary>
    private static List<(string Group, IReadOnlyList<string> Users)> AccessGroups(CrmDataSet data, Customer account, DateTimeOffset nowUtc)
    {
        var assignments = data.Find<UserRoleAssignment>(x => x.CompanyId == account.CompanyId).Where(x => x.IsEffective(nowUtc)).ToList();
        var dealerCodes = data.Find<DealerCustomerAssignment>(x => x.CustomerId == account.Id).Where(x => x.IsActive).Select(x => x.DealerId).ToArray();
        var dealerKeys = dealerCodes.Length == 0 ? [] : data.Find<Dealer>(x => dealerCodes.Contains(x.Id)).Select(x => x.DealerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covering = assignments.Where(x => AccountGuard.Same(x.ScopeType, "Company") || AccountGuard.Same(x.ScopeType, "Branch") && AccountGuard.Same(x.ScopeId, account.BranchId) ||
            AccountGuard.Same(x.ScopeType, "Territory") && AccountGuard.Same(x.ScopeId, account.TerritoryId) ||
            AccountGuard.Same(x.ScopeType, "Dealer") && dealerKeys.Contains(x.ScopeId)).ToList();
        var users = AccountGuard.UserNames(data, covering.Select(x => x.CrmUserId));
        return covering.GroupBy(x => (x.RoleLabel, x.ScopeType, x.ScopeLabel)).OrderBy(x => x.Key.ScopeType)
            .Select(g => ($"{g.Key.RoleLabel} · {g.Key.ScopeLabel}", (IReadOnlyList<string>)g.Select(x => users.GetValueOrDefault(x.CrmUserId, "—")).Distinct().ToList())).ToList();
    }

    private AccountSectionDto AccessGroupSection(SectionContext c)
    {
        var rows = AccessGroups(c.Data, c.Account, c.NowUtc).Where(x => c.Matches(x.Group, string.Join(" ", x.Users)))
            .Select(x => new AccountRowDto(Guid.Empty, x.Group, string.Join("، ", x.Users), $"{x.Users.Count} کاربر", "neutral", null, null, "/identity/users", null, []));
        return Result(c, rows, ["نقش و دامنه", "کاربران", "تعداد"], "گروه دسترسی‌ای این حساب را پوشش نمی‌دهد.",
            note: "فقط برای مدیران دسترسی؛ از نقش‌ها و دامنه‌های تخصیص‌یافته محاسبه می‌شود و تغییر آن در «کاربران و دسترسی» است.");
    }

    /// <summary>Change log of the account (تاریخچه تغییرات): every recorded change with actor and time, plus ownership history.</summary>
    public IReadOnlyList<TimelineItemDto> GetHistory(Guid userId, OrganizationSelection organization, Guid accountId, int take = 100)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId);
            var events = data.Find<CustomerTimelineEvent>(x => x.CustomerId == account.Id).OrderByDescending(x => x.OccurredAtUtc).Take(take).ToList();
            var ownership = data.Find<CustomerOwnershipHistory>(x => x.CustomerId == account.Id);
            var actors = AccountGuard.UserNames(data, events.Select(x => x.ActorUserId ?? Guid.Empty).Concat(ownership.Select(x => x.ChangedByUserId)));
            return events.Select(x => new TimelineItemDto(x.Type.ToString(), x.Title, x.Description, TehranTime.Format(x.OccurredAtUtc), x.OccurredAtUtc,
                    x.ActorUserId is { } actor ? actors.GetValueOrDefault(actor, "—") : x.Source, null, "i-clock"))
                .Concat(ownership.Select(x => new TimelineItemDto("Ownership", $"مالکیت: {x.Owner} · شعبه {x.BranchId}", x.Reason,
                    TehranTime.Format(x.ValidFromUtc), x.ValidFromUtc, actors.GetValueOrDefault(x.ChangedByUserId, "—"), null, "i-users")))
                .OrderByDescending(x => x.AtUtc).Take(take).ToList();
        });
    }
}
