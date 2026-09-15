using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DrugiSet.Api.Posts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class ImageUploadServiceTests
{
    private static IFormFile CreateFakeImageFile(int width, int height, string contentType = "image/png")
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "test.png") { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static ImageUploadService CreateService(string uploadsPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:Path"] = uploadsPath })
            .Build();
        return new ImageUploadService(configuration);
    }

    [Fact]
    public void Validate_AcceptsSupportedImageType()
    {
        var service = CreateService(Path.GetTempPath());
        var file = CreateFakeImageFile(100, 100, "image/jpeg");

        Assert.Null(service.Validate(file));
    }

    [Fact]
    public void Validate_RejectsUnsupportedContentType()
    {
        var service = CreateService(Path.GetTempPath());
        var file = CreateFakeImageFile(100, 100, "image/gif");

        Assert.Equal(ImageValidationError.UnsupportedType, service.Validate(file));
    }

    [Fact]
    public void Validate_RejectsFilesOverFiveMegabytes()
    {
        var service = CreateService(Path.GetTempPath());
        var stream = new MemoryStream(new byte[6 * 1024 * 1024]);
        var file = new FormFile(stream, 0, stream.Length, "file", "big.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

        Assert.Equal(ImageValidationError.TooLarge, service.Validate(file));
    }

    [Fact]
    public async Task SaveAsync_ResizesImagesWiderThan1600PxKeepingAspectRatio()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var service = CreateService(tempDir);
            var file = CreateFakeImageFile(2000, 1000);

            var url = await service.SaveAsync(file, "https://api.example.com");

            var relative = url[(url.IndexOf("/uploads/", StringComparison.Ordinal) + "/uploads/".Length)..];
            var savedPath = Path.Combine(tempDir, relative.Replace('/', Path.DirectorySeparatorChar));
            using var saved = await Image.LoadAsync(savedPath);
            Assert.Equal(1600, saved.Width);
            Assert.Equal(800, saved.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_LeavesSmallImagesUnresized()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var service = CreateService(tempDir);
            var file = CreateFakeImageFile(400, 300);

            var url = await service.SaveAsync(file, "https://api.example.com");

            var relative = url[(url.IndexOf("/uploads/", StringComparison.Ordinal) + "/uploads/".Length)..];
            var savedPath = Path.Combine(tempDir, relative.Replace('/', Path.DirectorySeparatorChar));
            using var saved = await Image.LoadAsync(savedPath);
            Assert.Equal(400, saved.Width);
            Assert.Equal(300, saved.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
