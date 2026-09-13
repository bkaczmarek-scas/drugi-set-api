using System;
using System.Linq;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrugiSet.Api.Seed;

public static class TestAccountSeeder
{
    public static readonly (string Email, string Password, string Role)[] Accounts =
    {
        ("admin@drugiset.pl", "admin", "Admin"),
        ("zawodnik@drugiset.pl", "zawodnik", "Zawodnik"),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILogger<AppDbContext>>();

        foreach (var role in Accounts.Select(a => a.Role).Distinct())
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        foreach (var (email, password, role) in Accounts)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Nie udało się utworzyć konta testowego {email}: {errors}");
            }

            await userManager.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded test account {Email}", email);
        }
    }
}
