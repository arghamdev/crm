using Crm.Application.Abstractions;
using Crm.Application.Contracts;

namespace Crm.Application.Services;

/// <summary>
/// Field-level masking applied to DTOs before they leave the application layer, so a view or API can never
/// render a value the caller is not entitled to. Record-level access (permission + scope) is enforced
/// separately; masking only narrows what an already-visible record exposes.
/// </summary>
public static class FieldMasking
{
    public const string ContactPermission = "Customer.Contact.Read";
    public const string NationalIdPermission = "Customer.NationalId.Read";
    public const string FinancialPermission = "Customer.Financial.Read";
    public const string MarginPermission = "Quote.Margin.Read";

    public static CustomerDto Mask(this CustomerDto value, AccessSnapshot snapshot)
    {
        var permissions = snapshot.PermissionsFor(value.CompanyId);
        var financial = permissions.Contains(FinancialPermission);
        return value with
        {
            NationalId = permissions.Contains(NationalIdPermission) ? value.NationalId : MaskIdentifier(value.NationalId),
            PrimaryPhone = permissions.Contains(ContactPermission) ? value.PrimaryPhone : MaskPhone(value.PrimaryPhone),
            PrimaryEmail = permissions.Contains(ContactPermission) ? value.PrimaryEmail : MaskEmail(value.PrimaryEmail),
            Balance = financial ? value.Balance : 0,
            CreditLimit = financial ? value.CreditLimit : 0,
            FinancialMasked = !financial
        };
    }

    public static LeadDto Mask(this LeadDto value, AccessSnapshot snapshot)
    {
        if (snapshot.PermissionsFor(value.CompanyId).Contains(ContactPermission)) return value;
        return value with { Phone = MaskPhone(value.Phone), Email = MaskEmail(value.Email) };
    }

    public static CustomerContactDto Mask(this CustomerContactDto value, AccessSnapshot snapshot, string companyId)
    {
        if (snapshot.PermissionsFor(companyId).Contains(ContactPermission)) return value;
        return value with { Phone = MaskPhone(value.Phone), Mobile = MaskPhone(value.Mobile), Email = MaskEmail(value.Email) };
    }

    public static QuoteSummaryDto Mask(this QuoteSummaryDto value, AccessSnapshot snapshot) =>
        snapshot.PermissionsFor(value.CompanyId).Contains(MarginPermission) ? value : value with { MarginPercent = 0, MarginMasked = true };

    public static QuoteDto Mask(this QuoteDto value, AccessSnapshot snapshot) =>
        snapshot.PermissionsFor(value.CompanyId).Contains(MarginPermission) ? value : value with { MarginPercent = 0, MarginMasked = true };

    public static QuoteDetailsDto Mask(this QuoteDetailsDto value, AccessSnapshot snapshot)
    {
        if (snapshot.PermissionsFor(value.Quote.CompanyId).Contains(MarginPermission)) return value;
        return value with
        {
            Quote = value.Quote.Mask(snapshot),
            Lines = value.Lines.Select(x => x with { MarginPercent = 0, MarginMasked = true }).ToList()
        };
    }

    /// <summary>Keeps the last three characters: "10101234567" → "********567".</summary>
    public static string? MaskIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Length <= 3 ? new string('*', value.Length) : new string('*', value.Length - 3) + value[^3..];

    /// <summary>Keeps the first four and last two digits: "09120005505" → "0912*****05".</summary>
    public static string? MaskPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (value.Length <= 6) return new string('*', value.Length);
        return value[..4] + new string('*', value.Length - 6) + value[^2..];
    }

    /// <summary>Keeps the first character of the local part and the domain: "info@sepehr.test" → "i***@sepehr.test".</summary>
    public static string? MaskEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var at = value.IndexOf('@');
        return at <= 0 ? MaskIdentifier(value) : value[0] + "***" + value[at..];
    }
}
