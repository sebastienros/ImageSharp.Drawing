// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile documents for the SVG 2 features: huge <c>polygon()</c> and <c>path()</c> clips that load and draw, stroke
/// boxes of deep and wide groups, extreme <c>pathLength</c> values, thousands of font feature settings, and
/// <c>:lang()</c> selectors with thousands of ranges. Each case compares the time of two input sizes
/// (<see cref="SvgGrowth"/>), so the tests hold on a machine under load; the test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgSvg2HostileDocumentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgSvg2HostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgSvg2HostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData("nonzero")]
    [InlineData("evenodd")]
    public void HugePolygonClip_LoadsAndDrawsInLinearTime(string rule)
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "20,000 vertices", () => Draw(PolygonDocument(rule, 20_000)));
        TimeSpan large = SvgGrowth.Measure(this.output, "200,000 vertices", () => Draw(PolygonDocument(rule, 200_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void LongPathClip_LoadsAndDrawsInLinearTime()
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "20,000 segments", () => Draw(PathDocument(20_000)));
        TimeSpan large = SvgGrowth.Measure(this.output, "200,000 segments", () => Draw(PathDocument(200_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void StrokeBoxOfNestedGroups_CostsLinearTimeInTheLeaves()
    {
        // Every level of the nesting has a basic shape on its stroke box, which walks every leaf below it, so the cost is
        // the depth, bounded by the nesting limit, times the number of leaves.
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "40 levels, 1,000 leaves", () => Draw(NestedStrokeBoxDocument(40, 1_000)));
        TimeSpan large = SvgGrowth.Measure(this.output, "40 levels, 10,000 leaves", () => Draw(NestedStrokeBoxDocument(40, 10_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1e-30")]
    [InlineData("1e30")]
    [InlineData("3.4e38")]
    [InlineData("1e999")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ExtremePathLength_DrawsWithoutError(string value)
    {
        string document = SvgOpen
            + $"<path d=\"M 10 10 H 190 V 190\" fill=\"none\" stroke=\"green\" stroke-width=\"4\" pathLength=\"{value}\""
            + " stroke-dasharray=\"10 5\" stroke-dashoffset=\"3\"/>"
            + $"<circle cx=\"100\" cy=\"100\" r=\"50\" fill=\"none\" stroke=\"navy\" stroke-width=\"4\" pathLength=\"{value}\""
            + " stroke-dasharray=\"0 10\"/>"
            + $"<path id=\"p\" d=\"M 10 150 H 190\" pathLength=\"{value}\"/>"
            + "<text font-size=\"10\"><textPath href=\"#p\" startOffset=\"20\">Text</textPath></text>"
            + "</svg>";

        Draw(document);
    }

    [Fact]
    public void PathLengthThatMakesMillionsOfDashes_IsBounded()
    {
        // pathLength 1e7 on a path 180 long makes each dash 1.8e-5 units long; the dasher of the library bounds the
        // number of dashes it makes, so the cost does not grow with pathLength.
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "pathLength 1e3", () => Draw(DashDocument("1e3")));
        TimeSpan large = SvgGrowth.Measure(this.output, "pathLength 1e7", () => Draw(DashDocument("1e7")));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void ThousandsOfFontFeatureSettings_CostLinearTime()
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "2,000 settings", () => Draw(FeatureDocument(2_000)));
        TimeSpan large = SvgGrowth.Measure(this.output, "20,000 settings", () => Draw(FeatureDocument(20_000)));
        SvgGrowth.AssertLinear(small, large, 12);
    }

    [Fact]
    public void LangSelectorWithThousandsOfRanges_CostsLinearTime()
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "2,000 ranges", () => Draw(LangDocument(2_000)));
        TimeSpan large = SvgGrowth.Measure(this.output, "20,000 ranges", () => Draw(LangDocument(20_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    private static void Draw(string svg)
    {
        SvgDocument document = SvgDocument.Parse(svg, SvgSvg2RenderTests.TextOptions);
        using Image<Rgba32> image = new(200, 200);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new PointF(0, 0))));
    }

    private static string PolygonDocument(string rule, int count)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect width=\"200\" height=\"200\" fill=\"green\" style=\"clip-path: polygon(").Append(rule);
        for (int i = 0; i < count; i++)
        {
            // A star-like ring of points that crosses itself, so the fill rule matters.
            double angle = i * 2.39996;
            double radius = 40 + ((i % 2) * 50);
            builder.Append(CultureInfo.InvariantCulture, $", {100 + (radius * Math.Cos(angle)):0.##}px {100 + (radius * Math.Sin(angle)):0.##}px");
        }

        return builder.Append(")\"/></svg>").ToString();
    }

    private static string PathDocument(int count)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect width=\"200\" height=\"200\" fill=\"green\" style=\"clip-path: path(evenodd, 'M 0 0");
        for (int i = 0; i < count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $" L {(i * 7) % 200} {(i * 13) % 200}");
        }

        return builder.Append(" Z')\"/></svg>").ToString();
    }

    private static string NestedStrokeBoxDocument(int depth, int leaves)
    {
        StringBuilder builder = new(SvgOpen);
        for (int i = 0; i < depth; i++)
        {
            builder.Append("<g style=\"clip-path: inset(1px)\">");
        }

        for (int i = 0; i < leaves; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{i % 190}\" y=\"{i % 170}\" width=\"10\" height=\"30\" fill=\"green\" stroke=\"navy\"/>");
        }

        for (int i = 0; i < depth; i++)
        {
            builder.Append("</g>");
        }

        return builder.Append("</svg>").ToString();
    }

    private static string DashDocument(string pathLength)
        => SvgOpen + $"<path d=\"M 10 100 H 190\" stroke=\"green\" stroke-width=\"4\" pathLength=\"{pathLength}\" stroke-dasharray=\"1 1\"/></svg>";

    private static string FeatureDocument(int count)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<text x=\"10\" y=\"50\" font-family=\"Inter\" style=\"font-feature-settings: ");
        for (int i = 0; i < count; i++)
        {
            // Four printable ASCII characters per tag, many of them distinct, some repeated.
            char a = (char)('a' + (i % 26));
            char b = (char)('a' + ((i / 26) % 26));
            char c = (char)('a' + ((i / 676) % 26));
            builder.Append(i == 0 ? string.Empty : ", ").Append('\'').Append(a).Append(b).Append(c).Append("x' ").Append(i % 3);
        }

        builder.Append("\">0123 office<tspan>4567</tspan><tspan>89 fi</tspan></text>");
        return builder.Append("</svg>").ToString();
    }

    private static string LangDocument(int ranges)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<style>rect:lang(");
        for (int i = 0; i < ranges; i++)
        {
            builder.Append(i == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"x-l{i}");
        }

        builder.Append(", fr) { fill: green; }</style>");
        for (int i = 0; i < 200; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect x=\"{i % 200}\" width=\"1\" height=\"10\" lang=\"{(i % 2 == 0 ? "fr-CA" : "de-Latn-DE")}\"/>");
        }

        return builder.Append("</svg>").ToString();
    }
}
