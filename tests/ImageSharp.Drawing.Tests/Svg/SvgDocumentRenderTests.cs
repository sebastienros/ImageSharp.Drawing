// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests <see cref="SvgDocument.Render{TPixel}(Configuration, Color)"/> and
/// <see cref="SvgDocument.Render{TPixel}(Configuration, Size, Color)"/>, and loading a document from UTF-16 text. Expected
/// pixels come from <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> on an image of the same size and pixel
/// format.
/// </summary>
public class SvgDocumentRenderTests
{
    private const string SvgNamespace = "xmlns=\"http://www.w3.org/2000/svg\"";

    // A 40 x 20 document with fills, a gradient, a stroke, and a group with opacity.
    private const string Shapes =
        "<svg " + SvgNamespace + " width=\"40\" height=\"20\" viewBox=\"0 0 40 20\">"
        + "<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#08f\"/><stop offset=\"1\" stop-color=\"#f80\" stop-opacity=\"0.5\"/></linearGradient>"
        + "<rect x=\"1\" y=\"1\" width=\"24\" height=\"14\" fill=\"url(#g)\"/>"
        + "<circle cx=\"30\" cy=\"10\" r=\"7.3\" fill=\"none\" stroke=\"#c00\" stroke-width=\"1.7\"/>"
        + "<g opacity=\"0.6\"><rect x=\"8\" y=\"8\" width=\"10\" height=\"10\" fill=\"#0a0\"/>"
        + "<rect x=\"12\" y=\"5\" width=\"10\" height=\"10\" fill=\"#00a\"/></g>"
        + "</svg>";

    [Fact]
    public void Render_EqualsDrawSvgAndValidatesArguments()
    {
        SvgDocument document = SvgDocument.Parse(Shapes);

        using Image<Rgba32> intrinsic = document.Render<Rgba32>();
        using Image<Rgba32> sized = document.Render<Rgba32>(new Size(90, 30));
        using Image<Bgra32> withBackground = document.Render<Bgra32>(Configuration.Default, new Size(90, 30), Color.Gray);
        using Image<Rgba32> expectedIntrinsic = Draw<Rgba32>(document, 40, 20, null, default);
        using Image<Rgba32> expectedSized = Draw<Rgba32>(document, 90, 30, new RectangleF(0, 0, 90, 30), default);
        using Image<Bgra32> expectedBackground = Draw<Bgra32>(document, 90, 30, new RectangleF(0, 0, 90, 30), Color.Gray.ToPixel<Bgra32>());

        ImageComparer.Exact.VerifySimilarity(expectedIntrinsic, intrinsic);
        ImageComparer.Exact.VerifySimilarity(expectedSized, sized);
        ImageComparer.Exact.VerifySimilarity(expectedBackground, withBackground);

        Assert.Throws<ArgumentNullException>(() => document.Render<Rgba32>(null!, Color.Transparent));
        Assert.Throws<ArgumentNullException>(() => document.Render<Rgba32>(null!, new Size(1, 1), Color.Transparent));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Render<Rgba32>(new Size(0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Render<Rgba32>(new Size(10, -1)));
    }

    [Fact]
    public void Render_AtTheIntrinsicSize_FollowsTheImagePixelLimit()
    {
        SvgDocument limited = SvgDocument.Parse(Shapes, new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = 799 } });

        Assert.Throws<InvalidImageContentException>(() => limited.Render<Rgba32>());
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse("<svg " + SvgNamespace + " width=\"0\" height=\"10\"/>").Render<Rgba32>());
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse("<svg " + SvgNamespace + " width=\"1\" height=\"65536\"/>").Render<Rgba32>());

        // A size that the caller chooses is not limited by the document.
        using Image<Rgba32> image = limited.Render<Rgba32>(new Size(40, 20));
        Assert.Equal(new Size(40, 20), image.Size);
    }

    [Fact]
    public void Render_FractionalSize_RoundsUpAndDrawsAtTheExactSize()
    {
        string svg = "<svg " + SvgNamespace + " width=\"10.5\" height=\"7.25\"><rect width=\"100%\" height=\"100%\" fill=\"#f00\"/></svg>";
        SvgDocument document = SvgDocument.Parse(svg);

        using Image<Rgba32> rendered = document.Render<Rgba32>();
        using Image<Rgba32> expected = Draw<Rgba32>(document, 11, 8, null, default);

        Assert.Equal(new Size(11, 8), rendered.Size);
        ImageComparer.Exact.VerifySimilarity(expected, rendered);

        // The last column is only partly covered; the content is not stretched over it.
        Assert.Equal(255, rendered[0, 0].A);
        Assert.InRange(rendered[10, 0].A, 1, 254);

        // An excess that unit conversion adds does not add a pixel.
        using Image<Rgba32> converted = SvgDocument.Parse("<svg " + SvgNamespace + " width=\"2.54cm\" height=\"25.4mm\"/>").Render<Rgba32>();
        Assert.Equal(new Size(96, 96), converted.Size);
    }

    [Fact]
    public void Render_IntrinsicSize_IsBoundedBeforeAllocation()
    {
        TestMemoryAllocator allocator = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;

        // The intrinsic size exceeds the default image pixel limit.
        SvgDocument huge = SvgDocument.Parse("<svg " + SvgNamespace + " width=\"100000\" height=\"100000\"><rect width=\"10\" height=\"10\"/></svg>");
        Assert.Throws<InvalidImageContentException>(() => huge.Render<Rgba32>(configuration, Color.Transparent));

        // A side is bounded apart from the pixel count: the scratch memory of the rasterizer grows with the width.
        SvgDocument wide = SvgDocument.Parse("<svg " + SvgNamespace + " width=\"65536\" height=\"1\"/>");
        Assert.Throws<InvalidImageContentException>(() => wide.Render<Rgba32>(configuration, Color.Transparent));

        // A side that is not a usable image size is refused.
        foreach (string width in new[] { "1e30", "3e9" })
        {
            SvgDocument unusable = SvgDocument.Parse("<svg " + SvgNamespace + $" width=\"{width}\" height=\"10\"/>");
            Assert.Throws<InvalidImageContentException>(() => unusable.Render<Rgba32>(configuration, Color.Transparent));
        }

        // The limit comes from the load options and includes its value.
        SvgDocument belowLimit = SvgDocument.Parse(Shapes, new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = (40 * 20) - 1 } });
        Assert.Throws<InvalidImageContentException>(() => belowLimit.Render<Rgba32>(configuration, Color.Transparent));

        // No case allocated pixel memory. The allocator logs the buffers of an image.
        Assert.Empty(allocator.AllocationLog);
        using (Image<Rgba32> logged = new(configuration, 2, 2))
        {
            Assert.NotEmpty(allocator.AllocationLog);
        }

        SvgDocument atLimit = SvgDocument.Parse(Shapes, new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = 40 * 20 } });
        using Image<Rgba32> image = atLimit.Render<Rgba32>();
        Assert.Equal(new Size(40, 20), image.Size);

        using Image<Rgba32> widestImage = SvgDocument.Parse("<svg " + SvgNamespace + " width=\"65535\" height=\"1\"/>").Render<Rgba32>();
        Assert.Equal(new Size(65535, 1), widestImage.Size);
    }

    [Fact]
    public void Load_Utf16_EqualsUtf8()
    {
        using Image<Rgba32> expected = SvgDocument.Load(new MemoryStream(Encoding.UTF8.GetBytes(Shapes))).Render<Rgba32>();

        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Shapes)];
        byte[] utf16BigEndian = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Shapes)];
        foreach (byte[] data in new[] { utf16, utf16BigEndian })
        {
            using Image<Rgba32> loaded = SvgDocument.Load(new MemoryStream(data)).Render<Rgba32>();
            ImageComparer.Exact.VerifySimilarity(expected, loaded);
        }
    }

    private static Image<TPixel> Draw<TPixel>(SvgDocument document, int width, int height, RectangleF? destination, TPixel background)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = new(width, height, background);
        image.Mutate(x => x.Paint(canvas =>
        {
            if (destination is RectangleF rectangle)
            {
                canvas.DrawSvg(document, rectangle);
            }
            else
            {
                canvas.DrawSvg(document, PointF.Empty);
            }
        }));

        return image;
    }
}
