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
/// The steps for single features are partial methods in files of their own: <see cref="ApplyBaselineShift"/>
/// (<c>SvgTextPositioner.Baselines.cs</c>), <see cref="ApplyVerticalAdvance"/> (<c>SvgTextPositioner.Vertical.cs</c>),
/// <see cref="ReorderClusters"/> (<c>SvgTextPositioner.Bidi.cs</c>), and <see cref="ApplyTextLength"/>
/// (<c>SvgTextPositioner.LengthAdjust.cs</c>), which runs after the walk and before text anchoring.
/// </para>
/// <para>
/// Inside a <c>textPath</c> the walk lays the text out on a straight line that starts at (0, 0), and stage 6
/// (<see cref="SvgTextPaths"/>) maps that line onto the path: the first cluster of a <c>textPath</c> and the first
/// cluster after one start text chunks, and absolute positions across the line do not apply on a path.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <summary>
    /// Adds the offset of a cluster from the pen position across the line, from <c>dominant-baseline</c>,
    /// <c>alignment-baseline</c>, and <c>baseline-shift</c> (cumulative through the spans), positive toward the bottom of
    /// the glyphs. It moves the glyphs, not the pen. <see cref="Position"/> turns it into a move along y in horizontal
    /// text and along x in vertical text.
    /// </summary>
    /// <param name="layout">The layout, with the spans in <see cref="SvgTextContent.Spans"/>.</param>
    /// <param name="cluster">The cluster, with its span, its font, and its orientation set.</param>
    /// <param name="shift">The offset in user units, 0 on entry.</param>
    static partial void ApplyBaselineShift(SvgTextLayout layout, in SvgTextCluster cluster, ref float shift);

    /// <summary>
    /// For a cluster of a vertical writing mode, sets <paramref name="vertical"/>, the orientation of the cluster, and
    /// its advance along y; for a cluster of a cursive script, removes the letter spacing from the advance.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="cluster">The cluster.</param>
    /// <param name="vertical">Whether the pen advances downward; <see langword="false"/> on entry.</param>
    /// <param name="advance">The advance of the cluster, with spacing; the horizontal advance on entry.</param>
    static partial void ApplyVerticalAdvance(SvgTextLayout layout, ref SvgTextCluster cluster, ref bool vertical, ref float advance);

    /// <summary>
    /// Reorders the visual positions of right-to-left runs with the Unicode bidirectional algorithm. The clusters stay
    /// in logical order in the list; only their positions change, so position lists keep applying in logical order.
    /// </summary>
    /// <param name="layout">The positioned layout.</param>
    static partial void ReorderClusters(SvgTextLayout layout);

    /// <summary>
    /// Adjusts the clusters of every element with <c>textLength</c>, the spacing for <c>lengthAdjust="spacing"</c> and
    /// also <see cref="SvgTextCluster.ScaleX"/> for <c>spacingAndGlyphs</c>.
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
        bool verticalFlow = content.Directions?.IsVertical == true;
        float penX = 0F;
        float penY = 0F;

        for (int i = 0; i < clusters.Length; i++)
        {
            ref SvgTextCluster cluster = ref clusters[i];
            ref readonly SvgTextCharacter first = ref characters[cluster.CharStart];
            SvgTextSpan span = content.Spans[cluster.Span];
            SvgComputedStyle style = span.Style;

            // Text on a path (SVG 2 "resolve character positioning"): a textPath starts a text chunk, and inside it the
            // absolute position across the line (y in horizontal text, x in vertical text) is ignored. As in Chromium,
            // the pen restarts at (0, 0) on the path, so the text before the path moves neither the start along the
            // path nor the offset across it, and the text after a path starts a chunk of its own, which text anchoring
            // measures without the path text. Stage 6 maps these line positions onto the path.
            int textPath = span.TextPath;
            bool startsChunk = i == 0;
            if (i > 0 && textPath != content.Spans[clusters[i - 1].Span].TextPath)
            {
                startsChunk = true;
                if (textPath >= 0)
                {
                    penX = 0F;
                    penY = 0F;
                }
            }

            if ((first.Flags & SvgTextCharacterFlags.HasX) != 0 && !(textPath >= 0 && verticalFlow))
            {
                penX = first.X;
                startsChunk = true;
            }

            if ((first.Flags & SvgTextCharacterFlags.HasY) != 0 && !(textPath >= 0 && !verticalFlow))
            {
                penY = first.Y;
                startsChunk = true;
            }

            penX += first.Dx;
            penY += first.Dy;

            cluster.StartsChunk = startsChunk;
            cluster.X = penX;
            cluster.Y = penY;
            cluster.Rotation = float.IsNaN(first.Rotate) ? 0F : first.Rotate;
            cluster.Hidden = style.Visibility != SvgVisibility.Visible;

            float width = cluster.Advance + GetSpacing(content.Text.AsSpan(cluster.CharStart, cluster.CharEnd - cluster.CharStart), in style);
            bool vertical = false;
            ApplyVerticalAdvance(layout, ref cluster, ref vertical, ref width);
            cluster.Width = width;

            // The baseline offset moves the glyphs across the line, toward the bottom of the glyphs: down in horizontal
            // text; in vertical text left, where glyphs turned clockwise have their bottom, and right in sideways-lr,
            // whose glyphs turn counterclockwise.
            float shift = 0F;
            ApplyBaselineShift(layout, in cluster, ref shift);
            if (vertical)
            {
                cluster.X += layout.Content.Directions!.WritingMode == SvgWritingMode.SidewaysLr ? shift : -shift;
                penY += width;
            }
            else
            {
                cluster.Y += shift;
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
