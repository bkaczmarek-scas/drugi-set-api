# Moduł aktualności — backend (drugi-set-api) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Post` entity with public read + authorized admin CRUD endpoints, plus an image-upload pipeline (validate → resize/compress → store on a Railway Volume), so `drugi-set-web`'s admin panel can create real news posts and both `drugi-set-web` and `drugi-set-www` can display them.

**Architecture:** New `Posts/` folder mirroring the existing `Auth/` folder's minimal-API pattern (`MapXEndpoints(app)` extension method, no MVC Controllers). Thin, directly-testable static endpoint handlers delegate to two injected services (`PostsService` for CRUD against `AppDbContext`, `ImageUploadService` for validation/processing/storage) — mirrors how `AuthEndpoints.cs` delegates to `JwtTokenService`/`UserManager`. DTOs (records) keep EF Core entities from leaking through the API, matching `AuthEndpoints.cs`'s `LoginRequest`/`LoginResponse` pattern.

**Tech Stack:** ASP.NET Core minimal APIs (.NET 10), EF Core + Npgsql (Neon Postgres), ASP.NET Core Identity/JWT (existing), SixLabors.ImageSharp (new dependency), xUnit + EF Core InMemory provider (existing test stack).

**Spec:** `drugi-set-web` repo, `docs/superpowers/specs/2026-09-14-aktualnosci-design.md` (cross-repo design doc, approved by the user). This plan implements its "Backend: model danych i migracja", "Backend: endpointy", "Backend: upload i przechowywanie zdjęć", and "Backend: CORS" sections.

## Global Constraints

- Wszystkie komunikaty błędów zwracane z API — po polsku (spójne z `AuthEndpoints.cs`).
- Nullable reference types włączone (`<Nullable>enable</Nullable>`) — traktuj ostrzeżenia null jak błędy.
- Wzorzec minimal-API (`MapXEndpoints` extension method) — nie wprowadzaj `Controllers`/`AddControllers()`.
- DTO (record) zawsze pomiędzy encją EF Core a JSON-em — encja nigdy nie wraca bezpośrednio z endpointu.
- Commituj po każdym tasku, bezpośrednio na `main` (branch `dev` zawieszony do odwołania od 2026-09-14) — `git fetch origin` + `git status` przed edycją, `git pull --rebase origin main` jeśli lokalna kopia jest w tyle (patrz `AGENTS.md` sekcja 0).
- Migracje EF Core generowane z katalogu `src/DrugiSet.Api` (`dotnet ef migrations add ...`), stosowane przez `dotnet ef database update` na bazie Neon `production` (single-environment — nie ma osobnego stagingu, patrz README).
- `Cors:AllowedOrigins`, `Jwt:Secret`, `ConnectionStrings:DefaultConnection` — nigdy w repo, tylko user-secrets lokalnie / Railway Variables na produkcji.
- Nowa zależność NuGet: `SixLabors.ImageSharp` (najnowsza stabilna wersja) — dodawana w Tasku 7.

---

## Task 1: Generator slugów (czysta funkcja)

**Files:**
- Create: `src/DrugiSet.Api/Posts/SlugGenerator.cs`
- Test: `tests/DrugiSet.Api.Tests/Posts/SlugGeneratorTests.cs`

**Interfaces:**
- Produces: `static class SlugGenerator { static string Generate(string title); static string MakeUnique(string baseSlug, ISet<string> existingSlugs); }`

- [ ] **Step 1: Napisz failing testy**

```csharp
using System.Collections.Generic;
using DrugiSet.Api.Posts;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class SlugGeneratorTests
{
    [Fact]
    public void Generate_LowercasesAndDashesSpaces()
    {
        Assert.Equal("harmonogram-zimowej-edycji", SlugGenerator.Generate("Harmonogram zimowej edycji"));
    }

    [Fact]
    public void Generate_TransliteratesPolishDiacritics()
    {
        Assert.Equal("mistrz-drugiego-seta-zakonczenie", SlugGenerator.Generate("Mistrz Drugiego Seta — zakończenie"));
    }

    [Fact]
    public void Generate_CollapsesPunctuationIntoSingleDashes()
    {
        Assert.Equal("a-b-c", SlugGenerator.Generate("A!!  B,,, C"));
    }

    [Fact]
    public void Generate_TrimsLeadingAndTrailingDashes()
    {
        Assert.Equal("start-koniec", SlugGenerator.Generate("  Start koniec!  "));
    }

    [Fact]
    public void MakeUnique_ReturnsBaseSlugWhenNoCollision()
    {
        Assert.Equal("nowy-post", SlugGenerator.MakeUnique("nowy-post", new HashSet<string>()));
    }

    [Fact]
    public void MakeUnique_AppendsIncrementingSuffixOnCollision()
    {
        var existing = new HashSet<string> { "nowy-post", "nowy-post-2" };
        Assert.Equal("nowy-post-3", SlugGenerator.MakeUnique("nowy-post", existing));
    }
}
```

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się (SlugGenerator jeszcze nie istnieje)**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter SlugGeneratorTests`
Expected: FAIL (błąd kompilacji — `DrugiSet.Api.Posts.SlugGenerator` nie istnieje)

- [ ] **Step 3: Zaimplementuj SlugGenerator**

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace DrugiSet.Api.Posts;

public static class SlugGenerator
{
    private static readonly Dictionary<char, string> PolishMap = new()
    {
        ['ą'] = "a", ['ć'] = "c", ['ę'] = "e", ['ł'] = "l", ['ń'] = "n",
        ['ó'] = "o", ['ś'] = "s", ['ź'] = "z", ['ż'] = "z",
        ['Ą'] = "a", ['Ć'] = "c", ['Ę'] = "e", ['Ł'] = "l", ['Ń'] = "n",
        ['Ó'] = "o", ['Ś'] = "s", ['Ź'] = "z", ['Ż'] = "z",
    };

    public static string Generate(string title)
    {
        var builder = new StringBuilder();
        foreach (var ch in title)
        {
            builder.Append(PolishMap.TryGetValue(ch, out var mapped) ? mapped : ch.ToString());
        }

        var lowered = builder.ToString().ToLowerInvariant();
        var withDashes = Regex.Replace(lowered, "[^a-z0-9]+", "-");
        return withDashes.Trim('-');
    }

    public static string MakeUnique(string baseSlug, ISet<string> existingSlugs)
    {
        if (!existingSlugs.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (existingSlugs.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        return $"{baseSlug}-{suffix}";
    }
}
```

- [ ] **Step 4: Uruchom testy — muszą przejść**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter SlugGeneratorTests`
Expected: PASS (6/6)

- [ ] **Step 5: Commit**

```bash
git add src/DrugiSet.Api/Posts/SlugGenerator.cs tests/DrugiSet.Api.Tests/Posts/SlugGeneratorTests.cs
git commit -m "Dodaj generator slugów dla aktualności"
git push
```

---

## Task 2: Encja Post, AppDbContext, migracja

**Files:**
- Create: `src/DrugiSet.Api/Posts/Post.cs`
- Modify: `src/DrugiSet.Api/Data/AppDbContext.cs`
- Create (wygenerowane): `src/DrugiSet.Api/Migrations/<timestamp>_AddPosts.cs` i towarzyszące pliki

**Interfaces:**
- Produces: `class Post { Guid Id; string Title; string Slug; string ContentHtml; string? CoverImageUrl; Guid AuthorId; DateTime CreatedAt; DateTime UpdatedAt; }`, `AppDbContext.Posts : DbSet<Post>`

To zadanie nie ma cyklu TDD (schemat/tooling, nie logika) — weryfikacja przez build i realne zastosowanie migracji zamiast testu jednostkowego.

- [ ] **Step 1: Stwórz encję Post**

```csharp
namespace DrugiSet.Api.Posts;

public class Post
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public required string ContentHtml { get; set; }
    public string? CoverImageUrl { get; set; }
    public Guid AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Zarejestruj DbSet i unikalny indeks na Slug**

Modify `src/DrugiSet.Api/Data/AppDbContext.cs`:

```csharp
using DrugiSet.Api.Posts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DrugiSet.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Post>()
            .HasIndex(p => p.Slug)
            .IsUnique();
    }
}
```

(Sama logika `SlugGenerator.MakeUnique` z Tasku 1 nie eliminuje wyścigu przy dwóch niemal równoczesnych zapisach — indeks w bazie tak.)

- [ ] **Step 3: Zbuduj projekt, żeby potwierdzić brak błędów kompilacji**

Run: `dotnet build src/DrugiSet.Api`
Expected: Build succeeded

- [ ] **Step 4: Wygeneruj migrację**

Run (z katalogu `src/DrugiSet.Api`): `dotnet ef migrations add AddPosts`
Expected: nowe pliki w `Migrations/` — otwórz wygenerowany `<timestamp>_AddPosts.cs` i sprawdź ręcznie, że zawiera `CreateTable("Posts", ...)` z kolumnami z encji i `CreateIndex(..., unique: true)` na `Slug`.

- [ ] **Step 5: Zastosuj migrację na bazie dev/lokalnej**

Run: `dotnet ef database update`
Expected: `Applying migration 'AddPosts'.` bez błędów. Sprawdź w bazie (np. `psql` albo Neon SQL Editor): `\d "Posts"` pokazuje właściwe kolumny i unikalny indeks.

- [ ] **Step 6: Commit**

```bash
git add src/DrugiSet.Api/Posts/Post.cs src/DrugiSet.Api/Data/AppDbContext.cs src/DrugiSet.Api/Migrations/
git commit -m "Dodaj encję Post i migrację AddPosts"
git push
```

---

## Task 3: PostsService — odczyty publiczne + DTO

**Files:**
- Create: `src/DrugiSet.Api/Posts/PostDtos.cs`
- Create: `src/DrugiSet.Api/Posts/PostsService.cs`
- Test: `tests/DrugiSet.Api.Tests/Posts/PostsServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (z `Posts/2`), `Post` (z Task 2)
- Produces: `record PostSummaryDto(Guid Id, string Title, string Slug, string? CoverImageUrl, DateTime CreatedAt)`, `record PostDetailDto(Guid Id, string Title, string Slug, string ContentHtml, string? CoverImageUrl, DateTime CreatedAt, DateTime UpdatedAt)`, `class PostsService { Task<List<PostSummaryDto>> GetPublishedPostsAsync(); Task<PostDetailDto?> GetPostBySlugAsync(string slug); }`

- [ ] **Step 1: Napisz failing testy**

```csharp
using System;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Posts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class PostsServiceTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Post SamplePost(string title = "Pierwszy wpis", DateTime? createdAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        return new Post
        {
            Id = Guid.NewGuid(),
            Title = title,
            Slug = SlugGenerator.Generate(title),
            ContentHtml = "<p>Treść</p>",
            AuthorId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [Fact]
    public async Task GetPublishedPostsAsync_ReturnsPostsNewestFirst()
    {
        await using var db = CreateDbContext();
        var older = SamplePost("Starszy wpis", DateTime.UtcNow.AddDays(-1));
        var newer = SamplePost("Nowszy wpis", DateTime.UtcNow);
        db.Posts.AddRange(older, newer);
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        var result = await service.GetPublishedPostsAsync();

        Assert.Equal(new[] { "Nowszy wpis", "Starszy wpis" }, result.ConvertAll(p => p.Title));
    }

    [Fact]
    public async Task GetPostBySlugAsync_ReturnsFullContentForExistingSlug()
    {
        await using var db = CreateDbContext();
        var post = SamplePost("Wpis ze szczegółami");
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        var result = await service.GetPostBySlugAsync(post.Slug);

        Assert.NotNull(result);
        Assert.Equal(post.ContentHtml, result!.ContentHtml);
    }

    [Fact]
    public async Task GetPostBySlugAsync_ReturnsNullWhenSlugDoesNotExist()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);

        var result = await service.GetPostBySlugAsync("nie-istnieje");

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsServiceTests`
Expected: FAIL (kompilacja — `PostDtos`/`PostsService` jeszcze nie istnieją)

- [ ] **Step 3: Zaimplementuj DTO i PostsService (odczyty)**

```csharp
// src/DrugiSet.Api/Posts/PostDtos.cs
namespace DrugiSet.Api.Posts;

public record PostSummaryDto(Guid Id, string Title, string Slug, string? CoverImageUrl, DateTime CreatedAt);
public record PostDetailDto(Guid Id, string Title, string Slug, string ContentHtml, string? CoverImageUrl, DateTime CreatedAt, DateTime UpdatedAt);
public record CreatePostRequest(string Title, string ContentHtml, string? CoverImageUrl);
public record UpdatePostRequest(string Title, string ContentHtml, string? CoverImageUrl);
public record ImageUploadResponse(string Url);
```

```csharp
// src/DrugiSet.Api/Posts/PostsService.cs
using DrugiSet.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DrugiSet.Api.Posts;

public class PostsService
{
    private readonly AppDbContext _dbContext;

    public PostsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PostSummaryDto>> GetPublishedPostsAsync()
    {
        return await _dbContext.Posts
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PostSummaryDto(p.Id, p.Title, p.Slug, p.CoverImageUrl, p.CreatedAt))
            .ToListAsync();
    }

    public async Task<PostDetailDto?> GetPostBySlugAsync(string slug)
    {
        var post = await _dbContext.Posts.FirstOrDefaultAsync(p => p.Slug == slug);
        return post is null ? null : ToDetailDto(post);
    }

    private static PostDetailDto ToDetailDto(Post post) =>
        new(post.Id, post.Title, post.Slug, post.ContentHtml, post.CoverImageUrl, post.CreatedAt, post.UpdatedAt);
}
```

(DTO `record CreatePostRequest`/`UpdatePostRequest`/`ImageUploadResponse` są tu tylko zadeklarowane — używane dopiero w Tasku 4 i 7-8. `PostDetailDto` jest współdzielony: ten sam kształt wraca z publicznego odczytu po slugu i z chronionego odczytu po id w Tasku 4 — żaden z nich nie ujawnia `AuthorId`.)

- [ ] **Step 4: Uruchom testy — muszą przejść**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsServiceTests`
Expected: PASS (3/3)

- [ ] **Step 5: Commit**

```bash
git add src/DrugiSet.Api/Posts/PostDtos.cs src/DrugiSet.Api/Posts/PostsService.cs tests/DrugiSet.Api.Tests/Posts/PostsServiceTests.cs
git commit -m "Dodaj publiczne odczyty aktualności (PostsService)"
git push
```

---

## Task 4: PostsService — CRUD admina

**Files:**
- Modify: `src/DrugiSet.Api/Posts/PostsService.cs`
- Modify: `tests/DrugiSet.Api.Tests/Posts/PostsServiceTests.cs`

**Interfaces:**
- Consumes: `SlugGenerator` (Task 1), `CreatePostRequest`/`UpdatePostRequest` (Task 3)
- Produces: `PostsService` zyskuje `Task<List<PostSummaryDto>> GetAdminPostsAsync(); Task<PostDetailDto?> GetAdminPostAsync(Guid id); Task<PostDetailDto> CreatePostAsync(CreatePostRequest request, Guid authorId); Task<PostDetailDto?> UpdatePostAsync(Guid id, UpdatePostRequest request); Task<bool> DeletePostAsync(Guid id);`

- [ ] **Step 1: Dopisz failing testy do PostsServiceTests.cs**

```csharp
    [Fact]
    public async Task CreatePostAsync_GeneratesSlugAndSetsServerTimestamps()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var authorId = Guid.NewGuid();
        var request = new CreatePostRequest("Nowa aktualność", "<p>Treść</p>", null);

        var result = await service.CreatePostAsync(request, authorId);

        Assert.Equal("nowa-aktualnosc", result.Slug);
        Assert.True((DateTime.UtcNow - result.CreatedAt).TotalSeconds < 5);
        Assert.Equal(result.CreatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task CreatePostAsync_AppendsSuffixWhenSlugAlreadyExists()
    {
        await using var db = CreateDbContext();
        db.Posts.Add(SamplePost("Nowa aktualność"));
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        var result = await service.CreatePostAsync(
            new CreatePostRequest("Nowa aktualność", "<p>Inna treść</p>", null), Guid.NewGuid());

        Assert.Equal("nowa-aktualnosc-2", result.Slug);
    }

    [Fact]
    public async Task UpdatePostAsync_ChangesContentButKeepsOriginalSlug()
    {
        await using var db = CreateDbContext();
        var post = SamplePost("Tytuł początkowy");
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        var result = await service.UpdatePostAsync(
            post.Id, new UpdatePostRequest("Zupełnie inny tytuł", "<p>Nowa treść</p>", null));

        Assert.NotNull(result);
        Assert.Equal("Zupełnie inny tytuł", result!.Title);
        Assert.Equal(post.Slug, result.Slug);
        Assert.True(result.UpdatedAt > post.CreatedAt);
    }

    [Fact]
    public async Task UpdatePostAsync_ReturnsNullWhenPostDoesNotExist()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);

        var result = await service.UpdatePostAsync(Guid.NewGuid(), new UpdatePostRequest("X", "<p>Y</p>", null));

        Assert.Null(result);
    }

    [Fact]
    public async Task DeletePostAsync_RemovesPostAndReturnsTrue()
    {
        await using var db = CreateDbContext();
        var post = SamplePost();
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        var deleted = await service.DeletePostAsync(post.Id);

        Assert.True(deleted);
        Assert.Empty(await db.Posts.ToListAsync());
    }

    [Fact]
    public async Task DeletePostAsync_ReturnsFalseWhenPostDoesNotExist()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);

        Assert.False(await service.DeletePostAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAdminPostsAsync_ReturnsAllPostsRegardlessOfAge()
    {
        await using var db = CreateDbContext();
        db.Posts.AddRange(SamplePost("A"), SamplePost("B"));
        await db.SaveChangesAsync();
        var service = new PostsService(db);

        Assert.Equal(2, (await service.GetAdminPostsAsync()).Count);
    }
```

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsServiceTests`
Expected: FAIL (kompilacja — metody CRUD jeszcze nie istnieją)

- [ ] **Step 3: Dopisz metody CRUD do PostsService**

```csharp
    public async Task<List<PostSummaryDto>> GetAdminPostsAsync()
    {
        return await _dbContext.Posts
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PostSummaryDto(p.Id, p.Title, p.Slug, p.CoverImageUrl, p.CreatedAt))
            .ToListAsync();
    }

    public async Task<PostDetailDto?> GetAdminPostAsync(Guid id)
    {
        var post = await _dbContext.Posts.FindAsync(id);
        return post is null ? null : ToDetailDto(post);
    }

    public async Task<PostDetailDto> CreatePostAsync(CreatePostRequest request, Guid authorId)
    {
        var baseSlug = SlugGenerator.Generate(request.Title);
        var existingSlugs = await _dbContext.Posts
            .Where(p => p.Slug.StartsWith(baseSlug))
            .Select(p => p.Slug)
            .ToListAsync();
        var slug = SlugGenerator.MakeUnique(baseSlug, existingSlugs.ToHashSet());

        var now = DateTime.UtcNow;
        var post = new Post
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Slug = slug,
            ContentHtml = request.ContentHtml,
            CoverImageUrl = request.CoverImageUrl,
            AuthorId = authorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _dbContext.Posts.Add(post);
        await _dbContext.SaveChangesAsync();

        return ToDetailDto(post);
    }

    public async Task<PostDetailDto?> UpdatePostAsync(Guid id, UpdatePostRequest request)
    {
        var post = await _dbContext.Posts.FindAsync(id);
        if (post is null) return null;

        post.Title = request.Title;
        post.ContentHtml = request.ContentHtml;
        post.CoverImageUrl = request.CoverImageUrl;
        post.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return ToDetailDto(post);
    }

    public async Task<bool> DeletePostAsync(Guid id)
    {
        var post = await _dbContext.Posts.FindAsync(id);
        if (post is null) return false;

        _dbContext.Posts.Remove(post);
        await _dbContext.SaveChangesAsync();
        return true;
    }
```

(Wstaw te metody w `PostsService` obok istniejących odczytów. Slug **nigdy** nie jest przeliczany w `UpdatePostAsync` — zgodnie ze specem, zostaje stabilny od utworzenia.)

- [ ] **Step 4: Uruchom testy — muszą przejść**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsServiceTests`
Expected: PASS (10/10)

- [ ] **Step 5: Commit**

```bash
git add src/DrugiSet.Api/Posts/PostsService.cs tests/DrugiSet.Api.Tests/Posts/PostsServiceTests.cs
git commit -m "Dodaj CRUD admina do PostsService"
git push
```

---

## Task 5: Publiczne endpointy (`GET /api/posts`, `GET /api/posts/{slug}`)

**Files:**
- Create: `src/DrugiSet.Api/Posts/PostsEndpoints.cs`
- Modify: `src/DrugiSet.Api/Program.cs`
- Test: `tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs`

**Interfaces:**
- Consumes: `PostsService` (Task 3/4)
- Produces: `static class PostsEndpoints { static void MapPostsEndpoints(WebApplication app); internal static Task<IResult> GetPublishedPosts(PostsService); internal static Task<IResult> GetPostBySlug(string slug, PostsService); }`

Handlery są statycznymi metodami z jawną sygnaturą — wywoływane bezpośrednio w testach (bez hostowania serwera), zwracają typowane `IResult` (`Ok<T>`, `NotFound`) z `Microsoft.AspNetCore.Http.HttpResults`, dające się asercjonować bez `WebApplicationFactory` — ten sam poziom testowania co istniejący `JwtTokenServiceTests`/`TestAccountSeederTests`, tylko jeden poziom wyżej (handler zamiast czystego serwisu).

- [ ] **Step 1: Napisz failing testy**

```csharp
using System;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Posts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class PostsEndpointsTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetPublishedPosts_ReturnsOkWithPostList()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        await service.CreatePostAsync(new CreatePostRequest("Wpis", "<p>Treść</p>", null), Guid.NewGuid());

        var result = await PostsEndpoints.GetPublishedPosts(service);

        var ok = Assert.IsType<Ok<List<PostSummaryDto>>>(result);
        Assert.Single(ok.Value!);
    }

    [Fact]
    public async Task GetPostBySlug_ReturnsOkWhenFound()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var created = await service.CreatePostAsync(new CreatePostRequest("Wpis", "<p>Treść</p>", null), Guid.NewGuid());

        var result = await PostsEndpoints.GetPostBySlug(created.Slug, service);

        var ok = Assert.IsType<Ok<PostDetailDto>>(result);
        Assert.Equal(created.Id, ok.Value!.Id);
    }

    [Fact]
    public async Task GetPostBySlug_ReturnsNotFoundWhenMissing()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);

        var result = await PostsEndpoints.GetPostBySlug("brak-takiego", service);

        Assert.IsType<NotFound>(result);
    }
}
```

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: FAIL (kompilacja — `PostsEndpoints` jeszcze nie istnieje)

- [ ] **Step 3: Zaimplementuj PostsEndpoints (część publiczna)**

```csharp
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrugiSet.Api.Posts;

public static class PostsEndpoints
{
    public static void MapPostsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/posts", GetPublishedPosts);
        app.MapGet("/api/posts/{slug}", GetPostBySlug);
    }

    internal static async Task<IResult> GetPublishedPosts(PostsService postsService)
    {
        var posts = await postsService.GetPublishedPostsAsync();
        return TypedResults.Ok(posts);
    }

    internal static async Task<IResult> GetPostBySlug(string slug, PostsService postsService)
    {
        var post = await postsService.GetPostBySlugAsync(slug);
        return post is null ? TypedResults.NotFound() : TypedResults.Ok(post);
    }
}
```

- [ ] **Step 4: Zarejestruj serwis i endpointy w Program.cs**

Modify `src/DrugiSet.Api/Program.cs` — dodaj import `using DrugiSet.Api.Posts;` na górze, `builder.Services.AddScoped<PostsService>();` obok `builder.Services.AddSingleton<JwtTokenService>();`, i `app.MapPostsEndpoints();` zaraz po `app.MapAuthEndpoints();`.

- [ ] **Step 5: Uruchom testy — muszą przejść, potem zbuduj cały projekt**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: PASS (3/3)

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/DrugiSet.Api/Posts/PostsEndpoints.cs src/DrugiSet.Api/Program.cs tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs
git commit -m "Dodaj publiczne endpointy GET /api/posts"
git push
```

---

## Task 6: Chronione endpointy CRUD (`/api/admin/posts/*`)

**Files:**
- Modify: `src/DrugiSet.Api/Posts/PostsEndpoints.cs`
- Modify: `tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs`
- Modify: `src/DrugiSet.Api/DrugiSet.Api.http`

**Interfaces:**
- Consumes: `PostsService.CreatePostAsync/UpdatePostAsync/DeletePostAsync/GetAdminPostsAsync/GetAdminPostAsync` (Task 4)
- Produces: `internal static Task<IResult> GetAdminPosts/GetAdminPost/CreatePost/UpdatePost/DeletePost(...)`, trasy `/api/admin/posts/*` chronione `[Authorize(Roles="Admin")]`-equivalent (`RequireAuthorization`)

- [ ] **Step 1: Dopisz failing testy**

```csharp
    [Fact]
    public async Task CreatePost_ReturnsCreatedWithAuthorIdFromClaims()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var authorId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, authorId.ToString()),
        }));

        var result = await PostsEndpoints.CreatePost(
            new CreatePostRequest("Nowy wpis", "<p>Treść</p>", null), principal, service);

        var created = Assert.IsType<Created<PostDetailDto>>(result);
        Assert.Equal("Nowy wpis", created.Value!.Title);
    }

    [Fact]
    public async Task CreatePost_ReturnsBadRequestWhenTitleIsBlank()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
        }));

        var result = await PostsEndpoints.CreatePost(
            new CreatePostRequest("   ", "<p>Treść</p>", null), principal, service);

        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task UpdatePost_ReturnsOkWhenPostExists()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var created = await service.CreatePostAsync(new CreatePostRequest("Tytuł", "<p>A</p>", null), Guid.NewGuid());

        var result = await PostsEndpoints.UpdatePost(
            created.Id, new UpdatePostRequest("Nowy tytuł", "<p>B</p>", null), service);

        var ok = Assert.IsType<Ok<PostDetailDto>>(result);
        Assert.Equal("Nowy tytuł", ok.Value!.Title);
    }

    [Fact]
    public async Task UpdatePost_ReturnsNotFoundWhenPostMissing()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);

        var result = await PostsEndpoints.UpdatePost(
            Guid.NewGuid(), new UpdatePostRequest("X", "<p>Y</p>", null), service);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task DeletePost_ReturnsNoContentWhenDeleted()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var created = await service.CreatePostAsync(new CreatePostRequest("Do usunięcia", "<p>A</p>", null), Guid.NewGuid());

        var result = await PostsEndpoints.DeletePost(created.Id, service);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task GetAdminPost_ReturnsOkWhenFound()
    {
        await using var db = CreateDbContext();
        var service = new PostsService(db);
        var created = await service.CreatePostAsync(new CreatePostRequest("Wpis", "<p>A</p>", null), Guid.NewGuid());

        var result = await PostsEndpoints.GetAdminPost(created.Id, service);

        Assert.IsType<Ok<PostDetailDto>>(result);
    }
```

Dodaj na górze pliku testowego: `using System.IdentityModel.Tokens.Jwt;` i `using System.Security.Claims;`.

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: FAIL (kompilacja)

- [ ] **Step 3: Dopisz handlery i grupę tras admina**

Modify `MapPostsEndpoints`:

```csharp
    public static void MapPostsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/posts", GetPublishedPosts);
        app.MapGet("/api/posts/{slug}", GetPostBySlug);

        var admin = app.MapGroup("/api/admin/posts")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        admin.MapGet("/", GetAdminPosts);
        admin.MapGet("/{id:guid}", GetAdminPost);
        admin.MapPost("/", CreatePost);
        admin.MapPut("/{id:guid}", UpdatePost);
        admin.MapDelete("/{id:guid}", DeletePost);
    }
```

Dopisz handlery (obok istniejących, z importami `System.IdentityModel.Tokens.Jwt` i `System.Security.Claims` na górze pliku):

```csharp
    internal static async Task<IResult> GetAdminPosts(PostsService postsService)
    {
        var posts = await postsService.GetAdminPostsAsync();
        return TypedResults.Ok(posts);
    }

    internal static async Task<IResult> GetAdminPost(Guid id, PostsService postsService)
    {
        var post = await postsService.GetAdminPostAsync(id);
        return post is null ? TypedResults.NotFound() : TypedResults.Ok(post);
    }

    internal static async Task<IResult> CreatePost(CreatePostRequest request, ClaimsPrincipal principal, PostsService postsService)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.Problem(title: "Tytuł jest wymagany.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrWhiteSpace(request.ContentHtml))
        {
            return TypedResults.Problem(title: "Treść jest wymagana.", statusCode: StatusCodes.Status400BadRequest);
        }

        var authorId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var post = await postsService.CreatePostAsync(request, authorId);
        return TypedResults.Created($"/api/admin/posts/{post.Id}", post);
    }

    internal static async Task<IResult> UpdatePost(Guid id, UpdatePostRequest request, PostsService postsService)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.Problem(title: "Tytuł jest wymagany.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrWhiteSpace(request.ContentHtml))
        {
            return TypedResults.Problem(title: "Treść jest wymagana.", statusCode: StatusCodes.Status400BadRequest);
        }

        var post = await postsService.UpdatePostAsync(id, request);
        return post is null ? TypedResults.NotFound() : TypedResults.Ok(post);
    }

    internal static async Task<IResult> DeletePost(Guid id, PostsService postsService)
    {
        var deleted = await postsService.DeletePostAsync(id);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }
```

- [ ] **Step 4: Uruchom testy — muszą przejść, potem zbuduj**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: PASS (9/9)

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: Dopisz ręczne żądania do DrugiSet.Api.http**

Dopisz na końcu `src/DrugiSet.Api/DrugiSet.Api.http`:

```http
###

# Zaloguj się jako admin@drugiset.pl/admin (patrz żądanie logowania powyżej),
# wklej token w miejsce <TOKEN> w żądaniach poniżej.

GET {{DrugiSet.Api_HostAddress}}/api/admin/posts
Authorization: Bearer <TOKEN>

###

POST {{DrugiSet.Api_HostAddress}}/api/admin/posts
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "title": "Testowa aktualność",
  "contentHtml": "<p>Treść testowa</p>",
  "coverImageUrl": null
}

###

GET {{DrugiSet.Api_HostAddress}}/api/posts
```

Ręcznie uruchom te trzy żądania (np. w VS Code z rozszerzeniem REST Client, po `dotnet run`) — potwierdź: 200 z listą na pierwszym, 201 z pełnym obiektem na drugim, 200 z tym samym wpisem na trzecim.

- [ ] **Step 6: Commit**

```bash
git add src/DrugiSet.Api/Posts/PostsEndpoints.cs tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs src/DrugiSet.Api/DrugiSet.Api.http
git commit -m "Dodaj chroniony CRUD /api/admin/posts"
git push
```

---

## Task 7: ImageUploadService (walidacja + przetwarzanie ImageSharp)

**Files:**
- Modify: `src/DrugiSet.Api/DrugiSet.Api.csproj` (dodaj `SixLabors.ImageSharp`)
- Create: `src/DrugiSet.Api/Posts/ImageUploadService.cs`
- Test: `tests/DrugiSet.Api.Tests/Posts/ImageUploadServiceTests.cs`
- Modify: `src/DrugiSet.Api/appsettings.json`

**Interfaces:**
- Produces: `enum ImageValidationError { Empty, TooLarge, UnsupportedType }`, `class ImageUploadService { ImageValidationError? Validate(IFormFile file); Task<string> SaveAsync(IFormFile file, string publicBaseUrl); }`

- [ ] **Step 1: Dodaj zależność ImageSharp**

Run: `dotnet add src/DrugiSet.Api package SixLabors.ImageSharp`

- [ ] **Step 2: Napisz failing testy**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DrugiSet.Api.Posts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class ImageUploadServiceTests
{
    private static IFormFile CreateFakeImageFile(int width, int height, string contentType = "image/png")
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "test.png") { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static ImageUploadService CreateService(string uploadsPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:Path"] = uploadsPath })
            .Build();
        return new ImageUploadService(configuration);
    }

    [Fact]
    public void Validate_AcceptsSupportedImageType()
    {
        var service = CreateService(Path.GetTempPath());
        var file = CreateFakeImageFile(100, 100, "image/jpeg");

        Assert.Null(service.Validate(file));
    }

    [Fact]
    public void Validate_RejectsUnsupportedContentType()
    {
        var service = CreateService(Path.GetTempPath());
        var file = CreateFakeImageFile(100, 100, "image/gif");

        Assert.Equal(ImageValidationError.UnsupportedType, service.Validate(file));
    }

    [Fact]
    public void Validate_RejectsFilesOverFiveMegabytes()
    {
        var service = CreateService(Path.GetTempPath());
        var stream = new MemoryStream(new byte[6 * 1024 * 1024]);
        var file = new FormFile(stream, 0, stream.Length, "file", "big.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

        Assert.Equal(ImageValidationError.TooLarge, service.Validate(file));
    }

    [Fact]
    public async Task SaveAsync_ResizesImagesWiderThan1600PxKeepingAspectRatio()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var service = CreateService(tempDir);
            var file = CreateFakeImageFile(2000, 1000);

            var url = await service.SaveAsync(file, "https://api.example.com");

            var relative = url[(url.IndexOf("/uploads/", StringComparison.Ordinal) + "/uploads/".Length)..];
            var savedPath = Path.Combine(tempDir, relative.Replace('/', Path.DirectorySeparatorChar));
            using var saved = await Image.LoadAsync(savedPath);
            Assert.Equal(1600, saved.Width);
            Assert.Equal(800, saved.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_LeavesSmallImagesUnresized()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var service = CreateService(tempDir);
            var file = CreateFakeImageFile(400, 300);

            var url = await service.SaveAsync(file, "https://api.example.com");

            var relative = url[(url.IndexOf("/uploads/", StringComparison.Ordinal) + "/uploads/".Length)..];
            var savedPath = Path.Combine(tempDir, relative.Replace('/', Path.DirectorySeparatorChar));
            using var saved = await Image.LoadAsync(savedPath);
            Assert.Equal(400, saved.Width);
            Assert.Equal(300, saved.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
```

- [ ] **Step 3: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter ImageUploadServiceTests`
Expected: FAIL (kompilacja — `ImageUploadService` jeszcze nie istnieje)

- [ ] **Step 4: Zaimplementuj ImageUploadService**

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace DrugiSet.Api.Posts;

public enum ImageValidationError
{
    Empty,
    TooLarge,
    UnsupportedType,
}

public class ImageUploadService
{
    private static readonly HashSet<string> AllowedContentTypes = new() { "image/jpeg", "image/png", "image/webp" };
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int MaxWidthPx = 1600;

    private readonly string _uploadsRootPath;

    public ImageUploadService(IConfiguration configuration)
    {
        _uploadsRootPath = configuration["Uploads:Path"]
            ?? throw new InvalidOperationException("Konfiguracja 'Uploads:Path' jest wymagana.");
    }

    public ImageValidationError? Validate(IFormFile file)
    {
        if (file.Length == 0) return ImageValidationError.Empty;
        if (file.Length > MaxFileSizeBytes) return ImageValidationError.TooLarge;
        if (!AllowedContentTypes.Contains(file.ContentType)) return ImageValidationError.UnsupportedType;
        return null;
    }

    public async Task<string> SaveAsync(IFormFile file, string publicBaseUrl)
    {
        using var inputStream = file.OpenReadStream();
        using var image = await Image.LoadAsync(inputStream);

        if (image.Width > MaxWidthPx)
        {
            var newHeight = (int)(image.Height * (MaxWidthPx / (double)image.Width));
            image.Mutate(x => x.Resize(MaxWidthPx, newHeight));
        }

        var now = DateTime.UtcNow;
        var relativeDir = Path.Combine(now.Year.ToString(), now.Month.ToString("D2"));
        var fileName = $"{Guid.NewGuid()}.webp";
        var relativePath = Path.Combine(relativeDir, fileName);
        var fullPath = Path.Combine(_uploadsRootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await image.SaveAsWebpAsync(fullPath);

        return $"{publicBaseUrl}/uploads/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
    }
}
```

(Wszystko zapisywane jako `.webp` niezależnie od formatu wejściowego — prostsza reguła niż zachowywanie oryginalnego formatu, i zgodna z celem „kompresja" ze specu.)

- [ ] **Step 5: Dodaj placeholder konfiguracji**

Modify `src/DrugiSet.Api/appsettings.json` — dodaj sekcję (obok istniejących `ConnectionStrings`/`Jwt`/`Cors`):

```json
"Uploads": {
  "Path": ""
}
```

Lokalna wartość dev (przez `dotnet user-secrets`, nigdy nie commitowana) — bez tego `Program.cs` z Tasku 8 nie wystartuje lokalnie:

```bash
dotnet user-secrets set "Uploads:Path" "./uploads-dev" --project src/DrugiSet.Api
```

Dodaj `uploads-dev/` do `.gitignore`.

- [ ] **Step 6: Uruchom testy — muszą przejść**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter ImageUploadServiceTests`
Expected: PASS (5/5)

- [ ] **Step 7: Commit**

```bash
git add src/DrugiSet.Api/DrugiSet.Api.csproj src/DrugiSet.Api/Posts/ImageUploadService.cs tests/DrugiSet.Api.Tests/Posts/ImageUploadServiceTests.cs src/DrugiSet.Api/appsettings.json .gitignore
git commit -m "Dodaj ImageUploadService (walidacja i przetwarzanie ImageSharp)"
git push
```

---

## Task 8: Endpoint uploadu i serwowanie plików statycznych

**Files:**
- Modify: `src/DrugiSet.Api/Posts/PostsEndpoints.cs`
- Modify: `src/DrugiSet.Api/Program.cs`
- Modify: `tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs`
- Modify: `src/DrugiSet.Api/DrugiSet.Api.http`

**Interfaces:**
- Consumes: `ImageUploadService` (Task 7), `ImageUploadResponse` (Task 3)
- Produces: `internal static Task<IResult> UploadImage(IFormFile file, HttpRequest request, ImageUploadService)`, trasa `POST /api/admin/posts/images`, pliki serwowane pod `/uploads/*`

- [ ] **Step 1: Napisz failing testy**

```csharp
    [Fact]
    public async Task UploadImage_ReturnsOkWithUrlForValidImage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:Path"] = tempDir })
                .Build();
            var imageService = new ImageUploadService(configuration);
            using var image = new Image<Rgba32>(100, 100);
            var stream = new MemoryStream();
            image.SaveAsPng(stream);
            stream.Position = 0;
            var file = new FormFile(stream, 0, stream.Length, "file", "test.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("api.example.com");

            var result = await PostsEndpoints.UploadImage(file, context.Request, imageService);

            var ok = Assert.IsType<Ok<ImageUploadResponse>>(result);
            Assert.StartsWith("https://api.example.com/uploads/", ok.Value!.Url);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UploadImage_ReturnsProblemForUnsupportedType()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:Path"] = Path.GetTempPath() })
            .Build();
        var imageService = new ImageUploadService(configuration);
        var stream = new MemoryStream(new byte[10]);
        var file = new FormFile(stream, 0, stream.Length, "file", "test.gif") { Headers = new HeaderDictionary(), ContentType = "image/gif" };
        var context = new DefaultHttpContext();

        var result = await PostsEndpoints.UploadImage(file, context.Request, imageService);

        Assert.IsType<ProblemHttpResult>(result);
    }
```

Dopisz importy na górze pliku: `using SixLabors.ImageSharp;`, `using SixLabors.ImageSharp.PixelFormats;`, `using Microsoft.AspNetCore.Http;`, `using Microsoft.Extensions.Configuration;`, `using System.IO;`.

- [ ] **Step 2: Uruchom testy — muszą nie skompilować się**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: FAIL (kompilacja — `UploadImage` jeszcze nie istnieje)

- [ ] **Step 3: Dopisz trasę i handler**

Dodaj do `admin` group w `MapPostsEndpoints`: `admin.MapPost("/images", UploadImage);`

```csharp
    internal static async Task<IResult> UploadImage(IFormFile file, HttpRequest request, ImageUploadService imageUploadService)
    {
        var error = imageUploadService.Validate(file);
        if (error is not null)
        {
            var message = error switch
            {
                ImageValidationError.Empty => "Plik jest pusty.",
                ImageValidationError.TooLarge => "Plik jest za duży (maks. 5 MB).",
                ImageValidationError.UnsupportedType => "Dozwolone typy plików: JPEG, PNG, WebP.",
                _ => "Nieprawidłowy plik.",
            };
            return TypedResults.Problem(title: message, statusCode: StatusCodes.Status400BadRequest);
        }

        var publicBaseUrl = $"{request.Scheme}://{request.Host}";
        var url = await imageUploadService.SaveAsync(file, publicBaseUrl);
        return TypedResults.Ok(new ImageUploadResponse(url));
    }
```

- [ ] **Step 4: Zarejestruj ImageUploadService i UseStaticFiles w Program.cs**

Modify `src/DrugiSet.Api/Program.cs`:
- `builder.Services.AddSingleton<ImageUploadService>();` obok `AddScoped<PostsService>();`
- Po `var app = builder.Build();`, przed `app.UseAuthentication();`:

```csharp
var uploadsPath = builder.Configuration["Uploads:Path"]
    ?? throw new InvalidOperationException("Konfiguracja 'Uploads:Path' jest wymagana.");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
});
```

Dodaj `using Microsoft.Extensions.FileProviders;` na górze `Program.cs`.

- [ ] **Step 5: Uruchom testy — muszą przejść, potem zbuduj**

Run: `dotnet test tests/DrugiSet.Api.Tests --filter PostsEndpointsTests`
Expected: PASS (11/11)

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 6: Ręczna weryfikacja end-to-end**

Uruchom lokalnie: `dotnet run --project src/DrugiSet.Api`. W REST Client (`DrugiSet.Api.http`), dopisz i uruchom:

```http
###

# Upload obrazu — w VS Code REST Client zaznacz plik przez pole "Attach File"
# albo przetestuj przez curl:
# curl -X POST {{DrugiSet.Api_HostAddress}}/api/admin/posts/images -H "Authorization: Bearer <TOKEN>" -F "file=@/sciezka/do/zdjecia.jpg"
```

Sprawdź: odpowiedź 200 z `{"url": "http://localhost:5080/uploads/2026/09/<guid>.webp"}`, a otwarcie tego URL w przeglądarce pokazuje przeskalowany obraz.

- [ ] **Step 7: Commit**

```bash
git add src/DrugiSet.Api/Posts/PostsEndpoints.cs src/DrugiSet.Api/Program.cs tests/DrugiSet.Api.Tests/Posts/PostsEndpointsTests.cs src/DrugiSet.Api/DrugiSet.Api.http
git commit -m "Dodaj upload zdjęć i serwowanie /uploads"
git push
```

---

## Task 9: CORS, README, pełny smoke test

**Files:**
- Modify: `README.md`
- Operacyjne (bez zmian w kodzie): `Cors:AllowedOrigins` w Railway Variables i lokalnych user-secrets

**Interfaces:** brak nowych (wrap-up)

- [ ] **Step 1: Zaktualizuj lokalną wartość CORS**

Run (z katalogu `src/DrugiSet.Api`): `dotnet user-secrets set "Cors:AllowedOrigins" "http://localhost:5173,http://127.0.0.1:5173,https://panel.drugiset.pl,https://drugi-set-web-production.up.railway.app,http://localhost:5174,https://drugiset.pl"`

(Dopisano `http://localhost:5174` — lokalny dev origin `drugi-set-www`, patrz Task 1 planu www — i `https://drugiset.pl`, produkcyjny origin www, którego dotąd tu nie było.)

- [ ] **Step 2: Zaktualizuj README**

Modify `README.md` — w sekcji „API" dopisz:

```markdown
- `GET /api/posts` — lista opublikowanych aktualności → `[{ "id", "title", "slug", "coverImageUrl", "createdAt" }]`
- `GET /api/posts/{slug}` — szczegóły aktualności → `{ "id", "title", "slug", "contentHtml", "coverImageUrl", "createdAt", "updatedAt" }` (404 gdy brak)
- `GET /api/admin/posts` — jw., wymaga `Authorization: Bearer <token>` z rolą Admin
- `GET /api/admin/posts/{id}` — jw.
- `POST /api/admin/posts` — `{ "title", "contentHtml", "coverImageUrl" }` → 201 z pełnym wpisem
- `PUT /api/admin/posts/{id}` — jw. → 200 z pełnym wpisem
- `DELETE /api/admin/posts/{id}` — 204
- `POST /api/admin/posts/images` — multipart/form-data, pole `file` → `{ "url" }`
```

W tabeli zmiennych środowiskowych dopisz wiersz:

```markdown
| `Uploads__Path` | Ścieżka na dysku (Railway Volume) do zapisu przesłanych zdjęć |
```

- [ ] **Step 3: Pełny przebieg testów**

Run: `dotnet test`
Expected: wszystkie testy PASS (istniejące + nowe z Tasków 1-8)

Run: `dotnet build`
Expected: Build succeeded, bez ostrzeżeń o nullable

- [ ] **Step 4: Pełny ręczny smoke test przez DrugiSet.Api.http**

Z uruchomionym lokalnie API (`dotnet run --project src/DrugiSet.Api`), w kolejności: zaloguj się jako `admin@drugiset.pl` → skopiuj token → stwórz post (`POST /api/admin/posts`) → potwierdź, że pojawia się w `GET /api/posts` i pod `GET /api/posts/{slug}` z tym samym slugiem → zaktualizuj go (`PUT`) i potwierdź, że slug się nie zmienił → usuń go (`DELETE`) → potwierdź, że zniknął z `GET /api/posts`.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "Udokumentuj endpointy aktualności i zmienną Uploads__Path"
git push
```

## Wdrożenie na Railway (poza tym planem, do zrobienia ręcznie przez użytkownika przed uruchomieniem web/www planów)

1. Dodaj Volume do serwisu `drugi-set-api` w Railway (np. zamontowany pod `/data/uploads`).
2. Ustaw `Uploads__Path=/data/uploads` w Variables serwisu.
3. Zaktualizuj `Cors__AllowedOrigins` w Variables o `https://drugiset.pl` (i lokalny origin www, jeśli portem innym niż 5174 — patrz plan www).
4. Po pushu `dotnet ef database update` na produkcyjnej bazie Neon (albo automatyczna migracja przy starcie, jeśli tak skonfigurowane — sprawdź obecną praktykę w tym repo przed założeniem).
