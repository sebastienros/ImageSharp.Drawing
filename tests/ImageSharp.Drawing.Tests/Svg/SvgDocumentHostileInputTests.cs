// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile input for <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and
/// <see cref="SvgDocument.Render{TPixel}(Configuration, Color)"/>: every case either renders or throws
/// <see cref="InvalidImageContentException"/>, and work that a limit stops does not grow with the input. Time comparisons
/// use <see cref="SvgGrowth"/>, so they hold on a loaded machine.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgDocumentHostileInputTests
{
    private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDocumentHostileInputTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgDocumentHostileInputTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TruncatedFiles_RenderOrThrowInvalidImageContent()
    {
        byte[] data = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Svg.CarbonCrab));
        int rendered = 0;
        for (int length = 0; length < data.Length; length += 13)
        {
            rendered += RenderOrInvalid(data.AsSpan(0, length).ToArray());
        }

        // Only cuts in the white space after the root element leave a whole document.
        this.output.WriteLine($"{rendered} truncated inputs rendered.");
        Assert.True(rendered <= 3, $"{rendered} truncated inputs rendered.");
        Assert.Equal(1, RenderOrInvalid(data));
    }

    [Fact]
    public void MalformedFiles_RenderOrThrowInvalidImageContent()
    {
        byte[] data = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Svg.CarbonCrab));
        Random random = new(7);
        for (int i = 0; i < 200; i++)
        {
            byte[] copy = (byte[])data.Clone();
            for (int j = 0; j < 4; j++)
            {
                copy[random.Next(copy.Length)] = (byte)random.Next(256);
            }

            RenderOrInvalid(copy);
        }
    }

    [Fact]
    public void BinaryGarbageThatStartsLikeXml_ThrowsInvalidImageContent()
    {
        Random random = new(11);
        byte[] garbage = new byte[4096];
        random.NextBytes(garbage);

        byte[] declaration = [.. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>"), .. garbage];
        Assert.Throws<InvalidImageContentException>(() => Render(declaration));

        byte[] root = [.. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + Open), .. garbage];
        Assert.Throws<InvalidImageContentException>(() => Render(root));

        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Open), .. garbage];
        Assert.Throws<InvalidImageContentException>(() => Render(utf16));
    }

    [Theory]
    [InlineData("1000000", "1000000")]
    [InlineData("1e38", "1")]
    [InlineData("1", "3e38")]
    [InlineData("100000in", "1")]
    [InlineData("8000000", "8")]
    public void HugeIntrinsicSize_ThrowsBeforeAllocation(string width, string height)
    {
        TestMemoryAllocator allocator = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\"><rect width=\"100%\" height=\"100%\"/></svg>";

        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(svg).Render<Rgba32>(configuration, Color.Transparent));
        Assert.Empty(allocator.AllocationLog);
    }

    /// <summary>
    /// A small gzip-compressed SVG image that inflates far beyond the limit stops when the limit is reached, and the image
    /// draws nothing: data that inflates to eight times as much takes about as long.
    /// </summary>
    [Fact]
    public void GzipBombInAnImage_StopsAtTheInflateLimit()
    {
        int limitMegabytes = SvgResolver.MaxDecompressedSvgBytes / (1024 * 1024);
        string under = ImageDocument(CreateBomb(limitMegabytes / 8));
        string small = ImageDocument(CreateBomb(limitMegabytes * 2));
        string large = ImageDocument(CreateBomb(limitMegabytes * 16));
        this.output.WriteLine($"{small.Length} characters inflate to {limitMegabytes * 2} MiB, {large.Length} characters to {limitMegabytes * 16} MiB.");
        Assert.True(large.Length < 3 * 1024 * 1024);

        // Data under the limit draws, so every member of the concatenated gzip data is read.
        Assert.Equal(255, RenderCenter(under).A);
        Assert.Equal(0, RenderCenter(small).A);

        TimeSpan smallTime = SvgGrowth.MeasureWarm(this.output, "two times the limit", () => Assert.Equal(0, RenderCenter(small).A));
        TimeSpan largeTime = SvgGrowth.Measure(this.output, "sixteen times the limit", () => Assert.Equal(0, RenderCenter(large).A));
        SvgGrowth.AssertBounded(smallTime, largeTime);
    }

    /// <summary>
    /// A document that needs more layer memory than the default limit fails before it draws.
    /// </summary>
    [Fact]
    public void LayerMemoryOverTheDefaultLimit_ThrowsInvalidImageContent()
    {
        // Each nested group with opacity has two overlapping children, so each needs a layer of the whole image. 300
        // layers of 1000 x 1000 pixels are more than the default of 16 x 4096 x 4096 pixels.
        StringBuilder svg = new("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"1000\">");
        for (int i = 0; i < 300; i++)
        {
            svg.Append("<g opacity=\"0.9\"><rect width=\"1000\" height=\"1000\" fill=\"red\"/>");
        }

        svg.Append("<rect width=\"10\" height=\"10\"/>");
        for (int i = 0; i < 300; i++)
        {
            svg.Append("</g>");
        }

        svg.Append("</svg>");
        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(() => Render(Encoding.UTF8.GetBytes(svg.ToString())));
        this.output.WriteLine(exception.Message);
    }

    /// <summary>
    /// A long prolog before the root element loads in time that grows linearly.
    /// </summary>
    [Fact]
    public void LongProlog_LoadsInLinearTime()
    {
        byte[] small = CreateLongProlog(1 << 20);
        byte[] large = CreateLongProlog(8 << 20);

        TimeSpan smallTime = SvgGrowth.MeasureWarm(this.output, "1 MiB prolog", () => Render(small).Dispose());
        TimeSpan largeTime = SvgGrowth.Measure(this.output, "8 MiB prolog", () => Render(large).Dispose());
        SvgGrowth.AssertLinear(smallTime, largeTime, 8);

        // An editor DOCTYPE with a long internal subset loads.
        StringBuilder doctype = new("<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\"");
        doctype.Append(" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\" [");
        for (int i = 0; i < 100; i++)
        {
            doctype.Append(CultureInfo.InvariantCulture, $"<!ENTITY ns_{i} \"http://ns.example.com/{i}/\">");
        }

        doctype.Append("]>").Append(Open).Append("<rect width=\"10\" height=\"10\"/></svg>");
        using Image<Rgba32> image = Render(Encoding.UTF8.GetBytes(doctype.ToString()));
        Assert.Equal(255, image[5, 5].A);
    }

    private static int RenderOrInvalid(byte[] data)
    {
        try
        {
            using Image<Rgba32> image = Render(data);
            return 1;
        }
        catch (InvalidImageContentException)
        {
            return 0;
        }
    }

    private static Image<Rgba32> Render(byte[] data)
        => SvgDocument.Load(new MemoryStream(data)).Render<Rgba32>();

    /// <summary>
    /// Renders a document and returns its center pixel.
    /// </summary>
    /// <param name="svg">The document.</param>
    /// <returns>The pixel at (10, 10).</returns>
    private static Rgba32 RenderCenter(string svg)
    {
        using Image<Rgba32> image = SvgDocument.Parse(svg).Render<Rgba32>();
        return image[10, 10];
    }

    /// <summary>
    /// Creates a document that shows gzip data as an image of the whole document, through a <c>data:</c> URI without a
    /// media type.
    /// </summary>
    /// <param name="gzip">The compressed image.</param>
    /// <returns>The document.</returns>
    private static string ImageDocument(byte[] gzip)
        => Open + $"<image width=\"20\" height=\"20\" href=\"data:;base64,{Convert.ToBase64String(gzip)}\"/></svg>";

    /// <summary>
    /// Creates gzip data that inflates to an svg start tag followed by white space: one member with the start tag and
    /// one member of 1 MiB of spaces repeated. Each repeated member is about one kilobyte.
    /// </summary>
    /// <param name="megabytes">The number of members of spaces.</param>
    /// <returns>The data.</returns>
    private static byte[] CreateBomb(int megabytes)
    {
        byte[] start = Gzip(Encoding.UTF8.GetBytes(Open + "<rect width=\"20\" height=\"20\"/>"));
        byte[] spaces = new byte[1024 * 1024];
        spaces.AsSpan().Fill((byte)' ');
        byte[] member = Gzip(spaces);
        byte[] end = Gzip(Encoding.UTF8.GetBytes("</svg>"));

        using MemoryStream stream = new();
        stream.Write(start);
        for (int i = 0; i < megabytes; i++)
        {
            stream.Write(member);
        }

        stream.Write(end);
        return stream.ToArray();
    }

    /// <summary>
    /// Compresses data with gzip.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The compressed data.</returns>
    private static byte[] Gzip(byte[] data)
    {
        using MemoryStream stream = new();
        using (GZipStream gzip = new(stream, CompressionLevel.Optimal, true))
        {
            gzip.Write(data);
        }

        return stream.ToArray();
    }

    private static byte[] CreateLongProlog(int length)
    {
        StringBuilder text = new(length + 200);
        text.Append("<?xml version=\"1.0\"?><!--");
        text.Append('x', length);

        text.Append("-->").Append(Open).Append("<rect width=\"10\" height=\"10\"/></svg>");
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
