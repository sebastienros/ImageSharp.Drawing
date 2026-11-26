// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile documents for SVG text: each loads and draws within <see cref="SvgLimits"/> or fails with
/// <see cref="InvalidImageContentException"/>, in time that grows linearly with the input. The time bounds are generous;
/// the test output prints the measured times.
/// </summary>
public class SvgTextHostileDocumentTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"200\" height=\"200\" "
        + "font-family=\"Open Sans\" font-size=\"10\">";

    /// <summary>
    /// The time limit of one case.
    /// </summary>
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgTextHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TextAtTheCharacterLimit_LoadsAndOneMoreFails()
    {
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        options.Limits = new SvgLimits { MaxTextCharacters = 200_000 };
        string text = new('a', 200_000);
        this.Measure("200,000 characters", () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{text}</text></svg>", options));
        Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{text}b</text></svg>", options));

        // With the default limits, a million glyphs reach the node limit first: each glyph is one draw node.
        string million = new('a', 1_000_000);
        this.Measure("1,000,000 characters", () => Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{million}</text></svg>", SvgTextDirectionRenderTests.Options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" fill=\"url(#g)\"")]
    [InlineData(" clip-path=\"url(#c)\"")]
    [InlineData(" textLength=\"4\"")]
    [InlineData(" text-decoration=\"underline\" baseline-shift=\"1\"")]
    public void ManyTspans_CostLinearTime(string attributes)
    {
        // Paint servers and clips in bounding box units look up the box of each tspan.
        const string defs = "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient>"
            + "<clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect width=\"0.5\" height=\"1\"/></clipPath></defs>";
        TimeSpan small = this.Measure($"5,000 tspans{attributes}", () => this.LoadAndDraw(SvgOpen + defs + Tspans(5_000, attributes) + "</svg>"));
        TimeSpan large = this.Measure($"50,000 tspans{attributes}", () => this.LoadAndDraw(SvgOpen + defs + Tspans(50_000, attributes) + "</svg>"));

        // Ten times the input may cost a little more than ten times the time, not a hundred times.
        AssertLinear(small, large, 10);
    }

    [Fact]
    public void DeepTspanNesting_StopsAtTheNestingLimit()
    {
        // Baseline shifts, decorations, and textLength walk the ancestors of a cluster.
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        int depth = options.Limits.MaxNestingDepth - 4;
        this.Measure($"{depth} nested tspans", () => this.LoadAndDraw(SvgOpen + "<text y=\"50\">" + Nested(depth) + "</text></svg>"));
        Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(SvgOpen + "<text y=\"50\">" + Nested(options.Limits.MaxNestingDepth + 1) + "</text></svg>", options));

        static string Nested(int depth)
        {
            StringBuilder text = new();
            for (int i = 0; i < depth; i++)
            {
                text.Append("<tspan baseline-shift=\"0.01\" text-decoration=\"underline\" textLength=\"").Append(100 + i).Append("\">a");
            }

            for (int i = 0; i < depth; i++)
            {
                text.Append("</tspan>");
            }

            return text.ToString();
        }
    }

    [Fact]
    public void BidiControlsAndNestedEmbeddings_CostLinearTime()
    {
        // Explicit embeddings, overrides, isolates, and their terminators between letters of both directions.
        TimeSpan small = this.Measure("20,000 bidi controls", () => this.LoadAndDraw(SvgOpen + $"<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">{Controls(5_000)}</text></svg>"));
        TimeSpan large = this.Measure("200,000 bidi controls", () => this.LoadAndDraw(SvgOpen + $"<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">{Controls(50_000)}</text></svg>"));
        AssertLinear(small, large, 10);

        // Nested unicode-bidi embeddings that alternate the direction, as deep as the nesting limit allows.
        StringBuilder nested = new(SvgOpen + "<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">");
        int depth = SvgTextDirectionRenderTests.Options.Limits.MaxNestingDepth - 4;
        for (int i = 0; i < depth; i++)
        {
            nested.Append(CultureInfo.InvariantCulture, $"<tspan direction=\"{(i % 2 == 0 ? "rtl" : "ltr")}\" unicode-bidi=\"{(i % 3 == 0 ? "bidi-override" : "embed")}\">aب");
        }

        for (int i = 0; i < depth; i++)
        {
            nested.Append("</tspan>");
        }

        this.Measure($"{depth} nested embeddings", () => this.LoadAndDraw(nested.Append("</text></svg>").ToString()));

        static string Controls(int count)
        {
            StringBuilder text = new();
            string[] controls = ["‪", "‫", "‭", "‮", "⁦", "⁧", "⁨"];
            for (int i = 0; i < count; i++)
            {
                text.Append(controls[i % controls.Length]).Append(i % 2 == 0 ? 'a' : 'ب').Append(i % 7 == 6 ? "⁩" : "‬");
            }

            return text.ToString();
        }
    }

    [Fact]
    public void TrefFanOut_StopsAtTheCharacterLimit()
    {
        // Each tref copies the 100,000 characters of the referenced text: 1,000 of them would lay out 10^8 characters.
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        StringBuilder svg = new(SvgOpen);
        svg.Append("<defs><text id=\"source\">").Append('a', 100_000).Append("</text></defs><text y=\"20\">");
        for (int i = 0; i < 1_000; i++)
        {
            svg.Append("<tref xlink:href=\"#source\"/>");
        }

        string document = svg.Append("</text></svg>").ToString();
        this.Measure("1,000 tref of 100,000 characters", () => Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(document, options)));

        // A chain of tref references: only the character data of the referenced text counts, so the chain does not
        // expand.
        StringBuilder chain = new(SvgOpen + "<defs><text id=\"t0\">a</text>");
        for (int i = 1; i < 2_000; i++)
        {
            chain.Append(CultureInfo.InvariantCulture, $"<text id=\"t{i}\">b<tref xlink:href=\"#t{i - 1}\"/></text>");
        }

        chain.Append("</defs><text y=\"20\"><tref xlink:href=\"#t1999\"/></text></svg>");
        this.Measure("a chain of 2,000 tref", () => this.LoadAndDraw(chain.ToString()));
    }

    [Fact]
    public void UseStormOfDecoratedTextOnAPath_StopsAtALimit()
    {
        StringBuilder svg = new(SvgOpen);
        svg.Append("<defs><path id=\"p\" d=\"M0,100 Q100,0 200,100\"/><text id=\"l0\" text-decoration=\"underline\" textLength=\"50\"><textPath xlink:href=\"#p\">abcdefghij</textPath></text>");
        for (int level = 1; level <= 6; level++)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<g id=\"l{level}\">");
            for (int i = 0; i < 10; i++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#l{level - 1}\"/>");
            }

            svg.Append("</g>");
        }

        string document = svg.Append("</defs><use xlink:href=\"#l6\"/></svg>").ToString();
        this.Measure("10^6 uses of decorated text on a path", () => Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(document, SvgTextDirectionRenderTests.Options)));
    }

    [Fact]
    public void ManyFontsAndManyMissingCharacters_CostLinearTime()
    {
        // 400 faces in the collection, a fallback list that names every family, and thousands of distinct characters
        // that no face has: each grapheme searches the chain once.
        FontCollection collection = new();
        string[] files = ["OpenSans-Regular.ttf", "Inter-Light.ttf", "me_quran_volt_newmet.ttf", "WendyOne-Regular.ttf"];
        List<string> families = [];
        for (int i = 0; i < 100; i++)
        {
            foreach (string file in files)
            {
                using FileStream stream = File.OpenRead(TestFontUtilities.GetPath(file));
                FontFamily family = collection.Add(stream);
                if (!families.Contains(family.Name))
                {
                    families.Add(family.Name);
                }
            }
        }

        SvgLoadOptions options = new() { FontCollection = collection, DefaultFontFamily = "Open Sans", FallbackFontFamilies = families };
        TimeSpan small = this.Measure("2,000 missing characters", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\">{Missing(2_000)}</text></svg>", options));
        TimeSpan large = this.Measure("20,000 missing characters", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\">{Missing(20_000)}</text></svg>", options));
        AssertLinear(small, large, 10);

        // A family list with thousands of names, which no face has.
        StringBuilder names = new();
        for (int i = 0; i < 5_000; i++)
        {
            names.Append(CultureInfo.InvariantCulture, $"Missing{i}, ");
        }

        this.Measure("5,000 family names", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\" font-family=\"{names}serif\">text</text></svg>", options));

        // Distinct CJK ideographs: no face of the collection has them.
        static string Missing(int count)
        {
            StringBuilder text = new();
            for (int i = 0; i < count; i++)
            {
                text.Append((char)(0x4E00 + i));
            }

            return text.ToString();
        }
    }

    private static string Tspans(int count, string attributes)
    {
        StringBuilder text = new("<text x=\"0\" y=\"20\">");
        for (int i = 0; i < count; i++)
        {
            text.Append("<tspan").Append(attributes).Append(">a</tspan>");
        }

        return text.Append("</text>").ToString();
    }

    private static void AssertLinear(TimeSpan small, TimeSpan large, int factor)
    {
        // A small run can be dominated by fixed costs, so it counts as at least 50 ms.
        double reference = Math.Max(small.TotalMilliseconds, 50D);
        Assert.True(large.TotalMilliseconds < reference * factor * 4, $"{small.TotalMilliseconds:0} ms grew to {large.TotalMilliseconds:0} ms.");
    }

    private void LoadAndDraw(string svg, SvgLoadOptions options = null)
    {
        SvgDocument document = SvgDocument.Parse(svg, options ?? SvgTextDirectionRenderTests.Options);
        using Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));
    }

    private TimeSpan Measure(string name, Action action)
    {
        Stopwatch watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        this.output.WriteLine($"{name}: {watch.Elapsed.TotalMilliseconds:0} ms");
        Assert.True(watch.Elapsed < TimeLimit, $"{name} took {watch.Elapsed}.");
        return watch.Elapsed;
    }
}
