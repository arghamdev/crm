using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Data;

// Preserve the identifiers already shipped in migration history. New scaffolds use standard 14-digit IDs.
public sealed class CompatibleMigrationsIdGenerator : IMigrationsIdGenerator
{
    private readonly object gate = new();
    private DateTime last;
    public bool IsValidId(string value)
    {
        var separator=value.IndexOf('_');
        return separator is 12 or 14 && value.Length>separator+1 && value.AsSpan(0,separator).ToArray().All(c=>c is >= '0' and <= '9');
    }
    public string GetName(string id) => IsValidId(id) ? id[(id.IndexOf('_')+1)..] : throw new ArgumentException("Invalid migration identifier.",nameof(id));
    public string GenerateId(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Migration name is required.",nameof(name));
        lock(gate) {
            var now=DateTime.UtcNow;
            var next=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute,now.Second,DateTimeKind.Utc);
            if(next<=last)next=last.AddSeconds(1);last=next;
            return next.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture)+"_"+name;
        }
    }
}
