using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Identity;

namespace Crm.Application.Services;

public interface ICustomerListService
{
    Task<CustomerListDto> GetAsync(Guid currentUserId, OrganizationSelection organization, CustomerListQuery query, DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default);
    BulkResultDto PlanTasks(Guid currentUserId, OrganizationSelection organization, BulkAccountTaskCommand command, DateTimeOffset nowUtc);
}

/// <summary>
/// The customer/account list workspace: views, filters, counters and the next planned follow-up of each row,
/// filtered and paged in the data source (SQL for the EF store) inside the user's permission scope.
/// </summary>
public sealed class CustomerListService(ICrmQuerySource source, IAccessSnapshotService access, IAccountActivityService activities) : ICustomerListService
{
    public async Task<CustomerListDto> GetAsync(Guid currentUserId, OrganizationSelection organization, CustomerListQuery request,
        DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var snapshot = access.Get(currentUserId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var endOfToday = TehranTime.ToUtc(JalaliDate.Format(TehranTime.Today(now).AddDays(1)), "00:00", "") ?? now.AddDays(1);
        var permissions = snapshot.PermissionsFor(organization.CompanyId);
        var canReadActivities = permissions.Contains("Activity.Read");

        var users = await source.ToListAsync(source.Query<CrmUser>().Where(x => x.Id == currentUserId), cancellationToken);
        var myName = users.SingleOrDefault()?.DisplayName ?? "\0";
        var visible = source.Query<Customer>().InScope(snapshot, organization, "Customer.Read");
        var planned = source.Query<CrmActivity>().Where(x => x.CompanyId == organization.CompanyId && x.Status == ActivityStatus.Planned);
        var pendingDuplicates = source.Query<CustomerDuplicateCandidate>().Where(x => x.CompanyId == organization.CompanyId && x.Status == DuplicateReviewStatus.Pending);
        var active = visible.Where(x => x.Status != CustomerStatus.Inactive);
        var followUpDue = active.Where(x => planned.Any(a => a.CustomerId == x.Id && a.StartAtUtc < endOfToday));
        var noFollowUp = active.Where(x => !planned.Any(a => a.CustomerId == x.Id));
        var incomplete = active.Where(x => x.NationalId == null || x.PrimaryPhone == null || x.PrimaryEmail == null);
        var duplicates = visible.Where(x => pendingDuplicates.Any(d => d.CustomerId == x.Id || d.PossibleDuplicateCustomerId == x.Id));

        var view = request.View?.Trim().ToLowerInvariant() switch
        {
            "mine" or "nofollowup" or "inactive" or "followup" or "incomplete" or "duplicates" => request.View!.Trim().ToLowerInvariant(),
            _ => "all"
        };
        var filtered = view switch
        {
            "mine" => visible.Where(x => x.Owner == myName),
            "nofollowup" => noFollowUp,
            "inactive" => visible.Where(x => x.Status == CustomerStatus.Inactive),
            "followup" => followUpDue,
            "incomplete" => incomplete,
            "duplicates" => duplicates,
            _ => visible
        };
        if (!string.IsNullOrWhiteSpace(request.BranchId)) filtered = filtered.Where(x => x.BranchId == request.BranchId);
        if (request.Kind is { } kind) filtered = filtered.Where(x => x.Kind == kind);
        if (request.Relationship is { } relationship) filtered = filtered.Where(x => x.RelationshipType == relationship);
        if (!string.IsNullOrWhiteSpace(request.Segment)) filtered = filtered.Where(x => x.Segment == request.Segment);
        if (request.Status is { } status) filtered = filtered.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Owner)) filtered = filtered.Where(x => x.Owner == request.Owner);
        var term = request.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            filtered = filtered.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.City.Contains(term));

        var sort = request.Sort is "name" or "code" ? request.Sort : "recent";
        var ordered = sort switch
        {
            "name" => filtered.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "code" => filtered.OrderBy(x => x.Code).ThenBy(x => x.Id),
            _ => filtered.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
        var pageSize = request.PageSize is 10 or 20 or 50 or 100 ? request.PageSize : Math.Clamp(request.PageSize, 1, PageRequest.MaxPageSize);
        var page = await ordered.ToPageAsync(source, PageRequest.Of(request.Page, pageSize), x => x, term, cancellationToken);

        var ids = page.Items.Select(x => x.Id).ToArray();
        var contactIds = (await source.ToListAsync(source.Query<CustomerContact>().Where(x => ids.Contains(x.CustomerId) && x.IsActive).Select(x => x.CustomerId), cancellationToken)).ToHashSet();
        var addressIds = (await source.ToListAsync(source.Query<CustomerAddress>().Where(x => ids.Contains(x.CustomerId) && x.IsActive).Select(x => x.CustomerId), cancellationToken)).ToHashSet();
        var duplicateIds = (await source.ToListAsync(pendingDuplicates.Where(d => ids.Contains(d.CustomerId) || ids.Contains(d.PossibleDuplicateCustomerId))
            .Select(d => new { d.CustomerId, d.PossibleDuplicateCustomerId }), cancellationToken))
            .SelectMany(d => new[] { d.CustomerId, d.PossibleDuplicateCustomerId }).ToHashSet();
        var next = canReadActivities
            ? (await source.ToListAsync(planned.Where(a => ids.Contains(a.CustomerId)).Select(a => new { a.CustomerId, a.Subject, a.Type, a.StartAtUtc }), cancellationToken))
                .GroupBy(a => a.CustomerId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.StartAtUtc).First())
            : [];
        var rows = page.Items.Select(x =>
        {
            next.TryGetValue(x.Id, out var activity);
            return new CustomerListRowDto(CrmApplicationService.ToDto(x, contactIds.Contains(x.Id), addressIds.Contains(x.Id), snapshot),
                x.RelationshipType, activity?.Subject, activity?.Type, activity?.StartAtUtc, duplicateIds.Contains(x.Id));
        }).ToList();

        return new CustomerListDto(rows, page.Page, page.PageSize, page.TotalCount)
        {
            View = view,
            Query = term,
            BranchId = string.IsNullOrWhiteSpace(request.BranchId) ? null : request.BranchId,
            Kind = request.Kind,
            Relationship = request.Relationship,
            Segment = string.IsNullOrWhiteSpace(request.Segment) ? null : request.Segment,
            Status = request.Status,
            Owner = string.IsNullOrWhiteSpace(request.Owner) ? null : request.Owner,
            Sort = sort,
            VisibleCount = await source.CountAsync(visible, cancellationToken),
            ActiveCount = await source.CountAsync(visible.Where(x => x.Status == CustomerStatus.Active), cancellationToken),
            MineCount = await source.CountAsync(visible.Where(x => x.Owner == myName), cancellationToken),
            NoFollowUpCount = await source.CountAsync(noFollowUp, cancellationToken),
            InactiveCount = await source.CountAsync(visible.Where(x => x.Status == CustomerStatus.Inactive), cancellationToken),
            FollowUpDueCount = canReadActivities ? await source.CountAsync(followUpDue, cancellationToken) : 0,
            IncompleteCount = await source.CountAsync(incomplete, cancellationToken),
            DuplicateCount = await source.CountAsync(duplicates, cancellationToken),
            Segments = (await source.ToListAsync(visible.Select(x => x.Segment).Distinct(), cancellationToken)).OrderBy(x => x).ToList(),
            Owners = (await source.ToListAsync(visible.Select(x => x.Owner).Distinct(), cancellationToken)).OrderBy(x => x).ToList(),
            CanCreate = permissions.Contains("Customer.Create"),
            CanPlanActivities = permissions.Contains("Activity.Create"),
            CanReadActivities = canReadActivities,
            CanReviewDuplicates = permissions.Contains("Customer.MergeReview")
        };
    }

    /// <summary>Plans the same follow-up task on several accounts; each account goes through the normal activity rules.</summary>
    public BulkResultDto PlanTasks(Guid currentUserId, OrganizationSelection organization, BulkAccountTaskCommand command, DateTimeOffset nowUtc)
    {
        if (command.AccountIds.Count == 0) throw new InvalidOperationException("هیچ حسابی انتخاب نشده است.");
        if (command.AccountIds.Count > 200) throw new InvalidOperationException("حداکثر ۲۰۰ حساب در هر عملیات گروهی.");
        if (string.IsNullOrWhiteSpace(command.Subject)) throw new InvalidOperationException("عنوان وظیفه الزامی است.");
        var ok = 0;
        var failures = new List<string>();
        foreach (var id in command.AccountIds.Distinct())
        {
            try
            {
                activities.SaveActivity(currentUserId, organization, id, null, new SaveActivityCommand(ActivityType.Task, command.Subject, null, null,
                    currentUserId, command.DueDate, command.DueTime, null, null, null, null, null, command.Priority, null, ActivityRelatedKind.None, null,
                    null, null, Guid.NewGuid()), nowUtc);
                ok++;
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or ArgumentException)
            {
                failures.Add(exception.Message);
            }
        }
        return new BulkResultDto(ok, failures);
    }
}
