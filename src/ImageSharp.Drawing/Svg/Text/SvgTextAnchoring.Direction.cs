// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Package T4 in stage 5: <c>text-anchor</c> for right-to-left and vertical chunks.
/// </summary>
/// <remarks>
/// A right-to-left chunk runs leftward from its start position (<see cref="SvgTextPositioner"/>), so <c>start</c> keeps
/// it there and <c>end</c> moves it right by its extent. A vertical chunk runs downward, or upward for
/// <c>sideways-lr</c>, and shifts along y by the extent of its clusters along y.
/// </remarks>
internal static partial class SvgTextAnchoring
{
    /// <inheritdoc cref="ApplyDirection"/>
    static partial void ApplyDirection(in SvgComputedStyle style, ref float direction)
    {
        if (style.Text.Direction == SvgDirection.Rtl)
        {
            direction = -1F;
        }
    }

    /// <inheritdoc cref="ApplyVerticalAnchor"/>
    static partial void ApplyVerticalAnchor(SvgTextLayout layout, Span<SvgTextCluster> chunk, SvgTextAnchor anchor, ref bool handled)
    {
        SvgTextDirections? directions = layout.Content.Directions;
        if (directions is null || !directions.IsVertical)
        {
            return;
        }

        handled = true;
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (ref readonly SvgTextCluster cluster in chunk)
        {
            // The advance of sideways-lr text is negative, so a cluster spans from its pen position upward.
            float end = cluster.Y + cluster.Width;
            min = MathF.Min(min, MathF.Min(cluster.Y, end));
            max = MathF.Max(max, MathF.Max(cluster.Y, end));
        }

        float extent = max - min;
        float shift = anchor == SvgTextAnchor.Middle ? -extent / 2F : -extent;
        if (directions.WritingMode == SvgWritingMode.SidewaysLr)
        {
            shift = -shift;
        }

        foreach (ref SvgTextCluster cluster in chunk)
        {
            cluster.Y += shift;
        }
    }
}
