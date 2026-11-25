// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgReaderTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">";

    [Fact]
    public void Read_StoresElementsInDocumentOrderWithSubtreeRanges()
    {
        SvgElementTree tree = Read("<g id=\"a\"><rect/><circle/></g><path d=\"M0 0\"/>");

        Assert.Equal(5, tree.Count);
        Assert.Equal(
            [SvgElementKind.Svg, SvgElementKind.G, SvgElementKind.Rect, SvgElementKind.Circle, SvgElementKind.Path],
            tree.Elements.ToArray().Select(e => e.Kind));
        Assert.Equal([-1, 0, 1, 1, 0], tree.Elements.ToArray().Select(e => e.Parent));
        Assert.Equal([5, 4, 3, 4, 5], tree.Elements.ToArray().Select(e => e.SubtreeEnd));

        Assert.Equal(1, tree.GetFirstChild(0));
        Assert.Equal(4, tree.GetNextSibling(1));
        Assert.Equal(-1, tree.GetNextSibling(4));
        Assert.Equal(3, tree.GetNextSibling(2));
        Assert.Equal(-1, tree.GetNextSibling(3));
        Assert.Equal(-1, tree.GetFirstChild(2));
        Assert.Equal(-1, tree.GetNextSibling(0));
    }

    [Fact]
    public void Read_ReadsAStream()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(SvgOpen + "<rect width=\"5\"/></svg>"));

        SvgElementTree tree = SvgReader.Read(stream, SvgReaderOptions.Default);

        Assert.Equal(SvgElementKind.Rect, tree[1].Kind);
        Assert.Equal(new SvgLength(5, SvgLengthUnit.Number), tree.GetRect(1).Width);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Read_ReadsASpan()
    {
        SvgElementTree tree = SvgReader.Read((SvgOpen + "<circle r=\"2\"/></svg>").AsSpan(), SvgReaderOptions.Default);

        Assert.Equal(new SvgLength(2, SvgLengthUnit.Number), tree.GetCircle(1).R);
    }

    [Fact]
    public void Read_KeepsUnknownAndForeignElementsInTheTree()
    {
        SvgElementTree tree = Read("<blah><rect/></blah><x:foo xmlns:x=\"urn:x\"><circle/></x:foo><svg:rect xmlns:svg=\"http://www.w3.org/2000/svg\"/>");

        Assert.Equal(SvgElementKind.Unknown, tree[1].Kind);
        Assert.Equal("blah", tree[1].LocalName);
        Assert.Equal(SvgElementKind.Rect, tree[2].Kind);
        Assert.Equal(1, tree[2].Parent);
        Assert.Equal(SvgElementKind.Unknown, tree[3].Kind);
        Assert.Equal("foo", tree[3].LocalName);
        Assert.Equal(SvgElementKind.Circle, tree[4].Kind);

        // A prefixed element in the SVG namespace is an SVG element.
        Assert.Equal(SvgElementKind.Rect, tree[5].Kind);
    }

    [Fact]
    public void Read_ElementInAnotherNamespaceIsUnknownEvenWithAnSvgName()
        => Assert.Equal(SvgElementKind.Unknown, Read("<rect xmlns=\"urn:other\"/>")[1].Kind);

    [Fact]
    public void Read_IndexesIdsAndTheFirstElementWins()
    {
        SvgElementTree tree = Read("<rect id=\"x\"/><circle id=\"x\"/><g id=\"y\"/>");

        Assert.True(tree.TryGetElementById("x", out int x));
        Assert.Equal(1, x);
        Assert.Equal("x", tree[2].Id);
        Assert.True(tree.TryGetElementById("y".AsSpan(), out int y));
        Assert.Equal(3, y);
        Assert.False(tree.TryGetElementById("z", out _));
    }

    [Fact]
    public void Read_SplitsClassNames()
    {
        SvgElementTree tree = Read("<rect class=\" a  b\tc \"/><rect class=\"b\"/>");

        Assert.Equal(["a", "b", "c"], tree.GetClassNames(1).ToArray());
        Assert.Equal(["b"], tree.GetClassNames(2).ToArray());

        // Class names are interned, so equal names are one instance.
        Assert.Same(tree.GetClassNames(1)[1], tree.GetClassNames(2)[0]);
    }

    [Theory]
    [InlineData("<use href=\"#a\" xlink:href=\"#b\"/>", "a")]
    [InlineData("<use xlink:href=\"#b\" href=\"#a\"/>", "a")]
    [InlineData("<use xlink:href=\"#b\"/>", "b")]
    [InlineData("<use href=\" #a \"/>", "a")]
    [InlineData("<use href=\"other.svg#a\"/>", null)]
    [InlineData("<use href=\"#\"/>", null)]
    [InlineData("<use/>", null)]
    public void Read_HrefWinsOverXLinkHref(string content, string expected)
        => Assert.Equal(expected, Read(content).GetUse(1).Href);

    [Fact]
    public void Read_ParsesGeometryAttributes()
    {
        SvgElementTree tree = Read(
            """
            <rect x="1" y="2%" width="3mm" height="4in" rx="5" ry="6em"/>
            <circle cx="1" cy="2" r="3"/>
            <ellipse cx="1" cy="2" rx="3" ry="4"/>
            <line x1="1" y1="2" x2="3" y2="4"/>
            <polyline points="0,0 10,10"/>
            <polygon points="0,0 10,0 10,10"/>
            <use x="1" y="2" width="3" height="4" href="#a"/>
            <symbol viewBox="0 0 10 20" preserveAspectRatio="xMinYMax slice" width="5"/>
            <stop offset="25%"/>
            """);

        ref readonly SvgRectAttributes rect = ref tree.GetRect(1);
        Assert.Equal(new SvgLength(1, SvgLengthUnit.Number), rect.X);
        Assert.Equal(new SvgLength(2, SvgLengthUnit.Percent), rect.Y);
        Assert.Equal(new SvgLength(3, SvgLengthUnit.Mm), rect.Width);
        Assert.Equal(new SvgLength(4, SvgLengthUnit.In), rect.Height);
        Assert.Equal(new SvgLength(5, SvgLengthUnit.Number), rect.Rx);
        Assert.Equal(new SvgLength(6, SvgLengthUnit.Em), rect.Ry);

        Assert.Equal(new SvgLength(3, SvgLengthUnit.Number), tree.GetCircle(2).R);
        Assert.Equal(new SvgLength(4, SvgLengthUnit.Number), tree.GetCircle(3).Ry);
        Assert.False(tree.GetCircle(3).R.IsSpecified);
        Assert.Equal(new SvgLength(4, SvgLengthUnit.Number), tree.GetLine(4).Y2);
        Assert.Equal([new PointF(0, 0), new PointF(10, 10)], tree.GetPoints(5).ToArray());
        Assert.Equal(3, tree.GetPoints(6).Length);

        ref readonly SvgUseAttributes use = ref tree.GetUse(7);
        Assert.Equal(new SvgLength(4, SvgLengthUnit.Number), use.Height);
        Assert.Equal("a", use.Href);

        ref readonly SvgViewportAttributes symbol = ref tree.GetViewport(8);
        Assert.True(symbol.HasViewBox);
        Assert.Equal(new RectangleF(0, 0, 10, 20), symbol.ViewBox);
        Assert.Equal(SvgAlign.XMinYMax, symbol.PreserveAspectRatio.Align);
        Assert.True(symbol.PreserveAspectRatio.Slice);
        Assert.Equal(new SvgLength(5, SvgLengthUnit.Number), symbol.Width);

        Assert.Equal(0.25F, tree.GetStopOffset(9));
    }

    [Fact]
    public void Read_ParsesGradientAttributes()
    {
        SvgElementTree tree = Read(
            """
            <linearGradient x1="1" y1="2" x2="3" y2="4" gradientUnits="userSpaceOnUse" spreadMethod="reflect"
                            gradientTransform="translate(5 6)" xlink:href="#base" cx="9"/>
            <radialGradient cx="1" cy="2" r="3" fx="4" fy="5" fr="6" spreadMethod="repeat" x1="9"/>
            <linearGradient id="base"/>
            """);

        ref readonly SvgGradientAttributes linear = ref tree.GetGradient(1);
        Assert.Equal(new SvgLength(4, SvgLengthUnit.Number), linear.Y2);
        Assert.Equal(SvgGradientUnits.UserSpaceOnUse, linear.Units);
        Assert.Equal(SvgSpreadMethod.Reflect, linear.SpreadMethod);
        Assert.True(linear.HasTransform);
        Assert.Equal(Matrix3x2.CreateTranslation(5, 6), linear.Transform);
        Assert.Equal("base", linear.Href);

        // Attributes of the other gradient kind are not read.
        Assert.False(linear.Cx.IsSpecified);

        ref readonly SvgGradientAttributes radial = ref tree.GetGradient(2);
        Assert.Equal(new SvgLength(6, SvgLengthUnit.Number), radial.Fr);
        Assert.Equal(SvgSpreadMethod.Repeat, radial.SpreadMethod);
        Assert.Equal(SvgGradientUnits.Unspecified, radial.Units);
        Assert.False(radial.HasTransform);
        Assert.False(radial.X1.IsSpecified);

        ref readonly SvgGradientAttributes empty = ref tree.GetGradient(3);
        Assert.False(empty.X1.IsSpecified);
        Assert.Equal(SvgSpreadMethod.Unspecified, empty.SpreadMethod);
    }

    [Fact]
    public void Read_ParsesTransformAttribute()
    {
        SvgElementTree tree = Read("<g transform=\"translate(1 2)\"/><g/><g transform=\"bogus\"/>");

        Assert.True(tree.TryGetTransform(1, out Matrix3x2 transform));
        Assert.Equal(Matrix3x2.CreateTranslation(1, 2), transform);
        Assert.False(tree.TryGetTransform(2, out _));
        Assert.False(tree.TryGetTransform(3, out _));
    }

    [Fact]
    public void Read_KeepsPathDataUpToTheFirstError()
    {
        SvgElementTree tree = Read("<path d=\"M 10 10 L 20 30 L 40\"/><path d=\"M 0 0 L 1e1 2E1\"/><path/><path d=\"\"/>");

        ref readonly SvgPathAttributes partial = ref tree.GetPath(1);
        Assert.False(partial.IsComplete);
        Assert.NotNull(partial.Path);
        Assert.Equal(new RectangleF(10, 10, 10, 20), partial.Path.Bounds);

        ref readonly SvgPathAttributes complete = ref tree.GetPath(2);
        Assert.True(complete.IsComplete);
        Assert.Equal(new RectangleF(0, 0, 10, 20), complete.Path!.Bounds);

        Assert.Null(tree.GetPath(3).Path);
        Assert.True(tree.GetPath(4).IsComplete);
    }

    // SVG path data must start with a move command. Any other first command is the first error, so nothing renders.
    // resvg test suite: shapes/path/A.svg.
    [Fact]
    public void Read_PathDataWithoutAnInitialMoveIsAnError()
    {
        SvgElementTree tree = Read("<path d=\"A 5 5 0 0 1 50 50\"/><path d=\" m 5 5 l 10 0\"/>");

        ref readonly SvgPathAttributes withoutMove = ref tree.GetPath(1);
        Assert.False(withoutMove.IsComplete);
        Assert.Empty(withoutMove.Path!.Flatten());

        Assert.True(tree.GetPath(2).IsComplete);
    }

    // A command after a close path starts a new subpath at the start point of the closed one, for absolute and relative
    // commands. resvg test suite: shapes/path/M-L-Z-A.svg and M-L-Z-L-L.svg.
    [Fact]
    public void Read_CommandAfterClosePathStartsAtTheSubpathStart()
    {
        SvgElementTree tree = Read("<path d=\"M 10 50 L 10 10 L 50 10 z L 100 100\"/><path d=\"M 10 50 L 10 10 L 50 10 z l 0 50\"/>");

        ISimplePath[] absolute = [.. tree.GetPath(1).Path!.Flatten()];
        Assert.Equal(2, absolute.Length);
        Assert.Equal([new PointF(10, 50), new PointF(100, 100)], absolute[1].Points.ToArray());

        ISimplePath[] relative = [.. tree.GetPath(2).Path!.Flatten()];
        Assert.Equal(2, relative.Length);
        Assert.Equal([new PointF(10, 50), new PointF(10, 100)], relative[1].Points.ToArray());
    }

    [Fact]
    public void Read_KeepsPointsUpToTheFirstError()
    {
        SvgElementTree tree = Read("<polyline points=\"0,0 10,10 20\"/>");

        Assert.False(tree.GetPointsAttributes(1).IsComplete);
        Assert.Equal([new PointF(0, 0), new PointF(10, 10)], tree.GetPoints(1).ToArray());
    }

    // SVG error handling: an invalid value makes the attribute unspecified. It never throws.
    [Fact]
    public void Read_InvalidAttributeValuesAreUnspecified()
    {
        SvgElementTree tree = Read(
            """
            <svg viewBox="0 0 -1 1" width="-5" preserveAspectRatio="bogus" transform="rotate("/>
            <rect width="-5" height="x" rx="-1" ry="2" x="1 2"/>
            <circle r="-1"/>
            <linearGradient gradientUnits="bogus" gradientTransform="matrix(1)" spreadMethod="PAD" x1="--1"/>
            <stop offset="abc"/>
            <path d="garbage" fill="url(" stroke-width="-1" opacity="x" stroke-dasharray="1 -2" fill-rule="bogus"/>
            <polygon points="a b"/>
            <use href="#a" x="nope"/>
            """);

        ref readonly SvgViewportAttributes svg = ref tree.GetViewport(1);
        Assert.False(svg.HasViewBox);
        Assert.False(svg.Width.IsSpecified);
        Assert.Equal(SvgAlign.XMidYMid, svg.PreserveAspectRatio.Align);
        Assert.False(tree.TryGetTransform(1, out _));

        ref readonly SvgRectAttributes rect = ref tree.GetRect(2);
        Assert.False(rect.Width.IsSpecified);
        Assert.False(rect.Height.IsSpecified);
        Assert.False(rect.Rx.IsSpecified);
        Assert.True(rect.Ry.IsSpecified);
        Assert.False(rect.X.IsSpecified);

        Assert.False(tree.GetCircle(3).R.IsSpecified);

        ref readonly SvgGradientAttributes gradient = ref tree.GetGradient(4);
        Assert.Equal(SvgGradientUnits.Unspecified, gradient.Units);
        Assert.False(gradient.HasTransform);
        Assert.Equal(SvgSpreadMethod.Unspecified, gradient.SpreadMethod);
        Assert.False(gradient.X1.IsSpecified);

        Assert.Equal(0F, tree.GetStopOffset(5));

        ref readonly SvgPathAttributes path = ref tree.GetPath(6);
        Assert.False(path.IsComplete);
        Assert.True(tree.GetStyle(6).IsEmpty);

        Assert.Equal(0, tree.GetPoints(7).Length);
        Assert.False(tree.GetUse(8).X.IsSpecified);
    }

    [Fact]
    public void Read_StylePropertiesArePresentationAttributes()
    {
        SvgElementTree tree = Read("<rect fill=\"#00f\" stroke-width=\"2px\" Fill=\"red\" display=\"none\"/>");
        ref readonly SvgSpecifiedStyle style = ref tree.GetStyle(1);

        Assert.Equal(SvgPaintKind.Color, style.Fill.Kind);
        Assert.Equal(new SvgLength(2, SvgLengthUnit.Px), style.StrokeWidth);
        Assert.Equal(SvgDisplay.None, style.Display);

        // Attribute names are case-sensitive XML names, so "Fill" is not a presentation attribute.
        Assert.Equal(new Rgba32(0, 0, 255, 255), style.Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void Read_EnforcesTheElementLimit()
    {
        SvgReaderOptions options = new(3, 512, 1 << 20);

        SvgElementTree tree = SvgReader.Read(SvgOpen + "<g/><g/></svg>", options);
        Assert.Equal(3, tree.Count);

        Assert.Throws<InvalidImageContentException>(() => SvgReader.Read(SvgOpen + "<g/><g/><g/></svg>", options));
    }

    [Fact]
    public void Read_EnforcesTheNestingDepthLimit()
    {
        SvgReaderOptions options = new(1000, 3, 1 << 20);

        SvgElementTree tree = SvgReader.Read(SvgOpen + "<g><g/></g></svg>", options);
        Assert.Equal(3, tree.Count);

        Assert.Throws<InvalidImageContentException>(() => SvgReader.Read(SvgOpen + "<g><g><g/></g></g></svg>", options));
    }

    [Fact]
    public void Read_DeepNestingWithinTheLimitDoesNotRecurse()
    {
        const int depth = 100_000;
        StringBuilder builder = new(SvgOpen);
        builder.Insert(builder.Length, "<g>", depth).Insert(builder.Length, "</g>", depth).Append("</svg>");

        SvgElementTree tree = SvgReader.Read(builder.ToString(), new SvgReaderOptions(1_000_000, depth + 1, 1 << 20));

        Assert.Equal(depth + 1, tree.Count);
        Assert.Equal(depth - 1, tree[depth].Parent);
    }

    [Fact]
    public void Read_ExpandsInternalEntities()
    {
        // Adobe Illustrator exports declare the namespace and other values as internal entities.
        const string svg =
            """
            <?xml version="1.0"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
              <!ENTITY ns_svg "http://www.w3.org/2000/svg">
              <!ENTITY red "#ff0000">
            ]>
            <svg xmlns="&ns_svg;"><rect fill="&red;"/></svg>
            """;

        SvgElementTree tree = SvgReader.Read(svg, SvgReaderOptions.Default);

        Assert.Equal(SvgElementKind.Rect, tree[1].Kind);
        Assert.Equal(new Rgba32(255, 0, 0, 255), tree.GetStyle(1).Fill.Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void Read_EnforcesTheEntityExpansionLimit()
    {
        const string svg =
            """
            <!DOCTYPE svg [
              <!ENTITY a "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
              <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
            ]>
            <svg xmlns="http://www.w3.org/2000/svg"><g id="&d;"/></svg>
            """;

        // The expansion is 100,000 characters.
        Assert.Equal(2, SvgReader.Read(svg, new SvgReaderOptions(100, 100, 200_000)).Count);
        Assert.Throws<InvalidImageContentException>(() => SvgReader.Read(svg, new SvgReaderOptions(100, 100, 10_000)));
    }

    [Fact]
    public void Read_DoesNotResolveExternalEntities()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "rect { fill: red }");
            string svg =
                $"""
                <!DOCTYPE svg [ <!ENTITY ext SYSTEM "{new Uri(path).AbsoluteUri}"> ]>
                <svg xmlns="http://www.w3.org/2000/svg"><style>&ext;</style><rect/></svg>
                """;

            SvgElementTree tree = null;
            Exception exception = Record.Exception(() => tree = SvgReader.Read(svg, SvgReaderOptions.Default));

            // The reader either rejects the reference or leaves it unexpanded; it never reads the file.
            if (exception is null)
            {
                Assert.Equal(0, tree.StyleRuleCount);
            }
            else
            {
                Assert.IsType<InvalidImageContentException>(exception);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"a\" fill=\"b\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\">&undefined;</svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    public void Read_MalformedXmlThrows(string svg)
        => Assert.Throws<InvalidImageContentException>(() => SvgReader.Read(svg, SvgReaderOptions.Default));

    [Theory]
    [InlineData("<svg/>")]
    [InlineData("<html xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/1999/xhtml\"/>")]
    [InlineData("<g xmlns=\"http://www.w3.org/2000/svg\"/>")]
    public void Read_RootThatIsNotAnSvgElementThrows(string svg)
        => Assert.Throws<InvalidImageContentException>(() => SvgReader.Read(svg, SvgReaderOptions.Default));

    [Theory]
    [InlineData(TestImages.Svg.GhostscriptTiger)]
    [InlineData(TestImages.Svg.Paris30k)]
    public void Read_ReadsTheBenchmarkDocuments(string file)
    {
        string text = File.ReadAllText(TestFile.GetInputFileFullPath(file));
        int expectedPaths = CountOccurrences(text, "<path");

        using FileStream stream = File.OpenRead(TestFile.GetInputFileFullPath(file));
        SvgElementTree tree = SvgReader.Read(stream, SvgReaderOptions.Default);

        int paths = 0;
        for (int i = 0; i < tree.Count; i++)
        {
            if (tree[i].Kind == SvgElementKind.Path)
            {
                paths++;
                Assert.True(tree.GetPath(i).IsComplete);
            }
        }

        Assert.Equal(SvgElementKind.Svg, tree[0].Kind);
        Assert.Equal(expectedPaths, paths);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static SvgElementTree Read(string content) => SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
}
