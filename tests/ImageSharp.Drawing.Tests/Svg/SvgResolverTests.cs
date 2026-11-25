// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Tests the shared resolve helpers that the phase 2 compositing and paint features build on: the object bounding box
/// (SVG 2, "Bounding boxes"), the computed style of the phase 2 properties (CSS Cascade Level 4 and the property
/// definitions), and the entry point for referenced content.
/// </summary>
public class SvgResolverTests
{
    private const string SvgOpen = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">";

    [Theory]
    [InlineData("<rect id=\"e\" x=\"10\" y=\"20\" width=\"30\" height=\"40\" stroke=\"black\" stroke-width=\"10\"/>", 10F, 20F, 30F, 40F)]
    [InlineData("<circle id=\"e\" cx=\"50\" cy=\"60\" r=\"10\"/>", 40F, 50F, 20F, 20F)]
    [InlineData("<ellipse id=\"e\" cx=\"50\" cy=\"60\" rx=\"10\" ry=\"5\"/>", 40F, 55F, 20F, 10F)]
    [InlineData("<path id=\"e\" d=\"M 10 10 L 30 10 L 30 50 Z\"/>", 10F, 10F, 20F, 40F)]
    [InlineData("<polygon id=\"e\" points=\"0 0 10 5 4 20\"/>", 0F, 0F, 10F, 20F)]
    [InlineData("<rect id=\"e\" x=\"10%\" width=\"50%\" height=\"1em\" font-size=\"8\"/>", 20F, 0F, 100F, 8F)]
    [InlineData("<line id=\"e\" x1=\"10\" y1=\"20\" x2=\"50\" y2=\"20\"/>", 10F, 20F, 40F, 0F)]
    public void ObjectBoundingBox_OfAShapeIsItsFillGeometry(string content, float x, float y, float width, float height)
        => AssertBox(content, "e", new RectangleF(x, y, width, height));

    [Fact]
    public void ObjectBoundingBox_OfAGroupIsTheUnionOfItsTransformedChildren()
        => AssertBox(
            "<g id=\"e\" transform=\"translate(1000)\">"
            + "<rect width=\"10\" height=\"10\" transform=\"translate(20 30)\"/>"
            + "<g transform=\"scale(2)\"><circle cx=\"50\" cy=\"50\" r=\"5\"/></g>"
            + "<rect width=\"10\" height=\"10\" transform=\"rotate(45)\"/>"
            + "</g>",
            "e",

            // The rotated square maps to the box of its rotated corners, as browsers compute it: x from -7.07 to 7.07.
            RectangleF.FromLTRB(-7.0710678F, 0F, 110F, 110F));

    // SVG 2: display: none removes an element from the box; visibility and opacity do not.
    [Fact]
    public void ObjectBoundingBox_SkipsDisplayNoneButKeepsHiddenChildren()
        => AssertBox(
            "<g id=\"e\">"
            + "<rect x=\"500\" width=\"10\" height=\"10\" display=\"none\"/>"
            + "<rect x=\"10\" width=\"10\" height=\"10\" visibility=\"hidden\"/>"
            + "<rect x=\"30\" width=\"10\" height=\"10\" opacity=\"0\"/>"
            + "<linearGradient><stop/></linearGradient>"
            + "<rect x=\"-100\" width=\"0\" height=\"10\"/>"
            + "</g>",
            "e",
            new RectangleF(10F, 0F, 30F, 10F));

    [Theory]
    [InlineData("<g id=\"e\"/>")]
    [InlineData("<g id=\"e\"><defs><rect width=\"10\" height=\"10\"/></defs></g>")]
    [InlineData("<g id=\"e\"><g display=\"none\"><rect width=\"10\" height=\"10\"/></g></g>")]
    [InlineData("<rect id=\"e\" width=\"0\" height=\"10\"/>")]
    [InlineData("<use id=\"e\" href=\"#missing\"/>")]
    [InlineData("<g id=\"e\"><use id=\"u\" href=\"#e\"/></g>")]
    [InlineData("<use id=\"e\" href=\"#v\"/><use id=\"v\" href=\"#e\"/>")]
    public void ObjectBoundingBox_IsMissingWhenNothingDraws(string content)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        Assert.False(resolver.TryGetObjectBoundingBox(Index(tree, "e"), Context(resolver, tree, "e"), out _));
    }

    // A use element is a container of its referenced content, translated by x and y after the transform of the target.
    [Fact]
    public void ObjectBoundingBox_OfAUseElementIsItsReferencedContent()
        => AssertBox(
            "<defs><rect id=\"r\" width=\"10\" height=\"20\" transform=\"scale(2)\"/></defs>"
            + "<use id=\"e\" href=\"#r\" x=\"5\" y=\"7\" transform=\"translate(1000)\"/>",
            "e",
            new RectangleF(5F, 7F, 20F, 40F));

    // A symbol referenced by use takes the size of the use element and maps its content through its view box.
    [Fact]
    public void ObjectBoundingBox_OfAUsedSymbolMapsItsViewBox()
        => AssertBox(
            "<symbol id=\"s\" viewBox=\"0 0 10 10\"><rect x=\"5\" width=\"5\" height=\"5\"/></symbol>"
            + "<use id=\"e\" href=\"#s\" x=\"100\" width=\"50\" height=\"50\"/>",
            "e",
            new RectangleF(125F, 0F, 25F, 25F));

    [Fact]
    public void ObjectBoundingBox_OfANestedSvgMapsItsViewBox()
        => AssertBox(
            "<svg id=\"e\" x=\"10\" y=\"10\" width=\"100\" height=\"100\" viewBox=\"0 0 50 50\"><rect x=\"10\" y=\"10\" width=\"10\" height=\"10\"/></svg>",
            "e",
            new RectangleF(30F, 30F, 20F, 20F));

    // The transform-origin of a child applies when its box maps into the parent.
    [Fact]
    public void ObjectBoundingBox_AppliesTheTransformOriginOfChildren()
        => AssertBox(
            "<g id=\"e\"><rect x=\"90\" y=\"90\" width=\"20\" height=\"20\" transform=\"scale(2)\" transform-origin=\"center\"/></g>",
            "e",
            new RectangleF(80F, 80F, 40F, 40F));

    // The own transform of the element is not part of its box: the box is in the user space of the element.
    [Fact]
    public void ObjectBoundingBox_IgnoresTheOwnTransform()
        => AssertBox("<rect id=\"e\" width=\"10\" height=\"10\" transform=\"translate(50 50)\"/>", "e", new RectangleF(0F, 0F, 10F, 10F));

    [Fact]
    public void ComputedStyle_InheritsAndResetsThePhase2Properties()
    {
        SvgElementTree tree = Read(
            "<g id=\"parent\" clip-path=\"url(#c)\" clip-rule=\"evenodd\" mask=\"url(#m)\" mask-type=\"alpha\" mix-blend-mode=\"screen\""
            + " isolation=\"isolate\" paint-order=\"stroke\" vector-effect=\"non-scaling-stroke\" overflow=\"scroll\""
            + " marker-start=\"url(#a)\" marker-mid=\"url(#b)\" marker-end=\"url(#d)\" image-rendering=\"pixelated\""
            + " shape-rendering=\"crispEdges\" color-interpolation=\"linearRGB\" transform-origin=\"center\">"
            + "<rect id=\"child\"/></g>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        SvgComputedStyle child = resolver.ComputeDocumentStyle(Index(tree, "child"));

        // Inherited properties take the parent value.
        Assert.Equal(SvgFillRule.EvenOdd, child.ClipRule);
        Assert.Equal(SvgPaintOrder.StrokeFillMarkers, child.PaintOrder);
        Assert.Equal("a", child.MarkerStart.Id);
        Assert.Equal("b", child.MarkerMid.Id);
        Assert.Equal("d", child.MarkerEnd.Id);
        Assert.Equal(SvgImageRendering.Pixelated, child.ImageRendering);
        Assert.Equal(SvgShapeRendering.CrispEdges, child.ShapeRendering);
        Assert.Equal(SvgColorInterpolation.LinearRgb, child.ColorInterpolation);

        // Properties that do not inherit take the initial value.
        Assert.True(child.ClipPath.IsNone);
        Assert.True(child.Mask.IsNone);
        Assert.Equal(SvgMaskType.Luminance, child.MaskType);
        Assert.Equal(SvgBlendMode.Normal, child.MixBlendMode);
        Assert.Equal(SvgIsolation.Auto, child.Isolation);
        Assert.Equal(SvgVectorEffect.None, child.VectorEffect);
        Assert.Equal(SvgOverflow.Visible, child.Overflow);
        Assert.Equal(new SvgLength(0, SvgLengthUnit.Px), child.TransformOriginX);
        Assert.Equal(new SvgLength(0, SvgLengthUnit.Px), child.TransformOriginY);
    }

    [Fact]
    public void ComputedStyle_InheritKeywordTakesTheParentValueOfAPropertyThatDoesNotInherit()
    {
        SvgElementTree tree = Read(
            "<g clip-path=\"url(#c)\" mix-blend-mode=\"multiply\"><rect id=\"child\" clip-path=\"inherit\" mix-blend-mode=\"inherit\"/></g>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        SvgComputedStyle child = resolver.ComputeDocumentStyle(Index(tree, "child"));

        Assert.Equal("c", child.ClipPath.Id);
        Assert.Equal(SvgBlendMode.Multiply, child.MixBlendMode);
    }

    // SVG 2 user agent style sheet: svg:not(:root), symbol, image, marker, pattern { overflow: hidden }.
    [Theory]
    [InlineData("<svg id=\"e\"/>", (int)SvgOverflow.Hidden)]
    [InlineData("<symbol id=\"e\"/>", (int)SvgOverflow.Hidden)]
    [InlineData("<image id=\"e\"/>", (int)SvgOverflow.Hidden)]
    [InlineData("<marker id=\"e\"/>", (int)SvgOverflow.Hidden)]
    [InlineData("<pattern id=\"e\"/>", (int)SvgOverflow.Hidden)]
    [InlineData("<g id=\"e\"/>", (int)SvgOverflow.Visible)]
    [InlineData("<marker id=\"e\" overflow=\"visible\"/>", (int)SvgOverflow.Visible)]
    [InlineData("<marker id=\"e\" style=\"overflow: initial\"/>", (int)SvgOverflow.Visible)]
    [InlineData("<marker id=\"e\" overflow=\"auto\"/>", (int)SvgOverflow.Auto)]
    [InlineData("<g overflow=\"auto\"><marker id=\"e\" overflow=\"inherit\"/></g>", (int)SvgOverflow.Auto)]
    public void ComputedStyle_OverflowFollowsTheUserAgentStyleSheet(string content, int expected)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        Assert.Equal((SvgOverflow)expected, resolver.ComputeDocumentStyle(Index(tree, "e")).Overflow);
    }

    // Lengths of transform-origin compute to pixels with the font size of the element; percentages stay.
    [Fact]
    public void ComputedStyle_TransformOriginComputesFontRelativeLengths()
    {
        SvgElementTree tree = Read("<rect id=\"e\" font-size=\"10\" transform-origin=\"2em 30%\"/>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        SvgComputedStyle style = resolver.ComputeDocumentStyle(Index(tree, "e"));

        Assert.Equal(new SvgLength(20, SvgLengthUnit.Px), style.TransformOriginX);
        Assert.Equal(new SvgLength(30, SvgLengthUnit.Percent), style.TransformOriginY);
        Assert.Equal(new Vector2(20F, 15F), style.GetTransformOrigin(new SizeF(100F, 50F)));
    }

    // CSS Transforms 1: the effective transform is translate(origin) transform translate(-origin).
    [Fact]
    public void TryGetLocalTransform_RotatesAboutTheOrigin()
    {
        SvgElementTree tree = Read("<rect id=\"e\" transform=\"rotate(90)\" transform-origin=\"50% 50%\"/>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        int index = Index(tree, "e");
        SvgComputedStyle style = resolver.ComputeDocumentStyle(index);

        Assert.True(resolver.TryGetLocalTransform(index, in style, new SizeF(200F, 100F), out Matrix3x2 transform));

        // The origin (100, 50) is fixed; (110, 50) turns to (100, 60).
        Assert.Equal(new Vector2(100F, 50F), Vector2.Transform(new Vector2(100F, 50F), transform));
        Vector2 turned = Vector2.Transform(new Vector2(110F, 50F), transform);
        Assert.Equal(100F, turned.X, 4);
        Assert.Equal(60F, turned.Y, 4);
    }

    // Content of a referenced element inherits from the ancestors of that element and renders through the normal walk.
    [Fact]
    public void ResolveContent_ResolvesTheChildrenAndTakesTheirNodes()
    {
        SvgElementTree tree = Read(
            "<g fill=\"red\"><mask id=\"m\" fill=\"blue\"><rect width=\"10\" height=\"10\"/><circle r=\"5\" opacity=\"0.5\" stroke=\"red\"/></mask></g>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        int mask = Index(tree, "m");
        SvgComputedStyle style = resolver.ComputeDocumentStyle(mask);
        SvgResolveContext context = new(in style, Matrix3x2.Identity, new SizeF(200F, 200F), 1);

        int start = resolver.NodeCount;
        Assert.True(resolver.ResolveContent(mask, in context));
        SvgRenderNode[] nodes = resolver.TakeNodes(start, out int depth);

        // The rectangle, then a layer for the circle with fill and stroke under opacity.
        Assert.Equal(
            [SvgRenderNodeKind.Draw, SvgRenderNodeKind.BeginLayer, SvgRenderNodeKind.Draw, SvgRenderNodeKind.EndLayer],
            nodes.Select(n => n.Kind));
        Assert.Equal(3, nodes[1].End);
        Assert.Equal(1, depth);
        Assert.Equal(start, resolver.NodeCount);
        Assert.Equal(new Rgba32(0, 0, 255, 255), ((SolidBrush)nodes[0].Fill!).Color.ToPixel<Rgba32>());
    }

    [Fact]
    public void ResolveContent_ReturnsFalseForACycle()
    {
        SvgElementTree tree = Read("<pattern id=\"p\"><rect width=\"10\" height=\"10\"/></pattern>");
        SvgResolver resolver = new(tree, new SvgLoadOptions());
        int pattern = Index(tree, "p");
        SvgComputedStyle style = resolver.ComputeDocumentStyle(pattern);
        SvgResolveContext context = new(in style, Matrix3x2.Identity, new SizeF(200F, 200F), 1);

        Assert.True(resolver.TryEnterReference(pattern));
        Assert.True(resolver.IsReferenceActive(pattern));
        Assert.False(resolver.ResolveContent(pattern, in context));
        Assert.Equal(0, resolver.NodeCount);
        resolver.ExitReference();

        Assert.False(resolver.IsReferenceActive(pattern));
        Assert.True(resolver.ResolveContent(pattern, in context));
        Assert.Equal(1, resolver.NodeCount);
    }

    [Fact]
    public void Resolver_ReceivesThePhase2Options()
    {
        SvgLoadOptions options = new()
        {
            Languages = ["fr", "en-GB"],
            ResourceBaseDirectory = "images",
            Limits = new SvgLimits { MaxImagePixels = 100, MaxPatternTilePixels = 200 },
        };

        SvgResolver resolver = new(Read(string.Empty), options);

        Assert.Equal<string>(["fr", "en-GB"], resolver.Languages);
        Assert.Equal("images", resolver.ResourceBaseDirectory);
        Assert.Equal(100, resolver.MaxImagePixels);
        Assert.Equal(200, resolver.MaxPatternTilePixels);
    }

    private static void AssertBox(string content, string id, RectangleF expected)
    {
        SvgElementTree tree = Read(content);
        SvgResolver resolver = new(tree, new SvgLoadOptions());

        Assert.True(resolver.TryGetObjectBoundingBox(Index(tree, id), Context(resolver, tree, id), out RectangleF box));
        Assert.Equal(expected.X, box.X, 3);
        Assert.Equal(expected.Y, box.Y, 3);
        Assert.Equal(expected.Width, box.Width, 3);
        Assert.Equal(expected.Height, box.Height, 3);
    }

    private static SvgResolveContext Context(SvgResolver resolver, SvgElementTree tree, string id)
    {
        SvgComputedStyle style = resolver.ComputeDocumentStyle(Index(tree, id));
        return new SvgResolveContext(in style, Matrix3x2.Identity, new SizeF(200F, 200F), 1);
    }

    private static int Index(SvgElementTree tree, string id)
    {
        Assert.True(tree.TryGetElementById(id, out int index));
        return index;
    }

    private static SvgElementTree Read(string content) => SvgReader.Read(SvgOpen + content + "</svg>", SvgReaderOptions.Default);
}
