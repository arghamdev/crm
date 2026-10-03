using System.Security.Cryptography;
using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Commercial;
using Crm.Domain.Organization;
using Crm.Domain.Work;

namespace Crm.Application.Services;

public sealed class OrderApplicationService(
    ICrmDataStore store,
    IAccessSnapshotService access,
    IAccountingCreditProvider accounting,
    IErpOrderGateway erp) : IOrderApplicationService
{
    public OrderWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Read");
        return store.Read(data =>
        {
            var orders = data.OrderRequests
                .Where(x => InContext(snapshot, organization, "Order.Read", x) && CanManage(snapshot, currentUserId, x))
                .OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Code)
                .ToArray();
            var ids = orders.Select(x => x.Id).ToHashSet();
            var errors = data.OrderIntegrationMessages
                .Where(x => ids.Contains(x.OrderRequestId) && x.Status is OrderIntegrationStatus.RetryScheduled or OrderIntegrationStatus.DeadLetter)
                .OrderByDescending(x => x.Status == OrderIntegrationStatus.DeadLetter)
                .ThenBy(x => x.NextAttemptAtUtc)
                .Select(Map).ToArray();
            return new OrderWorkspaceDto(orders.Select(Map).ToArray(), errors,
                orders.Where(x => x.Status is not (OrderRequestStatus.Paid or OrderRequestStatus.Cancelled or OrderRequestStatus.ErpRejected))
                    .Sum(x => x.NetAmount),
                orders.Count(x => x.Status == OrderRequestStatus.CreditHold),
                orders.Count(x => x.Status == OrderRequestStatus.SubmissionPending),
                orders.Count(x => x.Status == OrderRequestStatus.IntegrationFailed));
        });
    }

    public OrderDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Read");
        return store.Read(data =>
        {
            var order = data.OrderRequests.SingleOrDefault(x => x.Id == id &&
                InContext(snapshot, organization, "Order.Read", x) && CanManage(snapshot, currentUserId, x));
            return order is null ? null : MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public IReadOnlyList<EligibleOrderQuoteDto> GetEligibleQuotes(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Create");
        return store.Read(data => data.Quotes
            .Where(x => x.Status == QuoteStatus.Accepted && !data.OrderRequests.Any(o => o.QuoteId == x.Id) &&
                InContext(snapshot, organization, "Order.Create", x) && CanManageQuote(data, snapshot, currentUserId, x))
            .OrderByDescending(x => x.AcceptedAtUtc)
            .Select(x => new EligibleOrderQuoteDto(x.Id, x.Code, x.Revision, x.Customer, x.Opportunity,
                x.NetAmount, x.CurrencyCode, x.Version)).ToArray());
    }

    public OrderDetailsDto Create(Guid currentUserId, OrganizationSelection organization,
        CreateOrderRequestCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Create");
        return store.Write(data =>
        {
            var quote = data.Quotes.SingleOrDefault(x => x.Id == command.QuoteId && x.Status == QuoteStatus.Accepted &&
                InContext(snapshot, organization, "Order.Create", x)) ??
                throw new KeyNotFoundException("پیشنهاد پذیرفته‌شده در دامنه جاری پیدا نشد.");
            if (!CanManageQuote(data, snapshot, currentUserId, quote))
                throw new UnauthorizedAccessException("پیشنهاد متعلق به کاربر دیگری است.");
            EnsureVersion(quote.Version, command.ExpectedQuoteVersion);
            if (data.OrderRequests.Any(x => x.QuoteId == quote.Id))
                throw new InvalidOperationException("برای این نسخه پیشنهاد قبلاً درخواست سفارش ساخته شده است.");
            var order = new OrderRequest(Guid.NewGuid(), NextCode(data, nowUtc), quote.Id,
                $"{quote.Code}/R{quote.Revision}", quote.CustomerId, quote.Customer, quote.OpportunityId,
                quote.OwnerUserId, quote.CompanyId, quote.BranchId, quote.TerritoryId, quote.CurrencyCode,
                quote.NetAmount, $"crm-{Guid.NewGuid():N}");
            data.OrderRequests.Add(order);
            AddHistory(data, order, null, OrderRequestStatus.Draft, "ایجاد درخواست از پیشنهاد پذیرفته‌شده",
                "CRM", currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public OrderDetailsDto CheckCredit(Guid currentUserId, OrganizationSelection organization, Guid id,
        CheckOrderCreditCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.CreditCheck");
        return store.Write(data =>
        {
            var order = RequiredOrder(data, snapshot, currentUserId, organization, id, "Order.CreditCheck");
            EnsureVersion(order.Version, command.ExpectedVersion);
            var credit = accounting.Get(order.CompanyId, order.CustomerId, nowUtc);
            var from = order.Status;
            order.ApplyCreditSnapshot(credit.CreditLimit, credit.CreditUsed, credit.OverdueAmount,
                credit.IsCreditHold, credit.HoldReason, credit.Source, credit.SnapshotAtUtc, nowUtc);
            var decision = order.Status == OrderRequestStatus.CreditHold
                ? OrderCreditDecisionType.Held : OrderCreditDecisionType.Approved;
            data.OrderCreditDecisions.Add(new OrderCreditDecision(Guid.NewGuid(), order.CompanyId, order.BranchId,
                order.TerritoryId, order.Id, decision, order.CreditLimit, order.CreditUsed, order.OverdueAmount,
                order.AvailableCredit, order.CreditReason ?? credit.HoldReason, order.CreditSource ?? credit.Source,
                currentUserId, nowUtc));
            AddHistory(data, order, from, order.Status, order.CreditReason ?? "بررسی اعتبار",
                credit.Source, currentUserId, nowUtc);
            if (order.Status == OrderRequestStatus.CreditHold)
                CreateFinanceWorkItem(data, order, nowUtc, $"بررسی Hold اعتباری {order.Code}");
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public OrderDetailsDto OverrideCredit(Guid currentUserId, OrganizationSelection organization, Guid id,
        OverrideOrderCreditCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.CreditOverride");
        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new InvalidOperationException("دلیل Override اعتباری الزامی است.");
        return store.Write(data =>
        {
            var order = RequiredOrder(data, snapshot, currentUserId, organization, id, "Order.CreditOverride", false);
            EnsureVersion(order.Version, command.ExpectedVersion);
            var from = order.Status;
            order.OverrideCreditHold(command.Reason, command.ExpiresAtUtc, nowUtc);
            data.OrderCreditDecisions.Add(new OrderCreditDecision(Guid.NewGuid(), order.CompanyId, order.BranchId,
                order.TerritoryId, order.Id, OrderCreditDecisionType.Overridden, order.CreditLimit, order.CreditUsed,
                order.OverdueAmount, order.AvailableCredit, command.Reason, "Finance Override", currentUserId,
                nowUtc, command.ExpiresAtUtc));
            AddHistory(data, order, from, order.Status, command.Reason, "Finance Override", currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public OrderDetailsDto QueueSubmission(Guid currentUserId, OrganizationSelection organization, Guid id,
        QueueOrderSubmissionCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Submit");
        return store.Write(data =>
        {
            var order = RequiredOrder(data, snapshot, currentUserId, organization, id, "Order.Submit");
            EnsureVersion(order.Version, command.ExpectedVersion);
            if (data.OrderIntegrationMessages.Any(x => x.OrderRequestId == order.Id))
                throw new InvalidOperationException("پیام ارسال این سفارش قبلاً ساخته شده و باید با همان Idempotency Key ادامه یابد.");
            var from = order.Status;
            order.QueueSubmission(nowUtc);
            data.OrderIntegrationMessages.Add(new OrderIntegrationMessage(Guid.NewGuid(), order.CompanyId,
                order.BranchId, order.TerritoryId, order.Id, order.SubmissionIdempotencyKey, order.CorrelationId,
                Fingerprint(order)));
            AddHistory(data, order, from, order.Status, "قرارگیری در صف ارسال ERP", "CRM Outbox",
                currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public OrderDetailsDto ProcessIntegration(Guid currentUserId, OrganizationSelection organization, Guid id,
        ProcessOrderIntegrationCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Integration.Process");
        return store.Write(data =>
        {
            var order = RequiredOrder(data, snapshot, currentUserId, organization, id,
                "Order.Integration.Process", false);
            EnsureVersion(order.Version, command.ExpectedOrderVersion);
            var message = data.OrderIntegrationMessages.SingleOrDefault(x => x.OrderRequestId == order.Id) ??
                throw new KeyNotFoundException("پیام یکپارچه‌سازی سفارش پیدا نشد.");
            EnsureVersion(message.Version, command.ExpectedMessageVersion);
            var from = order.Status;
            if (order.Status == OrderRequestStatus.IntegrationFailed)
            {
                order.QueueSubmission(nowUtc);
                AddHistory(data, order, from, order.Status, "تلاش مجدد با همان Idempotency Key",
                    "CRM Retry", currentUserId, nowUtc);
                from = order.Status;
            }
            var attemptNumber = message.BeginAttempt(nowUtc);
            var result = erp.Submit(new ErpOrderSubmission(order.Id, order.Code, message.IdempotencyKey,
                message.CorrelationId, order.CustomerId, order.NetAmount, order.CurrencyCode,
                command.SimulatedOutcome), nowUtc);
            order.ApplySubmissionResult(result.Outcome, result.ExternalReference, result.Detail, nowUtc);
            data.OrderIntegrationAttempts.Add(new OrderIntegrationAttempt(Guid.NewGuid(), order.CompanyId,
                order.BranchId, order.TerritoryId, message.Id, order.Id, attemptNumber, result.Outcome,
                result.Detail, result.ExternalReference, nowUtc));
            switch (result.Outcome)
            {
                case ErpSubmissionOutcome.Accepted:
                case ErpSubmissionOutcome.Rejected:
                    message.Complete(result.ExternalReference, nowUtc);
                    break;
                case ErpSubmissionOutcome.Pending:
                    message.AwaitExternal(nowUtc);
                    break;
                case ErpSubmissionOutcome.TransientFailure:
                    message.Fail(result.Detail, nowUtc);
                    break;
            }
            if (order.Status != from)
                AddHistory(data, order, from, order.Status, result.Detail, "ERP Mock", currentUserId, nowUtc);
            if (message.Status == OrderIntegrationStatus.DeadLetter)
                CreateFinanceWorkItem(data, order, nowUtc, $"رسیدگی به خطای نهایی ERP برای {order.Code}");
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    public OrderDetailsDto AdvanceProjection(Guid currentUserId, OrganizationSelection organization, Guid id,
        AdvanceOrderProjectionCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Order.Sync");
        return store.Write(data =>
        {
            var order = RequiredOrder(data, snapshot, currentUserId, organization, id, "Order.Sync", false);
            EnsureVersion(order.Version, command.ExpectedVersion);
            var from = order.Status;
            order.AdvanceProjection(command.TargetStatus, command.ExternalReference, nowUtc);
            AddHistory(data, order, from, order.Status, $"همگام‌سازی مرجع {command.ExternalReference}",
                "ERP Projection", currentUserId, nowUtc);
            return MapDetails(data, snapshot, currentUserId, order, nowUtc);
        });
    }

    private OrderRequest RequiredOrder(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrganizationSelection organization, Guid id, string permission, bool requireOwnership = true)
    {
        var order = data.OrderRequests.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
            throw new KeyNotFoundException("درخواست سفارش در دامنه جاری پیدا نشد.");
        if (requireOwnership && !CanManage(snapshot, userId, order))
            throw new UnauthorizedAccessException("درخواست سفارش متعلق به کاربر دیگری است.");
        return order;
    }

    private static OrderDetailsDto MapDetails(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        OrderRequest order, DateTimeOffset nowUtc)
    {
        var integration = data.OrderIntegrationMessages.SingleOrDefault(x => x.OrderRequestId == order.Id);
        var canManage = CanManage(snapshot, userId, order);
        var canProcess = integration is not null &&
            integration.Status is OrderIntegrationStatus.Pending or OrderIntegrationStatus.RetryScheduled or OrderIntegrationStatus.AwaitingExternal &&
            (!integration.NextAttemptAtUtc.HasValue || integration.NextAttemptAtUtc <= nowUtc);
        return new OrderDetailsDto(Map(order), order.CreditLimit, order.CreditUsed, order.OverdueAmount,
            order.AvailableCredit, order.IsCreditHold, order.CreditReason, order.CreditSource,
            order.CreditSnapshotAtUtc, order.CreditOverrideExpiresAtUtc, order.DeliveryReference,
            order.InvoiceNumber, order.PaymentReference, integration is null ? null : Map(integration),
            data.OrderCreditDecisions.Where(x => x.OrderRequestId == order.Id).OrderByDescending(x => x.DecidedAtUtc)
                .Select(x => Map(data, x)).ToArray(),
            data.OrderStatusHistory.Where(x => x.OrderRequestId == order.Id).OrderByDescending(x => x.ChangedAtUtc)
                .Select(x => Map(data, x)).ToArray(),
            data.OrderIntegrationAttempts.Where(x => x.OrderRequestId == order.Id).OrderByDescending(x => x.AttemptNumber)
                .Select(Map).ToArray(),
            canManage && order.Status is OrderRequestStatus.Draft or OrderRequestStatus.CreditHold &&
                HasPermission(snapshot, order.CompanyId, "Order.CreditCheck"),
            order.Status == OrderRequestStatus.CreditHold && HasPermission(snapshot, order.CompanyId, "Order.CreditOverride"),
            canManage && order.Status == OrderRequestStatus.CreditApproved && integration is null &&
                HasPermission(snapshot, order.CompanyId, "Order.Submit"),
            canProcess && HasPermission(snapshot, order.CompanyId, "Order.Integration.Process"),
            order.Status is OrderRequestStatus.ErpAccepted or OrderRequestStatus.Allocated or OrderRequestStatus.Delivered or OrderRequestStatus.Invoiced &&
                HasPermission(snapshot, order.CompanyId, "Order.Sync"));
    }

    private static void CreateFinanceWorkItem(CrmDataSet data, OrderRequest order, DateTimeOffset nowUtc, string title)
    {
        if (data.WorkItems.Any(x => Same(x.CompanyId, order.CompanyId) && Same(x.Title, title) && !x.IsDone)) return;
        var finance = data.UserRoleAssignments.FirstOrDefault(x => Same(x.CompanyId, order.CompanyId) &&
            Same(x.RoleKey, "FinanceManager") && x.IsEffective(nowUtc));
        if (finance is null) return;
        data.WorkItems.Add(new CrmWorkItem(Guid.NewGuid(), title, "فوری", nowUtc.AddHours(4),
            finance.CrmUserId, order.CompanyId, order.BranchId, order.TerritoryId));
    }

    private static void AddHistory(CrmDataSet data, OrderRequest order, OrderRequestStatus? from,
        OrderRequestStatus to, string reason, string source, Guid userId, DateTimeOffset nowUtc) =>
        data.OrderStatusHistory.Add(new OrderStatusHistory(Guid.NewGuid(), order.CompanyId, order.BranchId,
            order.TerritoryId, order.Id, from, to, string.IsNullOrWhiteSpace(reason) ? "تغییر وضعیت" : reason,
            source, userId, nowUtc));

    private static string Fingerprint(OrderRequest order)
    {
        var input = $"{order.Id:N}|{order.Code}|{order.CustomerId:N}|{order.NetAmount:0.####}|{order.CurrencyCode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string NextCode(CrmDataSet data, DateTimeOffset nowUtc) =>
        RecordCodes.Next(data.OrderRequests.Select(x => x.Code), $"OR-{nowUtc.Year}-", 1, 3);

    private static OrderSummaryDto Map(OrderRequest x) => new(x.Id, x.Code, x.QuoteCode, x.QuoteId,
        x.Customer, x.CustomerId, x.CompanyId, x.BranchId, x.TerritoryId, x.CurrencyCode, x.NetAmount,
        x.Status, x.ErpOrderNumber, x.LastSynchronizedAtUtc, x.Version);
    private static OrderIntegrationMessageDto Map(OrderIntegrationMessage x) => new(x.Id, x.OrderRequestId, x.MessageType,
        x.IdempotencyKey, x.CorrelationId, x.Status, x.AttemptCount, x.NextAttemptAtUtc, x.LastError,
        x.ExternalReference, x.Version);
    private static OrderCreditDecisionDto Map(CrmDataSet data, OrderCreditDecision x) => new(x.Id, x.Decision,
        x.CreditLimit, x.CreditUsed, x.OverdueAmount, x.AvailableCredit, x.Reason, x.Source,
        Actor(data, x.DecidedByUserId), x.DecidedAtUtc, x.ExpiresAtUtc);
    private static OrderStatusHistoryDto Map(CrmDataSet data, OrderStatusHistory x) => new(x.Id,
        x.FromStatus, x.ToStatus, x.Reason, x.Source, Actor(data, x.ChangedByUserId), x.ChangedAtUtc);
    private static OrderIntegrationAttemptDto Map(OrderIntegrationAttempt x) => new(x.Id, x.AttemptNumber,
        x.Outcome, x.Detail, x.ExternalReference, x.AttemptedAtUtc);
    private static string Actor(CrmDataSet data, Guid id) =>
        data.Users.SingleOrDefault(x => x.Id == id)?.DisplayName ?? "سیستم";

    private static bool CanManage(AccessSnapshot snapshot, Guid userId, OrderRequest order)
    {
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, order.CompanyId) &&
            x.RoleKey is "SalesManager" or "SalesSupervisor" or "FinanceManager")) return true;
        return order.OwnerUserId == userId;
    }

    private static bool CanManageQuote(CrmDataSet data, AccessSnapshot snapshot, Guid userId, Quote quote)
    {
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, quote.CompanyId) &&
            x.RoleKey is "SalesManager" or "SalesSupervisor" or "FinanceManager")) return true;
        if (quote.OwnerUserId.HasValue) return quote.OwnerUserId == userId;
        return data.Opportunities.Any(x => x.Id == quote.OpportunityId && x.OwnerUserId == userId);
    }

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("No active access snapshot was found.");
    private static void RequirePermission(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!HasPermission(snapshot, companyId, permission))
            throw new UnauthorizedAccessException($"{permission} permission is required.");
    }
    private static bool HasPermission(AccessSnapshot snapshot, string companyId, string permission) =>
        snapshot.PermissionsFor(companyId).Contains(permission);
    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission,
        IOrganizationScoped entity) => Same(entity.CompanyId, organization.CompanyId) &&
        (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InvalidOperationException("رکورد تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
