// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>text-decoration</c> of SVG text.
/// </content>
/// <remarks>
/// <para>
/// Lines: an element draws the lines of its own <c>text-decoration-line</c> and those that its ancestors set
/// (<see cref="SvgTextStyle.AncestorDecorationLine"/>), so a decoration set on a <c>g</c> around the <c>text</c> applies
/// too. <c>blink</c> draws nothing, as in browsers.
/// </para>
/// <para>
/// Paint: each line takes the fill and stroke of the nearest span, from the span of the text up to the <c>text</c>
/// element, that sets the line; a line that only an element outside the text sets takes the paint of the <c>text</c>
/// element. SVG 1.1 (section 10.12) defines it so, and resvg and the resvg suite follow it. Chromium differs (measured):
/// it paints every line of a text fragment with the paint of the innermost span that sets any line, and a line set on
/// a <c>g</c> with the paint of the <c>g</c>.
/// </para>
/// <para>
/// Geometry: each line is a band along the advance of every cluster in its line frame
/// (<see cref="SvgTextLayout.GetLineFrame"/>), so it follows <c>x</c>, <c>y</c>, <c>dx</c>, <c>dy</c>, <c>rotate</c>,
/// spacing, <c>textLength</c>, anchoring, baseline shifts, vertical text, and placement on a path, and leaves out hidden
/// clusters and clusters that fall off their path. In vertical text the line runs down the column, left of the glyphs
/// for an underline, as in Chromium. Consecutive clusters that touch on one line and share their metrics form one
/// rectangle, in either direction, so right-to-left runs join too; any other cluster gets a band of its own under its
/// transform. Spacing that <c>textLength</c> adds between clusters is not part of their advances, so the bands of
/// <c>lengthAdjust="spacing"</c> have gaps, as in Chromium. Position and thickness come from the primary font of the
/// span of the clusters, so the glyphs of a fallback font share the line, as in Chromium and resvg: the underline is
/// centered on <see cref="FontMetrics.UnderlinePosition"/> with
/// <see cref="FontMetrics.UnderlineThickness"/>, the line-through on <see cref="FontMetrics.StrikeoutPosition"/> with
/// <see cref="FontMetrics.StrikeoutSize"/>, and the overline on the ascent with the underline thickness. These are the
/// rules of resvg. Chromium uses a thickness of a tenth of the font size, draws the line-through at a third of the
/// ascent, and puts the overline above the ascent.
/// </para>
/// <para>
/// Order, as Chromium paints a text fragment: underline, then overline, below the glyphs of the group, and line-through
/// above them; each line paints its fill and stroke in the <c>paint-order</c> of the span that sets it.
/// <c>text-decoration-color</c> is not used, as Chromium also paints SVG decorations with the fill and stroke. Every
/// line is solid: Chromium draws the <c>double</c>, <c>dotted</c>, <c>dashed</c>, and <c>wavy</c> styles of
/// <c>text-decoration-style</c>, and leaves gaps in underlines where descenders cross them
/// (<c>text-decoration-skip-ink</c>); both are not done here. Decorations do not count for the object bounding box, as in
/// Chromium, but they do add to the geometry of text in a <c>clipPath</c>, as Chromium paints them into the clip.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The lines that draw.
    /// </summary>
    private const SvgTextDecorationLine DrawnDecorationLines =
        SvgTextDecorationLine.Underline | SvgTextDecorationLine.Overline | SvgTextDecorationLine.LineThrough;

    /// <summary>
    /// The largest gap, in user units, between the end of a cluster and the start of the next for their bands to join.
    /// It absorbs the rounding of pen positions that anchoring shifts.
    /// </summary>
    private const float DecorationJoinTolerance = 1E-3F;

    /// <summary>
    /// Adds the decoration lines of a paint group as draw nodes: underline and overline before the glyph nodes of the
    /// group, line-through after them.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster of the group.</param>
    /// <param name="end">The cluster after the group.</param>
    /// <param name="span">The span of the group.</param>
    /// <param name="context">The context of the span.</param>
    /// <param name="firstNode">The index of the first glyph node of the group in the node list.</param>
    partial void EmitTextDecorations(SvgTextLayout layout, int start, int end, SvgTextSpan span, in SvgResolveContext context, int firstNode)
    {
        SvgTextStyle text = span.Style.Text;
        SvgTextDecorationLine lines = (text.TextDecorationLine | text.AncestorDecorationLine) & DrawnDecorationLines;
        if (lines == SvgTextDecorationLine.None)
        {
            return;
        }

        int spanIndex = layout.Clusters[start].Span;
        int insert = firstNode;
        if ((lines & SvgTextDecorationLine.Underline) != 0)
        {
            insert = this.EmitTextDecoration(layout, start, end, spanIndex, SvgTextDecorationLine.Underline, in context, insert);
        }

        if ((lines & SvgTextDecorationLine.Overline) != 0)
        {
            _ = this.EmitTextDecoration(layout, start, end, spanIndex, SvgTextDecorationLine.Overline, in context, insert);
        }

        if ((lines & SvgTextDecorationLine.LineThrough) != 0)
        {
            _ = this.EmitTextDecoration(layout, start, end, spanIndex, SvgTextDecorationLine.LineThrough, in context, this.nodes.Count);
        }
    }

    /// <summary>
    /// Adds the decoration bands of the visible clusters of a text to the geometry of a <c>clipPath</c>.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="paths">The clip geometry in the user space of the text; the bands are added to it.</param>
    static partial void AddTextDecorationClipPaths(SvgTextLayout layout, List<IPath> paths)
    {
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        SvgTextSpan[] spans = layout.Content.Spans;
        int start = 0;
        while (start < clusters.Length)
        {
            // Bands form per run of clusters of one span, as they draw per paint group.
            int spanIndex = clusters[start].Span;
            int end = start + 1;
            while (end < clusters.Length && clusters[end].Span == spanIndex)
            {
                end++;
            }

            SvgTextStyle text = spans[spanIndex].Style.Text;
            SvgTextDecorationLine lines = (text.TextDecorationLine | text.AncestorDecorationLine) & DrawnDecorationLines;
            AddClipBand(layout, start, end, lines, SvgTextDecorationLine.Underline, paths);
            AddClipBand(layout, start, end, lines, SvgTextDecorationLine.Overline, paths);
            AddClipBand(layout, start, end, lines, SvgTextDecorationLine.LineThrough, paths);

            start = end;
        }

        static void AddClipBand(SvgTextLayout layout, int start, int end, SvgTextDecorationLine lines, SvgTextDecorationLine line, List<IPath> paths)
        {
            if ((lines & line) != 0 && BuildDecorationPath(layout, start, end, line) is IPath path)
            {
                paths.Add(path);
            }
        }
    }

    /// <summary>
    /// Adds the draw node of one decoration line of a paint group.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster of the group.</param>
    /// <param name="end">The cluster after the group.</param>
    /// <param name="spanIndex">The span of the group.</param>
    /// <param name="line">The line.</param>
    /// <param name="context">The context of the span of the group.</param>
    /// <param name="insert">The index in the node list to insert the node at.</param>
    /// <returns>The index after the inserted node, or <paramref name="insert"/> when nothing draws.</returns>
    private int EmitTextDecoration(
        SvgTextLayout layout,
        int start,
        int end,
        int spanIndex,
        SvgTextDecorationLine line,
        in SvgResolveContext context,
        int insert)
    {
        IPath? path = BuildDecorationPath(layout, start, end, line);
        if (path is null)
        {
            return insert;
        }

        // The span that sets the line gives the paint, in the user space of the text; the transform is that of the group.
        SvgComputedStyle style = layout.Content.Spans[FindDecorationSpan(layout.Content.Spans, spanIndex, line)].Style;
        SvgResolveContext paintContext = new(in style, context.Transform, context.Viewport, context.Depth);
        RectangleF bounds = layout.Bounds ?? default;
        Brush? fill = this.ResolvePaint(style.Fill, style.FillOpacity, bounds, in paintContext);
        Pen? stroke = null;
        float strokeWidth = SvgUnits.ToUser(style.StrokeWidth, SvgUnits.Diagonal(context.Viewport), style.FontSize, this.units, 0F);

        if (strokeWidth > 0F && this.ResolvePaint(style.Stroke, style.StrokeOpacity, bounds, in paintContext) is Brush strokeBrush)
        {
            stroke = this.CreatePen(strokeBrush, strokeWidth, in style, context.Viewport);
        }

        if (fill is null && stroke is null)
        {
            return insert;
        }

        // Prime the cached closed form of the path, which every fill uses, while the resolve stage is single threaded.
        if (fill is not null)
        {
            _ = path.AsClosedPath();
        }

        this.CountInstance();
        RectangleF paintBounds = stroke is null ? path.Bounds : RectangleF.Inflate(path.Bounds, stroke.GetOutlineRadius(1F), stroke.GetOutlineRadius(1F));
        SvgDrawFlags flags = GetDrawFlags(in style, fill is not null, stroke is not null);
        this.nodes.Insert(insert, new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.Draw,
            Path = path,
            Fill = fill,
            Stroke = stroke,
            FillRule = IntersectionRule.NonZero,
            TransformIndex = this.GetTransformIndex(context.Transform),
            Bounds = (flags & SvgDrawFlags.NonScalingStroke) != 0 ? UnboundedBounds : TransformBounds(paintBounds, context.Transform),
            Flags = flags,
        });

        return insert + 1;
    }

    /// <summary>
    /// Returns the span whose paint a decoration line of a span uses: the nearest span, the span itself included, that
    /// sets the line, else the <c>text</c> element, for a line that an element outside the text sets.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <param name="spanIndex">The span of the text.</param>
    /// <param name="line">The line.</param>
    /// <returns>The span index.</returns>
    private static int FindDecorationSpan(SvgTextSpan[] spans, int spanIndex, SvgTextDecorationLine line)
    {
        for (int i = spanIndex; i > 0; i = spans[i].Parent)
        {
            if ((spans[i].Style.Text.TextDecorationLine & line) != 0)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Builds the bands of one decoration line along a range of clusters, in the user space of the text.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="start">The first cluster.</param>
    /// <param name="end">The cluster after the range.</param>
    /// <param name="line">The line.</param>
    /// <returns>The bands, or <see langword="null"/> when no cluster of the range draws.</returns>
    private static IPath? BuildDecorationPath(SvgTextLayout layout, int start, int end, SvgTextDecorationLine line)
    {
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        IPath? first = null;
        List<IPath>? more = null;

        // The open run of joined clusters, in the coordinates of its line axes: the direction along the line and the
        // direction across it toward the bottom of the glyphs (the axes of the line frame of the clusters, which are the
        // x and y axes for unrotated horizontal text). Positions are measured along those axes from the origin of the user
        // space: the left and right ends of the band along the line, and its center across it.
        bool open = false;
        Vector2 along = Vector2.UnitX;
        Vector2 across = Vector2.UnitY;
        float left = 0F;
        float right = 0F;
        float center = 0F;
        float thickness = 0F;

        // The range holds the clusters of one span. Its lines take their position and thickness from the primary font of
        // the span, as Chromium and resvg do, so glyphs of a fallback font stay on the same line.
        SvgComputedStyle spanStyle = layout.Content.Spans[clusters[start].Span].Style;
        SvgFontFace? face = layout.FontMatcher.GetChain(spanStyle.Text).Primary;
        Font? primary = face is not null && spanStyle.FontSize > 0F
            ? layout.FontMatcher.GetFont(face, spanStyle.FontSize, spanStyle.Text.FontWeight, spanStyle.Text.FontStyle)
            : null;

        float offset = 0F;
        float size = 0F;
        if (primary is not null)
        {
            GetDecorationMetrics(primary, line, out offset, out size);
        }

        for (int c = start; c < end; c++)
        {
            ref readonly SvgTextCluster cluster = ref clusters[c];
            if (cluster.Hidden || cluster.Skipped || cluster.Font is null)
            {
                Close(ref open, along, across, left, right, center, thickness, ref first, ref more);
                continue;
            }

            float width = layout.GetInlineExtent(in cluster);
            if (!(size > 0F) || width == 0F)
            {
                Close(ref open, along, across, left, right, center, thickness, ref first, ref more);
                continue;
            }

            // The line frame turns with the rotation of the cluster, with vertical text, and with the direction of a path.
            Matrix3x2 frame = layout.GetLineFrame(in cluster);
            Vector2 clusterAlong = new(frame.M11, frame.M12);
            Vector2 clusterAcross = new(frame.M21, frame.M22);
            float u = Vector2.Dot(frame.Translation, clusterAlong);
            float low = MathF.Min(u, u + width);
            float high = MathF.Max(u, u + width);
            float v = Vector2.Dot(frame.Translation, clusterAcross) + offset;

            // A cluster joins the open run when it lies on the same line and touches one of its ends: the right end in
            // left-to-right order, the left end where right-to-left clusters, which stay in logical order, come in visual
            // order from right to left. Bidirectional reordering, text on a path, rotations, and spacing that textLength
            // adds outside the advances start a new band.
            if (open
                && clusterAlong == along
                && clusterAcross == across
                && MathF.Abs(v - center) <= DecorationJoinTolerance
                && size == thickness)
            {
                if (MathF.Abs(low - right) <= DecorationJoinTolerance)
                {
                    right = high;
                    continue;
                }

                if (MathF.Abs(high - left) <= DecorationJoinTolerance)
                {
                    left = low;
                    continue;
                }
            }

            Close(ref open, along, across, left, right, center, thickness, ref first, ref more);
            open = true;
            along = clusterAlong;
            across = clusterAcross;
            left = low;
            right = high;
            center = v;
            thickness = size;
        }

        Close(ref open, along, across, left, right, center, thickness, ref first, ref more);
        return more is null ? first : new ComplexPolygon([.. more]);

        static void Close(
            ref bool open,
            Vector2 along,
            Vector2 across,
            float left,
            float right,
            float center,
            float thickness,
            ref IPath? first,
            ref List<IPath>? more)
        {
            if (!open)
            {
                return;
            }

            open = false;
            IPath band = CreateBand(left, right, center, thickness);
            if (along != Vector2.UnitX || across != Vector2.UnitY)
            {
                // The band is in the coordinates of the line axes; the axes map it back to the user space of the text.
                band = band.Transform(new Matrix4x4(new Matrix3x2(along.X, along.Y, across.X, across.Y, 0F, 0F)));
            }

            Add(band, ref first, ref more);
        }

        static void Add(IPath path, ref IPath? first, ref List<IPath>? more)
        {
            if (first is null)
            {
                first = path;
                return;
            }

            more ??= [first];
            more.Add(path);
        }
    }

    /// <summary>
    /// Creates the rectangle of a band between two positions along the baseline.
    /// </summary>
    /// <param name="x0">One end.</param>
    /// <param name="x1">The other end; it is left of <paramref name="x0"/> for a negative advance.</param>
    /// <param name="center">The center of the band across the baseline.</param>
    /// <param name="thickness">The thickness of the band.</param>
    /// <returns>The rectangle.</returns>
    private static RectanglePolygon CreateBand(float x0, float x1, float center, float thickness)
        => new(MathF.Min(x0, x1), center - (thickness / 2F), MathF.Abs(x1 - x0), thickness);

    /// <summary>
    /// Returns the position and thickness of a decoration line in a font.
    /// </summary>
    /// <remarks>
    /// A font whose table gives no thickness uses a twentieth of the em, the value of most fonts, an underline a tenth of
    /// the em below the baseline, and a line-through at half the x-height.
    /// </remarks>
    /// <param name="font">The font of the cluster.</param>
    /// <param name="line">The line.</param>
    /// <param name="offset">When this method returns, contains the center of the band below the baseline, in user units.</param>
    /// <param name="thickness">When this method returns, contains the thickness in user units.</param>
    private static void GetDecorationMetrics(Font font, SvgTextDecorationLine line, out float offset, out float thickness)
    {
        FontMetrics metrics = font.FontMetrics;
        float unitsPerEm = metrics.UnitsPerEm;
        float scale = font.Size / unitsPerEm;
        float underlineThickness = metrics.UnderlineThickness > 0 ? metrics.UnderlineThickness : unitsPerEm / 20F;
        switch (line)
        {
            case SvgTextDecorationLine.Underline:
                offset = (metrics.UnderlineThickness > 0 ? -metrics.UnderlinePosition : unitsPerEm / 10F) * scale;
                thickness = underlineThickness * scale;
                break;
            case SvgTextDecorationLine.Overline:
                offset = -metrics.HorizontalMetrics.Ascender * scale;
                thickness = underlineThickness * scale;
                break;
            default:
                if (metrics.StrikeoutSize > 0)
                {
                    offset = -metrics.StrikeoutPosition * scale;
                    thickness = metrics.StrikeoutSize * scale;
                }
                else
                {
                    float xHeight = metrics.XHeight > 0 ? metrics.XHeight : metrics.HorizontalMetrics.Ascender * 0.56F;
                    offset = -xHeight / 2F * scale;
                    thickness = underlineThickness * scale;
                }

                break;
        }
    }
}
