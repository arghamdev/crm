using Crm.Domain.Customers;

namespace Crm.Application.Contracts;

/// <summary>Customer form fields as typed by the user (Persian digits and Jalali dates accepted).</summary>
public sealed record CustomerProfileInput(
    string? ActivityType = null,
    string? Title = null,
    string? FirstName = null,
    string? LastName = null,
    string? Position = null,
    string? Mobile1 = null,
    string? Mobile2 = null,
    string? Phone1 = null,
    string? Phone2 = null,
    string? Province = null,
    string? Address = null,
    string? PostalCode = null,
    string? Notes = null,
    string? LegalName = null,
    string? EconomicCode = null,
    string? LegalNationalId = null,
    string? NationalCode = null,
    string? RegistrationNumber = null,
    string? BirthCertificateNumber = null,
    string? BirthDate = null,
    string? EmployeeCount = null,
    string? AccountingCode = null,
    string? AcquaintanceDate = null,
    string? SoftwarePurchase = null,
    string? BranchSubscriptionCode = null,
    string? ReferralSource = null,
    string? Website = null);

/// <summary>One contact person (رابط مشتری) row of the customer form.</summary>
public sealed record ContactPersonInput(
    string? Title = null,
    string? FirstName = null,
    string? LastName = null,
    string? Position = null,
    string? Mobile = null,
    string? Phone = null,
    string? Extension = null,
    string? Email = null,
    string? Notes = null,
    bool IsPrimary = false)
{
    public bool IsEmpty => new[] { FirstName, LastName, Position, Mobile, Phone, Extension, Email, Notes }.All(string.IsNullOrWhiteSpace);
}

public sealed record CustomerProfileDto(
    string? ActivityType, string? Title, string? FirstName, string? LastName, string? Position,
    string? Mobile1, string? Mobile2, string? Phone1, string? Phone2, string? Province, string? Notes,
    string? LegalName, string? EconomicCode, string? RegistrationNumber, string? RepresentativeNationalCode,
    string? BirthCertificateNumber, string? BirthDate, string? EmployeeCount, string? AccountingCode, string? AcquaintanceDate,
    string? SoftwarePurchase, string? BranchSubscriptionCode, string? ReferralSource, string? Website, bool HasLogo, bool Masked);

public sealed record CustomerContactFormDto(Guid CustomerId, Guid? ContactId, ContactPersonInput Contact,
    ContactConsentStatus ConsentStatus, long ExpectedVersion);

public sealed record SaveContactPersonCommand(ContactPersonInput Contact, ContactConsentStatus ConsentStatus, long ExpectedVersion);

/// <summary>Business-rule violations keyed by form field (e.g. "Profile.Mobile1", "Contacts[0].Mobile").</summary>
public sealed class CustomerValidationException(IReadOnlyDictionary<string, string> errors)
    : InvalidOperationException(errors.Count == 1 ? errors.First().Value : $"{errors.Count} مورد از اطلاعات فرم نیاز به اصلاح دارد.")
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}

/// <summary>An uploaded logo/photo; the content type is taken from the file signature, not from the client.</summary>
public sealed record CustomerLogoUpload(byte[] Content);

public sealed record CustomerLogoDto(string ContentType, byte[] Content, long Version);
