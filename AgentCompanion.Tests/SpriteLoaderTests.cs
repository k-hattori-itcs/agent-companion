using System.Windows.Media.Imaging;
using AgentCompanion.Services;
using AgentCompanion.Windows;
using SkiaSharp;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class SpriteLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"agent-companion-sprite-loader-{Guid.NewGuid():N}");

    public SpriteLoaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void LoadSpritesheet_ReloadsUpdatedPixelsFromSamePath()
    {
        var path = Path.Combine(_directory, "spritesheet.png");
        WritePng(path, SKColors.Red);
        var first = Assert.IsAssignableFrom<BitmapSource>(SpriteLoader.LoadSpritesheet(path));

        WritePng(path, SKColors.Blue);
        var second = Assert.IsAssignableFrom<BitmapSource>(SpriteLoader.LoadSpritesheet(path));

        Assert.Equal((byte)255, ReadPixel(first)[2]);
        Assert.Equal((byte)255, ReadPixel(second)[0]);
    }

    [Fact]
    public void LoadCharacterPreview_ReleasesFileForSamePathReplacement()
    {
        var path = Path.Combine(_directory, "preview-idle.png");
        WritePng(path, SKColors.Red);
        var first = Assert.IsAssignableFrom<BitmapSource>(SettingsWindow.LoadCharacterPreview(path));

        WritePng(path, SKColors.Blue);
        var second = Assert.IsAssignableFrom<BitmapSource>(SettingsWindow.LoadCharacterPreview(path));

        Assert.Equal((byte)255, ReadPixel(first)[2]);
        Assert.Equal((byte)255, ReadPixel(second)[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static void WritePng(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static byte[] ReadPixel(BitmapSource source)
    {
        var bytes = new byte[4];
        source.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), bytes, 4, 0);
        return bytes;
    }
}
