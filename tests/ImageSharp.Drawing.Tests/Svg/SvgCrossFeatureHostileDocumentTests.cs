// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile documents that combine the CSS features and the SVG 2 features: <c>var()</c> expansion inside
/// <c>calc()</c> inside a basic shape, a document that reaches several limits at once, and every hostile document of
/// the CSS, SVG 2, and style tests loaded through <see cref="SvgDocument.Load(Stream, SvgLoadOptions)"/> and rendered
/// through <see cref="SvgDocument.Render{TPixel}()"/>. A render either returns an image or throws
/// <see cref="InvalidImageContentException"/>. Each case compares the time of two input sizes (<see cref="SvgGrowth"/>),
/// so the tests hold on a machine under load; the test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgCrossFeatureHostileDocumentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCrossFeatureHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgCrossFeatureHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// Every hostile document of the CSS, SVG 2, and style tests, and the two documents of this class, loads and renders
    /// or throws <see cref="InvalidImageContentException"/>, and the larger input costs at most the growth that the
    /// original test allows.
    /// </summary>
    /// <param name="name">The name of the case, which <see cref="GetCase"/> resolves.</param>
    [Theory]
    [InlineData("doubling var() chain")]
    [InlineData("use instances of long values")]
    [InlineData("long var() reference chain")]
    [InlineData("deep calc() nesting")]
    [InlineData("long calc() expressions")]
    [InlineData("deep color-mix() nesting")]
    [InlineData("huge nonzero polygon() clip")]
    [InlineData("huge evenodd polygon() clip")]
    [InlineData("long path() clip")]
    [InlineData("stroke boxes of nested groups")]
    [InlineData("pathLength of millions of dashes")]
    [InlineData("thousands of font feature settings")]
    [InlineData(":lang() with thousands of ranges")]
    [InlineData("long custom property value")]
    [InlineData("thousands of custom properties")]
    [InlineData("deep chain of custom properties")]
    [InlineData("long polygon() in clip-path")]
    [InlineData("long d: path()")]
    [InlineData("long clip-path: path()")]
    [InlineData("var() inside calc() inside a basic shape")]
    [InlineData("several limits at once")]
    public void HostileDocument_RendersOrThrowsInvalidImageContent(string name)
    {
        (string small, string large, double factor) = GetCase(name);
        byte[] smallData = Encoding.UTF8.GetBytes(small);
        byte[] largeData = Encoding.UTF8.GetBytes(large);
        TimeSpan smallTime = SvgGrowth.MeasureWarm(this.output, "small", () => this.RenderOrInvalid(smallData));
        TimeSpan largeTime = SvgGrowth.Measure(this.output, "large", () => this.RenderOrInvalid(largeData));
        SvgGrowth.AssertLinear(smallTime, largeTime, factor);
    }

    /// <summary>
    /// A doubling chain of custom properties inside <c>calc()</c> inside a basic shape stops at the substitution limits:
    /// the shape is invalid, so the element is not clipped, and twice the steps cost no more.
    /// </summary>
    [Fact]
    public void VarExpansionInsideCalcInsideABasicShape_StopsAtTheLimits()
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "30 steps", () => AssertUnclipped(VarCalcShapeDocument(30)));
        TimeSpan large = SvgGrowth.Measure(this.output, "60 steps", () => AssertUnclipped(VarCalcShapeDocument(60)));
        SvgGrowth.AssertBounded(small, large);

        // Few steps stay inside the limits, so the shape is valid and clips.
        SvgDocument valid = SvgDocument.Parse(VarCalcShapeDocument(3));
        using Image<Rgba32> image = valid.Render<Rgba32>();
        Assert.Equal(0, image[2, 2].A);
        Assert.Equal(255, image[100, 100].A);
    }

    /// <summary>
    /// A document that reaches the substitution budget, the math function limits, the color nesting limit, and the layer
    /// limit at once fails at the layer limit when it draws, with the type of every limit, and costs linear time.
    /// </summary>
    [Fact]
    public void DocumentAtSeveralLimits_FailsWithInvalidImageContent()
    {
        TimeSpan small = SvgGrowth.MeasureWarm(this.output, "100 elements", () => AssertLayerLimit(SeveralLimitsDocument(100)));
        TimeSpan large = SvgGrowth.Measure(this.output, "1,000 elements", () => AssertLayerLimit(SeveralLimitsDocument(1_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    /// <summary>
    /// Returns the small and the large document of a hostile case and the largest expected ratio of their decode times,
    /// as the original test states it.
    /// </summary>
    /// <param name="name">The name of the case.</param>
    /// <returns>The documents and the ratio.</returns>
    private static (string Small, string Large, double Factor) GetCase(string name)
    {
        int depth = new SvgLimits().MaxNestingDepth - 8;
        return name switch
        {
            "doubling var() chain" => (SvgCssHostileDocumentTests.DoublingDocument(30, 200), SvgCssHostileDocumentTests.DoublingDocument(60, 200), 1),
            "use instances of long values" => (SvgCssHostileDocumentTests.UseDocument(1_000), SvgCssHostileDocumentTests.UseDocument(10_000), 10),
            "long var() reference chain" => (
                SvgCssHostileDocumentTests.ReferenceChainDocument(2_000),
                SvgCssHostileDocumentTests.ReferenceChainDocument(20_000),
                10),
            "deep calc() nesting" => (
                SvgCssHostileDocumentTests.NestedCalcDocument(10_000),
                SvgCssHostileDocumentTests.NestedCalcDocument(100_000),
                10),
            "long calc() expressions" => (
                SvgCssHostileDocumentTests.LongCalcDocument(200, 1_000),
                SvgCssHostileDocumentTests.LongCalcDocument(200, 10_000),
                10),
            "deep color-mix() nesting" => (
                SvgCssHostileDocumentTests.NestedColorDocument(2_000),
                SvgCssHostileDocumentTests.NestedColorDocument(20_000),
                10),
            "huge nonzero polygon() clip" => (
                SvgSvg2HostileDocumentTests.PolygonDocument("nonzero", 20_000),
                SvgSvg2HostileDocumentTests.PolygonDocument("nonzero", 200_000),
                10),
            "huge evenodd polygon() clip" => (
                SvgSvg2HostileDocumentTests.PolygonDocument("evenodd", 20_000),
                SvgSvg2HostileDocumentTests.PolygonDocument("evenodd", 200_000),
                10),
            "long path() clip" => (SvgSvg2HostileDocumentTests.PathDocument(20_000), SvgSvg2HostileDocumentTests.PathDocument(200_000), 10),
            "stroke boxes of nested groups" => (
                SvgSvg2HostileDocumentTests.NestedStrokeBoxDocument(40, 1_000),
                SvgSvg2HostileDocumentTests.NestedStrokeBoxDocument(40, 10_000),
                10),
            "pathLength of millions of dashes" => (
                SvgSvg2HostileDocumentTests.DashDocument("1e3"),
                SvgSvg2HostileDocumentTests.DashDocument("1e7"),
                1),
            "thousands of font feature settings" => (
                SvgSvg2HostileDocumentTests.FeatureDocument(2_000),
                SvgSvg2HostileDocumentTests.FeatureDocument(20_000),
                12),
            ":lang() with thousands of ranges" => (
                SvgSvg2HostileDocumentTests.LangDocument(2_000),
                SvgSvg2HostileDocumentTests.LangDocument(20_000),
                10),
            "long custom property value" => (
                SvgStyleHostileDocumentTests.CustomValueDocument(new string('a', 200_000)),
                SvgStyleHostileDocumentTests.CustomValueDocument(new string('a', 2_000_000)),
                10),
            "thousands of custom properties" => (
                SvgStyleHostileDocumentTests.ManyCustomPropertiesDocument(2_000),
                SvgStyleHostileDocumentTests.ManyCustomPropertiesDocument(20_000),
                10),
            "deep chain of custom properties" => (
                SvgStyleHostileDocumentTests.DeepCustomPropertiesDocument(depth, 20),
                SvgStyleHostileDocumentTests.DeepCustomPropertiesDocument(depth, 200),
                10),
            "long polygon() in clip-path" => (
                SvgStyleHostileDocumentTests.PolygonDocument(20_000),
                SvgStyleHostileDocumentTests.PolygonDocument(200_000),
                10),
            "long d: path()" => (
                SvgStyleHostileDocumentTests.PathDocument("d: path('{0}')", 20_000),
                SvgStyleHostileDocumentTests.PathDocument("d: path('{0}')", 200_000),
                10),
            "long clip-path: path()" => (
                SvgStyleHostileDocumentTests.PathDocument("clip-path: path(evenodd, '{0}')", 20_000),
                SvgStyleHostileDocumentTests.PathDocument("clip-path: path(evenodd, '{0}')", 200_000),
                10),
            "var() inside calc() inside a basic shape" => (VarCalcShapeDocument(30), VarCalcShapeDocument(60), 1),
            "several limits at once" => (SeveralLimitsDocument(100), SeveralLimitsDocument(1_000), 10),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown case."),
        };
    }

    /// <summary>
    /// Returns a document whose basic shape takes its radius from the last step of a chain of custom properties, each
    /// of which is <c>calc()</c> of its predecessor twice, so the text and the terms double at each step.
    /// </summary>
    /// <param name="steps">The number of steps of the chain.</param>
    /// <returns>The document.</returns>
    private static string VarCalcShapeDocument(int steps)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<style>svg { --v0: 1px;");
        for (int i = 1; i <= steps; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $" --v{i}: calc(var(--v{i - 1}) + var(--v{i - 1}));");
        }

        int divisor = 1 << Math.Min(steps, 30);
        builder.Append(CultureInfo.InvariantCulture, $" }} rect {{ clip-path: circle(calc(var(--v{steps}) / {divisor} * 90) at 50% 50%) }}</style>");
        for (int i = 0; i < 200; i++)
        {
            builder.Append("<rect width=\"200\" height=\"200\" fill=\"green\"/>");
        }

        return builder.Append("</svg>").ToString();
    }

    /// <summary>
    /// Returns a document with a doubling chain of custom properties, deep <c>calc()</c> and <c>color-mix()</c> nesting,
    /// a long math expression in a basic shape, and nested groups with opacity whose layers need more than the default
    /// layer limit.
    /// </summary>
    /// <param name="elements">The number of elements that use the hostile values.</param>
    /// <returns>The document.</returns>
    private static string SeveralLimitsDocument(int elements)
    {
        StringBuilder builder = new("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"1000\"><style>svg { --v0: abcdefgh;");
        for (int i = 1; i <= 40; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $" --v{i}: var(--v{i - 1}) var(--v{i - 1});");
        }

        builder.Append(" } .s { fill: var(--v40, green); clip-path: circle(calc(");
        builder.Append(string.Join(" + ", Enumerable.Repeat("1px", 1_000)));
        builder.Append(")); stroke-width: calc(").Append(new string('(', 100)).Append("1px").Append(new string(')', 100)).Append(") }</style>");
        for (int i = 0; i < 300; i++)
        {
            builder.Append("<g opacity=\"0.9\"><rect width=\"1000\" height=\"1000\" fill=\"red\"/>");
        }

        builder.Append("<g fill=\"").Append(string.Concat(Enumerable.Repeat("color-mix(in srgb, ", 100))).Append("red");
        builder.Append(string.Concat(Enumerable.Repeat(", blue)", 100))).Append("\">");
        for (int i = 0; i < elements; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect class=\"s\" x=\"{i % 990}\" width=\"10\" height=\"10\"/>");
        }

        builder.Append("</g>");
        for (int i = 0; i < 300; i++)
        {
            builder.Append("</g>");
        }

        return builder.Append("</svg>").ToString();
    }

    private static void AssertUnclipped(string svg)
    {
        // The radius is invalid, so the clip-path declaration is unset and the rectangle covers its corner.
        SvgDocument document = SvgDocument.Parse(svg);
        using Image<Rgba32> image = document.Render<Rgba32>();
        Assert.Equal(new Rgba32(0, 128, 0, 255), image[2, 2]);
    }

    private static void AssertLayerLimit(string svg)
    {
        // Loading succeeds: each hostile value is invalid on its own. Drawing needs about 300 layers of the whole image.
        byte[] data = Encoding.UTF8.GetBytes(svg);
        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(
            () => SvgDocument.Load(new MemoryStream(data)).Render<Rgba32>().Dispose());
        Assert.Contains("layer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void RenderOrInvalid(byte[] data)
    {
        try
        {
            using Image<Rgba32> image = SvgDocument.Load(new MemoryStream(data), SvgSvg2RenderTests.TextOptions).Render<Rgba32>();
        }
        catch (InvalidImageContentException exception)
        {
            this.output.WriteLine($"  {exception.Message}");
        }
    }
}
