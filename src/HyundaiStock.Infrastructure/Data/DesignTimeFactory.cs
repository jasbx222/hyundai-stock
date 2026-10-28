using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HyundaiStock.Infrastructure.Data;

/// <summary>Used only by dotnet-ef to generate SQL Server migrations. No connection is opened when adding migrations.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("HYST_MIGRATIONS_CONNECTION")
                 ?? "Server=localhost;Database=HyundaiStock;Trusted_Connection=True;TrustServerCertificate=True";
        var o = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cs, s => s.UseCompatibilityLevel(120).MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(o);
    }
}
