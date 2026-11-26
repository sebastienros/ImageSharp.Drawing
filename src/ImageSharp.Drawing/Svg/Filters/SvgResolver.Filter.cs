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
/// document, as a pattern tile does; the node carries the resolved filter and the transform of the element, and its
/// bounds are the filter region in document user space. Filter Effects orders the effects on one element as filter,
/// clipping, masking, opacity: the filter node sits inside the compositing scope of the element, and the opacity of the
/// element multiplies the filter result.
/// </para>
/// <para>
/// Error rules, from Filter Effects Level 1 and checked against Chromium:
/// </para>
/// <list type="bullet">
/// <item><description>A reference to a missing element or to an element that is not a <c>filter</c>: the whole filter
/// value is ignored and the element draws without a filter (SVG 1.1 and the resvg references do not draw it).</description></item>
/// <item><description>A <c>filter</c> without primitives, a filter region without area, and <c>objectBoundingBox</c>
/// filter units or primitive units on an element without a bounding box: the element does not draw.</description></item>
/// <item><description>A filter that this build does not render (a list of more than one entry, a filter function, or a
/// primitive that is not implemented): the element draws without a filter.</description></item>
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
    /// Gets the initial style of the document, which the filter builder needs to compute the style of primitives.
    /// </summary>
    internal SvgComputedStyle InitialStyle => this.initialStyle;

    /// <summary>
    /// Opens the filter of an element before its content resolves. When the filter applies, the context changes to the
    /// user space of the element with an opacity of 1, so the content resolves as the source of the filter.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element. It changes when the filter applies; see the summary.</param>
    /// <param name="scope">When this method returns, contains what to close in <see cref="EndFilter(in SvgFilterScope)"/>.</param>
    /// <returns><see langword="true"/> if the element renders, with or without the filter; <see langword="false"/> if the filter makes it draw nothing.</returns>
    private bool TryBeginFilter(int element, ref SvgResolveContext context, out SvgFilterScope scope)
    {
        scope = default;
        SvgFilterList list = context.Style.Filter!;

        // Filter functions and lists of several entries are not rendered yet: the element draws without the filter.
        if (!list.IsSingleUrl)
        {
            return true;
        }

        // A reference that names no filter element makes the whole filter value ignored.
        string? id = list.Functions[0].Reference.Id;
        if (id is null || !this.tree.TryGetElementById(id, out int filter) || this.tree[filter].Kind != SvgElementKind.Filter)
        {
            return true;
        }

        // A filter whose content uses the filter again (through feImage) is a cycle, which drops the element, as for the
        // other reference kinds.
        if (this.IsReferenceActive(filter))
        {
            return false;
        }

        // The root element is in the view box space already, as for clips and masks.
        bool found = element == 0
            ? this.TryGetChildrenBoundingBox(0, in context, Matrix3x2.Identity, context.Viewport, out RectangleF bounds)
            : this.TryGetObjectBoundingBox(element, in context, out bounds);
        RectangleF? box = found && bounds.Width > 0F && bounds.Height > 0F ? bounds : null;

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
        SvgFilterOutcome outcome = SvgFilterBuilder.Build(buildContext, filter, in filterStyle, this.units, out SvgFilterGraph? graph);
        if (outcome == SvgFilterOutcome.Unsupported)
        {
            return true;
        }

        if (outcome == SvgFilterOutcome.NotRendered)
        {
            return false;
        }

        scope = new SvgFilterScope(this.nodes.Count, graph!, context.Transform, context.Style.Opacity);
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
    /// <param name="scope">The scope that <see cref="TryBeginFilter"/> returned.</param>
    private void EndFilter(in SvgFilterScope scope)
    {
        if (scope.Graph is null)
        {
            return;
        }

        this.filterNesting--;
        SvgRenderNode[] nodes = this.TakeNodes(scope.Start, out int maxScopeDepth);
        SvgDocument? content = nodes.Length > 0 ? this.CreateContentDocument(nodes, scope.Graph.Region, maxScopeDepth) : null;

        // A filter renders its content, and with it the filters inside, while its own images are alive, so nested
        // filters would multiply the memory of one render by the nesting depth, up to SvgLimits.MaxNestingDepth. Halving
        // the budget at each level past the first few bounds a whole chain to a few times the limit, as for pattern
        // tiles; filters below the limit, which are most filters, keep their full resolution.
        int halvings = Math.Min(Math.Max(this.filterNesting - FullBudgetFilterNesting, 0), 30);
        int maxPixels = Math.Max(this.loadOptions.Limits.MaxFilterPixels >> halvings, 1);

        // An element without content still renders through its filter, because primitives such as feFlood produce
        // output without input.
        List<SvgFilterNode> filters = this.filterNodes ??= [];
        filters.Add(new SvgFilterNode(content, [scope.Graph], maxPixels));
        this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.Filter,
            FilterIndex = filters.Count - 1,
            TransformIndex = this.GetTransformIndex(scope.Transform),
            Opacity = scope.Opacity,
            Bounds = TransformBounds(scope.Graph.Region, scope.Transform),
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
}
