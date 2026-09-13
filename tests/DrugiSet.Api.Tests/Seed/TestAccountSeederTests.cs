using System;
using System.Linq;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DrugiSet.Api.Tests.Seed;

public class TestAccountSeederTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 4;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedAsync_CreatesBothAccountsWithCorrectRoles()
    {
        using var provider = BuildServices();

        await TestAccountSeeder.SeedAsync(provider);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var admin = await userManager.FindByEmailAsync("admin@drugiset.pl");
        Assert.NotNull(admin);
        Assert.Contains("Admin", await userManager.GetRolesAsync(admin!));

        var zawodnik = await userManager.FindByEmailAsync("zawodnik@drugiset.pl");
        Assert.NotNull(zawodnik);
        Assert.Contains("Zawodnik", await userManager.GetRolesAsync(zawodnik!));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotentOnSecondRun()
    {
        using var provider = BuildServices();

        await TestAccountSeeder.SeedAsync(provider);
        await TestAccountSeeder.SeedAsync(provider);

        var dbContext = provider.GetRequiredService<AppDbContext>();
        var count = await dbContext.Users.CountAsync(u => u.Email == "admin@drugiset.pl");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SeedAsync_PasswordsAreVerifiable()
    {
        using var provider = BuildServices();

        await TestAccountSeeder.SeedAsync(provider);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@drugiset.pl");

        Assert.True(await userManager.CheckPasswordAsync(admin!, "admin"));
    }
}
