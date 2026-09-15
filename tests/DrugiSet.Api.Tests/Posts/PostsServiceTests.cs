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
}
