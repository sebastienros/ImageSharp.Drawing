// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stages 2 and 3 of the text layout pipeline: splits the characters into runs of one font, with per-character font
/// fallback, and shapes each run into glyphs grouped into clusters.
/// </summary>
/// <remarks>
/// <para>
/// A shaping run is a maximal range of characters of one span and one text chunk. Chromium shapes each inline box of
/// SVG text on its own and starts a new run at an absolute position, so no kerning, ligature, or contextual form joins
/// characters across a <c>tspan</c> boundary or a chunk boundary (Chromium fails the resvg test
/// <c>text/tspan/text-shaping-across-multiple-tspan-1</c>, which expects the opposite). Within a run, each grapheme takes
/// the first face of its fallback chain that has glyphs for all its code points (default ignorable code points need
/// none), and consecutive graphemes of one face form a segment.
/// </para>
/// <para>
/// Each segment is shaped with <see cref="TextShaper.ShapeRun(Font, TextShapingBuffer, Tag[])"/> as one left-to-right
/// run; the glyphs map to characters through <see cref="ShapedGlyph.StringIndex"/> and
/// <see cref="ShapedGlyph.GraphemeIndex"/>. A cluster that holds several graphemes (a ligature) is shaped again one
/// grapheme at a time when a character after its first grapheme has its own position value, when the cluster crosses a
/// span boundary, or when <c>letter-spacing</c> is not zero, as CSS disables ligatures then.
/// </para>
/// <para>
/// Package T1 shapes every run horizontally, left to right, with the default features and kerning on, as Chromium does
/// for <c>font-kerning: auto</c>. The hooks are partial methods that later packages implement in files of their own:
/// package T4 sets the direction and layout mode in <see cref="ConfigureDirection"/>, and package T3 the kerning and
/// the features in <see cref="ConfigureFeatures"/>.
/// </para>
/// </remarks>
internal sealed partial class SvgTextShaper
{
    private readonly SvgTextContent content;
    private readonly SvgFontMatcher matcher;
    private readonly TextShapingBuffer buffer = new();
    private readonly List<SvgShapedGlyph> glyphs = [];
    private readonly List<SvgTextCluster> clusters = [];
    private readonly List<int> codePoints = [];
    private readonly List<Tag> features = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextShaper"/> class.
    /// </summary>
    /// <param name="content">The characters from stage 1.</param>
    /// <param name="matcher">The font matcher of the load.</param>
    private SvgTextShaper(SvgTextContent content, SvgFontMatcher matcher)
    {
        this.content = content;
        this.matcher = matcher;
    }

    /// <summary>
    /// Shapes the characters of a <c>text</c> element.
    /// </summary>
    /// <param name="content">The characters from stage 1.</param>
    /// <param name="matcher">The font matcher of the load.</param>
    /// <param name="glyphs">When this method returns, contains the glyphs in logical order.</param>
    /// <returns>The clusters in logical order; together they cover every character once.</returns>
    public static SvgTextCluster[] Shape(SvgTextContent content, SvgFontMatcher matcher, out SvgShapedGlyph[] glyphs)
    {
        SvgTextShaper shaper = new(content, matcher);
        int length = content.Text.Length;
        int start = 0;
        while (start < length)
        {
            // A run ends at a span boundary and at an absolute position, which starts a new text chunk.
            int span = content.Characters[start].Span;
            int end = start + 1;
            while (end < length
                && (content.Characters[end].Flags & (SvgTextCharacterFlags.HasX | SvgTextCharacterFlags.HasY)) == 0
                && content.Characters[end].Span == span)
            {
                end++;
            }

            shaper.ShapeRun(start, end, content.Spans[span].Style);
            start = end;
        }

        glyphs = [.. shaper.glyphs];
        return [.. shaper.clusters];
    }

    /// <summary>
    /// Splits a shaping run into font segments and shapes each.
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <param name="end">The character after the run.</param>
    /// <param name="style">The computed style of the run.</param>
    private void ShapeRun(int start, int end, in SvgComputedStyle style)
    {
        SvgTextStyle text = style.Text;
        SvgFontChain chain = this.matcher.GetChain(text);
        ReadOnlySpan<char> chars = this.content.Text;
        if (chain.Primary is null || !(style.FontSize > 0F))
        {
            // Without a font, or at a zero font size, the characters keep their positions but draw nothing.
            this.AddEmptyClusters(start, end);
            return;
        }

        int segmentStart = start;
        SvgFontFace? segmentFace = null;
        int position = start;
        while (position < end)
        {
            int length = StringInfo.GetNextTextElementLength(chars[position..end]);
            SvgFontFace face = this.SelectFace(chain, chars.Slice(position, length)) ?? segmentFace ?? chain.Primary;
            if (segmentFace is not null && face != segmentFace)
            {
                this.ShapeSegment(segmentStart, position, this.matcher.GetFont(segmentFace, style.FontSize, text.FontWeight, text.FontStyle), text);
                segmentStart = position;
            }

            segmentFace = face;
            position += length;
        }

        this.ShapeSegment(segmentStart, end, this.matcher.GetFont(segmentFace!, style.FontSize, text.FontWeight, text.FontStyle), text);
    }

    /// <summary>
    /// Selects the face of a grapheme: the first face of the chain with glyphs for all its code points that need one, else
    /// the first face with a glyph for its first such code point.
    /// </summary>
    /// <param name="chain">The fallback chain.</param>
    /// <param name="grapheme">The characters of the grapheme.</param>
    /// <returns>The face, or <see langword="null"/> when the grapheme needs no glyph or no face has one.</returns>
    private SvgFontFace? SelectFace(SvgFontChain chain, ReadOnlySpan<char> grapheme)
    {
        List<int> points = this.codePoints;
        points.Clear();
        for (int i = 0; i < grapheme.Length; i++)
        {
            int value = grapheme[i];
            if (char.IsHighSurrogate(grapheme[i]) && i + 1 < grapheme.Length && char.IsLowSurrogate(grapheme[i + 1]))
            {
                value = char.ConvertToUtf32(grapheme[i], grapheme[i + 1]);
                i++;
            }

            CodePoint codePoint = new(value);
            if (!NeedsGlyph(codePoint))
            {
                continue;
            }

            points.Add(value);
        }

        if (points.Count == 0)
        {
            return null;
        }

        ReadOnlySpan<int> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(points);
        return chain.GetFace(span) ?? chain.GetFace(span[0]);
    }

    /// <summary>
    /// Returns whether a code point needs a glyph of its own for font selection. Default ignorable code points, such as
    /// joiners and variation selectors, and controls take the face of their grapheme.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> if the face must have a glyph for the code point; otherwise, <see langword="false"/>.</returns>
    private static bool NeedsGlyph(CodePoint codePoint)
        => !(CodePoint.IsZeroWidthJoiner(codePoint)
            || CodePoint.IsZeroWidthNonJoiner(codePoint)
            || CodePoint.IsVariationSelector(codePoint)
            || CodePoint.IsControl(codePoint)
            || codePoint.Value is 0x200B or 0x2060 or 0xFEFF or 0x00AD or (>= 0xE0000 and <= 0xE0FFF));

    /// <summary>
    /// Adds one cluster without glyphs per grapheme of a range.
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <param name="end">The character after the range.</param>
    private void AddEmptyClusters(int start, int end)
    {
        ReadOnlySpan<char> chars = this.content.Text;
        int position = start;
        while (position < end)
        {
            int length = StringInfo.GetNextTextElementLength(chars[position..end]);
            this.clusters.Add(new SvgTextCluster
            {
                CharStart = position,
                CharEnd = position + length,
                GlyphStart = this.glyphs.Count,
                Span = this.content.Characters[position].Span,
                ScaleX = 1F,
            });

            position += length;
        }
    }

    /// <summary>
    /// Shapes one segment of one font and adds its clusters, breaking ligatures where the rules of the remarks require.
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <param name="end">The character after the segment.</param>
    /// <param name="font">The font.</param>
    /// <param name="style">The computed text style of the segment.</param>
    private void ShapeSegment(int start, int end, Font font, SvgTextStyle style)
    {
        if (start >= end)
        {
            return;
        }

        TextShapingBuffer buffer = this.buffer;
        buffer.Add(this.content.Text.AsSpan(start, end - start));
        buffer.TextDirection = TextDirection.LeftToRight;
        buffer.LayoutMode = LayoutMode.HorizontalTopBottom;
        buffer.KerningMode = KerningMode.Standard;
        List<Tag> features = this.features;
        features.Clear();
        ConfigureDirection(buffer, style);
        ConfigureFeatures(buffer, style, features);
        TextShaper.ShapeRun(font, buffer, features.Count == 0 ? [] : [.. features]);

        // Breaking a ligature shapes again with the same buffer, so the glyphs are copied out first.
        ReadOnlySpan<ShapedGlyph> shaped = buffer.Glyphs.ToArray();

        if (shaped.Length == 0)
        {
            this.AddEmptyClusters(start, end);
            return;
        }

        // Group the glyphs into clusters: a new cluster starts at a glyph that maps to characters after every character of
        // the current cluster and to another grapheme. Shaping keeps a left-to-right run in logical order.
        int count = shaped.Length;
        Span<int> clusterStarts = count <= 256 ? stackalloc int[count] : new int[count];
        int clusterCount = 0;
        int maxIndex = -1;
        for (int i = 0; i < count; i++)
        {
            ShapedGlyph glyph = shaped[i];
            if (i == 0 || (glyph.StringIndex > maxIndex && glyph.GraphemeIndex != shaped[i - 1].GraphemeIndex))
            {
                clusterStarts[clusterCount++] = i;
            }

            maxIndex = Math.Max(maxIndex, glyph.StringIndex);
        }

        bool breakLigatures = style.LetterSpacing.IsSpecified && style.LetterSpacing.Value != 0F;
        for (int c = 0; c < clusterCount; c++)
        {
            int glyphStart = clusterStarts[c];
            int glyphEnd = c + 1 < clusterCount ? clusterStarts[c + 1] : count;

            // The cluster covers the characters up to the first character of the next cluster; the first cluster also
            // covers characters before its first glyph.
            int charStart = c == 0 ? start : start + MinIndex(shaped, glyphStart, glyphEnd);
            int charEnd = c + 1 < clusterCount ? start + MinIndex(shaped, glyphEnd, c + 2 < clusterCount ? clusterStarts[c + 2] : count) : end;
            if (charEnd <= charStart)
            {
                continue;
            }

            if (this.NeedsBreaking(charStart, charEnd, breakLigatures))
            {
                // Shape each grapheme on its own. The cluster has several graphemes, so each call shapes less.
                int position = charStart;
                ReadOnlySpan<char> chars = this.content.Text;
                while (position < charEnd)
                {
                    int length = StringInfo.GetNextTextElementLength(chars[position..charEnd]);
                    this.ShapeSegment(position, position + length, font, style);
                    position += length;
                }

                continue;
            }

            this.AddCluster(shaped, glyphStart, glyphEnd, charStart, charEnd, font);
        }
    }

    /// <summary>
    /// Hook of package T4: sets <see cref="TextShapingBuffer.TextDirection"/> and <see cref="TextShapingBuffer.LayoutMode"/>
    /// for a run of a vertical writing mode or of right-to-left text. On entry they are left to right and horizontal.
    /// </summary>
    /// <param name="buffer">The buffer that holds the text of the run.</param>
    /// <param name="style">The computed text style of the run.</param>
    static partial void ConfigureDirection(TextShapingBuffer buffer, SvgTextStyle style);

    /// <summary>
    /// Hook of package T3: sets <see cref="TextShapingBuffer.KerningMode"/> from <c>font-kerning</c> and
    /// <c>kerning</c>, and adds the OpenType features of <c>font-variant</c>. On entry the kerning mode is
    /// <see cref="KerningMode.Standard"/> and the list is empty.
    /// </summary>
    /// <param name="buffer">The buffer that holds the text of the run.</param>
    /// <param name="style">The computed text style of the run.</param>
    /// <param name="features">The features to turn on, in addition to the defaults of the shaper.</param>
    static partial void ConfigureFeatures(TextShapingBuffer buffer, SvgTextStyle style, List<Tag> features);

    /// <summary>
    /// Returns the smallest string index of a range of glyphs.
    /// </summary>
    /// <param name="shaped">The glyphs.</param>
    /// <param name="start">The first glyph.</param>
    /// <param name="end">The glyph after the range.</param>
    /// <returns>The smallest string index.</returns>
    private static int MinIndex(ReadOnlySpan<ShapedGlyph> shaped, int start, int end)
    {
        int min = int.MaxValue;
        for (int i = start; i < end; i++)
        {
            min = Math.Min(min, shaped[i].StringIndex);
        }

        return min;
    }

    /// <summary>
    /// Returns whether a cluster of several graphemes must be shaped again one grapheme at a time.
    /// </summary>
    /// <param name="charStart">The first character of the cluster.</param>
    /// <param name="charEnd">The character after the cluster.</param>
    /// <param name="breakLigatures">Whether <c>letter-spacing</c> disables ligatures.</param>
    /// <returns><see langword="true"/> if the cluster must break; otherwise, <see langword="false"/>.</returns>
    private bool NeedsBreaking(int charStart, int charEnd, bool breakLigatures)
    {
        ReadOnlySpan<char> chars = this.content.Text;
        int first = StringInfo.GetNextTextElementLength(chars[charStart..charEnd]);
        if (charStart + first >= charEnd)
        {
            return false;
        }

        if (breakLigatures)
        {
            return true;
        }

        SvgTextCharacter[] characters = this.content.Characters;
        int span = characters[charStart].Span;
        for (int i = charStart + 1; i < charEnd; i++)
        {
            if (characters[i].Span != span || (i >= charStart + first && characters[i].HasOwnPosition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds one cluster and its glyphs, converting the shaped values to user units with the y axis down.
    /// </summary>
    /// <param name="shaped">The shaped glyphs.</param>
    /// <param name="glyphStart">The first glyph of the cluster.</param>
    /// <param name="glyphEnd">The glyph after the cluster.</param>
    /// <param name="charStart">The first character.</param>
    /// <param name="charEnd">The character after the cluster.</param>
    /// <param name="font">The font of the glyphs.</param>
    private void AddCluster(ReadOnlySpan<ShapedGlyph> shaped, int glyphStart, int glyphEnd, int charStart, int charEnd, Font font)
    {
        // Shaping gives font units scaled to the font size, in a y-up system. The font size is in pixels, so the values are
        // user units; only the y axis turns.
        float advance = 0F;
        int first = this.glyphs.Count;
        for (int i = glyphStart; i < glyphEnd; i++)
        {
            ShapedGlyph glyph = shaped[i];
            this.glyphs.Add(new SvgShapedGlyph
            {
                GlyphId = glyph.GlyphId,
                Font = font,
                Advance = glyph.AdvanceWidth,
                Offset = new Vector2(glyph.Offset.X, -glyph.Offset.Y),
            });

            advance += glyph.AdvanceWidth;
        }

        this.clusters.Add(new SvgTextCluster
        {
            CharStart = charStart,
            CharEnd = charEnd,
            GlyphStart = first,
            GlyphCount = glyphEnd - glyphStart,
            Advance = advance,
            Font = font,
            Span = this.content.Characters[charStart].Span,
            ScaleX = 1F,
        });
    }
}
