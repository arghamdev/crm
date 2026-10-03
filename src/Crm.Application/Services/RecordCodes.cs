using System.Globalization;

namespace Crm.Application.Services;

/// <summary>
/// Generates human-readable record codes as "highest existing number + 1" for a prefix. Unlike
/// "row count + offset" this cannot reuse a code after rows are removed or merged, or collide with
/// seeded/imported codes. Codes are unique across companies, which also satisfies the per-company
/// unique indexes. Two concurrent SQL writers can still pick the same next number; the unique index
/// rejects the loser and the EF store retries that write against fresh data.
/// </summary>
public static class RecordCodes
{
    public static string Next(IEnumerable<string> existingCodes, string prefix, int firstNumber, int minDigits)
    {
        var highest = firstNumber - 1;
        foreach (var code in existingCodes)
        {
            if (!code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > highest)
                highest = number;
        }
        return prefix + (highest + 1).ToString(new string('0', minDigits), CultureInfo.InvariantCulture);
    }
}
