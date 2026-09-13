using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DrugiSet.Api.Data;

/// <summary>
/// Design-time factory used only by EF Core tooling (`dotnet ef migrations add`,
/// `dotnet ef database update`). Bypasses building the full WebApplication host —
/// without this, EF's design-time host resolution can hang (Kestrel actually starts
/// listening instead of the interception short-circuiting it) until its internal
/// 5-minute timeout fires. Not referenced by Program.cs; has no effect at runtime.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<AppDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
