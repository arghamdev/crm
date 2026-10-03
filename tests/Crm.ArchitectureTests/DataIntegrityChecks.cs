using Crm.Application.Services;

internal static class DataIntegrityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(RecordCodes.Next([], "CUS-", 481, 5) == "CUS-00481", "CODES: an empty set starts at the configured first number.");
        check(RecordCodes.Next(["CUS-00481", "CUS-00601", "CUS-00479"], "CUS-", 481, 5) == "CUS-00602",
            "CODES: next code follows the highest existing number, not the row count (no collision with seeded CUS-00601).");
        check(RecordCodes.Next(["LD-1405-150", "LD-2026-119", "LD-2026-abc"], "LD-2026-", 118, 3) == "LD-2026-120",
            "CODES: other prefixes and malformed suffixes are ignored.");
        check(RecordCodes.Next(["OP-2041", "OP-2026-2041"], "OP-", 2041, 4) == "OP-2042",
            "CODES: a longer prefix sharing the start does not corrupt the sequence.");
        var codes = new List<string>();
        for (var i = 0; i < 5; i++) codes.Add(RecordCodes.Next(codes, "OR-2026-", 1, 3));
        check(codes.Distinct().Count() == 5 && codes[^1] == "OR-2026-005", "CODES: successive codes are unique and increasing.");
        var afterRemoval = codes.Where(x => x != "OR-2026-003").ToList();
        check(RecordCodes.Next(afterRemoval, "OR-2026-", 1, 3) == "OR-2026-006", "CODES: removing a row never makes a used code available again.");
    }
}
