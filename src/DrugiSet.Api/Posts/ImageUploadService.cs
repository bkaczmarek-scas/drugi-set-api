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
