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

    private static PostDetailDto ToDetailDto(Post post) =>
        new(post.Id, post.Title, post.Slug, post.ContentHtml, post.CoverImageUrl, post.CreatedAt, post.UpdatedAt);
}
