# CORS for drugi-set-web Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a browser running `drugi-set-web` call `drugi-set-api` cross-origin, so the login form (wired up in a companion `drugi-set-web` plan) can actually reach `POST /api/auth/login`.

**Architecture:** Standard ASP.NET Core CORS middleware (`AddCors`/`UseCors`), origins read from config (`Cors:AllowedOrigins`, already documented in the README, never implemented) as a comma-separated list.

**Tech Stack:** ASP.NET Core (built-in CORS support — no new NuGet package).

**Spec:** [docs/superpowers/specs/2026-09-13-login-integration-design.md (in drugi-set-web)](../../../drugi-set-web/docs/superpowers/specs/2026-09-13-login-integration-design.md) — see "Backend: CORS" section. This plan implements only that section; the rest of the spec is a separate plan in `drugi-set-web`.

## Global Constraints

- No `.AllowCredentials()` — the frontend sends the JWT via an `Authorization` header, not cookies, so CORS credentials mode isn't needed
- Allowed origins (local dev + both real deployments): `http://localhost:5173`, `http://127.0.0.1:5173`, `https://panel.drugiset.pl`, `https://drugi-set-web-production.up.railway.app`
- Config key: `Cors:AllowedOrigins` (env var `Cors__AllowedOrigins`) — already documented in README, this is its first real implementation
- Secrets (the origins list itself isn't secret, but goes through the same `dotnet user-secrets` mechanism as the rest of this repo's local config for consistency) — never into a committed file
- Work happens on a new branch `feature/cors-frontend-origins`, off `dev` — PRs target `dev`, never `main` directly

---

### Task 1: CORS middleware + config

**Files:**
- Modify: `src/DrugiSet.Api/Program.cs` (full replacement)
- Modify: `src/DrugiSet.Api/appsettings.json` (full replacement)

**Interfaces:**
- Consumes: nothing from other tasks (this plan has only one task).
- Produces: a `"Frontend"` named CORS policy applied via `app.UseCors("Frontend")` to every request. No other task in this plan or the companion `drugi-set-web` plan depends on any exported type here — the companion plan's `apiClient.ts` just needs this policy to be live when it calls the API from a browser.

- [ ] **Step 1: Confirm branch**

```bash
git checkout dev
git pull
git checkout -b feature/cors-frontend-origins
```

- [ ] **Step 2: Add the Cors section to appsettings.json**

Replace the entire contents of `src/DrugiSet.Api/appsettings.json` with:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Jwt": {
    "Secret": ""
  },
  "Cors": {
    "AllowedOrigins": ""
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

- [ ] **Step 3: Add CORS registration and middleware to Program.cs**

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

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException("Konfiguracja 'Jwt:Secret' jest wymagana i musi mieć co najmniej 32 bajty.");
}

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

var corsOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseExceptionHandler();

app.UseCors("Frontend");

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

- [ ] **Step 4: Set the local dev origins list**

```bash
dotnet user-secrets set "Cors:AllowedOrigins" "http://localhost:5173,http://127.0.0.1:5173,https://panel.drugiset.pl,https://drugi-set-web-production.up.railway.app" --project src/DrugiSet.Api
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 6: Run and verify CORS headers**

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" dotnet run --project src/DrugiSet.Api --no-launch-profile > /tmp/api.log 2>&1 &
API_PID=$!
sleep 5

echo "--- allowed origin ---"
curl -s -i http://localhost:5080/health -H "Origin: http://localhost:5173" | grep -i "access-control-allow-origin"

echo "--- disallowed origin ---"
curl -s -i http://localhost:5080/health -H "Origin: http://evil.example.com" | grep -i "access-control-allow-origin"

echo "--- preflight for login ---"
curl -s -i -X OPTIONS http://localhost:5080/api/auth/login \
  -H "Origin: http://localhost:5173" \
  -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: Content-Type" \
  | grep -iE "^HTTP|access-control-allow"

kill $API_PID
```

Expected:
- Allowed origin: `Access-Control-Allow-Origin: http://localhost:5173` present
- Disallowed origin: no `Access-Control-Allow-Origin` header at all (empty grep output)
- Preflight: `204` (or `200`) status, `Access-Control-Allow-Origin: http://localhost:5173`, and `Access-Control-Allow-Methods` including `POST`

- [ ] **Step 7: Commit and push**

```bash
git add src/DrugiSet.Api/Program.cs src/DrugiSet.Api/appsettings.json
git commit -m "Dodaj CORS dla drugi-set-web (Cors:AllowedOrigins)"
git push -u origin feature/cors-frontend-origins
```

---

### Delivery

Open a PR into `dev` (not `main`) with `gh pr create --base dev`, following the repo's PR template. Do not merge — leave it for review, same as the rest of this project's convention. This is a self-contained, single-task change — no broader final-branch review is needed beyond the task review already covering it in full.
