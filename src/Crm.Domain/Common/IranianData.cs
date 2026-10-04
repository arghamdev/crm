using System.Globalization;
using System.Text;

namespace Crm.Domain.Common;

/// <summary>Normalizes text typed on Persian keyboards: Persian/Arabic digits become ASCII, Arabic yeh/kaf become Persian.</summary>
public static class PersianText
{
    /// <summary>Trim, Arabic yeh/kaf to Persian, and Persian/Arabic digits to ASCII (for codes, numbers and dates).</summary>
    public static string? Normalize(string? value) => Map(value, digits: true);

    /// <summary>Trim and Arabic yeh/kaf to Persian only; digits stay as typed (for names and descriptive text).</summary>
    public static string? NormalizeLetters(string? value) => Map(value, digits: false);

    private static string? Map(string? value, bool digits)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            builder.Append(c switch
            {
                >= '\u06F0' and <= '\u06F9' when digits => (char)('0' + (c - '\u06F0')),
                >= '\u0660' and <= '\u0669' when digits => (char)('0' + (c - '\u0660')),
                '\u064A' => '\u06CC',
                '\u0643' => '\u06A9',
                _ => c
            });
        }
        return builder.ToString();
    }

    /// <summary>Digits only (spaces, dashes and slashes removed), or null when nothing is left.</summary>
    public static string? Digits(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null) return null;
        var digits = new string(normalized.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}

/// <summary>Format and checksum rules of Iranian identifiers, phone numbers and postal codes.</summary>
public static class IranianIdentifiers
{
    private static readonly int[] LegalCoefficients = [29, 27, 23, 19, 17, 29, 27, 23, 19, 17];

    /// <summary>Individual national code (کد ملی): 10 digits with a mod-11 check digit.</summary>
    public static bool IsValidNationalCode(string? value)
    {
        if (value is not { Length: 10 } || !value.All(char.IsAsciiDigit) || value.Distinct().Count() == 1) return false;
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (value[i] - '0') * (10 - i);
        var remainder = sum % 11;
        var check = value[9] - '0';
        return remainder < 2 ? check == remainder : check == 11 - remainder;
    }

    /// <summary>Legal entity national ID (شناسه ملی): 11 digits; the check digit uses the tens digit + 2 as an offset.</summary>
    public static bool IsValidLegalNationalId(string? value)
    {
        if (value is not { Length: 11 } || !value.All(char.IsAsciiDigit) || value.Distinct().Count() == 1) return false;
        var offset = value[9] - '0' + 2;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (value[i] - '0' + offset) * LegalCoefficients[i];
        var remainder = sum % 11;
        if (remainder == 10) remainder = 0;
        return value[10] - '0' == remainder;
    }

    /// <summary>Economic code (کد اقتصادی): 12 digits for individuals, 11 (the national ID) or 12/14 for legal entities.</summary>
    public static bool IsValidEconomicCode(string? value) => value is { Length: 11 or 12 or 14 } && value.All(char.IsAsciiDigit);

    /// <summary>Iranian mobile number in the 09xxxxxxxxx form; +98 / 0098 / 98 prefixes are accepted and normalized.</summary>
    public static string? NormalizeMobile(string? value)
    {
        var digits = PersianText.Digits(value);
        if (digits is null) return null;
        if (digits.StartsWith("0098", StringComparison.Ordinal)) digits = "0" + digits[4..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.StartsWith('9') && digits.Length == 10) digits = "0" + digits;
        return digits;
    }

    public static bool IsValidMobile(string? normalized) => normalized is { Length: 11 } && normalized.StartsWith("09", StringComparison.Ordinal);

    /// <summary>Landline with area code (02188776655: 11 digits, 0 then not 9) or a local 8-digit number.</summary>
    public static bool IsValidLandline(string? digits) =>
        digits is { Length: 11 } && digits[0] == '0' && digits[1] is not '9' and not '0' ||
        digits is { Length: 8 } && digits[0] != '0';

    /// <summary>Sheba/IBAN: "IR" + 24 digits; spaces and Persian digits are accepted on input.</summary>
    public static string? NormalizeIban(string? value)
    {
        var text = PersianText.Normalize(value)?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        if (text is null) return null;
        return text.All(char.IsAsciiDigit) && text.Length == 24 ? "IR" + text : text;
    }

    /// <summary>ISO 13616 mod-97 check of an Iranian IBAN.</summary>
    public static bool IsValidIban(string? iban)
    {
        if (iban is not { Length: 26 } || !iban.StartsWith("IR", StringComparison.Ordinal) || !iban[2..].All(char.IsAsciiDigit)) return false;
        var rearranged = iban[4..] + "1827" + iban[2..4]; // I=18, R=27
        return System.Numerics.BigInteger.Parse(rearranged, System.Globalization.CultureInfo.InvariantCulture) % 97 == 1;
    }

    /// <summary>Postal code: 10 digits, the first five without 0 or 2 (post office rule), and not a single repeated digit.</summary>
    public static bool IsValidPostalCode(string? digits) =>
        digits is { Length: 10 } && digits[..5].All(c => c is not '0' and not '2') && digits.Distinct().Count() > 1;
}

/// <summary>Solar Hijri (Jalali) dates in the yyyy/MM/dd form used by Persian users.</summary>
public static class JalaliDate
{
    private static readonly PersianCalendar Calendar = new();

    public static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public static bool TryParse(string? value, out DateOnly date)
    {
        date = default;
        var normalized = PersianText.Normalize(value);
        if (normalized is null) return false;
        var parts = normalized.Split('/', '-', '.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) ||
            !int.TryParse(parts[2], out var day)) return false;
        if (year < 1200 || year > 1600 || month is < 1 or > 12 || day < 1 || day > Calendar.GetDaysInMonth(year, month)) return false;
        date = DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
        return true;
    }

    public static string Format(DateOnly date) => Format(date.ToDateTime(TimeOnly.MinValue));

    public static string Format(DateTime value) =>
        $"{Calendar.GetYear(value):0000}/{Calendar.GetMonth(value):00}/{Calendar.GetDayOfMonth(value):00}";

    public static (int Year, int Month) YearMonth(DateTimeOffset value) =>
        (Calendar.GetYear(value.UtcDateTime), Calendar.GetMonth(value.UtcDateTime));

    /// <summary>UTC midnight of the first day of a Jalali month (Gregorian date of 1st of the month).</summary>
    public static DateTimeOffset MonthStart(int year, int month)
    {
        var start = Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0);
        return new DateTimeOffset(start.Year, start.Month, start.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public static DateTimeOffset NextMonthStart(int year, int month) => month == 12 ? MonthStart(year + 1, 1) : MonthStart(year, month + 1);

    public static string MonthLabel(int year, int month) => $"{MonthNames[month - 1]} {year}";
}

/// <summary>Jalali date + local time in Iran (Asia/Tehran), the way users enter and read times.</summary>
public static class TehranTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromMinutes(210), "Tehran", "Tehran");
        }
    }

    /// <summary>Jalali date ("1405/07/12") and time ("14:30") in Tehran time to UTC; null when the date is empty.</summary>
    public static DateTimeOffset? ToUtc(string? jalaliDate, string? time, string label)
    {
        if (string.IsNullOrWhiteSpace(jalaliDate)) return null;
        if (!JalaliDate.TryParse(jalaliDate, out var date)) throw new InvalidOperationException($"{label}: تاریخ را به شکل شمسی ۱۴۰۵/۰۷/۱۲ وارد کنید.");
        var clock = TimeOnly.MinValue;
        var text = PersianText.Normalize(time);
        if (text is not null && !TimeOnly.TryParseExact(text, ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out clock))
            throw new InvalidOperationException($"{label}: ساعت را به شکل ۱۴:۳۰ وارد کنید.");
        var local = date.ToDateTime(clock, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateTime Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone).DateTime;

    public static string Date(DateTimeOffset utc) => JalaliDate.Format(Local(utc));

    public static string Clock(DateTimeOffset utc) => Local(utc).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public static string Format(DateTimeOffset utc) => $"{Date(utc)} {Clock(utc)}";

    public static DateOnly Today(DateTimeOffset utc) => DateOnly.FromDateTime(Local(utc));
}
