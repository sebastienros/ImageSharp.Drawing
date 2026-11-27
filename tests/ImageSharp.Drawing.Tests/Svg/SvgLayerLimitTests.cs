// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests <see cref="SvgLimits.MaxLayerPixels"/>: the largest total of the pixels of the layers that one draw keeps open
/// at the same time. Expected totals follow the layer rectangles of the canvas: the bounds of the layer content rounded
/// outward and grown by one pixel for antialiased edges, a mask layer one more pixel for the reach of its effect, each
/// clipped to the layer around it.
/// </summary>
[Collection(SvgSequentialCollection.Name)]
public class SvgLayerLimitTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\">";

    // Two nested groups with opacity, each with children that overlap, so each needs a layer. The outer group covers
    // (10, 10) to (70, 60) and the inner one (20, 20) to (70, 60).
    private const string NestedGroups =
        "<g opacity=\"0.5\"><rect x=\"10\" y=\"10\" width=\"50\" height=\"40\" fill=\"green\"/>"
        + "<g opacity=\"0.5\"><rect x=\"20\" y=\"20\" width=\"50\" height=\"40\" fill=\"blue\"/>"
        + "<rect x=\"30\" y=\"30\" width=\"20\" height=\"20\" fill=\"red\"/></g></g>";

    // The layers of NestedGroups at the intrinsic size: (9, 9) to (71, 61) and (19, 19) to (71, 61).
    private const long NestedGroupsPixels = (62L * 52) + (52L * 42);

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgLayerLimitTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgLayerLimitTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Limit_DefaultAndValidation()
    {
        SvgLimits limits = new();

        Assert.Equal(16L * 4096 * 4096, limits.MaxLayerPixels);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgLimits { MaxLayerPixels = 0 });
    }

    [Fact]
    public void LargestTotal_FollowsTheLayerRectangles()
    {
        SvgDocument document = SvgDocument.Parse(SvgOpen + NestedGroups + "</svg>");

        Assert.Equal(NestedGroupsPixels, LargestTotal(document, new RectangleF(0, 0, 200, 100), Matrix4x4.Identity, new Rectangle(0, 0, 200, 100)));

        // At twice the size the layers cover (19, 19) to (141, 121) and (39, 39) to (141, 121); a canvas 100 pixels high
        // clips the outer one, and the outer one clips the inner one.
        Assert.Equal(
            (122L * 81) + (102L * 61),
            LargestTotal(document, new RectangleF(0, 0, 400, 200), Matrix4x4.Identity, new Rectangle(0, 0, 400, 100)));

        // A caller transform scales the layer rectangles after rounding, as the canvas does: (18, 18) to (142, 122) and
        // (38, 38) to (142, 122).
        Assert.Equal(
            (124L * 104) + (104L * 84),
            LargestTotal(document, new RectangleF(0, 0, 200, 100), Matrix4x4.CreateScale(2F), new Rectangle(0, 0, 400, 200)));
    }

    [Fact]
    public void LargestTotal_CountsOpenLayersOnly()
    {
        // Two groups side by side are open one after the other, so the largest total is one layer: (20, 20) to (35, 30)
        // grows to (19, 19) to (36, 31).
        SvgDocument document = SvgDocument.Parse(
            SvgOpen
            + "<g opacity=\"0.5\"><rect x=\"20\" y=\"20\" width=\"10\" height=\"10\" fill=\"red\"/><rect x=\"25\" y=\"20\" width=\"10\" height=\"10\"/></g>"
            + "<g opacity=\"0.5\"><rect x=\"100\" y=\"20\" width=\"10\" height=\"10\" fill=\"red\"/><rect x=\"105\" y=\"20\" width=\"10\" height=\"10\"/></g>"
            + "</svg>");

        Assert.Equal(17L * 12, LargestTotal(document, new RectangleF(0, 0, 200, 100), Matrix4x4.Identity, new Rectangle(0, 0, 200, 100)));
    }

    [Fact]
    public void LargestTotal_CountsAMaskLayerWithItsReach()
    {
        SvgDocument document = SvgDocument.Parse(
            SvgOpen
            + "<mask id=\"m\" maskUnits=\"userSpaceOnUse\" x=\"50\" y=\"20\" width=\"40\" height=\"30\">"
            + "<rect width=\"200\" height=\"100\" fill=\"white\"/></mask>"
            + "<rect x=\"50\" y=\"20\" width=\"40\" height=\"30\" fill=\"green\" mask=\"url(#m)\"/></svg>");

        // The masked layer covers (49, 19) to (91, 51) and the mask layer one pixel more on each side, clipped to it.
        Assert.Equal(2L * 42 * 32, LargestTotal(document, new RectangleF(0, 0, 200, 100), Matrix4x4.Identity, new Rectangle(0, 0, 200, 100)));
    }

    [Fact]
    public void DrawOverTheLimit_ThrowsBeforeTheCanvasChanges()
    {
        string svg = SvgOpen + "<rect width=\"200\" height=\"100\" fill=\"red\"/>" + NestedGroups + "</svg>";
        long needed = NestedGroupsPixels;
        SvgLoadOptions options = new() { Limits = new SvgLimits { MaxLayerPixels = needed - 1 } };
        SvgDocument document = SvgDocument.Parse(svg, options);

        using Image<Rgba32> image = new(200, 100, new Rgba32(1, 2, 3, 255));
        InvalidImageContentException exception = Assert.Throws<InvalidImageContentException>(
            () => image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty))));

        Assert.Contains(needed.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);

        // The red background comes before the layers in paint order, and it was not drawn either.
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Assert.Equal(new Rgba32(1, 2, 3, 255), image[x, y]);
            }
        }
    }

    [Fact]
    public void DrawAtTheLimit_DrawsAsWithoutIt()
    {
        string svg = SvgOpen + NestedGroups + "</svg>";
        SvgDocument atLimit = SvgDocument.Parse(svg, new SvgLoadOptions { Limits = new SvgLimits { MaxLayerPixels = NestedGroupsPixels } });
        SvgDocument unlimited = SvgDocument.Parse(svg, new SvgLoadOptions { Limits = new SvgLimits { MaxLayerPixels = long.MaxValue } });

        using Image<Rgba32> expected = new(200, 100);
        using Image<Rgba32> actual = new(200, 100);
        expected.Mutate(x => x.Paint(canvas => canvas.DrawSvg(unlimited, PointF.Empty)));
        actual.Mutate(x => x.Paint(canvas => canvas.DrawSvg(atLimit, PointF.Empty)));

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected[x, y], actual[x, y]);
            }
        }

        Assert.NotEqual(default, actual[30, 30]);
    }

    // The content of a filter draws on a canvas of its own, which is checked when the filter runs.
    [Fact]
    public void FilterContentOverTheLimit_Throws()
    {
        string svg = SvgOpen
            + "<filter id=\"f\"><feOffset dx=\"1\"/></filter>"
            + "<g filter=\"url(#f)\">" + NestedGroups + "</g></svg>";
        SvgDocument document = SvgDocument.Parse(svg, new SvgLoadOptions { Limits = new SvgLimits { MaxLayerPixels = 100 } });

        using Image<Rgba32> image = new(200, 100);
        Assert.Throws<InvalidImageContentException>(() => image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, PointF.Empty))));
    }

    /// <summary>
    /// Measures the largest layer totals of the benchmark corpus and of the resvg test suite drawn at 4000 pixels on the
    /// long edge, and checks that every document fits under the default limit with a margin of at least two.
    /// </summary>
    [Fact]
    public void CorpusAndSuite_FitUnderTheDefaultLimit()
    {
        List<(long Pixels, string Name)> totals = [];
        foreach (string file in CorpusFiles())
        {
            SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath(file), SvgTextRenderTests.Options);
            totals.Add((LargestTotalAt(document, 4000), file));
        }

        string testsDirectory = IOPath.Combine(TestEnvironment.GetFullPath(SvgResvgConformanceTests.SuiteRelativePath), "tests");
        int suiteCount = 0;
        if (Directory.Exists(testsDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(testsDirectory, "*.svg", SearchOption.AllDirectories))
            {
                SvgDocument document;
                try
                {
                    document = SvgDocument.Load(file, SvgResvgConformanceTests.CreateSuiteOptions(file));
                }
                catch (InvalidImageContentException)
                {
                    // Tests of malformed documents do not load.
                    continue;
                }

                suiteCount++;
                totals.Add((LargestTotalAt(document, 4000), IOPath.GetRelativePath(testsDirectory, file).Replace('\\', '/')));
            }
        }

        totals.Sort((a, b) => b.Pixels.CompareTo(a.Pixels));
        this.output.WriteLine($"{totals.Count} documents ({suiteCount} of the suite) at 4000 pixels on the long edge; the largest layer totals:");
        foreach ((long pixels, string name) in totals.Take(15))
        {
            this.output.WriteLine($"{pixels,12:N0}  {pixels / (4000.0 * 4000.0),6:0.00} canvases  {name}");
        }

        this.output.WriteLine("The largest layer totals of the benchmark corpus:");
        foreach ((long pixels, string name) in totals.Where(entry => entry.Name.StartsWith("Svg/", StringComparison.Ordinal)).Take(8))
        {
            this.output.WriteLine($"{pixels,12:N0}  {pixels / (4000.0 * 4000.0),6:0.00} canvases  {name}");
        }

        Assert.True(totals[0].Pixels * 2 <= SvgLimits.DefaultMaxLayerPixels, $"{totals[0].Name} needs {totals[0].Pixels} layer pixels.");
    }

    private static IEnumerable<string> CorpusFiles()
    {
        yield return TestImages.Svg.GhostscriptTiger;
        yield return TestImages.Svg.Paris30k;
        yield return TestImages.Svg.FluentSoftIceCream;
        yield return TestImages.Svg.FluentMoose;
        yield return TestImages.Svg.StyledLandscape;
        yield return TestImages.Svg.CompositingDashboard;
        yield return TestImages.Svg.PatternChart;
        yield return TestImages.Svg.TextHeavy;
        yield return TestImages.Svg.TextDecorations;
        yield return TestImages.Svg.FilterShadows;
        yield return TestImages.Svg.FilterBlur;
        yield return TestImages.Svg.FilterChain;
        yield return TestImages.Svg.CssFeatures;
        foreach (string icon in TestImages.Svg.Icons)
        {
            yield return icon;
        }
    }

    private static long LargestTotalAt(SvgDocument document, int longEdge)
    {
        SizeF size = document.Size;
        float scale = longEdge / MathF.Max(size.Width, size.Height);
        int width = Math.Max(1, (int)MathF.Round(size.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(size.Height * scale));
        return LargestTotal(document, new RectangleF(0, 0, width, height), Matrix4x4.Identity, new Rectangle(0, 0, width, height));
    }

    private static long LargestTotal(SvgDocument document, RectangleF destination, Matrix4x4 callerTransform, Rectangle canvas)
        => SvgEmitter.GetLargestLayerPixels(
            document.Nodes,
            document.MaxScopeDepth,
            document.CreateViewportTransform(destination),
            destination,
            in callerTransform,
            canvas);
}
