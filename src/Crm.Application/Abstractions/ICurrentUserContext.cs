namespace Crm.Application.Abstractions;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    Guid CrmUserId { get; }
    Guid SessionId { get; }
    string? SelectedCompanyId { get; }
    string? SelectedBranchId { get; }
    string? SelectedTerritoryId { get; }
}
