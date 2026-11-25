// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The resolve stage: turns an <see cref="SvgElementTree"/> into the flat render model of an <see cref="SvgDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// One walk goes top down from the root. It computes the style of each element from its cascaded specified style and
/// the computed style of its parent, composes transforms into document user space, converts lengths against the nearest
/// viewport, builds the geometry of each shape once, resolves paint to shared brushes and pens, and expands each
/// <c>use</c> reference into its own nodes. After the walk no string parsing and no reference lookup remains.
/// </para>
/// <para>
/// Group opacity takes the cheapest correct form (see <see cref="EndGroup(int, float)"/>): a group whose content is one
/// path with only a fill or only a stroke multiplies the paint alpha, and other groups become a layer bounded to the
/// conservative bounding box of their content.
/// </para>
/// <para>
/// The walk recurses on the element structure. The reader bounds the document depth, and the walk counts the depth
/// that <c>use</c> references add against the same limit, so the recursion depth is bounded. The walk also counts every
/// element instance against <see cref="SvgLimits.MaxNodes"/>, because nested references can multiply the content of a
/// small document.
/// </para>
/// </remarks>
internal sealed class SvgResolver
{
    /// <summary>
    /// The control point distance of a cubic Bézier quarter circle, as a fraction of the radius.
    /// </summary>
    private const float Kappa = 0.5522848F;

    private readonly SvgElementTree tree;
    private readonly float dpi;
    private readonly int maxNodes;
    private readonly int maxDepth;
    private readonly SvgComputedStyle initialStyle;
    private readonly SvgPaintCache paints;
    private readonly List<SvgRenderNode> nodes = [];
    private readonly List<Matrix3x2> transforms = [];
    private readonly Dictionary<Matrix3x2, int> transformIndexes = [];
    private readonly List<int> useChain = [];
    private readonly IPath?[] shapes;
    private readonly Vector3[] shapeContexts;
    private Matrix3x2 lastTransform;
    private int lastTransformIndex = -1;
    private int instanceCount;
    private bool hasEmptyNodes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgResolver"/> class.
    /// </summary>
    /// <param name="tree">The element tree.</param>
    /// <param name="options">The load options.</param>
    private SvgResolver(SvgElementTree tree, SvgLoadOptions options)
    {
        this.tree = tree;
        this.dpi = options.Dpi;
        this.maxNodes = options.Limits.MaxNodes;
        this.maxDepth = options.Limits.MaxNestingDepth;
        this.initialStyle = SvgComputedStyle.CreateInitial(options.CurrentColor);
        this.paints = new SvgPaintCache(tree, this.dpi, in this.initialStyle);
        this.shapes = new IPath?[tree.Count];
        this.shapeContexts = new Vector3[tree.Count];
    }

    /// <summary>
    /// Resolves an element tree into a document.
    /// </summary>
    /// <param name="tree">The element tree. The root element is an <c>svg</c> element.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidImageContentException">
    /// The expansion of <c>use</c> references exceeds <see cref="SvgLimits.MaxNodes"/> or <see cref="SvgLimits.MaxNestingDepth"/>.
    /// </exception>
    public static SvgDocument Resolve(SvgElementTree tree, SvgLoadOptions options)
        => new SvgResolver(tree, options).ResolveDocument();

    /// <summary>
    /// Resolves the root element and its content, then builds the document.
    /// </summary>
    /// <returns>The document.</returns>
    private SvgDocument ResolveDocument()
    {
        SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(0), in this.initialStyle, in this.initialStyle, this.dpi);
        ref readonly SvgViewportAttributes root = ref this.tree.GetViewport(0);

        // The intrinsic size comes from absolute width and height. A missing or percentage side follows the aspect ratio
        // of the view box, and without a view box the browser fallback of 300 x 150 applies. A view box with a zero side
        // is kept by the parser because SVG 2 makes it disable rendering; it gives no size.
        bool hasWidth = root.Width.IsSpecified && root.Width.Unit != SvgLengthUnit.Percent;
        bool hasHeight = root.Height.IsSpecified && root.Height.Unit != SvgLengthUnit.Percent;
        float width = hasWidth ? SvgUnits.ToPixels(root.Width, style.FontSize, this.dpi) : 0F;
        float height = hasHeight ? SvgUnits.ToPixels(root.Height, style.FontSize, this.dpi) : 0F;
        bool viewBoxDisablesRendering = root.HasViewBox && (root.ViewBox.Width <= 0F || root.ViewBox.Height <= 0F);
        RectangleF? viewBox = root.HasViewBox && !viewBoxDisablesRendering ? root.ViewBox : null;

        if (viewBox is RectangleF box)
        {
            if (!hasWidth && !hasHeight)
            {
                width = box.Width;
                height = box.Height;
            }
            else if (!hasWidth)
            {
                width = height * box.Width / box.Height;
            }
            else if (!hasHeight)
            {
                height = width * box.Height / box.Width;
            }
        }
        else
        {
            width = hasWidth ? width : 300F;
            height = hasHeight ? height : 150F;
        }

        // Without a view box, the intrinsic size is the coordinate system that maps into the destination.
        RectangleF mappedBox = viewBox ?? new RectangleF(0F, 0F, width, height);
        bool renders = !viewBoxDisablesRendering
            && width > 0F
            && height > 0F
            && float.IsFinite(width)
            && float.IsFinite(height)
            && style.Display != SvgDisplay.None
            && style.Opacity > 0F;

        if (renders)
        {
            this.instanceCount = 1;
            SvgResolveContext context = new(in style, Matrix3x2.Identity, mappedBox.Size, 1);
            int group = this.BeginGroup(style.Opacity);
            this.ResolveChildren(0, in context);
            this.EndGroup(group, style.Opacity);
        }

        SvgRenderNode[] nodes = this.BuildNodes(out int maxScopeDepth);
        return new SvgDocument(
            nodes,
            [.. this.transforms],
            new SizeF(width, height),
            viewBox,
            mappedBox,
            root.PreserveAspectRatio,
            maxScopeDepth);
    }

    /// <summary>
    /// Resolves the children of an element in document order.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element, which is the parent context of the children.</param>
    private void ResolveChildren(int index, in SvgResolveContext context)
    {
        for (int child = this.tree.GetFirstChild(index); child >= 0; child = this.tree.GetNextSibling(child))
        {
            this.ResolveElement(child, in context, default, default, false);
        }
    }

    /// <summary>
    /// Resolves one element instance and its content.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="parent">The context of the parent, or of the referencing <c>use</c> element.</param>
    /// <param name="useWidth">The <c>width</c> of the referencing <c>use</c> element, for <c>svg</c> and <c>symbol</c>.</param>
    /// <param name="useHeight">The <c>height</c> of the referencing <c>use</c> element, for <c>svg</c> and <c>symbol</c>.</param>
    /// <param name="viaUse">Whether a <c>use</c> element references this element.</param>
    private void ResolveElement(int index, in SvgResolveContext parent, SvgLength useWidth, SvgLength useHeight, bool viaUse)
    {
        SvgElementKind kind = this.tree[index].Kind;

        // Only the phase 1 graphics and container elements render. Definitions, paint servers, style sheets, unknown
        // elements, and later-phase elements render nothing, and neither do their children. A symbol renders only
        // through use.
        switch (kind)
        {
            case SvgElementKind.G:
            case SvgElementKind.Svg:
            case SvgElementKind.Use:
            case SvgElementKind.Path:
            case SvgElementKind.Rect:
            case SvgElementKind.Circle:
            case SvgElementKind.Ellipse:
            case SvgElementKind.Line:
            case SvgElementKind.Polyline:
            case SvgElementKind.Polygon:
                break;
            case SvgElementKind.Symbol when viaUse:
                break;
            default:
                return;
        }

        if (++this.instanceCount > this.maxNodes)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the node limit of {this.maxNodes} after use references are expanded.");
        }

        int depth = parent.Depth + 1;
        if (depth > this.maxDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.maxDepth} through use references.");
        }

        SvgComputedStyle parentStyle = parent.Style;
        SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(index), in parentStyle, in this.initialStyle, this.dpi);

        // display: none removes the subtree, and opacity 0 paints nothing.
        if (style.Display == SvgDisplay.None || style.Opacity <= 0F)
        {
            return;
        }

        // The transform attribute maps the user space of the element into the user space of its parent. With row vectors
        // the element transform comes first.
        Matrix3x2 transform = this.tree.TryGetTransform(index, out Matrix3x2 local) ? local * parent.Transform : parent.Transform;
        SvgResolveContext context = new(in style, transform, parent.Viewport, depth);

        switch (kind)
        {
            case SvgElementKind.G:
            {
                int group = this.BeginGroup(style.Opacity);
                this.ResolveChildren(index, in context);
                this.EndGroup(group, style.Opacity);
                break;
            }

            case SvgElementKind.Svg:
            case SvgElementKind.Symbol:
                this.ResolveViewport(index, in context, useWidth, useHeight);
                break;

            case SvgElementKind.Use:
                this.ResolveUse(index, in context);
                break;

            default:
                this.ResolveShape(index, kind, in context);
                break;
        }
    }

    /// <summary>
    /// Resolves a nested <c>svg</c> element, or a <c>symbol</c> that a <c>use</c> element references: a new viewport
    /// with an optional view box transform and a clip to the viewport rectangle.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="useWidth">The <c>width</c> of the referencing <c>use</c> element, which wins when specified.</param>
    /// <param name="useHeight">The <c>height</c> of the referencing <c>use</c> element, which wins when specified.</param>
    private void ResolveViewport(int index, in SvgResolveContext context, SvgLength useWidth, SvgLength useHeight)
    {
        ref readonly SvgViewportAttributes attributes = ref this.tree.GetViewport(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;

        // Width and height default to auto, which is 100% of the parent viewport.
        SvgLength widthLength = useWidth.IsSpecified ? useWidth : attributes.Width;
        SvgLength heightLength = useHeight.IsSpecified ? useHeight : attributes.Height;
        float x = SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, this.dpi, 0F);
        float y = SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, this.dpi, 0F);
        float width = SvgUnits.ToUser(widthLength, viewport.Width, fontSize, this.dpi, viewport.Width);
        float height = SvgUnits.ToUser(heightLength, viewport.Height, fontSize, this.dpi, viewport.Height);

        if (width <= 0F || height <= 0F)
        {
            return;
        }

        RectangleF viewportRectangle = new(x, y, width, height);
        Matrix3x2 content;
        SizeF contentViewport;

        if (attributes.HasViewBox)
        {
            // A view box with a zero side disables rendering of the element.
            RectangleF viewBox = attributes.ViewBox;
            if (viewBox.Width <= 0F || viewBox.Height <= 0F)
            {
                return;
            }

            content = SvgViewBoxTransform.Create(viewBox, attributes.PreserveAspectRatio, viewportRectangle) * context.Transform;
            contentViewport = viewBox.Size;
        }
        else
        {
            content = Matrix3x2.CreateTranslation(x, y) * context.Transform;
            contentViewport = viewportRectangle.Size;
        }

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);

        // The viewport clips its content. The clip rectangle is in the user space of the element, before the view box.
        int clip = this.nodes.Count;
        this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.BeginClip,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Clip = [new RectanglePolygon(viewportRectangle)],
            Bounds = TransformBounds(viewportRectangle, context.Transform),
        });

        SvgResolveContext contentContext = new(context.Style, content, contentViewport, context.Depth);
        this.ResolveChildren(index, in contentContext);

        if (this.nodes.Count == clip + 1)
        {
            // Nothing rendered inside the viewport, so the clip is not needed.
            this.nodes.RemoveAt(clip);
        }
        else
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
        }

        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Resolves a <c>use</c> element: an instance of the referenced element that inherits style from the <c>use</c>
    /// element, translated by <c>x</c> and <c>y</c>.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the <c>use</c> element.</param>
    private void ResolveUse(int index, in SvgResolveContext context)
    {
        ref readonly SvgUseAttributes use = ref this.tree.GetUse(index);
        if (use.Href is null || !this.tree.TryGetElementById(use.Href, out int target))
        {
            return;
        }

        // A reference to the element itself or to an ancestor would contain itself, and a reference back into the chain
        // of use elements being expanded is a cycle. Both render nothing.
        if ((target <= index && index < this.tree[target].SubtreeEnd) || this.useChain.Contains(index))
        {
            return;
        }

        SizeF viewport = context.Viewport;
        float x = SvgUnits.ToUser(use.X, viewport.Width, context.Style.FontSize, this.dpi, 0F);
        float y = SvgUnits.ToUser(use.Y, viewport.Height, context.Style.FontSize, this.dpi, 0F);

        // x and y add a translation after the transform of the use element.
        Matrix3x2 transform = Matrix3x2.CreateTranslation(x, y) * context.Transform;
        SvgResolveContext instance = new(context.Style, transform, viewport, context.Depth);

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        this.useChain.Add(index);
        this.ResolveElement(target, in instance, use.Width, use.Height, true);
        this.useChain.RemoveAt(this.useChain.Count - 1);
        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Resolves a shape into one draw node: its geometry, its fill brush, and its pen.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    private void ResolveShape(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        SvgComputedStyle style = context.Style;

        // visibility hides the shape itself; children of hidden groups can still render, so this is checked per shape.
        if (style.Visibility != SvgVisibility.Visible)
        {
            return;
        }

        IPath? path = this.GetShape(index, kind, in context);
        if (path is null)
        {
            return;
        }

        RectangleF bounds = path.Bounds;
        SizeF viewport = context.Viewport;
        float fontSize = style.FontSize;
        float opacity = style.Opacity;

        // A line has no interior, so it is never filled.
        Brush? fill = kind == SvgElementKind.Line
            ? null
            : this.paints.ResolvePaint(style.Fill, style.Color, style.FillOpacity, bounds, viewport, fontSize);

        Pen? stroke = null;
        float strokeWidth = style.StrokeWidth.Unit == SvgLengthUnit.Percent
            ? style.StrokeWidth.Value * SvgUnits.Diagonal(viewport) / 100F
            : style.StrokeWidth.Value;

        if (strokeWidth > 0F)
        {
            Brush? strokeBrush = this.paints.ResolvePaint(style.Stroke, style.Color, style.StrokeOpacity, bounds, viewport, fontSize);
            if (strokeBrush is not null)
            {
                stroke = this.CreatePen(strokeBrush, strokeWidth, in style, viewport);
            }
        }

        if (fill is null && stroke is null)
        {
            return;
        }

        // Opacity on a shape with only a fill or only a stroke multiplies the paint alpha. With both, the stroke overlaps
        // the fill, so the shape needs a layer.
        bool needsLayer = opacity < 1F && fill is not null && stroke is not null;
        if (opacity < 1F && !needsLayer)
        {
            fill = fill is null ? null : this.paints.MultiplyOpacity(fill, opacity);
            stroke = stroke is null ? null : this.paints.MultiplyOpacity(stroke, opacity);
        }

        // Prime the cached closed form of the path, which every fill uses, while the resolve stage is single threaded.
        if (fill is not null)
        {
            _ = path.AsClosedPath();
        }

        RectangleF paintBounds = stroke is null ? bounds : RectangleF.Inflate(bounds, stroke.GetOutlineRadius(1F), stroke.GetOutlineRadius(1F));
        SvgRenderNode draw = new()
        {
            Kind = SvgRenderNodeKind.Draw,
            Path = path,
            Fill = fill,
            Stroke = stroke,
            FillRule = style.FillRule == SvgFillRule.EvenOdd ? IntersectionRule.EvenOdd : IntersectionRule.NonZero,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Bounds = TransformBounds(paintBounds, context.Transform),
        };

        if (needsLayer)
        {
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.BeginLayer, Opacity = opacity, Bounds = draw.Bounds });
            this.nodes.Add(draw);
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
        }
        else
        {
            this.nodes.Add(draw);
        }
    }

    /// <summary>
    /// Creates the pen of a stroke from the computed stroke properties.
    /// </summary>
    /// <param name="brush">The stroke brush.</param>
    /// <param name="width">The stroke width in user units. It is greater than zero.</param>
    /// <param name="style">The computed style of the shape.</param>
    /// <param name="viewport">The viewport size, for percentages.</param>
    /// <returns>The pen.</returns>
    private Pen CreatePen(Brush brush, float width, in SvgComputedStyle style, SizeF viewport)
    {
        LineCap lineCap = style.StrokeLineCap switch
        {
            SvgStrokeLineCap.Round => LineCap.Round,
            SvgStrokeLineCap.Square => LineCap.Square,
            _ => LineCap.Butt
        };

        // SVG miter falls back to a bevel past the miter limit, which is MiterRevert. miter-clip and arcs have no match
        // and fall back to miter, as SVG 2 allows for user agents that do not support them.
        LineJoin lineJoin = style.StrokeLineJoin switch
        {
            SvgStrokeLineJoin.Round => LineJoin.Round,
            SvgStrokeLineJoin.Bevel => LineJoin.Bevel,
            _ => LineJoin.MiterRevert
        };

        float[]? pattern = null;
        float offset = 0F;
        if (!style.StrokeDashArray.IsNone)
        {
            pattern = this.CreateDashPattern(style.StrokeDashArray, width, style.FontSize, viewport);
            if (pattern is not null)
            {
                float dashOffset = style.StrokeDashOffset.Unit == SvgLengthUnit.Percent
                    ? style.StrokeDashOffset.Value * SvgUnits.Diagonal(viewport) / 100F
                    : style.StrokeDashOffset.Value;

                // The pen measures the dash offset in multiples of the stroke width.
                offset = dashOffset / width;
            }
        }

        return this.paints.GetPen(brush, width, lineCap, lineJoin, style.StrokeMiterLimit, pattern, offset);
    }

    /// <summary>
    /// Converts a <c>stroke-dasharray</c> value to the dash pattern of a pen.
    /// </summary>
    /// <remarks>
    /// The pen measures the pattern in multiples of the stroke width, so each length is divided by the width. A list
    /// with an odd number of values repeats once to make an even number, and a list whose sum is zero draws a solid
    /// stroke, as SVG defines. The parser has already rejected negative values.
    /// </remarks>
    /// <param name="dashArray">The computed dash array.</param>
    /// <param name="width">The stroke width in user units.</param>
    /// <param name="fontSize">The computed font size, for <c>em</c> and <c>ex</c>.</param>
    /// <param name="viewport">The viewport size, for percentages.</param>
    /// <returns>The pattern, or <see langword="null"/> for a solid stroke.</returns>
    private float[]? CreateDashPattern(SvgDashArray dashArray, float width, float fontSize, SizeF viewport)
    {
        ReadOnlySpan<SvgLength> lengths = this.tree.GetDashArray(dashArray);
        float diagonal = SvgUnits.Diagonal(viewport);
        int count = lengths.Length % 2 == 0 ? lengths.Length : lengths.Length * 2;
        float[] pattern = new float[count];
        float sum = 0F;

        for (int i = 0; i < lengths.Length; i++)
        {
            float length = SvgUnits.ToUser(lengths[i], diagonal, fontSize, this.dpi, 0F);
            pattern[i] = length / width;
            sum += length;
        }

        if (!(sum > 0F) || !float.IsFinite(sum))
        {
            return null;
        }

        if (count != lengths.Length)
        {
            pattern.AsSpan(0, lengths.Length).CopyTo(pattern.AsSpan(lengths.Length));
        }

        return pattern;
    }

    /// <summary>
    /// Returns the geometry of a shape in its user space, building it once per element and sharing it across every
    /// <c>use</c> instance with the same viewport and font size.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when the geometry is invalid or empty and the shape does not render.</returns>
    private IPath? GetShape(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        if (kind == SvgElementKind.Path)
        {
            // The parser built the path. An empty path, which the parser builds when the first command has an error, has
            // nothing to paint.
            IPath? path = this.tree.GetPath(index).Path;
            if (path is ComplexPolygon complex)
            {
                using IEnumerator<IPath> subpaths = complex.Paths.GetEnumerator();
                if (!subpaths.MoveNext())
                {
                    return null;
                }
            }

            return path;
        }

        if (kind is SvgElementKind.Polyline or SvgElementKind.Polygon)
        {
            ReadOnlySpan<PointF> points = this.tree.GetPoints(index);
            if (points.Length < 2)
            {
                return null;
            }

            // A polyline is an open path, which fill closes implicitly; a polygon is closed.
            LinearLineSegment segment = new(points.ToArray());
            return kind == SvgElementKind.Polygon ? new Polygon(segment) : new Path(segment);
        }

        // Percentages in the other shapes refer to the viewport, and em and ex to the font size, so the shared geometry is
        // keyed by both. Instances in the same viewport, which is the usual case, share one path.
        Vector3 key = new(context.Viewport.Width, context.Viewport.Height, context.Style.FontSize);
        IPath? cached = this.shapes[index];
        if (cached is not null && this.shapeContexts[index] == key)
        {
            return cached;
        }

        IPath? shape = kind switch
        {
            SvgElementKind.Rect => this.CreateRect(index, in context),
            SvgElementKind.Circle or SvgElementKind.Ellipse => this.CreateEllipse(index, kind, in context),
            _ => this.CreateLine(index, in context),
        };

        if (shape is not null)
        {
            this.shapes[index] = shape;
            this.shapeContexts[index] = key;
        }

        return shape;
    }

    /// <summary>
    /// Builds the geometry of a <c>rect</c> element.
    /// </summary>
    /// <remarks>
    /// A missing <c>rx</c> takes the value of <c>ry</c> and the other way round, then each radius is clamped to half the
    /// size on its axis, as SVG 2 defines. The rounded corners are elliptical arcs with separate radii.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when the width or height is not positive.</returns>
    private IPath? CreateRect(int index, in SvgResolveContext context)
    {
        ref readonly SvgRectAttributes rect = ref this.tree.GetRect(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;

        float width = SvgUnits.ToUser(rect.Width, viewport.Width, fontSize, this.dpi, 0F);
        float height = SvgUnits.ToUser(rect.Height, viewport.Height, fontSize, this.dpi, 0F);
        if (!(width > 0F) || !(height > 0F))
        {
            return null;
        }

        float x = SvgUnits.ToUser(rect.X, viewport.Width, fontSize, this.dpi, 0F);
        float y = SvgUnits.ToUser(rect.Y, viewport.Height, fontSize, this.dpi, 0F);
        float rx = SvgUnits.ToUser(rect.Rx, viewport.Width, fontSize, this.dpi, 0F);
        float ry = SvgUnits.ToUser(rect.Ry, viewport.Height, fontSize, this.dpi, 0F);

        if (!rect.Rx.IsSpecified)
        {
            rx = ry;
        }
        else if (!rect.Ry.IsSpecified)
        {
            ry = rx;
        }

        rx = MathF.Min(rx, width / 2F);
        ry = MathF.Min(ry, height / 2F);

        return rx > 0F && ry > 0F
            ? new RoundedRectanglePolygon(x, y, width, height, new SizeF(rx, ry))
            : new RectanglePolygon(x, y, width, height);
    }

    /// <summary>
    /// Builds the geometry of a <c>circle</c> or <c>ellipse</c> element.
    /// </summary>
    /// <remarks>
    /// The outline starts at the rightmost point and runs through the bottom, as the SVG path equivalent of the shapes
    /// does, so dash patterns start where SVG starts them. Each quarter is a cubic Bézier curve. For an ellipse, a missing
    /// <c>rx</c> takes the value of <c>ry</c> and the other way round, as SVG 2 defines.
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path, or <see langword="null"/> when a radius is not positive.</returns>
    private Polygon? CreateEllipse(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        ref readonly SvgCircleAttributes circle = ref this.tree.GetCircle(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;

        float rx;
        float ry;
        if (kind == SvgElementKind.Circle)
        {
            rx = SvgUnits.ToUser(circle.R, SvgUnits.Diagonal(viewport), fontSize, this.dpi, 0F);
            ry = rx;
        }
        else
        {
            rx = SvgUnits.ToUser(circle.Rx, viewport.Width, fontSize, this.dpi, 0F);
            ry = SvgUnits.ToUser(circle.Ry, viewport.Height, fontSize, this.dpi, 0F);
            if (!circle.Rx.IsSpecified)
            {
                rx = ry;
            }
            else if (!circle.Ry.IsSpecified)
            {
                ry = rx;
            }
        }

        if (!(rx > 0F) || !(ry > 0F))
        {
            return null;
        }

        float cx = SvgUnits.ToUser(circle.Cx, viewport.Width, fontSize, this.dpi, 0F);
        float cy = SvgUnits.ToUser(circle.Cy, viewport.Height, fontSize, this.dpi, 0F);
        float kx = rx * Kappa;
        float ky = ry * Kappa;

        PointF[] points =
        [
            new(cx + rx, cy),
            new(cx + rx, cy + ky), new(cx + kx, cy + ry), new(cx, cy + ry),
            new(cx - kx, cy + ry), new(cx - rx, cy + ky), new(cx - rx, cy),
            new(cx - rx, cy - ky), new(cx - kx, cy - ry), new(cx, cy - ry),
            new(cx + kx, cy - ry), new(cx + rx, cy - ky), new(cx + rx, cy),
        ];

        return new Polygon(new CubicBezierLineSegment(points));
    }

    /// <summary>
    /// Builds the geometry of a <c>line</c> element: an open path with one segment.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns>The path.</returns>
    private Path CreateLine(int index, in SvgResolveContext context)
    {
        ref readonly SvgLineAttributes line = ref this.tree.GetLine(index);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;

        PointF start = new(
            SvgUnits.ToUser(line.X1, viewport.Width, fontSize, this.dpi, 0F),
            SvgUnits.ToUser(line.Y1, viewport.Height, fontSize, this.dpi, 0F));
        PointF end = new(
            SvgUnits.ToUser(line.X2, viewport.Width, fontSize, this.dpi, 0F),
            SvgUnits.ToUser(line.Y2, viewport.Height, fontSize, this.dpi, 0F));

        return new Path(new LinearLineSegment(start, end));
    }

    /// <summary>
    /// Starts a group with an opacity. The group reserves a placeholder node that <see cref="EndGroup(int, float)"/> turns
    /// into a layer when the group needs one, so no node has to move.
    /// </summary>
    /// <param name="opacity">The opacity of the group.</param>
    /// <returns>The index of the placeholder, or -1 when the group is opaque.</returns>
    private int BeginGroup(float opacity)
    {
        if (opacity >= 1F)
        {
            return -1;
        }

        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.None });
        return this.nodes.Count - 1;
    }

    /// <summary>
    /// Ends a group with an opacity and picks the cheapest correct way to apply the opacity to its content.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>A group with no content needs nothing.</item>
    /// <item>A group whose content is one path with only a fill or only a stroke multiplies the paint alpha. One path
    /// cannot overlap itself in paint, so the result is the same as with a layer. Viewport clips around the path do not
    /// change that.</item>
    /// <item>A group whose content is exactly one layer multiplies the opacity of that layer.</item>
    /// <item>Any other group becomes a layer bounded to the union of the paint bounds of its content, which include the
    /// stroke extent.</item>
    /// </list>
    /// </remarks>
    /// <param name="placeholder">The index that <see cref="BeginGroup(float)"/> returned.</param>
    /// <param name="opacity">The opacity of the group.</param>
    private void EndGroup(int placeholder, float opacity)
    {
        if (placeholder < 0)
        {
            return;
        }

        int first = placeholder + 1;
        int end = this.nodes.Count;
        if (first == end)
        {
            this.nodes.RemoveAt(placeholder);
            return;
        }

        Span<SvgRenderNode> nodes = CollectionsMarshal.AsSpan(this.nodes);
        int drawCount = 0;
        int drawIndex = -1;
        bool hasLayer = false;
        RectangleF bounds = default;

        for (int i = first; i < end; i++)
        {
            ref SvgRenderNode node = ref nodes[i];
            if (node.Kind == SvgRenderNodeKind.Draw)
            {
                bounds = drawCount == 0 ? node.Bounds : RectangleF.Union(bounds, node.Bounds);
                drawCount++;
                drawIndex = i;
            }
            else if (node.Kind == SvgRenderNodeKind.BeginLayer)
            {
                hasLayer = true;
            }
        }

        if (!hasLayer && drawCount == 1 && (nodes[drawIndex].Fill is null || nodes[drawIndex].Stroke is null))
        {
            ref SvgRenderNode draw = ref nodes[drawIndex];
            draw.Fill = draw.Fill is null ? null : this.paints.MultiplyOpacity(draw.Fill, opacity);
            draw.Stroke = draw.Stroke is null ? null : this.paints.MultiplyOpacity(draw.Stroke, opacity);
            this.hasEmptyNodes = true;
            return;
        }

        if (nodes[first].Kind == SvgRenderNodeKind.BeginLayer && IsLayerSpanning(nodes, first, end))
        {
            nodes[first].Opacity *= opacity;
            this.hasEmptyNodes = true;
            return;
        }

        nodes[placeholder] = new SvgRenderNode { Kind = SvgRenderNodeKind.BeginLayer, Opacity = opacity, Bounds = bounds };
        this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndLayer });
    }

    /// <summary>
    /// Returns whether the layer that begins at <paramref name="first"/> ends at the last node before <paramref name="end"/>.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="first">The index of a <see cref="SvgRenderNodeKind.BeginLayer"/> node.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns><see langword="true"/> if the layer spans the whole range; otherwise, <see langword="false"/>.</returns>
    private static bool IsLayerSpanning(ReadOnlySpan<SvgRenderNode> nodes, int first, int end)
    {
        int depth = 0;
        for (int i = first; i < end; i++)
        {
            SvgRenderNodeKind kind = nodes[i].Kind;
            if (kind == SvgRenderNodeKind.BeginLayer)
            {
                depth++;
            }
            else if (kind == SvgRenderNodeKind.EndLayer && --depth == 0)
            {
                return i == end - 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the index of a transform in the transform table, adding it when it is new. Consecutive nodes usually share
    /// a transform, so the last lookup is remembered.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The index.</returns>
    private int GetTransformIndex(Matrix3x2 transform)
    {
        if (this.lastTransformIndex >= 0 && transform == this.lastTransform)
        {
            return this.lastTransformIndex;
        }

        if (!this.transformIndexes.TryGetValue(transform, out int index))
        {
            index = this.transforms.Count;
            this.transforms.Add(transform);
            this.transformIndexes.Add(transform, index);
        }

        this.lastTransform = transform;
        this.lastTransformIndex = index;
        return index;
    }

    /// <summary>
    /// Removes the placeholder nodes, links every begin node to its end node, and measures the deepest scope nesting.
    /// </summary>
    /// <param name="maxScopeDepth">When this method returns, contains the deepest nesting of layer and clip scopes.</param>
    /// <returns>The nodes of the document.</returns>
    private SvgRenderNode[] BuildNodes(out int maxScopeDepth)
    {
        ReadOnlySpan<SvgRenderNode> source = CollectionsMarshal.AsSpan(this.nodes);
        int count = source.Length;
        if (this.hasEmptyNodes)
        {
            count = 0;
            foreach (SvgRenderNode node in source)
            {
                if (node.Kind != SvgRenderNodeKind.None)
                {
                    count++;
                }
            }
        }

        SvgRenderNode[] result = new SvgRenderNode[count];
        int[] open = new int[Math.Max(count, 1)];
        int depth = 0;
        int written = 0;
        maxScopeDepth = 0;

        foreach (SvgRenderNode node in source)
        {
            switch (node.Kind)
            {
                case SvgRenderNodeKind.None:
                    continue;
                case SvgRenderNodeKind.BeginLayer:
                case SvgRenderNodeKind.BeginClip:
                    open[depth++] = written;
                    maxScopeDepth = Math.Max(maxScopeDepth, depth);
                    break;
                case SvgRenderNodeKind.EndLayer:
                case SvgRenderNodeKind.EndClip:
                    result[open[--depth]].End = written;
                    break;
            }

            result[written++] = node;
        }

        return result;
    }

    /// <summary>
    /// Returns the axis-aligned bounding box of a transformed rectangle.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <param name="transform">The transform.</param>
    /// <returns>The bounding box.</returns>
    private static RectangleF TransformBounds(RectangleF bounds, Matrix3x2 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        Vector2 a = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), transform);
        Vector2 b = Vector2.Transform(new Vector2(bounds.Right, bounds.Top), transform);
        Vector2 c = Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), transform);
        Vector2 d = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), transform);
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
    }
}
