// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Markers: <c>marker-start</c>, <c>marker-mid</c>, and <c>marker-end</c> on <c>path</c>, <c>line</c>,
/// <c>polyline</c>, and <c>polygon</c>.
/// </content>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The segments of the shape whose markers are being placed, reused between shapes. Created on first use, so documents without the feature allocate nothing for it.
    /// </summary>
    private List<MarkerSegment>? markerSegments;

    /// <summary>
    /// The marker vertices of the shape whose markers are being placed, reused between shapes. Created on first use, so documents without the feature allocate nothing for it.
    /// </summary>
    private List<MarkerVertex>? markerVertices;

    /// <summary>
    /// The computed style of each <c>marker</c> element, which its content inherits from. Created on first use, so documents without the feature allocate nothing for it.
    /// </summary>
    private Dictionary<int, SvgComputedStyle>? markerStyles;

    /// <summary>
    /// Resolves a shape that has markers: the shape and its markers form one group, painted in the order of
    /// <c>paint-order</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape paints through <see cref="ResolveShape(int, SvgElementKind, in SvgResolveContext)"/> with the marker
    /// properties cleared and an opacity of 1. The opacity of the shape applies to the group of the shape and its
    /// markers, because markers are part of the rendering of the element. When <c>paint-order</c> places the markers
    /// between the fill and the stroke, or the stroke first, the draw node is split into a fill-only and a stroke-only
    /// node in that order. That keeps the order independent of how the draw node itself orders its paints.
    /// </para>
    /// <para>
    /// A hidden shape (<c>visibility</c> other than <c>visible</c>) paints no markers, as in Chromium.
    /// </para>
    /// </remarks>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the element.</param>
    /// <returns><see langword="true"/> if the shape had markers and was resolved here; otherwise, <see langword="false"/>.</returns>
    private bool TryResolveMarkedShape(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        SvgComputedStyle style = context.Style;
        if (kind is not (SvgElementKind.Path or SvgElementKind.Line or SvgElementKind.Polyline or SvgElementKind.Polygon)
            || (style.MarkerStart.IsNone && style.MarkerMid.IsNone && style.MarkerEnd.IsNone)
            || style.Visibility != SvgVisibility.Visible)
        {
            return false;
        }

        int group = this.BeginGroup(style.Opacity);

        SvgComputedStyle shapeStyle = style;
        shapeStyle.Opacity = 1F;
        shapeStyle.MarkerStart = default;
        shapeStyle.MarkerMid = default;
        shapeStyle.MarkerEnd = default;
        SvgResolveContext shapeContext = new(in shapeStyle, context.Transform, context.Viewport, context.Depth);

        int first = this.nodes.Count;
        this.ResolveShape(index, kind, in shapeContext);

        // With an opacity of 1 the shape adds at most one draw node. Should it add anything else, such as scope nodes
        // around the draw node, the nodes stay as they are and the markers follow them.
        int added = this.nodes.Count - first;
        if (added > 1 || (added == 1 && this.nodes[first].Kind != SvgRenderNodeKind.Draw))
        {
            this.AddMarkers(index, kind, in context);
            this.EndGroup(group, style.Opacity);
            return true;
        }

        SvgRenderNode? draw = added == 1 ? this.nodes[first] : null;
        this.nodes.RemoveRange(first, added);

        SvgRenderNode fillNode = default;
        SvgRenderNode strokeNode = default;
        bool hasFill = false;
        bool hasStroke = false;
        if (draw is SvgRenderNode node)
        {
            hasFill = node.Fill is not null;
            hasStroke = node.Stroke is not null;
            fillNode = node;
            fillNode.Stroke = null;
            strokeNode = node;
            strokeNode.Fill = null;
        }

        switch (style.PaintOrder)
        {
            case SvgPaintOrder.FillMarkersStroke:
                this.AddDraw(in fillNode, hasFill);
                this.AddMarkers(index, kind, in context);
                this.AddDraw(in strokeNode, hasStroke);
                break;
            case SvgPaintOrder.StrokeFillMarkers:
                this.AddDraw(in strokeNode, hasStroke);
                this.AddDraw(in fillNode, hasFill);
                this.AddMarkers(index, kind, in context);
                break;
            case SvgPaintOrder.StrokeMarkersFill:
                this.AddDraw(in strokeNode, hasStroke);
                this.AddMarkers(index, kind, in context);
                this.AddDraw(in fillNode, hasFill);
                break;
            case SvgPaintOrder.MarkersFillStroke:
                this.AddMarkers(index, kind, in context);
                this.AddDraw(in fillNode, hasFill);
                this.AddDraw(in strokeNode, hasStroke);
                break;
            case SvgPaintOrder.MarkersStrokeFill:
                this.AddMarkers(index, kind, in context);
                this.AddDraw(in strokeNode, hasStroke);
                this.AddDraw(in fillNode, hasFill);
                break;
            default:
                this.AddDraw(in fillNode, hasFill);
                this.AddDraw(in strokeNode, hasStroke);
                this.AddMarkers(index, kind, in context);
                break;
        }

        this.EndGroup(group, style.Opacity);
        return true;
    }

    /// <summary>
    /// Adds a draw node that paints one of the paints of a shape.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="add">Whether the shape has the paint.</param>
    private void AddDraw(in SvgRenderNode node, bool add)
    {
        if (add)
        {
            this.nodes.Add(node);
        }
    }

    /// <summary>
    /// Adds the markers of a shape at its vertices: <c>marker-start</c> at the first vertex, <c>marker-end</c> at the last
    /// vertex, and <c>marker-mid</c> at every other vertex, in path order.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the shape.</param>
    private void AddMarkers(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        List<MarkerVertex> vertices = this.markerVertices ??= [];
        vertices.Clear();
        this.BuildMarkerVertices(index, kind, in context);
        if (vertices.Count == 0)
        {
            return;
        }

        // The vertex list is reused by markers whose content holds marked shapes, so place from a copy.
        MarkerVertex[] placed = [.. vertices];
        SvgComputedStyle style = context.Style;

        // The shape is the context element of its markers, for context-fill and context-stroke.
        IPath? shape = this.GetShape(index, kind, in context);
        this.EnterContextElement(in context, shape?.Bounds ?? default, -1);
        float strokeWidth = style.StrokeWidth.Unit == SvgLengthUnit.Percent
            ? style.StrokeWidth.Value * SvgUnits.Diagonal(context.Viewport) / 100F
            : style.StrokeWidth.Value;

        for (int i = 0; i < placed.Length; i++)
        {
            SvgReference reference = i == 0 ? style.MarkerStart : i == placed.Length - 1 ? style.MarkerEnd : style.MarkerMid;
            if (reference.Id is string id
                && this.tree.TryGetElementById(id, out int marker)
                && this.tree[marker].Kind == SvgElementKind.Marker)
            {
                this.AddMarker(marker, placed[i], i == 0, strokeWidth, in context);
            }
        }

        this.ExitContextElement();
    }

    /// <summary>
    /// Adds one marker instance.
    /// </summary>
    /// <remarks>
    /// The marker content maps to the user space of the shape through, in order: the <c>viewBox</c> transform of the
    /// marker into its viewport of <c>markerWidth</c> x <c>markerHeight</c>; the translation that moves the reference point
    /// (<c>refX</c>, <c>refY</c> in content coordinates) to the origin; the scale by the stroke width for
    /// <c>markerUnits="strokeWidth"</c>; the rotation of <c>orient</c>; and the translation to the vertex. The viewport
    /// rectangle clips the content unless <c>overflow</c> is <c>visible</c> or <c>auto</c>. A zero or negative marker
    /// size disables the marker. A marker whose content uses the marker again is a cycle; the inner use is ignored.
    /// </remarks>
    /// <param name="marker">The index of the <c>marker</c> element.</param>
    /// <param name="vertex">The vertex.</param>
    /// <param name="isStart">Whether the vertex is the first vertex of the shape.</param>
    /// <param name="strokeWidth">The stroke width of the shape in user units.</param>
    /// <param name="context">The context of the shape.</param>
    private void AddMarker(int marker, MarkerVertex vertex, bool isStart, float strokeWidth, in SvgResolveContext context)
    {
        if (this.IsReferenceActive(marker))
        {
            return;
        }

        ref readonly SvgMarkerAttributes attributes = ref this.tree.GetMarker(marker);
        SizeF viewport = context.Viewport;
        float fontSize = context.Style.FontSize;
        float width = SvgUnits.ToUser(attributes.MarkerWidth, viewport.Width, fontSize, this.units, 3F);
        float height = SvgUnits.ToUser(attributes.MarkerHeight, viewport.Height, fontSize, this.units, 3F);
        if (!(width > 0F) || !(height > 0F) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            return;
        }

        RectangleF viewportRectangle = new(0F, 0F, width, height);
        Matrix3x2 viewBoxTransform = Matrix3x2.Identity;
        SizeF contentViewport = viewportRectangle.Size;
        if (attributes.HasViewBox)
        {
            RectangleF viewBox = attributes.ViewBox;
            if (!(viewBox.Width > 0F) || !(viewBox.Height > 0F))
            {
                return;
            }

            viewBoxTransform = SvgViewBoxTransform.Create(viewBox, attributes.PreserveAspectRatio, viewportRectangle);
            contentViewport = viewBox.Size;
        }

        // The reference point is in content coordinates; its position in the viewport moves to the vertex.
        Vector2 reference = Vector2.Transform(
            new Vector2(
                SvgUnits.ToUser(attributes.RefX, viewport.Width, fontSize, this.units, 0F),
                SvgUnits.ToUser(attributes.RefY, viewport.Height, fontSize, this.units, 0F)),
            viewBoxTransform);

        float scale = attributes.Units == SvgMarkerUnits.UserSpaceOnUse ? 1F : strokeWidth;
        if (!(scale > 0F) || !float.IsFinite(scale))
        {
            return;
        }

        float angle = attributes.Orient.Kind switch
        {
            SvgMarkerOrientKind.Angle => attributes.Orient.Angle,
            SvgMarkerOrientKind.Auto => vertex.Angle,
            SvgMarkerOrientKind.AutoStartReverse => isStart ? vertex.Angle + 180F : vertex.Angle,
            _ => 0F
        };

        // Row vectors: the transform applied first comes first.
        Matrix3x2 viewportTransform = Matrix3x2.CreateTranslation(-reference)
            * Matrix3x2.CreateScale(scale)
            * Matrix3x2.CreateRotation(angle * (MathF.PI / 180F))
            * Matrix3x2.CreateTranslation(vertex.Position)
            * context.Transform;

        Vector2 translation = viewportTransform.Translation;
        if (!float.IsFinite(viewportTransform.GetDeterminant()) || !float.IsFinite(translation.X + translation.Y))
        {
            return;
        }

        this.markerStyles ??= [];
        if (!this.markerStyles.TryGetValue(marker, out SvgComputedStyle markerStyle))
        {
            markerStyle = this.ComputeDocumentStyle(marker);
            this.markerStyles.Add(marker, markerStyle);
        }

        bool clips = markerStyle.Overflow is SvgOverflow.Hidden or SvgOverflow.Scroll;
        int clip = this.nodes.Count;
        if (clips)
        {
            this.nodes.Add(new SvgRenderNode
            {
                Kind = SvgRenderNodeKind.BeginClip,
                TransformIndex = this.GetTransformIndex(viewportTransform),
                Clip = [new RectanglePolygon(viewportRectangle)],
                Bounds = TransformBounds(viewportRectangle, viewportTransform),
            });
        }

        SvgResolveContext contentContext = new(in markerStyle, viewBoxTransform * viewportTransform, contentViewport, context.Depth + 1);
        _ = this.ResolveContent(marker, in contentContext);

        if (clips)
        {
            if (this.nodes.Count == clip + 1)
            {
                // Nothing rendered inside the marker, so the clip is not needed.
                this.nodes.RemoveAt(clip);
            }
            else
            {
                this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.EndClip });
            }
        }
    }

    /// <summary>
    /// Builds the marker vertices of a shape, with the direction at each vertex, into <see cref="markerVertices"/>.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="context">The context of the shape.</param>
    private void BuildMarkerVertices(int index, SvgElementKind kind, in SvgResolveContext context)
    {
        List<MarkerSegment> segments = this.markerSegments ??= [];
        if (kind is SvgElementKind.Polyline or SvgElementKind.Polygon)
        {
            ReadOnlySpan<PointF> points = this.tree.GetPoints(index);
            if (points.Length < 2)
            {
                return;
            }

            segments.Clear();
            for (int i = 1; i < points.Length; i++)
            {
                segments.Add(MarkerSegment.Line(points[i - 1], points[i]));
            }

            this.AddSubpathVertices(kind == SvgElementKind.Polygon);
            return;
        }

        IPath? path = this.GetShape(index, kind, in context);
        if (path is null)
        {
            return;
        }

        if (path is ComplexPolygon complex)
        {
            foreach (IPath subpath in complex.Paths)
            {
                this.AddPathVertices(subpath);
            }
        }
        else
        {
            this.AddPathVertices(path);
        }
    }

    /// <summary>
    /// Adds the vertices of one subpath of a path built by the path data parser.
    /// </summary>
    /// <param name="subpath">The subpath: a <see cref="Path"/>, or a <see cref="Polygon"/> when it was closed.</param>
    private void AddPathVertices(IPath subpath)
    {
        if (subpath is not Path path)
        {
            return;
        }

        List<MarkerSegment> segments = this.markerSegments ??= [];
        segments.Clear();
        IReadOnlyList<ILineSegment> lineSegments = path.LineSegments;
        for (int i = 0; i < lineSegments.Count; i++)
        {
            switch (lineSegments[i])
            {
                case CubicBezierLineSegment cubic:
                {
                    // The parser builds one cubic per curve command; other builders can chain curves in one segment.
                    IReadOnlyList<PointF> points = cubic.ControlPoints;
                    for (int j = 0; j + 3 < points.Count; j += 3)
                    {
                        segments.Add(MarkerSegment.Cubic(points[j], points[j + 1], points[j + 2], points[j + 3]));
                    }

                    break;
                }

                case LinearLineSegment linear:
                {
                    // A linear segment can hold a polyline; each point after the first ends a segment.
                    int count = linear.LinearVertexCount(Vector2.One);
                    Span<PointF> points = count <= 64 ? stackalloc PointF[count] : new PointF[count];
                    linear.CopyTo(points, false, Vector2.One);
                    for (int j = 1; j < count; j++)
                    {
                        segments.Add(MarkerSegment.Line(points[j - 1], points[j]));
                    }

                    break;
                }

                default:
                {
                    // An arc keeps only its flattened points. The directions at its ends come from the first and the last
                    // chord, which deviate from the tangent by half the angle of a chord.
                    ILineSegment segment = lineSegments[i];
                    int count = segment.LinearVertexCount(Vector2.One);
                    PointF[] points = new PointF[count];
                    segment.CopyTo(points, false, Vector2.One);
                    segments.Add(MarkerSegment.Polyline(points));
                    break;
                }
            }
        }

        this.AddSubpathVertices(path.IsClosed);
    }

    /// <summary>
    /// Adds the vertices of the subpath in <see cref="markerSegments"/> to <see cref="markerVertices"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules follow SVG 2 "path directionality" and "vertex directions" as Chromium implements them:
    /// </para>
    /// <list type="bullet">
    /// <item><description>A closed subpath gets its closing segment, a line back to the start, which is zero length when
    /// the last segment already ends there.</description></item>
    /// <item><description>A zero-length segment takes its direction from the nearest segment before it in the subpath
    /// that has a direction, or else from the nearest one after it.</description></item>
    /// <item><description>Between two segments, the direction is the bisector of the incoming and the outgoing direction.
    /// At the start of an open subpath it is the outgoing direction, and at its end the incoming direction. At the start
    /// and the end of a closed subpath it is the bisector of the direction at the end of the closing segment and the
    /// direction at the start of the first segment.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="closed">Whether the subpath is closed.</param>
    private void AddSubpathVertices(bool closed)
    {
        List<MarkerSegment> segments = this.markerSegments ??= [];
        if (segments.Count == 0)
        {
            return;
        }

        if (closed)
        {
            segments.Add(MarkerSegment.Line(segments[^1].End, segments[0].Start));
        }

        // Zero-length segments inherit the direction of a neighbor: first look back, then forward.
        Span<MarkerSegment> span = CollectionsMarshal.AsSpan(segments);
        Vector2 previous = Vector2.Zero;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].IsDegenerate)
            {
                span[i].StartDirection = previous;
                span[i].EndDirection = previous;
            }
            else
            {
                previous = span[i].EndDirection;
            }
        }

        Vector2 next = Vector2.Zero;
        for (int i = span.Length - 1; i >= 0; i--)
        {
            if (span[i].StartDirection == Vector2.Zero)
            {
                span[i].StartDirection = next;
                span[i].EndDirection = next;
            }
            else
            {
                next = span[i].StartDirection;
            }
        }

        List<MarkerVertex> vertices = this.markerVertices!;
        float startAngle = closed
            ? Bisect(span[^1].EndDirection, span[0].StartDirection)
            : Angle(span[0].StartDirection);

        vertices.Add(new MarkerVertex(span[0].Start, startAngle));
        for (int i = 0; i < span.Length; i++)
        {
            float angle;
            if (i < span.Length - 1)
            {
                angle = Bisect(span[i].EndDirection, span[i + 1].StartDirection);
            }
            else
            {
                angle = closed ? startAngle : Angle(span[i].EndDirection);
            }

            vertices.Add(new MarkerVertex(span[i].End, angle));
        }
    }

    /// <summary>
    /// Returns the angle of a direction in degrees, clockwise from the positive x-axis in the y-down user space. A zero
    /// direction has the angle 0.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The angle.</returns>
    private static float Angle(Vector2 direction)
        => direction == Vector2.Zero ? 0F : MathF.Atan2(direction.Y, direction.X) * (180F / MathF.PI);

    /// <summary>
    /// Returns the bisector of an incoming and an outgoing direction, in degrees, as Chromium computes it: the mean of the
    /// two angles after the incoming angle moves by a full turn when the two are more than half a turn apart.
    /// </summary>
    /// <param name="incoming">The incoming direction.</param>
    /// <param name="outgoing">The outgoing direction.</param>
    /// <returns>The angle.</returns>
    private static float Bisect(Vector2 incoming, Vector2 outgoing)
    {
        float a = Angle(incoming);
        float b = Angle(outgoing);
        if (MathF.Abs(a - b) > 180F)
        {
            a += 360F;
        }

        return (a + b) / 2F;
    }

    /// <summary>
    /// One segment of a marked shape, with its end points and the directions at its ends.
    /// </summary>
    private struct MarkerSegment
    {
        /// <summary>
        /// Gets or sets the start point.
        /// </summary>
        public PointF Start { readonly get; set; }

        /// <summary>
        /// Gets or sets the end point.
        /// </summary>
        public PointF End { readonly get; set; }

        /// <summary>
        /// Gets or sets the direction at the start: the tangent, or zero for a zero-length segment.
        /// </summary>
        public Vector2 StartDirection { readonly get; set; }

        /// <summary>
        /// Gets or sets the direction at the end.
        /// </summary>
        public Vector2 EndDirection { readonly get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the segment has no direction of its own because all its points coincide.
        /// </summary>
        public bool IsDegenerate { readonly get; set; }

        /// <summary>
        /// Creates a line segment.
        /// </summary>
        /// <param name="start">The start point.</param>
        /// <param name="end">The end point.</param>
        /// <returns>The segment.</returns>
        public static MarkerSegment Line(PointF start, PointF end)
        {
            Vector2 direction = (Vector2)end - (Vector2)start;
            return new MarkerSegment
            {
                Start = start,
                End = end,
                StartDirection = direction,
                EndDirection = direction,
                IsDegenerate = direction == Vector2.Zero,
            };
        }

        /// <summary>
        /// Creates a cubic Bézier segment. The direction at the start points to the first control point that differs
        /// from the start point, and the direction at the end comes from the last one that differs from the end point.
        /// </summary>
        /// <param name="p0">The start point.</param>
        /// <param name="p1">The first control point.</param>
        /// <param name="p2">The second control point.</param>
        /// <param name="p3">The end point.</param>
        /// <returns>The segment.</returns>
        public static MarkerSegment Cubic(PointF p0, PointF p1, PointF p2, PointF p3)
        {
            Vector2 a = p0;
            Vector2 b = p1;
            Vector2 c = p2;
            Vector2 d = p3;
            Vector2 start = b != a ? b - a : c != a ? c - a : d - a;
            Vector2 end = d != c ? d - c : d != b ? d - b : d - a;
            return new MarkerSegment
            {
                Start = p0,
                End = p3,
                StartDirection = start,
                EndDirection = end,
                IsDegenerate = start == Vector2.Zero,
            };
        }

        /// <summary>
        /// Creates a segment from flattened points.
        /// </summary>
        /// <param name="points">The points; at least one.</param>
        /// <returns>The segment.</returns>
        public static MarkerSegment Polyline(PointF[] points)
        {
            Vector2 first = points[0];
            Vector2 last = points[^1];
            Vector2 start = Vector2.Zero;
            for (int i = 1; i < points.Length && start == Vector2.Zero; i++)
            {
                start = (Vector2)points[i] - first;
            }

            Vector2 end = Vector2.Zero;
            for (int i = points.Length - 2; i >= 0 && end == Vector2.Zero; i--)
            {
                end = last - (Vector2)points[i];
            }

            return new MarkerSegment
            {
                Start = points[0],
                End = points[^1],
                StartDirection = start,
                EndDirection = end,
                IsDegenerate = start == Vector2.Zero,
            };
        }
    }

    /// <summary>
    /// A marker vertex: its position and the direction of <c>orient="auto"</c> there.
    /// </summary>
    private readonly struct MarkerVertex
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MarkerVertex"/> struct.
        /// </summary>
        /// <param name="position">The position in the user space of the shape.</param>
        /// <param name="angle">The angle in degrees.</param>
        public MarkerVertex(PointF position, float angle)
        {
            this.Position = position;
            this.Angle = angle;
        }

        /// <summary>
        /// Gets the position in the user space of the shape.
        /// </summary>
        public Vector2 Position { get; }

        /// <summary>
        /// Gets the angle in degrees.
        /// </summary>
        public float Angle { get; }
    }
}
