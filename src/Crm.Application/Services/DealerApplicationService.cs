using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public sealed class DealerApplicationService(
    ICrmDataStore store,
    IAccessSnapshotService access,
    IDealerFinancialProjectionProvider financialProvider,
    IDealerPerformanceProjectionProvider performanceProvider) : IDealerApplicationService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(15);

    public DealerWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Read");
        return store.Read(data =>
        {
            var visible = data.Dealers.Where(x => InContext(snapshot, organization, "Dealer.Read", x)).
                OrderBy(x => x.TradeName).ToArray();
            var items = visible.Select(x => MapSummary(data, snapshot, x, nowUtc)).ToArray();
            var contractLimit = nowUtc.AddDays(30);
            var mayReadFinancial = HasPermission(snapshot, organization.CompanyId, "Dealer.Financial.Read");
            return new DealerWorkspaceDto(items,
                items.Count(x => x.Status == DealerStatus.Active),
                items.Count(x => x.Status == DealerStatus.PendingApproval),
                data.DealerContracts.Count(x => visible.Any(d => d.Id == x.DealerId) &&
                    x.Status == DealerContractStatus.Active && x.ValidToUtc > nowUtc && x.ValidToUtc <= contractLimit),
                mayReadFinancial
                    ? items.Count(x => x.FinancialSynchronizedAtUtc is null || nowUtc - x.FinancialSynchronizedAtUtc > FreshnessWindow)
                    : 0,
                mayReadFinancial ? items.Sum(x => x.OverdueAmount ?? 0) : null,
                items.Sum(x => x.TargetAmount), items.Sum(x => x.NetSales));
        });
    }

    public DealerDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Read");
        return store.Read(data =>
        {
            var dealer = data.Dealers.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, "Dealer.Read", x));
            return dealer is null ? null : MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerFormDto GetForm(Guid currentUserId, OrganizationSelection organization, Guid? id, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequireCompanyManagement(snapshot, organization.CompanyId, "Dealer.Manage");
        return store.Read(data =>
        {
            var dealer = id.HasValue
                ? RequiredDealer(data, snapshot, organization, id.Value, "Dealer.Manage")
                : null;
            var branches = data.OrganizationUnits.Where(x => x.CompanyId == organization.CompanyId &&
                    x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active &&
                    snapshot.Allows(organization.CompanyId, "Dealer.Manage", "Branch", x.UnitId))
                .OrderBy(x => x.Name)
                .Select(x => new OrganizationUnitOptionDto(x.UnitId, x.Code, x.Name, x.Type.ToString(),
                    x.UnitId == dealer?.BranchId)).ToArray();
            var territories = data.Territories.Where(x => x.CompanyId == organization.CompanyId && x.IsEffective(nowUtc) &&
                    snapshot.Allows(organization.CompanyId, "Dealer.Manage", "Territory", x.TerritoryId))
                .OrderBy(x => x.Name)
                .Select(x => new TerritoryOptionDto(x.TerritoryId, x.Code, x.Name, x.Dimension.ToString(),
                    x.TerritoryId == dealer?.TerritoryId)).ToArray();
            var companyUserIds = data.UserRoleAssignments.Where(x => x.CompanyId == organization.CompanyId &&
                    x.RoleKey.Equals("ChannelManager", StringComparison.OrdinalIgnoreCase) && x.IsEffective(nowUtc))
                .Select(x => x.CrmUserId).Distinct().ToHashSet();
            var managers = data.Users.Where(x => companyUserIds.Contains(x.Id) && x.IsActiveAt(nowUtc))
                .OrderBy(x => x.DisplayName).Select(x => new DealerManagerOptionDto(x.Id, x.DisplayName)).ToArray();
            return new DealerFormDto(dealer?.Id, dealer?.DealerId ?? string.Empty, dealer?.Code ?? string.Empty,
                dealer?.LegalName ?? string.Empty, dealer?.TradeName ?? string.Empty,
                dealer?.BranchId ?? branches.FirstOrDefault()?.Id ?? string.Empty, dealer?.TerritoryId,
                dealer?.City ?? string.Empty, dealer?.NationalId, dealer?.Phone, dealer?.Email,
                dealer?.ChannelManagerUserId ?? managers.FirstOrDefault()?.Id ?? Guid.Empty,
                dealer?.Version ?? 0, branches, territories, managers);
        });
    }

    public IReadOnlyList<TerritoryOptionDto> GetTerritoryOptions(Guid currentUserId,
        OrganizationSelection organization, Guid dealerId, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Territory.Request");
        return store.Read(data =>
        {
            _ = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Territory.Request");
            return data.Territories.Where(x => x.CompanyId == organization.CompanyId && x.IsEffective(nowUtc) &&
                    snapshot.Allows(organization.CompanyId, "Dealer.Territory.Request", "Territory", x.TerritoryId))
                .OrderBy(x => x.Name).Select(x => new TerritoryOptionDto(x.TerritoryId, x.Code, x.Name,
                    x.Dimension.ToString(), false)).ToArray();
        });
    }

    public IReadOnlyList<DealerCustomerOptionDto> GetCustomerOptions(Guid currentUserId,
        OrganizationSelection organization, Guid dealerId)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Customer.Assign");
        return store.Read(data =>
        {
            _ = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Customer.Assign");
            var assigned = data.DealerCustomerAssignments.Where(x => x.IsActive).Select(x => x.CustomerId).ToHashSet();
            return data.Customers.Where(x => x.CompanyId == organization.CompanyId && x.Status != CustomerStatus.Inactive &&
                    !assigned.Contains(x.Id) && snapshot.AllowsRecord(x.CompanyId, "Customer.Read", x.BranchId, x.TerritoryId))
                .OrderBy(x => x.Name).Select(x => new DealerCustomerOptionDto(x.Id, x.Code, x.Name, x.BranchId)).ToArray();
        });
    }

    public DealerTargetFormDto GetTargetForm(Guid currentUserId, OrganizationSelection organization,
        Guid dealerId, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Target.Manage");
        return store.Read(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Target.Manage");
            var target = data.DealerTargets.Where(x => x.DealerId == dealer.Id &&
                    x.PeriodFromUtc <= nowUtc && nowUtc < x.PeriodToUtc)
                .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
            if (target is not null)
                return new DealerTargetFormDto(dealer.Id, new SaveDealerTargetCommand(target.PeriodFromUtc,
                    target.PeriodToUtc, target.Amount, target.Source, target.Version));

            var (start, end) = ChannelPeriod.MonthOf(nowUtc);
            return new DealerTargetFormDto(dealer.Id, new SaveDealerTargetCommand(start, end,
                1, "برنامه فروش مصوب", 0));
        });
    }

    public DealerDetailsDto Save(Guid currentUserId, OrganizationSelection organization, Guid? id,
        SaveDealerCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequireCompanyManagement(snapshot, organization.CompanyId, "Dealer.Manage");
        return store.Write(data =>
        {
            ValidateWriteScope(data, snapshot, organization, command.BranchId, command.TerritoryId, "Dealer.Manage", nowUtc);
            if (!data.Users.Any(x => x.Id == command.ChannelManagerUserId && x.IsActiveAt(nowUtc)) ||
                !data.UserRoleAssignments.Any(x => x.CrmUserId == command.ChannelManagerUserId &&
                    x.CompanyId == organization.CompanyId &&
                    x.RoleKey.Equals("ChannelManager", StringComparison.OrdinalIgnoreCase) && x.IsEffective(nowUtc)))
                throw new InvalidOperationException("مدیر کانال انتخاب‌شده در این شرکت نقش فعال ChannelManager ندارد.");
            var duplicate = data.Dealers.Any(x => x.CompanyId == organization.CompanyId && x.Id != id &&
                (Same(x.DealerId, command.DealerId) || Same(x.Code, command.Code) ||
                 !string.IsNullOrWhiteSpace(command.NationalId) && Same(x.NationalId, command.NationalId)));
            if (duplicate) throw new InvalidOperationException("شناسه، کد یا شناسه ملی نماینده تکراری است.");
            Dealer dealer;
            if (id is null)
            {
                dealer = new Dealer(Guid.NewGuid(), command.DealerId, command.Code, command.LegalName,
                    command.TradeName, organization.CompanyId, command.BranchId, command.TerritoryId,
                    command.City, command.NationalId, command.Phone, command.Email, command.ChannelManagerUserId);
                data.Dealers.Add(dealer);
                AddHistory(data, dealer, null, DealerStatus.Draft, "ایجاد پرونده نماینده", currentUserId, nowUtc);
            }
            else
            {
                dealer = RequiredDealer(data, snapshot, organization, id.Value, "Dealer.Manage");
                EnsureVersion(dealer.Version, command.ExpectedVersion);
                dealer.Update(command.Code, command.LegalName, command.TradeName, command.BranchId,
                    command.TerritoryId, command.City, command.NationalId, command.Phone, command.Email,
                    command.ChannelManagerUserId);
            }
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto ChangeStatus(Guid currentUserId, OrganizationSelection organization, Guid id,
        ChangeDealerStatusCommand command, DateTimeOffset nowUtc)
    {
        var permission = command.TargetStatus switch
        {
            DealerStatus.PendingApproval => "Dealer.Submit",
            DealerStatus.Active => "Dealer.Approve",
            _ => "Dealer.Manage"
        };
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, permission);
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, id, permission);
            EnsureVersion(dealer.Version, command.ExpectedVersion);
            if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل تغییر وضعیت الزامی است.");
            var from = dealer.Status;
            switch (command.TargetStatus)
            {
                case DealerStatus.PendingApproval:
                    EnsureActivationPrerequisites(data, dealer, nowUtc);
                    dealer.SubmitForApproval(command.Reason);
                    break;
                case DealerStatus.Active:
                    EnsureActivationPrerequisites(data, dealer, nowUtc);
                    dealer.Activate(command.Reason, nowUtc);
                    break;
                case DealerStatus.Suspended:
                    dealer.Suspend(command.Reason);
                    break;
                case DealerStatus.Terminated:
                    dealer.Terminate(command.Reason, nowUtc);
                    foreach (var contract in data.DealerContracts.Where(x => x.DealerId == dealer.Id &&
                                 x.Status == DealerContractStatus.Active))
                        contract.End(DealerContractStatus.Terminated, command.Reason);
                    foreach (var assignment in data.DealerTerritoryAssignments.Where(x => x.DealerId == dealer.Id &&
                                 x.Status == DealerTerritoryStatus.Active))
                        assignment.End(nowUtc, command.Reason);
                    foreach (var assignment in data.DealerCustomerAssignments.Where(x => x.DealerId == dealer.Id && x.IsActive))
                        assignment.End(nowUtc, currentUserId, command.Reason);
                    break;
                default:
                    throw new InvalidOperationException("انتقال وضعیت درخواست‌شده مجاز نیست.");
            }
            AddHistory(data, dealer, from, dealer.Status, command.Reason, currentUserId, nowUtc);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto SaveContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid? contractId, SaveDealerContractCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Contract.Request");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Contract.Request");
            if (command.ValidToUtc <= command.ValidFromUtc) throw new InvalidOperationException("پایان قرارداد باید بعد از شروع باشد.");
            if (data.DealerContracts.Any(x => x.CompanyId == dealer.CompanyId && x.Id != contractId &&
                Same(x.ContractNumber, command.ContractNumber)))
                throw new InvalidOperationException("شماره قرارداد در شرکت تکراری است.");
            if (contractId is null)
                data.DealerContracts.Add(new DealerContract(Guid.NewGuid(), dealer.CompanyId, dealer.BranchId,
                    dealer.TerritoryId, dealer.Id, command.ContractNumber, command.ValidFromUtc,
                    command.ValidToUtc, command.AnnualTarget, command.PaymentTerms, currentUserId));
            else
            {
                var contract = RequiredContract(data, dealer, contractId.Value);
                EnsureVersion(contract.Version, command.ExpectedVersion);
                contract.Update(command.ContractNumber, command.ValidFromUtc, command.ValidToUtc,
                    command.AnnualTarget, command.PaymentTerms);
            }
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto SubmitContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, DecideDealerContractCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Contract.Request");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Contract.Request");
            var contract = RequiredContract(data, dealer, contractId);
            EnsureVersion(contract.Version, command.ExpectedVersion);
            contract.Submit(command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto ApproveContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, DecideDealerContractCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Contract.Approve");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Contract.Approve");
            var contract = RequiredContract(data, dealer, contractId);
            EnsureVersion(contract.Version, command.ExpectedVersion);
            contract.Approve(currentUserId, nowUtc, command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto EndContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Contract.Approve");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Contract.Approve");
            var contract = RequiredContract(data, dealer, contractId);
            EnsureVersion(contract.Version, command.ExpectedVersion);
            if (dealer.Status == DealerStatus.Active && contract.IsEffective(nowUtc) &&
                !data.DealerContracts.Any(x => x.Id != contract.Id && x.DealerId == dealer.Id && x.IsEffective(nowUtc)))
                throw new InvalidOperationException("پیش از خاتمه آخرین قرارداد، نماینده را تعلیق کنید یا قرارداد جایگزین را فعال کنید.");
            contract.End(DealerContractStatus.Terminated, command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto RequestTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        AssignDealerTerritoryCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Territory.Request");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Territory.Request");
            var territory = data.Territories.SingleOrDefault(x => x.CompanyId == dealer.CompanyId &&
                x.TerritoryId == command.TerritoryId && x.IsEffective(nowUtc)) ??
                throw new KeyNotFoundException("Territory فعال پیدا نشد.");
            if (!snapshot.Allows(dealer.CompanyId, "Dealer.Territory.Request", "Territory", territory.TerritoryId))
                throw new UnauthorizedAccessException("Territory خارج از دامنه دسترسی است.");
            if (data.DealerTerritoryAssignments.Any(x => x.DealerId == dealer.Id &&
                x.TerritoryId == territory.TerritoryId && x.Status != DealerTerritoryStatus.Ended &&
                x.Overlaps(command.ValidFromUtc, command.ValidToUtc)))
                throw new InvalidOperationException("برای این Territory یک تخصیص هم‌پوشان وجود دارد.");
            data.DealerTerritoryAssignments.Add(new DealerTerritoryAssignment(Guid.NewGuid(), dealer.CompanyId,
                dealer.BranchId, territory.TerritoryId, dealer.Id, command.IsExclusive,
                command.ValidFromUtc, command.ValidToUtc, currentUserId));
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto ApproveTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, DecideDealerTerritoryCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Territory.Approve");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Territory.Approve");
            var assignment = data.DealerTerritoryAssignments.SingleOrDefault(x => x.Id == assignmentId && x.DealerId == dealer.Id)
                ?? throw new KeyNotFoundException("تخصیص Territory پیدا نشد.");
            EnsureVersion(assignment.Version, command.ExpectedVersion);
            var conflicting = data.DealerTerritoryAssignments.Any(x => x.Id != assignment.Id &&
                x.CompanyId == assignment.CompanyId && x.TerritoryId == assignment.TerritoryId &&
                x.Status == DealerTerritoryStatus.Active && (x.IsExclusive || assignment.IsExclusive) &&
                x.Overlaps(assignment.ValidFromUtc, assignment.ValidToUtc));
            if (conflicting) throw new InvalidOperationException("تخصیص با قرارداد انحصاری فعال نماینده دیگری تعارض دارد.");
            assignment.Approve(currentUserId, nowUtc, command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto EndTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Territory.Approve");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Territory.Approve");
            var assignment = data.DealerTerritoryAssignments.SingleOrDefault(x => x.Id == assignmentId && x.DealerId == dealer.Id)
                ?? throw new KeyNotFoundException("تخصیص Territory پیدا نشد.");
            EnsureVersion(assignment.Version, command.ExpectedVersion);
            if (dealer.Status == DealerStatus.Active && assignment.IsEffective(nowUtc) &&
                !data.DealerTerritoryAssignments.Any(x => x.Id != assignment.Id && x.DealerId == dealer.Id && x.IsEffective(nowUtc)))
                throw new InvalidOperationException("پیش از پایان آخرین Territory، نماینده را تعلیق کنید یا تخصیص جایگزین را فعال کنید.");
            assignment.End(nowUtc, command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto SaveTarget(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        SaveDealerTargetCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Target.Manage");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Target.Manage");
            var target = data.DealerTargets.SingleOrDefault(x => x.DealerId == dealer.Id &&
                x.PeriodFromUtc == command.PeriodFromUtc && x.PeriodToUtc == command.PeriodToUtc);
            if (target is null)
            {
                if (command.ExpectedVersion != 0) throw new InvalidOperationException("هدف تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
                data.DealerTargets.Add(new DealerTarget(Guid.NewGuid(), dealer.CompanyId, dealer.BranchId,
                    dealer.TerritoryId, dealer.Id, command.PeriodFromUtc, command.PeriodToUtc,
                    command.Amount, command.Source, currentUserId));
            }
            else
            {
                EnsureVersion(target.Version, command.ExpectedVersion);
                target.Update(command.PeriodFromUtc, command.PeriodToUtc, command.Amount, command.Source, currentUserId);
            }
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto AssignCustomer(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        AssignDealerCustomerCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Customer.Assign");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Customer.Assign");
            var customer = data.Customers.SingleOrDefault(x => x.Id == command.CustomerId &&
                x.CompanyId == dealer.CompanyId && x.Status != CustomerStatus.Inactive &&
                snapshot.AllowsRecord(x.CompanyId, "Customer.Read", x.BranchId, x.TerritoryId)) ??
                throw new KeyNotFoundException("مشتری مجاز و فعال پیدا نشد.");
            if (data.DealerCustomerAssignments.Any(x => x.CustomerId == customer.Id && x.IsActive))
                throw new InvalidOperationException("مشتری در حال حاضر به یک نماینده فعال متصل است.");
            data.DealerCustomerAssignments.Add(new DealerCustomerAssignment(Guid.NewGuid(), dealer.CompanyId,
                customer.BranchId, customer.TerritoryId, dealer.Id, customer.Id, nowUtc, currentUserId,
                command.Reason));
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto EndCustomerAssignment(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Customer.Assign");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Customer.Assign");
            var assignment = data.DealerCustomerAssignments.SingleOrDefault(x => x.Id == assignmentId && x.DealerId == dealer.Id)
                ?? throw new KeyNotFoundException("ارتباط مشتری و نماینده پیدا نشد.");
            EnsureVersion(assignment.Version, command.ExpectedVersion);
            assignment.End(nowUtc, currentUserId, command.Reason);
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto SyncFinancial(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        long expectedVersion, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Financial.Sync");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Financial.Sync");
            EnsureVersion(dealer.Version, expectedVersion);
            var projection = financialProvider.Get(dealer.CompanyId, dealer.DealerId, nowUtc);
            data.DealerFinancialSnapshots.Add(new DealerFinancialSnapshot(Guid.NewGuid(), dealer.CompanyId,
                dealer.BranchId, dealer.TerritoryId, dealer.Id, projection.CreditLimit, projection.CreditUsed,
                projection.Balance, projection.OverdueAmount, projection.Source, projection.SynchronizedAtUtc));
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    public DealerDetailsDto SyncPerformance(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        long expectedVersion, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        RequirePermission(snapshot, organization.CompanyId, "Dealer.Performance.Sync");
        return store.Write(data =>
        {
            var dealer = RequiredDealer(data, snapshot, organization, dealerId, "Dealer.Performance.Sync");
            EnsureVersion(dealer.Version, expectedVersion);
            var projection = performanceProvider.Get(dealer.CompanyId, dealer.DealerId, nowUtc);
            data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.NewGuid(), dealer.CompanyId,
                dealer.BranchId, dealer.TerritoryId, dealer.Id, projection.PeriodFromUtc,
                projection.PeriodToUtc, projection.NetSales, projection.OrderCount, projection.Source,
                projection.SynchronizedAtUtc));
            return MapDetails(data, snapshot, dealer, nowUtc);
        });
    }

    private static DealerDetailsDto MapDetails(CrmDataSet data, AccessSnapshot snapshot, Dealer dealer, DateTimeOffset nowUtc)
    {
        var contracts = data.DealerContracts.Where(x => x.DealerId == dealer.Id).OrderByDescending(x => x.ValidFromUtc)
            .Select(x => new DealerContractDto(x.Id, x.ContractNumber, x.ValidFromUtc, x.ValidToUtc,
                x.AnnualTarget, x.PaymentTerms, x.Status, x.RequestedByUserId, UserName(data, x.RequestedByUserId),
                x.ApprovedByUserId, x.ApprovedByUserId.HasValue ? UserName(data, x.ApprovedByUserId.Value) : null,
                x.ApprovedAtUtc, x.DecisionReason, x.Version)).ToArray();
        var territories = data.DealerTerritoryAssignments.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.ValidFromUtc)
            .Select(x => new DealerTerritoryAssignmentDto(x.Id, x.TerritoryId!,
                data.Territories.SingleOrDefault(t => t.CompanyId == dealer.CompanyId && t.TerritoryId == x.TerritoryId)?.Name ?? x.TerritoryId!,
                x.IsExclusive, x.ValidFromUtc, x.ValidToUtc, x.Status, x.RequestedByUserId,
                UserName(data, x.RequestedByUserId), x.ApprovedByUserId,
                x.ApprovedByUserId.HasValue ? UserName(data, x.ApprovedByUserId.Value) : null,
                x.Reason, x.Version)).ToArray();
        var canReadFinancial = HasPermission(snapshot, dealer.CompanyId, "Dealer.Financial.Read");
        var customers = data.DealerCustomerAssignments.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.ValidFromUtc)
            .Select(x =>
            {
                var customer = data.Customers.Single(c => c.Id == x.CustomerId);
                return new DealerCustomerAssignmentDto(x.Id, customer.Id, customer.Code, customer.Name,
                    customer.BranchId, customer.TerritoryId, customer.Status, x.ValidFromUtc, x.ValidToUtc,
                    x.IsActive, canReadFinancial ? customer.Balance : null, x.EndedByUserId,
                    x.EndedByUserId.HasValue ? UserName(data, x.EndedByUserId.Value) : null, x.EndReason, x.Version);
            }).ToArray();
        var history = data.Find<Crm.Domain.Channel.DealerStatusHistory>(x => x.DealerId == dealer.Id).OrderByDescending(x => x.ChangedAtUtc)
            .Select(x => new DealerStatusHistoryDto(x.Id, x.FromStatus, x.ToStatus, x.Reason,
                x.ChangedByUserId, UserName(data, x.ChangedByUserId), x.ChangedAtUtc)).ToArray();
        var financial = canReadFinancial ? LatestFinancial(data, dealer, nowUtc) : null;
        var performance = LatestPerformance(data, dealer, nowUtc);
        var canSubmit = (dealer.Status is DealerStatus.Draft or DealerStatus.Suspended) &&
            CanAccess(snapshot, "Dealer.Submit", dealer) && HasActivationPrerequisites(data, dealer, nowUtc);
        return new DealerDetailsDto(MapSummary(data, snapshot, dealer, nowUtc), dealer.NationalId, dealer.Phone,
            dealer.Email, dealer.ChannelManagerUserId, UserName(data, dealer.ChannelManagerUserId), dealer.StatusReason,
            financial, performance, contracts, territories, customers, history,
            CanAccess(snapshot, "Dealer.Manage", dealer) && dealer.Status != DealerStatus.Terminated,
            canSubmit, dealer.Status == DealerStatus.PendingApproval && CanAccess(snapshot, "Dealer.Approve", dealer),
            dealer.Status == DealerStatus.Active && CanAccess(snapshot, "Dealer.Manage", dealer),
            dealer.Status != DealerStatus.Terminated && CanAccess(snapshot, "Dealer.Manage", dealer),
            CanAccess(snapshot, "Dealer.Contract.Request", dealer),
            CanAccess(snapshot, "Dealer.Contract.Approve", dealer),
            CanAccess(snapshot, "Dealer.Territory.Request", dealer),
            CanAccess(snapshot, "Dealer.Territory.Approve", dealer),
            CanAccess(snapshot, "Dealer.Customer.Assign", dealer),
            CanAccess(snapshot, "Dealer.Target.Manage", dealer),
            CanAccess(snapshot, "Dealer.Financial.Sync", dealer),
            CanAccess(snapshot, "Dealer.Performance.Sync", dealer));
    }

    private static DealerSummaryDto MapSummary(CrmDataSet data, AccessSnapshot snapshot, Dealer dealer, DateTimeOffset nowUtc)
    {
        var target = data.DealerTargets.Where(x => x.DealerId == dealer.Id && x.PeriodFromUtc <= nowUtc && nowUtc < x.PeriodToUtc)
            .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
        var performance = data.DealerPerformanceSnapshots.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
        var financial = data.DealerFinancialSnapshots.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
        var mayReadFinancial = HasPermission(snapshot, dealer.CompanyId, "Dealer.Financial.Read");
        var targetAmount = target?.Amount ?? 0;
        var netSales = performance?.NetSales ?? 0;
        return new DealerSummaryDto(dealer.Id, dealer.DealerId, dealer.Code, dealer.LegalName, dealer.TradeName,
            dealer.City, dealer.CompanyId, dealer.BranchId, dealer.TerritoryId,
            data.Territories.SingleOrDefault(x => x.CompanyId == dealer.CompanyId && x.TerritoryId == dealer.TerritoryId)?.Name ?? "بدون Territory",
            dealer.Status,
            data.DealerContracts.Count(x => x.DealerId == dealer.Id && x.IsEffective(nowUtc)),
            data.DealerTerritoryAssignments.Count(x => x.DealerId == dealer.Id && x.IsEffective(nowUtc)),
            data.DealerCustomerAssignments.Count(x => x.DealerId == dealer.Id && x.IsActive),
            mayReadFinancial ? financial?.Balance : null, mayReadFinancial ? financial?.OverdueAmount : null,
            targetAmount, netSales, Percent(netSales, targetAmount),
            mayReadFinancial ? financial?.SynchronizedAtUtc : null, dealer.Version);
    }

    private static DealerFinancialSnapshotDto? LatestFinancial(CrmDataSet data, Dealer dealer, DateTimeOffset nowUtc)
    {
        var value = data.DealerFinancialSnapshots.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
        return value is null ? null : new DealerFinancialSnapshotDto(value.Id, value.CreditLimit, value.CreditUsed,
            value.AvailableCredit, value.Balance, value.OverdueAmount, value.Source, value.SynchronizedAtUtc,
            Freshness(value.SynchronizedAtUtc, nowUtc));
    }

    private static DealerPerformanceSnapshotDto? LatestPerformance(CrmDataSet data, Dealer dealer, DateTimeOffset nowUtc)
    {
        var value = data.DealerPerformanceSnapshots.Where(x => x.DealerId == dealer.Id)
            .OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault();
        if (value is null) return null;
        var target = data.DealerTargets.Where(x => x.DealerId == dealer.Id &&
                x.PeriodFromUtc <= value.PeriodFromUtc && x.PeriodToUtc >= value.PeriodToUtc)
            .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault()?.Amount ?? 0;
        return new DealerPerformanceSnapshotDto(value.Id, value.PeriodFromUtc, value.PeriodToUtc, target,
            value.NetSales, value.OrderCount, Percent(value.NetSales, target), value.Source,
            value.SynchronizedAtUtc, Freshness(value.SynchronizedAtUtc, nowUtc));
    }

    private static void EnsureActivationPrerequisites(CrmDataSet data, Dealer dealer, DateTimeOffset nowUtc)
    {
        if (!HasActivationPrerequisites(data, dealer, nowUtc))
            throw new InvalidOperationException("فعال‌سازی به قرارداد فعال و حداقل یک Territory فعال و بدون تعارض نیاز دارد.");
    }

    private static bool HasActivationPrerequisites(CrmDataSet data, Dealer dealer, DateTimeOffset nowUtc) =>
        data.DealerContracts.Any(x => x.DealerId == dealer.Id && x.IsEffective(nowUtc)) &&
        data.DealerTerritoryAssignments.Any(x => x.DealerId == dealer.Id && x.IsEffective(nowUtc));

    private static void ValidateWriteScope(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization,
        string branchId, string? territoryId, string permission, DateTimeOffset nowUtc)
    {
        if (!data.OrganizationUnits.Any(x => x.CompanyId == organization.CompanyId && x.UnitId == branchId &&
            x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active))
            throw new InvalidOperationException("شعبه فعال و معتبر نیست.");
        if (!snapshot.Allows(organization.CompanyId, permission, "Branch", branchId))
            throw new UnauthorizedAccessException("شعبه خارج از دامنه مجاز است.");
        if (territoryId is not null)
        {
            if (!data.Territories.Any(x => x.CompanyId == organization.CompanyId && x.TerritoryId == territoryId && x.IsEffective(nowUtc)))
                throw new InvalidOperationException("Territory فعال و معتبر نیست.");
            if (!snapshot.Allows(organization.CompanyId, permission, "Territory", territoryId))
                throw new UnauthorizedAccessException("Territory خارج از دامنه مجاز است.");
        }
    }

    private static Dealer RequiredDealer(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization,
        Guid id, string permission) => data.Dealers.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
        throw new KeyNotFoundException("نماینده در دامنه جاری پیدا نشد.");

    private static DealerContract RequiredContract(CrmDataSet data, Dealer dealer, Guid contractId) =>
        data.DealerContracts.SingleOrDefault(x => x.Id == contractId && x.DealerId == dealer.Id) ??
        throw new KeyNotFoundException("قرارداد نماینده پیدا نشد.");

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("نشست دسترسی فعال پیدا نشد.");

    private static void RequirePermission(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!HasPermission(snapshot, companyId, permission)) throw new UnauthorizedAccessException("مجوز لازم وجود ندارد.");
    }

    private static void RequireCompanyManagement(AccessSnapshot snapshot, string companyId, string permission)
    {
        RequirePermission(snapshot, companyId, permission);
        if (!snapshot.Allows(companyId, permission, "Company", companyId))
            throw new UnauthorizedAccessException("مدیریت نمایندگان به Scope شرکت نیاز دارد.");
    }

    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, Dealer dealer) =>
        Same(dealer.CompanyId, organization.CompanyId) &&
        (organization.BranchId is null || Same(dealer.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(dealer.TerritoryId, organization.TerritoryId)) &&
        CanAccess(snapshot, permission, dealer);

    private static bool CanAccess(AccessSnapshot snapshot, string permission, Dealer dealer) =>
        snapshot.AllowsRecord(dealer.CompanyId, permission, dealer.BranchId, dealer.TerritoryId) ||
        snapshot.Allows(dealer.CompanyId, permission, "Dealer", dealer.DealerId);

    private static bool HasPermission(AccessSnapshot snapshot, string companyId, string permission) =>
        snapshot.PermissionsFor(companyId).Contains(permission);

    private static void AddHistory(CrmDataSet data, Dealer dealer, DealerStatus? from, DealerStatus to,
        string reason, Guid actorUserId, DateTimeOffset nowUtc) => data.Append<Crm.Domain.Channel.DealerStatusHistory>(
            new DealerStatusHistory(Guid.NewGuid(), dealer.CompanyId, dealer.BranchId, dealer.TerritoryId,
                dealer.Id, from, to, reason, actorUserId, nowUtc));

    private static void EnsureVersion(long current, long expected)
    {
        if (current != expected) throw new InvalidOperationException("رکورد تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }

    private static string UserName(CrmDataSet data, Guid id) =>
        data.Users.SingleOrDefault(x => x.Id == id)?.DisplayName ?? "کاربر نامشخص";
    private static decimal Percent(decimal actual, decimal target) => target <= 0 ? 0 : Math.Round(actual / target * 100m, 1);
    private static string Freshness(DateTimeOffset synchronizedAtUtc, DateTimeOffset nowUtc) =>
        nowUtc - synchronizedAtUtc <= FreshnessWindow ? "تازه" : "نیازمند همگام‌سازی";
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
