using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Common;
using Crm.Domain.Customers;

namespace Crm.Application.Services;

/// <summary>Persists the extended customer form (profile, registered address, logo, contact persons) for create and edit.</summary>
internal static class CustomerProfileWriter
{
    public const string RegisteredAddressTitle = "آدرس ثبت‌شده";

    public static void Apply(CrmDataSet data, Customer customer, NormalizedCustomerForm form, CustomerLogoUpload? logo, bool removeLogo)
    {
        var profile = data.Find<CustomerProfile>(x => x.Id == customer.Id).SingleOrDefault();
        if (profile is null)
        {
            profile = new CustomerProfile(customer.Id, customer.CompanyId, customer.Kind, form.Profile);
            data.CustomerProfiles.Add(profile);
        }
        else profile.Update(customer.Kind, form.Profile);

        if (form.Address is not null)
        {
            var address = data.Find<CustomerAddress>(x => x.CustomerId == customer.Id && x.IsActive && x.Type == CustomerAddressType.Registered)
                .OrderByDescending(x => x.IsPrimary).FirstOrDefault();
            var province = form.Profile.Province ?? string.Empty;
            if (address is null)
            {
                var hasPrimary = data.Find<CustomerAddress>(x => x.CustomerId == customer.Id && x.IsActive && x.IsPrimary).Any();
                data.CustomerAddresses.Add(new CustomerAddress(Guid.NewGuid(), customer.CompanyId, customer.Id, CustomerAddressType.Registered,
                    RegisteredAddressTitle, province, customer.City, form.Address, form.PostalCode, !hasPrimary));
            }
            else address.Update(address.Title, province, customer.City, form.Address, form.PostalCode, address.IsPrimary);
        }

        var stored = data.Find<CustomerLogo>(x => x.Id == customer.Id).SingleOrDefault();
        if (logo is not null)
        {
            if (stored is null)
            {
                stored = new CustomerLogo(customer.Id, customer.CompanyId, logo.Content);
                data.CustomerLogos.Add(stored);
            }
            else stored.Replace(logo.Content);
            profile.SetLogo(stored.ContentType);
        }
        else if (removeLogo && stored is not null)
        {
            data.CustomerLogos.Remove(stored);
            profile.SetLogo(null);
        }
    }

    /// <summary>Adds the form's contact persons; without any, the profile person (or legacy phone/email) becomes the primary contact.</summary>
    public static IReadOnlyList<CustomerContact> AddContacts(CrmDataSet data, Customer customer, NormalizedCustomerForm form)
    {
        var added = new List<CustomerContact>();
        foreach (var person in form.Contacts)
            added.Add(new CustomerContact(Guid.NewGuid(), customer.CompanyId, customer.Id, person.Details.FullName ?? "رابط",
                person.Position, person.Phone, person.Email, person.IsPrimary, ContactConsentStatus.Unknown, person.Details));
        if (added.Count == 0 && form.Profile is { FirstName: not null } or { LastName: not null } &&
            (form.Profile.Mobile1 is not null || form.Profile.Phone1 is not null || form.PrimaryEmail is not null))
        {
            var details = new ContactPersonDetails(form.Profile.Title, form.Profile.FirstName, form.Profile.LastName, form.Profile.Mobile1, null, null);
            added.Add(new CustomerContact(Guid.NewGuid(), customer.CompanyId, customer.Id, details.FullName!,
                form.Profile.Position ?? (customer.Kind == CustomerKind.Individual ? "مشتری" : "نماینده شرکت"), form.Profile.Phone1,
                form.PrimaryEmail, true, ContactConsentStatus.Unknown, details));
        }
        data.CustomerContacts.AddRange(added);
        return added;
    }

    public static CustomerProfileDto? Map(CustomerProfile? profile, Customer customer, AccessSnapshot snapshot)
    {
        if (profile is null) return null;
        var permissions = snapshot.PermissionsFor(customer.CompanyId);
        var contact = permissions.Contains(FieldMasking.ContactPermission);
        var identity = permissions.Contains(FieldMasking.NationalIdPermission);
        string? Phone(string? value) => contact ? value : FieldMasking.MaskPhone(value);
        string? Id(string? value) => identity ? value : FieldMasking.MaskIdentifier(value);
        return new CustomerProfileDto(profile.ActivityType, profile.Title, profile.FirstName, profile.LastName, profile.Position,
            Phone(profile.Mobile1), Phone(profile.Mobile2), Phone(profile.Phone1), Phone(profile.Phone2), profile.Province, profile.Notes,
            profile.LegalName, profile.EconomicCode, profile.RegistrationNumber, Id(profile.RepresentativeNationalCode),
            Id(profile.BirthCertificateNumber), identity ? profile.BirthDate is { } b ? JalaliDate.Format(b) : null : profile.BirthDate is null ? null : "****/**/**",
            profile.EmployeeCount, profile.AccountingCode, profile.AcquaintanceDate is { } a ? JalaliDate.Format(a) : null,
            profile.SoftwarePurchase, profile.BranchSubscriptionCode, profile.ReferralSource, profile.Website, profile.LogoContentType is not null,
            !contact || !identity);
    }
}
