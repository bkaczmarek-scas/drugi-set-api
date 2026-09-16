# Rejestracja użytkownika + akceptacja administratora + Resend (drugi-set-api) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zbudować realny `POST /api/auth/register` (konto tworzone jako nieaktywne), panel akceptacji (`GET/approve/delete /api/admin/players`), blokadę logowania na niezatwierdzone konto, i wysyłkę trzech e-maili transakcyjnych przez Resend — zastępując dzisiejsze dwa endpointy (login/me) i dwa zasiane konta testowe pełnym przepływem rejestracja → oczekiwanie → akceptacja.

**Architecture:** `AuthEndpoints.cs` przechodzi z anonimowych lambd na nazwane metody statyczne (wzorem `PostsEndpoints.cs`), co czyni je bezpośrednio testowalne bez hosta HTTP — to samo dla nowego `Players/PlayersEndpoints.cs`. Wysyłka e-maili jest wydzielona za interfejsem `IEmailSender`, wprowadzonym już w Tasku 2 z tymczasową implementacją tylko-do-logów — dzięki temu rejestracja/akceptacja są w pełni testowalne i działające, zanim w Tasku 4 dojdzie prawdziwy `ResendEmailService` (podmiana jednej rejestracji DI, zero zmian w endpointach).

**Tech Stack:** .NET 10, ASP.NET Core minimal API, EF Core + Npgsql (Neon), ASP.NET Core Identity, xUnit + EF Core InMemory provider.

**Spec:** `docs/superpowers/specs/2026-09-16-user-registration-resend-design.md` (ten plan doprecyzowuje i w jednym miejscu **koryguje** ten spec — patrz "Odstępstwa od spec-u" niżej).

## Odstępstwa od spec-u (znalezione podczas pisania tego planu)

1. **Błąd w spec-ie, naprawiony tutaj:** spec zakładał, że zaostrzenie polityki haseł do 12 znaków "nie dotyczy kont testowych, bo polityka działa tylko przy tworzeniu". To nieprawda dla **tworzenia od zera** — idempotentny seeder na świeżej bazie (reset brancha Neona, nowe środowisko) próbowałby utworzyć `admin`/`admin` i `zawodnik`/`zawodnik` (4-8 znaków) pod nową, 12-znakową polityką i **rzuciłby wyjątkiem, wywalając start aplikacji**. Task 1 niżej naprawia to przez bezpośrednie ustawienie `PasswordHash` i utworzenie konta przeciążeniem `CreateAsync` bez hasła (które nie uruchamia walidacji polityki) — legalna, standardowa technika Identity dla kont zasiewanych.
2. **Doprecyzowanie nieopisane w spec-ie:** wysyłka e-maili jest za interfejsem `IEmailSender` (nie bezpośrednio za `ResendEmailService`), żeby Register/Approve dało się przetestować bez mockowania HTTP w każdym teście. Task 2 wprowadza `IEmailSender` z tymczasową implementacją `LoggingEmailSender` (loguje zamiast wysyłać) — **jeśli Task 2/3 zostaną zapushowane na `main` przed Taskiem 4, prawdziwa rejestracja zadziała, ale bez realnych e-maili (tylko wpis w logach)**. To świadomy, przejściowy stan, nie błąd — zaznacz to użytkownikowi, jeśli pushujesz część tasków bez reszty.

## Global Constraints

- Praca wprost na `main` w `drugi-set-api` — **bez** branchy `feature/*` ani PR-ów (tymczasowa konwencja od 2026-09-14, patrz `AGENTS.md`)
- Przed startem i przed każdym pushem: `git fetch origin` + `git pull --rebase origin main`
- Commity małe, częste, opis krótki i konkretny **po polsku**
- **Nie pushuj na `main` bez wyraźnej zgody użytkownika** — każdy push tam auto-deployuje na Railway produkcyjnie
- **Migracja EF Core na bazę produkcyjną (Task 1) wymaga osobnego, wyraźnego potwierdzenia użytkownika przed uruchomieniem** — to nieodwracalna zmiana schematu na jedynej istniejącej bazie (Neon `production`), nie coś do odpalenia automatycznie w ramach "wykonaj plan"
- Ten plan dotyczy wyłącznie `drugi-set-api`. Kontrakt JSON, który mają konsumować `drugi-set-web`, jest już ustalony (patrz `docs/superpowers/plans/2026-09-16-user-registration-resend-web.md` w repo `drugi-set-web`) — nie zmieniaj kształtu odpowiedzi bez świadomości, że to zerwie już napisany front
- Przed zamknięciem każdego taska: `dotnet build` musi przejść czysto, `dotnet test` musi przejść w całości
- Nie commituj `appsettings.Development.json`, `bin/`, `obj/`, sekretów

---

### Task 1: Model danych — profil zawodnika, status akceptacji, migracja, naprawa seedera

**Files:**
- Modify: `src/DrugiSet.Api/Data/ApplicationUser.cs`
- Modify: `src/DrugiSet.Api/Seed/TestAccountSeeder.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (sekcja polityki haseł)
- Modify: `tests/DrugiSet.Api.Tests/Seed/TestAccountSeederTests.cs`
- Create: migracja EF Core (`dotnet ef migrations add`)

**Interfaces:**
- Produces: `ApplicationUser.FirstName: string`, `.LastName: string`, `.IsApproved: bool`, `.CreatedAtUtc: DateTime` — używane przez Task 2 (Register/Login) i Task 3 (Players)

- [ ] **Step 1: Zaktualizuj `BuildServices()` w `tests/DrugiSet.Api.Tests/Seed/TestAccountSeederTests.cs` do docelowej polityki haseł**

Zmień w prywatnej metodzie `BuildServices()`:
```csharp
                options.Password.RequiredLength = 4;
```
na:
```csharp
                options.Password.RequiredLength = 12;
```
(reszta opcji — `RequireDigit = false` itd. — bez zmian)

- [ ] **Step 2: Uruchom testy seedera, potwierdź że teraz nie przechodzą**

Run: `dotnet test --filter FullyQualifiedName~TestAccountSeederTests`
Expected: FAIL — `CreateAsync(user, "admin")` odrzuca hasło jako za krótkie pod nową polityką (seeder jeszcze nie naprawiony)

- [ ] **Step 3: Dodaj pola do `ApplicationUser`**

```csharp
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

- [ ] **Step 4: Napraw `TestAccountSeeder.cs` — ustaw hash hasła bezpośrednio, pomijając politykę**

Zastąp całą zawartość pliku:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DrugiSet.Api.Seed;

public class TestAccountSeeder
{
    public static readonly (string Email, string Password, string Role, string FirstName, string LastName)[] Accounts =
    {
        ("admin@drugiset.pl", "admin", "Admin", "Admin", "Testowy"),
        ("zawodnik@drugiset.pl", "zawodnik", "Zawodnik", "Zawodnik", "Testowy"),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILogger<TestAccountSeeder>>();

        foreach (var role in Accounts.Select(a => a.Role).Distinct())
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        foreach (var (email, password, role, firstName, lastName) in Accounts)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                IsApproved = true,
                CreatedAtUtc = DateTime.UtcNow,
            };
            // Konta testowe celowo mają hasła krótsze niż realna polityka (min. 12 znaków,
            // patrz Program.cs) — CreateAsync(user, password) by je odrzucił. Ustawiamy hash
            // bezpośrednio i tworzymy przeciążeniem bez hasła, które nie uruchamia walidatorów.
            user.PasswordHash = userManager.PasswordHasher.HashPassword(user, password);
            var createResult = await userManager.CreateAsync(user);
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

- [ ] **Step 5: Zaostrz politykę haseł w `Program.cs`**

Zamień:
```csharp
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
```
na:
```csharp
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    // Min. 12 znaków, bez wymogu wielkiej litery/cyfry/znaku specjalnego — dokładnie to,
    // co front (validateRegistrationPassword) już wymusza po swojej stronie. Rozjazd
    // wymogów front/backend tworzyłby błędy niewidoczne na etapie walidacji klienckiej.
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 12;
})
```

- [ ] **Step 6: Uruchom testy seedera, potwierdź PASS**

Run: `dotnet test --filter FullyQualifiedName~TestAccountSeederTests`
Expected: wszystkie 3 testy PASS (dowód, że seeder działa mimo zaostrzonej polityki)

- [ ] **Step 7: Wygeneruj migrację**

Run:
```bash
dotnet ef migrations add AddPlayerProfileAndApproval --project src/DrugiSet.Api/DrugiSet.Api.csproj --startup-project src/DrugiSet.Api/DrugiSet.Api.csproj
```
Expected: nowe pliki w `src/DrugiSet.Api/Migrations/` dodające kolumny `FirstName`, `LastName`, `IsApproved`, `CreatedAtUtc` do `AspNetUsers`

- [ ] **Step 8: `dotnet build`, potwierdź czysty build**

Run: `dotnet build`
Expected: 0 błędów

- [ ] **Step 9: Commit**

```bash
git add src/DrugiSet.Api/Data/ApplicationUser.cs src/DrugiSet.Api/Seed/TestAccountSeeder.cs src/DrugiSet.Api/Program.cs src/DrugiSet.Api/Migrations tests/DrugiSet.Api.Tests/Seed/TestAccountSeederTests.cs
git commit -m "Dodaj profil zawodnika i status akceptacji, zaostrz politykę haseł"
```

- [ ] **Step 10: ⚠️ STOP — migracja na bazę produkcyjną wymaga osobnej zgody**

**Nie uruchamiaj tego kroku automatycznie.** To jedyna istniejąca baza (Neon `production`, brak stagingu) — zmiana schematu tam jest realna i nieodwracalna bez kolejnej migracji. Zapytaj użytkownika o wyraźne potwierdzenie, zanim to zrobisz. Po potwierdzeniu:
1. Uzyskaj connection string: Neon MCP `get_connection_string` dla projektu `drugi-set` (`lingering-frost-42394754`), branch `production` (`br-mute-frost-b2iighrm`), albo poproś użytkownika o wklejenie go
2. `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection-string>"` (w `src/DrugiSet.Api`) — nigdy nie zapisuj tego do commitowanego pliku
3. `dotnet ef database update --project src/DrugiSet.Api/DrugiSet.Api.csproj --startup-project src/DrugiSet.Api/DrugiSet.Api.csproj`
4. Zweryfikuj przez Neon MCP `run_sql`: `SELECT "Email", "FirstName", "IsApproved" FROM "AspNetUsers";` — dwa konta testowe powinny mieć `IsApproved = true` i wypełnione imię/nazwisko

---

### Task 2: `IEmailSender` + `POST /api/auth/register` + refaktor `AuthEndpoints.cs` na metody nazwane + blokada logowania (403)

**Files:**
- Create: `src/DrugiSet.Api/Email/IEmailSender.cs`
- Create: `src/DrugiSet.Api/Email/LoggingEmailSender.cs` (tymczasowy — usuwany w Tasku 4)
- Modify: `src/DrugiSet.Api/Auth/AuthEndpoints.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (rejestracja `IEmailSender`)
- Create: `tests/DrugiSet.Api.Tests/Auth/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `ApplicationUser.FirstName/.LastName/.IsApproved/.CreatedAtUtc` (Task 1)
- Produces: `IEmailSender.SendAsync(string to, string subject, string htmlBody): Task` — konsumowane przez Task 3 (Approve) i zastępowane realną implementacją w Tasku 4; `AuthEndpoints.Register`, `AuthEndpoints.Login`, `AuthEndpoints.Me` jako publiczne metody statyczne — konsumowane tylko przez testy i `MapAuthEndpoints`

- [ ] **Step 1: Napisz `IEmailSender` i `LoggingEmailSender`**

`src/DrugiSet.Api/Email/IEmailSender.cs`:
```csharp
namespace DrugiSet.Api.Email;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody);
}
```

`src/DrugiSet.Api/Email/LoggingEmailSender.cs`:
```csharp
using Microsoft.Extensions.Logging;

namespace DrugiSet.Api.Email;

public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        _logger.LogInformation(
            "E-mail (Resend jeszcze nie podłączony, tylko log) do {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Zarejestruj `IEmailSender` w `Program.cs`**

Dodaj po `builder.Services.AddSingleton<JwtTokenService>();`:
```csharp
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
```
(wymaga `using DrugiSet.Api.Email;` na górze pliku)

- [ ] **Step 3: Napisz nieprzechodzące testy w `tests/DrugiSet.Api.Tests/Auth/AuthEndpointsTests.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DrugiSet.Api.Auth;
using DrugiSet.Api.Data;
using DrugiSet.Api.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DrugiSet.Api.Tests.Auth;

file class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        Sent.Add((to, subject));
        return Task.CompletedTask;
    }
}

public class AuthEndpointsTests
{
    private static (ServiceProvider Provider, FakeEmailSender EmailSender) BuildServices()
    {
        var emailSender = new FakeEmailSender();
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
                options.Password.RequiredLength = 12;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = new string('x', 64) })
                .Build());

        return (services.BuildServiceProvider(), emailSender);
    }

    private static async Task EnsureRoleAsync(ServiceProvider provider, string role)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }

    [Fact]
    public async Task Register_CreatesUnapprovedZawodnikAndSendsTwoEmails()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        await EnsureRoleAsync(provider, "Admin");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var adminUser = new ApplicationUser { UserName = "admin@drugiset.pl", Email = "admin@drugiset.pl", FirstName = "A", LastName = "B", IsApproved = true, CreatedAtUtc = DateTime.UtcNow };
        await userManager.CreateAsync(adminUser, "wystarczajaco-dlugie-haslo");
        await userManager.AddToRoleAsync(adminUser, "Admin");

        var result = await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "wystarczajaco-dlugie-haslo"),
            userManager,
            emailSender);

        Assert.IsType<Created<RegisterResponse>>(result);
        var user = await userManager.FindByEmailAsync("anna@example.com");
        Assert.NotNull(user);
        Assert.False(user!.IsApproved);
        Assert.Equal("Anna", user.FirstName);
        Assert.Contains("Zawodnik", await userManager.GetRolesAsync(user));
        Assert.Equal(2, emailSender.Sent.Count);
        Assert.Contains(emailSender.Sent, e => e.To == "anna@example.com");
        Assert.Contains(emailSender.Sent, e => e.To == "admin@drugiset.pl");
    }

    [Fact]
    public async Task Register_ReturnsBadRequestWhenEmailAlreadyExists()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "wystarczajaco-dlugie-haslo"),
            userManager, emailSender);

        var result = await AuthEndpoints.Register(
            new RegisterRequest("Inna", "Osoba", "anna@example.com", "inne-haslo-tez-dlugie"),
            userManager, emailSender);

        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task Register_ReturnsBadRequestWhenPasswordTooShort()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "krotkie"),
            userManager, emailSender);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task Login_ReturnsForbiddenWhenAccountNotApproved()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "wystarczajaco-dlugie-haslo"),
            userManager, emailSender);

        var result = await AuthEndpoints.Login(
            new LoginRequest("anna@example.com", "wystarczajaco-dlugie-haslo"),
            provider.GetRequiredService<SignInManager<ApplicationUser>>(),
            userManager,
            provider.GetRequiredService<JwtTokenService>());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTokenWhenAccountApproved()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "wystarczajaco-dlugie-haslo"),
            userManager, emailSender);
        var user = await userManager.FindByEmailAsync("anna@example.com");
        user!.IsApproved = true;
        await userManager.UpdateAsync(user);

        var result = await AuthEndpoints.Login(
            new LoginRequest("anna@example.com", "wystarczajaco-dlugie-haslo"),
            provider.GetRequiredService<SignInManager<ApplicationUser>>(),
            userManager,
            provider.GetRequiredService<JwtTokenService>());

        Assert.IsType<Ok<LoginResponse>>(result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorizedForWrongPassword()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await EnsureRoleAsync(provider, "Zawodnik");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await AuthEndpoints.Register(
            new RegisterRequest("Anna", "Kowalska", "anna@example.com", "wystarczajaco-dlugie-haslo"),
            userManager, emailSender);

        var result = await AuthEndpoints.Login(
            new LoginRequest("anna@example.com", "zle-haslo-ale-dlugie"),
            provider.GetRequiredService<SignInManager<ApplicationUser>>(),
            userManager,
            provider.GetRequiredService<JwtTokenService>());

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }
}
```

- [ ] **Step 4: Uruchom testy, potwierdź że nie przechodzą (kompilacja się wysypie — `Register`/`AuthEndpoints.Register` jeszcze nie istnieje w tym kształcie)**

Run: `dotnet test --filter FullyQualifiedName~AuthEndpointsTests`
Expected: FAIL (błąd kompilacji — brak `AuthEndpoints.Register` z sygnaturą przyjmującą `IEmailSender`, `RegisterRequest`/`RegisterResponse` nie istnieją)

- [ ] **Step 5: Zastąp całą zawartość `src/DrugiSet.Api/Auth/AuthEndpoints.cs`**

```csharp
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/register", Register);
        app.MapPost("/api/auth/login", Login);
        app.MapGet("/api/auth/me", Me).RequireAuthorization();
    }

    internal static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender)
    {
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return TypedResults.Problem(title: "Konto z tym adresem e-mail już istnieje.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsApproved = false,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return TypedResults.Problem(
                title: "Nie udało się utworzyć konta. Sprawdź, czy hasło ma co najmniej 12 znaków.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await userManager.AddToRoleAsync(user, "Zawodnik");

        await emailSender.SendAsync(
            user.Email!,
            "Dziękujemy za zgłoszenie do Drugi Set",
            $"<p>Cześć {user.FirstName},</p><p>dziękujemy za zgłoszenie do ligi Drugi Set. Administrator wkrótce zweryfikuje zgłoszenie i aktywuje konto — napiszemy, gdy to nastąpi.</p>");

        var admins = await userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
        {
            if (string.IsNullOrWhiteSpace(admin.Email))
            {
                continue;
            }
            await emailSender.SendAsync(
                admin.Email,
                $"Nowe zgłoszenie do ligi: {user.FirstName} {user.LastName}",
                $"<p>Nowe zgłoszenie w Drugi Set: {user.FirstName} {user.LastName} ({user.Email}). Sprawdź je w panelu administratora.</p>");
        }

        return TypedResults.Created(
            (string?)null,
            new RegisterResponse("Zgłoszenie zostało przyjęte. Sprawdź e-mail — administrator wkrótce aktywuje Twoje konto."));
    }

    internal static async Task<IResult> Login(
        LoginRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokenService)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return TypedResults.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return TypedResults.Problem(title: "Nieprawidłowy e-mail lub hasło.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!user.IsApproved)
        {
            return TypedResults.Problem(
                title: "Twoje konto oczekuje na aktywację przez administratora.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? string.Empty;
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user.Id, user.Email!, role);

        return TypedResults.Ok(new LoginResponse(token, expiresAtUtc, user.Email!, role));
    }

    internal static IResult Me(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        var role = principal.FindFirstValue(ClaimTypes.Role);

        return TypedResults.Ok(new MeResponse(id!, email!, role!));
    }
}

public record RegisterRequest(string FirstName, string LastName, string Email, string Password);

public record RegisterResponse(string Message);

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Email, string Role);

public record MeResponse(string Id, string Email, string Role);
```

Zmiany względem oryginału: `Login`/`Me` przeniesione z anonimowych lambd w `MapAuthEndpoints` na nazwane metody `internal static` (testowalne bezpośrednio, wzorem `PostsEndpoints.cs`), `Results.X` → `TypedResults.X` (spójnie z `PostsEndpoints.cs`), nowy `Register`, nowa gałąź 403 w `Login`.

- [ ] **Step 6: Uruchom testy, potwierdź PASS**

Run: `dotnet test --filter FullyQualifiedName~AuthEndpointsTests`
Expected: wszystkich 6 testów PASS

- [ ] **Step 7: `dotnet build` całego rozwiązania**

Run: `dotnet build`
Expected: 0 błędów (upewnij się, że `Program.cs` z Tasku 1 nadal się kompiluje z nowym `AuthEndpoints.cs`)

- [ ] **Step 8: Commit**

```bash
git add src/DrugiSet.Api/Email/IEmailSender.cs src/DrugiSet.Api/Email/LoggingEmailSender.cs src/DrugiSet.Api/Auth/AuthEndpoints.cs src/DrugiSet.Api/Program.cs tests/DrugiSet.Api.Tests/Auth/AuthEndpointsTests.cs
git commit -m "Dodaj endpoint rejestracji, zablokuj logowanie na niezatwierdzone konto"
```

---

### Task 3: `Players` — lista, akceptacja, usuwanie (rola Admin)

**Files:**
- Create: `src/DrugiSet.Api/Players/PlayersEndpoints.cs`
- Modify: `src/DrugiSet.Api/Program.cs` (wpięcie `MapPlayersEndpoints`)
- Create: `tests/DrugiSet.Api.Tests/Players/PlayersEndpointsTests.cs`

**Interfaces:**
- Consumes: `IEmailSender` (Task 2)
- Produces: `AdminPlayerResponse(Guid Id, string FirstName, string LastName, string Email, bool IsApproved, DateTime CreatedAtUtc)` — kontrakt JSON konsumowany przez `drugi-set-web`'s `AdminPlayerSummary` (plan frontendowy)

- [ ] **Step 1: Napisz nieprzechodzące testy w `tests/DrugiSet.Api.Tests/Players/PlayersEndpointsTests.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Email;
using DrugiSet.Api.Players;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DrugiSet.Api.Tests.Players;

file class FakeEmailSender : IEmailSender
{
    public List<string> SentTo { get; } = new();

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        SentTo.Add(to);
        return Task.CompletedTask;
    }
}

public class PlayersEndpointsTests
{
    private static (ServiceProvider Provider, FakeEmailSender EmailSender) BuildServices()
    {
        var emailSender = new FakeEmailSender();
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
                options.Password.RequiredLength = 12;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton<IEmailSender>(emailSender);

        return (services.BuildServiceProvider(), emailSender);
    }

    private static async Task<ApplicationUser> CreatePlayerAsync(
        ServiceProvider provider, string email, bool isApproved, string role = "Zawodnik")
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Test",
            LastName = "Testowy",
            IsApproved = isApproved,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await userManager.CreateAsync(user, "wystarczajaco-dlugie-haslo");
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    [Fact]
    public async Task GetPlayers_ReturnsOnlyZawodnicySortedNewestFirst()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        await CreatePlayerAsync(provider, "stary@example.com", isApproved: true);
        await Task.Delay(10);
        await CreatePlayerAsync(provider, "nowy@example.com", isApproved: false);
        await CreatePlayerAsync(provider, "admin@example.com", isApproved: true, role: "Admin");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.GetPlayers(userManager);

        var ok = Assert.IsType<Ok<List<AdminPlayerResponse>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("nowy@example.com", ok.Value![0].Email);
    }

    [Fact]
    public async Task ApprovePlayer_SetsIsApprovedTrueAndSendsEmail()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        var player = await CreatePlayerAsync(provider, "anna@example.com", isApproved: false);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.ApprovePlayer(player.Id, userManager, emailSender);

        var ok = Assert.IsType<Ok<AdminPlayerResponse>>(result);
        Assert.True(ok.Value!.IsApproved);
        Assert.Contains("anna@example.com", emailSender.SentTo);
    }

    [Fact]
    public async Task ApprovePlayer_ReturnsNotFoundForAdminId()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        var admin = await CreatePlayerAsync(provider, "admin@example.com", isApproved: true, role: "Admin");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.ApprovePlayer(admin.Id, userManager, emailSender);

        Assert.IsType<NotFound>(result);
        Assert.Empty(emailSender.SentTo);
    }

    [Fact]
    public async Task ApprovePlayer_ReturnsNotFoundForUnknownId()
    {
        var (provider, emailSender) = BuildServices();
        using var _ = provider;
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.ApprovePlayer(Guid.NewGuid(), userManager, emailSender);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task DeletePlayer_RemovesUser()
    {
        var (provider, _) = BuildServices();
        using var _p = provider;
        var player = await CreatePlayerAsync(provider, "anna@example.com", isApproved: true);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.DeletePlayer(player.Id, userManager);

        Assert.IsType<NoContent>(result);
        Assert.Null(await userManager.FindByIdAsync(player.Id.ToString()));
    }

    [Fact]
    public async Task DeletePlayer_ReturnsNotFoundForAdminId()
    {
        var (provider, _) = BuildServices();
        using var _p = provider;
        var admin = await CreatePlayerAsync(provider, "admin@example.com", isApproved: true, role: "Admin");
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await PlayersEndpoints.DeletePlayer(admin.Id, userManager);

        Assert.IsType<NotFound>(result);
        Assert.NotNull(await userManager.FindByIdAsync(admin.Id.ToString()));
    }
}
```

- [ ] **Step 2: Uruchom testy, potwierdź brak modułu `DrugiSet.Api.Players`**

Run: `dotnet test --filter FullyQualifiedName~PlayersEndpointsTests`
Expected: FAIL — błąd kompilacji, `PlayersEndpoints` nie istnieje

- [ ] **Step 3: Napisz `src/DrugiSet.Api/Players/PlayersEndpoints.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DrugiSet.Api.Players;

public static class PlayersEndpoints
{
    public static void MapPlayersEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/players")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        admin.MapGet("/", GetPlayers);
        admin.MapPost("/{id:guid}/approve", ApprovePlayer);
        admin.MapDelete("/{id:guid}", DeletePlayer);
    }

    internal static async Task<IResult> GetPlayers(UserManager<ApplicationUser> userManager)
    {
        var players = await userManager.GetUsersInRoleAsync("Zawodnik");
        var response = players
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(ToResponse)
            .ToList();
        return TypedResults.Ok(response);
    }

    internal static async Task<IResult> ApprovePlayer(
        Guid id, UserManager<ApplicationUser> userManager, IEmailSender emailSender)
    {
        var player = await FindPlayerAsync(userManager, id);
        if (player is null)
        {
            return TypedResults.NotFound();
        }

        player.IsApproved = true;
        await userManager.UpdateAsync(player);

        if (!string.IsNullOrWhiteSpace(player.Email))
        {
            await emailSender.SendAsync(
                player.Email,
                "Twoje konto Drugi Set jest aktywne",
                $"<p>Cześć {player.FirstName},</p><p>Twoje konto zostało aktywowane. Możesz się teraz zalogować.</p>");
        }

        return TypedResults.Ok(ToResponse(player));
    }

    internal static async Task<IResult> DeletePlayer(Guid id, UserManager<ApplicationUser> userManager)
    {
        var player = await FindPlayerAsync(userManager, id);
        if (player is null)
        {
            return TypedResults.NotFound();
        }

        await userManager.DeleteAsync(player);

        return TypedResults.NoContent();
    }

    private static async Task<ApplicationUser?> FindPlayerAsync(UserManager<ApplicationUser> userManager, Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || !await userManager.IsInRoleAsync(user, "Zawodnik"))
        {
            return null;
        }
        return user;
    }

    private static AdminPlayerResponse ToResponse(ApplicationUser user) =>
        new(user.Id, user.FirstName, user.LastName, user.Email!, user.IsApproved, user.CreatedAtUtc);
}

public record AdminPlayerResponse(Guid Id, string FirstName, string LastName, string Email, bool IsApproved, DateTime CreatedAtUtc);
```

- [ ] **Step 4: Wpnij `MapPlayersEndpoints` w `Program.cs`**

Dodaj `using DrugiSet.Api.Players;` na górze, i po `app.MapAuthEndpoints();`:
```csharp
app.MapPlayersEndpoints();
```

- [ ] **Step 5: Uruchom testy, potwierdź PASS**

Run: `dotnet test --filter FullyQualifiedName~PlayersEndpointsTests`
Expected: wszystkich 6 testów PASS

- [ ] **Step 6: Pełny build i pełny zestaw testów**

Run: `dotnet build && dotnet test`
Expected: build czysty, cały zestaw testów PASS (nie tylko nowe pliki)

- [ ] **Step 7: Commit**

```bash
git add src/DrugiSet.Api/Players tests/DrugiSet.Api.Tests/Players src/DrugiSet.Api/Program.cs
git commit -m "Dodaj panel akceptacji zawodników (lista/approve/delete)"
```

---

### Task 4: `ResendEmailService` — prawdziwa wysyłka zamiast logowania

**Files:**
- Create: `src/DrugiSet.Api/Email/ResendEmailService.cs`
- Delete: `src/DrugiSet.Api/Email/LoggingEmailSender.cs` (nie jest już potrzebny)
- Modify: `src/DrugiSet.Api/Program.cs` (podmiana rejestracji DI)
- Modify: `src/DrugiSet.Api/appsettings.json`
- Create: `tests/DrugiSet.Api.Tests/Email/ResendEmailServiceTests.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: nic nowego z wcześniejszych tasków (implementuje `IEmailSender` z Tasku 2, bez zmian w Register/Approve)

- [ ] **Step 1: Napisz nieprzechodzące testy w `tests/DrugiSet.Api.Tests/Email/ResendEmailServiceTests.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DrugiSet.Api.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DrugiSet.Api.Tests.Email;

file class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public FakeHttpMessageHandler(HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode);
    }
}

file class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}

public class ResendEmailServiceTests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resend:ApiKey"] = "test-api-key",
                ["Resend:FromAddress"] = "no-reply@drugiset.pl",
            })
            .Build();

    [Fact]
    public async Task SendAsync_SendsAuthorizedRequestWithExpectedPayload()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var service = new ResendEmailService(
            new FakeHttpClientFactory(handler), BuildConfig(), NullLogger<ResendEmailService>.Instance);

        await service.SendAsync("anna@example.com", "Temat", "<p>Treść</p>");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-api-key", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.Contains("anna@example.com", handler.LastRequestBody);
        Assert.Contains("no-reply@drugiset.pl", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendAsync_DoesNotThrowWhenResendReturnsError()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        var service = new ResendEmailService(
            new FakeHttpClientFactory(handler), BuildConfig(), NullLogger<ResendEmailService>.Instance);

        await service.SendAsync("anna@example.com", "Temat", "<p>Treść</p>");
        // brak wyjątku = test przechodzi
    }

    [Fact]
    public void Constructor_ThrowsWhenApiKeyMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:FromAddress"] = "no-reply@drugiset.pl" })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ResendEmailService(
                new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK)),
                config,
                NullLogger<ResendEmailService>.Instance));
    }

    [Fact]
    public void Constructor_ThrowsWhenFromAddressMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:ApiKey"] = "test-api-key" })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            new ResendEmailService(
                new FakeHttpClientFactory(new FakeHttpMessageHandler(HttpStatusCode.OK)),
                config,
                NullLogger<ResendEmailService>.Instance));
    }
}
```

- [ ] **Step 2: Uruchom testy, potwierdź brak `ResendEmailService`**

Run: `dotnet test --filter FullyQualifiedName~ResendEmailServiceTests`
Expected: FAIL — błąd kompilacji, klasa nie istnieje

- [ ] **Step 3: Napisz `src/DrugiSet.Api/Email/ResendEmailService.cs`**

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DrugiSet.Api.Email;

public class ResendEmailService : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly string _fromAddress;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ResendEmailService> logger)
    {
        var apiKey = configuration["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Konfiguracja 'Resend:ApiKey' jest wymagana.");
        }
        var fromAddress = configuration["Resend:FromAddress"];
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException("Konfiguracja 'Resend:FromAddress' jest wymagana.");
        }
        _fromAddress = fromAddress;

        _httpClient = httpClientFactory.CreateClient("Resend");
        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var payload = new { from = _fromAddress, to = new[] { to }, subject, html = htmlBody };
            var response = await _httpClient.PostAsJsonAsync("emails", payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Resend API zwróciło {StatusCode} przy wysyłce do {To}", response.StatusCode, to);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wysłać e-maila do {To}", to);
        }
    }
}
```

- [ ] **Step 4: Uruchom testy, potwierdź PASS**

Run: `dotnet test --filter FullyQualifiedName~ResendEmailServiceTests`
Expected: wszystkie 4 testy PASS

- [ ] **Step 5: Podmień rejestrację DI w `Program.cs`, usuń `LoggingEmailSender`**

Zamień:
```csharp
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
```
na:
```csharp
builder.Services.AddHttpClient("Resend");
builder.Services.AddSingleton<IEmailSender, ResendEmailService>();
```
Usuń plik `src/DrugiSet.Api/Email/LoggingEmailSender.cs` (`git rm`).

- [ ] **Step 6: Dodaj `Resend` do `appsettings.json`**

```json
  "Resend": {
    "ApiKey": "",
    "FromAddress": ""
  },
```
(wstaw po sekcji `"Uploads"`, przed `"Logging"`)

- [ ] **Step 7: `dotnet build` i pełny `dotnet test`**

Run: `dotnet build && dotnet test`
Expected: build czysty (upewnij się, że nic w `Program.cs` nie odwołuje się już do usuniętego `LoggingEmailSender`), cały zestaw testów PASS

- [ ] **Step 8: Zaktualizuj `README.md`**

W tabeli "Wymagane zmienne środowiskowe", dodaj wiersze:
```
| `Resend__ApiKey` | Klucz API Resend do wysyłki e-maili transakcyjnych |
| `Resend__FromAddress` | Adres nadawcy e-maili (musi być na zweryfikowanej w Resend domenie) |
```

W sekcji "API", dodaj po opisie `POST /api/auth/login`:
```
- `POST /api/auth/register` — `{ "firstName", "lastName", "email", "password" }` → 201 `{ "message" }` (konto tworzone jako nieaktywne; 400 przy zajętym e-mailu lub haśle krótszym niż 12 znaków); logowanie na nieaktywne konto zwraca 403
```
i po opisie endpointów `/api/admin/posts`:
```
- `GET /api/admin/players` — rola Admin → `[{ "id", "firstName", "lastName", "email", "isApproved", "createdAtUtc" }]`
- `POST /api/admin/players/{id}/approve` — rola Admin → aktywuje konto, wysyła e-mail (404 gdy nie istnieje)
- `DELETE /api/admin/players/{id}` — rola Admin → 204 (404 gdy nie istnieje)
```

W sekcji "Konta testowe (tymczasowe)", usuń zdanie:
```
Polityka haseł jest tymczasowo złagodzona (min. 4 znaki, bez wymogu wielkich liter/cyfr/znaków specjalnych) — patrz `TODO` w `Program.cs` i `docs/superpowers/specs/2026-09-13-api-auth-scaffold-design.md`. **Musi zostać zaostrzona przed wdrożeniem prawdziwej rejestracji.**
```
(polityka jest już zaostrzona — ten warunek jest spełniony)

- [ ] **Step 9: Commit**

```bash
git add src/DrugiSet.Api/Email/ResendEmailService.cs src/DrugiSet.Api/Program.cs src/DrugiSet.Api/appsettings.json tests/DrugiSet.Api.Tests/Email/ResendEmailServiceTests.cs README.md
git rm src/DrugiSet.Api/Email/LoggingEmailSender.cs
git commit -m "Podłącz Resend do wysyłki e-maili rejestracyjnych"
```

---

## Po ukończeniu wszystkich tasków

- Nie pushuj na `main` bez wyraźnej zgody użytkownika
- Task 1 Step 10 (migracja produkcyjna) i konfiguracja `Resend__ApiKey`/`Resend__FromAddress` na Railway to osobne, wymagające zgody kroki — nie zakładaj, że "wykonanie planu" obejmuje ich automatyczne uruchomienie
- Po wdrożeniu: zweryfikuj deploy na Railway (`list-deployments`) — zielony build lokalnie nie znaczy, że usługa faktycznie wstała (patrz incydent z modułem aktualności, gdzie brakująca zmienna środowiskowa spowodowała ~10h przestoju niezauważonego bez tej weryfikacji)
- `drugi-set-web` ma już gotowy, samowystarczalny plan frontendowy (`docs/superpowers/plans/2026-09-16-user-registration-resend-web.md` w tamtym repo) zakładający dokładnie ten kontrakt API — po wdrożeniu tego planu warto ręcznie sprawdzić end-to-end (curl + realny front), nie tylko testy jednostkowe
