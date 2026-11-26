// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 5 of the text layout pipeline: shifts each text chunk for <c>text-anchor</c>.
/// </summary>
/// <remarks>
/// A text chunk starts at a cluster with <see cref="SvgTextCluster.StartsChunk"/> and runs to the next such cluster. Its
/// extent is the smallest pen position of its clusters to the largest pen position plus advance, which includes
/// <c>letter-spacing</c> and the shifts inside the chunk. The anchor of the chunk is the <c>text-anchor</c> of its first
/// cluster: <c>middle</c> moves the chunk left by half its extent and <c>end</c> by its whole extent. Clusters that stage
/// 6 leaves out still count, as they keep their space until package T2 places them. Package T4 swaps start and end for
/// right-to-left chunks in the hook <see cref="ApplyDirection"/>, and shifts vertical chunks along y.
/// </remarks>
internal static partial class SvgTextAnchoring
{
    /// <summary>
    /// Hook of package T4: sets the sign of the inline direction of a chunk, -1 for a right-to-left chunk, so that
    /// <c>start</c> and <c>end</c> follow the direction. Without an implementation every chunk runs left to right.
    /// </summary>
    /// <param name="style">The computed style of the first cluster of the chunk.</param>
    /// <param name="direction">The sign, 1 on entry.</param>
    static partial void ApplyDirection(in SvgComputedStyle style, ref float direction);

    /// <summary>
    /// Shifts the chunks of a layout.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    public static void Apply(SvgTextLayout layout)
    {
        Span<SvgTextCluster> clusters = layout.Clusters;
        int start = 0;
        while (start < clusters.Length)
        {
            int end = start + 1;
            while (end < clusters.Length && !clusters[end].StartsChunk)
            {
                end++;
            }

            ApplyChunk(layout, clusters[start..end]);
            start = end;
        }
    }

    /// <summary>
    /// Shifts one chunk.
    /// </summary>
    /// <param name="layout">The layout, for the styles of the spans.</param>
    /// <param name="chunk">The clusters of the chunk.</param>
    private static void ApplyChunk(SvgTextLayout layout, Span<SvgTextCluster> chunk)
    {
        SvgComputedStyle style = layout.Content.Spans[chunk[0].Span].Style;
        SvgTextAnchor anchor = style.Text.TextAnchor;
        float direction = 1F;
        ApplyDirection(in style, ref direction);
        if (anchor == SvgTextAnchor.Start)
        {
            return;
        }

        bool handled = false;
        ApplyVerticalAnchor(layout, chunk, anchor, ref handled);
        if (handled)
        {
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (ref readonly SvgTextCluster cluster in chunk)
        {
            min = MathF.Min(min, cluster.X);
            max = MathF.Max(max, cluster.X + (cluster.Width * cluster.ScaleX));
        }

        float extent = max - min;
        float shift = anchor == SvgTextAnchor.Middle ? -extent / 2F : -extent;
        shift *= direction;
        foreach (ref SvgTextCluster cluster in chunk)
        {
            cluster.X += shift;
        }
    }

    /// <summary>
    /// Hook of package T4: shifts a chunk of vertical text along y for <c>middle</c> and <c>end</c>, and sets
    /// <paramref name="handled"/>. Without an implementation every chunk shifts along x.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="chunk">The clusters of the chunk.</param>
    /// <param name="anchor">The anchor of the chunk, <c>middle</c> or <c>end</c>.</param>
    /// <param name="handled">Whether the hook shifted the chunk; <see langword="false"/> on entry.</param>
    static partial void ApplyVerticalAnchor(SvgTextLayout layout, Span<SvgTextCluster> chunk, SvgTextAnchor anchor, ref bool handled);
}
