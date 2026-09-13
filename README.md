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
