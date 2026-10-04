using System.Text;
using Crm.Application.Contracts;

namespace Crm.Web.Presentation;

/// <summary>Drawer form for bulk lead actions; <see cref="Ids"/> are the rows selected in the list.</summary>
public sealed record BulkLeadFormModel(Guid[] Ids, Guid? OwnerUserId, string? Reason, string? NextAction, string? DueDate, string? DueTime, int DueHours = 4);

/// <summary>Drawer form for planning a follow-up task on several accounts.</summary>
public sealed record BulkAccountTaskFormModel(Guid[] Ids, string? Subject, string? DueDate, string? DueTime, Crm.Domain.Accounts.ActivityPriority Priority = Crm.Domain.Accounts.ActivityPriority.Normal);

/// <summary>Outcome of a bulk action or an import when some rows failed.</summary>
public sealed record BulkResultView(string Title, int Succeeded, IReadOnlyList<string> Failures);

/// <summary>
/// Reads the lead import file: a UTF-8 CSV whose first row names the columns (Persian or English):
/// نام سرنخ/name (required), شخص تماس/contact, تلفن/phone, ایمیل/email, منبع/source.
/// </summary>
public static class LeadImportCsv
{
    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["نام سرنخ"] = "name", ["نام"] = "name", ["name"] = "name", ["نام شرکت"] = "name",
        ["شخص تماس"] = "contact", ["contact"] = "contact",
        ["تلفن"] = "phone", ["موبایل"] = "phone", ["phone"] = "phone", ["mobile"] = "phone",
        ["ایمیل"] = "email", ["email"] = "email",
        ["منبع"] = "source", ["source"] = "source"
    };

    public static IReadOnlyList<LeadImportRow> Parse(string text, out string? error)
    {
        error = null;
        var records = Records(text).ToList();
        if (records.Count == 0) { error = "فایل خالی است."; return []; }
        var header = records[0].Fields.Select(x => Columns.TryGetValue(x.Trim().Trim('﻿'), out var key) ? key : "").ToList();
        if (!header.Contains("name")) { error = "ستون «نام سرنخ» در سطر اول فایل پیدا نشد؛ از فایل نمونه استفاده کنید."; return []; }
        string? Field(IReadOnlyList<string> fields, string key)
        {
            var index = header.IndexOf(key);
            return index >= 0 && index < fields.Count && !string.IsNullOrWhiteSpace(fields[index]) ? fields[index].Trim() : null;
        }
        var rows = records.Skip(1).Where(x => x.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            .Select(x => new LeadImportRow(x.Line, Field(x.Fields, "name"), Field(x.Fields, "contact"), Field(x.Fields, "phone"),
                Field(x.Fields, "email"), Field(x.Fields, "source")))
            .ToList();
        if (rows.Count == 0) error = "فایل ردیف داده‌ای ندارد.";
        return rows;
    }

    /// <summary>RFC 4180 records (quoted fields, doubled quotes, line breaks inside quotes) with their starting line number.</summary>
    private static IEnumerable<(int Line, List<string> Fields)> Records(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var start = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }
            switch (c)
            {
                case '"' when field.Length == 0: quoted = true; break;
                case ',' or ';' or '\t': fields.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString()); field.Clear();
                    yield return (start, fields);
                    fields = [];
                    line++;
                    start = line;
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (start, fields);
        }
    }
}
