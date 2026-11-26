// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The <c>filter</c> property.
/// </content>
/// <remarks>
/// <para>
/// An element with a filter resolves into one <see cref="SvgRenderNodeKind.Filter"/> node. Its content (what the element
/// draws without its filter, clip, mask, and opacity) resolves in the user space of the element and becomes a nested
/// document, as a pattern tile does; the node carries the resolved filters of the filter list and the transform of the
/// element, and its bounds are what the last filter can paint, in document user space. Filter Effects orders the effects
/// on one element as filter, clipping, masking, opacity: the filter node sits inside the compositing scope of the
/// element, and the opacity of the element multiplies the filter result.
/// </para>
/// <para>
/// A filter list applies its entries in order, each on the result of the one before. A <c>url()</c> entry uses the
/// region of its <c>filter</c> element; a filter function has a region that clips nothing: what its input can paint,
/// grown by the reach of the function. Filter functions compute in sRGB.
/// </para>
/// <para>
/// Error rules, from Filter Effects Level 1 and checked against Chromium:
/// </para>
/// <list type="bullet">
/// <item><description>A reference to a missing element or to an element that is not a <c>filter</c>, also inside a list
/// of several entries: the whole filter value is ignored and the element draws without a filter (SVG 1.1 and the resvg
/// references do not draw it; Chromium and the resvg references skip only that entry of a list).</description></item>
/// <item><description>A <c>filter</c> without primitives, a filter region without area, and <c>objectBoundingBox</c>
/// filter units or primitive units on an element without a bounding box: the element does not draw.</description></item>
/// <item><description>A filter that uses a primitive that this build does not render: the element draws without a
/// filter.</description></item>
/// <item><description>A filter that is used again by content that its own <c>feImage</c> renders is a cycle: the element
/// that closes the cycle does not draw.</description></item>
/// <item><description>A filter list of more than <see cref="MaxFilterListEntries"/> entries: the element does not draw,
/// which bounds the work of hostile documents.</description></item>
/// <item><description><c>href</c> on <c>filter</c> is not followed: Filter Effects removed it and Chromium ignores it.</description></item>
/// </list>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The default origin of the filter region, as a fraction of the bounding box.
    /// </summary>
    private const float DefaultFilterOrigin = -0.1F;

    /// <summary>
    /// The default size of the filter region, as a fraction of the bounding box.
    /// </summary>
    private const float DefaultFilterSize = 1.2F;

    /// <summary>
    /// The number of nesting levels of filtered content whose filters get the full pixel budget of
    /// <see cref="SvgLimits.MaxFilterPixels"/>. Each deeper level halves the budget.
    /// </summary>
    private const int FullBudgetFilterNesting = 3;

    /// <summary>
    /// The largest number of entries of a filter list. Each entry runs on the result of the one before, over an area
    /// that can reach the pixel limit, so the cost of a list grows with its length; Chromium needs more than 30 seconds
    /// for a list of 400 blurs. Real documents use a few entries. A longer list is an error: the element does not draw.
    /// </summary>
    private const int MaxFilterListEntries = 32;

    /// <summary>
    /// The number of filtered elements whose content is resolving around the current element.
    /// </summary>
    private int filterNesting;

    /// <summary>
    /// The filter data of the filter nodes resolved so far. Created on first use, so documents without filters allocate
    /// nothing for it. A node indexes this list until <see cref="TakeFilters(SvgRenderNode[])"/> moves its data into the
    /// table of the document that holds the node.
    /// </summary>
    private List<SvgFilterNode>? filterNodes;

    /// <summary>
    /// The depth of the filtered element whose filter graph is resolving, which content that <c>feImage</c> references
    /// resolves below, so that it counts against <see cref="SvgLimits.MaxNestingDepth"/>.
    /// </summary>
    private int filterContentDepth;

    /// <summary>
    /// Gets the initial style of the document, which the filter builder needs to compute the style of primitives.
    /// </summary>
    internal SvgComputedStyle InitialStyle => this.initialStyle;

    /// <summary>
    /// Opens the filter of an element before its content resolves. When the filter applies, the context changes to the
    /// user space of the element with an opacity of 1, so the content resolves as the source of the filter.
    /// </summary>
    /// <remarks>
    /// Every entry of the filter list resolves here: a <c>url()</c> entry into the graph of its <c>filter</c> element, a
    /// filter function into the graph of the primitives that Filter Effects defines for it. A function that is the
    /// identity, such as <c>blur(0)</c>, adds no graph, and a list of identities leaves the element without a filter. A
    /// <c>filter</c> element goes on the reference chain while its graph resolves, so content that <c>feImage</c>
    /// references and that uses the same filter again is a cycle.
    /// </remarks>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element. It changes when the filter applies; see the summary.</param>
    /// <param name="scope">When this method returns, contains what to close in <see cref="EndFilter(in SvgFilterScope)"/>.</param>
    /// <returns><see langword="true"/> if the element renders, with or without the filter; <see langword="false"/> if the filter makes it draw nothing.</returns>
    private bool TryBeginFilter(int element, ref SvgResolveContext context, out SvgFilterScope scope)
    {
        scope = default;
        ReadOnlySpan<SvgFilterFunction> functions = context.Style.Filter!.Functions;
        if (functions.Length > MaxFilterListEntries)
        {
            return false;
        }

        List<SvgFilterGraph> graphs = new(functions.Length);
        List<Vector4> growth = new(functions.Length);
        bool hasBox = false;
        RectangleF? box = null;
        foreach (SvgFilterFunction function in functions)
        {
            if (function.Kind != SvgFilterFunctionKind.Url)
            {
                if (this.TryCreateFunctionGraph(in function, in context, out SvgFilterGraph? functionGraph, out Vector4 functionGrowth))
                {
                    graphs.Add(functionGraph!);
                    growth.Add(functionGrowth);
                }

                continue;
            }

            // A reference that names no filter element makes the whole filter value ignored.
            string? id = function.Reference.Id;
            if (id is null || !this.tree.TryGetElementById(id, out int filter) || this.tree[filter].Kind != SvgElementKind.Filter)
            {
                return true;
            }

            // A filter whose content uses the filter again (through feImage) is a cycle, which drops the element, as for
            // the other reference kinds.
            if (this.IsReferenceActive(filter))
            {
                return false;
            }

            // The root element is in the view box space already, as for clips and masks.
            if (!hasBox)
            {
                hasBox = true;
                bool found = element == 0
                    ? this.TryGetChildrenBoundingBox(0, in context, Matrix3x2.Identity, context.Viewport, out RectangleF bounds)
                    : this.TryGetObjectBoundingBox(element, in context, out bounds);
                box = found && bounds.Width > 0F && bounds.Height > 0F ? bounds : null;
            }

            SvgComputedStyle filterStyle = this.ComputeDocumentStyle(filter);
            ref readonly SvgFilterAttributes attributes = ref this.tree.GetFilter(filter);
            if (!this.TryGetFilterRegion(in attributes, filterStyle.FontSize, box, context.Viewport, out RectangleF region))
            {
                return false;
            }

            SvgCoordinateUnits primitiveUnits = attributes.PrimitiveUnits == SvgCoordinateUnits.ObjectBoundingBox
                ? SvgCoordinateUnits.ObjectBoundingBox
                : SvgCoordinateUnits.UserSpaceOnUse;
            if (primitiveUnits == SvgCoordinateUnits.ObjectBoundingBox && box is null)
            {
                return false;
            }

            SvgFilterBuildContext buildContext = new(this, this.tree, region, primitiveUnits, box, context.Viewport);
            int depth = this.filterContentDepth;
            this.filterContentDepth = context.Depth;
            _ = this.TryEnterReference(filter);
            SvgFilterOutcome outcome = SvgFilterBuilder.Build(buildContext, filter, in filterStyle, this.units, out SvgFilterGraph? graph);
            this.ExitReference();
            this.filterContentDepth = depth;
            if (outcome == SvgFilterOutcome.Unsupported)
            {
                return true;
            }

            if (outcome == SvgFilterOutcome.NotRendered)
            {
                return false;
            }

            graphs.Add(graph!);
            growth.Add(default);
        }

        if (graphs.Count == 0)
        {
            return true;
        }

        scope = new SvgFilterScope(this.nodes.Count, [.. graphs], [.. growth], context.Transform, context.Style.Opacity);
        this.filterNesting++;
        SvgComputedStyle contentStyle = context.Style;
        contentStyle.Opacity = 1F;
        context = new SvgResolveContext(in contentStyle, Matrix3x2.Identity, context.Viewport, context.Depth);
        return true;
    }

    /// <summary>
    /// Closes the filter of an element after its content resolved: it takes the content nodes into a nested document and
    /// adds the filter node in their place.
    /// </summary>
    /// <remarks>
    /// The region of a filter function grows from what the entries before it can paint: the bounds of the content for
    /// the first entry, the region of the entry before otherwise, grown by the reach of the function, so the function
    /// clips nothing. The node bounds are the region of the last entry, which bounds everything the chain can paint.
    /// </remarks>
    /// <param name="scope">The scope that <see cref="TryBeginFilter"/> returned.</param>
    private void EndFilter(in SvgFilterScope scope)
    {
        if (scope.Graphs is not SvgFilterGraph[] graphs)
        {
            return;
        }

        this.filterNesting--;
        SvgRenderNode[] nodes = this.TakeNodes(scope.Start, out int maxScopeDepth);

        // What the content can paint, in the user space of the element.
        RectangleF extent = RectangleF.Empty;
        foreach (SvgRenderNode node in nodes)
        {
            extent = UnionNonEmpty(extent, node.Bounds);
        }

        SvgDocument? content = nodes.Length > 0 ? this.CreateContentDocument(nodes, extent, maxScopeDepth) : null;
        Vector4[] growth = scope.Growth!;
        for (int i = 0; i < graphs.Length; i++)
        {
            if (HasArea(graphs[i].Region))
            {
                extent = graphs[i].Region;
                continue;
            }

            // The result covers the input and the input moved by the offset and widened by the reach.
            Vector4 g = growth[i];
            if (HasArea(extent))
            {
                RectangleF moved = new(extent.X + g.X - g.Z, extent.Y + g.Y - g.W, extent.Width + (2F * g.Z), extent.Height + (2F * g.W));
                extent = RectangleF.Union(extent, moved);
            }

            foreach (SvgFilterPrimitive primitive in graphs[i].Primitives)
            {
                primitive.Subregion = extent;
            }

            graphs[i] = new SvgFilterGraph(extent, graphs[i].Primitives);
        }

        // A filter renders its content, and with it the filters inside, while its own images are alive, so nested
        // filters would multiply the memory of one render by the nesting depth, up to SvgLimits.MaxNestingDepth. Halving
        // the budget at each level past the first few bounds a whole chain to a few times the limit, as for pattern
        // tiles; filters below the limit, which are most filters, keep their full resolution.
        int halvings = Math.Min(Math.Max(this.filterNesting - FullBudgetFilterNesting, 0), 30);
        int maxPixels = Math.Max(this.loadOptions.Limits.MaxFilterPixels >> halvings, 1);

        // An element without content still renders through its filter, because primitives such as feFlood produce
        // output without input.
        List<SvgFilterNode> filters = this.filterNodes ??= [];
        filters.Add(new SvgFilterNode(content, graphs, maxPixels));
        this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.Filter,
            FilterIndex = filters.Count - 1,
            TransformIndex = this.GetTransformIndex(scope.Transform),
            Opacity = scope.Opacity,
            Bounds = HasArea(extent) ? TransformBounds(extent, scope.Transform) : RectangleF.Empty,
        });
    }

    /// <summary>
    /// Moves the filter data of the filter nodes of a finished node array into a table of its own and points the nodes at
    /// it, so a document carries only the filters of its own nodes.
    /// </summary>
    /// <param name="nodes">The finished nodes; the filter indexes of their filter nodes are rewritten.</param>
    /// <returns>The table, or <see langword="null"/> when the nodes have no filter node.</returns>
    private SvgFilterNode[]? TakeFilters(SvgRenderNode[] nodes)
    {
        if (this.filterNodes is null)
        {
            return null;
        }

        List<SvgFilterNode>? table = null;
        for (int i = 0; i < nodes.Length; i++)
        {
            ref SvgRenderNode node = ref nodes[i];
            if (node.Kind == SvgRenderNodeKind.Filter)
            {
                table ??= [];
                table.Add(this.filterNodes[node.FilterIndex]);
                node.FilterIndex = table.Count - 1;
            }
        }

        return table?.ToArray();
    }

    /// <summary>
    /// Computes the filter region in the user space of the filtered element.
    /// </summary>
    /// <remarks>
    /// With <c>filterUnits="objectBoundingBox"</c> (the default) the lengths are fractions of the bounding box, a
    /// percentage being a fraction of 1; without a box the filter has no region. With <c>userSpaceOnUse</c> they are user
    /// lengths whose percentages refer to the viewport. The defaults are -10%, -10%, 120%, 120%. A zero or negative width
    /// or height disables rendering of the element (SVG 1.1; Chromium draws nothing).
    /// </remarks>
    /// <param name="attributes">The attributes of the filter.</param>
    /// <param name="fontSize">The font size of the filter element, for font-relative lengths.</param>
    /// <param name="box">The bounding box of the filtered element, or <see langword="null"/>.</param>
    /// <param name="viewport">The viewport of the filtered element.</param>
    /// <param name="region">When this method returns <see langword="true"/>, contains the region.</param>
    /// <returns><see langword="true"/> if the region has area; otherwise, <see langword="false"/>.</returns>
    private bool TryGetFilterRegion(in SvgFilterAttributes attributes, float fontSize, RectangleF? box, SizeF viewport, out RectangleF region)
    {
        region = default;
        float x;
        float y;
        float width;
        float height;
        if (attributes.Units != SvgCoordinateUnits.UserSpaceOnUse)
        {
            if (box is not RectangleF b)
            {
                return false;
            }

            x = b.X + (SvgUnits.ToUser(attributes.X, 1F, fontSize, this.units, DefaultFilterOrigin) * b.Width);
            y = b.Y + (SvgUnits.ToUser(attributes.Y, 1F, fontSize, this.units, DefaultFilterOrigin) * b.Height);
            width = SvgUnits.ToUser(attributes.Width, 1F, fontSize, this.units, DefaultFilterSize) * b.Width;
            height = SvgUnits.ToUser(attributes.Height, 1F, fontSize, this.units, DefaultFilterSize) * b.Height;
        }
        else
        {
            // The percentage defaults refer to the viewport, as for any user space length.
            x = SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, this.units, DefaultFilterOrigin * viewport.Width);
            y = SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, this.units, DefaultFilterOrigin * viewport.Height);
            width = SvgUnits.ToUser(attributes.Width, viewport.Width, fontSize, this.units, DefaultFilterSize * viewport.Width);
            height = SvgUnits.ToUser(attributes.Height, viewport.Height, fontSize, this.units, DefaultFilterSize * viewport.Height);
        }

        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(x) || !float.IsFinite(y))
        {
            return false;
        }

        region = new RectangleF(x, y, width, height);
        return true;
    }

    /// <summary>
    /// Resolves the <c>href</c> of an <c>feImage</c> into a nested document to render: an image resource under the rules
    /// of the <c>image</c> element, or an element of this document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A raster image becomes one draw node that fills its pixel rectangle with the image, sampled as
    /// <paramref name="nearest"/> says. An SVG image becomes its nodes inside a clip to its mapped box, as the nodes of an
    /// SVG image inside <c>image</c> are, under the identity transform. Either document is in the space of
    /// <paramref name="box"/>, which the primitive maps into its subregion.
    /// </para>
    /// <para>
    /// An element resolves as the target of a <c>use</c> element would, in the user space of the filtered element and
    /// with the style it inherits from its own parent. It resolves below the depth of the filtered element and counts
    /// against the node limit. The filter that holds the primitive is on the reference chain while this runs, so an
    /// element inside the content that uses it again does not draw.
    /// </para>
    /// </remarks>
    /// <param name="href">The whole trimmed <c>href</c>.</param>
    /// <param name="nearest">Whether a raster image samples the nearest pixel, from <c>image-rendering</c>.</param>
    /// <param name="viewport">The viewport of the filtered element, which percentages in a referenced element refer to.</param>
    /// <param name="box">When this method returns a document of an image resource, contains its box; otherwise <see langword="null"/>.</param>
    /// <returns>The document, or <see langword="null"/> when the reference names nothing, does not load, or renders nothing.</returns>
    internal SvgDocument? ResolveFilterImage(string href, bool nearest, SizeF viewport, out RectangleF? box)
    {
        box = null;
        int start = this.nodes.Count;
        if (href.StartsWith('#'))
        {
            if (!this.tree.TryGetElementById(href.AsSpan(1), out int element))
            {
                return null;
            }

            int parent = this.tree[element].Parent;
            SvgComputedStyle style = parent >= 0 ? this.ComputeDocumentStyle(parent) : this.initialStyle;
            SvgResolveContext context = new(in style, Matrix3x2.Identity, viewport, this.filterContentDepth);
            this.filterNesting++;
            this.ResolveElement(element, in context, default, default, true);
            this.filterNesting--;
        }
        else
        {
            LoadedImage? image = this.LoadImage(href);
            if (image?.Document is SvgDocument document)
            {
                RectangleF mappedBox = document.MappedBox;
                SvgResolveContext context = new(in this.initialStyle, Matrix3x2.Identity, viewport, this.filterContentDepth);
                this.AddNestedDocument(document, new SvgPreserveAspectRatio(SvgAlign.None, false), mappedBox, in context);
                box = mappedBox;
            }
            else if (image?.Texture is SvgTexture texture)
            {
                RectangleF source = new(0F, 0F, texture.Width, texture.Height);
                box = source;
                SvgRenderNode[] imageNodes =
                [
                    new SvgRenderNode
                    {
                        Kind = SvgRenderNodeKind.Draw,
                        Path = new RectanglePolygon(source),
                        Fill = new SvgImagePaint(texture, Matrix3x2.Identity, nearest, 1F),
                        FillRule = IntersectionRule.NonZero,
                        TransformIndex = 0,
                        Bounds = source,
                    }
                ];

                return new SvgDocument(imageNodes, [Matrix3x2.Identity], source.Size, null, source, new SvgPreserveAspectRatio(SvgAlign.None, false), 0);
            }
        }

        SvgRenderNode[] nodes = this.TakeNodes(start, out int maxScopeDepth);
        if (nodes.Length == 0)
        {
            box = null;
            return null;
        }

        return this.CreateContentDocument(nodes, box ?? new RectangleF(PointF.Empty, viewport), maxScopeDepth);
    }

    /// <summary>
    /// Creates the graph of one filter function: the primitives that Filter Effects Level 1 defines for it, computed in
    /// sRGB, with an empty region that <see cref="EndFilter(in SvgFilterScope)"/> replaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>grayscale()</c> and <c>sepia()</c> are color matrices with the coefficients of Filter Effects, <c>saturate()</c>
    /// and <c>hue-rotate()</c> the matrices of <c>feColorMatrix</c>. <c>brightness()</c> and <c>contrast()</c> are
    /// <c>linear</c> transfer functions on the color channels, <c>invert()</c> the table <c>[amount, 1 - amount]</c> on
    /// them, and <c>opacity()</c> the table <c>[0, amount]</c> on alpha. <c>blur()</c> is <c>feGaussianBlur</c> and
    /// <c>drop-shadow()</c> is <c>feDropShadow</c> at full opacity, with its color resolved against <c>color</c>.
    /// </para>
    /// <para>
    /// The amounts of <c>grayscale()</c>, <c>sepia()</c>, <c>invert()</c>, and <c>opacity()</c> clamp to 1, as Filter
    /// Effects defines at computed value time; the parser has already refused negative amounts. Lengths resolve against
    /// the font size of the element.
    /// </para>
    /// </remarks>
    /// <param name="function">The filter function; not a <c>url()</c>.</param>
    /// <param name="context">The context of the filtered element.</param>
    /// <param name="graph">When this method returns <see langword="true"/>, contains the graph.</param>
    /// <param name="growth">When this method returns <see langword="true"/>, contains the growth; see <see cref="SvgFilterScope.Growth"/>.</param>
    /// <returns><see langword="true"/> if the function changes its input; <see langword="false"/> for the identity.</returns>
    private bool TryCreateFunctionGraph(in SvgFilterFunction function, in SvgResolveContext context, out SvgFilterGraph? graph, out Vector4 growth)
    {
        graph = null;
        growth = default;
        const int source = SvgFilterPrimitive.SourceGraphic;
        float fontSize = context.Style.FontSize;
        float amount = function.Amount;
        float clamped = Math.Min(amount, 1F);
        Span<float> matrix = stackalloc float[20];
        SvgFilterPrimitive? primitive = null;
        switch (function.Kind)
        {
            case SvgFilterFunctionKind.Blur:
            {
                float radius = SvgUnits.ToUser(function.Radius, 0F, fontSize, this.units, 0F);
                if (!(radius > 0F))
                {
                    return false;
                }

                primitive = new SvgFeGaussianBlur(source, new Vector2(radius));
                growth = new Vector4(0F, 0F, 3F * radius, 3F * radius);
                break;
            }

            case SvgFilterFunctionKind.DropShadow:
            {
                float dx = SvgUnits.ToUser(function.OffsetX, 0F, fontSize, this.units, 0F);
                float dy = SvgUnits.ToUser(function.OffsetY, 0F, fontSize, this.units, 0F);
                float radius = SvgUnits.ToUser(function.Radius, 0F, fontSize, this.units, 0F);
                SvgPaint paint = function.Color;
                Color color = paint.Kind == SvgPaintKind.CurrentColor ? context.Style.Color : paint.Color;
                primitive = new SvgFeDropShadow(source, new Vector2(dx, dy), new Vector2(radius), color.ToPixel<RgbaVector>().ToScaledVector4());
                growth = new Vector4(dx, dy, 3F * radius, 3F * radius);
                break;
            }

            case SvgFilterFunctionKind.Grayscale:
            {
                float s = 1F - clamped;
                ReadOnlySpan<float> grayscale =
                [
                    0.2126F + (0.7874F * s), 0.7152F - (0.7152F * s), 0.0722F - (0.0722F * s), 0F, 0F,
                    0.2126F - (0.2126F * s), 0.7152F + (0.2848F * s), 0.0722F - (0.0722F * s), 0F, 0F,
                    0.2126F - (0.2126F * s), 0.7152F - (0.7152F * s), 0.0722F + (0.9278F * s), 0F, 0F,
                    0F, 0F, 0F, 1F, 0F,
                ];
                grayscale.CopyTo(matrix);
                break;
            }

            case SvgFilterFunctionKind.Sepia:
            {
                float s = 1F - clamped;
                ReadOnlySpan<float> sepia =
                [
                    0.393F + (0.607F * s), 0.769F - (0.769F * s), 0.189F - (0.189F * s), 0F, 0F,
                    0.349F - (0.349F * s), 0.686F + (0.314F * s), 0.168F - (0.168F * s), 0F, 0F,
                    0.272F - (0.272F * s), 0.534F - (0.534F * s), 0.131F + (0.869F * s), 0F, 0F,
                    0F, 0F, 0F, 1F, 0F,
                ];
                sepia.CopyTo(matrix);
                break;
            }

            case SvgFilterFunctionKind.Saturate:
                SvgFeColorMatrix.GetSaturateMatrix(amount, matrix);
                break;

            case SvgFilterFunctionKind.HueRotate:
                SvgFeColorMatrix.GetHueRotateMatrix(amount, matrix);
                break;

            case SvgFilterFunctionKind.Brightness:
            case SvgFilterFunctionKind.Contrast:
            {
                if (amount == 1F)
                {
                    return false;
                }

                // brightness: C' = amount * C; contrast: C' = amount * C - amount / 2 + 1 / 2.
                float intercept = function.Kind == SvgFilterFunctionKind.Contrast ? (-0.5F * amount) + 0.5F : 0F;
                float[] table = SvgFeComponentTransfer.CreateLinear(amount, intercept);
                primitive = new SvgFeComponentTransfer(source, [table, table, table, null]);
                break;
            }

            case SvgFilterFunctionKind.Invert:
            {
                if (clamped == 0F)
                {
                    return false;
                }

                float[] table = SvgFeComponentTransfer.CreateTable([clamped, 1F - clamped]);
                primitive = new SvgFeComponentTransfer(source, [table, table, table, null]);
                break;
            }

            default:
            {
                // opacity()
                if (clamped == 1F)
                {
                    return false;
                }

                primitive = new SvgFeComponentTransfer(source, [null, null, null, SvgFeComponentTransfer.CreateTable([0F, clamped])]);
                break;
            }
        }

        // The matrix functions leave the primitive to be made here from the matrix, unless it is the identity.
        if (primitive is null)
        {
            if (SvgFeColorMatrix.IsIdentity(matrix))
            {
                return false;
            }

            primitive = new SvgFeColorMatrix(source, matrix);
        }

        primitive.ColorSpace = SvgColorInterpolation.SRgb;
        graph = new SvgFilterGraph(RectangleF.Empty, [primitive]);
        return true;
    }

    /// <summary>
    /// Returns whether a rectangle has a positive width and height.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns><see langword="true"/> if the rectangle has area; otherwise, <see langword="false"/>.</returns>
    private static bool HasArea(RectangleF rectangle) => rectangle.Width > 0F && rectangle.Height > 0F;

    /// <summary>
    /// Returns the union of two rectangles, either of which may have no area and then does not count.
    /// </summary>
    /// <param name="a">The first rectangle.</param>
    /// <param name="b">The second rectangle.</param>
    /// <returns>The union.</returns>
    private static RectangleF UnionNonEmpty(RectangleF a, RectangleF b)
        => !HasArea(b) ? a : !HasArea(a) ? b : RectangleF.Union(a, b);
}
