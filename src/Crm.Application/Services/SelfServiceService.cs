using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.SelfService;

namespace Crm.Application.Services;

public sealed class SelfServiceService(ICrmDataStore store, IAccessSnapshotService access, IPortalReadSource source) : ISelfServiceService
{
    private AccessSnapshot Access(Guid actor) => access.Get(actor) ?? throw new UnauthorizedAccessException();
    private static bool Context(IOrganizationScoped row, OrganizationSelection org) => row.CompanyId == org.CompanyId &&
        (org.BranchId is null || row.BranchId == org.BranchId) && (org.TerritoryId is null || row.TerritoryId == org.TerritoryId);
    private static bool Permit(AccessSnapshot a, string permission, IOrganizationScoped row, OrganizationSelection org) =>
        Context(row, org) && a.AllowsRecord(org.CompanyId, permission, row.BranchId, row.TerritoryId);
    private Dealer Dealer(CrmDataSet d, AccessSnapshot a, OrganizationSelection org, string permission)
    {
        // A company-wide grant must never impersonate a dealer. Require one explicit active dealer grant.
        var matches = d.Dealers.Where(x => Context(x, org) && a.PermissionScopeGrants.Any(g =>
            g.CompanyId == org.CompanyId && g.Permission == permission && g.ScopeType == "Dealer" && g.ScopeId == x.DealerId)).ToList();
        if (matches.Count != 1) throw new UnauthorizedAccessException("یک نمایندهٔ مشخص در دامنهٔ فعال لازم است.");
        return matches[0];
    }
    private static bool Ready(CrmDataSet d, Dealer dealer, DateTimeOffset now) => dealer.Status == DealerStatus.Active &&
        d.DealerContracts.Any(x => x.CompanyId == dealer.CompanyId && x.DealerId == dealer.Id && x.IsEffective(now)) &&
        d.DealerTerritoryAssignments.Any(x => x.CompanyId == dealer.CompanyId && x.DealerId == dealer.Id && x.IsEffective(now));
    private static IEnumerable<Customer> Customers(CrmDataSet d, Dealer dealer, DateTimeOffset now) => d.Customers.Where(c =>
        c.CompanyId == dealer.CompanyId && c.Status == CustomerStatus.Active && d.DealerCustomerAssignments.Any(x =>
            x.CompanyId == dealer.CompanyId && x.DealerId == dealer.Id && x.CustomerId == c.Id &&
            x.ValidFromUtc <= now && (x.ValidToUtc is null || now < x.ValidToUtc)));
    private static bool DealerAccount(CrmDataSet d, Guid user, Dealer dealer, DateTimeOffset now) => d.UserRoleAssignments.Any(x =>
        x.CrmUserId == user && x.CompanyId == dealer.CompanyId && x.RoleKey == "DealerUser" && x.ScopeType == "Dealer" &&
        x.ScopeId == dealer.DealerId && x.IsEffective(now));
    private static string Text(string? value, int length, string field)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > length) throw new ArgumentException($"{field} الزامی است و حداکثر {length} نویسه دارد.");
        return result;
    }
    private static void Operation(Guid id) { if (id == Guid.Empty) throw new ArgumentException("شناسهٔ عملیات الزامی است."); }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string LeadKey(string subject) => Hash(string.Join(" ",subject.Normalize(NormalizationForm.FormKC).Replace('ي','ی').Replace('ك','ک').Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant());
    private static void Version(long actual, long expected) { if (actual != expected) throw new SelfServiceConflictException("رکورد تغییر کرده است؛ صفحه را تازه کنید و دوباره بررسی کنید."); }
    private static void Audit(CrmDataSet d, Guid actor, string action, Guid id, DateTimeOffset now) => d.Append<Crm.Domain.Identity.SecurityAuditEvent>(
        new(Guid.NewGuid(), now, action, "Succeeded", actor, null, null, id.ToString("N"), $"record:{id:N}", "", ""));
    private static PortalRequestDto Map(CrmDataSet d, PortalRequest r) => new(r.Id, "PR-" + r.Id.ToString("N")[..8], r.DealerId,
        d.Dealers.First(x => x.Id == r.DealerId).TradeName, r.Kind, r.Status, r.Subject, r.Description, r.PublicReply,
        r.ProductCode, r.Quantity, r.UnitPrice, r.Source, r.PriceAtUtc, r.Version, r.CreatedAtUtc, r.LinkedRecordId,
        r.ProtectedUntilUtc, r.Kind == PortalRequestKind.Order && r.LinkedRecordId is { } order ?
            d.OrderRequests.FirstOrDefault(x => x.Id == order && x.CompanyId == r.CompanyId && x.CustomerId == r.CustomerId)?.Status.ToString() : null,
        r.CreatedByUserId, r.CustomerId, r.Email, r.TargetUserId,
        r.Kind is PortalRequestKind.Complaint or PortalRequestKind.Claim && r.LinkedRecordId is { } caseId ?
            d.ServiceCases.FirstOrDefault(x => x.Id == caseId && x.CompanyId == r.CompanyId)?.Code : null,
        r.CustomerId is { } customerId ? d.Customers.FirstOrDefault(x => x.Id == customerId && x.CompanyId == r.CompanyId)?.Name : null);

    public PortalDashboardDto Portal(Guid userId, OrganizationSelection org, DateTimeOffset now)
    {
        var a = Access(userId);
        return store.Read(d => {
            var dealer = Dealer(d, a, org, "Portal.Read");
            var financialAllowed = a.PermissionScopeGrants.Any(x => x.CompanyId == org.CompanyId && x.Permission == "Dealer.Financial.Read" && x.ScopeType == "Dealer" && x.ScopeId == dealer.DealerId);
            var f = financialAllowed ? d.DealerFinancialSnapshots.Where(x => x.CompanyId == org.CompanyId && x.DealerId == dealer.Id).OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault() : null;
            var target = d.DealerTargets.Where(x => x.CompanyId == org.CompanyId && x.DealerId == dealer.Id && x.PeriodFromUtc <= now && now < x.PeriodToUtc).OrderByDescending(x => x.PeriodFromUtc).FirstOrDefault();
            var performance = target is null ? null : d.DealerPerformanceSnapshots.Where(x => x.CompanyId == org.CompanyId && x.DealerId == dealer.Id && x.PeriodFromUtc == target.PeriodFromUtc && x.PeriodToUtc == target.PeriodToUtc).OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
            return new PortalDashboardDto(dealer.Id, dealer.TradeName, Ready(d, dealer, now) && a.PermissionScopeGrants.Any(x=>x.CompanyId==org.CompanyId && x.Permission=="Portal.Submit" && x.ScopeType=="Dealer" && x.ScopeId==dealer.DealerId), f?.Balance, f?.OverdueAmount, f?.Source, f?.SynchronizedAtUtc,
                target?.Amount, performance?.NetSales, Customers(d, dealer, now).Where(x=>Context(x,org)).Select(x => new DealerCustomerOptionDto(x.Id, x.Code, x.Name, x.BranchId)).ToList(),
                source.Products(org.CompanyId, dealer.DealerId, now), financialAllowed ? source.Invoices(org.CompanyId, dealer.DealerId, now) : [],
                d.Users.Where(x => DealerAccount(d, x.Id, dealer, now)).Select(x => new PortalAccountDto(x.Id, x.DisplayName, x.UserName, x.Status.ToString())).ToList(),
                d.PortalRequests.Where(x => x.CompanyId == org.CompanyId && x.DealerId == dealer.Id).OrderByDescending(x => x.CreatedAtUtc).Select(x => Map(d, x)).ToList());
        });
    }
    public PortalRequestDto Submit(Guid userId, OrganizationSelection org, SubmitPortalRequestCommand command, DateTimeOffset now)
    {
        var a = Access(userId); Operation(command.OperationId);
        var c = command with { Subject = Text(command.Subject, 160, "عنوان"), Description = Text(command.Description, 2000, "شرح"), Email = command.Email?.Trim().ToUpperInvariant(), ProductCode = command.ProductCode?.Trim() };
        if (!Enum.IsDefined(c.Kind)) throw new ArgumentException("نوع درخواست معتبر نیست.");
        var fingerprint = Hash(c);
        return store.Write(d => {
            var dealer = Dealer(d, a, org, "Portal.Submit");
            if (!Ready(d, dealer, now)) throw new UnauthorizedAccessException("نماینده، قرارداد یا قلمرو فعال نیست.");
            var previous = d.PortalRequests.FirstOrDefault(x => x.CompanyId == org.CompanyId && x.CreatedByUserId == userId && x.OperationId == c.OperationId);
            if (previous is not null) {
                if (previous.DealerId != dealer.Id || previous.Fingerprint != fingerprint) throw new SelfServiceConflictException("شناسهٔ عملیات قبلاً با محتوای دیگری استفاده شده است.");
                return Map(d, previous);
            }
            PortalProductDto? product = null;
            if (c.Kind == PortalRequestKind.Order) {
                if (!Customers(d, dealer, now).Any(x => x.Id == c.CustomerId && Context(x,org))) throw new UnauthorizedAccessException();
                product = source.Products(org.CompanyId, dealer.DealerId, now).FirstOrDefault(x => x.Code == c.ProductCode) ?? throw new ArgumentException("کالای مجاز انتخاب کنید.");
                if (c.Quantity <= 0 || c.Quantity > 10000 || decimal.Round(c.Quantity,3)!=c.Quantity) throw new ArgumentException("تعداد مثبت، حداکثر ۱۰۰۰۰ و با حداکثر سه رقم اعشار باشد.");
            }
            // A complaint or damage claim may name one of the dealer's customers; it becomes the service case's customer.
            var complaintCustomer = c.Kind is PortalRequestKind.Complaint or PortalRequestKind.Claim && c.CustomerId is { } concerned
                ? Customers(d, dealer, now).FirstOrDefault(x => x.Id == concerned && Context(x, org))?.Id ?? throw new UnauthorizedAccessException()
                : (Guid?)null;
            if (c.Kind == PortalRequestKind.AccessInvite && (!MailAddress.TryCreate(c.Email, out var mail) || mail.Address != c.Email)) throw new ArgumentException("ایمیل معتبر وارد کنید.");
            if (c.Kind == PortalRequestKind.AccessRevoke && (c.TargetUserId is not { } target || target == userId || !DealerAccount(d, target, dealer, now))) throw new UnauthorizedAccessException();
            var r = new PortalRequest(Guid.NewGuid(), org.CompanyId, dealer.BranchId, dealer.TerritoryId, dealer.Id, userId, c.OperationId, fingerprint, c.Kind, c.Subject, c.Description,
                product is null ? complaintCustomer : c.CustomerId, product?.Code, product is null ? 0 : c.Quantity, product?.UnitPrice ?? 0, product?.Source, product?.SynchronizedAtUtc,
                c.Kind == PortalRequestKind.AccessInvite ? c.Email : null, c.Kind == PortalRequestKind.AccessRevoke ? c.TargetUserId : null);
            d.PortalRequests.Add(r); Audit(d, userId, "Portal.Submitted", r.Id, now); return Map(d, r);
        });
    }
    public PortalRequestDto Request(Guid userId, OrganizationSelection org, Guid id, DateTimeOffset now) => Portal(userId, org, now).Requests.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
    public void Cancel(Guid userId, OrganizationSelection org, Guid id, long expectedVersion, DateTimeOffset now)
    {
        var a = Access(userId);
        store.Write(d => { var dealer = Dealer(d, a, org, "Portal.Submit");
            var r = d.PortalRequests.FirstOrDefault(x => x.Id == id && x.CompanyId == org.CompanyId && x.DealerId == dealer.Id && x.CreatedByUserId == userId) ?? throw new KeyNotFoundException();
            Version(r.Version, expectedVersion); r.Decide(PortalRequestStatus.Cancelled, "لغو توسط ثبت‌کننده", userId); Audit(d, userId, "Portal.Cancelled", id, now); return true; });
    }
    public IReadOnlyList<PortalRequestDto> Inbox(Guid userId, OrganizationSelection org, DateTimeOffset now)
    {
        var a = Access(userId); if (!a.PermissionsFor(org.CompanyId).Contains("Portal.Review")) throw new UnauthorizedAccessException();
        return store.Read(d => d.PortalRequests.Where(x => Permit(a, "Portal.Review", x, org)).OrderByDescending(x => x.CreatedAtUtc).Select(x => Map(d, x)).ToList());
    }
    private static PortalRequest Reviewed(CrmDataSet d, AccessSnapshot a, OrganizationSelection org, Guid id) =>
        d.PortalRequests.FirstOrDefault(x => x.Id == id && Permit(a, "Portal.Review", x, org)) ?? throw new KeyNotFoundException();
    public PortalReviewDto ReviewForm(Guid userId, OrganizationSelection org, Guid id, DateTimeOffset now)
    {
        var a = Access(userId); return store.Read(d => {
            var r = Reviewed(d, a, org, id);
            var dealer = d.Dealers.First(x => x.Id == r.DealerId && x.CompanyId == org.CompanyId);
            var customers = r.Kind is PortalRequestKind.Complaint or PortalRequestKind.Claim && r.CustomerId is null
                ? Customers(d, dealer, now).Select(x => new DealerCustomerOptionDto(x.Id, x.Code, x.Name, x.BranchId)).ToList() : null;
            return new PortalReviewDto(Map(d, r), Permit(a, "Lead.Create", r, org), Permit(a, "Administration.Manage", r, org), customers);
        });
    }
    public PortalRequestDto Review(Guid userId, OrganizationSelection org, Guid id, ReviewPortalRequestCommand command, DateTimeOffset now)
    {
        var a = Access(userId); var reply = Text(command.Reply, 2000, "پاسخ قابل مشاهده برای نماینده");
        if (command.Status is not (PortalRequestStatus.InReview or PortalRequestStatus.Accepted or PortalRequestStatus.Rejected)) throw new ArgumentException("تصمیم معتبر نیست.");
        Guid? invalidate = null;
        var result = store.Write(d => {
            var r = Reviewed(d, a, org, id); Version(r.Version, command.ExpectedVersion);
            if (r.Status is PortalRequestStatus.Accepted or PortalRequestStatus.Rejected or PortalRequestStatus.Cancelled) throw new SelfServiceConflictException("درخواست نهایی شده است.");
            var dealer = d.Dealers.First(x => x.Id == r.DealerId && x.CompanyId == org.CompanyId);
            Guid? linked = null; DateTimeOffset? protection = null; string? protectionKey = null;
            if (command.Status == PortalRequestStatus.Accepted) {
                if (!Ready(d, dealer, now)) throw new ArgumentException("نماینده و قرارداد باید فعال باشند.");
                if (r.Kind == PortalRequestKind.Lead) {
                    if (!Permit(a, "Lead.Create", r, org)) throw new UnauthorizedAccessException();
                    protectionKey=LeadKey(r.Subject);
                    if (d.PortalRequests.Any(x => x.Id != id && x.CompanyId == org.CompanyId && x.Kind == PortalRequestKind.Lead && x.ProtectedUntilUtc > now && x.ProtectionKey==protectionKey)) throw new SelfServiceConflictException("این سرنخ نیازمند بررسی تداخل مالکیت است.");
                    foreach(var expired in d.PortalRequests.Where(x=>x.CompanyId==org.CompanyId && x.ProtectionKey==protectionKey)) expired.ReleaseExpiredProtection(now);
                    linked = Guid.NewGuid(); protection = now.AddDays(30);
                    d.Leads.Add(new(linked.Value, "L-P-" + linked.Value.ToString("N")[..10], r.Subject, "ارجاع پرتال", "DealerPortal", d.Users.First(x => x.Id == userId).DisplayName, org.CompanyId, r.BranchId, r.TerritoryId, ownerUserId:userId, firstContactDueAtUtc:now.AddHours(4)));
                }
                if (r.Kind == PortalRequestKind.Order) {
                    if (command.LinkedOrderId is not { } orderId) throw new ArgumentException("پس از گردش پیش‌فاکتور و کنترل اعتبار، شناسهٔ سفارش را برای تأیید نهایی وارد کنید.");
                    var order = d.OrderRequests.FirstOrDefault(x => x.Id == orderId && x.CompanyId == org.CompanyId && x.CustomerId == r.CustomerId && Permit(a, "Order.Read", x, org));
                    if (order is null || !Customers(d, dealer, now).Any(x => x.Id == r.CustomerId)) throw new UnauthorizedAccessException();
                    if (d.PortalRequests.Any(x => x.Id != id && x.LinkedRecordId == orderId && x.Kind == PortalRequestKind.Order)) throw new SelfServiceConflictException("سفارش قبلاً به یک درخواست متصل است.");
                    linked = orderId;
                }
                if (r.Kind is PortalRequestKind.Complaint or PortalRequestKind.Claim) {
                    // Accepting a complaint/claim opens a service case (portal channel) that follows the normal SLA and triage.
                    var customerId = r.CustomerId ?? command.CustomerId ?? throw new ArgumentException("برای ثبت پرونده خدمات، مشتری مربوط را انتخاب کنید.");
                    var customer = Customers(d, dealer, now).FirstOrDefault(x => x.Id == customerId) ?? throw new UnauthorizedAccessException();
                    var claim = r.Kind == PortalRequestKind.Claim;
                    var item = ServiceCaseService.Open(d, customer, r.Subject, r.Description,
                        claim ? Crm.Domain.Service.ServiceCaseCategory.ProductDefect : Crm.Domain.Service.ServiceCaseCategory.Complaint,
                        Crm.Domain.Service.ServiceCaseChannel.Portal,
                        claim ? Crm.Domain.Service.ServiceCasePriority.High : Crm.Domain.Service.ServiceCasePriority.Medium,
                        userId, now, $"ارجاع از پرتال نماینده {dealer.TradeName} (PR-{r.Id.ToString("N")[..8]})");
                    linked = item.Id;
                }
                if (r.Kind is PortalRequestKind.AccessInvite or PortalRequestKind.AccessRevoke) {
                    if (!Permit(a, "Administration.Manage", r, org)) throw new UnauthorizedAccessException();
                    if (r.Kind == PortalRequestKind.AccessInvite) {
                        if (d.Users.Any(x => x.NormalizedEmail == r.Email)) throw new SelfServiceConflictException("دعوت نیازمند بررسی حساب موجود توسط مدیر دسترسی است.");
                        var account = new CrmUser(Guid.NewGuid(), r.Subject, "dealer." + Guid.NewGuid().ToString("N")[..12], r.Email!);
                        d.Users.Add(account); d.UserRoleAssignments.Add(new(Guid.NewGuid(), account.Id, "DealerUser", "کاربر نماینده", org.CompanyId, "Dealer", dealer.DealerId, dealer.TradeName, now, null, userId, "Portal approved invitation")); linked = account.Id;
                    } else {
                        var target = r.TargetUserId ?? throw new ArgumentException("کاربر مشخص نیست.");
                        if (!DealerAccount(d, target, dealer, now) || target == r.CreatedByUserId) throw new UnauthorizedAccessException();
                        var roles = d.UserRoleAssignments.Where(x => x.CrmUserId == target && x.IsEffective(now)).ToList();
                        if (roles.Any(x => x.RoleKey != "DealerUser" || x.CompanyId != org.CompanyId || x.ScopeType != "Dealer" || x.ScopeId != dealer.DealerId)) throw new ArgumentException("حساب مشترک باید در مدیریت دسترسی بررسی شود.");
                        foreach (var role in roles) role.Revoke(now);
                        d.Users.First(x => x.Id == target).IncrementSecurityVersion();
                        foreach (var session in d.UserSessions.Where(x => x.CrmUserId == target)) session.Revoke(now, "Portal access revoked");
                        invalidate = target; linked = target;
                    }
                }
            }
            r.Decide(command.Status, reply, userId, linked, protection, protectionKey); Audit(d, userId, "Portal.Reviewed", id, now); return Map(d, r);
        });
        if (invalidate is { } changed) access.Invalidate(changed); return result;
    }
    public ReportExport Invoice(Guid userId, OrganizationSelection org, string invoiceId, DateTimeOffset now)
    {
        var p = Portal(userId, org, now); var i = p.Invoices.FirstOrDefault(x => x.Id == invoiceId) ?? throw new KeyNotFoundException();
        static string Csv(string s) => "\"" + ((s.Length > 0 && "=+-@\t\r".Contains(s[0])) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        var csv = "Number,Total,Collected,DueUtc,Source,SyncUtc\r\n" + string.Join(",", new[]{Csv(i.Number), i.Total.ToString(System.Globalization.CultureInfo.InvariantCulture), i.Collected.ToString(System.Globalization.CultureInfo.InvariantCulture), Csv(i.DueAtUtc.ToString("O")), Csv(i.Source), Csv(i.SynchronizedAtUtc.ToString("O"))});
        store.Write(d => { Audit(d, userId, "Portal.InvoiceExport", p.DealerId, now); return true; });
        return new("dealer-invoice.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());
    }
    private static MobileVisitDto Map(CrmDataSet d, MobileVisit v) => new(v.Id, v.CustomerId, d.Customers.First(x => x.Id == v.CustomerId).Name, v.BranchId, v.PlannedAtUtc, v.Purpose, v.Status, v.Version, v.Outcome, v.CheckedInAtUtc, v.CompletedAtUtc, v.LastReceivedAtUtc, v.LocationConsent && v.Latitude.HasValue);
    private static IEnumerable<Customer> MobileCustomers(CrmDataSet d, AccessSnapshot a, OrganizationSelection org, string permission) => d.Customers.Where(x => x.Status == CustomerStatus.Active && Permit(a, permission, x, org) && Permit(a, "Customer.Read", x, org));
    public MobileWorkspaceDto Mobile(Guid userId, OrganizationSelection org, DateOnly? day, DateTimeOffset now)
    {
        var a = Access(userId); if (!a.PermissionsFor(org.CompanyId).Contains("Mobile.Visit.Read")) throw new UnauthorizedAccessException();
        var date = day ?? DateOnly.FromDateTime(now.UtcDateTime);
        return store.Read(d => new MobileWorkspaceDto(d.MobileVisits.Where(v => v.OwnerUserId == userId && Permit(a, "Mobile.Visit.Read", v, org) && MobileCustomers(d, a, org, "Mobile.Visit.Read").Any(c => c.Id == v.CustomerId) && DateOnly.FromDateTime(v.PlannedAtUtc.UtcDateTime) == date).OrderBy(v => v.PlannedAtUtc).Select(v => Map(d, v)).ToList(), MobileCustomers(d, a, org, "Mobile.Visit.Write").Select(x => new DealerCustomerOptionDto(x.Id, x.Code, x.Name, x.BranchId)).ToList(), date));
    }
    private static MobileAck? Replay(CrmDataSet d, Guid actor, string company, Guid operation, string fingerprint)
    {
        var r = d.MobileOperationReceipts.FirstOrDefault(x => x.CompanyId == company && x.ActorUserId == actor && x.OperationId == operation);
        if (r is null) return null;
        if (r.Fingerprint != fingerprint) throw new SelfServiceConflictException("شناسهٔ عملیات تکراری با محتوای متفاوت است.");
        var v = d.MobileVisits.First(x => x.Id == r.VisitId); return new(v.Id, r.AppliedVersion, true, Map(d, v));
    }
    private static MobileAck Receipt(CrmDataSet d, Guid actor, Guid operation, string fingerprint, MobileVisit v, DateTimeOffset now)
    { d.MobileOperationReceipts.Add(new(Guid.NewGuid(), v.CompanyId, actor, operation, v.Id, fingerprint, v.Version, now)); Audit(d, actor, "Mobile." + v.Status, v.Id, now); return new(v.Id, v.Version, false, Map(d, v)); }
    public MobileAck PlanVisit(Guid userId, OrganizationSelection org, CreateVisitCommand command, DateTimeOffset now)
    {
        var a = Access(userId); Operation(command.OperationId); var purpose = Text(command.Purpose, 1000, "هدف بازدید");
        var planned = new DateTimeOffset(DateTime.SpecifyKind(command.PlannedAtUtc, DateTimeKind.Utc));
        var fingerprint = Hash(new { Action="Plan", command.OperationId, command.CustomerId, Planned=planned, Purpose=purpose });
        return store.Write(d => {
            var c = MobileCustomers(d, a, org, "Mobile.Visit.Write").FirstOrDefault(x => x.Id == command.CustomerId) ?? throw new UnauthorizedAccessException();
            var previous = Replay(d, userId, org.CompanyId, command.OperationId, fingerprint); if (previous is not null) return previous;
            if (planned < now.AddDays(-1) || planned > now.AddDays(365)) throw new ArgumentException("زمان برنامه در بازهٔ مجاز نیست.");
            var visit = new MobileVisit(Guid.NewGuid(), org.CompanyId, c.BranchId, c.TerritoryId, c.Id, userId, planned, purpose);
            d.MobileVisits.Add(visit); return Receipt(d, userId, command.OperationId, fingerprint, visit, now);
        });
    }
    public MobileAck VisitAction(Guid userId, OrganizationSelection org, Guid id, VisitActionCommand command, DateTimeOffset now)
    {
        var a = Access(userId); Operation(command.OperationId);
        var outcome = command.Status == VisitStatus.CheckedIn ? (command.Outcome?.Trim() ?? "") : Text(command.Outcome, 2000, "نتیجه / دلیل");
        if (outcome.Length > 2000) throw new ArgumentException("شرح طولانی است.");
        if (command.Latitude.HasValue != command.Longitude.HasValue || command.Latitude is < -90 or > 90 || command.Longitude is < -180 or > 180 ||
            command.Latitude.HasValue && (!command.LocationConsent || command.Status != VisitStatus.CheckedIn)) throw new ArgumentException("ثبت موقعیت فقط هنگام شروع و با رضایت مجاز است.");
        var fingerprint = Hash(new { Action="Transition", Id=id, Command=command with { Outcome=outcome } });
        return store.Write(d => {
            var v = d.MobileVisits.FirstOrDefault(x => x.Id == id && x.OwnerUserId == userId && Permit(a, "Mobile.Visit.Write", x, org) && MobileCustomers(d, a, org, "Mobile.Visit.Write").Any(c => c.Id == x.CustomerId)) ?? throw new KeyNotFoundException();
            var previous = Replay(d, userId, org.CompanyId, command.OperationId, fingerprint); if (previous is not null) return previous;
            Version(v.Version, command.ExpectedVersion); var occurred = command.OccurredAtUtc ?? now;
            if (occurred < now.AddHours(-8) || occurred > now.AddMinutes(5)) throw new SelfServiceConflictException("زمان عملیات منقضی یا نامعتبر است؛ بازدید را آنلاین بررسی کنید.");
            v.Transition(command.Status, occurred, now, outcome, command.LocationConsent, command.Latitude, command.Longitude);
            if (command.Status==VisitStatus.Completed) d.Append<Crm.Domain.Customers.CustomerTimelineEvent>(new(Guid.NewGuid(),org.CompanyId,v.CustomerId,
                CustomerTimelineType.VisitCompleted,"بازدید انجام شد",outcome,occurred,"MobileCRM",v.Id.ToString("N"),userId));
            return Receipt(d, userId, command.OperationId, fingerprint, v, now);
        });
    }
}
