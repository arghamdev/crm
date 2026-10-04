using Crm.Application.Abstractions;
using Crm.Domain.Accounts;

namespace Crm.Application.Services;

/// <summary>
/// Double-submit protection: every quick-create form carries a one-time operation id. The first submit stores
/// (user, operation id) next to the created record in the same write; a repeated submit gets the same record back.
/// </summary>
public static class ClientOperations
{
    public static Guid? Existing(CrmDataSet data, Guid userId, Guid? operationId) =>
        operationId is { } op && op != Guid.Empty
            ? data.Find<ClientOperation>(x => x.UserId == userId && x.OperationId == op).Select(x => (Guid?)x.EntityId).FirstOrDefault()
            : null;

    public static void Record(CrmDataSet data, Guid userId, Guid? operationId, string kind, Guid entityId)
    {
        if (operationId is { } op && op != Guid.Empty) data.Append(new ClientOperation(Guid.NewGuid(), userId, op, kind, entityId));
    }
}
