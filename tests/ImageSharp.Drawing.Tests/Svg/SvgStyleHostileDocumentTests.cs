// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile documents for the style storage: very long custom property values, thousands of custom properties, deep
/// chains of elements that declare them, and very long <c>polygon()</c> and <c>path()</c> values. Each case loads in
/// time that grows linearly with the input; each compares the time of two input sizes (<see cref="SvgGrowth"/>), so the
/// tests hold on a machine under load. The test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgStyleHostileDocumentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgStyleHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgStyleHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void LongCustomPropertyValue_CostsLinearTime()
    {
        // The value is kept as text; elements that inherit it share the one string.
        string value = new('a', 2_000_000);
        TimeSpan small = this.MeasureWarm("200,000 characters", () => Load(CustomValueDocument(value[..200_000])));
        TimeSpan large = this.Measure("2,000,000 characters", () => Load(CustomValueDocument(value)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void ThousandsOfCustomProperties_CostLinearTime()
    {
        // The root declares n custom properties and each of n children declares one more and uses one with var(), so
        // every child extends the inherited map and keeps a pending declaration.
        TimeSpan small = this.MeasureWarm("2,000 properties", () => Load(ManyCustomPropertiesDocument(2_000)));
        TimeSpan large = this.Measure("20,000 properties", () => Load(ManyCustomPropertiesDocument(20_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void DeepChainOfCustomProperties_CostsLinearTime()
    {
        // Every level of a deep nesting declares k custom properties. Lookups would walk the whole chain without the
        // flattening of SvgCustomProperties, and a flattening per element would copy the chain once per element.
        int depth = new SvgLimits().MaxNestingDepth - 8;
        TimeSpan small = this.MeasureWarm($"{depth} levels of 20", () => Load(DeepCustomPropertiesDocument(depth, 20)));
        TimeSpan large = this.Measure($"{depth} levels of 200", () => Load(DeepCustomPropertiesDocument(depth, 200)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void LongPolygonInClipPath_CostsLinearTime()
    {
        TimeSpan small = this.MeasureWarm("20,000 vertices", () => Load(PolygonDocument(20_000)));
        TimeSpan large = this.Measure("200,000 vertices", () => Load(PolygonDocument(200_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Theory]
    [InlineData("d: path('{0}')")]
    [InlineData("clip-path: path(evenodd, '{0}')")]
    public void LongPathFunction_CostsLinearTime(string declaration)
    {
        TimeSpan small = this.MeasureWarm("20,000 segments", () => Load(PathDocument(declaration, 20_000)));
        TimeSpan large = this.Measure("200,000 segments", () => Load(PathDocument(declaration, 200_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    private static void Load(string document) => _ = SvgDocument.Parse(document);

    private static string CustomValueDocument(string value)
        => SvgOpen + $"<g style=\"--long: {value}\"><g><rect width=\"10\" height=\"10\" style=\"fill: var(--long)\"/></g></g></svg>";

    private static string ManyCustomPropertiesDocument(int count)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<g style=\"");
        for (int i = 0; i < count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"--p{i}: {i}px;");
        }

        builder.Append("\">");
        for (int i = 0; i < count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"<rect width=\"1\" height=\"1\" style=\"--q{i}: red; stroke-width: var(--p{i})\"/>");
        }

        return builder.Append("</g></svg>").ToString();
    }

    private static string DeepCustomPropertiesDocument(int depth, int perLevel)
    {
        StringBuilder builder = new(SvgOpen);
        for (int level = 0; level < depth; level++)
        {
            builder.Append("<g style=\"");
            for (int i = 0; i < perLevel; i++)
            {
                builder.Append(CultureInfo.InvariantCulture, $"--l{level}x{i}: {i};");
            }

            builder.Append("\">");
        }

        builder.Append("<rect width=\"1\" height=\"1\" style=\"opacity: var(--l0x0)\"/>");
        for (int level = 0; level < depth; level++)
        {
            builder.Append("</g>");
        }

        return builder.Append("</svg>").ToString();
    }

    private static string PolygonDocument(int vertices)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect width=\"200\" height=\"200\" style=\"clip-path: polygon(nonzero");
        for (int i = 0; i < vertices; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $", {i % 200}px {i % 197}%");
        }

        return builder.Append(")\"/></svg>").ToString();
    }

    private static string PathDocument(string declaration, int segments)
    {
        StringBuilder data = new("M 0 0");
        for (int i = 0; i < segments; i++)
        {
            data.Append(CultureInfo.InvariantCulture, $" L {i % 200} {i % 199}");
        }

        return SvgOpen + $"<path d=\"M0 0\" style=\"{string.Format(CultureInfo.InvariantCulture, declaration, data)}\"/></svg>";
    }

    private TimeSpan MeasureWarm(string name, Action action) => SvgGrowth.MeasureWarm(this.output, name, action);

    private TimeSpan Measure(string name, Action action) => SvgGrowth.Measure(this.output, name, action);
}
