// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests for the paint and content features of SVG rendering: <c>pattern</c>, markers, and <c>image</c>. The reference
/// images were accepted only after a comparison with the Chromium render oracle in <c>tests/SvgTools</c> at the same
/// size. The robustness and security tests check the required behavior directly through <c>DrawSvg</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgPaintContentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    /// <summary>
    /// The feature files of this package, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "pattern")]
    [WithBlankImage(400, 400, PixelTypes.Rgba32, "pattern")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "marker")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "image")]
    public void DrawSvg_PaintContent<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
        using Image<TPixel> image = provider.GetImage();
        string output = $"{name}-{image.Width}";
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, output, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            output,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    /// <summary>
    /// A pattern tile renders at the resolution of the device transform, so a pattern drawn under a rotated and scaled
    /// canvas transform must look like the same pattern drawn by a document that applies the same transform itself.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(160, 160, PixelTypes.Rgba32)]
    public void DrawSvg_PatternFollowsCanvasTransform<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const string pattern = "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"10\" height=\"10\">"
            + "<rect width=\"5\" height=\"10\" fill=\"#1f77b4\"/></pattern>";

        SvgDocument plain = SvgDocument.Parse(SvgOpen + " width=\"80\" height=\"80\">" + pattern
            + "<rect width=\"80\" height=\"80\" fill=\"url(#p)\"/></svg>");
        SvgDocument transformed = SvgDocument.Parse(SvgOpen + " width=\"160\" height=\"160\">" + pattern
            + "<rect width=\"80\" height=\"80\" fill=\"url(#p)\" transform=\"translate(80 0) rotate(30) scale(1.5)\"/></svg>");

        System.Numerics.Matrix4x4 transform = new(
            System.Numerics.Matrix3x2.CreateRotation(MathF.PI / 6F)
            * System.Numerics.Matrix3x2.CreateScale(1.5F)
            * System.Numerics.Matrix3x2.CreateTranslation(80F, 0F));

        using Image<TPixel> viaCanvas = provider.GetImage();
        viaCanvas.Mutate(x => x.Paint(new DrawingOptions { Transform = transform }, canvas => canvas.DrawSvg(plain, PointF.Empty)));

        using Image<TPixel> viaDocument = provider.GetImage();
        viaDocument.Mutate(x => x.Paint(canvas => canvas.DrawSvg(transformed, PointF.Empty)));

        ImageComparer.TolerantPercentage(0.5F).VerifySimilarity(viaDocument, viaCanvas);
    }

    /// <summary>
    /// Concurrent renders of one document with patterns, markers, and images equal a render on one thread. The pattern
    /// tile cache is shared by the renders.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(250, 250, PixelTypes.Rgba32)]
    public void DrawSvg_ConcurrentRendersWithPatternsMatch<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument[] documents =
        [
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/pattern.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/marker.svg")),
            SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/image.svg")),
        ];

        foreach (SvgDocument document in documents)
        {
            // Different sizes ask for different tile resolutions at the same time.
            RectangleF[] destinations = [new(0, 0, 250, 250), new(10, 10, 170, 170), new(0, 0, 250, 250), new(5, 0, 230, 240)];
            Image<TPixel>[] expected = new Image<TPixel>[destinations.Length];
            Image<TPixel>[] actual = new Image<TPixel>[destinations.Length * 4];
            try
            {
                for (int i = 0; i < destinations.Length; i++)
                {
                    RectangleF destination = destinations[i];
                    expected[i] = provider.GetImage();
                    expected[i].Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, destination)));
                }

                Parallel.For(
                    0,
                    actual.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = 8 },
                    i =>
                    {
                        RectangleF destination = destinations[i % destinations.Length];
                        Image<TPixel> image = provider.GetImage();
                        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, destination)));
                        actual[i] = image;
                    });

                for (int i = 0; i < actual.Length; i++)
                {
                    ImageComparer.Exact.VerifySimilarity(expected[i % destinations.Length], actual[i]);
                }
            }
            finally
            {
                foreach (Image<TPixel> image in expected.Concat(actual))
                {
                    image?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// A retained scene keeps the pattern tiles and decoded images that its commands sample, so replaying it after the
    /// draw call gives the same pixels as a direct render.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void DrawSvg_RetainedSceneMatchesDirectRender<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        foreach (string name in new[] { "pattern", "image" })
        {
            SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
            using Image<TPixel> direct = provider.GetImage();
            direct.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

            using Image<TPixel> replayed = provider.GetImage();
            DrawingBackendScene scene = null;
            replayed.Mutate(x => x.Paint(canvas =>
            {
                canvas.DrawSvg(document, PointF.Empty);
                scene = canvas.CreateScene();
            }));

            // Collect garbage between recording and replay: nothing the scene needs may depend on the draw call.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            replayed.Mutate(x => x.Paint(canvas => canvas.RenderScene(scene)));
            scene.Dispose();

            ImageComparer.Exact.VerifySimilarity(direct, replayed);
        }
    }

    /// <summary>
    /// A pattern tile larger than <see cref="SvgLimits.MaxPatternTilePixels"/> renders at a lower resolution instead
    /// of failing.
    /// </summary>
    [Fact]
    public void DrawSvg_PatternTileLimitLowersResolution()
    {
        string svg = SvgOpen + " width=\"100\" height=\"100\">"
            + "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"100\" height=\"100\">"
            + "<rect width=\"50\" height=\"100\" fill=\"#ff0000\"/></pattern>"
            + "<rect width=\"100\" height=\"100\" fill=\"url(#p)\"/></svg>";

        SvgDocument document = SvgDocument.Parse(svg, new SvgLoadOptions { Limits = new SvgLimits { MaxPatternTilePixels = 100 } });
        using Image<Rgba32> image = new(100, 100);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        // A 10 x 10 tile still shows the left half red and the right half empty, away from the blurred middle.
        Assert.Equal(255, image[10, 50].R);
        Assert.Equal(255, image[10, 50].A);
        Assert.Equal(0, image[90, 50].A);
    }

    /// <summary>
    /// Gets documents whose references form cycles: a pattern that references itself through <c>href</c> and through its
    /// content, two patterns that reference each other, a marker whose content uses the marker, two markers that use
    /// each other through <c>use</c>, and a pattern and a marker that reference each other.
    /// </summary>
    public static TheoryData<string> ReferenceCycles { get; } = new()
    {
        "<pattern id=\"p\" href=\"#p\" patternUnits=\"userSpaceOnUse\" width=\"10\" height=\"10\">"
            + "<rect width=\"10\" height=\"10\" fill=\"url(#p)\" stroke=\"#000\"/></pattern>"
            + "<rect width=\"40\" height=\"40\" fill=\"url(#p)\"/>",
        "<pattern id=\"a\" href=\"#b\"/><pattern id=\"b\" href=\"#a\" width=\"1\" height=\"1\">"
            + "<rect width=\"1\" height=\"1\" fill=\"url(#a)\"/></pattern><rect width=\"40\" height=\"40\" fill=\"url(#a)\"/>",
        "<marker id=\"m\" markerWidth=\"9\" markerHeight=\"9\">"
            + "<path d=\"M0 0 L5 5\" stroke=\"#000\" marker-start=\"url(#m)\" marker-end=\"url(#m)\"/></marker>"
            + "<path d=\"M5 5 L30 30\" stroke=\"#000\" marker-start=\"url(#m)\"/>",
        "<marker id=\"a\"><use href=\"#l\"/></marker><marker id=\"b\"><use href=\"#l\"/></marker>"
            + "<path id=\"l\" d=\"M0 0 L5 5\" stroke=\"#000\" marker-start=\"url(#a)\" marker-end=\"url(#b)\"/>",
        "<pattern id=\"p\" width=\"1\" height=\"1\"><path d=\"M0 0 L1 1\" stroke=\"#000\" marker-start=\"url(#m)\"/></pattern>"
            + "<marker id=\"m\"><rect width=\"3\" height=\"3\" fill=\"url(#p)\"/></marker>"
            + "<path d=\"M5 5 L30 30\" stroke=\"url(#p)\" marker-start=\"url(#m)\"/>",
    };

    /// <summary>
    /// Reference cycles through pattern content and <c>href</c>, marker content, and nested SVG images load and render
    /// without failing; the reference that closes a cycle paints nothing.
    /// </summary>
    /// <param name="body">The document content.</param>
    [Theory]
    [MemberData(nameof(ReferenceCycles))]
    public void DrawSvg_ReferenceCyclesDoNotFail(string body)
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"40\" height=\"40\">" + body + "</svg>");
        using Image<Rgba32> image = new(40, 40);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
    }

    /// <summary>
    /// A pattern whose content references the pattern again paints nothing for that reference: the content shape keeps
    /// its stroke and loses its fill, as in Chromium.
    /// </summary>
    [Fact]
    public void DrawSvg_SelfReferencingPatternContentPaintsNoFill()
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + " width=\"40\" height=\"40\">"
            + "<pattern id=\"p\" patternUnits=\"userSpaceOnUse\" width=\"40\" height=\"40\">"
            + "<rect x=\"5\" y=\"5\" width=\"30\" height=\"30\" fill=\"url(#p)\" stroke=\"#00ff00\" stroke-width=\"4\"/></pattern>"
            + "<rect width=\"40\" height=\"40\" fill=\"url(#p)\"/></svg>");

        using Image<Rgba32> image = new(40, 40);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(255, image[5, 20].G);
        Assert.Equal(0, image[20, 20].A);
    }

    /// <summary>
    /// A nested SVG image that references itself through data URIs stops at the nesting limit instead of recursing
    /// without end.
    /// </summary>
    [Fact]
    public void DrawSvg_DeeplyNestedSvgImagesStop()
    {
        // Each level embeds the previous one, so the innermost level is the deepest.
        string inner = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" fill=\"#ff0000\"/></svg>";
        for (int i = 0; i < 8; i++)
        {
            inner = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"10\" height=\"10\">"
                + $"<image width=\"10\" height=\"10\" xlink:href=\"{ToDataUri("image/svg+xml", Encoding.UTF8.GetBytes(inner))}\"/></svg>";
        }

        SvgDocument document = SvgDocument.Parse(inner);
        using Image<Rgba32> image = new(10, 10);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        // Eight levels of images exceed the nesting limit, so the red square at the bottom does not render.
        Assert.Equal(0, image[5, 5].A);
    }

    /// <summary>
    /// A raster image renders through <c>data:</c> URIs in base64 and percent-encoded form, and an image whose media
    /// type disagrees with its content is decoded from its content, as browsers do for raster formats.
    /// </summary>
    /// <param name="encoding">The URI form.</param>
    [Theory]
    [InlineData("base64")]
    [InlineData("percent")]
    [InlineData("wrong-type")]
    [InlineData("no-type")]
    [InlineData("jpeg")]
    public void DrawSvg_ImageDataUriLoads(string encoding)
    {
        byte[] data = encoding == "jpeg" ? CreateJpeg(Color.Blue) : CreatePng(Color.Blue);
        string uri = encoding switch
        {
            "percent" => "data:image/png," + PercentEncode(data),
            "wrong-type" => ToDataUri("image/gif", data),
            "no-type" => ToDataUri(string.Empty, data),
            _ => ToDataUri(encoding == "jpeg" ? "image/jpeg" : "image/png", data),
        };

        Rgba32 pixel = RenderImageCenter(uri, new SvgLoadOptions());
        Assert.True(pixel.B > 200 && pixel.R < 40 && pixel.A == 255, $"The image did not render: {pixel}.");
    }

    /// <summary>
    /// Hostile or broken image data draws nothing and does not fail the document.
    /// </summary>
    /// <param name="kind">The kind of bad data.</param>
    [Theory]
    [InlineData("truncated")]
    [InlineData("huge-declared-size")]
    [InlineData("garbage")]
    [InlineData("svg-type-with-png")]
    [InlineData("bad-base64")]
    [InlineData("empty")]
    [InlineData("broken-svg")]
    [InlineData("gzip-garbage")]
    public void DrawSvg_BrokenImageDrawsNothing(string kind)
    {
        byte[] png = CreatePng(Color.Blue);
        string uri = kind switch
        {
            "truncated" => ToDataUri("image/png", png.AsSpan(0, png.Length / 2).ToArray()),
            "huge-declared-size" => ToDataUri("image/png", CreatePngHeader(100_000, 100_000)),
            "garbage" => ToDataUri("image/png", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
            "svg-type-with-png" => ToDataUri("image/svg+xml", png),
            "bad-base64" => "data:image/png;base64,@@@@",
            "empty" => "data:image/png;base64,",
            "broken-svg" => ToDataUri("image/svg+xml", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect")),
            _ => ToDataUri(string.Empty, [0x1F, 0x8B, 8, 0, 0, 0, 0, 0, 0, 3, 1, 2, 3]),
        };

        Rgba32 pixel = RenderImageCenter(uri, new SvgLoadOptions());
        Assert.Equal(0, pixel.A);
    }

    /// <summary>
    /// <see cref="SvgLimits.MaxImagePixels"/> counts the decoded pixels of all images of the document; an image that
    /// would exceed it draws nothing, and the images before it still draw.
    /// </summary>
    [Fact]
    public void DrawSvg_ImagePixelLimitRefusesImage()
    {
        string first = ToDataUri("image/png", CreatePng(Color.Blue, 10, 10));
        string second = ToDataUri("image/png", CreatePng(Color.Red, 10, 11));
        string svg = SvgOpen + " width=\"20\" height=\"10\">"
            + $"<image width=\"10\" height=\"10\" xlink:href=\"{first}\"/>"
            + $"<image x=\"10\" width=\"10\" height=\"10\" xlink:href=\"{second}\"/></svg>";

        SvgDocument document = SvgDocument.Parse(svg, new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = 150 } });
        using Image<Rgba32> image = new(20, 10);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        Assert.Equal(255, image[5, 5].B);
        Assert.Equal(0, image[15, 5].A);
    }

    /// <summary>
    /// File references load only when <see cref="SvgLoadOptions.ResourceBaseDirectory"/> is set, and only from inside
    /// that directory. Every way of leaving the directory, or of naming a resource that is not a relative file, is
    /// refused.
    /// </summary>
    /// <param name="reference">The <c>href</c>, with <c>{outside}</c> standing for the absolute path of a file outside the base directory.</param>
    /// <param name="loads">Whether the image is expected to load.</param>
    [Theory]
    [InlineData("inside.png", true)]
    [InlineData("sub/inside.png", true)]
    [InlineData("sub%2Finside.png", true)]
    [InlineData("./sub/../inside.png", true)]
    [InlineData("../outside.png", false)]
    [InlineData("sub/../../outside.png", false)]
    [InlineData("sub\\..\\..\\outside.png", false)]
    [InlineData("sub/..\\../outside.png", false)]
    [InlineData("%2e%2e/outside.png", false)]
    [InlineData("..%2Foutside.png", false)]
    [InlineData("..%5Coutside.png", false)]
    [InlineData("sub%2F..%2F..%2Foutside.png", false)]
    [InlineData("{outside}", false)]
    [InlineData("file://{outside}", false)]
    [InlineData("file:inside.png", false)]
    [InlineData("http://127.0.0.1:1/inside.png", false)]
    [InlineData("https://example.com/inside.png", false)]
    [InlineData("//server/share/inside.png", false)]
    [InlineData("C:/inside.png", false)]
    [InlineData("inside.png?x=1", false)]
    [InlineData("inside.png#frag", false)]
    [InlineData("inside.png%00.txt", false)]
    [InlineData("missing.png", false)]
    public void DrawSvg_ImageFileReferenceRules(string reference, bool loads)
    {
        using TemporaryDirectory directory = new();
        string baseDirectory = IOPath.Combine(directory.Path, "base");
        Directory.CreateDirectory(IOPath.Combine(baseDirectory, "sub"));
        byte[] png = CreatePng(Color.Blue);
        File.WriteAllBytes(IOPath.Combine(baseDirectory, "inside.png"), png);
        File.WriteAllBytes(IOPath.Combine(baseDirectory, "sub", "inside.png"), png);
        string outside = IOPath.Combine(directory.Path, "outside.png");
        File.WriteAllBytes(outside, png);

        string href = reference.Replace("{outside}", outside.Replace('\\', '/'));
        Rgba32 pixel = RenderImageCenter(href, new SvgLoadOptions { ResourceBaseDirectory = baseDirectory });
        Assert.Equal(loads ? 255 : 0, pixel.A);

        // Without a base directory no file is ever read, whatever the reference.
        Assert.Equal(0, RenderImageCenter(href, new SvgLoadOptions()).A);
    }

    /// <summary>
    /// A symbolic link inside the base directory that points outside it is refused.
    /// </summary>
    [Fact]
    public void DrawSvg_ImageSymbolicLinkLeavingBaseIsRefused()
    {
        using TemporaryDirectory directory = new();
        string baseDirectory = IOPath.Combine(directory.Path, "base");
        Directory.CreateDirectory(baseDirectory);
        string outside = IOPath.Combine(directory.Path, "outside.png");
        File.WriteAllBytes(outside, CreatePng(Color.Blue));
        File.WriteAllBytes(IOPath.Combine(baseDirectory, "real.png"), CreatePng(Color.Blue));

        try
        {
            File.CreateSymbolicLink(IOPath.Combine(baseDirectory, "link.png"), outside);
            File.CreateSymbolicLink(IOPath.Combine(baseDirectory, "inner.png"), IOPath.Combine(baseDirectory, "real.png"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creating links needs a privilege on Windows; without it there is nothing to test.
            return;
        }

        SvgLoadOptions options = new() { ResourceBaseDirectory = baseDirectory };
        Assert.Equal(0, RenderImageCenter("link.png", options).A);
        Assert.Equal(255, RenderImageCenter("inner.png", options).A);
    }

    /// <summary>
    /// An SVG file can reference itself as an image. The nested document never reads files, so the recursion stops at
    /// the first level.
    /// </summary>
    [Fact]
    public void DrawSvg_SelfReferencingSvgFileStops()
    {
        using TemporaryDirectory directory = new();
        string path = IOPath.Combine(directory.Path, "self.svg");
        string svg = SvgOpen + " width=\"20\" height=\"20\">"
            + "<rect width=\"10\" height=\"10\" fill=\"#ff0000\"/>"
            + "<image x=\"10\" y=\"10\" width=\"10\" height=\"10\" xlink:href=\"self.svg\"/></svg>";
        File.WriteAllText(path, svg);

        SvgDocument document = SvgDocument.Load(path, new SvgLoadOptions { ResourceBaseDirectory = directory.Path });
        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));

        // The first level renders a scaled copy whose own image is not loaded.
        Assert.Equal(255, image[5, 5].R);
        Assert.Equal(255, image[12, 12].R);
        Assert.Equal(0, image[17, 17].A);
    }

    /// <summary>
    /// Draws a 10 x 10 image element with an <c>href</c> into a 10 x 10 image and returns the center pixel.
    /// </summary>
    /// <param name="href">The reference.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The center pixel.</returns>
    private static Rgba32 RenderImageCenter(string href, SvgLoadOptions options)
    {
        string escaped = href.Replace("&", "&amp;").Replace("\"", "&quot;");
        string svg = SvgOpen + $" width=\"10\" height=\"10\"><image width=\"10\" height=\"10\" xlink:href=\"{escaped}\"/></svg>";
        SvgDocument document = SvgDocument.Parse(svg, options);

        using Image<Rgba32> image = new(10, 10);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty)));
        return image[5, 5];
    }

    /// <summary>
    /// Encodes a solid PNG image.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The PNG file.</returns>
    private static byte[] CreatePng(Color color, int width = 4, int height = 4)
    {
        using Image<Rgba32> image = new(width, height, color.ToPixel<Rgba32>());
        using MemoryStream stream = new();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes a solid JPEG image.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The JPEG file.</returns>
    private static byte[] CreateJpeg(Color color)
    {
        using Image<Rgba32> image = new(8, 8, color.ToPixel<Rgba32>());
        using MemoryStream stream = new();
        image.Save(stream, new JpegEncoder { Quality = 100 });
        return stream.ToArray();
    }

    /// <summary>
    /// Builds the start of a PNG file whose header declares a size, with no image data, as a hostile file that claims a
    /// huge size.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <returns>The bytes.</returns>
    private static byte[] CreatePngHeader(int width, int height)
    {
        byte[] png = CreatePng(Color.Blue);

        // The IHDR chunk follows the 8-byte signature and the 8-byte chunk length and type. Its CRC is left wrong on
        // purpose; a decoder must refuse the file either way.
        byte[] result = png.AsSpan(0, 33).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(20), height);
        return result;
    }

    /// <summary>
    /// Builds a base64 <c>data:</c> URI.
    /// </summary>
    /// <param name="mediaType">The media type.</param>
    /// <param name="data">The data.</param>
    /// <returns>The URI.</returns>
    private static string ToDataUri(string mediaType, byte[] data) => $"data:{mediaType};base64,{Convert.ToBase64String(data)}";

    /// <summary>
    /// Percent-encodes every byte.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The encoded text.</returns>
    private static string PercentEncode(byte[] data)
    {
        StringBuilder builder = new(data.Length * 3);
        foreach (byte b in data)
        {
            _ = builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// A new directory under the test output directory that is deleted on dispose.
    /// </summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TemporaryDirectory"/> class.
        /// </summary>
        public TemporaryDirectory()
            => this.Path = TestEnvironment.CreateOutputDirectory("Svg", "ImageFiles", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// Gets the full path.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, true);
            }
            catch (IOException)
            {
                // A file still open on a slow file system is left to the system cleanup.
            }
        }
    }
}
