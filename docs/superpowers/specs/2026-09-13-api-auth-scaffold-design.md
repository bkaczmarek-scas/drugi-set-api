# Drugi Set API — scaffold + auth foundation + test accounts

Date: 2026-09-13
Status: Approved by user; revised 2026-09-13 to single-environment scope (see "Database / Neon"); implementation plan written

## Context

`drugi-set-api` currently contains only `README.md`, `AGENTS.md`, `CLAUDE.md`, and `.gitignore` — no application code. The other two repos in the platform (`drugi-set-www`, `drugi-set-web`) are live; `drugi-set-web` in particular ships login/register/password-reminder forms that are intentionally stubbed (`AuthPage.tsx` shows a static "usługa niedostępna" message on submit — there is no fetch call, so no API contract is assumed yet).

Before building the full self-service registration and password-recovery flow, the user needs two working test accounts to unblock manual testing of the panel app:

- `admin@drugiset.pl` / `admin` (role: Admin)
- `zawodnik@drugiset.pl` / `zawodnik` (role: Zawodnik)

Seeded accounts are only useful if there's a way to actually authenticate with them, so this scaffold includes the minimal auth surface needed to prove the accounts work end-to-end — without building registration or password reset.

## Scope

**In scope:**
- ASP.NET Core Web API project scaffold (.NET 10, minimal API style)
- EF Core + Npgsql provider, connected to the Neon `drugi-set` project
- ASP.NET Core Identity (`ApplicationUser`, two roles: `Admin`, `Zawodnik`)
- JWT bearer authentication
- `POST /api/auth/login`, `GET /api/auth/me` (protected), `GET /health`
- Idempotent startup seeder that creates the two test accounts on every boot
- EF Core initial migration applied to the Neon **`production`** branch (single environment for now — see "Database / Neon")
- Unit test for the seeder (EF Core InMemory provider), written before the implementation (TDD)
- Manual end-to-end verification (build, migrate, run, login, `/me`, DB check via Neon MCP)
- `feature/api-scaffold-auth-seed` branch off `dev`, pushed, PR opened against `dev`

**Out of scope (explicitly deferred):**
- Self-service registration endpoint
- Password reset / "forgot password" flow, Resend email integration
- Any change to `drugi-set-web` (frontend stays stubbed until this ships and is reviewed)
- Railway environment variable configuration / deployment of this API
- Refresh tokens, email confirmation, lockout policies
- Tightening the password policy (see "Password policy" below) — tracked as a follow-up

## Architecture & stack

- **.NET 10**, ASP.NET Core Web API, minimal API endpoints (no MVC controllers — the surface is small enough that extension-method endpoint groups are simpler and match modern ASP.NET Core idiom)
- **EF Core** with `Npgsql.EntityFrameworkCore.PostgreSQL`
- **ASP.NET Core Identity** (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`) for user/role storage and password hashing — never hand-roll password hashing
- **JWT bearer** (`Microsoft.AspNetCore.Authentication.JwtBearer`) for stateless auth on top of Identity
- **Swashbuckle** (Swagger/OpenAPI, dev-only) — included because `dotnet new webapi` ships it by default and it gives a zero-effort way to manually exercise the endpoints during verification
- Single project — no Clean Architecture layering. The MVP surface (3 endpoints, 2 entities, 1 seeder) doesn't justify it; revisit if/when registration + content delivery land.

### Project layout

```
drugi-set-api/
  DrugiSet.Api.sln
  src/
    DrugiSet.Api/
      DrugiSet.Api.csproj
      Program.cs
      appsettings.json
      Data/
        ApplicationUser.cs        # IdentityUser<Guid>, no extra profile fields yet
        AppDbContext.cs           # IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
      Auth/
        AuthEndpoints.cs          # maps POST /api/auth/login, GET /api/auth/me
        JwtTokenService.cs        # issues signed JWTs with sub/email/role claims
      Seed/
        TestAccountSeeder.cs      # idempotent seeder, called from Program.cs
      Migrations/
        ...                       # EF Core generated
  tests/
    DrugiSet.Api.Tests/
      DrugiSet.Api.Tests.csproj
      Seed/
        TestAccountSeederTests.cs
  docs/
    superpowers/specs/
      2026-09-13-api-auth-scaffold-design.md   # this file
```

## Data model

```csharp
public class ApplicationUser : IdentityUser<Guid> { }

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
```

No profile fields (first name, last name, etc.) are added to `ApplicationUser` yet — the frontend's register form collects them, but wiring that up is part of the deferred registration flow. Adding speculative columns now would be designing for work that isn't scoped yet.

Two roles, matching the domain vocabulary already used in the README and frontend ("zawodnik" = player/competitor):
- `Admin`
- `Zawodnik`

## Password policy (temporary relaxation)

The requested passwords (`admin`, `zawodnik`) fail ASP.NET Identity's default `PasswordOptions` (requires uppercase, digit, non-alphanumeric, min length 6). Since there is no real self-service registration yet — the only accounts that can ever be created are these two seeded ones — the global password policy will be relaxed for now:

```csharp
options.Password.RequireDigit = false;
options.Password.RequireLowercase = true;
options.Password.RequireUppercase = false;
options.Password.RequireNonAlphanumeric = false;
options.Password.RequiredLength = 4;
```

This is flagged with a `// TODO` comment at the configuration site and a note in the README's environment-variables section: **this must be tightened before the real registration endpoint ships**, since at that point arbitrary users would be able to pick weak passwords. Passwords are still hashed via Identity's standard `PasswordHasher` — nothing here weakens storage, only the acceptance policy.

## Auth endpoints

### `POST /api/auth/login`
Request:
```json
{ "email": "admin@drugiset.pl", "password": "admin" }
```
Success (200):
```json
{ "token": "<jwt>", "expiresAtUtc": "2026-09-13T18:00:00Z", "email": "admin@drugiset.pl", "role": "Admin" }
```
Failure (401), generic message regardless of whether the email exists (don't leak account existence):
```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2", "title": "Nieprawidłowy e-mail lub hasło.", "status": 401 }
```
Implementation: `UserManager.FindByEmailAsync` + `UserManager.CheckPasswordAsync` (via `SignInManager.CheckPasswordSignInAsync` to get lockout/etc. semantics for free). On success, look up the user's role via `UserManager.GetRolesAsync` (single role expected per seeded account) and issue a JWT via `JwtTokenService`.

### `GET /api/auth/me` (`[Authorize]`)
Reads claims from the validated JWT — no DB round-trip needed.
Success (200):
```json
{ "id": "<guid>", "email": "admin@drugiset.pl", "role": "Admin" }
```
401 if no/invalid token (handled by the JWT bearer middleware itself).

### `GET /health`
Unauthenticated, returns 200 with a trivial body (`{ "status": "Healthy" }`). Cheap smoke-test target and a reasonable target for a future Railway healthcheck.

## JWT

- Claims: `sub` (user id), `email`, `role` (`ClaimTypes.Role`, so `[Authorize(Roles = "Admin")]` works without extra plumbing later)
- Signing: HMAC-SHA256, key from `Jwt:Secret` (maps to the already-documented `Jwt__Secret` env var — no new required env var introduced)
- Issuer/audience: fixed constants in code (`drugi-set-api` / `drugi-set-web`) — not made configurable, since there's no second consumer yet (YAGNI)
- Expiry: 8 hours, fixed constant — refresh tokens are out of scope
- Local dev secret: generated once and stored via `dotnet user-secrets set "Jwt:Secret" "<random 64-char value>"` — never committed

## Seeder

`TestAccountSeeder.SeedAsync(IServiceProvider services)`, called unconditionally from `Program.cs` after `app.Build()` and before `app.Run()` — no environment gate for now. A `Production`/`Development` split only earns its keep once there's a real production environment with real user data to protect; right now there's exactly one environment and no users, so gating would just stop the seeder from ever running against the one database that exists. Revisit this (re-add the gate, or remove the seeder entirely) once a real staging/production split exists — see "Follow-ups".

Behavior:
1. Ensure roles `Admin` and `Zawodnik` exist (`RoleManager.RoleExistsAsync` / `CreateAsync`)
2. For each of the two accounts: `UserManager.FindByEmailAsync` — if missing, `UserManager.CreateAsync(user, password)` then `UserManager.AddToRoleAsync`
3. Idempotent by construction — safe to run on every startup, safe to re-run after a Neon branch reset
4. Logs via `ILogger<TestAccountSeeder>` when an account is created ("Seeded test account {Email}") — never logs the password

This is intentionally a startup hook, not a one-off script, per the user's stated preference: it needs to survive Neon branch resets and let any dev/agent recreate the environment by just running the app.

## Database / Neon

**Single environment for now.** The platform has no real users yet and is still being built, so maintaining separate staging/production Neon branches — and propagating every change through both — is overhead with no current upside. Target the Neon **`production`** branch directly; it's already the default branch on the `drugi-set` project, so this needs no new Neon setup. The `staging` branch (`br-snowy-silence-b2eaf5zi`) that already exists is left alone (not deleted, just unused) in case a real split is wanted later.

- Target: Neon project `drugi-set` (`lingering-frost-42394754`), branch **`production`** (`br-mute-frost-b2iighrm`)
- Connection string obtained via the Neon MCP (`get_connection_string`, default/production branch) and stored locally with `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<value>"` — never written to a committed file (already covered by the existing `.gitignore` entries for `appsettings.Development.json` / `appsettings.*.local.json`, but user-secrets avoids the issue entirely by living outside the repo).
- Migration: `dotnet ef migrations add InitialIdentitySchema`, then `dotnet ef database update` against the production connection string. This creates the standard Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, etc.).

**Before this platform has real users or a real registration flow**, this must be revisited: introduce an actual staging/production split (or at least re-gate the seeder) so weak test-account passwords and startup seeding never touch a database holding real people's data. Tracked in "Follow-ups".

## Error handling

- `AddProblemDetails()` for consistent RFC 9110 problem-details error shapes across the API.
- Login failure is a generic 401 with a Polish user-facing message; no distinction between "unknown email" and "wrong password" in the response (avoids account enumeration).
- Unhandled exceptions fall through to the default developer exception page in `Development` and a generic problem-details response otherwise (standard ASP.NET Core behavior, no custom middleware needed at this scope).

## Testing plan

1. **TDD for the seeder**: write `TestAccountSeederTests` first, against an `AppDbContext` backed by EF Core's InMemory provider (fast, no live DB). Assertions:
   - Running the seeder against an empty DB creates both accounts with the correct roles
   - Running it a second time does not create duplicates
   - The stored password verifies via `UserManager.CheckPasswordAsync` for the plaintext passwords `admin` / `zawodnik`
   Watch it fail (no seeder exists yet), then implement `TestAccountSeeder` to make it pass.
2. `dotnet build` succeeds.
3. `dotnet ef database update` against the Neon production connection string succeeds.
4. `dotnet run` locally (`ASPNETCORE_ENVIRONMENT=Development`) — seeder log lines confirm both accounts created.
5. Verify via Neon MCP `run_sql` against the production branch: `AspNetUsers` has 2 rows with the expected emails; `AspNetUserRoles` joins to the expected role names.
6. `curl POST /api/auth/login` for both accounts → 200 + token; a deliberately wrong password → 401.
7. `curl GET /api/auth/me` with each token → correct `id`/`email`/`role`.
8. `curl GET /health` → 200.

## Git / delivery workflow

- Branch `feature/api-scaffold-auth-seed` off `dev` (repo is currently on `dev`, clean, up to date with `origin/dev`)
- Commit scaffold + seeder + tests + this spec
- Push the branch, open a PR into `dev` (per `AGENTS.md` convention — PRs target `dev`, never `main` directly)
- Do **not** merge the PR or merge `dev` → `main` — that's a separate, explicit decision left to the user (merging to `main` triggers a real Railway deploy attempt, which is a shared-infra action outside this task's scope)

## Follow-ups (not this task)

- Tighten the password policy before the real registration endpoint ships
- Introduce a real staging/production Neon split (and re-gate or remove the seeder) before this platform has real users or a real registration flow
- Wire `drugi-set-web`'s `AuthPage.tsx` to `POST /api/auth/login` once this is reviewed
- Configure `Jwt__Secret` / `ConnectionStrings__DefaultConnection` / `Cors__AllowedOrigins` on Railway when the API is actually deployed there
- Build registration, password reset, and Resend email integration
