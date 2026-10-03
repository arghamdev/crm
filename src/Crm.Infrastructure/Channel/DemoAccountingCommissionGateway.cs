using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;

namespace Crm.Infrastructure.Channel;

/// <summary>
/// Stand-in for the accounting system: accepts a payout once per idempotency key and returns the same voucher reference
/// for a repeated key (as a real idempotent endpoint would). Replace with the accounting adapter in production.
/// </summary>
public sealed class DemoAccountingCommissionGateway : IAccountingCommissionGateway
{
    private readonly ConcurrentDictionary<string, string> _vouchers = new(StringComparer.Ordinal);

    public CommissionPayoutResult Post(string idempotencyKey, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || !payload.Contains("\"lines\"", StringComparison.Ordinal))
            return new CommissionPayoutResult(false, null, "سند ناقص است: ردیف‌های کمیسیون موجود نیست.");
        var voucher = _vouchers.GetOrAdd(idempotencyKey, key =>
            "ACC-CM-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10]);
        return new CommissionPayoutResult(true, voucher, null);
    }
}
