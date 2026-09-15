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
