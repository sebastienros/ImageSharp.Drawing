// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Package T4 in stage 4: the visual order of bidirectional text and the placement of right-to-left chunks.
/// </summary>
/// <remarks>
/// <para>
/// The walk of stage 4 places the clusters of a chunk in logical order, so each pen position is the start of the chunk,
/// plus the advances of the clusters before it in logical order, plus the shifts (<c>dx</c>, <c>dy</c>) of the
/// characters up to it. SVG 2 applies the shifts in logical order to the positions of the CSS layout, which places the
/// clusters in visual order. This stage therefore replaces, for each cluster, the advances before it in logical order by
/// the advances before it in visual order (<see cref="SvgTextDirections.VisualOrder"/>), and keeps its shift.
/// </para>
/// <para>
/// A chunk of a right-to-left <c>text</c> element runs leftward from its start position: the visual line of the chunk
/// ends there. Each chunk is one bidirectional paragraph, as in Chromium, so text chunks follow each other in logical
/// order. <c>direction</c> in vertical text is undefined behavior in the resvg suite and has no effect here.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <inheritdoc cref="ReorderClusters"/>
    static partial void ReorderClusters(SvgTextLayout layout)
    {
        SvgTextDirections? directions = layout.Content.Directions;
        if (directions?.VisualOrder is not int[] visualOrder)
        {
            return;
        }

        bool vertical = directions.IsVertical;
        bool rightToLeft = !vertical && layout.Content.Spans[0].Style.Text.Direction == SvgDirection.Rtl;
        Span<SvgTextCluster> clusters = layout.Clusters;
        int start = 0;
        while (start < clusters.Length)
        {
            int end = start + 1;
            while (end < clusters.Length && !clusters[end].StartsChunk)
            {
                end++;
            }

            ReorderChunk(clusters[start..end], visualOrder, vertical, rightToLeft);
            start = end;
        }
    }

    /// <summary>
    /// Moves the clusters of one chunk to their visual positions along the inline axis.
    /// </summary>
    /// <param name="chunk">The clusters of the chunk, in logical order.</param>
    /// <param name="visualOrder">The visual position per character.</param>
    /// <param name="vertical">Whether the inline axis is y.</param>
    /// <param name="rightToLeft">Whether the chunk runs leftward from its start position.</param>
    private static void ReorderChunk(Span<SvgTextCluster> chunk, int[] visualOrder, bool vertical, bool rightToLeft)
    {
        int count = chunk.Length;
        if (count == 1)
        {
            // A lone cluster keeps its place, or ends at its start position in a right-to-left chunk.
            if (rightToLeft)
            {
                chunk[0].X -= chunk[0].Width;
            }

            return;
        }

        // The sort key holds the visual position of the first character in the high bits and the logical index in the
        // low bits, so clusters with the same visual position, such as the parts of a grapheme that a span boundary
        // splits, keep their logical order.
        Span<long> keys = count <= 128 ? stackalloc long[count] : new long[count];
        float total = 0F;
        for (int i = 0; i < count; i++)
        {
            keys[i] = ((long)visualOrder[chunk[i].CharStart] << 32) | (uint)i;
            total += chunk[i].Width;
        }

        keys.Sort();

        Span<float> visual = count <= 128 ? stackalloc float[count] : new float[count];
        float advance = rightToLeft ? -total : 0F;
        foreach (long key in keys)
        {
            int i = (int)(key & 0xFFFFFFFF);
            visual[i] = advance;
            advance += chunk[i].Width;
        }

        float logical = 0F;
        for (int i = 0; i < count; i++)
        {
            ref SvgTextCluster cluster = ref chunk[i];
            float delta = visual[i] - logical;
            logical += cluster.Width;
            if (vertical)
            {
                cluster.Y += delta;
            }
            else
            {
                cluster.X += delta;
            }
        }
    }
}
