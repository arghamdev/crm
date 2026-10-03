using Crm.Domain.Common;

namespace Crm.Domain.Customers;

/// <summary>
/// Extended master data of a customer as captured by the customer form. For an individual the person fields describe the
/// customer; for a legal entity they describe its representative. The customer's unique identifier stays on
/// <see cref="Customer.NationalId"/> (national code for individuals, legal national ID for companies).
/// </summary>
public sealed record CustomerProfileData(
    string? ActivityType,
    string? Title,
    string? FirstName,
    string? LastName,
    string? Position,
    string? Mobile1,
    string? Mobile2,
    string? Phone1,
    string? Phone2,
    string? Province,
    string? Notes,
    string? LegalName,
    string? EconomicCode,
    string? RegistrationNumber,
    string? RepresentativeNationalCode,
    string? BirthCertificateNumber,
    DateOnly? BirthDate,
    string? EmployeeCount,
    string? AccountingCode,
    DateOnly? AcquaintanceDate,
    string? SoftwarePurchase,
    string? BranchSubscriptionCode,
    string? ReferralSource,
    string? Website);

public sealed class CustomerProfile : Entity
{
    public CustomerProfile(Guid customerId, string companyId, CustomerKind kind, CustomerProfileData data) : base(customerId)
    {
        CompanyId = string.IsNullOrWhiteSpace(companyId) ? throw new ArgumentException("Value is required.", nameof(companyId)) : companyId;
        Apply(kind, data);
    }

    private CustomerProfile() : base(Guid.Empty) => CompanyId = "EF";

    /// <summary>Same value as <see cref="Entity.Id"/>: one profile per customer.</summary>
    public Guid CustomerId => Id;
    public string CompanyId { get; private set; }
    public string? ActivityType { get; private set; }
    public string? Title { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Position { get; private set; }
    public string? Mobile1 { get; private set; }
    public string? Mobile2 { get; private set; }
    public string? Phone1 { get; private set; }
    public string? Phone2 { get; private set; }
    public string? Province { get; private set; }
    public string? Notes { get; private set; }
    public string? LegalName { get; private set; }
    public string? EconomicCode { get; private set; }
    public string? RegistrationNumber { get; private set; }
    public string? RepresentativeNationalCode { get; private set; }
    public string? BirthCertificateNumber { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? EmployeeCount { get; private set; }
    public string? AccountingCode { get; private set; }
    public DateOnly? AcquaintanceDate { get; private set; }
    public string? SoftwarePurchase { get; private set; }
    public string? BranchSubscriptionCode { get; private set; }
    public string? ReferralSource { get; private set; }
    public string? Website { get; private set; }
    /// <summary>Content type of the stored logo, or null when there is none (lets pages skip loading the image bytes).</summary>
    public string? LogoContentType { get; private set; }

    public string? PersonName => string.Join(' ', new[] { FirstName, LastName }.Where(x => x is not null)) is { Length: > 0 } name ? name : null;

    public CustomerProfileData Data => new(ActivityType, Title, FirstName, LastName, Position, Mobile1, Mobile2, Phone1, Phone2, Province,
        Notes, LegalName, EconomicCode, RegistrationNumber, RepresentativeNationalCode, BirthCertificateNumber, BirthDate, EmployeeCount,
        AccountingCode, AcquaintanceDate, SoftwarePurchase, BranchSubscriptionCode, ReferralSource, Website);

    public void SetLogo(string? contentType)
    {
        LogoContentType = contentType;
        Touch();
    }

    public void Update(CustomerKind kind, CustomerProfileData data)
    {
        Apply(kind, data);
        Touch();
    }

    /// <summary>Kind rules: an individual needs first and last name and has no company registration; a company has no birth data.</summary>
    private void Apply(CustomerKind kind, CustomerProfileData data)
    {
        var value = Clean(data);
        if (kind == CustomerKind.Individual)
        {
            if (value.FirstName is null || value.LastName is null)
                throw new InvalidOperationException("برای مشتری حقیقی، نام و نام خانوادگی الزامی است.");
            if (value.RegistrationNumber is not null) throw new InvalidOperationException("شماره ثبت فقط برای مشتری حقوقی است.");
            if (value.RepresentativeNationalCode is not null) throw new InvalidOperationException("کد ملی مشتری حقیقی در شناسهٔ مشتری ثبت می‌شود.");
        }
        if (value.BirthDate is { } birth && (birth.Year < 1900 || birth > DateOnly.FromDateTime(DateTime.UtcNow)))
            throw new InvalidOperationException("تاریخ تولد معتبر نیست.");
        (ActivityType, Title, FirstName, LastName, Position) = (value.ActivityType, value.Title, value.FirstName, value.LastName, value.Position);
        (Mobile1, Mobile2, Phone1, Phone2, Province, Notes) = (value.Mobile1, value.Mobile2, value.Phone1, value.Phone2, value.Province, value.Notes);
        (LegalName, EconomicCode, RegistrationNumber, RepresentativeNationalCode) =
            (value.LegalName, value.EconomicCode, value.RegistrationNumber, value.RepresentativeNationalCode);
        (BirthCertificateNumber, BirthDate, EmployeeCount, AccountingCode, AcquaintanceDate) =
            (value.BirthCertificateNumber, value.BirthDate, value.EmployeeCount, value.AccountingCode, value.AcquaintanceDate);
        (SoftwarePurchase, BranchSubscriptionCode, ReferralSource, Website) =
            (value.SoftwarePurchase, value.BranchSubscriptionCode, value.ReferralSource, value.Website);
    }

    private static CustomerProfileData Clean(CustomerProfileData x) => x with
    {
        ActivityType = Text(x.ActivityType, 80), Title = Text(x.Title, 40), FirstName = Text(x.FirstName, 100), LastName = Text(x.LastName, 100),
        Position = Text(x.Position, 80), Mobile1 = Text(x.Mobile1, 20), Mobile2 = Text(x.Mobile2, 20), Phone1 = Text(x.Phone1, 20),
        Phone2 = Text(x.Phone2, 20), Province = Text(x.Province, 60), Notes = Text(x.Notes, 2000), LegalName = Text(x.LegalName, 200),
        EconomicCode = Text(x.EconomicCode, 14), RegistrationNumber = Text(x.RegistrationNumber, 20),
        RepresentativeNationalCode = Text(x.RepresentativeNationalCode, 10), BirthCertificateNumber = Text(x.BirthCertificateNumber, 20),
        EmployeeCount = Text(x.EmployeeCount, 40), AccountingCode = Text(x.AccountingCode, 40), SoftwarePurchase = Text(x.SoftwarePurchase, 200),
        BranchSubscriptionCode = Text(x.BranchSubscriptionCode, 40), ReferralSource = Text(x.ReferralSource, 120), Website = Text(x.Website, 200)
    };

    private static string? Text(string? value, int max)
    {
        var normalized = PersianText.NormalizeLetters(value);
        if (normalized is null) return null;
        return normalized.Length <= max ? normalized : throw new InvalidOperationException($"طول مقدار «{normalized[..Math.Min(20, normalized.Length)]}…» بیش از {max} نویسه است.");
    }
}

/// <summary>Customer logo/photo stored separately so that loading a profile never loads image bytes.</summary>
public sealed class CustomerLogo : Entity
{
    public const int MaxBytes = 512 * 1024;

    public CustomerLogo(Guid customerId, string companyId, byte[] content) : base(customerId)
    {
        CompanyId = companyId;
        (ContentType, Content) = Validated(content);
    }

    private CustomerLogo() : base(Guid.Empty)
    {
        CompanyId = "EF";
        ContentType = "image/png";
        Content = [];
    }

    public string CompanyId { get; private set; }
    public string ContentType { get; private set; }
    public byte[] Content { get; private set; }

    public void Replace(byte[] content)
    {
        (ContentType, Content) = Validated(content);
        Touch();
    }

    private static (string ContentType, byte[] Content) Validated(byte[] content)
    {
        if (content.Length == 0 || content.Length > MaxBytes) throw new InvalidOperationException("حجم تصویر باید حداکثر ۵۱۲ کیلوبایت باشد.");
        var detected = Detect(content) ?? throw new InvalidOperationException("فقط تصویر PNG، JPEG یا WebP پذیرفته می‌شود.");
        return (detected, content);
    }

    /// <summary>Content type from the file signature, never from the client-supplied header.</summary>
    public static string? Detect(ReadOnlySpan<byte> content) => content switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null
    };
}
