// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests <see cref="SvgDecoder"/> and <see cref="SvgDocument.Render{TPixel}(Configuration, Size, Color)"/>. Expected
/// pixels come from <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/> on an image of the same size and pixel
/// format; expected sizes come from the PNG decoder, which applies the target size rule of the ImageSharp decoders.
/// </summary>
public class SvgDecoderTests
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

    public static TheoryData<int, int> TargetSizes => new()
    {
        { 100, 100 },
        { 100, 0 },
        { 0, 60 },
        { 1000, 30 },
        { 10, 10 },
        { 40, 100 },
        { 77, 20 },
        { 39, 19 },
    };

    /// <summary>
    /// Gets the decoder through its public interface.
    /// </summary>
    internal static ISpecializedImageDecoder<SvgDecoderOptions> Decoder => SvgDecoder.Instance;

    [Fact]
    public void Decode_AtTheIntrinsicSize_EqualsDrawSvg()
    {
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        SvgDocument document = SvgDocument.Parse(Shapes);

        using Image<Rgba32> decoded = Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, Encoding.UTF8.GetBytes(Shapes));
        using Image<Rgba32> expected = Draw<Rgba32>(document, 40, 20, null, default);

        Assert.Same(SvgFormat.Instance, decoded.Metadata.DecodedImageFormat);
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    [Fact]
    public void Decode_CorpusFile_EqualsDrawSvg()
    {
        string path = TestFile.GetInputFileFullPath(TestImages.Svg.FluentSoftIceCream);
        SvgDocument document = SvgDocument.Load(path);
        Size size = document.GetPixelSize();

        using Image<Rgba32> decoded = Image.Load<Rgba32>(new DecoderOptions { Configuration = SvgImageFormatDetectorTests.CreateConfiguration() }, path);
        using Image<Rgba32> expected = Draw<Rgba32>(document, size.Width, size.Height, null, default);

        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    /// <summary>
    /// A document with text, a raster image, filters, custom properties, color functions, and basic-shape clips decodes as
    /// it draws, at the intrinsic size and at a target size. The drawn output at both sizes was compared with the Chromium
    /// render oracle; the intrinsic size has a reference image in <see cref="SvgCrossFeatureRenderTests"/>.
    /// </summary>
    /// <param name="targetWidth">The width of the target size, or 0 for none.</param>
    /// <param name="targetHeight">The height of the target size.</param>
    /// <param name="width">The expected image width.</param>
    /// <param name="height">The expected image height.</param>
    [Theory]
    [InlineData(0, 0, 160, 120)]
    [InlineData(400, 400, 400, 300)]
    public void Decode_DocumentWithEveryFeatureKind_EqualsDrawSvg(int targetWidth, int targetHeight, int width, int height)
    {
        string path = TestFile.GetInputFileFullPath("Svg/Features/cross-decoder.svg");
        SvgDocument document = SvgDocument.Load(path, SvgSvg2RenderTests.TextOptions);
        DecoderOptions general = new()
        {
            Configuration = SvgImageFormatDetectorTests.CreateConfiguration(),
            TargetSize = targetWidth > 0 ? new Size(targetWidth, targetHeight) : null,
        };

        SvgDecoderOptions options = new() { GeneralOptions = general, LoadOptions = SvgSvg2RenderTests.TextOptions };
        using FileStream stream = File.OpenRead(path);
        using Image<Rgba32> decoded = Decoder.Decode<Rgba32>(options, stream);
        RectangleF? destination = targetWidth > 0 ? new RectangleF(0, 0, width, height) : null;
        using Image<Rgba32> expected = Draw<Rgba32>(document, width, height, destination, default);

        Assert.Equal(new Size(width, height), decoded.Size);
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    [Fact]
    public void Decode_FractionalSize_RoundsUpAndDrawsAtTheExactSize()
    {
        string svg = "<svg " + SvgNamespace + " width=\"10.5\" height=\"7.25\"><rect width=\"100%\" height=\"100%\" fill=\"#f00\"/></svg>";
        SvgDocument document = SvgDocument.Parse(svg);

        using Image<Rgba32> decoded = Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new MemoryStream(Encoding.UTF8.GetBytes(svg)));
        using Image<Rgba32> expected = Draw<Rgba32>(document, 11, 8, null, default);

        Assert.Equal(new Size(11, 8), decoded.Size);
        ImageComparer.Exact.VerifySimilarity(expected, decoded);

        // The last column is only partly covered; the content is not stretched over it.
        Assert.Equal(255, decoded[0, 0].A);
        Assert.InRange(decoded[10, 0].A, 1, 254);

        // An excess that unit conversion adds does not add a pixel.
        Assert.Equal(new Size(96, 96), SvgDocument.Parse("<svg " + SvgNamespace + " width=\"2.54cm\" height=\"25.4mm\"/>").GetPixelSize());
    }

    [Theory]
    [MemberData(nameof(TargetSizes))]
    public void Decode_AtATargetSize_FitsLikeTheRasterDecoders(int targetWidth, int targetHeight)
    {
        Size target = new(targetWidth, targetHeight);
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        DecoderOptions options = new() { Configuration = configuration, TargetSize = target };

        // A PNG of the intrinsic size gets the size of the ImageSharp target size rule.
        using Image<Rgba32> png = Image.Load<Rgba32>(options, CreatePng(40, 20));
        Size expectedSize = png.Size;

        using Image<Rgba32> decoded = Image.Load<Rgba32>(options, Encoding.UTF8.GetBytes(Shapes));
        Assert.Equal(expectedSize, decoded.Size);

        // The vector content renders at that size: the view box fits the whole image.
        SvgDocument document = SvgDocument.Parse(Shapes);
        RectangleF? destination = expectedSize == new Size(40, 20) ? null : new RectangleF(0, 0, expectedSize.Width, expectedSize.Height);
        using Image<Rgba32> expected = Draw<Rgba32>(document, expectedSize.Width, expectedSize.Height, destination, default);
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    /// <summary>
    /// At a target size far above the intrinsic size the edges stay sharp: the document renders at the target size and is
    /// not rendered small and resampled.
    /// </summary>
    [Fact]
    public void Decode_AtALargeTargetSize_RendersTheVectorContent()
    {
        string svg = "<svg " + SvgNamespace + " width=\"10\" height=\"10\"><circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"#f00\"/></svg>";
        DecoderOptions options = new() { Configuration = SvgImageFormatDetectorTests.CreateConfiguration(), TargetSize = new Size(1000, 1000) };

        using Image<Rgba32> decoded = Image.Load<Rgba32>(options, Encoding.UTF8.GetBytes(svg));
        using Image<Rgba32> drawn = Draw<Rgba32>(SvgDocument.Parse(svg), 1000, 1000, new RectangleF(0, 0, 1000, 1000), default);
        ImageComparer.Exact.VerifySimilarity(drawn, decoded);

        // Rendered small and scaled up, the antialiased edge of the 10 pixel circle spreads over many pixels.
        using Image<Rgba32> resampled = SvgDocument.Parse(svg).Render<Rgba32>();
        resampled.Mutate(x => x.Resize(1000, 1000, KnownResamplers.Bicubic));

        Assert.Equal(1000, decoded.Width);
        Assert.InRange(CountPartial(decoded, 500), 1, 4);
        Assert.True(CountPartial(resampled, 500) > 50, $"{CountPartial(resampled, 500)} partial pixels.");
        Assert.Equal(255, decoded[500, 500].A);
        Assert.Equal(0, decoded[50, 500].A);

        static int CountPartial(Image<Rgba32> image, int y)
        {
            int count = 0;
            for (int x = 0; x < image.Width; x++)
            {
                if (image[x, y].A is > 0 and < 255)
                {
                    count++;
                }
            }

            return count;
        }
    }

    [Fact]
    public void Decode_PixelFormatsAndBackground_EqualDrawSvg()
    {
        AssertPixelFormat<Rgba32>(Color.Transparent);
        AssertPixelFormat<Rgba32>(Color.White);
        AssertPixelFormat<Bgra32>(Color.ParseHex("#336699cc"));
        AssertPixelFormat<Rgb24>(Color.White);
        AssertPixelFormat<L8>(Color.White);
        AssertPixelFormat<RgbaVector>(Color.Transparent);
        AssertPixelFormat<RgbaVector>(Color.Yellow);

        // The background fills the pixels that the document leaves transparent.
        using Image<Rgb24> image = Decoder.Decode<Rgb24>(
            new SvgDecoderOptions { BackgroundColor = Color.White },
            new MemoryStream(Encoding.UTF8.GetBytes(Shapes)));
        Assert.Equal(new Rgb24(255, 255, 255), image[0, 19]);
    }

    [Fact]
    public void Decode_WithoutAPixelType_IsRgba32()
    {
        DecoderOptions options = new() { Configuration = SvgImageFormatDetectorTests.CreateConfiguration() };
        using Image image = Image.Load(options, Encoding.UTF8.GetBytes(Shapes));
        using Image decodedDirectly = Decoder.Decode(new SvgDecoderOptions(), new MemoryStream(Encoding.UTF8.GetBytes(Shapes)));

        Image<Rgba32> loaded = Assert.IsType<Image<Rgba32>>(image);
        Image<Rgba32> decoded = Assert.IsType<Image<Rgba32>>(decodedDirectly);
        ImageComparer.Exact.VerifySimilarity(decoded, loaded);
    }

    [Fact]
    public void Decode_GzipAndUtf16_EqualTheUncompressedDocument()
    {
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        DecoderOptions options = new() { Configuration = configuration };
        byte[] utf8 = Encoding.UTF8.GetBytes(Shapes);
        using Image<Rgba32> expected = Image.Load<Rgba32>(options, utf8);

        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Shapes)];
        byte[] utf16BigEndian = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Shapes)];
        foreach (byte[] data in new[] { SvgImageFormatDetectorTests.Gzip(utf8), utf16, utf16BigEndian, SvgImageFormatDetectorTests.Gzip(utf16) })
        {
            using Image<Rgba32> decoded = Image.Load<Rgba32>(options, data);
            ImageComparer.Exact.VerifySimilarity(expected, decoded);
        }

        // SvgDocument.Load reads only uncompressed text; gzip data is not XML.
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Load(new MemoryStream(SvgImageFormatDetectorTests.Gzip(utf8))));
    }

    [Fact]
    public void Decode_StreamsThatCannotSeek()
    {
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        byte[] data = Encoding.UTF8.GetBytes(Shapes);
        byte[] gzip = SvgImageFormatDetectorTests.Gzip(data);
        using Image<Rgba32> expected = SvgDocument.Parse(Shapes).Render<Rgba32>();

        using Image<Rgba32> loaded = Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, new ForwardOnlyStream(data));
        using Image<Rgba32> loadedGzip = Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, new ForwardOnlyStream(gzip));
        using Image<Rgba32> decoded = Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new ForwardOnlyStream(gzip));
        ImageInfo info = Image.Identify(new DecoderOptions { Configuration = configuration }, new ForwardOnlyStream(data));

        ImageComparer.Exact.VerifySimilarity(expected, loaded);
        ImageComparer.Exact.VerifySimilarity(expected, loadedGzip);
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
        Assert.Equal(new Size(40, 20), info.Size);
    }

    [Fact]
    public async Task AsyncEntryPoints_EqualTheSynchronousOnes()
    {
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        DecoderOptions options = new() { Configuration = configuration, TargetSize = new Size(80, 80) };
        byte[] data = Encoding.UTF8.GetBytes(Shapes);
        using Image<Rgba32> expected = Image.Load<Rgba32>(options, data);

        CancellationToken token = TestContext.Current.CancellationToken;
        using Image<Rgba32> loaded = await Image.LoadAsync<Rgba32>(options, new ForwardOnlyStream(data), token);
        using Image loadedUntyped = await Image.LoadAsync(options, new MemoryStream(data), token);
        SvgDecoderOptions svgOptions = new() { GeneralOptions = options };
        using Image<Rgba32> decoded = await Decoder.DecodeAsync<Rgba32>(svgOptions, new MemoryStream(data), token);
        ImageInfo info = await Image.IdentifyAsync(options, new ForwardOnlyStream(data), token);
        IImageFormat format = await Image.DetectFormatAsync(options, new ForwardOnlyStream(data), token);

        ImageComparer.Exact.VerifySimilarity(expected, loaded);
        ImageComparer.Exact.VerifySimilarity(expected, Assert.IsType<Image<Rgba32>>(loadedUntyped));
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
        Assert.Equal(new Size(40, 20), info.Size);
        Assert.Same(SvgFormat.Instance, format);
    }

    [Fact]
    public async Task Cancellation_IsCheckedBeforeLoadingAndBeforeRendering()
    {
        byte[] data = Encoding.UTF8.GetBytes(Shapes);
        SvgDecoderOptions options = new();

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Decoder.DecodeAsync<Rgba32>(options, new MemoryStream(data), cancelled.Token));

        // The token is cancelled while the document loads, when the reader reaches the end of the data. The decoder
        // stops before it renders.
        using CancellationTokenSource duringLoad = new();
        CancelAtEndStream stream = new(data, duringLoad);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Decoder.DecodeAsync<Rgba32>(options, stream, duringLoad.Token));
        Assert.True(stream.ReachedEnd);

        // The same stream without cancellation decodes.
        using Image<Rgba32> image = await Decoder.DecodeAsync<Rgba32>(options, new CancelAtEndStream(data, null), CancellationToken.None);
        Assert.Equal(new Size(40, 20), image.Size);
    }

    [Fact]
    public void Identify_ReturnsTheIntrinsicSizeWithoutRendering()
    {
        TestMemoryAllocator allocator = new();
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        configuration.MemoryAllocator = allocator;
        DecoderOptions options = new() { Configuration = configuration, TargetSize = new Size(10, 10) };

        // The size is far above the pixel limit: rendering would fail, identifying does not.
        byte[] huge = Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " width=\"100000.2\" height=\"50000\"><rect width=\"10\" height=\"10\"/></svg>");
        ImageInfo info = Image.Identify(options, huge);

        Assert.Equal(new Size(100001, 50000), info.Size);
        Assert.Same(SvgFormat.Instance, info.Metadata.DecodedImageFormat);
        Assert.Equal(PixelResolutionUnit.PixelsPerInch, info.Metadata.ResolutionUnits);
        Assert.Equal(96, info.Metadata.HorizontalResolution);
        Assert.Equal(96, info.Metadata.VerticalResolution);
        Assert.Empty(allocator.AllocationLog);

        // Without width, height, and view box the size is the browser default.
        Assert.Equal(new Size(300, 150), Image.Identify(options, Encoding.UTF8.GetBytes("<svg " + SvgNamespace + "/>")).Size);
        Assert.Equal(new Size(30, 15), Image.Identify(options, Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " viewBox=\"0 0 30 15\"/>")).Size);
    }

    [Fact]
    public void Decode_SetsTheResolutionOfTheLoadOptions()
    {
        string svg = "<svg " + SvgNamespace + " width=\"1in\" height=\"0.5in\"/>";
        using Image<Rgba32> image = Decoder.Decode<Rgba32>(
            new SvgDecoderOptions { LoadOptions = new SvgLoadOptions { Dpi = 300 } },
            new MemoryStream(Encoding.UTF8.GetBytes(svg)));

        Assert.Equal(new Size(300, 150), image.Size);
        Assert.Equal(PixelResolutionUnit.PixelsPerInch, image.Metadata.ResolutionUnits);
        Assert.Equal(300, image.Metadata.HorizontalResolution);
        Assert.Equal(300, image.Metadata.VerticalResolution);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<svg")]
    [InlineData("<svg/>")]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"0\" height=\"10\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1e30\" height=\"10\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"3e9\" height=\"1\"/>")]
    public void InvalidDocuments_ThrowInvalidImageContent(string svg)
    {
        byte[] data = Encoding.UTF8.GetBytes(svg);
        byte[] gzip = SvgImageFormatDetectorTests.Gzip(data);
        Assert.Throws<InvalidImageContentException>(() => Decode(data));
        Assert.Throws<InvalidImageContentException>(() => Decode(gzip));
        Assert.Throws<InvalidImageContentException>(() => Decoder.Identify(new DecoderOptions(), new MemoryStream(data)));
    }

    [Fact]
    public void OtherFormatsAndCorruptGzip_ThrowInvalidImageContent()
    {
        byte[] gzip = SvgImageFormatDetectorTests.Gzip(Encoding.UTF8.GetBytes(Shapes));
        byte[][] inputs =
        [
            CreatePng(4, 4),
            gzip.AsSpan(0, gzip.Length / 2).ToArray(),
            [.. gzip.AsSpan(0, 10), .. Enumerable.Repeat((byte)0xA5, 64)],
            [0x1F, 0x8B],
        ];

        foreach (byte[] input in inputs)
        {
            Assert.Throws<InvalidImageContentException>(() => Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new MemoryStream(input)));
        }

        // Through format detection, data that no detector recognizes is an unknown format.
        DecoderOptions options = new() { Configuration = SvgImageFormatDetectorTests.CreateConfiguration() };
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(options, Encoding.UTF8.GetBytes("<html/>")));
    }

    [Fact]
    public void OutputSize_IsBoundedBeforeAllocation()
    {
        TestMemoryAllocator allocator = new();
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        configuration.MemoryAllocator = allocator;

        // The intrinsic size exceeds the default image pixel limit.
        byte[] huge = Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " width=\"100000\" height=\"100000\"><rect width=\"10\" height=\"10\"/></svg>");
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, huge));

        // A target size with one free side fits a very tall document to a size over the limit.
        byte[] tall = Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " width=\"1\" height=\"1000\"/>");
        DecoderOptions fit = new() { Configuration = configuration, TargetSize = new Size(1000, 0) };
        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(fit, tall));
        Assert.Contains("1000 x 1000000", exception.Message, StringComparison.Ordinal);

        // A side is bounded apart from the pixel count: the scratch memory of the rasterizer grows with the width.
        byte[] wide = Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " width=\"65536\" height=\"1\"/>");
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, wide));

        // The limit comes from the load options and includes its value.
        SvgDecoderOptions belowLimit = new()
        {
            GeneralOptions = new DecoderOptions { Configuration = configuration },
            LoadOptions = new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = (40 * 20) - 1 } },
        };
        Assert.Throws<InvalidImageContentException>(() => Decoder.Decode<Rgba32>(belowLimit, new MemoryStream(Encoding.UTF8.GetBytes(Shapes))));

        // No case allocated pixel memory. The allocator logs the buffers of an image.
        Assert.Empty(allocator.AllocationLog);
        using (Image<Rgba32> logged = new(configuration, 2, 2))
        {
            Assert.NotEmpty(allocator.AllocationLog);
        }

        SvgDecoderOptions atLimit = new() { LoadOptions = new SvgLoadOptions { Limits = new SvgLimits { MaxImagePixels = 40 * 20 } } };
        using Image<Rgba32> image = Decoder.Decode<Rgba32>(atLimit, new MemoryStream(Encoding.UTF8.GetBytes(Shapes)));
        Assert.Equal(new Size(40, 20), image.Size);

        byte[] widest = Encoding.UTF8.GetBytes("<svg " + SvgNamespace + " width=\"65535\" height=\"1\"/>");
        using Image<Rgba32> widestImage = Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new MemoryStream(widest));
        Assert.Equal(new Size(65535, 1), widestImage.Size);
    }

    [Fact]
    public void LayerLimit_ThrowsInvalidImageContent()
    {
        // Two nested groups with opacity need two layers of about 40 x 20 pixels.
        SvgDecoderOptions options = new() { LoadOptions = new SvgLoadOptions { Limits = new SvgLimits { MaxLayerPixels = 1000 } } };
        Assert.Throws<InvalidImageContentException>(() => Decoder.Decode<Rgba32>(options, new MemoryStream(Encoding.UTF8.GetBytes(
            "<svg " + SvgNamespace + " width=\"40\" height=\"20\"><g opacity=\"0.5\"><rect width=\"40\" height=\"20\"/><rect width=\"30\" height=\"20\"/>"
            + "<g opacity=\"0.5\"><rect width=\"40\" height=\"20\"/><rect width=\"30\" height=\"20\"/></g></g></svg>"))));
    }

    /// <summary>
    /// The decoder never sets the resource base directory, also for a file: a relative image reference next to the file
    /// is not read unless the caller sets the directory.
    /// </summary>
    [Fact]
    public void RelativeImageReference_IsNotLoadedByDefault()
    {
        string directory = TestEnvironment.CreateOutputDirectory("Svg", "Decoder", Guid.NewGuid().ToString("N"));
        try
        {
            string path = IOPath.Combine(directory, "document.svg");
            File.WriteAllBytes(IOPath.Combine(directory, "blue.png"), CreatePng(4, 4, Color.Blue));
            string svg = "<svg " + SvgNamespace + " xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"10\" height=\"10\">"
                + "<image width=\"10\" height=\"10\" xlink:href=\"blue.png\"/></svg>";
            File.WriteAllText(path, svg);

            using Image<Rgba32> loaded = Image.Load<Rgba32>(new DecoderOptions { Configuration = SvgImageFormatDetectorTests.CreateConfiguration() }, path);
            using FileStream stream = File.OpenRead(path);
            using Image<Rgba32> decoded = Decoder.Decode<Rgba32>(new SvgDecoderOptions(), stream);
            Assert.Equal(0, loaded[5, 5].A);
            Assert.Equal(0, decoded[5, 5].A);

            // With the directory set by the caller, the image loads.
            stream.Position = 0;
            SvgDecoderOptions withDirectory = new() { LoadOptions = new SvgLoadOptions { ResourceBaseDirectory = directory } };
            using Image<Rgba32> allowed = Decoder.Decode<Rgba32>(withDirectory, stream);
            Assert.Equal(new Rgba32(0, 0, 255, 255), allowed[5, 5]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Options_HaveDefaultsAndValidate()
    {
        SvgDecoderOptions options = new();

        Assert.NotNull(options.GeneralOptions);
        Assert.NotNull(options.LoadOptions);
        Assert.Null(options.LoadOptions.ResourceBaseDirectory);
        Assert.Equal(Color.Transparent, options.BackgroundColor);
        Assert.Throws<ArgumentNullException>(() => new SvgDecoderOptions { LoadOptions = null! });
        Assert.Throws<ArgumentNullException>(() => new SvgConfigurationModule().Configure(null!));
    }

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

    private static void AssertPixelFormat<TPixel>(Color background)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(Shapes);
        SvgDecoderOptions options = new()
        {
            GeneralOptions = new DecoderOptions { TargetSize = new Size(80, 0) },
            BackgroundColor = background,
        };

        using Image<TPixel> decoded = Decoder.Decode<TPixel>(options, new MemoryStream(Encoding.UTF8.GetBytes(Shapes)));
        using Image<TPixel> expected = Draw(document, 80, 40, new RectangleF(0, 0, 80, 40), background.ToPixel<TPixel>());
        ImageComparer.Exact.VerifySimilarity(expected, decoded);
    }

    private static Image<Rgba32> Decode(byte[] data)
        => Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new MemoryStream(data));

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

    private static byte[] CreatePng(int width, int height, Color? color = null)
    {
        using Image<Rgba32> image = new(width, height, (color ?? Color.Red).ToPixel<Rgba32>());
        using MemoryStream stream = new();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// A stream that can only read forward.
    /// </summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="ForwardOnlyStream"/> class.
        /// </summary>
        /// <param name="data">The data.</param>
        public ForwardOnlyStream(byte[] data) => this.inner = new MemoryStream(data);

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, count);

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A memory stream that cancels a token source when a read reaches the end of the data.
    /// </summary>
    private sealed class CancelAtEndStream : MemoryStream
    {
        private readonly CancellationTokenSource source;

        /// <summary>
        /// Initializes a new instance of the <see cref="CancelAtEndStream"/> class.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="source">The token source to cancel, or <see langword="null"/>.</param>
        public CancelAtEndStream(byte[] data, CancellationTokenSource source)
            : base(data) => this.source = source;

        /// <summary>
        /// Gets a value indicating whether a read reached the end of the data.
        /// </summary>
        public bool ReachedEnd { get; private set; }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => this.OnRead(base.Read(buffer, offset, count), count);

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer) => this.OnRead(base.Read(buffer), buffer.Length);

        private int OnRead(int read, int requested)
        {
            if (read == 0 && requested > 0)
            {
                this.ReachedEnd = true;
                this.source?.Cancel();
            }

            return read;
        }
    }
}
