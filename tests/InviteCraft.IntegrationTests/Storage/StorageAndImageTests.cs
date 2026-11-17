using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace InviteCraft.IntegrationTests.Storage;

public sealed class StorageAndImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public StorageAndImageTests()
    {
        _storage = new LocalFileStorage(
            Options.Create(new StorageSettings { LocalRootPath = _root, PublicBaseUrl = "/media" }),
            new TestEnvironment(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Saves_reads_and_deletes_files()
    {
        await _storage.SaveAsync("public/templates/t1/a1/web.webp", new MemoryStream([1, 2, 3]), "image/webp", Ct);

        await using (var read = await _storage.OpenReadAsync("public/templates/t1/a1/web.webp", Ct))
        {
            read.ShouldNotBeNull();
            read.Length.ShouldBe(3);
        }

        await _storage.DeletePrefixAsync("public/templates/t1/a1/", Ct);
        (await _storage.OpenReadAsync("public/templates/t1/a1/web.webp", Ct)).ShouldBeNull();
    }

    [Fact]
    public void Only_public_keys_get_urls()
    {
        _storage.GetPublicUrl("public/fonts/abc.woff2").ShouldBe("/media/fonts/abc.woff2");
        _storage.GetPublicUrl("private/templates/t1/a1/original.png").ShouldBeNull();
    }

    [Theory]
    [InlineData("public/../private/secret.png")]
    [InlineData("private/..\\..\\windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("other/file.txt")]
    public async Task Rejects_keys_that_could_escape_the_storage_root(string key)
    {
        await Should.ThrowAsync<ArgumentException>(() => _storage.SaveAsync(key, new MemoryStream([1]), "x", Ct));
    }

    [Fact]
    public void Image_processor_detects_format_and_creates_downscaled_webp()
    {
        var processor = new SkiaImageProcessor();
        using var png = CreatePng(1200, 1800);

        var info = processor.Inspect(png);
        info.ShouldNotBeNull();
        info.Format.ShouldBe("png");
        (info.Width, info.Height).ShouldBe((1200, 1800));

        png.Position = 0;
        var webp = processor.CreateWebVariant(png, maxDimension: 480, quality: 80);
        using var codec = SKCodec.Create(new MemoryStream(webp));
        codec.EncodedFormat.ShouldBe(SKEncodedImageFormat.Webp);
        codec.Info.Height.ShouldBe(480);
        codec.Info.Width.ShouldBe(320);
    }

    [Fact]
    public void Image_processor_rejects_non_images_even_with_an_image_name()
    {
        var fake = new MemoryStream("<?php echo 'not an image'; ?>"u8.ToArray());

        new SkiaImageProcessor().Inspect(fake).ShouldBeNull();
    }

    [Fact]
    public void Font_sniffing_accepts_woff2_and_ttf_magic_bytes_only()
    {
        FontFiles.IsWoff2("wOF2...."u8).ShouldBeTrue();
        FontFiles.IsWoff2("wOFF...."u8).ShouldBeFalse();
        FontFiles.IsTrueTypeOrOpenType([0x00, 0x01, 0x00, 0x00, 0x00]).ShouldBeTrue();
        FontFiles.IsTrueTypeOrOpenType("OTTO"u8).ShouldBeTrue();
        FontFiles.IsTrueTypeOrOpenType("GIF8"u8).ShouldBeFalse();
    }

    private static MemoryStream CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(new SKColor(0xFB, 0xF7, 0xF0));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
