// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Package T2: <c>textLength</c> and <c>lengthAdjust</c>, and the position rules of <c>textPath</c> that must apply
/// before them and before text anchoring.
/// </content>
/// <remarks>
/// <para>
/// The algorithm is step 5 of the SVG 2 text layout algorithm as Chromium implements it
/// (<c>SvgTextLayoutAlgorithm::ResolveTextLength</c> in Blink; adapted from the Chromium project, BSD 3-Clause License,
/// see THIRD-PARTY-NOTICES), which runs after the <c>dx</c> and <c>dy</c> shifts and
/// before the absolute <c>x</c> and <c>y</c> positions. Every <c>text</c>, <c>tspan</c>, and <c>textPath</c> with a
/// <c>textLength</c> above 0 is an element to resolve; descendants resolve before their ancestors. For an element, the
/// extent of its clusters from the smallest position to the largest position plus advance becomes the
/// <c>textLength</c>:
/// </para>
/// <list type="bullet">
/// <item><description><c>spacing</c>: the difference spreads over the gaps. Each cluster that no descendant resolved, and
/// each resolved descendant as a whole, takes the next share, in visual order.</description></item>
/// <item><description><c>spacingAndGlyphs</c>: the positions scale about the start of the extent, and the glyphs and
/// advances of the clusters that no descendant resolved scale by the same factor.</description></item>
/// </list>
/// <para>
/// The clusters after the element, up to the next text chunk, move by the last shift, as in Chromium; the SVG 2
/// algorithm does not define this. Stage 4 has already applied the absolute positions, so the shifts are found on the
/// positions without them, and then added relative to the last cluster with an absolute inline position, which keeps
/// that position, as the Chromium order gives.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <summary>
    /// Implements the hook of package T2.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    static partial void ApplyTextLength(SvgTextLayout layout)
    {
        bool horizontal = SvgTextPaths.IsHorizontal(layout);
        SvgTextPaths.ResolvePathPositions(layout, horizontal);
        ResolveTextLengths(layout, horizontal);
        SvgTextPaths.SeparatePathChunks(layout);
    }

    /// <summary>
    /// Resolves every element with a valid <c>textLength</c>, as the remarks describe.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    /// <param name="horizontal">Whether the inline direction is horizontal.</param>
    private static void ResolveTextLengths(SvgTextLayout layout, bool horizontal)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        Span<SvgTextCluster> clusters = layout.Clusters;
        List<(int Span, float Length, SvgLengthAdjust Adjust)>? elements = null;
        for (int s = 0; s < spans.Length; s++)
        {
            if (TryGetTextLength(layout, spans[s], out float length, out SvgLengthAdjust adjust))
            {
                (elements ??= []).Add((s, length, adjust));
            }
        }

        if (elements is null || clusters.IsEmpty)
        {
            return;
        }

        // Closing tag order: an element ends at or before its ancestors, and comes after them in document order.
        elements.Sort((a, b) =>
        {
            int order = spans[a.Span].CharEnd.CompareTo(spans[b.Span].CharEnd);
            return order != 0 ? order : b.Span.CompareTo(a.Span);
        });

        int count = clusters.Length;
        float[] positions = new float[count];
        float[] shifts = new float[count];
        float[] recorded = new float[count];
        bool[] resolved = new bool[count];
        int[] anchors = new int[count];
        GetPositionsWithoutAbsolute(layout, horizontal, positions, anchors);

        List<int> resolvedStarts = [];
        bool visualOrder = HasRightToLeft(layout);
        foreach ((int spanIndex, float length, SvgLengthAdjust adjust) in elements)
        {
            SvgTextSpan span = spans[spanIndex];
            int start = FindFirstCluster(clusters, span.CharStart);
            int end = FindFirstCluster(clusters, span.CharEnd);
            if (start >= end)
            {
                continue;
            }

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int k = start; k < end; k++)
            {
                min = MathF.Min(min, positions[k]);
                max = MathF.Max(max, positions[k] + (clusters[k].Width * clusters[k].ScaleX));
            }

            float delta = length - (max - min);
            float shift;
            if (adjust == SvgLengthAdjust.SpacingAndGlyphs)
            {
                float scale = length / (max - min);
                if (!(min < max) || !float.IsFinite(scale))
                {
                    continue;
                }

                for (int k = start; k < end; k++)
                {
                    float moved = min + ((positions[k] - min) * scale);
                    float change = moved - positions[k];
                    positions[k] = moved;
                    shifts[k] += change;
                    recorded[k] += change;
                    if (!resolved[k])
                    {
                        clusters[k].ScaleX = scale;
                    }

                    resolved[k] = true;
                }

                shift = delta;
            }
            else
            {
                // n counts the clusters that no descendant resolved and the resolved descendants; n - 1 gaps take the
                // difference. The last cluster also adds a share to the shift of the clusters after the element, as in
                // Chromium.
                int n = -1;
                for (int k = start; k < end; k++)
                {
                    n += resolved[k] ? 0 : 1;
                }

                foreach (int resolvedStart in resolvedStarts)
                {
                    n += resolvedStart >= start && resolvedStart < end ? 1 : 0;
                }

                float step = n != 0 ? delta / n : 0F;
                shift = 0F;
                int[]? order = visualOrder ? GetVisualOrder(clusters, positions, start, end) : null;
                for (int o = start; o < end; o++)
                {
                    int k = order is null ? o : order[o - start];
                    positions[k] += shift;
                    shifts[k] += shift;
                    recorded[k] += shift;
                    if (!resolved[k] || resolvedStarts.Contains(k))
                    {
                        shift += step;
                    }

                    resolved[k] = true;
                }
            }

            for (int k = end; k < count && !clusters[k].StartsChunk; k++)
            {
                positions[k] += shift;
                shifts[k] += shift;
            }

            _ = resolvedStarts.RemoveAll(k => k >= start && k < end);
            resolvedStarts.Add(start);
        }

        // The absolute position of the anchor cluster wins over its own shift; the clusters after it keep their shifts
        // relative to it.
        for (int k = 0; k < count; k++)
        {
            float change = shifts[k] - recorded[anchors[k]];
            if (change != 0F && float.IsFinite(change))
            {
                ref SvgTextCluster cluster = ref clusters[k];
                if (horizontal)
                {
                    cluster.X += change;
                }
                else
                {
                    cluster.Y += change;
                }
            }
        }
    }

    /// <summary>
    /// Returns the <c>textLength</c> of an element in user units, when it has a valid one.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="span">The span of the element.</param>
    /// <param name="length">The length; percentages refer to the viewport width.</param>
    /// <param name="adjust">The <c>lengthAdjust</c> of the element.</param>
    /// <returns><see langword="true"/> if the length is above 0, as Chromium requires; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetTextLength(SvgTextLayout layout, SvgTextSpan span, out float length, out SvgLengthAdjust adjust)
    {
        SvgElementTree tree = layout.Resolver.Tree;
        SvgLength value;
        switch (span.Kind)
        {
            case SvgElementKind.Text or SvgElementKind.TSpan:
                ref readonly SvgTextAttributes text = ref tree.GetText(span.Element);
                value = text.TextLength;
                adjust = text.LengthAdjust;
                break;
            case SvgElementKind.TextPath:
                ref readonly SvgTextPathAttributes path = ref tree.GetTextPath(span.Element);
                value = path.TextLength;
                adjust = path.LengthAdjust;
                break;
            default:
                length = 0F;
                adjust = default;
                return false;
        }

        length = SvgUnits.ToUser(value, layout.Viewport.Width, span.Style.FontSize, layout.Resolver.Units, 0F);
        return length > 0F && float.IsFinite(length) && span.CharEnd > span.CharStart;
    }

    /// <summary>
    /// Computes the inline position of every cluster without the absolute positions: the advances and the shifts from
    /// the start of the text, as in Chromium before it applies <c>x</c> and <c>y</c>. Also finds the anchor of every
    /// cluster: the last cluster at or before it whose absolute inline position sets the pen.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="horizontal">Whether the inline direction is horizontal.</param>
    /// <param name="positions">Receives the positions.</param>
    /// <param name="anchors">Receives the anchors.</param>
    private static void GetPositionsWithoutAbsolute(SvgTextLayout layout, bool horizontal, float[] positions, int[] anchors)
    {
        ReadOnlySpan<SvgTextCluster> clusters = layout.Clusters;
        SvgTextCharacter[] characters = layout.Content.Characters;
        SvgTextSpan[] spans = layout.Content.Spans;
        SvgTextCharacterFlags inlineFlag = horizontal ? SvgTextCharacterFlags.HasX : SvgTextCharacterFlags.HasY;
        float advance = 0F;
        float offset = 0F;
        int anchor = 0;
        int previousPath = -1;
        for (int k = 0; k < clusters.Length; k++)
        {
            ref readonly SvgTextCluster cluster = ref clusters[k];
            ref readonly SvgTextCharacter first = ref characters[cluster.CharStart];
            int path = spans[cluster.Span].TextPath;

            // The pen is set at an absolute position, and at the start of a path, where Chromium sets it to 0.
            bool hasInline = (first.Flags & inlineFlag) != 0;
            if (hasInline || (path >= 0 && path != previousPath))
            {
                anchor = k;
                offset = (hasInline ? (horizontal ? first.X : first.Y) : 0F) - advance;
            }

            previousPath = path;
            anchors[k] = anchor;
            positions[k] = (horizontal ? cluster.X : cluster.Y) - offset;
            advance += cluster.Width;
        }
    }

    /// <summary>
    /// Returns the index of the first cluster that starts at or after a character.
    /// </summary>
    /// <param name="clusters">The clusters in logical order.</param>
    /// <param name="character">The character index.</param>
    /// <returns>The cluster index, or the number of clusters.</returns>
    private static int FindFirstCluster(ReadOnlySpan<SvgTextCluster> clusters, int character)
    {
        int low = 0;
        int high = clusters.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (clusters[middle].CharStart < character)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Returns the clusters of a range ordered by position, which is their visual order.
    /// </summary>
    /// <param name="clusters">The clusters.</param>
    /// <param name="positions">The positions of the clusters.</param>
    /// <param name="start">The first cluster.</param>
    /// <param name="end">The cluster after the range.</param>
    /// <returns>The cluster indices in visual order.</returns>
    private static int[] GetVisualOrder(ReadOnlySpan<SvgTextCluster> clusters, float[] positions, int start, int end)
    {
        int[] order = new int[end - start];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = start + i;
        }

        Array.Sort(order, (a, b) =>
        {
            int compare = positions[a].CompareTo(positions[b]);
            return compare != 0 ? compare : a.CompareTo(b);
        });

        return order;
    }

    /// <summary>
    /// Returns whether a layout may hold right-to-left text, in which Chromium spreads <c>textLength</c> in visual
    /// order: a span with <c>direction: rtl</c> or a character of a right-to-left script.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns><see langword="true"/> if the text may have right-to-left runs; otherwise, <see langword="false"/>.</returns>
    private static bool HasRightToLeft(SvgTextLayout layout)
    {
        foreach (SvgTextSpan span in layout.Content.Spans)
        {
            if (span.Style.Text.Direction == SvgDirection.Rtl)
            {
                return true;
            }
        }

        // Hebrew, Arabic, Syriac, Thaana, NKo, Samaritan, Mandaic, and their presentation forms; and the
        // right-to-left marks and controls.
        foreach (char c in layout.Content.Text)
        {
            if (c is (>= '֐' and <= 'ࣿ') or (>= 'יִ' and <= '﷿') or (>= 'ﹰ' and <= '﻿')
                or '‏' or '‫' or '‮' or '⁧')
            {
                return true;
            }
        }

        return false;
    }
}
