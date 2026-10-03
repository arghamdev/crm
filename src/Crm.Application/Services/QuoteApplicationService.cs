using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Commercial;
using Crm.Domain.Customers;
using Crm.Domain.Organization;
using Crm.Domain.Sales;

namespace Crm.Application.Services;

public sealed class QuoteApplicationService(
    ICrmDataStore store,
    IAccessSnapshotService access,
    IProductPriceCatalog priceCatalog) : IQuoteApplicationService
{
    public QuoteWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Read");
        return store.Read(data =>
        {
            var items = data.Quotes.Where(x => CanAccess(data, snapshot, currentUserId, organization, "Quote.Read", x))
                .OrderByDescending(x => x.CreatedAtUtc).Select(Map).ToArray();
            return new QuoteWorkspaceDto(items,
                items.Where(x => x.Status is not (QuoteStatus.Accepted or QuoteStatus.Rejected or QuoteStatus.Expired))
                    .Sum(x => x.NetAmount),
                items.Count(x => x.Status == QuoteStatus.Draft),
                items.Count(x => x.Status == QuoteStatus.PendingApproval),
                items.Count(x => (x.Status is QuoteStatus.Approved or QuoteStatus.Sent) && x.ValidUntilUtc <= nowUtc.AddDays(3)));
        });
    }

    public QuoteDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Read");
        return store.Read(data =>
        {
            var quote = data.Quotes.SingleOrDefault(x => x.Id == id && CanAccess(data, snapshot, currentUserId, organization, "Quote.Read", x));
            return quote is null ? null : MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public IReadOnlyList<ProductPriceDto> GetProducts(string companyId, string currencyCode = "IRR") =>
        priceCatalog.GetProducts(companyId, currencyCode).Select(x => new ProductPriceDto(x.Code, x.Name, x.Unit,
            x.CurrencyCode, x.ListUnitPrice, x.StandardUnitCost, x.Source, x.EffectiveAtUtc)).ToArray();

    public QuoteDetailsDto CreateDraft(Guid currentUserId, OrganizationSelection organization,
        CreateQuoteDraftCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Create");
        return store.Write(data =>
        {
            var customer = data.Customers.SingleOrDefault(x => x.Id == command.CustomerId && x.Status != CustomerStatus.Inactive &&
                InContext(snapshot, organization, "Customer.Read", x)) ?? throw new InvalidOperationException("مشتری فعال و مجاز انتخاب نشده است.");
            var opportunity = data.Opportunities.SingleOrDefault(x => x.Id == command.OpportunityId && x.CustomerId == customer.Id &&
                x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost) &&
                CanAccessOpportunity(data, snapshot, currentUserId, organization, x)) ??
                throw new InvalidOperationException("فرصت باز، مجاز و مرتبط با مشتری انتخاب نشده است.");
            if (!Same(opportunity.BranchId, command.BranchId)) throw new InvalidOperationException("شعبه پیشنهاد باید با فرصت یکسان باشد.");
            var price = RequiredPrice(organization.CompanyId, command.ProductCode, command.CurrencyCode);
            var quote = new Quote(Guid.NewGuid(), NextCode(data, nowUtc), customer.Name, customer.Id,
                opportunity.Title, opportunity.Id, opportunity.OwnerUserId, opportunity.CompanyId, opportunity.BranchId,
                opportunity.TerritoryId, price.CurrencyCode, command.ValidUntilUtc, command.PaymentTerms);
            data.Quotes.Add(quote);
            data.QuoteLines.Add(CreateLine(quote, price, command.Quantity, command.DiscountPercent));
            quote.RefreshTotals(data.QuoteLines);
            AddHistory(data, quote, null, QuoteStatus.Draft, "ایجاد پیش‌نویس", currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public QuoteDetailsDto AddLine(Guid currentUserId, OrganizationSelection organization, Guid id,
        AddQuoteLineCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Update");
        return store.Write(data =>
        {
            var quote = RequiredQuote(data, snapshot, currentUserId, organization, id, "Quote.Update");
            EnsureVersion(quote.Version, command.ExpectedVersion);
            if (data.QuoteLines.Any(x => x.QuoteId == id && Same(x.ProductCode, command.ProductCode)))
                throw new InvalidOperationException("این کالا قبلاً در پیشنهاد ثبت شده است؛ ابتدا ردیف قبلی را حذف کنید.");
            var price = RequiredPrice(organization.CompanyId, command.ProductCode, quote.CurrencyCode);
            data.QuoteLines.Add(CreateLine(quote, price, command.Quantity, command.DiscountPercent));
            quote.RefreshTotals(data.QuoteLines);
            return MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public QuoteDetailsDto RemoveLine(Guid currentUserId, OrganizationSelection organization, Guid id, Guid lineId,
        long expectedVersion, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Update");
        return store.Write(data =>
        {
            var quote = RequiredQuote(data, snapshot, currentUserId, organization, id, "Quote.Update");
            EnsureVersion(quote.Version, expectedVersion);
            var line = data.QuoteLines.SingleOrDefault(x => x.Id == lineId && x.QuoteId == id) ??
                throw new KeyNotFoundException("ردیف پیشنهاد پیدا نشد.");
            data.QuoteLines.Remove(line);
            quote.RefreshTotals(data.QuoteLines);
            return MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public QuoteDetailsDto Submit(Guid currentUserId, OrganizationSelection organization, Guid id,
        SubmitQuoteCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Submit");
        return store.Write(data =>
        {
            var quote = RequiredQuote(data, snapshot, currentUserId, organization, id, "Quote.Submit");
            EnsureVersion(quote.Version, command.ExpectedVersion);
            var lines = data.QuoteLines.Where(x => x.QuoteId == id).ToArray();
            if (lines.Length == 0) throw new InvalidOperationException("پیشنهاد بدون ردیف قابل ارسال نیست.");
            var level = QuoteApprovalPolicy.Resolve(lines.Max(x => x.DiscountPercent), quote.MarginPercent);
            var from = quote.Status;
            quote.Submit(level, nowUtc);
            AddHistory(data, quote, from, QuoteStatus.Submitted, command.Reason, currentUserId, nowUtc);
            AddHistory(data, quote, QuoteStatus.Submitted, quote.Status,
                level == QuoteApprovalLevel.None ? "تأیید خودکار مطابق ماتریس اختیار" : "ارجاع مطابق ماتریس اختیار",
                currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public QuoteDetailsDto Decide(Guid currentUserId, OrganizationSelection organization, Guid id,
        DecideQuoteCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var quote = RequiredQuote(data, snapshot, currentUserId, organization, id, "Quote.Read", false);
            EnsureVersion(quote.Version, command.ExpectedVersion);
            var decisions = data.QuoteApprovalDecisions.Where(x => x.QuoteId == id).ToArray();
            var pendingRole = PendingRoles(quote, decisions).Cast<QuoteApprovalRole?>()
                .FirstOrDefault(role => role.HasValue && CanApprove(snapshot, quote.CompanyId, role.Value));
            if (!pendingRole.HasValue)
                throw new UnauthorizedAccessException("مرحله تأیید فعلی در اختیار این کاربر نیست.");
            if (string.IsNullOrWhiteSpace(command.Comment)) throw new InvalidOperationException("ثبت توضیح تصمیم الزامی است.");
            data.QuoteApprovalDecisions.Add(new QuoteApprovalDecision(Guid.NewGuid(), quote.CompanyId, quote.BranchId,
                quote.TerritoryId, quote.Id, pendingRole.Value, command.Decision, command.Comment, currentUserId, nowUtc));
            var from = quote.Status;
            var approvalsComplete = command.Decision == QuoteDecision.Approved &&
                PendingRoles(quote, data.QuoteApprovalDecisions.Where(x => x.QuoteId == id)).Count == 0;
            quote.ApplyDecision(command.Decision, approvalsComplete, nowUtc);
            if (quote.Status != from)
                AddHistory(data, quote, from, quote.Status, command.Comment, currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, quote, nowUtc);
        });
    }

    public QuoteDetailsDto MarkSent(Guid currentUserId, OrganizationSelection organization, Guid id,
        long expectedVersion, DateTimeOffset nowUtc) => ChangeStatus(currentUserId, organization, id, expectedVersion,
            "Quote.Send", nowUtc, (quote, time) => quote.MarkSent(time), "ارسال پیشنهاد به مشتری");

    public QuoteDetailsDto RecordOutcome(Guid currentUserId, OrganizationSelection organization, Guid id,
        QuoteOutcomeCommand command, DateTimeOffset nowUtc)
    {
        var permission = command.Accepted ? "Quote.Accept" : "Quote.Expire";
        return ChangeStatus(currentUserId, organization, id, command.ExpectedVersion, permission, nowUtc,
            (quote, time) => { if (command.Accepted) quote.Accept(time); else quote.Expire(time); }, command.Reason);
    }

    public QuoteDetailsDto Revise(Guid currentUserId, OrganizationSelection organization, Guid id,
        ReviseQuoteCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Quote.Update");
        return store.Write(data =>
        {
            var source = RequiredQuote(data, snapshot, currentUserId, organization, id, "Quote.Read");
            EnsureVersion(source.Version, command.ExpectedVersion);
            if (source.Status is QuoteStatus.Draft or QuoteStatus.PendingApproval or QuoteStatus.Accepted)
                throw new InvalidOperationException("از این وضعیت نمی‌توان نسخه جدید ساخت.");
            var nextRevision = data.Quotes
                .Where(x => Same(x.CompanyId, source.CompanyId) && Same(x.Code, source.Code))
                .Max(x => x.Revision) + 1;
            var revision = new Quote(Guid.NewGuid(), source.Code, source.Customer, source.CustomerId, source.Opportunity,
                source.OpportunityId, source.OwnerUserId, source.CompanyId, source.BranchId, source.TerritoryId,
                source.CurrencyCode, command.ValidUntilUtc, source.PaymentTerms, nextRevision, source.Id);
            data.Quotes.Add(revision);
            foreach (var line in data.QuoteLines.Where(x => x.QuoteId == source.Id).ToArray())
                data.QuoteLines.Add(new QuoteLine(Guid.NewGuid(), revision.CompanyId, revision.BranchId,
                    revision.TerritoryId, revision.Id, line.ProductCode, line.ProductName, line.Unit, line.Quantity,
                    line.ListUnitPrice, line.StandardUnitCost, line.DiscountPercent, line.PriceSource, line.PriceEffectiveAtUtc));
            revision.RefreshTotals(data.QuoteLines);
            AddHistory(data, revision, null, QuoteStatus.Draft, command.Reason, currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, revision, nowUtc);
        });
    }

    private QuoteDetailsDto ChangeStatus(Guid userId, OrganizationSelection organization, Guid id, long expectedVersion,
        string permission, DateTimeOffset nowUtc, Action<Quote, DateTimeOffset> change, string reason)
    {
        var snapshot = RequiredSnapshot(userId);
        RequirePermission(snapshot, organization.CompanyId, permission);
        return store.Write(data =>
        {
            var quote = RequiredQuote(data, snapshot, userId, organization, id, "Quote.Read");
            EnsureVersion(quote.Version, expectedVersion);
            var from = quote.Status;
            change(quote, nowUtc);
            AddHistory(data, quote, from, quote.Status, reason, userId, nowUtc);
            return MapDetails(data, snapshot, userId, quote, nowUtc);
        });
    }

    private Quote RequiredQuote(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, Guid id, string permission, bool requireOwnership = true)
    {
        var quote = data.Quotes.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
            throw new KeyNotFoundException("پیشنهاد در دامنه جاری پیدا نشد.");
        if (requireOwnership && !CanManage(data, snapshot, userId, quote))
            throw new UnauthorizedAccessException("پیشنهاد متعلق به کاربر دیگری است.");
        return quote;
    }

    private QuoteDetailsDto MapDetails(CrmDataSet data, AccessSnapshot snapshot, Guid userId, Quote quote, DateTimeOffset nowUtc)
    {
        var decisions = data.QuoteApprovalDecisions.Where(x => x.QuoteId == quote.Id).OrderBy(x => x.DecidedAtUtc).ToArray();
        var pending = PendingRoles(quote, decisions);
        var canManage = CanManage(data, snapshot, userId, quote);
        return new QuoteDetailsDto(Map(quote),
            data.QuoteLines.Where(x => x.QuoteId == quote.Id).OrderBy(x => x.ProductCode).Select(Map).ToArray(),
            decisions.Select(x => Map(data, x)).ToArray(),
            data.QuoteStatusHistory.Where(x => x.QuoteId == quote.Id).OrderByDescending(x => x.ChangedAtUtc).Select(x => Map(data, x)).ToArray(),
            pending, canManage && quote.Status == QuoteStatus.Draft && HasPermission(snapshot, quote.CompanyId, "Quote.Update"),
            canManage && quote.Status == QuoteStatus.Draft && HasPermission(snapshot, quote.CompanyId, "Quote.Submit"),
            quote.Status == QuoteStatus.PendingApproval && pending.Any(x => CanApprove(snapshot, quote.CompanyId, x)),
            canManage && quote.Status == QuoteStatus.Approved && quote.ValidUntilUtc > nowUtc && HasPermission(snapshot, quote.CompanyId, "Quote.Send"),
            canManage && quote.Status == QuoteStatus.Sent && HasPermission(snapshot, quote.CompanyId, "Quote.Accept"),
            canManage && quote.Status is not (QuoteStatus.Draft or QuoteStatus.PendingApproval or QuoteStatus.Accepted) &&
                HasPermission(snapshot, quote.CompanyId, "Quote.Update"));
    }

    private ProductPriceSnapshot RequiredPrice(string companyId, string productCode, string currencyCode) =>
        priceCatalog.Find(companyId, productCode, currencyCode) ??
        throw new InvalidOperationException("کالا یا قیمت مؤثر در ERP Mock پیدا نشد.");

    private static QuoteLine CreateLine(Quote quote, ProductPriceSnapshot price, decimal quantity, decimal discount) =>
        new(Guid.NewGuid(), quote.CompanyId, quote.BranchId, quote.TerritoryId, quote.Id, price.Code, price.Name,
            price.Unit, quantity, price.ListUnitPrice, price.StandardUnitCost, discount, price.Source, price.EffectiveAtUtc);

    private static IReadOnlyList<QuoteApprovalRole> PendingRoles(Quote quote, IEnumerable<QuoteApprovalDecision> decisions)
    {
        var approved = decisions.Where(x => x.Decision == QuoteDecision.Approved).Select(x => x.Role).ToHashSet();
        return QuoteApprovalPolicy.RequiredRoles(quote.ApprovalLevel).Where(x => !approved.Contains(x)).ToArray();
    }

    private static bool CanApprove(AccessSnapshot snapshot, string companyId, QuoteApprovalRole role) => role switch
    {
        QuoteApprovalRole.SalesSupervisor => HasPermission(snapshot, companyId, "Quote.Approve.Supervisor"),
        QuoteApprovalRole.CommercialManager => HasPermission(snapshot, companyId, "Quote.Approve.Commercial"),
        QuoteApprovalRole.SalesManager => HasPermission(snapshot, companyId, "Quote.Approve.Sales"),
        QuoteApprovalRole.FinanceManager => HasPermission(snapshot, companyId, "Quote.Approve.Finance"),
        _ => false
    };

    private static bool CanAccess(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, string permission, Quote quote) =>
        InContext(snapshot, organization, permission, quote) && CanManage(data, snapshot, userId, quote);

    private static bool CanManage(CrmDataSet data, AccessSnapshot snapshot, Guid userId, Quote quote)
    {
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, quote.CompanyId) && x.RoleKey is "SalesManager" or "SalesSupervisor" or "FinanceManager"))
            return true;
        if (quote.OwnerUserId.HasValue) return quote.OwnerUserId == userId;
        return data.Opportunities.Any(x => x.Id == quote.OpportunityId && x.OwnerUserId == userId);
    }

    private static bool CanAccessOpportunity(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, Opportunity opportunity)
    {
        if (!InContext(snapshot, organization, "Opportunity.Read", opportunity)) return false;
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, opportunity.CompanyId) && x.RoleKey is "SalesManager" or "SalesSupervisor")) return true;
        return opportunity.OwnerUserId == userId || data.Users.SingleOrDefault(x => x.Id == userId)?.DisplayName == opportunity.Owner;
    }

    private static void AddHistory(CrmDataSet data, Quote quote, QuoteStatus? from, QuoteStatus to,
        string reason, Guid userId, DateTimeOffset nowUtc) => data.QuoteStatusHistory.Add(new QuoteStatusHistory(
            Guid.NewGuid(), quote.CompanyId, quote.BranchId, quote.TerritoryId, quote.Id, from, to,
            string.IsNullOrWhiteSpace(reason) ? "تغییر وضعیت" : reason, userId, nowUtc));

    private static string NextCode(CrmDataSet data, DateTimeOffset nowUtc) =>
        RecordCodes.Next(data.Quotes.Select(x => x.Code), $"Q-{nowUtc.Year}-", 31, 3);
    private static QuoteSummaryDto Map(Quote x) => new(x.Id, x.Code, x.Revision, x.Customer, x.Opportunity,
        x.CustomerId, x.OpportunityId, x.OwnerUserId, x.CompanyId, x.BranchId, x.TerritoryId, x.CurrencyCode,
        x.ValidUntilUtc, x.GrossAmount, x.DiscountAmount, x.NetAmount, x.DiscountPercent, x.MarginPercent,
        x.ApprovalLevel, x.Status, x.Version);
    private static QuoteLineDto Map(QuoteLine x) => new(x.Id, x.ProductCode, x.ProductName, x.Unit, x.Quantity,
        x.ListUnitPrice, x.DiscountPercent, x.GrossAmount, x.DiscountAmount, x.NetAmount, x.MarginPercent,
        x.PriceSource, x.PriceEffectiveAtUtc);
    private static QuoteApprovalDecisionDto Map(CrmDataSet data, QuoteApprovalDecision x) => new(x.Id, x.Role,
        x.Decision, x.Comment, x.DecidedByUserId, Actor(data, x.DecidedByUserId), x.DecidedAtUtc);
    private static QuoteStatusHistoryDto Map(CrmDataSet data, QuoteStatusHistory x) => new(x.Id, x.FromStatus,
        x.ToStatus, x.Reason, x.ChangedByUserId, Actor(data, x.ChangedByUserId), x.ChangedAtUtc);
    private static string Actor(CrmDataSet data, Guid id) => data.Users.SingleOrDefault(x => x.Id == id)?.DisplayName ?? "سیستم";

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");
    private static void RequirePermission(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!HasPermission(snapshot, companyId, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
    }
    private static bool HasPermission(AccessSnapshot snapshot, string companyId, string permission) => snapshot.PermissionsFor(companyId).Contains(permission);
    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        Same(entity.CompanyId, organization.CompanyId) && (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InvalidOperationException("رکورد تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
