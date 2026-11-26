// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.IO.Compression;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests <see cref="SvgImageFormatDetector"/> and the detection that <see cref="SvgConfigurationModule"/> registers.
/// </summary>
public class SvgImageFormatDetectorTests
{
    private const string Root = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgImageFormatDetectorTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgImageFormatDetectorTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData("<svg>")]
    [InlineData("<svg/>")]
    [InlineData(Root)]
    [InlineData("\r\n\t <svg\nwidth=\"1\">")]
    [InlineData("<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + Root)]
    [InlineData("<?xml version=\"1.0\"?><?xml-stylesheet href=\"a.css\"?><!-- a > b -- c --><svg>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<!-- Generator: Adobe Illustrator 27.0.0, SVG Export Plug-In . SVG Version: 6.00 Build 0)  -->\n" + Root)]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n<!-- Created with Inkscape (http://www.inkscape.org/) -->\n\n" + Root)]
    [InlineData("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" + Root)]
    [InlineData("<!DOCTYPE svg [<!ENTITY a \"b\">]>" + Root)]
    [InlineData("<!DOCTYPE\nsvg>")]
    public void Detects_SvgText(string text)
    {
        Assert.True(Detect(Encoding.UTF8.GetBytes(text)));
        Assert.True(Detect(Gzip(Encoding.UTF8.GetBytes(text))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<")]
    [InlineData("<svg")]
    [InlineData("<svgx/>")]
    [InlineData("<SVG/>")]
    [InlineData("<html><body><svg/></body></html>")]
    [InlineData("<!DOCTYPE html><html><svg/></html>")]
    [InlineData("<?xml version=\"1.0\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><svg/></html>")]
    [InlineData("<?xml version=\"1.0\"?><rss version=\"2.0\"/>")]
    [InlineData("<?xml version=\"1.0\"?><g xmlns=\"http://www.w3.org/2000/svg\"><svg/></g>")]
    [InlineData("<?xml version=\"1.0\"")]
    [InlineData("<!-- unterminated <svg>")]
    [InlineData("<![CDATA[x]]><svg/>")]
    [InlineData("<!DOCTYPEsvg>")]
    [InlineData("<!DOCTYPE svgz>")]
    [InlineData("text<svg/>")]
    [InlineData("{\"svg\": true}")]
    public void DoesNotDetect_OtherText(string text)
    {
        Assert.False(Detect(Encoding.UTF8.GetBytes(text)));
        Assert.False(Detect(Gzip(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void Detects_TheEncodingsOfXml()
    {
        string text = "<?xml version=\"1.0\"?>\n" + Root;

        // UTF-8 with and without a byte order mark, UTF-16 with a byte order mark in both byte orders.
        Assert.True(Detect(Encoding.UTF8.GetBytes(text)));
        Assert.True(Detect([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]));
        Assert.True(Detect([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)]));
        Assert.True(Detect([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)]));
        Assert.True(Detect(Gzip([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)])));

        // UTF-16 needs a byte order mark, as XML requires; a byte order mark of the other byte order does not match.
        Assert.False(Detect(Encoding.Unicode.GetBytes(text)));
        Assert.False(Detect([.. Encoding.Unicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)]));
    }

    [Fact]
    public void RootMustStartWithinTheHeader()
    {
        SvgImageFormatDetector detector = new();
        Assert.Equal(512, detector.HeaderSize);

        // "<svg>" ends exactly at the end of the 512 bytes, so its delimiter is the last byte that counts.
        string fits = "<!--" + new string('x', 512 - 4 - 3 - 5) + "-->" + "<svg>";
        string late = "<!--" + new string('x', 512 - 4 - 3 - 4) + "-->" + "<svg>";
        Assert.Equal(512, fits.Length);
        Assert.True(Detect(Encoding.UTF8.GetBytes(fits)));
        Assert.False(Detect(Encoding.UTF8.GetBytes(late)));

        // UTF-16 takes two bytes per character, so the same text does not fit.
        Assert.False(Detect([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(fits)]));

        // Bytes after the first 512 are ignored, so a longer header gives the same result.
        Assert.False(Detect(Encoding.UTF8.GetBytes(late + new string(' ', 1000))));

        // A DOCTYPE that names svg is enough, however long its internal subset.
        string doctype = "<!DOCTYPE svg [" + new string(' ', 2000) + "]>" + Root;
        Assert.True(Detect(Encoding.UTF8.GetBytes(doctype)));
    }

    [Fact]
    public void Detects_GzipByItsInflatedStart()
    {
        // A compressed document whose start is SVG, with a body much longer than the header.
        // The fill colors vary, so the data does not compress into the header.
        string rectangles = string.Concat(Enumerable.Range(0, 500).Select(i => $"<rect fill=\"#{(i * 2654435761U) & 0xFFFFFF:x6}\"/>"));
        string text = "<?xml version=\"1.0\"?>" + Root[..^2] + ">" + rectangles + "</svg>";
        byte[] gzip = Gzip(Encoding.UTF8.GetBytes(text));
        Assert.True(gzip.Length > 512);
        Assert.True(Detect(gzip));

        // Gzip data of another format, a cut header, and corrupt data are not SVG.
        Assert.False(Detect(Gzip(CreatePng())));
        Assert.False(Detect(gzip.AsSpan(0, 12).ToArray()));
        byte[] corrupt = [.. gzip.AsSpan(0, 10), .. Enumerable.Repeat((byte)0xFF, 100)];
        Assert.False(Detect(corrupt));
    }

    [Fact]
    public void DoesNotDetect_OtherFormatsOrBinaryData()
    {
        Assert.False(Detect(CreatePng()));

        Random random = new(42);
        byte[] data = new byte[512];
        for (int i = 0; i < 200; i++)
        {
            random.NextBytes(data);
            Assert.False(Detect(data));
        }
    }

    [Fact]
    public void NeverThrows_OnAnyPrefixOfSvgOrGzipData()
    {
        byte[] text = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!-- c --><!DOCTYPE svg>" + Root);
        byte[] gzip = Gzip(text);
        byte[] utf16 = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Root)];
        foreach (byte[] data in new[] { text, gzip, utf16 })
        {
            for (int length = 0; length <= data.Length; length++)
            {
                _ = Detect(data.AsSpan(0, length).ToArray());
            }
        }

        // The whole data is detected; a prefix that ends inside the root name is not.
        Assert.True(Detect(text));
        Assert.True(Detect(gzip));
        Assert.True(Detect(utf16));
        Assert.False(Detect(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><sv")));
    }

    [Fact]
    public void Detection_AllocatesNothingForTextAndAFixedAmountForGzip()
    {
        SvgImageFormatDetector detector = new();
        byte[] text = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!-- comment -->" + Root);
        byte[] shortGzip = Gzip(text);
        byte[] longGzip = Gzip(Encoding.UTF8.GetBytes("<!--" + new string('x', 400) + "-->" + Root));
        byte[] png = CreatePng();

        long textBytes = MeasureAllocation(detector, text);
        long pngBytes = MeasureAllocation(detector, png);
        long shortGzipBytes = MeasureAllocation(detector, shortGzip);
        long longGzipBytes = MeasureAllocation(detector, longGzip);
        this.output.WriteLine($"Allocated bytes per call: text {textBytes}, PNG {pngBytes}, gzip {shortGzipBytes} and {longGzipBytes}.");

        Assert.Equal(0, textBytes);
        Assert.Equal(0, pngBytes);

        // The inflater state is the same for any input; it does not grow with the data.
        Assert.Equal(shortGzipBytes, longGzipBytes);
        Assert.True(shortGzipBytes < 4096, $"Gzip detection allocated {shortGzipBytes} bytes per call.");
    }

    [Fact]
    public void Module_DetectsThroughImageSharp()
    {
        Configuration configuration = CreateConfiguration();
        DecoderOptions options = new() { Configuration = configuration };

        // A minified document shorter than the header, a document with a prolog, UTF-16, gzip, and a file name.
        Assert.Same(SvgFormat.Instance, Image.DetectFormat(options, Encoding.UTF8.GetBytes(Root)));
        Assert.Same(SvgFormat.Instance, Image.DetectFormat(options, Encoding.UTF8.GetBytes("<svg/>")));
        Assert.Same(SvgFormat.Instance, Image.DetectFormat(options, Encoding.UTF8.GetBytes("<!-- " + new string('x', 450) + " -->" + Root)));
        Assert.Same(SvgFormat.Instance, Image.DetectFormat(options, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Root)).ToArray()));
        Assert.Same(SvgFormat.Instance, Image.DetectFormat(options, Gzip(Encoding.UTF8.GetBytes(Root))));

        // Other formats are still detected, and other text is not SVG.
        Assert.Same(PngFormat.Instance, Image.DetectFormat(options, CreatePng()));
        Assert.Throws<UnknownImageFormatException>(() => Image.DetectFormat(options, Encoding.UTF8.GetBytes("<html/>")));
        Assert.Throws<UnknownImageFormatException>(() => Image.DetectFormat(options, Encoding.UTF8.GetBytes("<!-- " + new string('x', 600) + " -->" + Root)));

        Assert.True(configuration.ImageFormatsManager.TryFindFormatByFileExtension("svgz", out IImageFormat byExtension));
        Assert.Same(SvgFormat.Instance, byExtension);
        Assert.True(configuration.ImageFormatsManager.TryFindFormatByMimeType("image/svg+xml", out IImageFormat byMimeType));
        Assert.Same(SvgFormat.Instance, byMimeType);
    }

    [Fact]
    public void DefaultConfiguration_DoesNotDetectSvg()
    {
        // The types of this package are loaded at this point; the default configuration is unchanged by them.
        Assert.NotNull(SvgFormat.Instance);
        Assert.DoesNotContain(Configuration.Default.ImageFormats, format => format is SvgFormat);
        Assert.False(Configuration.Default.ImageFormatsManager.TryFindFormatByFileExtension("svg", out _));
        Assert.False(Configuration.Default.ImageFormatsManager.TryFindFormatByMimeType("image/svg+xml", out _));
        Assert.Throws<UnknownImageFormatException>(() => Image.DetectFormat(Encoding.UTF8.GetBytes(Root)));
        Assert.Throws<UnknownImageFormatException>(() => Image.Load<Rgba32>(Encoding.UTF8.GetBytes(Root)));
    }

    [Fact]
    public void Format_NamesTheFormat()
    {
        Assert.Equal("SVG", SvgFormat.Instance.Name);
        Assert.Equal("image/svg+xml", SvgFormat.Instance.DefaultMimeType);
        Assert.Equal("image/svg+xml", Assert.Single(SvgFormat.Instance.MimeTypes));
        Assert.Equal("svg,svgz", string.Join(',', SvgFormat.Instance.FileExtensions));
    }

    /// <summary>
    /// The root element of every file of the benchmark corpus and of the resvg test suite starts within the header, so
    /// every file is detected, uncompressed and compressed.
    /// </summary>
    [Fact]
    public void Detects_EveryCorpusAndSuiteFile()
    {
        List<string> files = [.. Directory.EnumerateFiles(TestFile.GetInputFileFullPath("Svg"), "*.svg", SearchOption.AllDirectories)];
        string suite = IOPath.Combine(TestEnvironment.GetFullPath(SvgResvgConformanceTests.SuiteRelativePath), "tests");
        if (Directory.Exists(suite))
        {
            files.AddRange(Directory.EnumerateFiles(suite, "*.svg", SearchOption.AllDirectories));
        }

        byte[] header = new byte[512];
        List<string> missed = [];
        foreach (string file in files)
        {
            int length;
            using (FileStream stream = File.OpenRead(file))
            {
                length = stream.ReadAtLeast(header, header.Length, false);
            }

            if (!Detect(header.AsSpan(0, length).ToArray()) || !Detect(Gzip(File.ReadAllBytes(file))))
            {
                missed.Add(file);
            }
        }

        this.output.WriteLine($"{files.Count} files.");
        Assert.True(files.Count > 100);
        Assert.Empty(missed);
    }

    /// <summary>
    /// Creates a configuration with the default formats and the SVG module, without changing the default configuration.
    /// </summary>
    /// <returns>The configuration.</returns>
    internal static Configuration CreateConfiguration()
        => new(new PngConfigurationModule(), new SvgConfigurationModule());

    /// <summary>
    /// Compresses data with gzip.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The compressed data.</returns>
    internal static byte[] Gzip(byte[] data)
    {
        using MemoryStream stream = new();
        using (GZipStream gzip = new(stream, CompressionLevel.Optimal, true))
        {
            gzip.Write(data);
        }

        return stream.ToArray();
    }

    private static bool Detect(byte[] header)
    {
        bool detected = new SvgImageFormatDetector().TryDetectFormat(header, out IImageFormat format);
        Assert.Equal(detected, format is not null);
        Assert.True(format is null || format == SvgFormat.Instance);
        return detected;
    }

    private static long MeasureAllocation(SvgImageFormatDetector detector, byte[] header)
    {
        const int Calls = 100;
        for (int i = 0; i < 10; i++)
        {
            detector.TryDetectFormat(header, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Calls; i++)
        {
            detector.TryDetectFormat(header, out _);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private static byte[] CreatePng()
    {
        using Image<Rgba32> image = new(4, 4);
        using MemoryStream stream = new();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
