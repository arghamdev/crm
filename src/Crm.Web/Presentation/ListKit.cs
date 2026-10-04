using System.Globalization;
using Crm.Domain.Common;

namespace Crm.Web.Presentation;

/// <summary>
/// The current state of a list workspace (view, filters, sort, page) and the links that change one part of it.
/// Every link exists twice: a full-page URL (href, works without JavaScript and is put in the address bar) and the
/// fragment URL htmx swaps into the workspace.
/// </summary>
public sealed class ListState(string pagePath, string tablePath, IReadOnlyDictionary<string, string?> values)
{
    public string PagePath => pagePath;
    public string TablePath => tablePath;

    public string? this[string key] => values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Full-page URL with the given changes; changing anything but the page returns to page one.</summary>
    public string Href(params (string Key, object? Value)[] changes) => Build(pagePath, changes);

    public string Table(params (string Key, object? Value)[] changes) => Build(tablePath, changes);

    private string Build(string path, (string Key, object? Value)[] changes)
    {
        var merged = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in changes) merged[key] = Text(value);
        if (changes.Any(x => !x.Key.Equals("page", StringComparison.OrdinalIgnoreCase))) merged.Remove("page");
        var query = string.Join("&", merged.Where(x => !string.IsNullOrWhiteSpace(x.Value) && !(x.Key == "page" && x.Value == "1"))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}

public sealed record ListTab(string Label, int? Count, string View, bool Active);

/// <summary>A counter tile; <see cref="Tone"/> is green, blue, amber or red. A tile with a view filters the list.</summary>
public sealed record ListTile(string Label, int Value, string Hint, string Icon, string Tone, string? View, bool Active);

public sealed record ListTabsModel(ListState State, IReadOnlyList<ListTab> Tabs, string Label);

public sealed record ListTilesModel(ListState State, IReadOnlyList<ListTile> Tiles);

public sealed record ListPagerModel(ListState State, int Page, int PageSize, int TotalCount, int TotalPages, string FormId, string Label)
{
    public static readonly int[] PageSizes = [10, 20, 50, 100];
    public int First => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int Last => Math.Min(TotalCount, Page * PageSize);

    /// <summary>Page numbers around the current page; null marks a gap («…»).</summary>
    public IReadOnlyList<int?> Pages()
    {
        var wanted = new SortedSet<int> { 1, TotalPages, Page, Page - 1, Page + 1 };
        if (Page <= 3) { wanted.Add(2); wanted.Add(3); }
        if (Page >= TotalPages - 2) { wanted.Add(TotalPages - 1); wanted.Add(TotalPages - 2); }
        var pages = new List<int?>();
        var previous = 0;
        foreach (var page in wanted.Where(x => x >= 1 && x <= TotalPages))
        {
            if (page - previous > 1) pages.Add(null);
            pages.Add(page);
            previous = page;
        }
        return pages;
    }
}

/// <summary>Shared formatting for list cells.</summary>
public static class ListFormat
{
    /// <summary>A due time relative to now in Tehran: «۲ روز تأخیر» (danger), «امروز، ۱۴:۳۰» (success), «فردا، ۱۰:۰۰» (info) or a date.</summary>
    public static (string Text, string Tone) Due(DateTimeOffset? atUtc, DateTimeOffset nowUtc)
    {
        if (atUtc is not { } at) return ("—", "muted");
        if (at < nowUtc)
        {
            var late = nowUtc - at;
            return (late.TotalDays >= 1 ? $"{(int)late.TotalDays} روز تأخیر"
                : late.TotalHours >= 1 ? $"{(int)late.TotalHours} ساعت تأخیر" : "کمتر از یک ساعت تأخیر", "danger");
        }
        var days = TehranTime.Today(at).DayNumber - TehranTime.Today(nowUtc).DayNumber;
        return days switch
        {
            0 => ($"امروز، {TehranTime.Clock(at)}", "success"),
            1 => ($"فردا، {TehranTime.Clock(at)}", "info"),
            < 7 => ($"{days} روز دیگر", "neutral"),
            _ => (TehranTime.Date(at), "neutral")
        };
    }

    /// <summary>An icon for a free-text lead source (website, exhibition, referral, call, campaign…).</summary>
    public static string SourceIcon(string? source) => source switch
    {
        null => "i-link",
        _ when source.Contains("وب") || source.Contains("سایت") => "i-globe",
        _ when source.Contains("نمایشگاه") => "i-store",
        _ when source.Contains("معرفی") => "i-users",
        _ when source.Contains("تماس") || source.Contains("تلفن") => "i-phone",
        _ when source.Contains("کمپین") || source.Contains("تبلیغ") => "i-megaphone",
        _ when source.Contains("ایمیل") => "i-mail",
        _ when source.Contains("ورود") => "i-upload",
        _ => "i-link"
    };

    /// <summary>An icon for a free-text next action (call, meeting, email, proposal, otherwise a task).</summary>
    public static string ActionIcon(string? action) => action switch
    {
        null => "i-task",
        _ when action.Contains("تماس") => "i-phone",
        _ when action.Contains("جلسه") || action.Contains("بازدید") || action.Contains("دمو") => "i-calendar",
        _ when action.Contains("ایمیل") || action.Contains("ارسال") => "i-send",
        _ when action.Contains("پیشنهاد") || action.Contains("قیمت") => "i-file",
        _ => "i-task"
    };

    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "؟" : name.Trim()[..1];
}
