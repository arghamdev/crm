using Crm.Application.Abstractions;
using Crm.Domain.Commercial;

namespace Crm.Infrastructure.Commercial;

public sealed class DemoAccountingCreditProvider : IAccountingCreditProvider
{
    public AccountingCreditSnapshot Get(string companyId, Guid customerId, DateTimeOffset nowUtc)
    {
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var arya = Guid.Parse("20000000-0000-4000-8000-000000000002");
        var nakhl = Guid.Parse("20000000-0000-4000-8000-000000000003");
        return customerId switch
        {
            var id when id == sepehr => new("ACC-C01-481", 20_000_000_000m, 1_840_000_000m,
                420_000_000m, true, "بدهی سررسیدشده و Hold حسابداری", "Accounting Mock / Credit API v1", nowUtc.AddMinutes(-2)),
            var id when id == arya => new("ACC-C01-480", 12_000_000_000m, 720_000_000m,
                0, false, "بدون مانع اعتباری", "Accounting Mock / Credit API v1", nowUtc.AddMinutes(-2)),
            var id when id == nakhl => new("ACC-C01-479", 8_000_000_000m, 2_480_000_000m,
                0, false, "بدون مانع اعتباری", "Accounting Mock / Credit API v1", nowUtc.AddMinutes(-2)),
            _ => new($"ACC-{companyId}-{customerId.ToString("N")[..8]}", 25_000_000_000m, 1_000_000_000m,
                0, false, "Snapshot اعتباری نمونه", "Accounting Mock / Credit API v1", nowUtc.AddMinutes(-2))
        };
    }
}

public sealed class DemoErpOrderGateway : IErpOrderGateway
{
    public ErpOrderSubmissionResult Submit(ErpOrderSubmission submission, DateTimeOffset nowUtc) => submission.SimulatedOutcome switch
    {
        ErpSubmissionOutcome.Accepted => new(ErpSubmissionOutcome.Accepted, "سفارش در ERP Mock پذیرفته شد.",
            $"ERP-{nowUtc.Year}-{submission.OrderRequestId.ToString("N")[..8].ToUpperInvariant()}"),
        ErpSubmissionOutcome.Pending => new(ErpSubmissionOutcome.Pending, "ERP پردازش را در حالت Pending نگه داشت.", null),
        ErpSubmissionOutcome.Rejected => new(ErpSubmissionOutcome.Rejected, "ERP درخواست را به‌دلیل محدودیت سفارش رد کرد.", null),
        ErpSubmissionOutcome.TransientFailure => new(ErpSubmissionOutcome.TransientFailure,
            "Timeout موقت در ERP Mock؛ Retry با همان Idempotency Key زمان‌بندی شد.", null),
        _ => throw new ArgumentOutOfRangeException(nameof(submission))
    };
}
