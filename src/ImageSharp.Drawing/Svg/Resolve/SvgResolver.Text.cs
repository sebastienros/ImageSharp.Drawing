// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>text</c>: stage 7 of the text layout pipeline, which turns laid out text into draw nodes, the object bounding box
/// of text, and text in <c>clipPath</c>. The stages before it live in the <c>Svg/Text</c> folder; see
/// <see cref="SvgTextLayout"/>.
/// </content>
/// <remarks>
/// <para>
/// Each glyph becomes an ordinary draw node: the shared outline of its font and glyph (<see cref="SvgGlyphOutlines"/>)
/// under the transform that places the glyph, composed with the transform of the <c>text</c> element. Fill, stroke,
/// paint servers, clips, masks, and opacity then work as for shapes. A paint server sees the user space of the text: its
/// brush is moved into the glyph space with the inverse of the placement, and paint servers in bounding box units use
/// the box of the whole <c>text</c> element, also on a <c>tspan</c>.
/// </para>
/// <para>
/// The glyphs of one span render as one paint group, as Chromium paints one text fragment: first every fill, then every
/// stroke, or the other way round for <c>paint-order</c>. A <c>tspan</c> or <c>a</c> with <c>opacity</c>,
/// <c>clip-path</c>, <c>mask</c>, or a blend mode wraps its glyphs, those of its descendants included, as a group does.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The text state of the load, created on the first text element so that documents without text never touch the
    /// fonts and pay one field.
    /// </summary>
    private SvgTextResolveState? textState;

    /// <summary>
    /// Gets the element tree. The text layout stages read the text attributes and character data from it.
    /// </summary>
    internal SvgElementTree Tree => this.tree;

    /// <summary>
    /// Gets the text state of the load, creating it on first use.
    /// </summary>
    internal SvgTextResolveState TextState => this.textState ??= new SvgTextResolveState(this.loadOptions);

    /// <summary>
    /// Gets the largest number of characters of text that the document may lay out, <see cref="SvgLimits.MaxTextCharacters"/>.
    /// </summary>
    internal int MaxTextCharacters => this.loadOptions.Limits.MaxTextCharacters;

    /// <summary>
    /// Gets the number of characters of text that the load has laid out, counted per instance.
    /// </summary>
    private long TextCharacters => this.textState?.Characters ?? 0;

    /// <summary>
    /// Computes the style of an element from the computed style of its parent, as the resolve walk does.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="parent">The computed style of the parent.</param>
    /// <returns>The computed style.</returns>
    internal SvgComputedStyle ComputeChildStyle(int element, in SvgComputedStyle parent)
        => SvgComputedStyle.Compute(in this.tree.GetStyle(element), in parent, in this.initialStyle, this.units, this.tree[element].Kind);

    /// <summary>
    /// Returns the layout of a <c>text</c> element in a context, laying it out on first use. A layout depends on the
    /// computed style of the element, which content that <c>use</c> references inherits from the <c>use</c> element, so
    /// the cached layout of an element is reused when the style and the viewport are equal.
    /// </summary>
    /// <param name="element">The index of the <c>text</c> element.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="viewport">The viewport of the element.</param>
    /// <returns>The layout.</returns>
    private SvgTextLayout GetTextLayout(int element, in SvgComputedStyle style, SizeF viewport)
    {
        SvgTextResolveState state = this.TextState;
        if (state.Layouts.TryGetValue(element, out (SvgComputedStyle Style, SizeF Viewport, SvgTextLayout Layout) cached)
            && cached.Viewport == viewport
            && cached.Style.Equals(style))
        {
            return cached.Layout;
        }

        SvgTextLayout layout = SvgTextLayout.Create(this, element, in style, viewport, state.FontMatcher);
        state.Layouts[element] = (style, viewport, layout);
        return layout;
    }

    /// <summary>
    /// Counts the characters of a laid out instance of text against <see cref="SvgLimits.MaxTextCharacters"/>.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <exception cref="InvalidImageContentException">The document lays out more characters than the limit.</exception>
    private void CountTextCharacters(SvgTextLayout layout)
    {
        SvgTextResolveState state = this.TextState;
        state.Characters += layout.Content.Text.Length;
        int limit = this.loadOptions.Limits.MaxTextCharacters;
        if (state.Characters > limit)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the text character limit of {limit} after use references are expanded.");
        }
    }

    /// <summary>
    /// Resolves a <c>text</c> element into draw nodes.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    private void ResolveText(int index, in SvgResolveContext context)
    {
        SvgTextLayout layout = this.GetTextLayout(index, context.Style, context.Viewport);
        this.CountTextCharacters(layout);
        if (layout.Clusters.IsEmpty)
        {
            return;
        }

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        SvgTextResolveState state = this.TextState;
        SvgTextLayout? outer = state.ActiveLayout;
        state.ActiveLayout = layout;
        int cluster = 0;
        this.EmitTextSpan(layout, 0, ref cluster, in context);
        state.ActiveLayout = outer;
        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Emits the clusters of a span and of its descendant spans, in logical order.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="spanIndex">The span.</param>
    /// <param name="cluster">The index of the next cluster to emit; advanced past the clusters of the span.</param>
    /// <param name="context">The context of the span, whose style is the computed style of the span.</param>
    private void EmitTextSpan(SvgTextLayout layout, int spanIndex, ref int cluster, in SvgResolveContext context)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        SvgTextSpan span = spans[spanIndex];
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        int groupStart = cluster;
        while (cluster < clusters.Length && clusters[cluster].CharStart < span.CharEnd)
        {
            int owner = clusters[cluster].Span;
            if (owner == spanIndex)
            {
                cluster++;
                continue;
            }

            // The cluster belongs to a descendant: emit the paint group so far, then the child span that holds it.
            this.EmitGlyphGroup(layout, groupStart, cluster, span, in context);
            int child = owner;
            while (spans[child].Parent != spanIndex)
            {
                child = spans[child].Parent;
            }

            this.EmitChildSpan(layout, child, ref cluster, in context);
            groupStart = cluster;
        }

        this.EmitGlyphGroup(layout, groupStart, cluster, span, in context);
    }

    /// <summary>
    /// Emits a child span with its compositing and group opacity.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="spanIndex">The child span.</param>
    /// <param name="cluster">The index of the next cluster to emit.</param>
    /// <param name="parent">The context of the parent span.</param>
    private void EmitChildSpan(SvgTextLayout layout, int spanIndex, ref int cluster, in SvgResolveContext parent)
    {
        SvgTextSpan span = layout.Content.Spans[spanIndex];
        SvgComputedStyle style = span.Style;
        SvgResolveContext context = new(in style, parent.Transform, parent.Viewport, parent.Depth + 1);
        int start = this.nodes.Count;
        if (style.Opacity <= 0F || !this.TryBeginComposite(span.Element, in style, ref context, out SvgCompositeScope composite))
        {
            // The span renders nothing, but its clusters are consumed.
            SkipTextSpan(layout, span, ref cluster);
            this.nodes.RemoveRange(start, this.nodes.Count - start);
            return;
        }

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        this.EmitTextSpan(layout, spanIndex, ref cluster, in context);
        this.EndGroup(group, opacity);
        this.EndComposite(in composite);
    }

    /// <summary>
    /// Advances past the clusters of a span without emitting them.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="span">The span.</param>
    /// <param name="cluster">The index of the next cluster; advanced past the span.</param>
    private static void SkipTextSpan(SvgTextLayout layout, SvgTextSpan span, ref int cluster)
    {
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        while (cluster < clusters.Length && clusters[cluster].CharStart < span.CharEnd)
        {
            cluster++;
        }
    }

    /// <summary>
    /// Emits the glyphs of consecutive clusters of one span as one paint group: a draw node per glyph with the fill, then
    /// a draw node per glyph with the stroke, in the order of <c>paint-order</c>.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster.</param>
    /// <param name="end">The cluster after the group.</param>
    /// <param name="span">The span of the clusters.</param>
    /// <param name="context">The context of the span.</param>
    private void EmitGlyphGroup(SvgTextLayout layout, int start, int end, SvgTextSpan span, in SvgResolveContext context)
    {
        if (start >= end)
        {
            return;
        }

        SvgComputedStyle style = context.Style;
        if (style.Visibility != SvgVisibility.Visible)
        {
            return;
        }

        // Paint servers in bounding box units use the box of the whole text element.
        RectangleF bounds = layout.Bounds ?? default;
        Brush? fill = this.ResolvePaint(style.Fill, style.FillOpacity, bounds, in context);
        Pen? stroke = null;
        float strokeWidth = style.StrokeWidth.Unit == SvgLengthUnit.Percent
            ? style.StrokeWidth.Value * SvgUnits.Diagonal(context.Viewport) / 100F
            : style.StrokeWidth.Value;

        Brush? strokeBrush = null;
        if (strokeWidth > 0F)
        {
            strokeBrush = this.ResolvePaint(style.Stroke, style.StrokeOpacity, bounds, in context);
            if (strokeBrush is not null)
            {
                stroke = this.CreatePen(strokeBrush, strokeWidth, in style, context.Viewport);
            }
        }

        int firstNode = this.nodes.Count;
        if (fill is null && stroke is null)
        {
            this.EmitTextDecorations(layout, start, end, span, in context, firstNode);
            return;
        }

        SvgDrawFlags flags = GetDrawFlags(in style, false, stroke is not null) & ~SvgDrawFlags.StrokeFirst;
        bool strokeFirst = fill is not null
            && stroke is not null
            && style.PaintOrder is SvgPaintOrder.StrokeFillMarkers or SvgPaintOrder.StrokeMarkersFill or SvgPaintOrder.MarkersStrokeFill;

        if (strokeFirst)
        {
            this.EmitGlyphNodes(layout, start, end, null, stroke, strokeBrush!, strokeWidth, in context, flags);
            this.EmitGlyphNodes(layout, start, end, fill, null, null, 0F, in context, flags);
        }
        else
        {
            if (fill is not null)
            {
                this.EmitGlyphNodes(layout, start, end, fill, null, null, 0F, in context, flags);
            }

            if (stroke is not null)
            {
                this.EmitGlyphNodes(layout, start, end, null, stroke, strokeBrush!, strokeWidth, in context, flags);
            }
        }

        this.EmitTextDecorations(layout, start, end, span, in context, firstNode);
    }

    /// <summary>
    /// Emits one draw node per glyph of a range of clusters with one paint.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster.</param>
    /// <param name="end">The cluster after the range.</param>
    /// <param name="fill">The fill brush in the user space of the text, or <see langword="null"/>.</param>
    /// <param name="stroke">The pen, or <see langword="null"/>.</param>
    /// <param name="strokeBrush">The brush of the pen in the user space of the text, when <paramref name="stroke"/> is set.</param>
    /// <param name="strokeWidth">The stroke width in user units.</param>
    /// <param name="context">The context of the span.</param>
    /// <param name="flags">The draw flags.</param>
    private void EmitGlyphNodes(
        SvgTextLayout layout,
        int start,
        int end,
        Brush? fill,
        Pen? stroke,
        Brush? strokeBrush,
        float strokeWidth,
        in SvgResolveContext context,
        SvgDrawFlags flags)
    {
        SvgGlyphOutlines outlines = this.TextState.Outlines;
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        ReadOnlySpan<SvgShapedGlyph> glyphs = layout.Glyphs;
        bool solidFill = fill is null or SolidBrush;
        bool solidStroke = strokeBrush is null or SolidBrush;
        float radius = stroke?.GetOutlineRadius(1F) ?? 0F;

        for (int c = start; c < end; c++)
        {
            ref readonly SvgTextCluster cluster = ref clusters[c];
            if (cluster.Hidden || cluster.Skipped)
            {
                continue;
            }

            for (int g = cluster.GlyphStart; g < cluster.GlyphStart + cluster.GlyphCount; g++)
            {
                ref readonly SvgShapedGlyph glyph = ref glyphs[g];
                IPath? outline = outlines.Get(glyph.Font, glyph.GlyphId);
                if (outline is null)
                {
                    continue;
                }

                this.CountInstance();
                Matrix3x2 placement = layout.GetGlyphTransform(in cluster, g);
                Matrix3x2 transform = placement * context.Transform;

                // The brushes are in the user space of the text; the node draws in the space of the glyph outline.
                Brush? nodeFill = fill;
                Pen? nodeStroke = stroke;
                if ((!solidFill || !solidStroke) && Matrix3x2.Invert(placement, out Matrix3x2 inverse))
                {
                    Matrix4x4 toGlyph = new(inverse);
                    if (!solidFill)
                    {
                        nodeFill = fill!.Transform(toGlyph, default, default);
                    }

                    if (!solidStroke)
                    {
                        nodeStroke = this.CreatePen(strokeBrush!.Transform(toGlyph, default, default), strokeWidth, context.Style, context.Viewport);
                    }
                }

                RectangleF paintBounds = RectangleF.Inflate(outline.Bounds, radius, radius);
                this.nodes.Add(new SvgRenderNode
                {
                    Kind = SvgRenderNodeKind.Draw,
                    Path = outline,
                    Fill = nodeFill,
                    Stroke = nodeStroke,
                    FillRule = IntersectionRule.NonZero,
                    TransformIndex = this.GetTransformIndex(transform),
                    Bounds = (flags & SvgDrawFlags.NonScalingStroke) != 0 ? UnboundedBounds : TransformBounds(paintBounds, transform),
                    Flags = flags,
                });
            }
        }
    }

    /// <summary>
    /// Adds the <c>text-decoration</c> lines of a paint group as draw nodes (<c>SvgResolver.TextDecorations.cs</c>). It
    /// runs after the glyph nodes of the group; to paint underline and overline below the glyphs, as Chromium does, it
    /// inserts their nodes at <paramref name="firstNode"/>. The paint of a decoration comes from the span that sets
    /// <c>text-decoration</c> (an ancestor of <paramref name="span"/> or the span itself).
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster of the group.</param>
    /// <param name="end">The cluster after the group.</param>
    /// <param name="span">The span of the group.</param>
    /// <param name="context">The context of the span.</param>
    /// <param name="firstNode">The index of the first glyph node of the group in the node list.</param>
    partial void EmitTextDecorations(SvgTextLayout layout, int start, int end, SvgTextSpan span, in SvgResolveContext context, int firstNode);

    /// <summary>
    /// Adds the <c>text-decoration</c> lines of the visible clusters to the geometry of a <c>clipPath</c>, as Chromium
    /// paints them into the clip.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="paths">The clip geometry in the user space of the text.</param>
    static partial void AddTextDecorationClipPaths(SvgTextLayout layout, List<IPath> paths);

    /// <summary>
    /// Returns the object bounding box of a <c>text</c> element, or of a <c>tspan</c>, <c>a</c>, or <c>textPath</c> of
    /// the text whose nodes resolve.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the box in the user space of the text.</param>
    /// <returns><see langword="true"/> if the element has a box; otherwise, <see langword="false"/>.</returns>
    private bool TryGetTextBoundingBox(int element, in SvgResolveContext context, out RectangleF bounds)
    {
        bounds = default;
        if (this.tree[element].Kind == SvgElementKind.Text)
        {
            RectangleF? box = this.GetTextLayout(element, context.Style, context.Viewport).Bounds;
            bounds = box ?? default;
            return box.HasValue;
        }

        SvgTextLayout? layout = this.textState?.ActiveLayout;
        int span = layout?.Content.FindSpan(element) ?? -1;
        if (span < 0)
        {
            return false;
        }

        RectangleF? spanBox = layout!.GetSpanBounds(span);
        bounds = spanBox ?? default;
        return spanBox.HasValue;
    }

    /// <summary>
    /// Returns whether an element is a text content child of the text whose nodes resolve.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if a span of the active text layout is the element; otherwise, <see langword="false"/>.</returns>
    private bool IsActiveTextSpan(int element)
        => this.textState?.ActiveLayout?.Content.FindSpan(element) >= 0;

    /// <summary>
    /// Returns the geometry that a <c>text</c> element adds to a <c>clipPath</c>: the outlines of its visible glyphs in
    /// its user space, as one path. The glyph outlines are transformed copies here, because a clip takes one path.
    /// </summary>
    /// <param name="element">The index of the <c>text</c> element.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="viewport">The viewport of the element.</param>
    /// <returns>The path, or <see langword="null"/> when the text has no visible glyph.</returns>
    private IPath? GetTextClipPath(int element, in SvgComputedStyle style, SizeF viewport)
    {
        SvgTextLayout layout = this.GetTextLayout(element, in style, viewport);
        Dictionary<SvgTextLayout, IPath?> clipPaths = this.TextState.ClipPaths;
        if (clipPaths.TryGetValue(layout, out IPath? cached))
        {
            return cached;
        }

        this.CountTextCharacters(layout);
        SvgGlyphOutlines outlines = this.TextState.Outlines;
        List<IPath> paths = [];
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        ReadOnlySpan<SvgShapedGlyph> glyphs = layout.Glyphs;
        SvgTextSpan[] spans = layout.Content.Spans;
        foreach (ref readonly SvgTextCluster cluster in clusters)
        {
            // A hidden span adds nothing to the clip, as a hidden shape adds nothing.
            if (cluster.Hidden || cluster.Skipped || spans[cluster.Span].Style.Visibility != SvgVisibility.Visible)
            {
                continue;
            }

            for (int g = cluster.GlyphStart; g < cluster.GlyphStart + cluster.GlyphCount; g++)
            {
                IPath? outline = outlines.Get(glyphs[g].Font, glyphs[g].GlyphId);
                if (outline is not null)
                {
                    paths.Add(outline.Transform(new Matrix4x4(layout.GetGlyphTransform(in cluster, g))));
                }
            }
        }

        AddTextDecorationClipPaths(layout, paths);
        IPath? path = paths.Count == 0 ? null : new ComplexPolygon([.. paths]);
        clipPaths.Add(layout, path);
        return path;
    }
}
