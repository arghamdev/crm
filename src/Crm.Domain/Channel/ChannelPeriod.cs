using Crm.Domain.Common;

namespace Crm.Domain.Channel;

/// <summary>
/// The channel module's period: a Persian (Jalali) calendar month, from UTC midnight of its first day to UTC midnight of
/// the next month's first day. Dealer targets, ERP sales snapshots, commission statements and rankings share it, so
/// they can be matched by exact period boundaries.
/// </summary>
public static class ChannelPeriod
{
    public static (DateTimeOffset From, DateTimeOffset To) MonthOf(DateTimeOffset value)
    {
        var (year, month) = JalaliDate.YearMonth(value);
        return (JalaliDate.MonthStart(year, month), JalaliDate.NextMonthStart(year, month));
    }

    public static DateTimeOffset StartOf(DateTimeOffset value) => MonthOf(value).From;

    public static (DateTimeOffset From, DateTimeOffset To) Previous(DateTimeOffset periodStart) => MonthOf(periodStart.AddDays(-1));

    /// <summary>Parses "1405-07" or "1405/07" (Jalali year-month); null when invalid.</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        var normalized = PersianText.Normalize(value);
        if (normalized is null) return null;
        var parts = normalized.Split('-', '/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) ||
            year is < 1300 or > 1500 || month is < 1 or > 12) return null;
        return JalaliDate.MonthStart(year, month);
    }

    /// <summary>"1405-07" key of the Jalali month containing the value.</summary>
    public static string Key(DateTimeOffset value)
    {
        var (year, month) = JalaliDate.YearMonth(value);
        return $"{year:0000}-{month:00}";
    }

    public static string Label(DateTimeOffset value)
    {
        var (year, month) = JalaliDate.YearMonth(value);
        return JalaliDate.MonthLabel(year, month);
    }
}
