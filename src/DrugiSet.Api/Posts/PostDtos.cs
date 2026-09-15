namespace DrugiSet.Api.Posts;

public record PostSummaryDto(Guid Id, string Title, string Slug, string? CoverImageUrl, DateTime CreatedAt);
public record PostDetailDto(Guid Id, string Title, string Slug, string ContentHtml, string? CoverImageUrl, DateTime CreatedAt, DateTime UpdatedAt);
public record CreatePostRequest(string Title, string ContentHtml, string? CoverImageUrl);
public record UpdatePostRequest(string Title, string ContentHtml, string? CoverImageUrl);
public record ImageUploadResponse(string Url);
