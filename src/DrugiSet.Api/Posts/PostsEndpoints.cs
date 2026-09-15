using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DrugiSet.Api.Posts;

public static class PostsEndpoints
{
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
}
