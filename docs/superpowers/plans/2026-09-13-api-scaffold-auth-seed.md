# Drugi Set API — Scaffold, Auth Foundation & Test Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold the `drugi-set-api` ASP.NET Core Web API from nothing, add EF Core + Identity + JWT auth, and seed two working test accounts (`admin@drugiset.pl` / `zawodnik@drugiset.pl`) on the Neon `production` branch (the platform's single environment for now).

**Architecture:** Single ASP.NET Core Web API project (minimal APIs, no controllers, no layering) backed by EF Core over Npgsql, using ASP.NET Core Identity for user/role storage and JWT bearer tokens for stateless auth. An idempotent startup seeder creates the two test accounts on every boot, unconditionally — there's only one environment and no real users yet, so an environment gate would just block the seeder from ever running.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, xUnit + EF Core InMemory provider for tests.

**Spec:** [docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md](../specs/2026-09-13-api-auth-scaffold-design.md)

## Global Constraints

- Target framework: **.NET 10** (`net10.0`)
- Work happens on the already-created branch `feature/api-scaffold-auth-seed` (off `dev`) — do not create a new branch, do not push directly to `dev` or `main`
- Password policy (temporary, until real registration exists): `RequireDigit=false`, `RequireLowercase=true`, `RequireUppercase=false`, `RequireNonAlphanumeric=false`, `RequiredLength=4` — marked with a `TODO` comment pointing at the spec
- Roles are exactly `Admin` and `Zawodnik` (this casing)
- Seeded accounts are exactly: `admin@drugiset.pl` / `admin` → role `Admin`; `zawodnik@drugiset.pl` / `zawodnik` → role `Zawodnik`
- Database target: Neon project `drugi-set` (id `lingering-frost-42394754`), branch **`production`** (id `br-mute-frost-b2iighrm`) — single environment for now, no separate staging DB; the existing `staging` branch stays unused (not deleted)
- Seeder has no environment gate (no `IsProduction()` check) — runs on every boot, by design, until a real staging/production split exists (see spec's "Database / Neon" section)
- JWT: HMAC-SHA256, issuer `drugi-set-api`, audience `drugi-set-web`, 8-hour expiry, signing key from config key `Jwt:Secret` (env var `Jwt__Secret`, already documented in README) — no new required env var beyond what's already documented
- No changes to `drugi-set-web` or Railway configuration in this plan
- All secrets (`ConnectionStrings:DefaultConnection`, `Jwt:Secret`) go through `dotnet user-secrets` locally — never into a committed file

---

### Task 1: Solution & Web API scaffold with a health endpoint

**Files:**
- Create: `DrugiSet.Api.sln`
- Create: `src/DrugiSet.Api/DrugiSet.Api.csproj` (+ template-generated `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`)
- Create: `tests/DrugiSet.Api.Tests/DrugiSet.Api.Tests.csproj`
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)
- Delete: `src/DrugiSet.Api/WeatherForecast.cs` (template sample, if generated)
- Delete: `tests/DrugiSet.Api.Tests/UnitTest1.cs` (template sample)

**Interfaces:**
- Produces: a running app with `GET /health` → `200 { "status": "Healthy" }`. Later tasks build on this `Program.cs`.

- [ ] **Step 1: Confirm branch**

Run: `git branch --show-current`
Expected: `feature/api-scaffold-auth-seed`

- [ ] **Step 2: Create the solution and Web API project**

```bash
dotnet new sln -n DrugiSet.Api
dotnet new webapi -n DrugiSet.Api -o src/DrugiSet.Api
dotnet sln add src/DrugiSet.Api/DrugiSet.Api.csproj
```

- [ ] **Step 3: Create the test project and wire it up**

```bash
dotnet new xunit -n DrugiSet.Api.Tests -o tests/DrugiSet.Api.Tests
dotnet sln add tests/DrugiSet.Api.Tests/DrugiSet.Api.Tests.csproj
dotnet add tests/DrugiSet.Api.Tests/DrugiSet.Api.Tests.csproj reference src/DrugiSet.Api/DrugiSet.Api.csproj
```

- [ ] **Step 4: Remove template sample files**

```bash
rm -f src/DrugiSet.Api/WeatherForecast.cs
rm -f tests/DrugiSet.Api.Tests/UnitTest1.cs
```

Leave whatever OpenAPI/Swagger scaffolding the `webapi` template generated (e.g. an `AddOpenApi`/`MapOpenApi` call) exactly as generated — it's dev-only tooling, not something this plan needs to control.

- [ ] **Step 5: Replace Program.cs**

Open `src/DrugiSet.Api/Program.cs` and replace its entire contents with:

```csharp
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
```

(If the template added OpenAPI lines you want to keep, re-add them around this — but the plan does not depend on them.)

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 7: Run and verify the health endpoint**

```bash
ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5
curl -s http://localhost:5080/health
kill $API_PID
```

Expected curl output: `{"status":"Healthy"}`

- [ ] **Step 8: Commit**

```bash
git add DrugiSet.Api.sln src/DrugiSet.Api tests/DrugiSet.Api.Tests
git commit -m "Szkielet ASP.NET Core Web API + projekt testowy + /health"
```

---

### Task 2: Data model (ApplicationUser, AppDbContext) + Identity registration

**Files:**
- Modify: `src/DrugiSet.Api/DrugiSet.Api.csproj` (add packages)
- Create: `src/DrugiSet.Api/Data/ApplicationUser.cs`
- Create: `src/DrugiSet.Api/Data/AppDbContext.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)
- Modify: `src/DrugiSet.Api/appsettings.json` (add `ConnectionStrings` section)

**Interfaces:**
- Consumes: `Program.cs` from Task 1.
- Produces: `DrugiSet.Api.Data.ApplicationUser` (`IdentityUser<Guid>`) and `DrugiSet.Api.Data.AppDbContext` (`IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`), both registered in DI. Task 3 (migrations), Task 5 (seeder) and Task 6 (endpoints) depend on these exact type names.

- [ ] **Step 1: Add NuGet packages**

```bash
dotnet add src/DrugiSet.Api/DrugiSet.Api.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/DrugiSet.Api/DrugiSet.Api.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/DrugiSet.Api/DrugiSet.Api.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

- [ ] **Step 2: Create ApplicationUser**

Create `src/DrugiSet.Api/Data/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Data;

public class ApplicationUser : IdentityUser<Guid>
{
}
```

- [ ] **Step 3: Create AppDbContext**

Create `src/DrugiSet.Api/Data/AppDbContext.cs`:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DrugiSet.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
```

- [ ] **Step 4: Add ConnectionStrings section to appsettings.json**

Open `src/DrugiSet.Api/appsettings.json`. It currently looks like:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

Replace it with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

(If the generated file differs from the snippet above, keep its existing `Logging`/`AllowedHosts` values as they are and only add the `ConnectionStrings` key.)

- [ ] **Step 5: Register DbContext and Identity in Program.cs**

Replace the entire contents of `src/DrugiSet.Api/Program.cs` with:

```csharp
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // TODO: przywrócić domyślną politykę haseł (wielka litera/cyfra/znak specjalny)
    // przed wdrożeniem prawdziwej rejestracji — patrz docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 7: Run and verify /health still works (no live DB needed yet)**

```bash
ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5
curl -s http://localhost:5080/health
kill $API_PID
```

Expected: `{"status":"Healthy"}` — confirms DI wiring for DbContext/Identity doesn't crash startup even with an empty connection string (EF Core connects lazily).

- [ ] **Step 8: Commit**

```bash
git add src/DrugiSet.Api
git commit -m "Dodaj ApplicationUser/AppDbContext i rejestrację Identity (EF Core)"
```

---

### Task 3: EF Core migration, applied to Neon production

**Files:**
- Create: `src/DrugiSet.Api/Migrations/*` (EF Core generated)
- Modify: `src/DrugiSet.Api/DrugiSet.Api.csproj` (adds `<UserSecretsId>` via `dotnet user-secrets init`)

**Interfaces:**
- Consumes: `AppDbContext` from Task 2.
- Produces: Identity schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, etc.) existing on the Neon `production` branch. Task 5's manual verification and Task 6's manual verification both require this schema to already exist.

- [ ] **Step 1: Get the Neon production connection string**

Use the Neon MCP to fetch a connection string for project `drugi-set` (id `lingering-frost-42394754`), branch `production` (id `br-mute-frost-b2iighrm`). Keep the returned value for the next step — do not paste it into any file that gets committed.

- [ ] **Step 2: Store it in user-secrets**

```bash
dotnet user-secrets init --project src/DrugiSet.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<value from Step 1>" --project src/DrugiSet.Api
```

- [ ] **Step 3: Ensure the dotnet-ef tool is available**

Run: `dotnet ef --version`
If it prints a version, continue. If the command is not found, run:

```bash
dotnet tool install --global dotnet-ef
```

- [ ] **Step 4: Create the initial migration**

```bash
dotnet ef migrations add InitialIdentitySchema --project src/DrugiSet.Api
```

Expected: files created under `src/DrugiSet.Api/Migrations/`.

- [ ] **Step 5: Apply the migration to Neon production**

```bash
dotnet ef database update --project src/DrugiSet.Api
```

Expected: command completes without error (it uses the connection string from user-secrets set in Step 2).

- [ ] **Step 6: Verify the schema on production via Neon MCP**

Use the Neon MCP to run this SQL against project `drugi-set` (`lingering-frost-42394754`), branch `production` (`br-mute-frost-b2iighrm`):

```sql
SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;
```

Expected: includes `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`.

- [ ] **Step 7: Commit the migration files**

```bash
git add src/DrugiSet.Api/Migrations src/DrugiSet.Api/DrugiSet.Api.csproj
git commit -m "Dodaj migrację EF Core (schemat Identity) i zastosuj na Neon production"
```

---

### Task 4: JWT token service (TDD) + JWT bearer authentication

**Files:**
- Modify: `src/DrugiSet.Api/DrugiSet.Api.csproj` (add package)
- Create: `src/DrugiSet.Api/Auth/JwtTokenService.cs`
- Test: `tests/DrugiSet.Api.Tests/Auth/JwtTokenServiceTests.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)
- Modify: `src/DrugiSet.Api/appsettings.json` (document `Jwt:Secret` key)

**Interfaces:**
- Consumes: nothing from earlier tasks (pure service, only needs `IConfiguration`).
- Produces: `DrugiSet.Api.Auth.JwtTokenService` with `(string Token, DateTime ExpiresAtUtc) CreateToken(Guid userId, string email, string role)`, registered as a singleton in DI. Task 6's login endpoint calls this exact method with this exact signature.

- [ ] **Step 1: Add the JWT bearer package**

```bash
dotnet add src/DrugiSet.Api/DrugiSet.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Write the failing test**

Create `tests/DrugiSet.Api.Tests/Auth/JwtTokenServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DrugiSet.Api.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DrugiSet.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(string? secret = "test-secret-at-least-32-characters-long")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
            })
            .Build();

        return new JwtTokenService(configuration);
    }

    [Fact]
    public void CreateToken_EncodesUserIdEmailAndRoleAsClaims()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();

        var (token, _) = service.CreateToken(userId, "admin@drugiset.pl", "Admin");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "admin@drugiset.pl");
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void CreateToken_SetsExpiryToApproximatelyEightHoursFromNow()
    {
        var service = CreateService();

        var (_, expiresAtUtc) = service.CreateToken(Guid.NewGuid(), "admin@drugiset.pl", "Admin");

        var expectedExpiry = DateTime.UtcNow.AddHours(8);
        Assert.True(Math.Abs((expiresAtUtc - expectedExpiry).TotalMinutes) < 1);
    }

    [Fact]
    public void Constructor_ThrowsWhenSecretIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(configuration));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter FullyQualifiedName~JwtTokenServiceTests`
Expected: FAIL — `JwtTokenService` does not exist yet (compile error).

- [ ] **Step 4: Implement JwtTokenService**

Create `src/DrugiSet.Api/Auth/JwtTokenService.cs`:

```csharp
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DrugiSet.Api.Auth;

public class JwtTokenService
{
    private readonly string _secret;

    public JwtTokenService(IConfiguration configuration)
    {
        _secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana.");
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(Guid userId, string email, string role)
    {
        var expiresAtUtc = DateTime.UtcNow.AddHours(8);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "drugi-set-api",
            audience: "drugi-set-web",
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter FullyQualifiedName~JwtTokenServiceTests`
Expected: 3 passed.

- [ ] **Step 6: Wire JWT bearer authentication into Program.cs**

Replace the entire contents of `src/DrugiSet.Api/Program.cs` with:

```csharp
using System.Text;
using DrugiSet.Api.Auth;
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // TODO: przywrócić domyślną politykę haseł (wielka litera/cyfra/znak specjalny)
    // przed wdrożeniem prawdziwej rejestracji — patrz docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana.");

builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Bez tego ASP.NET Core mapuje standardowe krótkie nazwy claimów JWT ("sub", "email")
        // na przestarzałe długie URI (ClaimTypes.NameIdentifier / ClaimTypes.Email), przez co
        // AuthEndpoints.MapAuthEndpoints (który czyta JwtRegisteredClaimNames.Sub/.Email) dostaje null.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "drugi-set-api",
            ValidateAudience = true,
            ValidAudience = "drugi-set-web",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
```

- [ ] **Step 7: Document the Jwt:Secret key in appsettings.json**

Open `src/DrugiSet.Api/appsettings.json` and add a `Jwt` section so the expected shape is documented (value stays empty; the real value comes from user-secrets/env):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "Secret": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 8: Set a local dev JWT secret**

```bash
openssl rand -base64 48
dotnet user-secrets set "Jwt:Secret" "<output of the command above>" --project src/DrugiSet.Api
```

- [ ] **Step 9: Build and verify /health still works with auth middleware in the pipeline**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api --no-launch-profile > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5
curl -s http://localhost:5080/health
kill $API_PID
```

`ASPNETCORE_ENVIRONMENT=Development` is required here (not just cosmetic): ASP.NET Core only loads user-secrets when the environment is `Development`, and from this task onward `Jwt:Secret` must come from user-secrets — without it, `Jwt:Secret` resolves to the empty placeholder in `appsettings.json`, and `SymmetricSecurityKey` throws on the first request (the JWT bearer handler runs for every request once `UseAuthentication()` is in the pipeline, even for endpoints that don't require authorization), so `/health` would 500 instead of returning 200.

Expected: `Build succeeded.`, then `{"status":"Healthy"}` (an unauthenticated endpoint stays reachable after adding auth middleware).

- [ ] **Step 10: Commit**

```bash
git add src/DrugiSet.Api tests/DrugiSet.Api.Tests
git commit -m "Dodaj JwtTokenService (TDD) i uwierzytelnianie JWT bearer"
```

---

### Task 5: Test account seeder (TDD)

**Files:**
- Modify: `tests/DrugiSet.Api.Tests/DrugiSet.Api.Tests.csproj` (add package)
- Create: `src/DrugiSet.Api/Seed/TestAccountSeeder.cs`
- Test: `tests/DrugiSet.Api.Tests/Seed/TestAccountSeederTests.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)

**Interfaces:**
- Consumes: `ApplicationUser`, `AppDbContext` from Task 2.
- Produces: `DrugiSet.Api.Seed.TestAccountSeeder.SeedAsync(IServiceProvider services)` (static, async) and `TestAccountSeeder.Accounts` (the seed data list). Program.cs calls `SeedAsync` directly; no other task depends on this beyond Program.cs wiring.

- [ ] **Step 1: Add the EF Core InMemory package to the test project**

```bash
dotnet add tests/DrugiSet.Api.Tests/DrugiSet.Api.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 2: Write the failing tests**

Create `tests/DrugiSet.Api.Tests/Seed/TestAccountSeederTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter FullyQualifiedName~TestAccountSeederTests`
Expected: FAIL — `TestAccountSeeder` does not exist yet (compile error).

- [ ] **Step 4: Implement TestAccountSeeder**

Create `src/DrugiSet.Api/Seed/TestAccountSeeder.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter FullyQualifiedName~TestAccountSeederTests`
Expected: 3 passed.

- [ ] **Step 6: Wire the seeder into Program.cs**

Replace the entire contents of `src/DrugiSet.Api/Program.cs` with:

```csharp
using System.Text;
using DrugiSet.Api.Auth;
using DrugiSet.Api.Data;
using DrugiSet.Api.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // TODO: przywrócić domyślną politykę haseł (wielka litera/cyfra/znak specjalny)
    // przed wdrożeniem prawdziwej rejestracji — patrz docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana.");

builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Bez tego ASP.NET Core mapuje standardowe krótkie nazwy claimów JWT ("sub", "email")
        // na przestarzałe długie URI (ClaimTypes.NameIdentifier / ClaimTypes.Email), przez co
        // AuthEndpoints.MapAuthEndpoints (który czyta JwtRegisteredClaimNames.Sub/.Email) dostaje null.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "drugi-set-api",
            ValidateAudience = true,
            ValidAudience = "drugi-set-web",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

using (var scope = app.Services.CreateScope())
{
    await TestAccountSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
```

- [ ] **Step 7: Run against Neon production and verify the accounts get created**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5
grep "Seeded test account" /tmp/api.log
kill $API_PID
```

Expected: two log lines, one per seeded email.

- [ ] **Step 8: Verify the rows exist on Neon production via the Neon MCP**

Use the Neon MCP to run this SQL against project `drugi-set` (`lingering-frost-42394754`), branch `production` (`br-mute-frost-b2iighrm`):

```sql
SELECT u."Email", r."Name"
FROM "AspNetUsers" u
JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
ORDER BY u."Email";
```

Expected: two rows — `admin@drugiset.pl` / `Admin` and `zawodnik@drugiset.pl` / `Zawodnik`.

- [ ] **Step 9: Commit**

```bash
git add src/DrugiSet.Api tests/DrugiSet.Api.Tests
git commit -m "Dodaj idempotentny seeder kont testowych (TDD) i uruchom na Neon production"
```

---

### Task 6: Auth endpoints — login and me

**Files:**
- Create: `src/DrugiSet.Api/Auth/AuthEndpoints.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)

**Interfaces:**
- Consumes: `JwtTokenService.CreateToken(Guid userId, string email, string role)` from Task 4; `ApplicationUser`/`AppDbContext` from Task 2; `SignInManager<ApplicationUser>`/`UserManager<ApplicationUser>` registered by Task 2's `AddIdentityCore(...).AddSignInManager()`.
- Produces: `POST /api/auth/login`, `GET /api/auth/me` (final consumers — nothing later in this plan depends on these).

- [ ] **Step 1: Create the auth endpoints**

Create `src/DrugiSet.Api/Auth/AuthEndpoints.cs`:

```csharp
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            JwtTokenService jwtTokenService) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Results.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? string.Empty;
            var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, user.Email!, role);

            return Results.Ok(new LoginResponse(token, expiresAtUtc, user.Email!, role));
        });

        app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
            var role = principal.FindFirstValue(ClaimTypes.Role);

            return Results.Ok(new MeResponse(id!, email!, role!));
        }).RequireAuthorization();
    }
}

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Email, string Role);

public record MeResponse(string Id, string Email, string Role);
```

- [ ] **Step 2: Add ProblemDetails and map the endpoints in Program.cs**

Replace the entire contents of `src/DrugiSet.Api/Program.cs` with:

```csharp
using System.Text;
using DrugiSet.Api.Auth;
using DrugiSet.Api.Data;
using DrugiSet.Api.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // TODO: przywrócić domyślną politykę haseł (wielka litera/cyfra/znak specjalny)
    // przed wdrożeniem prawdziwej rejestracji — patrz docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana.");

builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Bez tego ASP.NET Core mapuje standardowe krótkie nazwy claimów JWT ("sub", "email")
        // na przestarzałe długie URI (ClaimTypes.NameIdentifier / ClaimTypes.Email), przez co
        // AuthEndpoints.MapAuthEndpoints (który czyta JwtRegisteredClaimNames.Sub/.Email) dostaje null.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "drugi-set-api",
            ValidateAudience = true,
            ValidAudience = "drugi-set-web",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapAuthEndpoints();

using (var scope = app.Services.CreateScope())
{
    await TestAccountSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 4: Manually verify login + me for the admin account**

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5

curl -s -X POST http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@drugiset.pl","password":"admin"}'
```

Expected: `200` with a JSON body containing `"role":"Admin"` and a non-empty `"token"`.

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -d '{"email":"admin@drugiset.pl","password":"admin"}' | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
curl -s http://localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
```

Expected: `200` with `"role":"Admin"` and `"email":"admin@drugiset.pl"`.

- [ ] **Step 5: Manually verify login + me for the zawodnik account**

```bash
curl -s -X POST http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"zawodnik@drugiset.pl","password":"zawodnik"}'
```

Expected: `200` with `"role":"Zawodnik"`.

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H "Content-Type: application/json" -d '{"email":"zawodnik@drugiset.pl","password":"zawodnik"}' | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
curl -s http://localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
```

Expected: `200` with `"role":"Zawodnik"`.

- [ ] **Step 6: Verify a wrong password is rejected**

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@drugiset.pl","password":"wrong-password"}'
kill $API_PID
```

Expected: `401`.

- [ ] **Step 7: Commit**

```bash
git add src/DrugiSet.Api
git commit -m "Dodaj endpointy /api/auth/login i /api/auth/me"
```

---

### Task 7: README update, full end-to-end verification, and PR

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: everything from Tasks 1-6. This is the final gate before the PR — no later task depends on it.

- [ ] **Step 1: Update the README**

Open `README.md`. In the "Wymagane zmienne środowiskowe" table, no new rows are needed (`ConnectionStrings__DefaultConnection` and `Jwt__Secret` were already documented). Add a new section right after that table:

```markdown
## Konta testowe (tymczasowe)

Do czasu wdrożenia pełnej rejestracji, przy każdym starcie aplikacji automatycznie tworzone są dwa konta testowe (bez blokady środowiskowej — patrz niżej):

| E-mail | Hasło | Rola |
|---|---|---|
| admin@drugiset.pl | admin | Admin |
| zawodnik@drugiset.pl | zawodnik | Zawodnik |

Polityka haseł jest tymczasowo złagodzona (min. 4 znaki, bez wymogu wielkich liter/cyfr/znaków specjalnych) — patrz `TODO` w `Program.cs` i `docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md`. **Musi zostać zaostrzona przed wdrożeniem prawdziwej rejestracji.**

Aplikacja i baza działają na ten moment w jednym środowisku (branch `production` w Neonie) — bez osobnego stagingu, bo platforma nie ma jeszcze użytkowników, a propagowanie zmian przez dwa środowiska byłoby dziś stratą czasu. **To trzeba zmienić** (prawdziwy split staging/production, ponowna blokada środowiskowa dla seedera) **zanim pojawią się prawdziwi użytkownicy lub prawdziwa rejestracja.**

## API

- `POST /api/auth/login` — `{ "email": string, "password": string }` → `{ "token", "expiresAtUtc", "email", "role" }` (401 przy błędnych danych)
- `GET /api/auth/me` — wymaga `Authorization: Bearer <token>` → `{ "id", "email", "role" }`
- `GET /health` — healthcheck
```

- [ ] **Step 2: Full build and automated test pass**

```bash
dotnet build
dotnet test
```

Expected: `Build succeeded.`, all tests pass (the 3 `JwtTokenServiceTests` + 3 `TestAccountSeederTests` from Tasks 4-5, plus the default template test count of 0 since the sample was deleted).

- [ ] **Step 3: Full end-to-end re-verification**

Repeat Task 6 Steps 4-6 in full (login as admin, `/me` as admin, login as zawodnik, `/me` as zawodnik, wrong password → 401) plus:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5
curl -s http://localhost:5080/health
kill $API_PID
```

Expected: `{"status":"Healthy"}`.

- [ ] **Step 4: Commit the README update**

```bash
git add README.md
git commit -m "Zaktualizuj README: konta testowe i powierzchnia API"
```

- [ ] **Step 5: Push and open the PR**

```bash
git push
gh pr create --base dev --title "Szkielet API: EF Core + Identity + JWT + konta testowe" --body "$(cat <<'EOF'
## Summary
- Szkielet ASP.NET Core Web API (.NET 10, minimal API) z EF Core + Npgsql
- ASP.NET Identity (role Admin/Zawodnik) + JWT bearer auth
- Idempotentny seeder tworzący 2 konta testowe (admin@drugiset.pl, zawodnik@drugiset.pl) przy każdym starcie — jedno środowisko na ten moment, bez blokady Production (platforma nie ma jeszcze użytkowników)
- Endpointy: POST /api/auth/login, GET /api/auth/me, GET /health
- Migracja EF Core zastosowana na branchu production Neona (jedyne środowisko na ten moment; branch staging zostaje nieużywany)
- Spec: docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md

## Test plan
- [x] dotnet build / dotnet test (6 testów: JwtTokenService + TestAccountSeeder)
- [x] Konta zweryfikowane w Neon production (Neon MCP run_sql)
- [x] curl: login + /me dla obu kont, błędne hasło -> 401, /health -> 200

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Do not merge this PR — leave it for review.
