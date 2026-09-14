# Jak pracujemy w tym repozytorium (Drugi Set)

Ten plik obowiązuje **wszystkich agentów AI** pracujących nad tym repozytorium — obecnie Claude i Codex. Oba muszą stosować się do tych samych zasad, żeby uniknąć konfliktów i rozjazdu konfiguracji między sesjami.

## 0. Zanim zrobisz cokolwiek: zsynchronizuj się z GitHub

Nad projektem pracuje równolegle więcej niż jeden agent, każdy z osobnej sesji/katalogu roboczego. **Nigdy nie zakładaj, że Twoja lokalna kopia jest aktualna.**

Przed jakąkolwiek zmianą:

```bash
git fetch origin
git status                 # upewnij się, że nie masz niezacommitowanych zmian
git pull --rebase origin <branch-na-którym-pracujesz>
```

Zasady:
- Jeśli `git status` pokazuje rozbieżność z `origin` — najpierw `pull`, dopiero potem edytuj pliki.
- Jeśli pull/rebase zgłasza konflikt — zatrzymaj się i rozwiąż go świadomie. Nigdy `--force push`, chyba że jawnie o to poproszono.
- Commituj i pushuj często, małymi krokami. Niezapushowana zmiana lokalna jest niewidoczna dla drugiego agenta i dla Railway (które deployuje z GitHub, nie z lokalnych plików).
- Po zakończeniu pracy zostaw repo w stanie zapushowanym — nie kończ sesji z lokalnymi, niezacommitowanymi zmianami.

## Kontekst projektu

**Drugi Set** — platforma amatorskiej ligi tenisowej dla graczy po 45. roku życia. Aktualny, pełny opis architektury, stacku technologicznego i statusu wdrożenia znajduje się w zakładce **Przegląd** projektu Asana „Drugi Set": https://app.asana.com/1/1218419123229773/project/1218423722977937 — to tam jest źródło prawdy o bieżącym stanie infrastruktury, nie w tym pliku.

Powiązane repozytoria (bkaczmarek-scas):
- **drugi-set-www** — publiczna strona (onepager), React + Vite + TS.
- **drugi-set-web** — aplikacja webowa dla zawodników (rejestracja, profile).
- **drugi-set-api** (to repo) — backend ASP.NET Core Web API.

Każda decyzja architektoniczna i status wdrożenia są opisane w Asanie — sprawdź tam przed podjęciem nietrywialnych decyzji technicznych. **Żaden agent AI nie dodaje, nie opisuje ani nie aktualizuje zadań (tasków) w Asanie** — jedyna dopuszczalna zmiana w Asanie to aktualizacja zakładki **Przegląd**, i tylko wtedy, gdy zmiana ma charakter architektoniczny.

## Stack tego repozytorium

ASP.NET Core Web API (.NET) + Entity Framework Core + PostgreSQL (Neon). Build: `dotnet build`. Migracje: `dotnet ef migrations add <Nazwa>` / `dotnet ef database update`. Wymagane zmienne środowiskowe opisane w README (sekcja „Wymagane zmienne środowiskowe") — nigdy nie commituj ich wartości. Deploy: Railway (serwis `drugi-set-api`), automatycznie po pushu do `main` — obecnie repo ma tylko szkielet (README/.gitignore), więc build w Railway będzie się nie udawał, dopóki nie pojawi się realny kod aplikacji.

## Konwencje git

- `main` = produkcja (Railway deployuje z tego brancha), `dev` = integracja, nowa praca na `feature/*` odgałęzionym od `dev`.
- PR-y kieruj do `dev` (nie do `main`), z wypełnionym szablonem (`.github/PULL_REQUEST_TEMPLATE.md`).
- Commity: krótki, konkretny opis po polsku (np. „Dodaj endpoint rejestracji gracza").
- Nie commituj `.env`, `appsettings.Development.json`, sekretów ani wygenerowanych plików (`bin`, `obj`) — patrz `.gitignore`.

## Zanim zgłosisz zadanie jako zrobione

1. Sprawdź, że kod się buduje (`dotnet build`), a migracje EF Core działają (`dotnet ef database update` na bazie dev/staging).
2. Zaktualizuj README, jeśli zmiana tego wymaga (w tym listę wymaganych zmiennych środowiskowych).
3. Nie aktualizuj zadań w Asanie. Jeśli zmiana ma charakter architektoniczny, zaktualizuj wyłącznie zakładkę Przegląd (patrz sekcja „Kontekst projektu").
