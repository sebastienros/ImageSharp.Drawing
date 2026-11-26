// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Hostile documents for <c>var()</c> substitution, math functions, and color functions: chains of custom properties that
/// double their length, use instances that substitute long values again, reference chains, deep nesting, and long
/// expressions. Each case compares the time of two input sizes (<see cref="SvgGrowth"/>), so the tests hold on a machine
/// under load. The test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgCssHostileDocumentTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgCssHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void DoublingVarChain_StopsAtTheValueLimit()
    {
        // Each custom property is its predecessor twice, so the value of step n has 2^n times the length of the first.
        // Without a limit, 60 steps would need 2^60 characters; the limit stops the chain after about 12 steps, and every
        // further step only finds an invalid predecessor.
        TimeSpan small = this.MeasureWarm("30 steps", () => AssertFallback(DoublingDocument(30, 200)));
        TimeSpan large = this.Measure("60 steps", () => AssertFallback(DoublingDocument(60, 200)));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void UseInstancesOfLongValues_StopAtTheDocumentBudget()
    {
        // Every use instance computes the custom properties of its content again, and each declares a value of twice a
        // 16,000 character value. The document budget ends the substitution after about 500 instances, so ten times the
        // instances do not cost ten times the substituted characters.
        TimeSpan small = this.MeasureWarm("1,000 instances", () => Load(UseDocument(1_000)));
        TimeSpan large = this.Measure("10,000 instances", () => Load(UseDocument(10_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void LongReferenceChainOnOneElement_CostsLinearTime()
    {
        // One element declares n custom properties, each referring to the one declared after it, so the first lookup walks
        // the chain depth first. The depth limit stops each walk, and each declaration computes once.
        TimeSpan small = this.MeasureWarm("2,000 properties", () => Load(ReferenceChainDocument(2_000)));
        TimeSpan large = this.Measure("20,000 properties", () => Load(ReferenceChainDocument(20_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void DeepCalcNesting_CostsLinearTime()
    {
        // The nesting limit rejects the value after its first levels; the rest of the cost is reading the text.
        TimeSpan small = this.MeasureWarm("10,000 levels", () => Load(NestedCalcDocument(10_000)));
        TimeSpan large = this.Measure("100,000 levels", () => Load(NestedCalcDocument(100_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void LongCalcExpressions_CostLinearTime()
    {
        // Each value has more terms than the limit of one expression; the parser stops at the limit.
        TimeSpan small = this.MeasureWarm("200 values of 1,000 terms", () => Load(LongCalcDocument(200, 1_000)));
        TimeSpan large = this.Measure("200 values of 10,000 terms", () => Load(LongCalcDocument(200, 10_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void DeepColorMixNesting_CostsLinearTime()
    {
        // The nesting limit of color functions rejects the value after 16 levels.
        TimeSpan small = this.MeasureWarm("2,000 levels", () => Load(NestedColorDocument(2_000)));
        TimeSpan large = this.Measure("20,000 levels", () => Load(NestedColorDocument(20_000)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    private static void Load(string document) => _ = SvgDocument.Parse(document);

    private static void AssertFallback(string document)
    {
        // The rectangle takes the fallback of the reference to the last step, which is invalid.
        SvgDocument parsed = SvgDocument.Parse(document);
        Assert.Equal(new Rgba32(0, 128, 0, 255), ((SolidBrush)parsed.Nodes[0].Fill!).Color.ToPixel<Rgba32>());
    }

    private static string DoublingDocument(int steps, int elements)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<style>svg { --v0: abcdefgh;");
        for (int i = 1; i <= steps; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $" --v{i}: var(--v{i - 1}) var(--v{i - 1});");
        }

        builder.Append(CultureInfo.InvariantCulture, $" }} rect {{ fill: var(--v{steps}, green) }}</style>");
        for (int i = 0; i < elements; i++)
        {
            builder.Append("<rect width=\"1\" height=\"1\"/>");
        }

        return builder.Append("</svg>").ToString();
    }

    private static string UseDocument(int instances)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<defs><g id=\"g\" style=\"--twice: var(--long) var(--long)\">");
        builder.Append("<rect width=\"1\" height=\"1\" fill=\"green\"/></g></defs>");
        builder.Append(CultureInfo.InvariantCulture, $"<g style=\"--long: {new string('a', 16_000)}\">");
        for (int i = 0; i < instances; i++)
        {
            builder.Append("<use href=\"#g\"/>");
        }

        return builder.Append("</g></svg>").ToString();
    }

    private static string ReferenceChainDocument(int count)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect width=\"1\" height=\"1\" style=\"");
        for (int i = 0; i < count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"--p{i}: var(--p{i + 1}, green);");
        }

        return builder.Append("fill: var(--p0)\"/></svg>").ToString();
    }

    private static string NestedCalcDocument(int depth)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect height=\"1\" width=\"calc(");
        builder.Append(new string('(', depth)).Append("1px").Append(new string(')', depth)).Append(")\"/>");
        builder.Append("<rect height=\"1\" style=\"width: calc(").Append(new string('(', depth)).Append("1px").Append(new string(')', depth));
        return builder.Append(")\"/></svg>").ToString();
    }

    private static string LongCalcDocument(int values, int terms)
    {
        StringBuilder builder = new(SvgOpen);
        string expression = "calc(" + string.Join(" + ", Enumerable.Repeat("1px", terms)) + ")";
        for (int i = 0; i < values; i++)
        {
            builder.Append("<rect height=\"1\" width=\"").Append(expression).Append("\"/>");
        }

        return builder.Append("</svg>").ToString();
    }

    private static string NestedColorDocument(int depth)
    {
        StringBuilder builder = new(SvgOpen);
        builder.Append("<rect width=\"1\" height=\"1\" fill=\"");
        for (int i = 0; i < depth; i++)
        {
            builder.Append("color-mix(in srgb, ");
        }

        builder.Append("red");
        for (int i = 0; i < depth; i++)
        {
            builder.Append(", blue)");
        }

        return builder.Append("\"/></svg>").ToString();
    }

    private TimeSpan MeasureWarm(string name, Action action) => SvgGrowth.MeasureWarm(this.output, name, action);

    private TimeSpan Measure(string name, Action action) => SvgGrowth.Measure(this.output, name, action);
}
