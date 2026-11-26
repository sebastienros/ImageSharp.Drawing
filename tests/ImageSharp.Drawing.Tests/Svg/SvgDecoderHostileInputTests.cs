// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile input for <see cref="SvgDecoder"/>: every case either decodes or throws
/// <see cref="InvalidImageContentException"/>, and work that a limit stops does not grow with the input. Time comparisons
/// use <see cref="SvgGrowth"/>, so they hold on a loaded machine.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgDecoderHostileInputTests
{
    private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDecoderHostileInputTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgDecoderHostileInputTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TruncatedFiles_DecodeOrThrowInvalidImageContent()
    {
        byte[] data = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Svg.CarbonCrab));
        byte[] gzip = SvgImageFormatDetectorTests.Gzip(data);
        int decoded = 0;
        for (int length = 0; length < data.Length; length += 13)
        {
            decoded += DecodeOrInvalid(data.AsSpan(0, length).ToArray());
        }

        for (int length = 0; length < gzip.Length; length += 7)
        {
            decoded += DecodeOrInvalid(gzip.AsSpan(0, length).ToArray());
        }

        // Only cuts in the white space after the root element, or in the gzip trailer after the compressed data, leave a
        // whole document.
        this.output.WriteLine($"{decoded} truncated inputs decoded.");
        Assert.True(decoded <= 3, $"{decoded} truncated inputs decoded.");
        Assert.Equal(1, DecodeOrInvalid(data));
        Assert.Equal(1, DecodeOrInvalid(gzip));
    }

    [Fact]
    public void MalformedFiles_DecodeOrThrowInvalidImageContent()
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

            DecodeOrInvalid(copy);
            DecodeOrInvalid(SvgImageFormatDetectorTests.Gzip(copy));
        }

        // Flipped bytes inside the compressed data corrupt it.
        byte[] gzip = SvgImageFormatDetectorTests.Gzip(data);
        for (int i = 0; i < 100; i++)
        {
            byte[] copy = (byte[])gzip.Clone();
            copy[10 + random.Next(copy.Length - 10)] ^= (byte)(1 + random.Next(255));
            DecodeOrInvalid(copy);
        }
    }

    [Fact]
    public void BinaryGarbageThatStartsLikeXml_ThrowsInvalidImageContent()
    {
        Random random = new(11);
        byte[] garbage = new byte[4096];
        random.NextBytes(garbage);
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();

        // An XML declaration followed by garbage is not detected as SVG; given to the decoder, it is not a document.
        byte[] declaration = [.. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>"), .. garbage];
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, declaration));
        Assert.Throws<InvalidImageContentException>(() => Decode(declaration));

        // An svg start tag followed by garbage is detected, and decoding it fails.
        byte[] root = [.. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + Open), .. garbage];
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, root));
        Assert.Throws<InvalidImageContentException>(() => Decode(SvgImageFormatDetectorTests.Gzip(root)));

        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Open), .. garbage];
        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, utf16));
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
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        configuration.MemoryAllocator = allocator;
        byte[] data = Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\"><rect width=\"100%\" height=\"100%\"/></svg>");

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, data));
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(Encoding.UTF8.GetString(data)).Render<Rgba32>(configuration, Color.Transparent));
        Assert.Empty(allocator.AllocationLog);
    }

    /// <summary>
    /// A small gzip file that inflates far beyond the limit fails when the limit is reached: a file that inflates to
    /// eight times as much takes about as long.
    /// </summary>
    [Fact]
    public void GzipBomb_FailsAtTheInflateLimit()
    {
        int limitMegabytes = SvgResolver.MaxDecompressedSvgBytes / (1024 * 1024);
        byte[] under = CreateBomb(limitMegabytes / 8);
        byte[] small = CreateBomb(limitMegabytes * 2);
        byte[] large = CreateBomb(limitMegabytes * 16);
        this.output.WriteLine($"{small.Length} compressed bytes inflate to {limitMegabytes * 2} MiB, {large.Length} bytes to {limitMegabytes * 16} MiB.");
        Assert.True(large.Length < 2 * 1024 * 1024);

        // Data under the limit decodes, so every member of the concatenated gzip data is read.
        using (Image<Rgba32> image = Decode(under))
        {
            Assert.Equal(new Size(20, 20), image.Size);
        }

        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(() => Decode(small));
        Assert.Contains("limit", exception.Message, StringComparison.Ordinal);

        TimeSpan smallTime = SvgGrowth.MeasureWarm(this.output, "two times the limit", () => Assert.Throws<InvalidImageContentException>(() => Decode(small)));
        TimeSpan largeTime = SvgGrowth.Measure(this.output, "sixteen times the limit", () => Assert.Throws<InvalidImageContentException>(() => Decode(large)));
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
        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(() => Decode(Encoding.UTF8.GetBytes(svg.ToString())));
        this.output.WriteLine(exception.Message);
    }

    /// <summary>
    /// A long prolog before the root element is not detected, and the decoder reads it in time that grows linearly.
    /// </summary>
    [Fact]
    public void LongProlog_IsNotDetectedAndDecodesInLinearTime()
    {
        byte[] small = CreateLongProlog(1 << 20);
        byte[] large = CreateLongProlog(8 << 20);
        Configuration configuration = SvgImageFormatDetectorTests.CreateConfiguration();
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, small));

        TimeSpan smallTime = SvgGrowth.MeasureWarm(this.output, "1 MiB prolog", () => Decode(small).Dispose());
        TimeSpan largeTime = SvgGrowth.Measure(this.output, "8 MiB prolog", () => Decode(large).Dispose());
        SvgGrowth.AssertLinear(smallTime, largeTime, 8);

        // An editor DOCTYPE with a long internal subset is detected by its name.
        StringBuilder doctype = new("<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\"");
        doctype.Append(" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\" [");
        for (int i = 0; i < 100; i++)
        {
            doctype.Append(CultureInfo.InvariantCulture, $"<!ENTITY ns_{i} \"http://ns.example.com/{i}/\">");
        }

        doctype.Append("]>").Append(Open).Append("<rect width=\"10\" height=\"10\"/></svg>");
        using Image<Rgba32> image = Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration }, Encoding.UTF8.GetBytes(doctype.ToString()));
        Assert.Equal(255, image[5, 5].A);
    }

    private static int DecodeOrInvalid(byte[] data)
    {
        try
        {
            using Image<Rgba32> image = Decode(data);
            return 1;
        }
        catch (InvalidImageContentException)
        {
            return 0;
        }
    }

    private static Image<Rgba32> Decode(byte[] data)
        => SvgDecoderTests.Decoder.Decode<Rgba32>(new SvgDecoderOptions(), new MemoryStream(data));

    /// <summary>
    /// Creates gzip data that inflates to an svg start tag followed by white space: one member with the start tag and
    /// one member of 1 MiB of spaces repeated. Each repeated member is about one kilobyte.
    /// </summary>
    /// <param name="megabytes">The number of members of spaces.</param>
    /// <returns>The data.</returns>
    private static byte[] CreateBomb(int megabytes)
    {
        byte[] start = SvgImageFormatDetectorTests.Gzip(Encoding.UTF8.GetBytes(Open + "<rect width=\"10\" height=\"10\"/>"));
        byte[] spaces = new byte[1024 * 1024];
        spaces.AsSpan().Fill((byte)' ');
        byte[] member = SvgImageFormatDetectorTests.Gzip(spaces);
        byte[] end = SvgImageFormatDetectorTests.Gzip(Encoding.UTF8.GetBytes("</svg>"));

        using MemoryStream stream = new();
        stream.Write(start);
        for (int i = 0; i < megabytes; i++)
        {
            stream.Write(member);
        }

        stream.Write(end);
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
