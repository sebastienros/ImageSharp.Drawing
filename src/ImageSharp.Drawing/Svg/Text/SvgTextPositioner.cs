// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 4 of the text layout pipeline: walks the clusters in logical order and sets their pen positions from the
/// absolute positions, the shifts, the rotations, and the spacing properties.
/// </summary>
/// <remarks>
/// <para>
/// The pen starts at (0, 0). For each cluster, the position values of its first character apply: an <c>x</c> or
/// <c>y</c> value sets the pen and starts a new text chunk; <c>dx</c> and <c>dy</c> move the pen; <c>rotate</c> turns
/// the glyphs about the pen position. Values on the other characters of the cluster are ignored. After the cluster the
/// pen advances by the glyph advances plus <c>letter-spacing</c>, and plus <c>word-spacing</c> after a word separator,
/// as CSS Text Level 3 defines and Chromium applies them: after every typographic character, the last one included.
/// </para>
/// <para>
/// Hooks of later packages are partial methods: a package implements its hook in a file of its own, and shared code
/// does not change. Package T3 implements <see cref="ApplyBaselineShift"/>; package T4 implements
/// <see cref="ApplyVerticalAdvance"/> and <see cref="ReorderClusters"/>; package T2 implements
/// <see cref="ApplyTextLength"/>, which runs after the walk and before text anchoring.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <summary>
    /// Hook of package T3: adds the vertical offset of a cluster from the pen position, from <c>dominant-baseline</c>,
    /// <c>alignment-baseline</c>, and <c>baseline-shift</c> (cumulative through the spans), positive downward. It moves
    /// the glyphs, not the pen. Without an implementation every cluster sits on the alphabetic baseline.
    /// </summary>
    /// <param name="layout">The layout, with the spans in <see cref="SvgTextContent.Spans"/>.</param>
    /// <param name="cluster">The cluster, with its span and font.</param>
    /// <param name="shift">The offset in user units, 0 on entry.</param>
    static partial void ApplyBaselineShift(SvgTextLayout layout, in SvgTextCluster cluster, ref float shift);

    /// <summary>
    /// Hook of package T4: for a cluster of a vertical writing mode, sets <paramref name="vertical"/> and the advance of
    /// the cluster along y. Without an implementation all text is horizontal.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="cluster">The cluster.</param>
    /// <param name="vertical">Whether the pen advances downward; <see langword="false"/> on entry.</param>
    /// <param name="advance">The advance of the cluster, with spacing; the horizontal advance on entry.</param>
    static partial void ApplyVerticalAdvance(SvgTextLayout layout, ref SvgTextCluster cluster, ref bool vertical, ref float advance);

    /// <summary>
    /// Hook of package T4: reorders the visual positions of right-to-left runs with the Unicode bidirectional algorithm.
    /// The clusters stay in logical order in the list; only their positions change, so position lists keep applying in
    /// logical order. Without an implementation all text is left to right.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    static partial void ReorderClusters(SvgTextLayout layout);

    /// <summary>
    /// Hook of package T2: adjusts the clusters of every element with <c>textLength</c>, the spacing for
    /// <c>lengthAdjust="spacing"</c> and also <see cref="SvgTextCluster.ScaleX"/> for <c>spacingAndGlyphs</c>. Without an
    /// implementation <c>textLength</c> has no effect.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    static partial void ApplyTextLength(SvgTextLayout layout);

    /// <summary>
    /// Sets the pen positions of the clusters.
    /// </summary>
    /// <param name="layout">The layout, with the characters and the shaped clusters.</param>
    public static void Position(SvgTextLayout layout)
    {
        SvgTextContent content = layout.Content;
        Span<SvgTextCluster> clusters = layout.Clusters;
        SvgTextCharacter[] characters = content.Characters;
        float penX = 0F;
        float penY = 0F;

        for (int i = 0; i < clusters.Length; i++)
        {
            ref SvgTextCluster cluster = ref clusters[i];
            ref readonly SvgTextCharacter first = ref characters[cluster.CharStart];
            SvgTextSpan span = content.Spans[cluster.Span];
            SvgComputedStyle style = span.Style;

            bool startsChunk = i == 0;
            if ((first.Flags & SvgTextCharacterFlags.HasX) != 0)
            {
                penX = first.X;
                startsChunk = true;
            }

            if ((first.Flags & SvgTextCharacterFlags.HasY) != 0)
            {
                penY = first.Y;
                startsChunk = true;
            }

            penX += first.Dx;
            penY += first.Dy;

            float shift = 0F;
            ApplyBaselineShift(layout, in cluster, ref shift);
            cluster.StartsChunk = startsChunk;
            cluster.X = penX;
            cluster.Y = penY + shift;
            cluster.Rotation = float.IsNaN(first.Rotate) ? 0F : first.Rotate;
            cluster.Hidden = style.Visibility != SvgVisibility.Visible;

            float width = cluster.Advance + GetSpacing(content.Text.AsSpan(cluster.CharStart, cluster.CharEnd - cluster.CharStart), in style);
            bool vertical = false;
            ApplyVerticalAdvance(layout, ref cluster, ref vertical, ref width);
            cluster.Width = width;
            if (vertical)
            {
                penY += width;
            }
            else
            {
                penX += width;
            }
        }

        ReorderClusters(layout);
        ApplyTextLength(layout);
    }

    /// <summary>
    /// Returns the spacing after a cluster: <c>letter-spacing</c>, plus <c>word-spacing</c> when the cluster is a word
    /// separator. Percentages refer to the font size.
    /// </summary>
    /// <param name="characters">The characters of the cluster.</param>
    /// <param name="style">The computed style of the cluster.</param>
    /// <returns>The spacing in user units.</returns>
    private static float GetSpacing(ReadOnlySpan<char> characters, in SvgComputedStyle style)
    {
        SvgTextStyle text = style.Text;
        float spacing = Resolve(text.LetterSpacing, style.FontSize);
        if (text.WordSpacing.IsSpecified && characters.Length > 0 && IsWordSeparator(characters))
        {
            spacing += Resolve(text.WordSpacing, style.FontSize);
        }

        return spacing;

        static float Resolve(SvgLength length, float fontSize)
            => !length.IsSpecified ? 0F : length.Unit == SvgLengthUnit.Percent ? length.Value * fontSize / 100F : length.Value;
    }

    /// <summary>
    /// Returns whether a cluster is a word separator of CSS Text Level 3: a space, a no-break space, or one of the word
    /// separators of other scripts.
    /// </summary>
    /// <param name="characters">The characters of the cluster.</param>
    /// <returns><see langword="true"/> if the cluster starts with a word separator; otherwise, <see langword="false"/>.</returns>
    private static bool IsWordSeparator(ReadOnlySpan<char> characters)
    {
        int value = char.IsHighSurrogate(characters[0]) && characters.Length > 1
            ? char.ConvertToUtf32(characters[0], characters[1])
            : characters[0];

        return value is 0x0020 or 0x00A0 or 0x1361 or 0x10100 or 0x10101 or 0x1039F or 0x1091F;
    }
}
