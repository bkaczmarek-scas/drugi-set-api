# Drugi Set — rejestracja użytkownika z akceptacją administratora i e-mailami przez Resend

Date: 2026-09-16
Status: Zaprojektowane z użytkownikiem, oczekuje na przegląd spec-u przed planem implementacji

## Kontekst

`drugi-set-api` ma dziś tylko `POST /api/auth/login` i `GET /api/auth/me` — oba działające na dwóch kontach zasianych startowo (`admin@drugiset.pl`, `zawodnik@drugiset.pl`). Poprzedni spec (`2026-09-13-api-auth-scaffold-design.md`) świadomie odłożył "self-service registration endpoint" i "Resend email integration" na później — to jest właśnie to zadanie.

`drugi-set-web`'s `AuthPage.tsx` ma już w pełni zwalidowany formularz rejestracji (imię, nazwisko, e-mail, hasło min. 12 znaków, zgoda na regulamin), ale `handleSubmit` dla trybu `register` tylko waliduje i pokazuje na stałe komunikat "Usługa rejestracji jest obecnie niedostępna" — nie ma żadnego wywołania API. Panel administratora (`AdminPlayers.tsx`) ma już gotowy, przetestowany wizualnie widok "Oczekujący na przyjęcie" / "Zawodnicy ligi" z przyciskiem "Przyjmij" i usuwaniem — ale działa wyłącznie na danych przykładowych (`demoAdminPlayers`), bez żadnego realnego API pod spodem.

**Model akceptacji ustalony z użytkownikiem:** to nie jest samoobsługowa weryfikacja e-mail (link potwierdzający). Rejestracja tworzy konto w stanie „oczekujące" — dopiero administrator ręcznie je aktywuje w panelu. Resend obsługuje trzy powiadomienia w tym przepływie (do zgłaszającego po rejestracji, do administratora o nowym zgłoszeniu, do użytkownika po aktywacji), nie weryfikację adresu e-mail.

Konto Resend, zweryfikowana domena i klucz API są już gotowe po stronie użytkownika — ten spec zakłada tylko podanie klucza jako zmiennej środowiskowej, bez kroków zakładania konta.

## Zakres

**W zakresie:**
- `POST /api/auth/register` — tworzy konto `Zawodnik` w stanie nieaktywnym
- Rozszerzenie `ApplicationUser` o `FirstName`, `LastName`, `IsApproved`, `CreatedAtUtc` + migracja EF Core
- Przywrócenie polityki haseł do sensownego minimum (12 znaków, zgodnie z tym, co front już wymusza) — obecne złagodzenie (4 znaki) było jawnie oznaczone jako tymczasowe "do czasu prawdziwej rejestracji"
- `GET /api/admin/players`, `POST /api/admin/players/{id}/approve`, `DELETE /api/admin/players/{id}` (rola Admin)
- Blokada logowania dla `IsApproved=false` z odrębnym komunikatem (403)
- `ResendEmailService` — cienki klient HTTP do Resend REST API, bez zewnętrznego pakietu NuGet
- Trzy e-maile transakcyjne: potwierdzenie zgłoszenia (do zgłaszającego), powiadomienie o nowym zgłoszeniu (do wszystkich userów w roli Admin, pobranych dynamicznie), potwierdzenie aktywacji (do użytkownika) — wszystkie best-effort, błąd wysyłki nigdy nie blokuje odpowiedzi HTTP
- `drugi-set-web`: wpięcie formularza rejestracji pod realne API, nowy ekran potwierdzenia po zgłoszeniu
- `drugi-set-web`: `AdminPlayers.tsx` przechodzi z danych przykładowych na realny fetch/akceptację/usuwanie
- Wspólny `request<T>()` z `postsApi.ts` przenosi się do `apiClient.ts` (eksportowany) — to trzeci konsument tego samego boilerplate'u

**Poza zakresem (świadomie odłożone):**
- Reset/przypomnienie hasła przez Resend — formularz `reminder` w `AuthPage.tsx` zostaje dokładnie taki, jak jest dziś (osobne przyszłe zadanie, ponownie użyje tego samego `ResendEmailService`)
- Rozszerzenie formularza rejestracji o wybór ligi i telefon — `AdminPlayers.tsx` pokazuje te pola jako niedostępne dla realnych zgłoszeń, dopóki formularz ich nie zbiera
- Realny model płatności, przynależności do ligi i historii meczów — te kolumny w panelu admina pokazują stały tekst zamiast danych, bo backend w ogóle nie ma jeszcze takich pojęć
- E-mail o odrzuceniu zgłoszenia (usunięcie oczekującego zgłoszenia jest dziś ciche, bez powiadomienia)
- Zapisywanie zgody na regulamin (`consent`) po stronie backendu — checkbox zostaje wyłącznie bramką frontendową, tak jak dziś
- Lokalizacja komunikatów błędów ASP.NET Identity inna niż "za krótkie hasło" (patrz sekcja "Obsługa błędów")

## Model danych (`drugi-set-api`)

```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

Nowa migracja `AddPlayerProfileAndApproval` (kolejna po `AddPosts`). `TestAccountSeeder.SeedAsync` przy tworzeniu obu kont testowych ustawia dodatkowo `FirstName`/`LastName` (np. "Admin"/"Testowy" i "Zawodnik"/"Testowy"), `IsApproved = true`, `CreatedAtUtc = DateTime.UtcNow` — konta testowe działają dalej bez zmian w zachowaniu.

### Polityka haseł

W `Program.cs`, zamiana obecnego złagodzenia:
```csharp
options.Password.RequireDigit = false;
options.Password.RequireLowercase = true;
options.Password.RequireUppercase = false;
options.Password.RequireNonAlphanumeric = false;
options.Password.RequiredLength = 12;
```
Usuwamy komentarz `// TODO: przywrócić...` — to zadanie go realizuje. Bez wymogu wielkich liter/cyfr/znaku specjalnego, bo `validateRegistrationPassword` we froncie sprawdza tylko długość ≥12 — dodanie dodatkowych wymogów po stronie backendu bez odzwierciedlenia ich we froncie tworzyłoby błędy niewidoczne na etapie walidacji klienckiej. Konta testowe (hasła `admin`/`zawodnik`) nie są tym dotknięte — polityka haseł obowiązuje tylko przy tworzeniu/zmianie hasła, nie przy logowaniu.

## Nowy endpoint: `POST /api/auth/register`

Bez osobnej warstwy serwisu — wzorem `AuthEndpoints.cs`, bezpośrednio na `UserManager<ApplicationUser>` (Auth nie ma dziś własnego "service", więc Players/register też nie powinny wprowadzać tego wzorca bez potrzeby).

Request:
```json
{ "firstName": "Jan", "lastName": "Kowalski", "email": "jan@example.com", "password": "co-najmniej-12-znakow" }
```

Zachowanie:
1. `userManager.FindByEmailAsync(email)` — jeśli istnieje, `400` z tytułem "Konto z tym adresem e-mail już istnieje."
2. `userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email, FirstName, LastName, IsApproved = false, CreatedAtUtc = DateTime.UtcNow, EmailConfirmed = true }, password)` — `EmailConfirmed = true` ustawiane jawnie dla spójności z seederem (Identity nie używa dziś tego pola do niczego — bramką jest własny `IsApproved`, nie mechanizm potwierdzania e-maila z Identity)
3. Błąd `CreateAsync` (w praktyce: za krótkie hasło) → `400` z ogólnym tytułem "Nie udało się utworzyć konta. Sprawdź, czy hasło ma co najmniej 12 znaków." (nie odsyłamy surowych, angielskich opisów błędów Identity — patrz "Poza zakresem")
4. `userManager.AddToRoleAsync(user, "Zawodnik")` — rejestracja zawsze tworzy zawodnika, nigdy admina
5. Best-effort, przez `ResendEmailService` (błędy tylko logowane, nigdy nie zmieniają odpowiedzi HTTP):
   - do `user.Email`: potwierdzenie zgłoszenia
   - do każdego adresu z `userManager.GetUsersInRoleAsync("Admin")`: powiadomienie o nowym zgłoszeniu
6. `201 Created`: `{ "message": "Zgłoszenie zostało przyjęte. Sprawdź e-mail — administrator wkrótce aktywuje Twoje konto." }`

## Zmiana w `POST /api/auth/login`

Po `signInManager.CheckPasswordSignInAsync` zwracającym sukces, przed wystawieniem tokenu:
```csharp
if (!user.IsApproved)
{
    return Results.Problem(
        title: "Twoje konto oczekuje na aktywację przez administratora.",
        statusCode: StatusCodes.Status403Forbidden);
}
```
Rozróżnienie od `401` jest celowe: hasło było poprawne, dostęp jest zablokowany z innego powodu. Ujawnienie tego (zamiast ogólnego komunikatu) jest świadomym wyborem UX — użytkownik musi wiedzieć, że ma czekać, a nie próbować innego hasła. To lekkie zwiększenie ryzyka enumeracji kont (ujawnia, że e-mail istnieje i jest niezatwierdzony) jest spójne z już przyjętym poziomem ryzyka tej platformy (złagodzone hasła, `localStorage` na tokeny, jedno środowisko bazy danych).

## Nowy moduł: `Players/PlayersEndpoints.cs` (rola Admin)

Wzorem `PostsEndpoints.cs` — grupa z `RequireAuthorization(policy => policy.RequireRole("Admin"))`. Wszystkie trzy operacje działają tylko na użytkownikach w roli `Zawodnik` (nigdy Admin, nawet jeśli ktoś poda id administratora) — `404` w przeciwnym razie.

```csharp
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

    // GetPlayers: userManager.GetUsersInRoleAsync("Zawodnik"), sortowanie po CreatedAtUtc malejąco,
    // rzutowanie na AdminPlayerResponse(Id, FirstName, LastName, Email, IsApproved, CreatedAtUtc)

    // ApprovePlayer(id): FindByIdAsync + IsInRoleAsync(user, "Zawodnik") → 404 gdy brak;
    // ustawia IsApproved=true, UpdateAsync, wysyła e-mail aktywacyjny (best-effort), zwraca AdminPlayerResponse

    // DeletePlayer(id): ta sama weryfikacja roli → 404 gdy brak; userManager.DeleteAsync, 204
}
```

`GET /api/admin/players` → `200`:
```json
[{ "id": "<guid>", "firstName": "Jan", "lastName": "Kowalski", "email": "jan@example.com", "isApproved": false, "createdAtUtc": "2026-09-16T10:00:00Z" }]
```

## `Email/ResendEmailService.cs`

Singleton, jak `JwtTokenService` — waliduje konfigurację w konstruktorze (ten sam wzorzec "fail fast"). Czysty `HttpClient` (przez `IHttpClientFactory`) do `POST https://api.resend.com/emails` — Resend nie ma oficjalnego SDK dla .NET, a to pojedynczy endpoint REST, więc dodawanie zależności nie jest uzasadnione.

```csharp
public class ResendEmailService
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
        _fromAddress = configuration["Resend:FromAddress"];
        if (string.IsNullOrWhiteSpace(_fromAddress))
        {
            throw new InvalidOperationException("Konfiguracja 'Resend:FromAddress' jest wymagana.");
        }

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

Rejestracja w `Program.cs`: `builder.Services.AddHttpClient("Resend");` + `builder.Services.AddSingleton<ResendEmailService>();`. `SendAsync` sam łapie własne wyjątki — wywołujący (register/approve) nie potrzebuje own try/catch.

Treści (proste stałe stringi w `Email/EmailTemplates.cs`, bez silnika szablonów — trzy krótkie, statyczne e-maile nie uzasadniają zależności):
1. Do zgłaszającego: „Dziękujemy za zgłoszenie do Drugi Set" / „Cześć {FirstName}, dziękujemy za zgłoszenie do ligi Drugi Set. Administrator wkrótce zweryfikuje zgłoszenie i aktywuje konto — napiszemy, gdy to nastąpi."
2. Do administratora: „Nowe zgłoszenie do ligi: {FirstName} {LastName}" / „Nowe zgłoszenie w Drugi Set: {FirstName} {LastName} ({Email}). Sprawdź je w panelu administratora."
3. Do użytkownika po akceptacji: „Twoje konto Drugi Set jest aktywne" / „Cześć {FirstName}, Twoje konto zostało aktywowane. Możesz się teraz zalogować."

Nowa konfiguracja (`appsettings.json` + README): `Resend:ApiKey`, `Resend:FromAddress` — ustawiane przez użytkownika jako sekrety/zmienne Railway, tak jak `Jwt:Secret`.

## Frontend (`drugi-set-web`)

### Współdzielony `request<T>()`
`postsApi.ts` ma dziś prywatną kopię `request<T>()` (fetch + `ApiError` + parsowanie `problem.title`). To będzie trzeci plik, który tego potrzebuje (`playersApi.ts`, `register` w `apiClient.ts`) — przenosimy `request<T>()` (i wariant bez treści odpowiedzi, użyty w `deletePost`) do `apiClient.ts`, eksportowane, `postsApi.ts` importuje stamtąd zamiast trzymać własną kopię.

### `apiClient.ts`
Nowa funkcja `register(firstName, lastName, email, password): Promise<{ message: string }>` → `POST /api/auth/register`, przez współdzielone `request<T>()`. `login()` dostaje nową gałąź: `response.status === 403` → `throw new ApiError` z komunikatem z ciała odpowiedzi (`problem.title`) zamiast ogólnego.

### Nowy `playersApi.ts` (bliźniaczy do `postsApi.ts`)
```typescript
export type AdminPlayer = {
  id: string; firstName: string; lastName: string; email: string;
  isApproved: boolean; createdAtUtc: string;
};

export async function listAdminPlayers(token: string): Promise<AdminPlayer[]>
export async function approvePlayer(token: string, id: string): Promise<AdminPlayer>
export async function deletePlayer(token: string, id: string): Promise<void>
```

### `AuthPage.tsx`
Tryb `register`: `handleSubmit` woła `apiRegister(...)` zamiast pokazywać stały komunikat "niedostępne". Nowy stan `registered: boolean` — po sukcesie formularz znika, w jego miejscu ekran potwierdzenia ("Zgłoszenie przyjęte — sprawdź e-mail, administrator wkrótce aktywuje Twoje konto."), bez przekierowania (konto jeszcze nieaktywne, nie ma tokenu). Błąd (np. zajęty e-mail) pokazuje się w istniejącym miejscu `.auth-status`, dokładnie jak dziś dla logowania.

Tryb `login`: zero zmian potrzebnych — istniejący `catch (error) { error instanceof ApiError ? error.message : ... }` już pokaże nowy komunikat 403 bez modyfikacji.

### `AdminPlayers.tsx`
Przechodzi z `useState(demoAdminPlayers)` na realny fetch, wzorem `AdminPosts.tsx` (`useAuth()` po token, `useState<AdminPlayer[] | null>(null)`, `refresh()` w `useEffect`, stan `error` z `role="alert"`, "Wczytywanie…" na czas ładowania). `accept()` woła `approvePlayer` + `refresh()`. `remove()` woła `deletePlayer` + `refresh()`. Usunięta notatka "Podgląd · dane przykładowe".

Kolumny bez realnego pokrycia w backendzie pokazują stały tekst zamiast danych demo:
- Liga / wybrana liga → „Nie podano" (rejestracja jej nie zbiera — patrz "Poza zakresem")
- Status płatności → „Brak danych" (pojęcie płatności nie istnieje jeszcze w backendzie)
- Historia meczów w podglądzie zawodnika → istniejący pusty stan „Ten zawodnik nie rozegrał jeszcze żadnego meczu" (każdy realny użytkownik ma zero meczów, więc to naturalnie zawsze prawda)
- Kolumna „Nr / ID" pokazuje realny GUID zamiast kosmetycznego „DS-001" — widoczna, oczekiwana zmiana wizualna

## Obsługa błędów

- Rejestracja: `400` przy zajętym e-mailu (konkretny komunikat) lub złym haśle (ogólny komunikat o min. 12 znakach); błąd sieci/inny → ogólny fallback (istniejący wzorzec `ApiError`)
- Logowanie: `401` bez zmian dla złych danych; nowy `403` dla niezatwierdzonego konta
- Endpointy admina: istniejący wzorzec `RequireAuthorization(policy => policy.RequireRole("Admin"))`, bez nowej infrastruktury
- Błędy Resend nigdy nie trafiają do wywołującego — tylko log po stronie serwera; rejestracja/akceptacja kończą się sukcesem nawet gdy Resend akurat nie odpowiada
- Błędy walidacji hasła z ASP.NET Identity nie są tłumaczone na polski indywidualnie — jeden ogólny komunikat zamiast nich (patrz "Poza zakresem")

## Plan testów

**API (xUnit + EF Core InMemory, TDD — wzorem `TestAccountSeederTests`):**
1. Rejestracja: tworzy użytkownika z `IsApproved=false`, rolą `Zawodnik`, poprawnymi polami; duplikat e-maila odrzucony (400); hasło <12 znaków odrzucone (400)
2. Logowanie: niezatwierdzone konto → 403 z oczekiwanym komunikatem; zatwierdzone konto → 200 z tokenem (regresja istniejącego zachowania)
3. Approve: `IsApproved` się zmienia; 404 dla nieistniejącego id; 404 dla id administratora
4. Delete: użytkownik usunięty; 404 dla nieistniejącego id; 404 dla id administratora
5. `ResendEmailService`: zamockowany `HttpMessageHandler` — weryfikacja kształtu żądania (nagłówek `Authorization`, treść JSON); nie-2xx odpowiedź nie przerywa działania wywołującego kodu

**Web (Vitest + RTL, wzorem `auth.test.tsx`/`panels.test.tsx`):**
1. Rejestracja: sukces pokazuje ekran potwierdzenia, nie przekierowuje; zajęty e-mail pokazuje komunikat; istniejące testy walidacji klienckiej bez zmian
2. Logowanie: odpowiedź 403 pokazuje komunikat o oczekiwaniu na aktywację (analogicznie do istniejącego testu dla 401)
3. `AdminPlayers`: stan ładowania → podział na oczekujące/aktywne z zamockowanej odpowiedzi; "Przyjmij" woła approve i przenosi wiersz; usuwanie woła delete

**Weryfikacja manualna (analogicznie do poprzednich speców):** build API, migracja na Neon **production**, rejestracja realnego konta testowego (curl lub UI), potwierdzenie że oba e-maile (zgłaszający + admin) faktycznie dochodzą przez Resend, logowanie jako admin, akceptacja w realnym panelu, potwierdzenie e-maila aktywacyjnego, logowanie nowo zatwierdzonym kontem.

## Dokumentacja

README (`drugi-set-api`) wymaga aktualizacji: tabela zmiennych środowiskowych (`Resend__ApiKey`, `Resend__FromAddress`), lista endpointów (4 nowe), sekcja "Konta testowe" (polityka haseł nie jest już tymczasowo złagodzona — usunąć to zastrzeżenie).

## Dostarczenie (dwa repozytoria)

Zgodnie z aktualną konwencją obu `AGENTS.md` (tymczasowo od 2026-09-14): **praca wprost na `main`, bez branchy/PR**, małe commity, komunikaty po polsku, `git fetch` + `git pull --rebase origin main` przed startem i przed każdym pushem w każdym repo osobno.

- `drugi-set-api`: większość zmian (model danych, migracja, 3 nowe endpointy, zmiana logowania, `ResendEmailService`)
- `drugi-set-web`: wpięcie formularza rejestracji i panelu admina pod realne API
- Migracja EF Core idzie na Neon **production** (jedyne środowisko, jak dziś) — to realna zmiana schematu na bazie produkcyjnej, wymaga wyraźnego potwierdzenia przed odpaleniem, nie tylko przed pushem
- Żadne push na `main` (który auto-deployuje przez Railway w obu repo) nie następuje bez wyraźnej zgody użytkownika — zgodnie z ustaloną praktyką tej sesji
- Po każdym pushu: weryfikacja realnego stanu deploya w Railway (`list-deployments`), nie tylko zielonego builda lokalnie

## Otwarte pytania / założenia do potwierdzenia przy przeglądzie spec-u

- Dokładny adres nadawcy Resend (`Resend:FromAddress`, np. `no-reply@drugiset.pl`) — użytkownik poda właściwą wartość przy konfiguracji sekretów, spec nie zakłada konkretnego stringa
- Treści e-maili powyżej są roboczą propozycją — do ewentualnej korekty tonu/treści bez wpływu na architekturę

## Uwagi po przeglądzie (2026-09-16)

Brak — spec nie był jeszcze przeglądany przez użytkownika po napisaniu.
