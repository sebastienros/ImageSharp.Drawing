// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 6: the placement of clusters on the path of their <c>textPath</c>, after the "position on path" step of the
/// SVG 2 text layout algorithm (SVG 2, 11.5.1 and 11.8.3).
/// </summary>
/// <remarks>
/// <para>
/// Stage 4 lays the text of a <c>textPath</c> out on a straight line that starts at (0, 0), and stage 5 anchors it.
/// This stage bends that line onto the path. The inline axis of the line becomes the distance along the path, and the
/// cross axis the offset along the normal of the path. The inline axis is x in horizontal text, y in vertical text, and
/// -y in <c>sideways-lr</c> text, whose pen moves upward.
/// </para>
/// <para>
/// For each cluster, <c>mid</c> is the distance along the path of the middle of its advance: its inline position, plus
/// half its advance (with spacing and <c>lengthAdjust</c>), plus <c>startOffset</c>. A cluster whose <c>mid</c> lies
/// before the start or after the end of the path is left out. Closed paths do not wrap: SVG 2 lets text on a single
/// closed subpath run once around it, but Chromium leaves the clusters past the end out, and this renderer follows it.
/// Otherwise the point at <c>mid</c> and the tangent there give the placement. The rotation that turns the inline axis
/// of the line onto the tangent turns the cluster, and turns the cross axis onto the normal (SVG 2: t + 90 degrees for
/// horizontal text, t - 90 degrees for vertical text).
/// </para>
/// <para>
/// The angle of the <c>rotate</c> attribute turns a cluster about its point on the path, together with its offset
/// across the path. SVG 2 places the pen at <c>point - o * t + y * n</c> and turns the glyph about the pen; the
/// Chromium oracle instead keeps the middle of the advance on the path for any <c>rotate</c> value, and turns a
/// <c>dy</c> offset with the glyph. This was decided from rendered output.
/// </para>
/// <para>
/// The text after a <c>textPath</c>, up to the next absolute position, continues at the end point of the path: it
/// moves so that its first cluster lies on the end point, as SVG 2 defines with <c>path_end</c>. This includes the
/// shift of that first cluster, which it cancels, as in the Chromium oracle. After a <c>textPath</c> without a path,
/// whose own text does not render, the text moves along the inline axis by the sum of its own advances instead, which
/// the Chromium oracle showed in every variant tested.
/// </para>
/// <para>
/// The path comes from the <c>path</c> attribute when it has a valid segment, and otherwise from the element that
/// <c>href</c> references. SVG 2 also allows basic shapes; Chromium accepts only <c>path</c> elements, and so does
/// this renderer, so a <c>textPath</c> that references anything else draws none of its text. The transform of the
/// referenced element applies, in the user space of the <c>text</c> element; the transforms of its ancestors do not.
/// <c>side="right"</c> reverses the path. <c>method</c> and <c>spacing</c> have no effect, as in Chromium; SVG 2
/// leaves their result to the user agent. <c>pathLength</c> on the referenced element is not supported.
/// </para>
/// </remarks>
internal static partial class SvgTextPaths
{
    /// <summary>
    /// The flattening tolerance of a path as a fraction of the font size of its <c>textPath</c>. Text placement then
    /// depends on the size of the text, not on the size of the user unit, and the library flattening (1/64 of a unit)
    /// applies at every font size above 15.6 units.
    /// </summary>
    private const float ToleranceOfFontSize = 1F / 1000F;

    /// <inheritdoc cref="PlaceOnPaths"/>
    static partial void PlaceOnPaths(SvgTextLayout layout, SvgResolver resolver, ref bool placed)
    {
        placed = true;
        SvgTextContent content = layout.Content;
        Span<SvgTextCluster> clusters = layout.Clusters;
        SvgTextSpan[] spans = content.Spans;
        SvgTextCharacter[] characters = content.Characters;
        SvgWritingMode mode = content.Directions?.WritingMode ?? SvgWritingMode.HorizontalTb;
        bool vertical = content.Directions?.IsVertical == true;

        // The axes of the straight line in user space: the inline axis, along which the pen moves, and the cross axis,
        // along which shifts across the line apply. The angle of the inline axis maps it onto the tangent of the path.
        float inlineAngle = !vertical ? 0F : mode == SvgWritingMode.SidewaysLr ? -90F : 90F;
        float inlineSign = mode == SvgWritingMode.SidewaysLr ? -1F : 1F;

        int start = 0;
        while (start < clusters.Length)
        {
            int textPath = spans[clusters[start].Span].TextPath;
            if (textPath < 0)
            {
                start++;
                continue;
            }

            // The clusters of one textPath are consecutive in logical order.
            int end = start + 1;
            while (end < clusters.Length && spans[clusters[end].Span].TextPath == textPath)
            {
                end++;
            }

            SvgTextSpan pathSpan = spans[content.FindSpan(textPath)];
            ref readonly SvgTextPathAttributes attributes = ref resolver.Tree.GetTextPath(textPath);
            SvgTextPathMeasure? measure = GetMeasure(layout, resolver, textPath, in attributes, pathSpan.Style.FontSize);
            bool reverse = attributes.Side == SvgTextPathSide.Right;
            Vector2 endPoint = default;
            if (measure is null)
            {
                foreach (ref SvgTextCluster cluster in clusters[start..end])
                {
                    cluster.Skipped = true;
                }
            }
            else
            {
                float length = measure.Length;
                float offset = SvgUnits.ToUser(attributes.StartOffset, length, pathSpan.Style.FontSize, resolver.Units, 0F);
                _ = measure.GetPoint(reverse ? 0F : length, out endPoint);

                for (int i = start; i < end; i++)
                {
                    ref SvgTextCluster cluster = ref clusters[i];
                    float rotate = characters[cluster.CharStart].Rotate;
                    PlaceCluster(ref cluster, measure, offset, reverse, vertical, inlineAngle, inlineSign, float.IsNaN(rotate) ? 0F : rotate);
                }
            }

            // The text after the path, up to the next absolute position or path, moves so that its first cluster lies
            // on the end point of the path. Text that starts with an absolute position stays where it is.
            int after = end;
            int stop = after;
            while (stop < clusters.Length
                && spans[clusters[stop].Span].TextPath < 0
                && (characters[clusters[stop].CharStart].Flags & (SvgTextCharacterFlags.HasX | SvgTextCharacterFlags.HasY)) == 0)
            {
                stop++;
            }

            if (stop > after)
            {
                Vector2 shift;
                if (measure is not null)
                {
                    shift = endPoint - new Vector2(clusters[after].X, clusters[after].Y);
                }
                else
                {
                    // Without a path there is no end point. The text keeps its line position, whose offset across the
                    // line restarted with the path, moved along the inline axis by the sum of its own advances, the
                    // distance the pen moves over it: +x in horizontal text, also right-to-left, +y in vertical text,
                    // and -y in sideways-lr text. SVG 2 does not define this case; the rule was decided from the
                    // output of the Chromium oracle for every text-anchor value, both directions, vertical and
                    // sideways-lr text, and text before the path with and without x, dx, and tspan positions.
                    float advance = 0F;
                    for (int i = after; i < stop; i++)
                    {
                        advance += clusters[i].Width * clusters[i].ScaleX;
                    }

                    shift = vertical ? new Vector2(0F, advance) : new Vector2(advance, 0F);
                }

                foreach (ref SvgTextCluster cluster in clusters[after..stop])
                {
                    cluster.X += shift.X;
                    cluster.Y += shift.Y;
                }
            }

            start = end;
        }
    }

    /// <summary>
    /// Places one cluster of a <c>textPath</c> on its path, or leaves it out when the middle of its advance is off the
    /// path.
    /// </summary>
    /// <param name="cluster">The cluster, with its anchored line position.</param>
    /// <param name="measure">The measured path.</param>
    /// <param name="offset">The <c>startOffset</c> in user units.</param>
    /// <param name="reverse">Whether <c>side="right"</c> reverses the path.</param>
    /// <param name="vertical">Whether the text is vertical, so that the inline axis is y and the cross axis x.</param>
    /// <param name="inlineAngle">The angle of the inline axis of the line in degrees: 0, 90, or -90.</param>
    /// <param name="inlineSign">The sign of the inline position along the inline axis: -1 for <c>sideways-lr</c>.</param>
    /// <param name="rotate">The angle of the <c>rotate</c> attribute of the cluster, in degrees.</param>
    private static void PlaceCluster(
        ref SvgTextCluster cluster,
        SvgTextPathMeasure measure,
        float offset,
        bool reverse,
        bool vertical,
        float inlineAngle,
        float inlineSign,
        float rotate)
    {
        float inlinePosition = (vertical ? cluster.Y : cluster.X) * inlineSign;
        float cross = vertical ? cluster.X : cluster.Y;
        float advance = MathF.Abs(cluster.Width * cluster.ScaleX);

        // mid: the distance along the path of the middle of the advance. The comparison is false for NaN, which a
        // hostile offset can give, so such a cluster is left out too.
        float length = measure.Length;
        float mid = inlinePosition + (advance / 2F) + offset;
        if (!(mid >= 0F && mid <= length))
        {
            cluster.Skipped = true;
            return;
        }

        float tangentAngle = measure.GetPoint(reverse ? length - mid : mid, out Vector2 point);
        if (reverse)
        {
            tangentAngle += 180F;
        }

        // The rotation that maps the inline axis of the line onto the tangent also maps the cross axis onto the normal:
        // t + 90 degrees for horizontal text, t - 90 degrees for vertical text, as SVG 2 defines.
        float turn = tangentAngle - inlineAngle;
        float turnRadians = turn * (MathF.PI / 180F);
        Vector2 tangent = new(MathF.Cos(tangentAngle * (MathF.PI / 180F)), MathF.Sin(tangentAngle * (MathF.PI / 180F)));
        Vector2 crossAxis = vertical ? Vector2.UnitX : Vector2.UnitY;
        Vector2 normal = Vector2.TransformNormal(crossAxis, Matrix3x2.CreateRotation(turnRadians));

        // The pen lies half the advance back from the middle along the tangent, shifted across the path by the cross
        // offset. The rotate angle turns both about the point on the path, as in the Chromium oracle.
        Vector2 local = (-advance / 2F * tangent) + (cross * normal);
        if (rotate != 0F)
        {
            local = Vector2.TransformNormal(local, Matrix3x2.CreateRotation(rotate * (MathF.PI / 180F)));
        }

        Vector2 pen = point + local;
        cluster.X = pen.X;
        cluster.Y = pen.Y;
        cluster.Rotation += turn;
    }

    /// <summary>
    /// Returns the measured path of a <c>textPath</c> in the user space of the <c>text</c> element: the geometry of its
    /// <c>path</c> attribute when that has a point, otherwise that of the <c>path</c> element that <c>href</c>
    /// references, under the transform of that element.
    /// </summary>
    /// <remarks>
    /// The measures are cached for the load by the element that holds the geometry, the tolerance, and the viewport,
    /// because many <c>textPath</c> elements, and every <c>use</c> instance of a text, can share one path.
    /// </remarks>
    /// <param name="layout">The layout, for the viewport.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="textPath">The element index of the <c>textPath</c>.</param>
    /// <param name="attributes">The attributes of the <c>textPath</c>.</param>
    /// <param name="fontSize">The font size of the <c>textPath</c>, which sets the flattening tolerance.</param>
    /// <returns>The measure, or <see langword="null"/> when the <c>textPath</c> has no path.</returns>
    private static SvgTextPathMeasure? GetMeasure(SvgTextLayout layout, SvgResolver resolver, int textPath, in SvgTextPathAttributes attributes, float fontSize)
    {
        float tolerance = fontSize * ToleranceOfFontSize;
        if (attributes.Path is IPath own
            && GetMeasure(layout, resolver, textPath, own, Matrix3x2.Identity, tolerance) is SvgTextPathMeasure measure)
        {
            return measure;
        }

        SvgElementTree tree = resolver.Tree;
        if (attributes.Href is not string href
            || !tree.TryGetElementById(href, out int target)
            || tree[target].Kind != SvgElementKind.Path
            || tree.GetPath(target).Path is not IPath path)
        {
            return null;
        }

        // Only the transform of the path element itself applies. Its transform-origin needs the computed style, which
        // costs a walk over the ancestors, so the style is computed only for a path that has a transform.
        Matrix3x2 transform = Matrix3x2.Identity;
        if (tree.TryGetTransform(target, out _))
        {
            SvgComputedStyle style = resolver.ComputeDocumentStyle(target);
            if (resolver.TryGetLocalTransform(target, in style, layout.Viewport, out Matrix3x2 local))
            {
                transform = local;
            }
        }

        return GetMeasure(layout, resolver, target, path, transform, tolerance);
    }

    /// <summary>
    /// Returns the cached measure of a path, measuring it on first use.
    /// </summary>
    /// <param name="layout">The layout, for the viewport.</param>
    /// <param name="resolver">The resolver, which holds the cache.</param>
    /// <param name="element">The element that holds the geometry, the key of the cache.</param>
    /// <param name="path">The geometry.</param>
    /// <param name="transform">The transform of the geometry into the user space of the text.</param>
    /// <param name="tolerance">The flattening tolerance.</param>
    /// <returns>The measure, or <see langword="null"/> when the path has no point.</returns>
    private static SvgTextPathMeasure? GetMeasure(SvgTextLayout layout, SvgResolver resolver, int element, IPath path, Matrix3x2 transform, float tolerance)
    {
        Dictionary<(int Element, float Tolerance, SizeF Viewport), SvgTextPathMeasure?> cache = resolver.TextState.PathMeasures;
        (int, float, SizeF) key = (element, tolerance, layout.Viewport);
        if (!cache.TryGetValue(key, out SvgTextPathMeasure? measure))
        {
            measure = SvgTextPathMeasure.Create(path, transform, tolerance);
            cache[key] = measure;
        }

        return measure;
    }
}
