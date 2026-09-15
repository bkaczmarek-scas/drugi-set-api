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
