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
/// Hostile documents for SVG filters, drawn at 1000 by 1000 pixels: each loads and draws within <see cref="SvgLimits"/>
/// or fails with <see cref="InvalidImageContentException"/>. Each case compares the time of a small and a large input
/// (<see cref="SvgGrowth"/>): where a limit or the visible area bounds the work, the large input costs about as much as
/// the small one; otherwise the cost grows linearly. The test output prints the measured times.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgFilterHostileDocumentTests
{
    private const int Size = 1000;

    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"100\" height=\"100\">";

    private const string Square = "<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" fill=\"#1f77b4\" filter=\"url(#f)\"/>";

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterHostileDocumentTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgFilterHostileDocumentTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void HugeFilterRegion_CostsOnlyTheVisiblePixels()
    {
        static string Document(float extent)
            => SvgOpen + string.Create(CultureInfo.InvariantCulture, $"<filter id=\"f\" filterUnits=\"userSpaceOnUse\" x=\"{-extent}\" y=\"{-extent}\" width=\"{2 * extent}\" height=\"{2 * extent}\">")
            + "<feGaussianBlur stdDeviation=\"3\"/><feOffset dx=\"2\"/></filter>" + Square + "</svg>";

        TimeSpan small = this.MeasureWarm("region of 200 units", () => Assert.Equal(255, Draw(Document(100F)).A));
        TimeSpan large = this.Measure("region of 2e9 units", () => Assert.Equal(255, Draw(Document(1E9F)).A));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void HugeStandardDeviation_IsCappedInFilterPixels()
    {
        // The blur limits the deviation to SvgFeGaussianBlur.MaxDeviation filter pixels, which bounds its reach: every
        // deviation above the cap costs the same. At 10 pixels per unit, 2,000 units are 20,000 pixels, above the cap.
        static string Document(string deviation)
            => SvgOpen + $"<filter id=\"f\" x=\"-1\" y=\"-1\" width=\"3\" height=\"3\"><feGaussianBlur stdDeviation=\"{deviation}\"/></filter>" + Square + "</svg>";

        TimeSpan small = this.MeasureWarm("stdDeviation 2000", () => Draw(Document("2000")));
        TimeSpan large = this.Measure("stdDeviation 1e30", () => Draw(Document("1e30")));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void HugeMorphologyRadius_CostsNoMoreThanASmallOne()
    {
        static string Document(string radius)
            => SvgOpen + $"<filter id=\"f\" x=\"-1\" y=\"-1\" width=\"3\" height=\"3\"><feMorphology operator=\"dilate\" radius=\"{radius}\"/></filter>" + Square + "</svg>";

        TimeSpan small = this.MeasureWarm("radius 10", () => Draw(Document("10")));
        TimeSpan large = this.Measure("radius 1e30", () => Draw(Document("1e30")));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void LargeConvolutionKernel_CostsInProportionToItsEntriesUpToTheLimit()
    {
        static string Document(int order)
        {
            string kernel = string.Join(' ', Enumerable.Repeat("1", order * order));
            return SvgOpen + $"<filter id=\"f\" x=\"-0.2\" y=\"-0.2\" width=\"1.4\" height=\"1.4\"><feConvolveMatrix order=\"{order}\" kernelMatrix=\"{kernel}\"/></filter>"
                + Square + "</svg>";
        }

        // 16 by 16 is the largest kernel that runs (SvgFeConvolveMatrix.MaxKernelEntries); a larger one gives
        // transparent black without work.
        TimeSpan small = this.MeasureWarm("4 by 4 kernel", () => Draw(Document(4)));
        TimeSpan large = this.Measure("16 by 16 kernel", () => Draw(Document(16)));
        SvgGrowth.AssertLinear(small, large, 16);

        TimeSpan above = this.Measure("100 by 100 kernel", () => Draw(Document(100)));
        SvgGrowth.AssertBounded(large, above);
    }

    [Fact]
    public void ManyOctaves_AreClamped()
    {
        static string Document(string octaves)
            => SvgOpen + $"<filter id=\"f\"><feTurbulence baseFrequency=\"0.05\" numOctaves=\"{octaves}\"/></filter>" + Square + "</svg>";

        TimeSpan small = this.MeasureWarm("9 octaves", () => Draw(Document("9")));
        TimeSpan large = this.Measure("1e9 octaves", () => Draw(Document("1000000000")));
        SvgGrowth.AssertBounded(small, large);
    }

    [Fact]
    public void HundredsOfPrimitives_CostLinearTime()
    {
        static string Document(int count)
        {
            StringBuilder filter = new(SvgOpen + "<filter id=\"f\" x=\"-0.5\" y=\"-0.5\" width=\"2\" height=\"2\">");
            for (int i = 0; i < count; i++)
            {
                filter.Append(i % 3 == 0 ? "<feOffset dx=\"0.1\"/>" : i % 3 == 1 ? "<feGaussianBlur stdDeviation=\"0.5\"/>" : "<feColorMatrix type=\"hueRotate\" values=\"5\"/>");
            }

            return filter.Append("</filter>").Append(Square).Append("</svg>").ToString();
        }

        // At 300 pixels: each primitive runs over the whole working area, so the cost is the number of primitives
        // times the area, and the area does not change what this case proves.
        TimeSpan small = this.MeasureWarm("40 primitives", () => Draw(Document(40), 300));
        TimeSpan large = this.Measure("400 primitives", () => Draw(Document(400), 300));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void LongFilterList_IsBoundedByTheEntryLimit()
    {
        // A list of 32 entries runs; a longer one is an error that draws nothing, without work.
        static string Document(int pairs)
            => SvgOpen + $"<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" fill=\"#1f77b4\" filter=\"{string.Concat(Enumerable.Repeat("blur(1px) hue-rotate(10deg) ", pairs))}\"/></svg>";

        TimeSpan small = this.MeasureWarm("a list of 4 entries", () => Draw(Document(2)));
        TimeSpan large = this.Measure("a list of 32 entries", () => Draw(Document(16)));
        SvgGrowth.AssertLinear(small, large, 8);

        TimeSpan above = this.Measure("a list of 20,000 entries", () => Draw(Document(10_000)));
        SvgGrowth.AssertBounded(large, above);
    }

    [Fact]
    public void DeeplyNestedFilters_FinishInBoundedTime()
    {
        // Each level blurs the result of the levels inside it. Past three levels each level gets half the pixel budget,
        // so a deep chain costs a few times one level, not the depth times the full budget.
        static string Document(int depth)
        {
            StringBuilder svg = new(SvgOpen + "<filter id=\"f\" x=\"-0.1\" y=\"-0.1\" width=\"1.2\" height=\"1.2\"><feGaussianBlur stdDeviation=\"0.5\"/></filter>");
            for (int i = 0; i < depth; i++)
            {
                svg.Append("<g filter=\"url(#f)\">");
            }

            svg.Append("<rect x=\"20\" y=\"20\" width=\"60\" height=\"60\" fill=\"#1f77b4\"/>");
            for (int i = 0; i < depth; i++)
            {
                svg.Append("</g>");
            }

            return svg.Append("</svg>").ToString();
        }

        int depth = new SvgLimits().MaxNestingDepth - 8;
        TimeSpan small = this.MeasureWarm($"{depth / 10} nested filters", () => Draw(Document(depth / 10)));
        TimeSpan large = this.Measure($"{depth} nested filters", () => Draw(Document(depth)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    [Fact]
    public void FeImageFanOut_StopsAtTheNodeLimit()
    {
        // Each level renders, through feImage, a group of four elements that use the filter of the next level, so the
        // content expands to 4^levels elements. The expansion counts against the node limit.
        static string Document(int levels)
        {
            StringBuilder svg = new(SvgOpen + "<defs><rect id=\"e0\" width=\"10\" height=\"10\" fill=\"#2ca02c\"/>");
            for (int i = 1; i <= levels; i++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<filter id=\"f{i}\" filterUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"100\" height=\"100\"><feImage href=\"#g{i - 1}\"/></filter>");
                svg.Append(CultureInfo.InvariantCulture, $"<g id=\"g{i}\">");
                for (int k = 0; k < 4; k++)
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{k * 20}\" width=\"10\" height=\"10\" filter=\"url(#f{i})\"/>");
                }

                svg.Append("</g>");
            }

            return svg.Append("<g id=\"g0\"><use xlink:href=\"#e0\"/></g></defs>")
                .Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#g{levels}\"/></svg>").ToString();
        }

        string ten = Document(10);
        string twenty = Document(20);
        TimeSpan small = this.MeasureWarm("4^10 elements through feImage", () => LoadAndDrawOrFail(ten));
        TimeSpan large = this.Measure("4^20 elements through feImage", () => LoadAndDrawOrFail(twenty));
        SvgGrowth.AssertBounded(small, large);
        Assert.Throws<InvalidImageContentException>(() => SvgDocument.Parse(twenty));
    }

    [Fact]
    public void HundredsOfSmallFilteredElements_CostLinearTime()
    {
        static string Document(int count)
        {
            StringBuilder svg = new(SvgOpen + "<filter id=\"f\" x=\"-0.5\" y=\"-0.5\" width=\"2\" height=\"2\"><feDropShadow dx=\"0.5\" dy=\"0.5\" stdDeviation=\"0.5\"/></filter>");
            for (int i = 0; i < count; i++)
            {
                svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{(i % 40) * 2.5F}\" y=\"{(i / 40 % 40) * 2.5F}\" width=\"1.5\" height=\"1.5\" fill=\"#d62728\" filter=\"url(#f)\"/>");
            }

            return svg.Append("</svg>").ToString();
        }

        TimeSpan small = this.MeasureWarm("160 filtered elements", () => Draw(Document(160)));
        TimeSpan large = this.Measure("1,600 filtered elements", () => Draw(Document(1_600)));
        SvgGrowth.AssertLinear(small, large, 10);
    }

    /// <summary>
    /// Loads and draws a document and returns the pixel at the center of the image, inside the square of most cases.
    /// </summary>
    /// <param name="svg">The document.</param>
    /// <param name="size">The width and height of the image.</param>
    /// <returns>The center pixel.</returns>
    private static Rgba32 Draw(string svg, int size = Size)
    {
        SvgDocument document = SvgDocument.Parse(svg);
        using Image<Rgba32> image = new(size, size);
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, size, size))));
        return image[size / 2, size / 2];
    }

    /// <summary>
    /// Loads and draws a document that may exceed a limit of <see cref="SvgLimits"/>.
    /// </summary>
    /// <param name="svg">The document.</param>
    private static void LoadAndDrawOrFail(string svg)
    {
        try
        {
            Draw(svg);
        }
        catch (InvalidImageContentException)
        {
        }
    }

    private TimeSpan Measure(string name, Action action) => SvgGrowth.Measure(this.output, name, action);

    private TimeSpan MeasureWarm(string name, Action action) => SvgGrowth.MeasureWarm(this.output, name, action);
}
