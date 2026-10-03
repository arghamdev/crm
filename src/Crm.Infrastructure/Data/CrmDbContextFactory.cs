using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crm.Infrastructure.Data;

public sealed class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CRM_SQLSERVER_CONNECTION") ??
            "Server=(localdb)\\mssqllocaldb;Database=EnterpriseCrmSample;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new CrmDbContext(options);
    }
}
