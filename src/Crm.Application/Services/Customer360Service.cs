using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Customers;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using System.Text.Json;

namespace Crm.Application.Services;

public sealed class Customer360Service(ICrmDataStore store, IAccessSnapshotService access) : ICustomer360Service
{
    public Customer360Dto? Get(Guid currentUserId, OrganizationSelection organization, Guid customerId, bool includeRelatedActivity = true)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data =>
        {
            var customer = data.Customers.SingleOrDefault(x => x.Id == customerId && InContext(snapshot, organization, "Customer.Read", x));
            if (customer is null) return null;
            var issues = CustomerDataQualityRules.Issues(data, customer);
            return new Customer360Dto(
                MapCustomer(customer, CustomerDataQualityRules.Score(issues), snapshot),
                data.CustomerContacts.Where(x => x.CustomerId == customer.Id && Same(x.CompanyId, customer.CompanyId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.FullName).Select(x => Map(x, snapshot)).ToList(),
                data.CustomerAddresses.Where(x => x.CustomerId == customer.Id && Same(x.CompanyId, customer.CompanyId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Title).Select(Map).ToList(),
                includeRelatedActivity
                    ? data.CustomerTimelineEvents.Where(x => x.CustomerId == customer.Id && Same(x.CompanyId, customer.CompanyId)).OrderByDescending(x => x.OccurredAtUtc).Take(50).Select(Map).ToList()
                    : [],
                data.CustomerOwnershipHistory.Where(x => x.CustomerId == customer.Id && Same(x.CompanyId, customer.CompanyId)).OrderByDescending(x => x.ValidFromUtc).Select(Map).ToList(),
                includeRelatedActivity ? data.Leads.Where(x => x.CustomerId == customer.Id &&
                    InContext(snapshot, organization, "Lead.Read", x) && CanManageSalesRecord(data, snapshot, currentUserId,
                        organization.CompanyId, x.OwnerUserId, x.Owner)).Select(x => MapLead(x, snapshot)).ToList() : [],
                includeRelatedActivity ? data.Opportunities.Where(x => x.CustomerId == customer.Id &&
                    InContext(snapshot, organization, "Opportunity.Read", x) && CanManageSalesRecord(data, snapshot, currentUserId,
                        organization.CompanyId, x.OwnerUserId, x.Owner)).Select(MapOpportunity).ToList() : [],
                includeRelatedActivity ? data.Quotes.Where(x => x.CustomerId == customer.Id &&
                    InContext(snapshot, organization, "Quote.Read", x)).Select(x => new QuoteDto(x.Id, x.Code, x.Customer, x.Opportunity, x.Amount, x.DiscountPercent, x.MarginPercent, x.CompanyId, x.BranchId, x.TerritoryId, x.Status, x.NetAmount, x.CustomerId, x.OpportunityId)).ToList() : [],
                includeRelatedActivity && snapshot.PermissionsFor(customer.CompanyId).Contains("Order.Read")
                    ? data.OrderRequests.Where(x => x.CustomerId == customer.Id &&
                        InContext(snapshot, organization, "Order.Read", x))
                        .Select(x => new OrderSummaryDto(x.Id, x.Code, x.QuoteCode, x.QuoteId, x.Customer,
                            x.CustomerId, x.CompanyId, x.BranchId, x.TerritoryId, x.CurrencyCode, x.NetAmount,
                            x.Status, x.ErpOrderNumber, x.LastSynchronizedAtUtc, x.Version)).ToList()
                    : [],
                includeRelatedActivity && snapshot.PermissionsFor(customer.CompanyId).Contains("Dealer.Read")
                    ? data.DealerCustomerAssignments.Where(x => x.CustomerId == customer.Id)
                        .Join(data.Dealers.Where(x =>
                                snapshot.AllowsRecord(x.CompanyId, "Dealer.Read", x.BranchId, x.TerritoryId) ||
                                snapshot.Allows(x.CompanyId, "Dealer.Read", "Dealer", x.DealerId)),
                            assignment => assignment.DealerId, dealer => dealer.Id,
                            (assignment, dealer) => new CustomerDealerAffiliationDto(dealer.Id, dealer.Code,
                                dealer.TradeName, assignment.ValidFromUtc, assignment.ValidToUtc, assignment.IsActive))
                        .OrderByDescending(x => x.IsActive).ToList()
                    : [],
                issues,
                Sources(customer),
                MapDuplicates(data, VisibleDuplicateCandidates(data, snapshot, organization)
                    .Where(x => x.CustomerId == customer.Id || x.PossibleDuplicateCustomerId == customer.Id)));
        });
    }

    public CustomerEditDto? GetEdit(Guid currentUserId, OrganizationSelection organization, Guid customerId)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data =>
        {
            var customer = data.Customers.SingleOrDefault(x => x.Id == customerId && InContext(snapshot, organization, "Customer.Update", x));
            if (customer is null || customer.Status == CustomerStatus.Inactive) return null;
            // The edit form posts identifiers back; a masked value would overwrite the real one, so editing requires full access.
            if (!snapshot.PermissionsFor(organization.CompanyId).Contains(FieldMasking.NationalIdPermission) ||
                !snapshot.PermissionsFor(organization.CompanyId).Contains(FieldMasking.ContactPermission))
                throw new UnauthorizedAccessException("ویرایش مشتری به مجوز مشاهدهٔ شناسه و اطلاعات تماس نیاز دارد.");
            var branches = data.OrganizationUnits.Where(x => Same(x.CompanyId, organization.CompanyId) && x.Type == OrganizationUnitType.Branch &&
                    x.Status == OrganizationStatus.Active && snapshot.AllowsRecord(organization.CompanyId, "Customer.Update", x.UnitId, organization.TerritoryId))
                .OrderBy(x => x.Name).Select(x => new OrganizationUnitOptionDto(x.UnitId, x.Code, x.Name, x.Type.ToString(), Same(x.UnitId, customer.BranchId))).ToList();
            var territories = data.Territories.Where(x => Same(x.CompanyId, organization.CompanyId) && x.Status == OrganizationStatus.Active)
                .OrderBy(x => x.Name).Select(x => new TerritoryOptionDto(x.TerritoryId, x.Code, x.Name, x.Dimension.ToString(), Same(x.TerritoryId, customer.TerritoryId))).ToList();
            return new CustomerEditDto(customer.Id, customer.Code, customer.Name, customer.City, customer.Owner,
                customer.BranchId, customer.TerritoryId, customer.Segment, customer.Kind, customer.NationalId,
                customer.PrimaryPhone, customer.PrimaryEmail, customer.Version, branches, territories);
        });
    }

    public CustomerDuplicateCheckDto CheckDuplicates(Guid currentUserId, OrganizationSelection organization,
        string name, string city, string? nationalId, string? phone, string? email, Guid? excludeCustomerId = null)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data =>
        {
            var visible = data.Customers.Where(x => InContext(snapshot, organization, "Customer.Read", x)).ToList();
            var matches = CustomerDataQualityRules.FindDuplicates(visible, organization.CompanyId, name, city, nationalId, phone, email, excludeCustomerId);
            var dtos = matches.Select(x => new DuplicateCandidateDto(Guid.Empty, Guid.Empty, string.Empty, name,
                x.Customer.Id, x.Customer.Code, x.Customer.Name, x.Score, x.Reasons, DuplicateReviewStatus.Pending,
                DateTimeOffset.UtcNow, 0)).ToList();
            var blocking = matches.Any(x => x.IsExact);
            var message = blocking ? "شناسه ملی تکراری است و ثبت مجاز نیست." : matches.Count > 0 ?
                "رکورد مشابه پیدا شد؛ برای ادامه دلیل ایجاد رکورد مستقل را ثبت کنید." : "رکورد مشابهی در دامنه مجاز پیدا نشد.";
            return new CustomerDuplicateCheckDto(blocking, dtos, message);
        });
    }

    public void Update(Guid currentUserId, OrganizationSelection organization, Guid customerId, UpdateCustomerCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!snapshot.PermissionsFor(organization.CompanyId).Contains(FieldMasking.NationalIdPermission) ||
            !snapshot.PermissionsFor(organization.CompanyId).Contains(FieldMasking.ContactPermission))
            throw new UnauthorizedAccessException("ویرایش مشتری به مجوز مشاهدهٔ شناسه و اطلاعات تماس نیاز دارد.");
        store.Write(data =>
        {
            var customer = RequiredCustomer(data, snapshot, organization, customerId, "Customer.Update");
            EnsureCustomerMutable(customer);
            EnsureVersion(customer.Version, command.ExpectedVersion);
            if (string.IsNullOrWhiteSpace(command.Name)) throw new InvalidOperationException("نام مشتری الزامی است.");
            var owner = string.IsNullOrWhiteSpace(command.Owner)
                ? throw new InvalidOperationException("مالک حساب الزامی است.")
                : command.Owner.Trim();
            var scope = ResolveWriteScope(data, snapshot, organization, "Customer.Update", command.BranchId, command.TerritoryId);
            var matches = CustomerDataQualityRules.FindDuplicates(data.Customers, organization.CompanyId, command.Name,
                command.City, command.NationalId, command.PrimaryPhone, command.PrimaryEmail, customer.Id);
            if (matches.Any(x => x.IsExact)) throw new InvalidOperationException("شناسه ملی مشتری دیگری در همین شرکت ثبت شده است.");
            var ownershipChanged = !Same(customer.BranchId, scope.BranchId) || !Same(customer.TerritoryId, scope.TerritoryId) ||
                !customer.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase);
            var before = $"{customer.Name}|{customer.BranchId}|{customer.TerritoryId}|{customer.Owner}";
            if (ownershipChanged)
            {
                if (string.IsNullOrWhiteSpace(command.OwnershipChangeReason))
                    throw new InvalidOperationException("دلیل تغییر مالکیت/دامنه الزامی است.");
                var current = data.CustomerOwnershipHistory.SingleOrDefault(x => x.CustomerId == customer.Id && x.ValidToUtc is null);
                current?.Close(nowUtc);
                data.CustomerOwnershipHistory.Add(new CustomerOwnershipHistory(Guid.NewGuid(), customer.CompanyId, customer.Id,
                    scope.BranchId, scope.TerritoryId, owner, nowUtc, command.OwnershipChangeReason, currentUserId));
            }
            customer.UpdateMasterData(command.Name, command.City, owner, scope.BranchId, scope.BranchName,
                scope.TerritoryId, command.Segment, command.NationalId, command.PrimaryPhone, command.PrimaryEmail);
            var after = $"{customer.Name}|{customer.BranchId}|{customer.TerritoryId}|{customer.Owner}";
            AddTimeline(data, customer, ownershipChanged ? CustomerTimelineType.OwnershipChanged : CustomerTimelineType.MasterDataChanged,
                ownershipChanged ? "مالکیت مشتری تغییر کرد" : "اطلاعات پایه مشتری ویرایش شد", $"{before} → {after}", nowUtc, currentUserId);
            AddDuplicateCases(data, customer, matches, nowUtc);
            return true;
        });
    }

    public void AddContact(Guid currentUserId, OrganizationSelection organization, Guid customerId, AddCustomerContactCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        store.Write(data =>
        {
            var customer = RequiredCustomer(data, snapshot, organization, customerId, "Customer.Update");
            EnsureCustomerMutable(customer);
            if (string.IsNullOrWhiteSpace(command.FullName)) throw new InvalidOperationException("نام شخص تماس الزامی است.");
            if (string.IsNullOrWhiteSpace(command.Phone) && string.IsNullOrWhiteSpace(command.Email))
                throw new InvalidOperationException("حداقل تلفن یا ایمیل شخص تماس الزامی است.");
            if (command.IsPrimary)
                foreach (var existing in data.CustomerContacts.Where(x => x.CustomerId == customer.Id && x.IsPrimary && x.IsActive))
                    existing.Update(existing.FullName, existing.Role, existing.Phone, existing.Email, false, existing.ConsentStatus);
            var contact = new CustomerContact(Guid.NewGuid(), customer.CompanyId, customer.Id, command.FullName,
                command.Role, command.Phone, command.Email, command.IsPrimary, command.ConsentStatus);
            data.CustomerContacts.Add(contact);
            AddTimeline(data, customer, CustomerTimelineType.ContactAdded, "شخص تماس افزوده شد", contact.FullName, nowUtc, currentUserId);
            return true;
        });
    }

    public void AddAddress(Guid currentUserId, OrganizationSelection organization, Guid customerId, AddCustomerAddressCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        store.Write(data =>
        {
            var customer = RequiredCustomer(data, snapshot, organization, customerId, "Customer.Update");
            EnsureCustomerMutable(customer);
            if (string.IsNullOrWhiteSpace(command.Title) || string.IsNullOrWhiteSpace(command.AddressLine))
                throw new InvalidOperationException("عنوان و متن آدرس الزامی است.");
            if (command.IsPrimary)
                foreach (var existing in data.CustomerAddresses.Where(x => x.CustomerId == customer.Id && x.IsPrimary && x.IsActive))
                    existing.Update(existing.Title, existing.Province, existing.City, existing.AddressLine, existing.PostalCode, false);
            var address = new CustomerAddress(Guid.NewGuid(), customer.CompanyId, customer.Id, command.Type,
                command.Title, command.Province, command.City, command.AddressLine, command.PostalCode, command.IsPrimary);
            data.CustomerAddresses.Add(address);
            AddTimeline(data, customer, CustomerTimelineType.AddressAdded, "آدرس افزوده شد", address.Title, nowUtc, currentUserId);
            return true;
        });
    }

    public IReadOnlyList<DuplicateCandidateDto> GetDuplicateReviewQueue(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!snapshot.HasCompany(organization.CompanyId) || !snapshot.PermissionsFor(organization.CompanyId).Contains("Customer.MergeReview"))
            throw new UnauthorizedAccessException("Duplicate review permission is required.");
        return store.Read(data =>
        {
            return MapDuplicates(data, VisibleDuplicateCandidates(data, snapshot, organization));
        });
    }

    public void ReviewDuplicate(Guid currentUserId, OrganizationSelection organization, Guid candidateId, ReviewDuplicateCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!snapshot.PermissionsFor(organization.CompanyId).Contains("Customer.MergeReview"))
            throw new UnauthorizedAccessException("Duplicate review permission is required.");
        store.Write(data =>
        {
            var candidate = data.CustomerDuplicateCandidates.SingleOrDefault(x => x.Id == candidateId && Same(x.CompanyId, organization.CompanyId))
                ?? throw new KeyNotFoundException("پرونده تطبیق پیدا نشد.");
            EnsureVersion(candidate.Version, command.ExpectedVersion);
            var first = RequiredCustomer(data, snapshot, organization, candidate.CustomerId, "Customer.Read");
            var second = RequiredCustomer(data, snapshot, organization, candidate.PossibleDuplicateCustomerId, "Customer.Read");
            candidate.Review(command.Decision, currentUserId, nowUtc, command.Note);
            AddTimeline(data, first, CustomerTimelineType.DuplicateReview, "بررسی رکورد مشابه", $"{command.Decision}: {second.Code}", nowUtc, currentUserId);
            AddTimeline(data, second, CustomerTimelineType.DuplicateReview, "بررسی رکورد مشابه", $"{command.Decision}: {first.Code}", nowUtc, currentUserId);
            return true;
        });
    }

    public CustomerMergePreviewDto GetMergePreview(Guid currentUserId, OrganizationSelection organization, Guid candidateId, Guid survivorCustomerId)
    {
        var snapshot = RequiredMergeAccess(currentUserId, organization);
        return store.Read(data =>
        {
            var (candidate, survivor, merged) = ResolveMerge(data, snapshot, organization, candidateId, survivorCustomerId);
            ValidateMergeState(data, candidate, survivor, merged);
            EnsureRelationshipCompanyConsistency(data, survivor);
            EnsureRelationshipCompanyConsistency(data, merged);
            var warnings = new List<string> { "رکورد ادغام‌شونده غیرفعال می‌شود و هیچ داده‌ای حذف نخواهد شد." };
            var visits=data.MobileVisits.Count(x=>x.CustomerId==merged.Id);var portal=data.PortalRequests.Count(x=>x.CustomerId==merged.Id);
            if(visits+portal>0)warnings.Add($"{visits} بازدید و {portal} درخواست پرتال به مشتری اصلی متصل می‌شود؛ نسخهٔ آن‌ها تغییر می‌کند و صف آفلاین قدیمی نیازمند بررسی خواهد بود.");
            if (survivor.BranchId != merged.BranchId) warnings.Add("دو رکورد در شعب متفاوت‌اند؛ دامنه رکورد اصلی حفظ می‌شود.");
            if (data.CustomerContacts.Any(x => x.CustomerId == survivor.Id && x.IsPrimary && x.IsActive) &&
                data.CustomerContacts.Any(x => x.CustomerId == merged.Id && x.IsPrimary && x.IsActive))
                warnings.Add("تماس اصلی رکورد ادغام‌شونده به تماس عادی تبدیل می‌شود و در Unmerge بازگردانی خواهد شد.");
            if (data.CustomerAddresses.Any(x => x.CustomerId == survivor.Id && x.IsPrimary && x.IsActive) &&
                data.CustomerAddresses.Any(x => x.CustomerId == merged.Id && x.IsPrimary && x.IsActive))
                warnings.Add("آدرس اصلی رکورد ادغام‌شونده به آدرس عادی تبدیل می‌شود و در Unmerge بازگردانی خواهد شد.");
            return new CustomerMergePreviewDto(candidate.Id, MapCustomer(survivor, 0, snapshot), MapCustomer(merged, 0, snapshot),
                data.CustomerContacts.Count(x => x.CustomerId == merged.Id),
                data.CustomerAddresses.Count(x => x.CustomerId == merged.Id),
                data.Leads.Count(x => x.CustomerId == merged.Id),
                data.Opportunities.Count(x => x.CustomerId == merged.Id),
                data.Quotes.Count(x => x.CustomerId == merged.Id),
                data.OrderRequests.Count(x => x.CustomerId == merged.Id),
                data.DealerCustomerAssignments.Count(x => x.CustomerId == merged.Id), warnings,
                candidate.Version, survivor.Version, merged.Version);
        });
    }

    public CustomerMergeOperationDto Merge(Guid currentUserId, OrganizationSelection organization, Guid candidateId,
        MergeCustomerCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredMergeAccess(currentUserId, organization);
        return store.Write(data =>
        {
            var (candidate, survivor, merged) = ResolveMerge(data, snapshot, organization, candidateId, command.SurvivorCustomerId);
            EnsureVersion(candidate.Version, command.ExpectedCandidateVersion);
            EnsureVersion(survivor.Version, command.ExpectedSurvivorVersion);
            EnsureVersion(merged.Version, command.ExpectedMergedVersion);
            ValidateMergeState(data, candidate, survivor, merged);
            EnsureRelationshipCompanyConsistency(data, survivor);
            EnsureRelationshipCompanyConsistency(data, merged);
            if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل ادغام الزامی است.");

            var manifest = new MergeManifest
            {
                Contacts = data.CustomerContacts.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                Addresses = data.CustomerAddresses.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                Leads = data.Leads.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                Opportunities = data.Opportunities.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                Quotes = data.Quotes.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                Orders = data.OrderRequests.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                DealerAssignments = data.DealerCustomerAssignments.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                MobileVisits = data.MobileVisits.Where(x=>x.CustomerId==merged.Id).Select(x=>x.Id).ToArray(),
                PortalRequests = data.PortalRequests.Where(x=>x.CustomerId==merged.Id).Select(x=>x.Id).ToArray(),
                ServiceCases = data.ServiceCases.Where(x => x.CustomerId == merged.Id).Select(x => x.Id).ToArray(),
                PrimaryContacts = data.CustomerContacts.Where(x => x.CustomerId == merged.Id && x.IsPrimary).Select(x => x.Id).ToArray(),
                PrimaryAddresses = data.CustomerAddresses.Where(x => x.CustomerId == merged.Id && x.IsPrimary).Select(x => x.Id).ToArray()
            };
            if (data.CustomerContacts.Any(x => x.CustomerId == survivor.Id && x.IsPrimary && x.IsActive))
                foreach (var item in data.CustomerContacts.Where(x => manifest.PrimaryContacts.Contains(x.Id))) item.SetPrimary(false);
            if (data.CustomerAddresses.Any(x => x.CustomerId == survivor.Id && x.IsPrimary && x.IsActive))
                foreach (var item in data.CustomerAddresses.Where(x => manifest.PrimaryAddresses.Contains(x.Id))) item.SetPrimary(false);
            foreach (var item in data.CustomerContacts.Where(x => manifest.Contacts.Contains(x.Id))) item.ReassignCustomer(survivor.Id);
            foreach (var item in data.CustomerAddresses.Where(x => manifest.Addresses.Contains(x.Id))) item.ReassignCustomer(survivor.Id);
            foreach (var item in data.Leads.Where(x => manifest.Leads.Contains(x.Id))) item.ReassignCustomer(survivor.Id);
            foreach (var item in data.Opportunities.Where(x => manifest.Opportunities.Contains(x.Id))) item.ReassignCustomer(survivor.Id, survivor.Name);
            foreach (var item in data.Quotes.Where(x => manifest.Quotes.Contains(x.Id))) item.ReassignCustomer(survivor.Id, survivor.Name);
            foreach (var item in data.OrderRequests.Where(x => manifest.Orders.Contains(x.Id))) item.ReassignCustomer(survivor.Id, survivor.Name);
            foreach (var item in data.DealerCustomerAssignments.Where(x => manifest.DealerAssignments.Contains(x.Id))) item.ReassignCustomer(survivor.Id);
            foreach(var item in data.MobileVisits.Where(x=>manifest.MobileVisits.Contains(x.Id)))item.ReassignCustomer(survivor.Id);
            foreach(var item in data.PortalRequests.Where(x=>manifest.PortalRequests.Contains(x.Id)))item.ReassignCustomer(survivor.Id);
            foreach (var item in data.ServiceCases.Where(x => manifest.ServiceCases.Contains(x.Id))) item.ReassignCustomer(survivor.Id);
            var previousStatus = merged.Status;
            merged.MergeInto();
            var operation = new CustomerMergeOperation(Guid.NewGuid(), merged.CompanyId, candidate.Id, survivor.Id,
                merged.Id, previousStatus, JsonSerializer.Serialize(manifest), command.Reason, currentUserId, nowUtc);
            data.CustomerMergeOperations.Add(operation);
            AddTimeline(data, survivor, CustomerTimelineType.Merge, "ادغام مشتری", $"{merged.Code} → {survivor.Code}", nowUtc, currentUserId);
            AddTimeline(data, merged, CustomerTimelineType.Merge, "ادغام در مشتری اصلی", survivor.Code, nowUtc, currentUserId);
            return MapMerge(data, operation);
        });
    }

    public IReadOnlyList<CustomerMergeOperationDto> GetMergeHistory(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredMergeAccess(currentUserId, organization);
        return store.Read(data => data.CustomerMergeOperations.Where(x => Same(x.CompanyId, organization.CompanyId))
            .Where(x => data.Customers.Any(c => c.Id == x.SurvivorCustomerId && InContext(snapshot, organization, "Customer.Read", c)) &&
                        data.Customers.Any(c => c.Id == x.MergedCustomerId && InContext(snapshot, organization, "Customer.Read", c)))
            .OrderByDescending(x => x.MergedAtUtc).Select(x => MapMerge(data, x)).ToList());
    }

    public void Unmerge(Guid currentUserId, OrganizationSelection organization, Guid operationId,
        UnmergeCustomerCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredMergeAccess(currentUserId, organization);
        store.Write(data =>
        {
            var operation = data.CustomerMergeOperations.SingleOrDefault(x => x.Id == operationId && Same(x.CompanyId, organization.CompanyId))
                ?? throw new KeyNotFoundException("سابقه ادغام پیدا نشد.");
            EnsureVersion(operation.Version, command.ExpectedVersion);
            if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل بازگردانی الزامی است.");
            var survivor = RequiredCustomer(data, snapshot, organization, operation.SurvivorCustomerId, "Customer.Read");
            var merged = RequiredCustomer(data, snapshot, organization, operation.MergedCustomerId, "Customer.Read");
            if (operation.Status != CustomerMergeStatus.Merged)
                throw new InvalidOperationException("این عملیات ادغام قبلاً بازگردانی شده است.");
            if (merged.Status != CustomerStatus.Inactive)
                throw new InvalidOperationException("وضعیت مشتری ادغام‌شده تغییر کرده است؛ بازگردانی خودکار ایمن نیست.");
            if (data.CustomerMergeOperations.Any(x => x.Id != operation.Id && x.Status == CustomerMergeStatus.Merged &&
                x.MergedCustomerId == survivor.Id))
                throw new InvalidOperationException("مشتری اصلی در یک ادغام بعدی مغلوب شده است؛ ابتدا عملیات جدیدتر را بازگردانی کنید.");
            EnsureRelationshipCompanyConsistency(data, survivor);
            EnsureRelationshipCompanyConsistency(data, merged);
            var manifest = JsonSerializer.Deserialize<MergeManifest>(operation.TransferManifestJson)
                ?? throw new InvalidOperationException("فهرست انتقال ادغام معتبر نیست.");
            if (!manifest.PrimaryContacts.ToHashSet().IsSubsetOf(manifest.Contacts) ||
                !manifest.PrimaryAddresses.ToHashSet().IsSubsetOf(manifest.Addresses))
                throw new InvalidOperationException("فهرست Primary در Manifest با روابط منتقل‌شده سازگار نیست.");
            EnsureManifestRelations(manifest.Contacts, data.CustomerContacts.Where(x => manifest.Contacts.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "Contact");
            EnsureManifestRelations(manifest.Addresses, data.CustomerAddresses.Where(x => manifest.Addresses.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "Address");
            EnsureManifestRelations(manifest.Leads, data.Leads.Where(x => manifest.Leads.Contains(x.Id)).Select(x => (x.Id, x.CustomerId)), survivor.Id, "Lead");
            EnsureManifestRelations(manifest.Opportunities, data.Opportunities.Where(x => manifest.Opportunities.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "Opportunity");
            EnsureManifestRelations(manifest.Quotes, data.Quotes.Where(x => manifest.Quotes.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "Quote");
            EnsureManifestRelations(manifest.Orders, data.OrderRequests.Where(x => manifest.Orders.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "Order");
            EnsureManifestRelations(manifest.DealerAssignments, data.DealerCustomerAssignments.Where(x => manifest.DealerAssignments.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "DealerAssignment");
            EnsureManifestRelations(manifest.MobileVisits,data.MobileVisits.Where(x=>manifest.MobileVisits.Contains(x.Id)).Select(x=>(x.Id,(Guid?)x.CustomerId)),survivor.Id,"MobileVisit");
            EnsureManifestRelations(manifest.PortalRequests,data.PortalRequests.Where(x=>manifest.PortalRequests.Contains(x.Id)).Select(x=>(x.Id,x.CustomerId)),survivor.Id,"PortalRequest");
            EnsureManifestRelations(manifest.ServiceCases, data.ServiceCases.Where(x => manifest.ServiceCases.Contains(x.Id)).Select(x => (x.Id, (Guid?)x.CustomerId)), survivor.Id, "ServiceCase");
            foreach (var item in data.CustomerContacts.Where(x => manifest.Contacts.Contains(x.Id))) item.ReassignCustomer(merged.Id);
            foreach (var item in data.CustomerAddresses.Where(x => manifest.Addresses.Contains(x.Id))) item.ReassignCustomer(merged.Id);
            foreach (var item in data.Leads.Where(x => manifest.Leads.Contains(x.Id))) item.ReassignCustomer(merged.Id);
            foreach (var item in data.Opportunities.Where(x => manifest.Opportunities.Contains(x.Id))) item.ReassignCustomer(merged.Id, merged.Name);
            foreach (var item in data.Quotes.Where(x => manifest.Quotes.Contains(x.Id))) item.ReassignCustomer(merged.Id, merged.Name);
            foreach (var item in data.OrderRequests.Where(x => manifest.Orders.Contains(x.Id))) item.ReassignCustomer(merged.Id, merged.Name);
            foreach (var item in data.DealerCustomerAssignments.Where(x => manifest.DealerAssignments.Contains(x.Id))) item.ReassignCustomer(merged.Id);
            foreach(var item in data.MobileVisits.Where(x=>manifest.MobileVisits.Contains(x.Id)))item.ReassignCustomer(merged.Id);
            foreach(var item in data.PortalRequests.Where(x=>manifest.PortalRequests.Contains(x.Id)))item.ReassignCustomer(merged.Id);
            foreach (var item in data.ServiceCases.Where(x => manifest.ServiceCases.Contains(x.Id))) item.ReassignCustomer(merged.Id);
            foreach (var item in data.CustomerContacts.Where(x => manifest.PrimaryContacts.Contains(x.Id))) item.SetPrimary(true);
            foreach (var item in data.CustomerAddresses.Where(x => manifest.PrimaryAddresses.Contains(x.Id))) item.SetPrimary(true);
            merged.RestoreAfterMerge(operation.MergedCustomerPreviousStatus);
            operation.Revert(currentUserId, nowUtc, command.Reason);
            AddTimeline(data, survivor, CustomerTimelineType.Unmerge, "بازگردانی ادغام", merged.Code, nowUtc, currentUserId);
            AddTimeline(data, merged, CustomerTimelineType.Unmerge, "مشتری بازگردانی شد", survivor.Code, nowUtc, currentUserId);
            return true;
        });
    }

    public IReadOnlyList<CustomerDataQualityRowDto> GetDataQuality(Guid currentUserId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Read(data => data.Customers.Where(x => InContext(snapshot, organization, "Customer.Read", x))
            .Select(customer => new CustomerDataQualityRowDto(MapCustomer(customer,
                CustomerDataQualityRules.Score(CustomerDataQualityRules.Issues(data, customer)), snapshot), CustomerDataQualityRules.Issues(data, customer)))
            .OrderBy(x => x.Customer.DataQualityScore).ToList());
    }

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");
    private static Customer RequiredCustomer(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid id, string permission) =>
        data.Customers.SingleOrDefault(x => x.Id == id && InContext(snapshot, organization, permission, x)) ??
        throw new KeyNotFoundException("مشتری در دامنه جاری پیدا نشد.");
    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        Same(entity.CompanyId, organization.CompanyId) && (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);

    private static (string BranchId, string BranchName, string? TerritoryId) ResolveWriteScope(CrmDataSet data, AccessSnapshot snapshot,
        OrganizationSelection organization, string permission, string branchId, string? territoryId)
    {
        var branch = data.OrganizationUnits.SingleOrDefault(x => Same(x.CompanyId, organization.CompanyId) && Same(x.UnitId, branchId) &&
            x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active) ??
            throw new UnauthorizedAccessException("شعبه انتخاب‌شده در شرکت جاری فعال نیست.");
        var resolvedTerritory = string.IsNullOrWhiteSpace(territoryId) ? null : territoryId.Trim();
        if (resolvedTerritory is not null && !data.Territories.Any(x => Same(x.CompanyId, organization.CompanyId) && Same(x.TerritoryId, resolvedTerritory) && x.Status == OrganizationStatus.Active))
            throw new UnauthorizedAccessException("قلمرو انتخاب‌شده در شرکت جاری فعال نیست.");
        if (!snapshot.AllowsRecord(organization.CompanyId, permission, branch.UnitId, resolvedTerritory))
            throw new UnauthorizedAccessException("دامنه انتخاب‌شده خارج از مجوز کاربر است.");
        return (branch.UnitId, branch.Name, resolvedTerritory);
    }

    private static void AddDuplicateCases(CrmDataSet data, Customer customer, IEnumerable<CustomerDuplicateMatch> matches, DateTimeOffset nowUtc)
    {
        foreach (var match in matches.Where(x => !x.IsExact))
        {
            var exists = data.CustomerDuplicateCandidates.Any(x => Same(x.CompanyId, customer.CompanyId) &&
                ((x.CustomerId == customer.Id && x.PossibleDuplicateCustomerId == match.Customer.Id) ||
                 (x.CustomerId == match.Customer.Id && x.PossibleDuplicateCustomerId == customer.Id)) && x.Status == DuplicateReviewStatus.Pending);
            if (!exists) data.CustomerDuplicateCandidates.Add(new CustomerDuplicateCandidate(Guid.NewGuid(), customer.CompanyId,
                customer.Id, match.Customer.Id, match.Score, match.Reasons, nowUtc));
        }
    }

    private static void AddTimeline(CrmDataSet data, Customer customer, CustomerTimelineType type, string title,
        string description, DateTimeOffset nowUtc, Guid actorUserId) => data.CustomerTimelineEvents.Add(
        new CustomerTimelineEvent(Guid.NewGuid(), customer.CompanyId, customer.Id, type, title, description,
            nowUtc, "CRM", null, actorUserId));

    private static IReadOnlyList<DuplicateCandidateDto> MapDuplicates(CrmDataSet data, IEnumerable<CustomerDuplicateCandidate> candidates) =>
        candidates.OrderByDescending(x => x.DetectedAtUtc).Select(x =>
        {
            var first = data.Customers.Single(customer => customer.Id == x.CustomerId);
            var second = data.Customers.Single(customer => customer.Id == x.PossibleDuplicateCustomerId);
            var activeMerge = data.CustomerMergeOperations.FirstOrDefault(operation =>
                operation.DuplicateCandidateId == x.Id && operation.Status == CustomerMergeStatus.Merged);
            return new DuplicateCandidateDto(x.Id, first.Id, first.Code, first.Name, second.Id, second.Code, second.Name,
                x.Score, x.Reasons, x.Status, x.DetectedAtUtc, x.Version, first.Version, second.Version,
                activeMerge is not null, activeMerge?.Id);
        }).ToList();

    private static IEnumerable<CustomerDuplicateCandidate> VisibleDuplicateCandidates(CrmDataSet data, AccessSnapshot snapshot,
        OrganizationSelection organization)
    {
        var visibleIds = data.Customers.Where(x => InContext(snapshot, organization, "Customer.Read", x))
            .Select(x => x.Id).ToHashSet();
        return data.CustomerDuplicateCandidates.Where(x => Same(x.CompanyId, organization.CompanyId) &&
            visibleIds.Contains(x.CustomerId) && visibleIds.Contains(x.PossibleDuplicateCustomerId));
    }

    private static IReadOnlyList<CustomerSourceDto> Sources(Customer customer) =>
    [
        new("اطلاعات پایه", customer.DataSource, customer.UpdatedAtUtc, "به‌روز"),
        new("مانده و اعتبار", "ERP Projection", customer.LastSynchronizedAtUtc,
            customer.LastSynchronizedAtUtc is null ? "نامشخص" : customer.LastSynchronizedAtUtc < DateTimeOffset.UtcNow.AddDays(-2) ? "قدیمی" : "به‌روز")
    ];

    private static CustomerDto MapCustomer(Customer x, int qualityScore, AccessSnapshot snapshot) => new CustomerDto(x.Id, x.Code, x.Name, x.City, x.Owner,
        x.CompanyId, x.Branch, x.BranchId, x.TerritoryId, x.Segment, x.Status, x.Balance, x.CreditLimit,
        x.Kind, x.NationalId, x.PrimaryPhone, x.PrimaryEmail, x.DataSource, x.LastSynchronizedAtUtc, x.Version, qualityScore).Mask(snapshot);
    private static CustomerContactDto Map(CustomerContact x, AccessSnapshot snapshot) =>
        new CustomerContactDto(x.Id, x.FullName, x.Role, x.Phone, x.Email, x.IsPrimary, x.ConsentStatus, x.IsActive, x.Version).Mask(snapshot, x.CompanyId);
    private static CustomerAddressDto Map(CustomerAddress x) => new(x.Id, x.Type, x.Title, x.Province, x.City, x.AddressLine, x.PostalCode, x.IsPrimary, x.IsActive, x.Version);
    private static CustomerTimelineDto Map(CustomerTimelineEvent x) => new(x.Id, x.Type, x.Title, x.Description, x.OccurredAtUtc, x.Source, x.SourceReference, x.ActorUserId);
    private static CustomerOwnershipDto Map(CustomerOwnershipHistory x) => new(x.Id, x.BranchId, x.TerritoryId, x.Owner, x.ValidFromUtc, x.ValidToUtc, x.Reason, x.ChangedByUserId);
    private static LeadDto MapLead(Lead x, AccessSnapshot snapshot) => new LeadDto(x.Id, x.Code, x.Name, x.Contact, x.Source, x.Owner,
        x.CompanyId, x.BranchId, x.TerritoryId, x.Score, x.Status, x.CustomerId, x.OwnerUserId, x.Phone,
        x.Email, x.AssignedAtUtc, x.FirstContactDueAtUtc, x.FirstContactAtUtc, x.LastActivityAtUtc,
        x.NextAction, x.NextActionAtUtc, x.StatusReason, x.ConvertedOpportunityId, x.Version).Mask(snapshot);
    private static OpportunityDto MapOpportunity(Opportunity x) => new(x.Id, x.Code, x.Title, x.Customer,
        x.Value, x.Owner, x.CompanyId, x.BranchId, x.TerritoryId, x.Stage, x.Probability, x.CustomerId,
        x.OwnerUserId, x.OriginLeadId, x.ExpectedCloseAtUtc, x.Source, x.NextAction, x.NextActionAtUtc,
        x.LastActivityAtUtc, x.Competitor, x.RiskLevel, x.OutcomeReason, x.ClosedAtUtc, x.Version);
    private static bool CanManageSalesRecord(CrmDataSet data, AccessSnapshot snapshot, Guid userId,
        string companyId, Guid? ownerUserId, string ownerName)
    {
        if (snapshot.ScopeGrants.Any(x => Same(x.CompanyId, companyId) &&
            (x.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) ||
             x.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase)))) return true;
        if (ownerUserId.HasValue) return ownerUserId.Value == userId;
        return data.Users.SingleOrDefault(x => x.Id == userId)?.DisplayName.Equals(ownerName, StringComparison.OrdinalIgnoreCase) == true;
    }
    private static void EnsureVersion(long actual, long expected)
    {
        if (actual != expected) throw new InvalidOperationException("رکورد مشتری تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
    }
    private static void EnsureCustomerMutable(Customer customer)
    {
        if (customer.Status == CustomerStatus.Inactive)
            throw new InvalidOperationException("مشتری غیرفعال قابل ویرایش نیست؛ ابتدا در صورت مجاز بودن Merge را بازگردانی کنید.");
    }
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private AccessSnapshot RequiredMergeAccess(Guid userId, OrganizationSelection organization)
    {
        var snapshot = RequiredSnapshot(userId);
        if (!snapshot.PermissionsFor(organization.CompanyId).Contains("Customer.MergeReview"))
            throw new UnauthorizedAccessException("Merge permission is required.");
        return snapshot;
    }

    private static (CustomerDuplicateCandidate Candidate, Customer Survivor, Customer Merged) ResolveMerge(
        CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid candidateId, Guid survivorCustomerId)
    {
        var candidate = data.CustomerDuplicateCandidates.SingleOrDefault(x => x.Id == candidateId && Same(x.CompanyId, organization.CompanyId))
            ?? throw new KeyNotFoundException("پرونده تطبیق پیدا نشد.");
        if (survivorCustomerId != candidate.CustomerId && survivorCustomerId != candidate.PossibleDuplicateCustomerId)
            throw new InvalidOperationException("مشتری اصلی باید یکی از دو رکورد پرونده باشد.");
        var mergedId = survivorCustomerId == candidate.CustomerId ? candidate.PossibleDuplicateCustomerId : candidate.CustomerId;
        return (candidate,
            RequiredCustomer(data, snapshot, organization, survivorCustomerId, "Customer.Read"),
            RequiredCustomer(data, snapshot, organization, mergedId, "Customer.Read"));
    }

    private static CustomerMergeOperationDto MapMerge(CrmDataSet data, CustomerMergeOperation operation)
    {
        var survivor = data.Customers.Single(x => x.Id == operation.SurvivorCustomerId);
        var merged = data.Customers.Single(x => x.Id == operation.MergedCustomerId);
        return new CustomerMergeOperationDto(operation.Id, survivor.Id, survivor.Code, survivor.Name, merged.Id,
            merged.Code, merged.Name, operation.Status, operation.Reason, operation.MergedAtUtc,
            operation.RevertedAtUtc, operation.Version);
    }

    private static void ValidateMergeState(CrmDataSet data, CustomerDuplicateCandidate candidate, Customer survivor, Customer merged)
    {
        if (candidate.Status != DuplicateReviewStatus.Confirmed)
            throw new InvalidOperationException("ابتدا شباهت دو رکورد را تأیید کنید.");
        if (survivor.Status == CustomerStatus.Inactive || merged.Status == CustomerStatus.Inactive)
            throw new InvalidOperationException("مشتری اصلی و مشتری ادغام‌شونده باید فعال یا در حال بررسی باشند.");
        if (data.CustomerMergeOperations.Any(x => x.DuplicateCandidateId == candidate.Id && x.Status == CustomerMergeStatus.Merged))
            throw new InvalidOperationException("این پرونده قبلاً ادغام شده است.");
        if (data.CustomerMergeOperations.Any(x => x.Status == CustomerMergeStatus.Merged && x.MergedCustomerId == survivor.Id))
            throw new InvalidOperationException("رکورد انتخاب‌شده قبلاً در مشتری دیگری ادغام شده و نمی‌تواند Survivor باشد.");
        if (data.CustomerMergeOperations.Any(x => x.Status == CustomerMergeStatus.Merged &&
            (x.MergedCustomerId == merged.Id || x.SurvivorCustomerId == merged.Id)))
            throw new InvalidOperationException("رکورد ادغام‌شونده در یک Merge فعال دیگر مشارکت دارد.");
    }

    private static void EnsureRelationshipCompanyConsistency(CrmDataSet data, Customer customer)
    {
        var invalid = data.CustomerContacts.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.MobileVisits.Any(x=>x.CustomerId==customer.Id && !Same(x.CompanyId,customer.CompanyId)) ||
            data.PortalRequests.Any(x=>x.CustomerId==customer.Id && !Same(x.CompanyId,customer.CompanyId)) ||
            data.ServiceCases.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.CustomerAddresses.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.Leads.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.Opportunities.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.Quotes.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.OrderRequests.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId)) ||
            data.DealerCustomerAssignments.Any(x => x.CustomerId == customer.Id && !Same(x.CompanyId, customer.CompanyId));
        if (invalid) throw new InvalidOperationException("یک رابطه با Company مشتری سازگار نیست؛ ابتدا کیفیت داده را اصلاح کنید.");
    }

    private static void EnsureManifestRelations(Guid[] expectedIds, IEnumerable<(Guid Id, Guid? CustomerId)> actualRelations,
        Guid survivorId, string relationName)
    {
        var actual = actualRelations.ToList();
        if (actual.Count != expectedIds.Distinct().Count() || actual.Select(x => x.Id).ToHashSet().SetEquals(expectedIds) is false)
            throw new InvalidOperationException($"یک رابطه {relationName} ثبت‌شده در Manifest حذف یا جایگزین شده است؛ بازگردانی خودکار ایمن نیست.");
        if (actual.Any(x => x.CustomerId != survivorId))
            throw new InvalidOperationException($"رابطه {relationName} پس از ادغام به مشتری دیگری منتقل شده است؛ بازگردانی خودکار ایمن نیست.");
    }

    private sealed record MergeManifest
    {
        public Guid[] Contacts { get; init; } = [];
        public Guid[] Addresses { get; init; } = [];
        public Guid[] Leads { get; init; } = [];
        public Guid[] Opportunities { get; init; } = [];
        public Guid[] Quotes { get; init; } = [];
        public Guid[] Orders { get; init; } = [];
        public Guid[] DealerAssignments { get; init; } = [];
        public Guid[] MobileVisits { get; init; } = [];
        public Guid[] ServiceCases { get; init; } = [];
        public Guid[] PortalRequests { get; init; } = [];
        public Guid[] PrimaryContacts { get; init; } = [];
        public Guid[] PrimaryAddresses { get; init; } = [];
    }
}
