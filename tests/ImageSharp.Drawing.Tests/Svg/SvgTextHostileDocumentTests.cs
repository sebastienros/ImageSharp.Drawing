// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
/// <see cref="InvalidImageContentException"/>, in time that grows linearly with the input, or not at all once a limit
/// stops the work. Each case compares the time of two input sizes (<see cref="SvgGrowth"/>), so the tests hold on a
/// machine under load; the test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgTextHostileDocumentTests
{
    private const string SvgOpen =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"200\" height=\"200\" "
        + "font-family=\"Open Sans\" font-size=\"10\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgTextHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TextAtTheCharacterLimit_LoadsAndOneMoreFails()
    {
        SvgLoadOptions options = new(SvgTextDirectionRenderTests.Options) { Limits = new SvgLimits { MaxTextCharacters = 200_000 } };
        string text = new('a', 200_000);
        TimeSpan small = this.MeasureWarm("20,000 characters", () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{text[..20_000]}</text></svg>", options));
        TimeSpan large = this.Measure("200,000 characters", () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{text}</text></svg>", options));
        SvgGrowth.AssertLinear(small, large, 10);
        Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{text}b</text></svg>", options));

        // With the default limits, a million glyphs reach the node limit first: each glyph is one draw node. Failing at
        // the limit costs at most about twice as much as loading half of the text.
        string million = new('a', 1_000_000);
        TimeSpan below = this.Measure("500,000 characters", () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{million[..500_000]}</text></svg>", SvgTextDirectionRenderTests.Options));
        TimeSpan above = this.Measure("1,000,000 characters", () => Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(SvgOpen + $"<text y=\"10\">{million}</text></svg>", SvgTextDirectionRenderTests.Options)));
        SvgGrowth.AssertLinear(below, above, 2);
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
        TimeSpan small = this.MeasureWarm($"5,000 tspans{attributes}", () => this.LoadAndDraw(SvgOpen + defs + Tspans(5_000, attributes) + "</svg>"));
        TimeSpan large = this.Measure($"50,000 tspans{attributes}", () => this.LoadAndDraw(SvgOpen + defs + Tspans(50_000, attributes) + "</svg>"));

        // Ten times the input may cost a little more than ten times the time, not a hundred times.
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void DeepTspanNesting_StopsAtTheNestingLimit()
    {
        // Baseline shifts, decorations, and textLength walk the ancestors of a cluster, so the cost may grow with the
        // square of the depth, but not faster.
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        int depth = options.Limits.MaxNestingDepth - 4;
        TimeSpan small = this.MeasureWarm($"{depth / 4} nested tspans", () => this.LoadAndDraw(SvgOpen + "<text y=\"50\">" + Nested(depth / 4) + "</text></svg>"));
        TimeSpan large = this.Measure($"{depth} nested tspans", () => this.LoadAndDraw(SvgOpen + "<text y=\"50\">" + Nested(depth) + "</text></svg>"));
        SvgGrowth.AssertLinear(small, large, 16);
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
        TimeSpan small = this.MeasureWarm("20,000 bidi controls", () => this.LoadAndDraw(SvgOpen + $"<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">{Controls(5_000)}</text></svg>"));
        TimeSpan large = this.Measure("200,000 bidi controls", () => this.LoadAndDraw(SvgOpen + $"<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">{Controls(50_000)}</text></svg>"));
        SvgGrowth.AssertLinear(small, large, 10);

        // Nested unicode-bidi embeddings that alternate the direction, as deep as the nesting limit allows. The ancestors
        // of each character are walked, so the cost may grow with the square of the depth, but not faster.
        int depth = SvgTextDirectionRenderTests.Options.Limits.MaxNestingDepth - 4;
        TimeSpan shallow = this.MeasureWarm($"{depth / 4} nested embeddings", () => this.LoadAndDraw(Nested(depth / 4)));
        TimeSpan deep = this.Measure($"{depth} nested embeddings", () => this.LoadAndDraw(Nested(depth)));
        SvgGrowth.AssertLinear(shallow, deep, 16);

        static string Nested(int depth)
        {
            StringBuilder nested = new(SvgOpen + "<text x=\"100\" y=\"50\" font-family=\"Open Sans, me_quran\">");
            for (int i = 0; i < depth; i++)
            {
                nested.Append(CultureInfo.InvariantCulture, $"<tspan direction=\"{(i % 2 == 0 ? "rtl" : "ltr")}\" unicode-bidi=\"{(i % 3 == 0 ? "bidi-override" : "embed")}\">aب");
            }

            for (int i = 0; i < depth; i++)
            {
                nested.Append("</tspan>");
            }

            return nested.Append("</text></svg>").ToString();
        }

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

    [Theory]

    // European numbers after one Arabic letter: rule W2 needs the last strong type before each number.
    [InlineData("direction=\"rtl\"", "ب", "1 ")]

    // Matched isolates: each joins the level runs around it into one isolating run sequence (BD13).
    [InlineData("", "", "⁧ב⁩a")]

    // Terminators without openers.
    [InlineData("", "", "‬⁩a")]

    // Openers without terminators, nested far beyond the depth limit of 125.
    [InlineData("", "", "‫⁧aב")]
    public void BidiAlgorithmInputs_CostLinearTime(string attributes, string prefix, string unit)
    {
        string Text(int count)
            => SvgOpen + $"<text x=\"100\" y=\"50\" {attributes} font-family=\"Open Sans, me_quran\">"
                + $"{prefix}{string.Concat(Enumerable.Repeat(unit, count))}</text></svg>";

        TimeSpan small = this.MeasureWarm($"10,000 times {unit}", () => this.LoadAndDraw(Text(10_000)));
        TimeSpan large = this.Measure($"100,000 times {unit}", () => this.LoadAndDraw(Text(100_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void TrefFanOut_StopsAtTheCharacterLimit()
    {
        // Each tref copies the 100,000 characters of the referenced text: 1,000 of them would lay out 10^8 characters.
        // Ten times as many tref elements fail at the same limit in about the same time.
        SvgLoadOptions options = SvgTextDirectionRenderTests.Options;
        string FanOut(int count)
        {
            StringBuilder svg = new(SvgOpen);
            svg.Append("<defs><text id=\"source\">").Append('a', 100_000).Append("</text></defs><text y=\"20\">");
            for (int i = 0; i < count; i++)
            {
                svg.Append("<tref xlink:href=\"#source\"/>");
            }

            return svg.Append("</text></svg>").ToString();
        }

        string thousand = FanOut(1_000);
        string tenThousand = FanOut(10_000);

        // Each case takes the best of three runs: one run of about 50 ms can take several times as long when the machine
        // is busy, which a single measurement of each case cannot tell from growth. The bound is unchanged.
        TimeSpan small = SvgGrowth.MeasureBest(
            this.output,
            "1,000 tref of 100,000 characters",
            3,
            () => Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(thousand, options)));
        TimeSpan large = SvgGrowth.MeasureBest(
            this.output,
            "10,000 tref of 100,000 characters",
            3,
            () => Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(tenThousand, options)));
        SvgGrowth.AssertBounded(small, large);

        // A chain of tref references: only the character data of the referenced text counts, so the chain does not
        // expand, and its cost is linear in its length.
        static string Chain(int length)
        {
            StringBuilder chain = new(SvgOpen + "<defs><text id=\"t0\">a</text>");
            for (int i = 1; i < length; i++)
            {
                chain.Append(CultureInfo.InvariantCulture, $"<text id=\"t{i}\">b<tref xlink:href=\"#t{i - 1}\"/></text>");
            }

            return chain.Append(CultureInfo.InvariantCulture, $"</defs><text y=\"20\"><tref xlink:href=\"#t{length - 1}\"/></text></svg>").ToString();
        }

        TimeSpan shortChain = this.MeasureWarm("a chain of 200 tref", () => this.LoadAndDraw(Chain(200)));
        TimeSpan longChain = this.Measure("a chain of 2,000 tref", () => this.LoadAndDraw(Chain(2_000)));
        SvgGrowth.AssertLinear(shortChain, longChain, 10);
    }

    [Fact]
    public void UseStormOfDecoratedTextOnAPath_StopsAtALimit()
    {
        // Ten uses per level: six levels would expand to 10^6 instances of the text, seven to 10^7. Both fail at a limit
        // in about the same time, so the expansion stops at the limit instead of following the document.
        static string Storm(int levels)
        {
            StringBuilder svg = new(SvgOpen);
            svg.Append("<defs><path id=\"p\" d=\"M0,100 Q100,0 200,100\"/><text id=\"l0\" text-decoration=\"underline\" textLength=\"50\"><textPath xlink:href=\"#p\">abcdefghij</textPath></text>");
            for (int level = 1; level <= levels; level++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<g id=\"l{level}\">");
                for (int i = 0; i < 10; i++)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#l{level - 1}\"/>");
                }

                svg.Append("</g>");
            }

            return svg.Append(CultureInfo.InvariantCulture, $"</defs><use xlink:href=\"#l{levels}\"/></svg>").ToString();
        }

        string six = Storm(6);
        string seven = Storm(7);
        TimeSpan small = this.MeasureWarm("10^6 uses of decorated text on a path", () => Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(six, SvgTextDirectionRenderTests.Options)));
        TimeSpan large = this.Measure("10^7 uses of decorated text on a path", () => Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Parse(seven, SvgTextDirectionRenderTests.Options)));
        SvgGrowth.AssertBounded(small, large);
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
        TimeSpan small = this.MeasureWarm("2,000 missing characters", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\">{Missing(2_000)}</text></svg>", options));
        TimeSpan large = this.Measure("20,000 missing characters", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\">{Missing(20_000)}</text></svg>", options));
        SvgGrowth.AssertLinear(small, large, 10);

        // A family list with thousands of names, which no face has.
        TimeSpan few = this.MeasureWarm("500 family names", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\" font-family=\"{Names(500)}serif\">text</text></svg>", options));
        TimeSpan many = this.Measure("5,000 family names", () => this.LoadAndDraw(SvgOpen + $"<text y=\"20\" font-family=\"{Names(5_000)}serif\">text</text></svg>", options));
        SvgGrowth.AssertLinear(few, many, 10);

        static string Names(int count)
        {
            StringBuilder names = new();
            for (int i = 0; i < count; i++)
            {
                names.Append(CultureInfo.InvariantCulture, $"Missing{i}, ");
            }

            return names.ToString();
        }

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

    private void LoadAndDraw(string svg, SvgLoadOptions options = null)
    {
        SvgDocument document = SvgDocument.Parse(svg, options ?? SvgTextDirectionRenderTests.Options);
        using Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, 200, 200))));
    }

    private TimeSpan Measure(string name, Action action) => SvgGrowth.Measure(this.output, name, action);

    private TimeSpan MeasureWarm(string name, Action action) => SvgGrowth.MeasureWarm(this.output, name, action);
}
