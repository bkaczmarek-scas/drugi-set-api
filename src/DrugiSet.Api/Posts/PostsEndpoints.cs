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
