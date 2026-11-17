using InviteCraft.Application.Common.Interfaces;
using SkiaSharp;

namespace InviteCraft.Infrastructure.Storage;

/// <summary>SkiaSharp-based image sniffing and resizing (MIT licence, native libs for Windows/Linux).</summary>
internal sealed class SkiaImageProcessor : IImageProcessor
{
    /// <summary>Guards against decompression bombs (a tiny file that decodes to gigabytes).</summary>
    private const long MaxPixels = 40_000_000;

    public ImageInfo? Inspect(Stream image)
    {
        using var data = SKData.Create(image);
        if (data is null)
            return null;

        using var codec = SKCodec.Create(data);
        if (codec is null)
            return null;

        return codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => new ImageInfo("jpeg", "image/jpeg", "jpg", codec.Info.Width, codec.Info.Height),
            SKEncodedImageFormat.Png => new ImageInfo("png", "image/png", "png", codec.Info.Width, codec.Info.Height),
            SKEncodedImageFormat.Webp => new ImageInfo("webp", "image/webp", "webp", codec.Info.Width, codec.Info.Height),
            _ => null,
        };
    }

    public byte[] CreateWebVariant(Stream image, int maxDimension, int quality)
    {
        using var data = SKData.Create(image) ?? throw new InvalidOperationException("Empty image stream.");
        using var codec = SKCodec.Create(data) ?? throw new InvalidOperationException("Unsupported image.");

        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if ((long)width * height > MaxPixels)
            throw new InvalidOperationException($"Image too large to process ({width}x{height}).");

        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("Image could not be decoded.");
        using var oriented = ApplyOrientation(decoded, codec.EncodedOrigin);

        var scale = Math.Min(1.0, (double)maxDimension / Math.Max(oriented.Width, oriented.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(oriented.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(oriented.Height * scale));

        using var resized = scale < 1.0
            ? oriented.Resize(new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell))
            : oriented.Copy();

        using var skImage = SKImage.FromBitmap(resized);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Webp, quality);
        return encoded.ToArray();
    }

    /// <summary>Honours the EXIF orientation of camera photos so they are not stored sideways.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return source.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, source.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, source.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            // Transverse: (x, y) → (h − y, w − x)
            case SKEncodedOrigin.RightBottom: canvas.Translate(result.Width, result.Height); canvas.Scale(1, -1); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
        }

        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
