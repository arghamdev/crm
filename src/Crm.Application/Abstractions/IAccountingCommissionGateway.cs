using Crm.Application.Contracts;

namespace Crm.Application.Abstractions;

/// <summary>Accounting system port for approved dealer commissions. Implementations must treat the idempotency key as unique.</summary>
public interface IAccountingCommissionGateway
{
    CommissionPayoutResult Post(string idempotencyKey, string payload);
}
