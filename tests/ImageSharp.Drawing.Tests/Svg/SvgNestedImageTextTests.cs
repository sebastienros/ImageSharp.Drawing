// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Text inside SVG documents that <c>image</c> elements embed: it draws with the font settings of the outer load
/// options, its characters count against the one text limit of the outer document, and nesting with text at every
/// level stays bounded. The timing cases compare two input sizes (<see cref="SvgGrowth"/>).
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgNestedImageTextTests
{
    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgNestedImageTextTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgNestedImageTextTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// The text of an embedded document draws with the fonts of the outer load options, through
    /// <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and through <see cref="SvgDecoder"/>; without fonts it draws
    /// nothing, as text of the outer document does.
    /// </summary>
    [Fact]
    public void EmbeddedText_DrawsWithTheOuterFonts()
    {
        string svg = Outer(string.Empty, Image(0, "Nested"));
        using Image<Rgba32> drawn = SvgDocument.Parse(svg, SvgSvg2RenderTests.TextOptions).Render<Rgba32>();
        Assert.True(CountInk(drawn, 0, 100) > 50);

        SvgDecoderOptions options = new() { LoadOptions = SvgSvg2RenderTests.TextOptions };
        using Image<Rgba32> decoded = SvgDecoderTests.Decoder.Decode<Rgba32>(options, new MemoryStream(Encoding.UTF8.GetBytes(svg)));
        Assert.Equal(CountInk(drawn, 0, 100), CountInk(decoded, 0, 100));

        SvgLoadOptions noFonts = new() { FontCollection = new SixLabors.Fonts.FontCollection() };
        using Image<Rgba32> empty = SvgDocument.Parse(svg, noFonts).Render<Rgba32>();
        Assert.Equal(0, CountInk(empty, 0, 100));
    }

    /// <summary>
    /// An <c>@font-face</c> rule with a data URL in an embedded document is skipped, as in every document: its text draws
    /// with the font that matching gives without the rule, so it equals the same text without the rule.
    /// </summary>
    [Fact]
    public void FontFaceInEmbeddedDocument_IsSkipped()
    {
        const string rule = "<style>@font-face { font-family: Embedded; src: url(data:font/ttf;base64,AAEAAA==); }</style>";
        string withRule = Outer(string.Empty, Image(0, "Face", rule, "Embedded"));
        string without = Outer(string.Empty, Image(0, "Face", string.Empty, "Embedded"));
        using Image<Rgba32> a = SvgDocument.Parse(withRule, SvgSvg2RenderTests.TextOptions).Render<Rgba32>();
        using Image<Rgba32> b = SvgDocument.Parse(without, SvgSvg2RenderTests.TextOptions).Render<Rgba32>();
        Assert.True(CountInk(a, 0, 100) > 50);
        Assert.Equal(CountInk(b, 0, 100), CountInk(a, 0, 100));
    }

    /// <summary>
    /// The characters of embedded documents count against the text limit of the outer document: the nested document gets
    /// what remains, an embedded document over it does not draw, and each drawn one uses up its part.
    /// </summary>
    [Fact]
    public void EmbeddedText_CountsAgainstTheOuterTextLimit()
    {
        // Ten characters in the outer document, then embedded documents of ten and of five characters.
        string svg = Outer("<text x=\"0\" y=\"190\" font-size=\"10\">abcdefghij</text>", Image(0, "klmnopqrst") + Image(100, "uvwxy"));

        Assert.Equal((true, true), Drawn(svg, 25));

        // 19: the first embedded document needs 10 of the 9 that remain; the second needs 5 of them.
        Assert.Equal((false, true), Drawn(svg, 19));

        // 24: the first takes 10 of the 14 that remain, so the second has 4.
        Assert.Equal((true, false), Drawn(svg, 24));

        static (bool First, bool Second) Drawn(string svg, int limit)
        {
            SvgLoadOptions options = SvgSvg2RenderTests.TextOptions;
            options.Limits = new SvgLimits { MaxTextCharacters = limit };
            using Image<Rgba32> image = SvgDocument.Parse(svg, options).Render<Rgba32>();
            Assert.True(CountInk(image, 180, 200) > 0);
            return (CountInk(image, 0, 100, 0, 100) > 0, CountInk(image, 0, 100, 100, 200) > 0);
        }
    }

    /// <summary>
    /// Embedded documents nested deeper than the image nesting limit, each level with text and ten instances of the next
    /// level, load and draw; twice the nesting depth costs no more, because the levels beyond the limit do not load.
    /// </summary>
    /// <remarks>
    /// Both documents exceed the limit of four nested levels, so both draw the same content and differ only in the
    /// levels that do not load. The draw of the ten thousand instances of the deepest level varies by a factor of four
    /// between runs on a loaded machine, so each time is the best of several runs.
    /// </remarks>
    [Fact]
    public void NestingWithTextAtEveryLevel_IsBounded()
    {
        TimeSpan small = SvgGrowth.MeasureBest(this.output, "5 levels", 5, () => DrawNested(5));
        TimeSpan large = SvgGrowth.MeasureBest(this.output, "10 levels", 5, () => DrawNested(10));
        SvgGrowth.AssertBounded(small, large);

        static void DrawNested(int depth)
        {
            using Image<Rgba32> image = SvgDocument.Parse(Nested(0, depth), SvgSvg2RenderTests.TextOptions).Render<Rgba32>();
            Assert.True(CountInk(image, 0, 100) > 0);
        }
    }

    private static string Outer(string content, string images)
        => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">" + content + images + "</svg>";

    private static string Image(int x, string text, string style = "", string family = "Open Sans")
    {
        string inner = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">" + style
            + $"<text x=\"5\" y=\"50\" font-family=\"{family}\" font-size=\"16\">{text}</text></svg>";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"<image x=\"{x}\" y=\"0\" width=\"100\" height=\"100\" href=\"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(inner))}\"/>");
    }

    private static string Nested(int level, int depth)
    {
        StringBuilder builder = new("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">");
        builder.Append(CultureInfo.InvariantCulture, $"<text x=\"2\" y=\"20\" font-size=\"16\">Level {level}</text>");
        if (level < depth)
        {
            string child = Convert.ToBase64String(Encoding.UTF8.GetBytes(Nested(level + 1, depth)));
            builder.Append(CultureInfo.InvariantCulture, $"<image id=\"i\" y=\"30\" width=\"10\" height=\"10\" href=\"data:image/svg+xml;base64,{child}\"/>");
            for (int i = 1; i < 10; i++)
            {
                builder.Append(CultureInfo.InvariantCulture, $"<use href=\"#i\" x=\"{i * 10}\"/>");
            }
        }

        return builder.Append("</svg>").ToString();
    }

    private static int CountInk(Image<Rgba32> image, int top, int bottom, int left = 0, int right = -1)
    {
        right = right < 0 ? image.Width : right;
        int count = 0;
        for (int y = top; y < Math.Min(bottom, image.Height); y++)
        {
            for (int x = left; x < Math.Min(right, image.Width); x++)
            {
                if (image[x, y].A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
