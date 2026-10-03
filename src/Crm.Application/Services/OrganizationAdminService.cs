using System.Text.Json;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public sealed class OrganizationAdminService(
    ICrmDataStore store,
    IAccessSnapshotService access) : IOrganizationAdminService
{
    public OrganizationAdminDto Get(string companyId, Guid actorUserId, DateTimeOffset nowUtc)
    {
        EnsureManage(actorUserId, companyId);
        return store.Read(data =>
        {
            var company = FindCompany(data, companyId);
            var units = data.OrganizationUnits.Where(x => Same(x.CompanyId, companyId)).OrderBy(x => x.Type).ThenBy(x => x.Name).ToList();
            var unitNames = units.ToDictionary(x => x.UnitId, x => x.Name, StringComparer.OrdinalIgnoreCase);
            return new OrganizationAdminDto(
                MapCompany(company),
                units.Select(x => new OrganizationUnitDto(x.Id, x.UnitId, x.Code, x.Name, x.Type, x.ParentUnitId,
                    x.ParentUnitId is not null && unitNames.TryGetValue(x.ParentUnitId, out var parentName) ? parentName : null,
                    x.Status, x.Version)).ToList(),
                data.Territories.Where(x => Same(x.CompanyId, companyId)).OrderBy(x => x.Dimension).ThenBy(x => x.Name)
                    .Select(x => new TerritoryDto(x.Id, x.TerritoryId, x.Code, x.Name, x.Dimension, x.ValidFromUtc,
                        x.ValidToUtc, x.Status, x.IsEffective(nowUtc), x.Version)).ToList(),
                data.Find<Crm.Domain.Organization.OrganizationChange>(x => x.CompanyId == companyId).OrderByDescending(x => x.OccurredAtUtc).Take(40)
                    .Select(x => new OrganizationChangeDto(x.Id, x.EntityType, x.EntityBusinessId, x.Action,
                        x.Summary, x.ActorUserId, x.OccurredAtUtc, x.CorrelationId, x.BeforeValue, x.AfterValue)).ToList());
        });
    }

    public UpdateCompanyCommand GetCompanyForm(string companyId, Guid actorUserId)
    {
        EnsureManage(actorUserId, companyId);
        return store.Read(data =>
        {
            var company = FindCompany(data, companyId);
            return new UpdateCompanyCommand(company.Code, company.Name, company.TimeZoneId, company.Version);
        });
    }

    public OrganizationUnitFormDto GetUnitForm(string companyId, Guid? id, OrganizationUnitType? type, Guid actorUserId)
    {
        EnsureManage(actorUserId, companyId);
        return store.Read(data =>
        {
            FindCompany(data, companyId);
            var unit = id is null ? null : data.OrganizationUnits.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("واحد سازمانی پیدا نشد.");
            var selectedType = unit?.Type ?? type ?? OrganizationUnitType.Branch;
            var parents = data.OrganizationUnits
                .Where(x => Same(x.CompanyId, companyId) && x.Status == OrganizationStatus.Active && x.Id != id && CanParent(x.Type, selectedType))
                .OrderBy(x => x.Type).ThenBy(x => x.Name)
                .Select(x => new OrganizationUnitOptionDto(x.UnitId, x.Code, x.Name, x.Type.ToString(), Same(x.UnitId, unit?.ParentUnitId)))
                .ToList();
            return new OrganizationUnitFormDto(companyId, unit?.Id, unit?.UnitId ?? string.Empty, unit?.Code ?? string.Empty,
                unit?.Name ?? string.Empty, selectedType, unit?.ParentUnitId, unit?.Version ?? 0, parents);
        });
    }

    public TerritoryFormDto GetTerritoryForm(string companyId, Guid? id, Guid actorUserId, DateTimeOffset nowUtc)
    {
        EnsureManage(actorUserId, companyId);
        return store.Read(data =>
        {
            FindCompany(data, companyId);
            var territory = id is null ? null : data.Territories.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("قلمرو پیدا نشد.");
            return new TerritoryFormDto(companyId, territory?.Id, territory?.TerritoryId ?? string.Empty,
                territory?.Code ?? string.Empty, territory?.Name ?? string.Empty,
                territory?.Dimension ?? TerritoryDimension.Geography, territory?.ValidFromUtc ?? nowUtc,
                territory?.ValidToUtc, territory?.Version ?? 0);
        });
    }

    public void UpdateCompany(string companyId, UpdateCompanyCommand command, Guid actorUserId, IdentityRequestContext context)
    {
        EnsureManage(actorUserId, companyId);
        store.Write(data =>
        {
            var company = FindCompany(data, companyId);
            EnsureVersion(company.Version, command.ExpectedVersion);
            if (data.Companies.Any(x => x.Id != company.Id && x.Code.Equals(Required(command.Code, "کد"), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("کد شرکت تکراری است.");
            var before = Snapshot(company);
            company.Update(command.Code, command.Name, command.TimeZoneId);
            AddChange(data, companyId, "Company", company.CompanyId, "Updated", "مشخصات شرکت ویرایش شد.", actorUserId, context, before, Snapshot(company));
            return true;
        });
    }

    public void SaveUnit(string companyId, Guid? id, SaveOrganizationUnitCommand command, Guid actorUserId, IdentityRequestContext context)
    {
        EnsureManage(actorUserId, companyId);
        store.Write(data =>
        {
            var company = FindActiveCompany(data, companyId);
            var unitId = Required(command.UnitId, "شناسه واحد");
            var code = Required(command.Code, "کد");
            var name = Required(command.Name, "نام");
            var parentId = Optional(command.ParentUnitId);
            var existing = id is null ? null : data.OrganizationUnits.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("واحد سازمانی پیدا نشد.");
            if (existing is not null && existing.Type != command.Type)
                throw new InvalidOperationException("نوع واحد سازمانی پس از ایجاد قابل تغییر نیست.");
            if (data.OrganizationUnits.Any(x => Same(x.CompanyId, companyId) && x.Id != id &&
                (Same(x.UnitId, unitId) || x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("شناسه یا کد واحد سازمانی تکراری است.");
            ValidateParent(data, company.CompanyId, unitId, command.Type, parentId);

            if (existing is null)
            {
                var unit = new OrganizationUnit(Guid.NewGuid(), unitId, company.CompanyId, code, name, command.Type, parentId);
                data.OrganizationUnits.Add(unit);
                AddChange(data, companyId, "OrganizationUnit", unit.UnitId, "Created", $"{Label(unit.Type)} ایجاد شد.",
                    actorUserId, context, string.Empty, Snapshot(unit));
            }
            else
            {
                EnsureVersion(existing.Version, command.ExpectedVersion);
                EnsureNoCycle(data, existing, parentId);
                var before = Snapshot(existing);
                existing.Update(code, name, parentId);
                AddChange(data, companyId, "OrganizationUnit", existing.UnitId, "Updated", $"{Label(existing.Type)} ویرایش شد.",
                    actorUserId, context, before, Snapshot(existing));
            }
            return true;
        });
    }

    public void SetUnitStatus(string companyId, Guid id, OrganizationStatus status, long expectedVersion, Guid actorUserId, IdentityRequestContext context)
    {
        EnsureManage(actorUserId, companyId);
        store.Write(data =>
        {
            var unit = data.OrganizationUnits.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("واحد سازمانی پیدا نشد.");
            EnsureVersion(unit.Version, expectedVersion);
            if (status == OrganizationStatus.Inactive)
            {
                if (data.OrganizationUnits.Any(x => Same(x.CompanyId, companyId) && Same(x.ParentUnitId, unit.UnitId) && x.Status == OrganizationStatus.Active))
                    throw new InvalidOperationException("ابتدا زیرواحدهای فعال را غیرفعال کنید.");
                if (data.UserRoleAssignments.Any(x => Same(x.CompanyId, companyId) && Same(x.ScopeId, unit.UnitId) && x.IsEffective(context.NowUtc)))
                    throw new InvalidOperationException("برای این واحد نقش فعال وجود دارد؛ ابتدا دامنه‌های دسترسی را منتقل یا خاتمه دهید.");
                if (HasActiveRecord(data, unit))
                    throw new InvalidOperationException("این واحد رکورد عملیاتی فعال دارد و بدون سیاست انتقال قابل غیرفعال‌سازی نیست.");
            }
            else
            {
                FindActiveCompany(data, companyId);
                if (unit.ParentUnitId is not null && !data.OrganizationUnits.Any(x => Same(x.CompanyId, companyId) && Same(x.UnitId, unit.ParentUnitId) && x.Status == OrganizationStatus.Active))
                    throw new InvalidOperationException("برای فعال‌سازی، والد واحد باید فعال باشد.");
            }
            var before = Snapshot(unit);
            unit.SetStatus(status);
            AddChange(data, companyId, "OrganizationUnit", unit.UnitId, "StatusChanged", $"وضعیت {Label(unit.Type)} به {status} تغییر کرد.",
                actorUserId, context, before, Snapshot(unit));
            return true;
        });
    }

    public void SaveTerritory(string companyId, Guid? id, SaveTerritoryCommand command, Guid actorUserId, IdentityRequestContext context)
    {
        EnsureManage(actorUserId, companyId);
        store.Write(data =>
        {
            var company = FindActiveCompany(data, companyId);
            var territoryId = Required(command.TerritoryId, "شناسه قلمرو");
            var code = Required(command.Code, "کد");
            var name = Required(command.Name, "نام");
            if (command.ValidToUtc is not null && command.ValidToUtc <= command.ValidFromUtc)
                throw new InvalidOperationException("پایان اعتبار باید بعد از شروع اعتبار باشد.");
            var existing = id is null ? null : data.Territories.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("قلمرو پیدا نشد.");
            if (existing is not null && existing.Dimension != command.Dimension)
                throw new InvalidOperationException("بُعد قلمرو پس از ایجاد قابل تغییر نیست.");
            if (data.Territories.Any(x => Same(x.CompanyId, companyId) && x.Id != id &&
                (Same(x.TerritoryId, territoryId) || x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("شناسه یا کد قلمرو تکراری است.");

            if (existing is null)
            {
                var territory = new Territory(Guid.NewGuid(), territoryId, company.CompanyId, code, name,
                    command.Dimension, command.ValidFromUtc, command.ValidToUtc);
                data.Territories.Add(territory);
                AddChange(data, companyId, "Territory", territory.TerritoryId, "Created", "قلمرو ایجاد شد.",
                    actorUserId, context, string.Empty, Snapshot(territory));
            }
            else
            {
                EnsureVersion(existing.Version, command.ExpectedVersion);
                var before = Snapshot(existing);
                existing.Update(code, name, command.ValidFromUtc, command.ValidToUtc);
                AddChange(data, companyId, "Territory", existing.TerritoryId, "Updated", "قلمرو ویرایش شد.",
                    actorUserId, context, before, Snapshot(existing));
            }
            return true;
        });
    }

    public void SetTerritoryStatus(string companyId, Guid id, OrganizationStatus status, long expectedVersion, Guid actorUserId, IdentityRequestContext context)
    {
        EnsureManage(actorUserId, companyId);
        store.Write(data =>
        {
            var territory = data.Territories.SingleOrDefault(x => x.Id == id && Same(x.CompanyId, companyId))
                ?? throw new KeyNotFoundException("قلمرو پیدا نشد.");
            EnsureVersion(territory.Version, expectedVersion);
            if (status == OrganizationStatus.Inactive && data.UserRoleAssignments.Any(x => Same(x.CompanyId, companyId) &&
                    x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase) && Same(x.ScopeId, territory.TerritoryId) && x.IsEffective(context.NowUtc)))
                throw new InvalidOperationException("برای این قلمرو نقش فعال وجود دارد؛ ابتدا دامنه دسترسی را منتقل یا خاتمه دهید.");
            if (status == OrganizationStatus.Active) FindActiveCompany(data, companyId);
            var before = Snapshot(territory);
            territory.SetStatus(status);
            AddChange(data, companyId, "Territory", territory.TerritoryId, "StatusChanged", $"وضعیت قلمرو به {status} تغییر کرد.",
                actorUserId, context, before, Snapshot(territory));
            return true;
        });
    }

    private void EnsureManage(Guid actorUserId, string companyId)
    {
        if (!access.HasPermission(actorUserId, Required(companyId, "شرکت"), "Administration.Manage"))
            throw new UnauthorizedAccessException("دسترسی مدیریت ساختار سازمانی در این شرکت وجود ندارد.");
    }

    private static Company FindCompany(CrmDataSet data, string companyId) => data.Companies.SingleOrDefault(x => Same(x.CompanyId, companyId))
        ?? throw new KeyNotFoundException("شرکت پیدا نشد.");

    private static Company FindActiveCompany(CrmDataSet data, string companyId)
    {
        var company = FindCompany(data, companyId);
        return company.Status == OrganizationStatus.Active ? company : throw new InvalidOperationException("شرکت غیرفعال است.");
    }

    private static void ValidateParent(CrmDataSet data, string companyId, string unitId, OrganizationUnitType type, string? parentId)
    {
        if (parentId is null)
        {
            if (type is OrganizationUnitType.Branch or OrganizationUnitType.SalesTeam)
                throw new InvalidOperationException("برای شعبه و تیم فروش انتخاب واحد والد الزامی است.");
            return;
        }
        if (Same(parentId, unitId)) throw new InvalidOperationException("واحد نمی‌تواند والد خودش باشد.");
        var parent = data.OrganizationUnits.SingleOrDefault(x => Same(x.CompanyId, companyId) && Same(x.UnitId, parentId) && x.Status == OrganizationStatus.Active)
            ?? throw new InvalidOperationException("واحد والد فعال و متعلق به همین شرکت نیست.");
        if (!CanParent(parent.Type, type)) throw new InvalidOperationException("نوع واحد والد با نوع واحد جدید سازگار نیست.");
    }

    private static bool CanParent(OrganizationUnitType parent, OrganizationUnitType child) => child switch
    {
        OrganizationUnitType.Headquarters => false,
        OrganizationUnitType.Region => parent == OrganizationUnitType.Headquarters,
        OrganizationUnitType.Branch => parent is OrganizationUnitType.Region or OrganizationUnitType.Headquarters,
        OrganizationUnitType.SalesTeam => parent == OrganizationUnitType.Branch,
        _ => false
    };

    private static void EnsureNoCycle(CrmDataSet data, OrganizationUnit unit, string? parentId)
    {
        var cursor = parentId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { unit.UnitId };
        while (cursor is not null)
        {
            if (!visited.Add(cursor)) throw new InvalidOperationException("چرخه در ساختار سازمانی مجاز نیست.");
            cursor = data.OrganizationUnits.SingleOrDefault(x => Same(x.CompanyId, unit.CompanyId) && Same(x.UnitId, cursor))?.ParentUnitId;
        }
    }

    private static bool HasActiveRecord(CrmDataSet data, OrganizationUnit unit) => unit.Type == OrganizationUnitType.Branch &&
        (data.Customers.Any(x => Same(x.CompanyId, unit.CompanyId) && Same(x.BranchId, unit.UnitId)) ||
         data.Leads.Any(x => Same(x.CompanyId, unit.CompanyId) && Same(x.BranchId, unit.UnitId)) ||
         data.Opportunities.Any(x => Same(x.CompanyId, unit.CompanyId) && Same(x.BranchId, unit.UnitId)) ||
         data.Quotes.Any(x => Same(x.CompanyId, unit.CompanyId) && Same(x.BranchId, unit.UnitId)) ||
         data.WorkItems.Any(x => Same(x.CompanyId, unit.CompanyId) && Same(x.BranchId, unit.UnitId)));

    private static void AddChange(CrmDataSet data, string companyId, string entityType, string businessId,
        string action, string summary, Guid actorUserId, IdentityRequestContext context, string before, string after) =>
        data.Append<Crm.Domain.Organization.OrganizationChange>(new OrganizationChange(Guid.NewGuid(), companyId, entityType, businessId,
            action, summary, actorUserId, context.NowUtc, context.CorrelationId, before, after));

    private static OrganizationCompanyDto MapCompany(Company x) =>
        new(x.Id, x.CompanyId, x.Code, x.Name, x.TimeZoneId, x.Status, x.Version);

    private static string Snapshot<T>(T value) => JsonSerializer.Serialize(value);
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InvalidOperationException("رکورد توسط کاربر دیگری تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static string Required(string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{label} الزامی است.") : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Label(OrganizationUnitType type) => type switch
    {
        OrganizationUnitType.Headquarters => "ستاد",
        OrganizationUnitType.Region => "منطقه",
        OrganizationUnitType.Branch => "شعبه",
        OrganizationUnitType.SalesTeam => "تیم فروش",
        _ => "واحد"
    };
}
