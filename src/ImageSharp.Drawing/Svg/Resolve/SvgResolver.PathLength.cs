// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>pathLength</c>: the author's length of a path, which scales the distances measured along it.
/// </content>
/// <remarks>
/// <para>
/// SVG 2 scales <c>stroke-dasharray</c> and <c>stroke-dashoffset</c> of <c>path</c> and the basic shapes, and the
/// <c>startOffset</c> of a <c>textPath</c> that references a <c>path</c>, by the computed length of the path divided by
/// <c>pathLength</c>. A negative value is an error and scales nothing. Zero scales by infinity: a zero distance stays
/// zero and every other distance becomes infinite, so a dash pattern that starts with a dash draws a solid stroke, one
/// that starts with a zero dash draws nothing but the caps of that dash, and a non-zero <c>startOffset</c> moves all text
/// off the path. The Chromium oracle draws a solid stroke for every dash pattern at zero; this renderer follows the
/// specification for a pattern that starts with a zero dash. An infinite dash offset has no defined result, and Chromium
/// ignores it with the rest of the pattern, so it is ignored. A value that is not a number is dropped by the reader, as
/// SVG 2 makes an invalid attribute have no effect; the Chromium oracle treats it as zero.
/// </para>
/// <para>
/// The computed length of a shape is the length that the dasher of the library walks: the sum, over the subpaths, of the
/// chords of the curves flattened at scale 1 in user space, including the closing segment of a closed subpath and not
/// the gaps between subpaths. Dashes therefore land where the dasher expects them for the given pattern; the difference
/// to the exact length of a curve is the flattening error of the library.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// A finite stand-in, in user units, for a dash or gap that a <c>pathLength</c> of zero makes infinite. It is longer
    /// than any path the limits allow and keeps the sum of a pattern finite.
    /// </summary>
    private const float InfiniteDashLength = 1E30F;

    /// <summary>
    /// Returns the factor that <c>pathLength</c> applies to the dash pattern and the dash offset of a shape.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="path">The geometry of the shape, as the draw node strokes it.</param>
    /// <param name="style">The computed style of the shape.</param>
    /// <returns>
    /// 1 when the shape has no dash pattern or no valid <c>pathLength</c>; <see cref="float.PositiveInfinity"/> for a
    /// <c>pathLength</c> of zero; otherwise the computed length divided by <c>pathLength</c>.
    /// </returns>
    private float GetDashScale(int index, IPath path, in SvgComputedStyle style)
    {
        if (style.StrokeDashArray.IsNone || !this.tree.TryGetPathLength(index, out float authorLength))
        {
            return 1F;
        }

        return GetPathLengthScale(authorLength, MeasureDashedLength(path));
    }

    /// <summary>
    /// Returns the factor that maps distances in <c>pathLength</c> units to the computed length of a path.
    /// </summary>
    /// <param name="authorLength">The <c>pathLength</c> value.</param>
    /// <param name="length">The computed length of the path.</param>
    /// <returns>
    /// 1 for a negative or non-finite <c>pathLength</c> or a non-finite length; <see cref="float.PositiveInfinity"/> for a
    /// <c>pathLength</c> of zero; otherwise <paramref name="length"/> divided by <paramref name="authorLength"/>, which can
    /// overflow to infinity for a tiny <c>pathLength</c>.
    /// </returns>
    internal static float GetPathLengthScale(float authorLength, float length)
    {
        if (!(authorLength >= 0F) || !float.IsFinite(authorLength) || !float.IsFinite(length))
        {
            return 1F;
        }

        return authorLength == 0F ? float.PositiveInfinity : length / authorLength;
    }

    /// <summary>
    /// Scales a distance along a path by a <c>pathLength</c> factor, keeping a zero distance zero when the factor is
    /// infinite, as SVG 2 requires.
    /// </summary>
    /// <param name="distance">The distance in <c>pathLength</c> units.</param>
    /// <param name="scale">The factor from <see cref="GetPathLengthScale(float, float)"/>.</param>
    /// <returns>The distance in user units; infinite when the factor is infinite and the distance is not zero.</returns>
    internal static float ScalePathDistance(float distance, float scale)
        => distance == 0F ? 0F : distance * scale;

    /// <summary>
    /// Scales one length of a dash pattern by a <c>pathLength</c> factor.
    /// </summary>
    /// <param name="length">The non-negative length in user units.</param>
    /// <param name="scale">The factor from <see cref="GetDashScale(int, IPath, in SvgComputedStyle)"/>.</param>
    /// <returns>The scaled length, at most <see cref="InfiniteDashLength"/>, which stands in for infinity.</returns>
    private static float ScaleDashLength(float length, float scale)
        => MathF.Min(ScalePathDistance(length, scale), InfiniteDashLength);

    /// <summary>
    /// Returns the length of a path as the dasher of the library measures it; see the remarks of the type.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The length in user units.</returns>
    private static float MeasureDashedLength(IPath path)
    {
        float length = 0F;
        foreach (ISimplePath subpath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = subpath.Points.Span;
            for (int i = 1; i < points.Length; i++)
            {
                length += Vector2.Distance(points[i - 1], points[i]);
            }

            if (subpath.IsClosed && points.Length > 1)
            {
                length += Vector2.Distance(points[^1], points[0]);
            }
        }

        return length;
    }
}
