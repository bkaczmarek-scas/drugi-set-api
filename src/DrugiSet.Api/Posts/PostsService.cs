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
