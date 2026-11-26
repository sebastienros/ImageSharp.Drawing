// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>clip-path</c> with a CSS basic shape or a geometry box (CSS Masking Level 1, CSS Shapes Level 1 and 2).
/// </content>
/// <remarks>
/// <para>
/// The clip region is the fill region of the shape, built in the coordinates of its reference box, which lie in the user
/// space of the clipped element after its own transform. Lengths resolve with the existing length rules; percentages
/// refer to the width or the height of the reference box, and the radius of <c>circle()</c> to its normalized diagonal.
/// </para>
/// <para>
/// Reference boxes, as the Chromium oracle renders them on SVG elements: <c>fill-box</c>, <c>content-box</c>, and
/// <c>padding-box</c> are the object bounding box; <c>stroke-box</c>, <c>border-box</c>, and <c>margin-box</c> are the
/// stroke bounding box, which is also the default (CSS Masking makes <c>border-box</c> the default); <c>view-box</c> is
/// a box at the origin of the user space with the size of the nearest viewport, which is the size of its
/// <c>viewBox</c> when it has one, as CSS Masking defines it (the origin of the <c>viewBox</c> does not move it). The
/// numbers apply in the user space of the element after its own transform, as in Chromium. The outermost
/// <c>svg</c> element has a CSS layout box, so every box is its viewport there. A shape without area, such as any shape
/// on the fill box of a horizontal line, or an element without a box, renders nothing; the default stroke box of a
/// stroked line has area.
/// </para>
/// <para>
/// A shape whose fill region does not depend on the fill rule becomes a geometry clip, without a layer. A
/// <c>polygon()</c> or <c>path()</c> with <c>evenodd</c> draws into an alpha mask layer, as a <c>clipPath</c> that needs
/// coverage does.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// Returns the reference box of a basic shape on an element, in the coordinates that the shape clip uses.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="box">The geometry box of the value.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="objectBox">The object bounding box of the element in its user space, or <see langword="null"/> when it has none.</param>
    /// <param name="transform">When this method returns, contains the transform from the box coordinates to document user space.</param>
    /// <returns>The reference box, or <see langword="null"/> when the element has none.</returns>
    private RectangleF? GetClipShapeBox(int element, SvgGeometryBox box, in SvgResolveContext context, RectangleF? objectBox, out Matrix3x2 transform)
    {
        transform = context.Transform;
        if (element == 0)
        {
            // The outermost svg element has a CSS layout box, its viewport, in the coordinates of the intrinsic size,
            // outside the view box transform. context.Transform is V × T × V⁻¹ (see GetRootTransform), so V⁻¹ in front
            // of it maps the viewport through the root transform T into document user space.
            RectangleF mappedBox = this.rootViewBox ?? new RectangleF(PointF.Empty, this.intrinsicSize);
            RectangleF viewport = new(PointF.Empty, this.intrinsicSize);
            Matrix3x2 viewBox = SvgViewBoxTransform.Create(mappedBox, this.tree.GetViewport(0).PreserveAspectRatio, viewport);
            if (!Matrix3x2.Invert(viewBox, out Matrix3x2 inverse))
            {
                return null;
            }

            transform = inverse * context.Transform;
            return viewport;
        }

        if (UsesViewBox(box))
        {
            return new RectangleF(PointF.Empty, context.Viewport);
        }

        if (UsesStrokeBox(box))
        {
            return this.TryGetStrokeBoundingBox(element, in context, out RectangleF stroke) ? stroke : null;
        }

        return objectBox;
    }

    /// <summary>
    /// Classifies the clip of a basic shape and builds its geometry. For <see cref="SvgClipKind.Geometry"/> it appends the
    /// path and its transform to <paramref name="geometry"/>; for <see cref="SvgClipKind.Coverage"/> it returns the path in
    /// <paramref name="coverage"/>, which fills with the <c>evenodd</c> rule.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="referenceBox">The reference box, or <see langword="null"/> when the element has none.</param>
    /// <param name="fontSize">The computed font size of the element, for font-relative lengths.</param>
    /// <param name="text">The computed text style of the element, whose font <c>ch</c> refers to.</param>
    /// <param name="transform">The transform from the box coordinates to document user space.</param>
    /// <param name="geometry">The list that receives the geometry.</param>
    /// <param name="coverage">When this method returns <see cref="SvgClipKind.Coverage"/>, contains the path.</param>
    /// <returns>
    /// <see cref="SvgClipKind.Empty"/> when the clip region has no area, so the element renders nothing;
    /// <see cref="SvgClipKind.Geometry"/> or <see cref="SvgClipKind.Coverage"/> otherwise.
    /// </returns>
    private SvgClipKind ClassifyClipShape(
        SvgClipShape shape,
        RectangleF? referenceBox,
        float fontSize,
        SvgTextStyle text,
        Matrix3x2 transform,
        List<(IPath Path, Matrix3x2 Transform)> geometry,
        out IPath? coverage)
    {
        coverage = null;
        if (referenceBox is not RectangleF box
            || !float.IsFinite(box.X + box.Y + box.Width + box.Height)
            || BuildClipShapePath(shape, box, fontSize, this.units.WithFont(text)) is not IPath path)
        {
            return SvgClipKind.Empty;
        }

        // Only a polygon or a path can cross itself, so only there does the rule change the region. A canvas clip fills
        // with the non-zero rule.
        if (shape.FillRule == SvgFillRule.EvenOdd && shape.Kind is SvgBasicShapeKind.Polygon or SvgBasicShapeKind.Path)
        {
            _ = path.AsClosedPath();
            coverage = path;
            return SvgClipKind.Coverage;
        }

        geometry.Add((path, transform));
        return SvgClipKind.Geometry;
    }

    /// <summary>
    /// Opens the clip of a basic shape around clip coverage, as <see cref="BeginClipScope(int, in SvgClipTarget)"/> does for
    /// a <c>clipPath</c>.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="referenceBox">The reference box in the user space of <paramref name="target"/>, or <see langword="null"/>.</param>
    /// <param name="fontSize">The computed font size, for font-relative lengths.</param>
    /// <param name="text">The computed text style, whose font <c>ch</c> refers to.</param>
    /// <param name="target">The clipped element.</param>
    /// <returns>The scope to close with <see cref="EndClipScope(in SvgClipScope)"/>.</returns>
    private SvgClipScope BeginShapeClipScope(SvgClipShape shape, RectangleF? referenceBox, float fontSize, SvgTextStyle text, in SvgClipTarget target)
    {
        List<(IPath Path, Matrix3x2 Transform)> geometry = this.clipGeometry ??= [];
        int first = geometry.Count;
        SvgClipKind kind = this.ClassifyClipShape(shape, referenceBox, fontSize, text, target.Transform, geometry, out IPath? coverage);
        int value = 0;
        if (kind == SvgClipKind.Geometry)
        {
            value = geometry.Count - first;
            this.AddClipGeometry(geometry, first);
        }
        else if (kind == SvgClipKind.Coverage)
        {
            value = this.nodes.Count;
            this.nodes.Add(new SvgRenderNode { Kind = SvgRenderNodeKind.None });
        }

        return new SvgClipScope { Kind = kind, Clip = -1, Target = target, Value = value, Shape = coverage };
    }

    /// <summary>
    /// Adds the draw node that gives the coverage of a basic shape with the <c>evenodd</c> rule: an opaque fill, which the
    /// caller places in a mask layer with <see cref="SvgMaskMode.Alpha"/>.
    /// </summary>
    /// <param name="path">The path in the coordinates of the reference box.</param>
    /// <param name="transform">The transform from the box coordinates to document user space.</param>
    private void AddClipShapeCoverage(IPath path, Matrix3x2 transform)
        => this.nodes.Add(new SvgRenderNode
        {
            Kind = SvgRenderNodeKind.Draw,
            Path = path,
            Fill = this.paints.GetSolidBrush(Color.Black),
            FillRule = IntersectionRule.EvenOdd,
            TransformIndex = this.GetTransformIndex(transform),
            Bounds = TransformBounds(path.Bounds, transform),
        });

    /// <summary>
    /// Returns the reference box of a basic shape on a child of a <c>clipPath</c>, or on the <c>clipPath</c> element itself,
    /// from the boxes that the clip code knows.
    /// </summary>
    /// <param name="box">The geometry box of the value.</param>
    /// <param name="fillBox">The object bounding box.</param>
    /// <param name="strokeBox">The stroke bounding box.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <returns>The reference box.</returns>
    private static RectangleF? SelectClipShapeBox(SvgGeometryBox box, RectangleF? fillBox, RectangleF? strokeBox, SizeF viewport)
        => UsesViewBox(box) ? new RectangleF(PointF.Empty, viewport) : UsesStrokeBox(box) ? strokeBox : fillBox;

    /// <summary>
    /// Returns whether a geometry box maps to the stroke bounding box on an SVG element.
    /// </summary>
    /// <param name="box">The geometry box.</param>
    /// <returns><see langword="true"/> for <c>stroke-box</c>, <c>border-box</c>, <c>margin-box</c>, and no box.</returns>
    private static bool UsesStrokeBox(SvgGeometryBox box)
        => box is SvgGeometryBox.Unspecified or SvgGeometryBox.StrokeBox or SvgGeometryBox.BorderBox or SvgGeometryBox.MarginBox;

    /// <summary>
    /// Returns whether a geometry box is <c>view-box</c>.
    /// </summary>
    /// <param name="box">The geometry box.</param>
    /// <returns><see langword="true"/> for <c>view-box</c>.</returns>
    private static bool UsesViewBox(SvgGeometryBox box) => box == SvgGeometryBox.ViewBox;

    /// <summary>
    /// Builds the outline of a basic shape in the coordinates of its reference box.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="box">The reference box.</param>
    /// <param name="fontSize">The computed font size, for font-relative lengths.</param>
    /// <param name="units">The unit basis, with the font of the element for <c>ch</c>.</param>
    /// <returns>The outline, or <see langword="null"/> when the shape has no area.</returns>
    private static IPath? BuildClipShapePath(SvgClipShape shape, RectangleF box, float fontSize, SvgUnitBasis units)
    {
        float width = box.Width;
        float height = box.Height;
        SvgLength[] lengths = shape.Lengths;
        switch (shape.Kind)
        {
            case SvgBasicShapeKind.None:
                return BuildRoundedRectangle(box, null, box, fontSize, units);

            case SvgBasicShapeKind.Inset:
            {
                float top = ToBoxLength(lengths[0], height, fontSize, units);
                float right = ToBoxLength(lengths[1], width, fontSize, units);
                float bottom = ToBoxLength(lengths[2], height, fontSize, units);
                float left = ToBoxLength(lengths[3], width, fontSize, units);

                // CSS Shapes: insets of one dimension that add up to more than the box reduce in proportion to fit.
                ReduceInsets(ref left, ref right, width);
                ReduceInsets(ref top, ref bottom, height);
                RectangleF rectangle = new(box.X + left, box.Y + top, width - left - right, height - top - bottom);
                return BuildRoundedRectangle(rectangle, shape.Radii, box, fontSize, units);
            }

            case SvgBasicShapeKind.Xywh:
            {
                RectangleF rectangle = new(
                    box.X + ToBoxLength(lengths[0], width, fontSize, units),
                    box.Y + ToBoxLength(lengths[1], height, fontSize, units),
                    ToBoxLength(lengths[2], width, fontSize, units),
                    ToBoxLength(lengths[3], height, fontSize, units));
                return BuildRoundedRectangle(rectangle, shape.Radii, box, fontSize, units);
            }

            case SvgBasicShapeKind.Rect:
            {
                // auto is 0% for the top and left edges and 100% for the right and bottom edges. CSS Shapes Level 2 clamps
                // the bottom edge to the top edge and the right edge to the left edge, so the rectangle is never negative.
                float top = lengths[0].IsSpecified ? ToBoxLength(lengths[0], height, fontSize, units) : 0F;
                float right = lengths[1].IsSpecified ? ToBoxLength(lengths[1], width, fontSize, units) : width;
                float bottom = lengths[2].IsSpecified ? ToBoxLength(lengths[2], height, fontSize, units) : height;
                float left = lengths[3].IsSpecified ? ToBoxLength(lengths[3], width, fontSize, units) : 0F;
                RectangleF rectangle = new(box.X + left, box.Y + top, MathF.Max(right - left, 0F), MathF.Max(bottom - top, 0F));
                return BuildRoundedRectangle(rectangle, shape.Radii, box, fontSize, units);
            }

            case SvgBasicShapeKind.Circle:
            case SvgBasicShapeKind.Ellipse:
            {
                SvgShapePosition position = shape.Position;
                float x = ToBoxLength(position.X, width, fontSize, units);
                float y = ToBoxLength(position.Y, height, fontSize, units);
                float cx = box.X + (position.XFromEnd ? width - x : x);
                float cy = box.Y + (position.YFromEnd ? height - y : y);

                // The distances from the center to the vertical and to the horizontal sides of the box.
                float nearX = MathF.Min(MathF.Abs(cx - box.Left), MathF.Abs(box.Right - cx));
                float farX = MathF.Max(MathF.Abs(cx - box.Left), MathF.Abs(box.Right - cx));
                float nearY = MathF.Min(MathF.Abs(cy - box.Top), MathF.Abs(box.Bottom - cy));
                float farY = MathF.Max(MathF.Abs(cy - box.Top), MathF.Abs(box.Bottom - cy));
                float rx;
                float ry;
                if (shape.Kind == SvgBasicShapeKind.Circle)
                {
                    // A circle measures closest-side and farthest-side against all four sides, and a percentage against
                    // the normalized diagonal of the box.
                    rx = shape.RadiusXKind switch
                    {
                        SvgShapeRadiusKind.Length => ToBoxLength(lengths[0], SvgUnits.Diagonal(box.Size), fontSize, units),
                        SvgShapeRadiusKind.FarthestSide => MathF.Max(farX, farY),
                        _ => MathF.Min(nearX, nearY),
                    };

                    ry = rx;
                }
                else
                {
                    rx = shape.RadiusXKind switch
                    {
                        SvgShapeRadiusKind.Length => ToBoxLength(lengths[0], width, fontSize, units),
                        SvgShapeRadiusKind.FarthestSide => farX,
                        _ => nearX,
                    };

                    ry = shape.RadiusYKind switch
                    {
                        SvgShapeRadiusKind.Length => ToBoxLength(lengths[1], height, fontSize, units),
                        SvgShapeRadiusKind.FarthestSide => farY,
                        _ => nearY,
                    };
                }

                if (!(rx > 0F) || !(ry > 0F) || !float.IsFinite(rx + ry + cx + cy))
                {
                    return null;
                }

                return BuildEllipse(cx, cy, rx, ry);
            }

            case SvgBasicShapeKind.Polygon:
            {
                int count = lengths.Length / 2;
                if (count < 3)
                {
                    return null;
                }

                PointF[] points = new PointF[count];
                for (int i = 0; i < count; i++)
                {
                    points[i] = new PointF(
                        box.X + ToBoxLength(lengths[2 * i], width, fontSize, units),
                        box.Y + ToBoxLength(lengths[(2 * i) + 1], height, fontSize, units));
                }

                Polygon polygon = new(new LinearLineSegment(points));
                return HasFiniteArea(polygon.Bounds) ? polygon : null;
            }

            case SvgBasicShapeKind.Path:
            {
                // The path data is in the coordinates of the reference box, with its origin at the top left corner.
                IPath? path = shape.Path;
                if (path is null || !HasFiniteArea(path.Bounds))
                {
                    return null;
                }

                return box.X == 0F && box.Y == 0F ? path : path.Translate(box.X, box.Y);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Converts a length of a basic shape to user units.
    /// </summary>
    /// <param name="length">The length.</param>
    /// <param name="percentBase">The size that a percentage refers to.</param>
    /// <param name="fontSize">The computed font size.</param>
    /// <param name="units">The unit basis.</param>
    /// <returns>The length in user units; 0 for an unspecified length.</returns>
    private static float ToBoxLength(SvgLength length, float percentBase, float fontSize, SvgUnitBasis units)
        => SvgUnits.ToUser(length, percentBase, fontSize, units, 0F);

    /// <summary>
    /// Reduces two opposite insets in proportion when they add up to more than the size of the box.
    /// </summary>
    /// <param name="start">The inset from the start edge.</param>
    /// <param name="end">The inset from the end edge.</param>
    /// <param name="size">The size of the box along the axis.</param>
    private static void ReduceInsets(ref float start, ref float end, float size)
    {
        float sum = start + end;
        if (sum > size && sum > 0F)
        {
            float scale = size / sum;
            start *= scale;
            end *= scale;
        }
    }

    /// <summary>
    /// Returns whether a rectangle has a positive, finite area.
    /// </summary>
    /// <param name="bounds">The rectangle.</param>
    /// <returns><see langword="true"/> if the rectangle has area.</returns>
    private static bool HasFiniteArea(RectangleF bounds)
        => bounds.Width > 0F && bounds.Height > 0F && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height);

    /// <summary>
    /// Builds a rectangle with the corner radii of <c>round</c>.
    /// </summary>
    /// <remarks>
    /// Percentages of a radius refer to the reference box: horizontal radii to its width and vertical radii to its height,
    /// as the Chromium oracle resolves them. When the radii of one side add up to more than the side, every radius shrinks
    /// by the same factor, the corner overlap rule of CSS Backgrounds. Each corner is a quarter ellipse as one cubic
    /// Bézier curve, as in <see cref="CreateRect(int, in SvgResolveContext)"/>.
    /// </remarks>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="radii">The eight radii of <see cref="SvgClipShape.Radii"/>, or <see langword="null"/> for square corners.</param>
    /// <param name="box">The reference box.</param>
    /// <param name="fontSize">The computed font size.</param>
    /// <param name="units">The unit basis.</param>
    /// <returns>The outline, or <see langword="null"/> when the rectangle has no area.</returns>
    private static IPath? BuildRoundedRectangle(RectangleF rectangle, SvgLength[]? radii, RectangleF box, float fontSize, SvgUnitBasis units)
    {
        if (!HasFiniteArea(rectangle) || !float.IsFinite(rectangle.X + rectangle.Y))
        {
            return null;
        }

        if (radii is null)
        {
            return new RectanglePolygon(rectangle);
        }

        // Horizontal radii of the corners top left, top right, bottom right, bottom left, then the vertical radii.
        Span<float> h = stackalloc float[4];
        Span<float> v = stackalloc float[4];
        bool rounded = false;
        for (int i = 0; i < 4; i++)
        {
            h[i] = SvgUnits.ToUser(radii[i], box.Width, fontSize, units, 0F);
            v[i] = SvgUnits.ToUser(radii[i + 4], box.Height, fontSize, units, 0F);
            if (!(h[i] > 0F && v[i] > 0F))
            {
                // A corner with a zero radius on either axis is square.
                h[i] = 0F;
                v[i] = 0F;
            }

            rounded |= h[i] > 0F;
        }

        if (!rounded)
        {
            return new RectanglePolygon(rectangle);
        }

        float w = rectangle.Width;
        float ht = rectangle.Height;
        float factor = MathF.Min(
            MathF.Min(Fit(w, h[0] + h[1]), Fit(w, h[3] + h[2])),
            MathF.Min(Fit(ht, v[0] + v[3]), Fit(ht, v[1] + v[2])));
        for (int i = 0; i < 4; i++)
        {
            h[i] *= factor;
            v[i] *= factor;
        }

        float left = rectangle.Left;
        float top = rectangle.Top;
        float right = rectangle.Right;
        float bottom = rectangle.Bottom;
        List<ILineSegment> segments = new(8);
        AddEdge(segments, new PointF(left + h[0], top), new PointF(right - h[1], top));
        AddCorner(segments, new PointF(right - h[1], top), new PointF(right, top + v[1]), new Vector2(h[1] * Kappa, 0F), new Vector2(0F, -v[1] * Kappa));
        AddEdge(segments, new PointF(right, top + v[1]), new PointF(right, bottom - v[2]));
        AddCorner(segments, new PointF(right, bottom - v[2]), new PointF(right - h[2], bottom), new Vector2(0F, v[2] * Kappa), new Vector2(h[2] * Kappa, 0F));
        AddEdge(segments, new PointF(right - h[2], bottom), new PointF(left + h[3], bottom));
        AddCorner(segments, new PointF(left + h[3], bottom), new PointF(left, bottom - v[3]), new Vector2(-h[3] * Kappa, 0F), new Vector2(0F, v[3] * Kappa));
        AddEdge(segments, new PointF(left, bottom - v[3]), new PointF(left, top + v[0]));
        AddCorner(segments, new PointF(left, top + v[0]), new PointF(left + h[0], top), new Vector2(0F, -v[0] * Kappa), new Vector2(-h[0] * Kappa, 0F));
        return new Polygon(segments.ToArray());

        static float Fit(float side, float sum) => sum > side ? side / sum : 1F;

        static void AddEdge(List<ILineSegment> segments, PointF start, PointF end)
        {
            if (start != end)
            {
                segments.Add(new LinearLineSegment(start, end));
            }
        }

        // A corner from start to end: the first control point lies along startTangent from the start, and the second
        // control point lies back along endTangent from the end.
        static void AddCorner(List<ILineSegment> segments, PointF start, PointF end, Vector2 startTangent, Vector2 endTangent)
        {
            if (start != end)
            {
                segments.Add(new CubicBezierLineSegment(start, (Vector2)start + startTangent, (Vector2)end + endTangent, end));
            }
        }
    }

    /// <summary>
    /// Builds an ellipse of four cubic Bézier quarters, starting at its rightmost point.
    /// </summary>
    /// <param name="cx">The x coordinate of the center.</param>
    /// <param name="cy">The y coordinate of the center.</param>
    /// <param name="rx">The horizontal radius, greater than zero.</param>
    /// <param name="ry">The vertical radius, greater than zero.</param>
    /// <returns>The outline.</returns>
    private static Polygon BuildEllipse(float cx, float cy, float rx, float ry)
    {
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
    /// Computes the stroke bounding box of an element in its own user space: the union of its object bounding box and the
    /// bounds of its stroke outline, without markers, as SVG 2 defines it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk mirrors <see cref="TryGetObjectBoundingBox(int, in SvgResolveContext, out RectangleF)"/>. A shape adds the
    /// bounds of its stroke outline, with its joins, miters, and caps and without its dashes, when its <c>stroke</c> is not
    /// <c>none</c> and its width is positive; a transparent stroke counts, as in Chromium. Text adds half its stroke width
    /// around its object bounding box. Images add their object bounding box.
    /// </para>
    /// <para>
    /// Only basic shapes in <c>clip-path</c> use the stroke bounding box, so a document without them never computes one.
    /// </para>
    /// </remarks>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element; its transform and depth are ignored.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the box in the user space of the element.</param>
    /// <returns><see langword="true"/> if the element has a box; otherwise, <see langword="false"/>.</returns>
    private bool TryGetStrokeBoundingBox(int element, in SvgResolveContext context, out RectangleF bounds)
    {
        bounds = default;
        SvgComputedStyle style = context.Style;
        if (style.Display == SvgDisplay.None)
        {
            return false;
        }

        SvgElementKind kind = this.tree[element].Kind;
        switch (kind)
        {
            case SvgElementKind.Path:
            case SvgElementKind.Rect:
            case SvgElementKind.Circle:
            case SvgElementKind.Ellipse:
            case SvgElementKind.Line:
            case SvgElementKind.Polyline:
            case SvgElementKind.Polygon:
            {
                IPath? path = this.GetShape(element, kind, in context);
                if (path is null)
                {
                    return false;
                }

                bounds = GetStrokeBounds(path, in style, this.GetStrokeWidth(in style, context.Viewport));
                return float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height);
            }

            case SvgElementKind.A when this.IsActiveTextSpan(element):
            case SvgElementKind.Text:
            case SvgElementKind.TSpan:
            case SvgElementKind.TextPath:
            {
                if (!this.TryGetObjectBoundingBox(element, in context, out bounds))
                {
                    return false;
                }

                float width = this.GetStrokeWidth(in style, context.Viewport);
                if (style.Stroke.Kind != SvgPaintKind.None && width > 0F)
                {
                    bounds = RectangleF.Inflate(bounds, width / 2F, width / 2F);
                }

                return true;
            }

            case SvgElementKind.G:
            case SvgElementKind.A:
                return this.TryGetChildrenStrokeBoundingBox(element, in context, Matrix3x2.Identity, context.Viewport, out bounds);

            case SvgElementKind.Switch:
            {
                int child = this.GetSwitchChild(element);
                if (child < 0)
                {
                    return false;
                }

                SvgElementKind childKind = this.tree[child].Kind;
                SvgComputedStyle childStyle = SvgComputedStyle.Compute(in this.tree.GetStyle(child), in style, in this.initialStyle, this.units, childKind);
                SvgResolveContext childContext = new(in childStyle, Matrix3x2.Identity, context.Viewport, context.Depth + 1);
                if (!this.TryGetStrokeBoundingBox(child, in childContext, out RectangleF box))
                {
                    return false;
                }

                bounds = this.TryGetLocalTransform(child, in childStyle, context.Viewport, out Matrix3x2 local) ? TransformBounds(box, local) : box;
                return true;
            }

            case SvgElementKind.Svg:
            case SvgElementKind.Symbol:
            {
                if (!this.TryGetViewportTransform(element, in context, default, default, out Matrix3x2 content, out SizeF contentViewport, out _))
                {
                    return false;
                }

                return this.TryGetChildrenStrokeBoundingBox(element, in context, content, contentViewport, out bounds);
            }

            case SvgElementKind.Use:
            {
                ref readonly SvgUseAttributes use = ref this.tree.GetUse(element);
                if (use.Href is null
                    || !this.tree.TryGetElementById(use.Href, out int target)
                    || (target <= element && element < this.tree[target].SubtreeEnd)
                    || !this.TryEnterReference(element))
                {
                    return false;
                }

                SvgElementKind targetKind = this.tree[target].Kind;
                SvgComputedStyle targetStyle = SvgComputedStyle.Compute(in this.tree.GetStyle(target), in style, in this.initialStyle, this.units, targetKind);
                SvgResolveContext targetContext = new(in targetStyle, Matrix3x2.Identity, context.Viewport, context.Depth + 1);
                bool found = false;
                RectangleF box = default;
                if (targetKind is SvgElementKind.Svg or SvgElementKind.Symbol)
                {
                    if (targetStyle.Display != SvgDisplay.None
                        && this.TryGetViewportTransform(target, in targetContext, use.Width, use.Height, out Matrix3x2 content, out SizeF viewport, out _))
                    {
                        found = this.TryGetChildrenStrokeBoundingBox(target, in targetContext, content, viewport, out box);
                    }
                }
                else
                {
                    found = this.TryGetStrokeBoundingBox(target, in targetContext, out box);
                }

                this.ExitReference();
                if (!found)
                {
                    return false;
                }

                SvgUnitBasis useUnits = this.units.WithFont(style.Text);
                float x = SvgUnits.ToUser(use.X, context.Viewport.Width, style.FontSize, useUnits, 0F);
                float y = SvgUnits.ToUser(use.Y, context.Viewport.Height, style.FontSize, useUnits, 0F);
                Matrix3x2 transform = this.TryGetLocalTransform(target, in targetStyle, context.Viewport, out Matrix3x2 local) ? local : Matrix3x2.Identity;
                bounds = TransformBounds(box, transform * Matrix3x2.CreateTranslation(x, y));
                return true;
            }

            default:
                return this.TryGetObjectBoundingBox(element, in context, out bounds);
        }
    }

    /// <summary>
    /// Computes the union of the stroke bounding boxes of the children of an element, as
    /// <see cref="TryGetChildrenBoundingBox(int, in SvgResolveContext, Matrix3x2, SizeF, out RectangleF)"/> does for
    /// object bounding boxes.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="context">The context of the element, whose style the children inherit.</param>
    /// <param name="contentTransform">The transform from the content coordinate system of the element to its user space.</param>
    /// <param name="viewport">The viewport that percentages in the children refer to.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the union.</param>
    /// <returns><see langword="true"/> if at least one child has a box; otherwise, <see langword="false"/>.</returns>
    private bool TryGetChildrenStrokeBoundingBox(int element, in SvgResolveContext context, Matrix3x2 contentTransform, SizeF viewport, out RectangleF bounds)
    {
        bounds = default;
        bool found = false;
        int depth = context.Depth + 1;
        if (depth > this.maxDepth)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the nesting depth limit of {this.maxDepth} through use references.");
        }

        for (int child = this.tree.GetFirstChild(element); child >= 0; child = this.tree.GetNextSibling(child))
        {
            if (this.tree[child].HasConditionalAttributes && !this.PassesConditions(child))
            {
                continue;
            }

            SvgElementKind kind = this.tree[child].Kind;
            SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(child), context.Style, in this.initialStyle, this.units, kind);
            SvgResolveContext childContext = new(in style, Matrix3x2.Identity, viewport, depth);
            if (!this.TryGetStrokeBoundingBox(child, in childContext, out RectangleF box))
            {
                continue;
            }

            Matrix3x2 transform = this.TryGetLocalTransform(child, in style, viewport, out Matrix3x2 local) ? local * contentTransform : contentTransform;
            box = TransformBounds(box, transform);
            bounds = found ? RectangleF.Union(bounds, box) : box;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Returns the stroke width of a style in user units.
    /// </summary>
    /// <param name="style">The computed style.</param>
    /// <param name="viewport">The viewport, for percentages.</param>
    /// <returns>The width.</returns>
    private float GetStrokeWidth(in SvgComputedStyle style, SizeF viewport)
        => SvgUnits.ToUser(style.StrokeWidth, SvgUnits.Diagonal(viewport), style.FontSize, this.units, 0F);

    /// <summary>
    /// Returns the union of the bounds of a shape and of its stroke outline.
    /// </summary>
    /// <param name="path">The shape geometry.</param>
    /// <param name="style">The computed style of the shape.</param>
    /// <param name="width">The stroke width in user units.</param>
    /// <returns>The bounds.</returns>
    private static RectangleF GetStrokeBounds(IPath path, in SvgComputedStyle style, float width)
    {
        RectangleF bounds = path.Bounds;
        if (style.Stroke.Kind == SvgPaintKind.None || !(width > 0F) || !float.IsFinite(width))
        {
            return bounds;
        }

        // The same cap and join mapping as the pens of the resolver; dashes do not change the box.
        StrokeOptions options = new()
        {
            LineCap = style.StrokeLineCap switch
            {
                SvgStrokeLineCap.Round => LineCap.Round,
                SvgStrokeLineCap.Square => LineCap.Square,
                _ => LineCap.Butt
            },
            LineJoin = style.StrokeLineJoin switch
            {
                SvgStrokeLineJoin.Round => LineJoin.Round,
                SvgStrokeLineJoin.Bevel => LineJoin.Bevel,
                _ => LineJoin.MiterRevert
            },
            MiterLimit = style.StrokeMiterLimit,
        };

        // An empty outline, of a path without length, has default bounds at the origin, which must not join the box.
        RectangleF outline = path.GenerateOutline(width, options).Bounds;
        return HasFiniteArea(outline) && float.IsFinite(outline.X + outline.Y) ? RectangleF.Union(bounds, outline) : bounds;
    }
}
