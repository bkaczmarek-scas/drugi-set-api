# Drugi Set — API

Backend platformy **Drugi Set** — amatorskiej ligi tenisowej dla graczy po 45. roku życia.

## Zakres (MVP)

- Rejestracja i profile zawodników
- Dostarczanie treści (m.in. aktualności) do [drugi-set-www](https://github.com/bkaczmarek-scas/drugi-set-www)

## Stack

- ASP.NET Core Web API (.NET)
- Entity Framework Core
- PostgreSQL (Neon)

## Powiązane repozytoria

- [drugi-set-www](https://github.com/bkaczmarek-scas/drugi-set-www) — publiczna strona (onepager)
- [drugi-set-web](https://github.com/bkaczmarek-scas/drugi-set-web) — aplikacja webowa dla zawodników

---

_Repozytorium przygotowane pod wdrożenie na [Railway](https://railway.com)._

## Wymagane zmienne środowiskowe

Serwer nie czyta wartości z repo — poniższe zmienne trzeba ustawić w środowisku uruchomieniowym (lokalnie w `appsettings.Development.json` / user-secrets, w Railway jako Variables serwisu):

| Zmienna | Opis |
|---|---|
| `ConnectionStrings__DefaultConnection` | Connection string do bazy PostgreSQL (Neon) |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` |
| `Jwt__Secret` | Sekret do podpisywania tokenów uwierzytelniania (gdy zostanie dodane) |
| `Cors__AllowedOrigins` | Dozwolone originy dla CORS (adresy drugi-set-www i drugi-set-web) |

Lista będzie uzupełniana w miarę rozwoju API.

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
