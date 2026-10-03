using System.Text.RegularExpressions;
using Crm.Application.Contracts;
using Crm.Domain.Common;
using Crm.Domain.Customers;

namespace Crm.Application.Services;

public sealed record NormalizedContactPerson(ContactPersonDetails Details, string Position, string? Phone, string? Email, bool IsPrimary);

public sealed record NormalizedCustomerForm(
    string? NationalId,
    string? PrimaryPhone,
    string? PrimaryEmail,
    CustomerProfileData Profile,
    string? Address,
    string? PostalCode,
    IReadOnlyList<NormalizedContactPerson> Contacts);

/// <summary>
/// Validation of the customer form for individual (حقیقی) and legal (حقوقی) customers and their contact persons.
/// Every problem is reported against its field so the whole form can be corrected in one pass.
/// </summary>
public static partial class CustomerFormRules
{
    public static readonly string[] ActivityTypes =
        ["تولیدی", "بازرگانی و پخش", "فروشگاه و خرده‌فروشی", "خدماتی", "پیمانکاری و عمرانی", "دولتی و عمومی", "آموزشی", "درمانی", "سایر موارد"];
    public static readonly string[] Titles = ["آقای", "خانم", "دکتر", "مهندس"];
    public static readonly string[] Positions =
        ["نامشخص", "مالک", "مدیرعامل", "عضو هیئت‌مدیره", "مدیر خرید", "مدیر مالی", "مدیر فروش", "مدیر فنی", "کارشناس", "سایر"];
    public static readonly string[] EmployeeCounts = ["۱ تا ۱۰ نفر", "۱۱ تا ۵۰ نفر", "۵۱ تا ۲۰۰ نفر", "۲۰۱ تا ۱۰۰۰ نفر", "بیش از ۱۰۰۰ نفر"];
    public static readonly string[] ReferralSources =
        ["معرفی مشتری", "وب‌سایت", "شبکه‌های اجتماعی", "نمایشگاه", "تبلیغات", "تماس فروش", "نماینده فروش", "سایر"];

    /// <param name="currentNationalId">The stored identifier; an unchanged legacy value is not re-validated.</param>
    public static NormalizedCustomerForm Normalize(CustomerKind kind, CustomerProfileInput input, IReadOnlyList<ContactPersonInput>? contacts,
        string? email, string? currentNationalId = null)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        void Error(string field, string message) => errors.TryAdd(field, message);
        const string p = "Profile.";

        var firstName = Clean(input.FirstName);
        var lastName = Clean(input.LastName);
        var nationalCode = PersianText.Digits(input.NationalCode);
        var legalNationalId = PersianText.Digits(input.LegalNationalId);
        var registration = PersianText.Digits(input.RegistrationNumber);
        if (kind == CustomerKind.Individual)
        {
            if (firstName is null) Error(p + "FirstName", "نام مشتری حقیقی الزامی است.");
            if (lastName is null) Error(p + "LastName", "نام خانوادگی مشتری حقیقی الزامی است.");
            // Company-only fields are hidden for individuals; anything left in them from switching the type is dropped.
            legalNationalId = null;
            registration = null;
            if (nationalCode is not null && nationalCode != currentNationalId && !IranianIdentifiers.IsValidNationalCode(nationalCode))
                Error(p + "NationalCode", "کد ملی معتبر نیست (۱۰ رقم با رقم کنترل).");
        }
        else
        {
            if (legalNationalId is not null && legalNationalId != currentNationalId && !IranianIdentifiers.IsValidLegalNationalId(legalNationalId))
                Error(p + "LegalNationalId", "شناسه ملی شرکت معتبر نیست (۱۱ رقم با رقم کنترل).");
            if (nationalCode is not null && !IranianIdentifiers.IsValidNationalCode(nationalCode))
                Error(p + "NationalCode", "کد ملی نماینده معتبر نیست.");
            if (registration is not null && registration.Length > 20) Error(p + "RegistrationNumber", "شماره ثبت حداکثر ۲۰ رقم است.");
            if (input.RegistrationNumber is not null && registration is null && Clean(input.RegistrationNumber) is not null)
                Error(p + "RegistrationNumber", "شماره ثبت باید عددی باشد.");
        }

        var mobile1 = Mobile(input.Mobile1, p + "Mobile1", Error);
        var mobile2 = Mobile(input.Mobile2, p + "Mobile2", Error);
        var phone1 = Landline(input.Phone1, p + "Phone1", Error);
        var phone2 = Landline(input.Phone2, p + "Phone2", Error);
        var postal = PersianText.Digits(input.PostalCode);
        if (postal is not null && !IranianIdentifiers.IsValidPostalCode(postal)) Error(p + "PostalCode", "کد پستی باید ۱۰ رقم معتبر باشد.");
        var economic = PersianText.Digits(input.EconomicCode);
        if (economic is not null && !IranianIdentifiers.IsValidEconomicCode(economic)) Error(p + "EconomicCode", "کد اقتصادی باید ۱۱، ۱۲ یا ۱۴ رقم باشد.");
        var certificate = PersianText.Digits(input.BirthCertificateNumber);
        if (Clean(input.BirthCertificateNumber) is not null && (certificate is null || certificate.Length > 10))
            Error(p + "BirthCertificateNumber", "شماره شناسنامه باید حداکثر ۱۰ رقم باشد.");

        var province = Clean(input.Province);
        if (province is not null && !IranDivisions.Cities.ContainsKey(province)) Error(p + "Province", "استان از فهرست انتخاب نشده است.");
        var activity = Option(input.ActivityType, ActivityTypes, p + "ActivityType", Error);
        var title = Option(input.Title, Titles, p + "Title", Error);
        var position = Option(input.Position, Positions, p + "Position", Error);
        var employees = Option(input.EmployeeCount, EmployeeCounts, p + "EmployeeCount", Error);

        var birthDate = Date(input.BirthDate, p + "BirthDate", Error);
        if (birthDate is { } birth && (birth > DateOnly.FromDateTime(DateTime.UtcNow) || birth.Year < 1900)) Error(p + "BirthDate", "تاریخ تولد معتبر نیست.");
        var acquaintance = Date(input.AcquaintanceDate, p + "AcquaintanceDate", Error);
        if (acquaintance > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))) Error(p + "AcquaintanceDate", "تاریخ آشنایی نمی‌تواند در آینده باشد.");

        var website = Website(input.Website, p + "Website", Error);
        var normalizedEmail = Email(email, "PrimaryEmail", Error);
        var address = Clean(input.Address);
        if (address is { Length: > 1000 }) Error(p + "Address", "آدرس حداکثر ۱۰۰۰ نویسه است.");

        var people = new List<NormalizedContactPerson>();
        var rows = (contacts ?? []).ToList<ContactPersonInput?>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row is null || row.IsEmpty) continue; // an untouched appended row binds as null
            var key = $"Contacts[{i}].";
            var first = Clean(row.FirstName);
            var last = Clean(row.LastName);
            if (first is null && last is null) Error(key + "LastName", "نام یا نام خانوادگی رابط الزامی است.");
            var contactMobile = Mobile(row.Mobile, key + "Mobile", Error);
            var contactPhone = Landline(row.Phone, key + "Phone", Error);
            var extension = PersianText.Digits(row.Extension);
            if (Clean(row.Extension) is not null && (extension is null || extension.Length > 6)) Error(key + "Extension", "داخلی باید حداکثر ۶ رقم باشد.");
            var contactEmail = Email(row.Email, key + "Email", Error);
            var contactTitle = Option(row.Title, Titles, key + "Title", Error);
            var contactPosition = Clean(row.Position);
            if (contactPosition is { Length: > 80 }) Error(key + "Position", "سمت حداکثر ۸۰ نویسه است.");
            var notes = Clean(row.Notes);
            if (notes is { Length: > 1000 }) Error(key + "Notes", "توضیحات حداکثر ۱۰۰۰ نویسه است.");
            people.Add(new NormalizedContactPerson(new ContactPersonDetails(contactTitle, first, last, contactMobile, extension, notes),
                contactPosition ?? string.Empty, contactPhone, contactEmail, row.IsPrimary));
        }
        if (people.Count(x => x.IsPrimary) > 1) Error("Contacts", "فقط یک رابط می‌تواند رابط اصلی باشد.");

        if (errors.Count > 0) throw new CustomerValidationException(errors);
        if (people.Count > 0 && !people.Any(x => x.IsPrimary)) people[0] = people[0] with { IsPrimary = true };

        var profile = new CustomerProfileData(activity, title, firstName, lastName, position, mobile1, mobile2, phone1, phone2, province,
            Clean(input.Notes), Clean(input.LegalName), economic, kind == CustomerKind.Legal ? registration : null,
            kind == CustomerKind.Legal ? nationalCode : null, certificate, birthDate, employees, Clean(input.AccountingCode), acquaintance,
            Clean(input.SoftwarePurchase), Clean(input.BranchSubscriptionCode), Clean(input.ReferralSource), website);
        return new NormalizedCustomerForm(kind == CustomerKind.Legal ? legalNationalId : nationalCode, phone1 ?? mobile1,
            normalizedEmail, profile, address, postal, people);
    }

    /// <summary>Form values for an existing customer (dates back in Jalali), used to pre-fill the edit form.</summary>
    public static CustomerProfileInput ToInput(Customer customer, CustomerProfile? profile, CustomerAddress? address)
    {
        var x = profile?.Data;
        return new CustomerProfileInput(x?.ActivityType, x?.Title, x?.FirstName, x?.LastName, x?.Position, x?.Mobile1, x?.Mobile2,
            x?.Phone1 ?? (profile is null ? customer.PrimaryPhone : null), x?.Phone2, x?.Province ?? address?.Province, address?.AddressLine,
            address?.PostalCode, x?.Notes, x?.LegalName, x?.EconomicCode,
            customer.Kind == CustomerKind.Legal ? customer.NationalId : null,
            customer.Kind == CustomerKind.Individual ? customer.NationalId : x?.RepresentativeNationalCode,
            x?.RegistrationNumber, x?.BirthCertificateNumber, x?.BirthDate is { } b ? JalaliDate.Format(b) : null, x?.EmployeeCount,
            x?.AccountingCode, x?.AcquaintanceDate is { } a ? JalaliDate.Format(a) : null, x?.SoftwarePurchase, x?.BranchSubscriptionCode,
            x?.ReferralSource, x?.Website);
    }

    /// <summary>Form values for a stored contact; a legacy free-form name (no first/last split) is shown as the last name.</summary>
    public static ContactPersonInput ToInput(CustomerContact contact) => new(contact.Title, contact.FirstName,
        contact.LastName ?? (contact.FirstName is null ? contact.FullName : null), contact.Role, contact.Mobile, contact.Phone, contact.Extension,
        contact.Email?.ToLowerInvariant(), contact.Notes, contact.IsPrimary);

    private static string? Clean(string? value) => PersianText.NormalizeLetters(value);

    private static string? Mobile(string? value, string field, Action<string, string> error)
    {
        if (Clean(value) is null) return null;
        var normalized = IranianIdentifiers.NormalizeMobile(value);
        if (IranianIdentifiers.IsValidMobile(normalized)) return normalized;
        error(field, "شمارهٔ همراه باید ۱۱ رقم باشد و با ۰۹ شروع شود.");
        return null;
    }

    private static string? Landline(string? value, string field, Action<string, string> error)
    {
        if (Clean(value) is null) return null;
        var digits = PersianText.Digits(value);
        if (IranianIdentifiers.IsValidLandline(digits)) return digits;
        error(field, "تلفن ثابت باید با پیش‌شماره (مثل ۰۲۱۸۸۷۷۶۶۵۵) یا ۸ رقم باشد.");
        return null;
    }

    private static DateOnly? Date(string? value, string field, Action<string, string> error)
    {
        if (Clean(value) is null) return null;
        if (JalaliDate.TryParse(value, out var date)) return date;
        error(field, "تاریخ را به شکل شمسی ۱۴۰۳/۰۵/۱۲ وارد کنید.");
        return null;
    }

    private static string? Option(string? value, string[] options, string field, Action<string, string> error)
    {
        var clean = Clean(value);
        if (clean is null || options.Contains(clean, StringComparer.Ordinal)) return clean;
        error(field, "مقدار از فهرست انتخاب نشده است.");
        return null;
    }

    private static string? Email(string? value, string field, Action<string, string> error)
    {
        var clean = Clean(value);
        if (clean is null) return null;
        if (clean.Length <= 256 && EmailPattern().IsMatch(clean)) return clean;
        error(field, "ایمیل معتبر نیست.");
        return null;
    }

    private static string? Website(string? value, string field, Action<string, string> error)
    {
        var clean = Clean(value);
        if (clean is null) return null;
        var candidate = clean.Contains("://", StringComparison.Ordinal) ? clean : "https://" + clean;
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Contains('.') &&
            candidate.Length <= 200) return uri.ToString().TrimEnd('/');
        error(field, "آدرس وب‌سایت معتبر نیست.");
        return null;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
