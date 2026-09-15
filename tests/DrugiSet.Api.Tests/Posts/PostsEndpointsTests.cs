using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using DrugiSet.Api.Data;
using DrugiSet.Api.Posts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
}
