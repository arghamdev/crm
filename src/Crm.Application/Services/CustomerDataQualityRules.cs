using System.Globalization;
using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Customers;

namespace Crm.Application.Services;

internal sealed record CustomerDuplicateMatch(Customer Customer, int Score, string Reasons, bool IsExact);

internal static class CustomerDataQualityRules
{
    public static IReadOnlyList<CustomerDuplicateMatch> FindDuplicates(
        IEnumerable<Customer> customers,
        string companyId,
        string name,
        string city,
        string? nationalId,
        string? phone,
        string? email,
        Guid? excludeCustomerId = null)
    {
        var normalizedName = NormalizeText(name);
        var normalizedCity = NormalizeText(city);
        var normalizedNationalId = Digits(nationalId);
        var normalizedPhone = Digits(phone);
        var normalizedEmail = email?.Trim().ToUpperInvariant();
        var results = new List<CustomerDuplicateMatch>();
        foreach (var customer in customers.Where(x =>
                     x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase) && x.Id != excludeCustomerId))
        {
            var score = 0;
            var reasons = new List<string>();
            var exactNationalId = normalizedNationalId.Length >= 8 && normalizedNationalId == Digits(customer.NationalId);
            if (exactNationalId) { score = 100; reasons.Add("شناسه ملی یکسان"); }
            if (!string.IsNullOrWhiteSpace(normalizedEmail) && normalizedEmail == customer.PrimaryEmail)
            {
                score += 35; reasons.Add("ایمیل یکسان");
            }
            if (normalizedPhone.Length >= 7 && normalizedPhone == Digits(customer.PrimaryPhone))
            {
                score += 35; reasons.Add("تلفن یکسان");
            }
            var nameSimilarity = TokenSimilarity(normalizedName, NormalizeText(customer.Name));
            if (nameSimilarity >= .99) { score += 65; reasons.Add("نام یکسان"); }
            else if (nameSimilarity >= .66) { score += 45; reasons.Add("نام بسیار مشابه"); }
            else if (nameSimilarity >= .45) { score += 25; reasons.Add("نام مشابه"); }
            if (normalizedCity.Length > 0 && normalizedCity == NormalizeText(customer.City))
            {
                score += 10; reasons.Add("شهر یکسان");
            }
            score = Math.Clamp(score, 0, 100);
            if (score >= 60) results.Add(new CustomerDuplicateMatch(customer, score, string.Join("، ", reasons), exactNationalId));
        }
        return results.OrderByDescending(x => x.Score).ToList();
    }

    public static IReadOnlyList<DataQualityIssueDto> Issues(CrmDataSet data, Customer customer)
        => Issues(customer,
            data.CustomerContacts.Any(x => x.CustomerId == customer.Id && x.IsActive),
            data.CustomerAddresses.Any(x => x.CustomerId == customer.Id && x.IsActive));

    public static IReadOnlyList<DataQualityIssueDto> Issues(Customer customer, bool hasActiveContact, bool hasActiveAddress)
    {
        var issues = new List<DataQualityIssueDto>();
        if (string.IsNullOrWhiteSpace(customer.NationalId)) issues.Add(new("NationalId", "High", "شناسه ملی/شناسه ثبت تکمیل نشده است."));
        if (string.IsNullOrWhiteSpace(customer.PrimaryPhone)) issues.Add(new("PrimaryPhone", "Medium", "شماره تماس اصلی ثبت نشده است."));
        if (string.IsNullOrWhiteSpace(customer.PrimaryEmail)) issues.Add(new("PrimaryEmail", "Low", "ایمیل اصلی ثبت نشده است."));
        if (!hasActiveContact) issues.Add(new("Contacts", "Medium", "شخص تماس فعال وجود ندارد."));
        if (!hasActiveAddress) issues.Add(new("Addresses", "Medium", "آدرس فعال وجود ندارد."));
        if (customer.LastSynchronizedAtUtc is null) issues.Add(new("FinancialSync", "High", "زمان آخرین همگام‌سازی داده مالی مشخص نیست."));
        else if (customer.LastSynchronizedAtUtc < DateTimeOffset.UtcNow.AddDays(-2)) issues.Add(new("FinancialSync", "Medium", "داده مالی بیش از ۴۸ ساعت قدیمی است."));
        if (customer.Status == CustomerStatus.UnderReview) issues.Add(new("Status", "Low", "مشتری هنوز در وضعیت بررسی است."));
        return issues;
    }

    public static int Score(IReadOnlyList<DataQualityIssueDto> issues)
    {
        var deduction = issues.Sum(x => x.Severity switch { "High" => 20, "Medium" => 10, _ => 5 });
        return Math.Max(0, 100 - deduction);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('ي', 'ی').Replace('ك', 'ک').ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)) builder.Append(character);
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Digits(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty :
        string.Concat(value.Where(char.IsDigit).Select(x => char.GetNumericValue(x).ToString(CultureInfo.InvariantCulture)));

    private static double TokenSimilarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left == right) return 1;
        var first = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var second = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var union = first.Union(second).Count();
        return union == 0 ? 0 : (double)first.Intersect(second).Count() / union;
    }
}
