// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 3: the glyph orientation of vertical text, the shaping direction of right-to-left runs, and the
/// visual order of bidirectional text.
/// </summary>
/// <remarks>
/// <para>
/// Vertical text (<c>vertical-rl</c>, <c>vertical-lr</c>, and the SVG 1.1 values that map to them) uses the orientation
/// of CSS <c>text-orientation: mixed</c>, as Chromium does: a grapheme whose first code point has the Unicode
/// <c>Vertical_Orientation</c> value R lies sideways and is shaped horizontally; every other grapheme stands upright and is
/// shaped with <see cref="LayoutMode.VerticalRightLeft"/>, which gives vertical advances and offsets and applies the
/// vertical forms of the font. <c>sideways-rl</c> and <c>sideways-lr</c> turn every grapheme sideways.
/// <c>glyph-orientation-vertical</c> has no effect, as in Chromium.
/// </para>
/// <para>
/// Bidirectional text: SVG reorders each text chunk on its own, as one paragraph in the direction of the <c>text</c>
/// element. SixLabors.Fonts keeps its implementation of the Unicode bidirectional algorithm internal; its public result is
/// the visual glyph order of <see cref="TextShaper.Shape(Font, TextShapingBuffer)"/>. Each chunk is shaped once that way,
/// with the <c>unicode-bidi</c> and <c>direction</c> values of its spans written as the equivalent bidirectional control
/// characters of CSS Writing Modes, and the visual order of its graphemes is read from the glyphs. A grapheme is shaped
/// right to left when its visual neighbour is its logical neighbour in reverse order; a grapheme with no such neighbour is
/// right to left when its first code point is strongly right to left. This misses only the mirroring of a lone bracket
/// between runs of the other direction, because the resolved embedding levels are not public.
/// </para>
/// </remarks>
internal sealed partial class SvgTextShaper
{
    /// <summary>
    /// The direction data, or <see langword="null"/> for horizontal left-to-right text, which needs none.
    /// </summary>
    private SvgTextDirections? directions;

    /// <inheritdoc cref="PrepareDirections"/>
    partial void PrepareDirections()
    {
        SvgTextContent content = this.content;
        SvgTextStyle root = content.Spans[0].Style.Text;
        bool vertical = root.WritingMode != SvgWritingMode.HorizontalTb;
        bool bidi = NeedsBidi(content);
        if (!vertical && !bidi)
        {
            return;
        }

        byte[] flags = new byte[content.Text.Length];
        float[]? centralBaselines = null;
        if (root.WritingMode is SvgWritingMode.VerticalRl or SvgWritingMode.VerticalLr)
        {
            SetOrientation(content.Text, flags);
            centralBaselines = this.GetCentralBaselines();
        }

        int[]? visualOrder = bidi ? this.ResolveBidi(flags) : null;
        this.directions = new SvgTextDirections(root.WritingMode, flags, visualOrder, centralBaselines);
        content.Directions = this.directions;
    }

    /// <inheritdoc cref="LimitRun"/>
    partial void LimitRun(int start, ref int end)
    {
        if (this.directions is null)
        {
            return;
        }

        byte[] flags = this.directions.Flags;
        for (int i = start + 1; i < end; i++)
        {
            if (flags[i] != flags[start])
            {
                end = i;
                return;
            }
        }
    }

    /// <inheritdoc cref="ConfigureDirection"/>
    partial void ConfigureDirection(TextShapingBuffer buffer, SvgTextStyle style, int start)
    {
        if (this.directions is null)
        {
            return;
        }

        byte flags = this.directions.Flags[start];
        if ((flags & SvgTextDirections.RightToLeft) != 0)
        {
            buffer.TextDirection = TextDirection.RightToLeft;
        }

        if ((flags & SvgTextDirections.Upright) != 0)
        {
            buffer.LayoutMode = this.directions.WritingMode == SvgWritingMode.VerticalLr
                ? LayoutMode.VerticalLeftRight
                : LayoutMode.VerticalRightLeft;
        }
    }

    /// <inheritdoc cref="OrderShapedGlyphs"/>
    static partial void OrderShapedGlyphs(TextShapingBuffer buffer, ShapedGlyph[] glyphs)
    {
        if (buffer.TextDirection == TextDirection.RightToLeft)
        {
            // A right-to-left run comes back in visual order. Reversing the whole run and then each grapheme gives the
            // graphemes in logical order with their glyphs in visual order, so the glyph offsets, which refer to the
            // visual order, stay valid from the left edge of each cluster.
            Array.Reverse(glyphs);
            int group = 0;
            while (group < glyphs.Length)
            {
                int end = group + 1;
                while (end < glyphs.Length && glyphs[end].GraphemeIndex == glyphs[group].GraphemeIndex)
                {
                    end++;
                }

                Array.Reverse(glyphs, group, end - group);
                group = end;
            }
        }
    }

    /// <inheritdoc cref="PlaceUprightGlyphs"/>
    partial void PlaceUprightGlyphs(ReadOnlySpan<ShapedGlyph> shaped, int glyphStart, int glyphEnd, int charStart, int first, ref float advance)
    {
        if (this.directions is null || (this.directions.Flags[charStart] & SvgTextDirections.Upright) == 0)
        {
            return;
        }

        // Shaping is y-up: an upright glyph advances downward by -AdvanceHeight and has no horizontal advance. The cluster
        // places its glyphs along x by their advances (GetGlyphTransform), so the vertical advance becomes the advance of
        // each glyph, and each offset subtracts the advances before it along x and adds them along y.
        Span<SvgShapedGlyph> glyphs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.glyphs);
        float before = 0F;
        for (int i = glyphStart; i < glyphEnd; i++)
        {
            ShapedGlyph glyph = shaped[i];
            ref SvgShapedGlyph target = ref glyphs[first + i - glyphStart];
            target.Advance = -glyph.AdvanceHeight;
            target.Offset = new Vector2(glyph.Offset.X - before, before - glyph.Offset.Y);
            before -= glyph.AdvanceHeight;
        }

        advance = before;
    }

    /// <inheritdoc cref="KeepCursiveLigatures"/>
    partial void KeepCursiveLigatures(int start, int end, ref bool breakLigatures)
    {
        if (breakLigatures && SvgTextDirections.IsCursive(this.content.Text.AsSpan(start, end - start)))
        {
            breakLigatures = false;
        }
    }

    /// <summary>
    /// Returns the central baseline of every span: half the sum of the ascent and the (negative) descent of the primary
    /// font of the span, at its font size. A span without a font gets 0.
    /// </summary>
    /// <returns>The height of the central baseline above the alphabetic baseline per span.</returns>
    private float[] GetCentralBaselines()
    {
        SvgTextSpan[] spans = this.content.Spans;
        float[] baselines = new float[spans.Length];
        for (int i = 0; i < spans.Length; i++)
        {
            SvgComputedStyle style = spans[i].Style;
            SvgFontFace? face = this.matcher.GetChain(style.Text).Primary;
            if (face is null || !(style.FontSize > 0F))
            {
                continue;
            }

            FontMetrics metrics = this.matcher.GetFont(face, style.FontSize, style.Text.FontWeight, style.Text.FontStyle).FontMetrics;
            baselines[i] = (metrics.HorizontalMetrics.Ascender + metrics.HorizontalMetrics.Descender) * style.FontSize / metrics.UnitsPerEm / 2F;
        }

        return baselines;
    }

    /// <summary>
    /// Returns whether the characters of a <c>text</c> element need the bidirectional algorithm: the <c>text</c> element
    /// is right to left or sets <c>unicode-bidi</c>, a span sets <c>unicode-bidi</c>, or a character is strongly right to
    /// left, an Arabic number, or an explicit directional formatting character.
    /// </summary>
    /// <param name="content">The characters.</param>
    /// <returns><see langword="true"/> if the visual order may differ from the logical order; otherwise, <see langword="false"/>.</returns>
    private static bool NeedsBidi(SvgTextContent content)
    {
        foreach (SvgTextSpan span in content.Spans)
        {
            if (span.Style.Text.UnicodeBidi != SvgUnicodeBidi.Normal)
            {
                return true;
            }
        }

        if (content.Spans[0].Style.Text.Direction == SvgDirection.Rtl)
        {
            return true;
        }

        ReadOnlySpan<char> text = content.Text;
        for (int i = 0; i < text.Length; i++)
        {
            // Every code point below the Hebrew block is left to right or neutral.
            if (text[i] < '\u0590')
            {
                continue;
            }

            CodePoint codePoint = DecodeCodePoint(text, i);
            i += codePoint.Utf16SequenceLength - 1;
            if (CodePoint.GetBidiClass(codePoint).CharacterType is BidiCharacterType.RightToLeft
                or BidiCharacterType.ArabicLetter
                or BidiCharacterType.ArabicNumber
                or BidiCharacterType.LeftToRightEmbedding
                or BidiCharacterType.LeftToRightOverride
                or BidiCharacterType.RightToLeftEmbedding
                or BidiCharacterType.RightToLeftOverride
                or BidiCharacterType.LeftToRightIsolate
                or BidiCharacterType.RightToLeftIsolate
                or BidiCharacterType.FirstStrongIsolate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sets <see cref="SvgTextDirections.Upright"/> on the graphemes of mixed vertical text whose first code point does not
    /// have the <c>Vertical_Orientation</c> value R.
    /// </summary>
    /// <param name="text">The characters.</param>
    /// <param name="flags">The flags per code unit.</param>
    private static void SetOrientation(string text, byte[] flags)
    {
        int position = 0;
        while (position < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text, position);
            CodePoint codePoint = DecodeCodePoint(text, position);
            if (CodePoint.GetVerticalOrientationType(codePoint) != VerticalOrientationType.Rotate)
            {
                flags.AsSpan(position, length).Fill(SvgTextDirections.Upright);
            }

            position += length;
        }
    }

    /// <summary>
    /// Resolves the visual order of every text chunk and sets <see cref="SvgTextDirections.RightToLeft"/> on the graphemes
    /// of right-to-left runs.
    /// </summary>
    /// <param name="flags">The flags per code unit.</param>
    /// <returns>The visual position per code unit, or <see langword="null"/> when no font exists.</returns>
    private int[]? ResolveBidi(byte[] flags)
    {
        SvgTextContent content = this.content;
        SvgComputedStyle rootStyle = content.Spans[0].Style;
        SvgTextStyle root = rootStyle.Text;
        SvgFontFace? face = this.matcher.GetChain(root).Primary;
        if (face is null)
        {
            return null;
        }

        // The order does not depend on the size, and a font needs a positive one.
        Font font = this.matcher.GetFont(face, rootStyle.FontSize > 0F ? rootStyle.FontSize : 16F, root.FontWeight, root.FontStyle);
        TextDirection direction = root.UnicodeBidi == SvgUnicodeBidi.Plaintext
            ? TextDirection.Auto
            : root.Direction == SvgDirection.Rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight;

        int[] visualOrder = new int[content.Text.Length];
        BidiScratch scratch = new();
        int start = 0;
        while (start < content.Text.Length)
        {
            // A text chunk starts at an absolute position, as in stage 4.
            int end = start + 1;
            while (end < content.Text.Length && (content.Characters[end].Flags & (SvgTextCharacterFlags.HasX | SvgTextCharacterFlags.HasY)) == 0)
            {
                end++;
            }

            this.ResolveChunk(font, direction, start, end, scratch, flags, visualOrder);
            start = end;
        }

        return visualOrder;
    }

    /// <summary>
    /// Resolves the visual order of one text chunk.
    /// </summary>
    /// <param name="font">The font to shape with; the order does not depend on it.</param>
    /// <param name="direction">The paragraph direction.</param>
    /// <param name="start">The first character of the chunk.</param>
    /// <param name="end">The character after the chunk.</param>
    /// <param name="scratch">The reusable buffers.</param>
    /// <param name="flags">The flags per code unit.</param>
    /// <param name="visualOrder">The visual position per code unit.</param>
    private void ResolveChunk(
        Font font,
        TextDirection direction,
        int start,
        int end,
        BidiScratch scratch,
        byte[] flags,
        int[] visualOrder)
    {
        SvgTextContent content = this.content;
        string text = content.Text;
        this.BuildBidiLine(start, end, scratch);
        List<int> map = scratch.Map;
        List<int> graphemes = scratch.Graphemes;
        List<int> starts = scratch.Starts;
        List<int> sequence = scratch.Sequence;

        TextShapingBuffer buffer = this.buffer;
        buffer.Add(scratch.Line.ToString());
        buffer.TextDirection = direction;
        buffer.LayoutMode = LayoutMode.HorizontalTopBottom;
        buffer.KerningMode = KerningMode.None;
        TextShaper.Shape(font, buffer);

        // The graphemes of the chunk, by character.
        graphemes.Clear();
        starts.Clear();
        int count = 0;
        for (int position = start; position < end;)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(position, end - position));
            starts.Add(position);
            for (int i = 0; i < length; i++)
            {
                graphemes.Add(count);
            }

            count++;
            position += length;
        }

        // The visual position of each grapheme is the order in which its first glyph appears. A grapheme without a glyph
        // of its own, such as the second grapheme of a ligature, comes later.
        Span<int> rank = count <= 256 ? stackalloc int[count] : new int[count];
        rank.Fill(-1);
        sequence.Clear();
        foreach (ShapedGlyph glyph in buffer.Glyphs)
        {
            int character = map[glyph.StringIndex];
            if (character < 0)
            {
                continue;
            }

            int grapheme = graphemes[character - start];
            if (rank[grapheme] < 0)
            {
                rank[grapheme] = sequence.Count;
                sequence.Add(grapheme);
            }
        }

        // The ordinal of each grapheme that has a glyph among those graphemes in logical order. Two graphemes are logical
        // neighbours when their ordinals differ by one.
        Span<int> ordinal = count <= 256 ? stackalloc int[count] : new int[count];
        int next = 0;
        for (int g = 0; g < count; g++)
        {
            ordinal[g] = rank[g] >= 0 ? next++ : -1;
        }

        Span<bool> rightToLeft = count <= 256 ? stackalloc bool[count] : new bool[count];
        rightToLeft.Clear();
        for (int p = 0; p < sequence.Count; p++)
        {
            int g = sequence[p];
            int previous = p > 0 ? ordinal[sequence[p - 1]] : int.MinValue;
            int following = p + 1 < sequence.Count ? ordinal[sequence[p + 1]] : int.MinValue;
            if (previous == ordinal[g] + 1 || following == ordinal[g] - 1)
            {
                rightToLeft[g] = true;
            }
            else if (previous != ordinal[g] - 1 && following != ordinal[g] + 1)
            {
                // A lone grapheme: its direction only matters for shaping, so its own strong direction decides.
                BidiCharacterType type = CodePoint.GetBidiClass(DecodeCodePoint(text, starts[g])).CharacterType;
                rightToLeft[g] = type is BidiCharacterType.RightToLeft or BidiCharacterType.ArabicLetter;
            }
        }

        // A grapheme without a glyph belongs to the cluster of its logical predecessor and takes its values.
        for (int g = 0; g < count; g++)
        {
            if (rank[g] < 0)
            {
                rank[g] = g > 0 ? rank[g - 1] : sequence.Count;
                rightToLeft[g] = g > 0 && rightToLeft[g - 1];
            }
        }

        for (int i = start; i < end; i++)
        {
            int g = graphemes[i - start];
            visualOrder[i] = rank[g];
            if (rightToLeft[g])
            {
                flags[i] |= SvgTextDirections.RightToLeft;
            }
        }
    }

    /// <summary>
    /// Writes the characters of a text chunk with the bidirectional control characters that CSS Writing Modes gives as
    /// the equivalent of <c>unicode-bidi</c> and <c>direction</c> on the spans, and maps each code unit to its character.
    /// </summary>
    /// <param name="start">The first character of the chunk.</param>
    /// <param name="end">The character after the chunk.</param>
    /// <param name="scratch">The buffers: the line receives the characters, and the map the character index of each
    /// written code unit, or -1 for a control character.</param>
    private void BuildBidiLine(int start, int end, BidiScratch scratch)
    {
        SvgTextContent content = this.content;
        SvgTextSpan[] spans = content.Spans;
        StringBuilder line = scratch.Line;
        List<int> map = scratch.Map;
        List<int> open = scratch.Open;
        List<int> chain = scratch.Chain;
        _ = line.Clear();
        map.Clear();
        open.Clear();

        // On the text element, which is the block container, only the overriding values apply to its content.
        SvgTextStyle root = spans[0].Style.Text;
        bool rootOverride = root.UnicodeBidi is SvgUnicodeBidi.BidiOverride or SvgUnicodeBidi.IsolateOverride;
        if (rootOverride)
        {
            Append(line, map, root.Direction == SvgDirection.Rtl ? '\u202E' : '\u202D');
        }

        // The open list holds the spans with a unicode-bidi value that enclose the current character, from the outermost.
        int lastSpan = -1;
        for (int i = start; i < end; i++)
        {
            int span = content.Characters[i].Span;
            if (span != lastSpan)
            {
                lastSpan = span;
                chain.Clear();
                for (int s = span; s > 0; s = spans[s].Parent)
                {
                    if (spans[s].Style.Text.UnicodeBidi != SvgUnicodeBidi.Normal)
                    {
                        chain.Add(s);
                    }
                }

                chain.Reverse();
                int common = 0;
                while (common < open.Count && common < chain.Count && open[common] == chain[common])
                {
                    common++;
                }

                for (int k = open.Count - 1; k >= common; k--)
                {
                    Close(line, map, spans[open[k]].Style.Text.UnicodeBidi);
                }

                open.RemoveRange(common, open.Count - common);
                for (int k = common; k < chain.Count; k++)
                {
                    SvgTextStyle style = spans[chain[k]].Style.Text;
                    Open(line, map, style.UnicodeBidi, style.Direction == SvgDirection.Rtl);
                    open.Add(chain[k]);
                }
            }

            _ = line.Append(content.Text[i]);
            map.Add(i);
        }

        for (int k = open.Count - 1; k >= 0; k--)
        {
            Close(line, map, spans[open[k]].Style.Text.UnicodeBidi);
        }

        if (rootOverride)
        {
            Append(line, map, '\u202C');
        }

        static void Append(StringBuilder line, List<int> map, char control)
        {
            _ = line.Append(control);
            map.Add(-1);
        }

        static void Open(StringBuilder line, List<int> map, SvgUnicodeBidi value, bool rtl)
        {
            switch (value)
            {
                case SvgUnicodeBidi.Embed:
                    Append(line, map, rtl ? '\u202B' : '\u202A');
                    break;
                case SvgUnicodeBidi.BidiOverride:
                    Append(line, map, rtl ? '\u202E' : '\u202D');
                    break;
                case SvgUnicodeBidi.Isolate:
                    Append(line, map, rtl ? '\u2067' : '\u2066');
                    break;
                case SvgUnicodeBidi.IsolateOverride:
                    Append(line, map, rtl ? '\u2067' : '\u2066');
                    Append(line, map, rtl ? '\u202E' : '\u202D');
                    break;
                case SvgUnicodeBidi.Plaintext:
                    Append(line, map, '\u2068');
                    break;
            }
        }

        static void Close(StringBuilder line, List<int> map, SvgUnicodeBidi value)
        {
            switch (value)
            {
                case SvgUnicodeBidi.Embed:
                case SvgUnicodeBidi.BidiOverride:
                    Append(line, map, '\u202C');
                    break;
                case SvgUnicodeBidi.Isolate:
                case SvgUnicodeBidi.Plaintext:
                    Append(line, map, '\u2069');
                    break;
                case SvgUnicodeBidi.IsolateOverride:
                    Append(line, map, '\u202C');
                    Append(line, map, '\u2069');
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the code point that starts at a position, or the replacement character for an unpaired surrogate.
    /// </summary>
    /// <param name="text">The characters.</param>
    /// <param name="index">The position.</param>
    /// <returns>The code point.</returns>
    internal static CodePoint DecodeCodePoint(ReadOnlySpan<char> text, int index)
        => Rune.DecodeFromUtf16(text[index..], out Rune rune, out _) == System.Buffers.OperationStatus.Done
            ? new CodePoint(rune.Value)
            : CodePoint.ReplacementChar;

    /// <summary>
    /// The buffers that the bidirectional resolution of one <c>text</c> element reuses for each of its chunks.
    /// </summary>
    private sealed class BidiScratch
    {
        /// <summary>
        /// Gets the text of the chunk with the bidirectional control characters.
        /// </summary>
        public StringBuilder Line { get; } = new();

        /// <summary>
        /// Gets the character index of each code unit of <see cref="Line"/>, or -1 for a control character.
        /// </summary>
        public List<int> Map { get; } = [];

        /// <summary>
        /// Gets the grapheme of each character of the chunk, counted from the start of the chunk.
        /// </summary>
        public List<int> Graphemes { get; } = [];

        /// <summary>
        /// Gets the first character of each grapheme of the chunk.
        /// </summary>
        public List<int> Starts { get; } = [];

        /// <summary>
        /// Gets the graphemes that have a glyph, in visual order.
        /// </summary>
        public List<int> Sequence { get; } = [];

        /// <summary>
        /// Gets the spans with a <c>unicode-bidi</c> value whose control characters are open, from the outermost.
        /// </summary>
        public List<int> Open { get; } = [];

        /// <summary>
        /// Gets the spans with a <c>unicode-bidi</c> value that enclose a character, from the outermost.
        /// </summary>
        public List<int> Chain { get; } = [];
    }
}
