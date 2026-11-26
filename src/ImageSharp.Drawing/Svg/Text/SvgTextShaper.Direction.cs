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
/// element. The embedding levels come from the Unicode bidirectional algorithm (UAX #9) in <see cref="BidiAlgorithm"/>,
/// a local copy of the internal implementation of SixLabors.Fonts (folder <c>Bidi</c>). The <c>unicode-bidi</c> and
/// <c>direction</c> values of the spans of a chunk enter the algorithm as the equivalent bidirectional control
/// characters of CSS Writing Modes. Each grapheme takes the level of its first code point. A shaping run ends where the
/// level changes, so each run is one level run, and a run at an odd level is shaped right to left. The shaper mirrors
/// the characters of a right-to-left run (rule L4; <c>TextShaper</c> substitutes the Unicode mirror glyph or applies
/// <c>rtlm</c>), so this stage does not mirror them again. The visual order of the graphemes of a chunk comes from the
/// levels by rule L2.
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

        int[]? visualOrder = null;
        sbyte[]? levels = null;
        if (bidi)
        {
            visualOrder = new int[content.Text.Length];
            levels = new sbyte[content.Text.Length];
            ResolveBidi(content, flags, levels, visualOrder);
        }

        this.directions = new SvgTextDirections(root.WritingMode, flags, visualOrder, levels, centralBaselines);
        content.Directions = this.directions;
    }

    /// <inheritdoc cref="LimitRun"/>
    partial void LimitRun(int start, ref int end)
    {
        if (this.directions is null)
        {
            return;
        }

        // A run ends where the orientation or the embedding level changes. Two level runs of the same direction are not
        // shaped together: rule L2 can place other characters between them.
        byte[] flags = this.directions.Flags;
        sbyte[]? levels = this.directions.Levels;
        for (int i = start + 1; i < end; i++)
        {
            if (flags[i] != flags[start] || (levels is not null && levels[i] != levels[start]))
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
    /// Resolves the embedding levels and the visual order of every text chunk, and sets
    /// <see cref="SvgTextDirections.RightToLeft"/> on the graphemes at an odd level.
    /// </summary>
    /// <param name="content">The characters.</param>
    /// <param name="flags">The flags per code unit.</param>
    /// <param name="levels">Receives the embedding level per code unit.</param>
    /// <param name="visualOrder">Receives the visual position per code unit.</param>
    private static void ResolveBidi(SvgTextContent content, byte[] flags, sbyte[] levels, int[] visualOrder)
    {
        // The paragraph level of each chunk: 0 for ltr, 1 for rtl, and 2 for plaintext, which makes the algorithm take
        // the direction of the first strong character of the chunk (rules P2 and P3).
        SvgTextStyle root = content.Spans[0].Style.Text;
        sbyte paragraphLevel = root.UnicodeBidi == SvgUnicodeBidi.Plaintext
            ? (sbyte)2
            : root.Direction == SvgDirection.Rtl ? (sbyte)1 : (sbyte)0;

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

            ResolveChunk(content, paragraphLevel, start, end, scratch, flags, levels, visualOrder);
            start = end;
        }
    }

    /// <summary>
    /// Resolves the embedding levels and the visual order of one text chunk.
    /// </summary>
    /// <param name="content">The characters.</param>
    /// <param name="paragraphLevel">The paragraph embedding level: 0, 1, or 2 for the level of the first strong character.</param>
    /// <param name="start">The first character of the chunk.</param>
    /// <param name="end">The character after the chunk.</param>
    /// <param name="scratch">The reusable buffers.</param>
    /// <param name="flags">The flags per code unit.</param>
    /// <param name="levels">The embedding level per code unit.</param>
    /// <param name="visualOrder">The visual position per code unit.</param>
    private static void ResolveChunk(
        SvgTextContent content,
        sbyte paragraphLevel,
        int start,
        int end,
        BidiScratch scratch,
        byte[] flags,
        sbyte[] levels,
        int[] visualOrder)
    {
        string text = content.Text;
        BuildBidiLine(content, start, end, scratch);
        ReadOnlySpan<char> line = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(scratch.Line);
        List<int> map = scratch.Map;
        BidiData data = scratch.Data;
        BidiAlgorithm algorithm = scratch.Algorithm;
        data.Init(line, paragraphLevel);
        algorithm.Process(data);

        // The algorithm gives one level per code point of the line. The control characters of the line map to no
        // character and are skipped.
        ArraySlice<sbyte> resolved = algorithm.ResolvedLevels;
        int unit = 0;
        int codePoint = 0;
        SpanCodePointEnumerator enumerator = new(line);
        while (enumerator.MoveNext())
        {
            int length = enumerator.Current.Utf16SequenceLength;
            sbyte level = resolved[codePoint++];
            for (int i = unit; i < unit + length; i++)
            {
                if (map[i] >= 0)
                {
                    levels[map[i]] = level;
                }
            }

            unit += length;
        }

        // Each grapheme takes the level of its first code point, so that a shaping run never splits it.
        List<int> starts = scratch.Starts;
        starts.Clear();
        int highest = 0;
        int lowest = int.MaxValue;
        for (int position = start; position < end;)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(position, end - position));
            sbyte level = levels[position];
            levels.AsSpan(position, length).Fill(level);
            starts.Add(position);
            highest = Math.Max(highest, level);
            lowest = Math.Min(lowest, level);
            position += length;
        }

        // Rule L2: from the highest level down to the lowest odd level at or above the lowest level of the chunk, reverse
        // every maximal sequence of graphemes at that level or higher. The order holds the logical index of the grapheme at each visual position. The cost is
        // the number of graphemes times the number of levels, which the depth limit of the algorithm (125) bounds.
        int count = starts.Count;
        Span<int> order = count <= 256 ? stackalloc int[count] : new int[count];
        for (int g = 0; g < count; g++)
        {
            order[g] = g;
        }

        for (int level = highest; level >= (lowest | 1); level--)
        {
            int p = 0;
            while (p < count)
            {
                if (levels[starts[order[p]]] < level)
                {
                    p++;
                    continue;
                }

                int q = p + 1;
                while (q < count && levels[starts[order[q]]] >= level)
                {
                    q++;
                }

                order[p..q].Reverse();
                p = q;
            }
        }

        for (int p = 0; p < count; p++)
        {
            int g = order[p];
            int first = starts[g];
            int next = g + 1 < count ? starts[g + 1] : end;
            bool rightToLeft = (levels[first] & 1) != 0;
            for (int i = first; i < next; i++)
            {
                visualOrder[i] = p;
                if (rightToLeft)
                {
                    flags[i] |= SvgTextDirections.RightToLeft;
                }
            }
        }
    }

    /// <summary>
    /// Writes the characters of a text chunk with the bidirectional control characters that CSS Writing Modes gives as
    /// the equivalent of <c>unicode-bidi</c> and <c>direction</c> on the spans, and maps each code unit to its character.
    /// </summary>
    /// <param name="content">The characters.</param>
    /// <param name="start">The first character of the chunk.</param>
    /// <param name="end">The character after the chunk.</param>
    /// <param name="scratch">The buffers: the line receives the characters, and the map the character index of each
    /// written code unit, or -1 for a control character.</param>
    private static void BuildBidiLine(SvgTextContent content, int start, int end, BidiScratch scratch)
    {
        SvgTextSpan[] spans = content.Spans;
        List<char> line = scratch.Line;
        List<int> map = scratch.Map;
        List<int> open = scratch.Open;
        List<int> chain = scratch.Chain;
        line.Clear();
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

            line.Add(content.Text[i]);
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

        static void Append(List<char> line, List<int> map, char control)
        {
            line.Add(control);
            map.Add(-1);
        }

        static void Open(List<char> line, List<int> map, SvgUnicodeBidi value, bool rtl)
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

        static void Close(List<char> line, List<int> map, SvgUnicodeBidi value)
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
        public List<char> Line { get; } = [];

        /// <summary>
        /// Gets the character index of each code unit of <see cref="Line"/>, or -1 for a control character.
        /// </summary>
        public List<int> Map { get; } = [];

        /// <summary>
        /// Gets the first character of each grapheme of the chunk.
        /// </summary>
        public List<int> Starts { get; } = [];

        /// <summary>
        /// Gets the bidirectional character types of <see cref="Line"/>; the algorithm reuses its buffers.
        /// </summary>
        public BidiData Data { get; } = new();

        /// <summary>
        /// Gets the bidirectional algorithm, which reuses its buffers for each chunk.
        /// </summary>
        public BidiAlgorithm Algorithm { get; } = new();

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
