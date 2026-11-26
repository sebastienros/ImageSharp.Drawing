// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The direction data of the characters of one <c>text</c> element: the glyph orientation of vertical text, the
/// direction in which each character is shaped, and the visual order of bidirectional text. Package T4 sets it in
/// <see cref="SvgTextContent.Directions"/> before shaping; horizontal left-to-right text has none.
/// </summary>
/// <remarks>
/// The flags hold one entry per UTF-16 code unit of <see cref="SvgTextContent.Text"/>. All code units of one grapheme
/// have the same flags, so a shaping run never splits a grapheme. The visual order is per text chunk: a character's entry
/// is the visual position of its grapheme among the graphemes of its chunk, counted from the left of a horizontal line or
/// the top of a vertical line. The Unicode bidirectional algorithm of SixLabors.Fonts gives it.
/// </remarks>
internal sealed class SvgTextDirections
{
    /// <summary>
    /// The flag of a character that is shaped right to left: it lies in a right-to-left run of the visual order.
    /// </summary>
    public const byte RightToLeft = 1;

    /// <summary>
    /// The flag of a character of vertical text whose glyphs stand upright. Characters without it lie sideways, turned
    /// 90 degrees clockwise.
    /// </summary>
    public const byte Upright = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextDirections"/> class.
    /// </summary>
    /// <param name="writingMode">The writing mode of the <c>text</c> element.</param>
    /// <param name="flags">The flags per code unit.</param>
    /// <param name="visualOrder">The visual position per code unit, or <see langword="null"/> when it is the logical order.</param>
    /// <param name="centralBaselines">The central baseline per span, or <see langword="null"/> when the baseline is alphabetic.</param>
    public SvgTextDirections(SvgWritingMode writingMode, byte[] flags, int[]? visualOrder, float[]? centralBaselines)
    {
        this.WritingMode = writingMode;
        this.Flags = flags;
        this.VisualOrder = visualOrder;
        this.CentralBaselines = centralBaselines;
    }

    /// <summary>
    /// Gets the writing mode of the <c>text</c> element. It applies to all its characters, as in Chromium, where
    /// <c>writing-mode</c> on a <c>tspan</c> has no effect.
    /// </summary>
    public SvgWritingMode WritingMode { get; }

    /// <summary>
    /// Gets a value indicating whether the text is vertical: the pen advances along y.
    /// </summary>
    public bool IsVertical => this.WritingMode != SvgWritingMode.HorizontalTb;

    /// <summary>
    /// Gets the flags per code unit: <see cref="RightToLeft"/> and <see cref="Upright"/>.
    /// </summary>
    public byte[] Flags { get; }

    /// <summary>
    /// Gets the visual position of the grapheme of each code unit within its text chunk, or <see langword="null"/> when
    /// the visual order is the logical order and every text chunk runs in the inline direction from its start.
    /// </summary>
    public int[]? VisualOrder { get; }

    /// <summary>
    /// Gets, per span, the height of the central baseline above the alphabetic baseline in user units: half the sum of
    /// the ascent and the (negative) descent of the primary font of the span at its font size. CSS takes the baselines
    /// of an inline box from its primary font, so glyphs of a fallback font share them. <see langword="null"/> unless the
    /// writing mode is <c>vertical-rl</c> or <c>vertical-lr</c>, whose dominant baseline is <c>central</c>.
    /// </summary>
    public float[]? CentralBaselines { get; }

    /// <summary>
    /// Returns whether characters belong to a cursive script, in which letters join: the script of the first code point
    /// that has a specific script is Arabic, Syriac, N'Ko, Mongolian, Mandaic, Manichaean, Psalter Pahlavi, Phags-pa, Hanifi
    /// Rohingya, Sogdian, Old Uyghur, or Adlam. CSS Text Level 3 does not apply <c>letter-spacing</c> to them, and
    /// Chromium does not either.
    /// </summary>
    /// <param name="characters">The characters of a cluster or a segment.</param>
    /// <returns><see langword="true"/> if the characters are of a cursive script; otherwise, <see langword="false"/>.</returns>
    public static bool IsCursive(ReadOnlySpan<char> characters)
    {
        for (int i = 0; i < characters.Length; i++)
        {
            // Every code point below the Hebrew block belongs to a script that does not join.
            if (characters[i] < '\u0590')
            {
                if (char.IsLetterOrDigit(characters[i]))
                {
                    return false;
                }

                continue;
            }

            CodePoint codePoint = SvgTextShaper.DecodeCodePoint(characters, i);
            i += codePoint.Utf16SequenceLength - 1;
            switch (CodePoint.GetScriptClass(codePoint))
            {
                case ScriptClass.Common:
                case ScriptClass.Inherited:
                case ScriptClass.Unknown:
                    continue;
                case ScriptClass.Arabic:
                case ScriptClass.Syriac:
                case ScriptClass.Nko:
                case ScriptClass.Mongolian:
                case ScriptClass.Mandaic:
                case ScriptClass.Manichaean:
                case ScriptClass.PsalterPahlavi:
                case ScriptClass.PhagsPa:
                case ScriptClass.HanifiRohingya:
                case ScriptClass.Sogdian:
                case ScriptClass.OldUyghur:
                case ScriptClass.Adlam:
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }
}
