// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The text properties and the shorthands <c>font</c> and <c>text-decoration</c>.
/// </content>
internal sealed partial class SvgPropertyParser
{
    /// <summary>
    /// The <c>font-style</c> keywords in <see cref="SvgFontStyle"/> order.
    /// </summary>
    private static readonly string[] FontStyleKeywords = ["normal", "italic", "oblique"];

    /// <summary>
    /// The <c>font-stretch</c> keywords, in the order of <see cref="FontStretchPercentages"/>.
    /// </summary>
    private static readonly string[] FontStretchKeywords =
    [
        "ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "normal", "semi-expanded", "expanded",
        "extra-expanded", "ultra-expanded"
    ];

    /// <summary>
    /// The percentage of each <c>font-stretch</c> keyword, from CSS Fonts Level 4.
    /// </summary>
    private static readonly float[] FontStretchPercentages = [50F, 62.5F, 75F, 87.5F, 100F, 112.5F, 125F, 150F, 200F];

    /// <summary>
    /// The capital letter keywords of <c>font-variant</c> in <see cref="SvgFontVariant"/> order, from index 1.
    /// </summary>
    private static readonly string[] FontVariantCapsKeywords =
        ["normal", "small-caps", "all-small-caps", "petite-caps", "all-petite-caps", "unicase", "titling-caps"];

    /// <summary>
    /// The keywords of the <c>font-variant</c> shorthand that do not select capital letter forms. They parse and have no
    /// effect.
    /// </summary>
    private static readonly string[] FontVariantOtherKeywords =
    [
        "common-ligatures", "no-common-ligatures", "discretionary-ligatures", "no-discretionary-ligatures",
        "historical-ligatures", "no-historical-ligatures", "contextual", "no-contextual", "lining-nums", "oldstyle-nums",
        "proportional-nums", "tabular-nums", "diagonal-fractions", "stacked-fractions", "ordinal", "slashed-zero",
        "jis78", "jis83", "jis90", "jis04", "simplified", "traditional", "full-width", "proportional-width", "ruby",
        "sub", "super", "historical-forms"
    ];

    /// <summary>
    /// The <c>font-kerning</c> keywords in <see cref="SvgFontKerning"/> order.
    /// </summary>
    private static readonly string[] FontKerningKeywords = ["auto", "normal", "none"];

    /// <summary>
    /// The <c>text-anchor</c> keywords in <see cref="SvgTextAnchor"/> order.
    /// </summary>
    private static readonly string[] TextAnchorKeywords = ["start", "middle", "end"];

    /// <summary>
    /// The <c>text-decoration-style</c> keywords in <see cref="SvgTextDecorationStyle"/> order.
    /// </summary>
    private static readonly string[] TextDecorationStyleKeywords = ["solid", "double", "dotted", "dashed", "wavy"];

    /// <summary>
    /// The <c>dominant-baseline</c> keywords in <see cref="SvgDominantBaseline"/> order.
    /// </summary>
    private static readonly string[] DominantBaselineKeywords =
    [
        "auto", "use-script", "no-change", "reset-size", "alphabetic", "ideographic", "middle", "central", "mathematical",
        "hanging", "text-before-edge", "text-after-edge", "text-top", "text-bottom"
    ];

    /// <summary>
    /// The <c>alignment-baseline</c> keywords in <see cref="SvgAlignmentBaseline"/> order.
    /// </summary>
    private static readonly string[] AlignmentBaselineKeywords =
    [
        "auto", "baseline", "before-edge", "text-before-edge", "middle", "central", "after-edge", "text-after-edge",
        "ideographic", "alphabetic", "hanging", "mathematical", "top", "center", "bottom", "text-top", "text-bottom"
    ];

    /// <summary>
    /// The <c>writing-mode</c> keywords, in the order of <see cref="WritingModeValues"/>.
    /// </summary>
    private static readonly string[] WritingModeKeywords =
        ["horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr", "lr", "lr-tb", "rl", "rl-tb", "tb", "tb-rl"];

    /// <summary>
    /// The value of each <c>writing-mode</c> keyword. The SVG 1.1 keywords map as CSS Writing Modes Level 3 defines.
    /// </summary>
    private static readonly SvgWritingMode[] WritingModeValues =
    [
        SvgWritingMode.HorizontalTb, SvgWritingMode.VerticalRl, SvgWritingMode.VerticalLr, SvgWritingMode.SidewaysRl,
        SvgWritingMode.SidewaysLr, SvgWritingMode.HorizontalTb, SvgWritingMode.HorizontalTb, SvgWritingMode.HorizontalTb,
        SvgWritingMode.HorizontalTb, SvgWritingMode.VerticalRl, SvgWritingMode.VerticalRl
    ];

    /// <summary>
    /// The <c>direction</c> keywords in <see cref="SvgDirection"/> order.
    /// </summary>
    private static readonly string[] DirectionKeywords = ["ltr", "rtl"];

    /// <summary>
    /// The <c>unicode-bidi</c> keywords in <see cref="SvgUnicodeBidi"/> order.
    /// </summary>
    private static readonly string[] UnicodeBidiKeywords = ["normal", "embed", "isolate", "bidi-override", "isolate-override", "plaintext"];

    /// <summary>
    /// The <c>white-space</c> keywords in <see cref="SvgWhiteSpace"/> order.
    /// </summary>
    private static readonly string[] WhiteSpaceKeywords = ["normal", "pre", "nowrap", "pre-wrap", "break-spaces", "pre-line"];

    /// <summary>
    /// The <c>text-rendering</c> keywords in <see cref="SvgTextRendering"/> order.
    /// </summary>
    private static readonly string[] TextRenderingKeywords = ["auto", "optimizeSpeed", "optimizeLegibility", "geometricPrecision"];

    /// <summary>
    /// The system font keywords of the <c>font</c> shorthand. Text has no system fonts here, so a value with one is invalid.
    /// </summary>
    private static readonly string[] SystemFontKeywords = ["caption", "icon", "menu", "message-box", "small-caption", "status-bar"];

    /// <summary>
    /// Gets the longhands of the <c>font</c> shorthand that the CSS-wide keywords apply to.
    /// </summary>
    private static ReadOnlySpan<SvgPropertyId> FontShorthandLonghands =>
    [
        SvgPropertyId.FontStyle, SvgPropertyId.FontVariant, SvgPropertyId.FontWeight, SvgPropertyId.FontStretch,
        SvgPropertyId.FontSize, SvgPropertyId.FontFamily, SvgPropertyId.FontSizeAdjust, SvgPropertyId.FontKerning
    ];

    /// <summary>
    /// Gets the longhands of the <c>text-decoration</c> shorthand.
    /// </summary>
    private static ReadOnlySpan<SvgPropertyId> TextDecorationShorthandLonghands =>
        [SvgPropertyId.TextDecorationLine, SvgPropertyId.TextDecorationStyle, SvgPropertyId.TextDecorationColor];

    /// <summary>
    /// Returns whether a CSS declaration name is a shorthand that sets several longhands, and which.
    /// </summary>
    /// <param name="name">The declaration name.</param>
    /// <param name="shorthand">When this method returns <see langword="true"/>, contains the shorthand.</param>
    /// <returns><see langword="true"/> if the name is <c>marker</c>, <c>font</c>, or <c>text-decoration</c>, ignoring ASCII case.</returns>
    public static bool TryGetShorthand(ReadOnlySpan<char> name, out SvgShorthand shorthand)
    {
        if (IsMarkerShorthand(name))
        {
            shorthand = SvgShorthand.Marker;
            return true;
        }

        if (name.Equals("font", StringComparison.OrdinalIgnoreCase))
        {
            shorthand = SvgShorthand.Font;
            return true;
        }

        if (name.Equals(SvgNames.TextDecoration, StringComparison.OrdinalIgnoreCase))
        {
            shorthand = SvgShorthand.TextDecoration;
            return true;
        }

        shorthand = default;
        return false;
    }

    /// <summary>
    /// Parses the value of a shorthand and stores it in its longhands. An invalid value leaves the style unchanged.
    /// </summary>
    /// <param name="shorthand">The shorthand.</param>
    /// <param name="source">The value characters, without <c>!important</c>.</param>
    /// <param name="style">The style that receives the values.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public bool TryParseShorthand(SvgShorthand shorthand, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
        => shorthand switch
        {
            SvgShorthand.Marker => this.TryParseMarkerShorthand(source, ref style),
            SvgShorthand.Font => TryParseFontShorthand(source, ref style),
            _ => this.TryParseTextDecorationShorthand(source, ref style),
        };

    /// <summary>
    /// Parses the value of a text property and stores it in the text values of the style. The caller marks the property.
    /// </summary>
    /// <param name="property">A text property.</param>
    /// <param name="source">The trimmed value, which is not a CSS-wide keyword.</param>
    /// <param name="style">The style that receives the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseText(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        // Parse first and create the text values only for a valid value, so an invalid declaration allocates nothing.
        int keyword;
        switch (property)
        {
            case SvgPropertyId.FontFamily:
                if (!TryParseFontFamily(source, out SvgFontFamilyName[] families))
                {
                    return false;
                }

                GetText(ref style).FontFamily = families;
                return true;
            case SvgPropertyId.FontStyle:
                if (!TryParseFontStyle(source, out SvgFontStyle fontStyle))
                {
                    return false;
                }

                GetText(ref style).FontStyle = fontStyle;
                return true;
            case SvgPropertyId.FontWeight:
                if (!TryParseFontWeight(source, out float weight))
                {
                    return false;
                }

                GetText(ref style).FontWeight = weight;
                return true;
            case SvgPropertyId.FontStretch:
                if (!TryParseFontStretch(source, out float stretch))
                {
                    return false;
                }

                GetText(ref style).FontStretch = stretch;
                return true;
            case SvgPropertyId.FontVariant:
                if (!TryParseFontVariant(source, out SvgFontVariant variant))
                {
                    return false;
                }

                GetText(ref style).FontVariant = variant;
                return true;
            case SvgPropertyId.FontSizeAdjust:
                float adjust = SvgTextStyle.None;
                if (!source.Equals("none", StringComparison.OrdinalIgnoreCase)
                    && (!SvgNumberParser.TryParseNumber(source, out adjust) || adjust < 0F))
                {
                    return false;
                }

                GetText(ref style).FontSizeAdjust = adjust;
                return true;
            case SvgPropertyId.FontKerning:
                if (!TryParseKeyword(source, FontKerningKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).FontKerning = (SvgFontKerning)keyword;
                return true;
            case SvgPropertyId.Kerning:
                SvgLength kerning = default;
                if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    && (!SvgLength.TryParse(source, out kerning) || kerning.Unit == SvgLengthUnit.Percent))
                {
                    return false;
                }

                GetText(ref style).Kerning = kerning;
                return true;
            case SvgPropertyId.TextAnchor:
                if (!TryParseKeyword(source, TextAnchorKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).TextAnchor = (SvgTextAnchor)keyword;
                return true;
            case SvgPropertyId.TextDecorationLine:
                if (!TryParseTextDecorationLine(source, out SvgTextDecorationLine line))
                {
                    return false;
                }

                GetText(ref style).TextDecorationLine = line;
                return true;
            case SvgPropertyId.TextDecorationStyle:
                if (!TryParseKeyword(source, TextDecorationStyleKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).TextDecorationStyle = (SvgTextDecorationStyle)keyword;
                return true;
            case SvgPropertyId.TextDecorationColor:
                if (!SvgPaint.TryParseColorOrCurrentColor(source, this.colors, out SvgPaint decorationColor))
                {
                    return false;
                }

                GetText(ref style).TextDecorationColor = decorationColor;
                return true;
            case SvgPropertyId.LetterSpacing:
            case SvgPropertyId.WordSpacing:
                SvgLength spacing = default;
                if (!source.Equals("normal", StringComparison.OrdinalIgnoreCase) && !SvgLength.TryParse(source, out spacing))
                {
                    return false;
                }

                if (property == SvgPropertyId.LetterSpacing)
                {
                    GetText(ref style).LetterSpacing = spacing;
                }
                else
                {
                    GetText(ref style).WordSpacing = spacing;
                }

                return true;
            case SvgPropertyId.DominantBaseline:
                if (!TryParseKeyword(source, DominantBaselineKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).DominantBaseline = (SvgDominantBaseline)keyword;
                return true;
            case SvgPropertyId.AlignmentBaseline:
                if (!TryParseKeyword(source, AlignmentBaselineKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).AlignmentBaseline = (SvgAlignmentBaseline)keyword;
                return true;
            case SvgPropertyId.BaselineShift:
                if (!TryParseBaselineShift(source, out SvgBaselineShiftKind shiftKind, out SvgLength shift))
                {
                    return false;
                }

                SvgTextStyle text = GetText(ref style);
                text.BaselineShiftKind = shiftKind;
                text.BaselineShift = shift;
                return true;
            case SvgPropertyId.WritingMode:
                if (!TryParseKeyword(source, WritingModeKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).WritingMode = WritingModeValues[keyword];
                return true;
            case SvgPropertyId.Direction:
                if (!TryParseKeyword(source, DirectionKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).Direction = (SvgDirection)keyword;
                return true;
            case SvgPropertyId.UnicodeBidi:
                if (!TryParseKeyword(source, UnicodeBidiKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).UnicodeBidi = (SvgUnicodeBidi)keyword;
                return true;
            case SvgPropertyId.WhiteSpace:
                if (!TryParseKeyword(source, WhiteSpaceKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).WhiteSpace = (SvgWhiteSpace)keyword;
                return true;
            case SvgPropertyId.TextRendering:
                if (!TryParseKeyword(source, TextRenderingKeywords, out keyword))
                {
                    return false;
                }

                GetText(ref style).TextRendering = (SvgTextRendering)keyword;
                return true;
            case SvgPropertyId.GlyphOrientationVertical:
                float vertical = SvgTextStyle.None;
                if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase) && !SvgNumberParser.TryParseAngle(source, out vertical))
                {
                    return false;
                }

                GetText(ref style).GlyphOrientationVertical = vertical;
                return true;
            case SvgPropertyId.GlyphOrientationHorizontal:
                if (!SvgNumberParser.TryParseAngle(source, out float horizontal))
                {
                    return false;
                }

                GetText(ref style).GlyphOrientationHorizontal = horizontal;
                return true;
            default:
                return false;
        }

        static SvgTextStyle GetText(ref SvgSpecifiedStyle style) => style.GetOrCreateExtended().GetOrCreateText();
    }

    /// <summary>
    /// Parses the value of the CSS <c>font</c> shorthand:
    /// <c>[ &lt;font-style&gt; || &lt;font-variant-css2&gt; || &lt;font-weight&gt; || &lt;font-stretch-css3&gt; ]?
    /// &lt;font-size&gt; [ / &lt;line-height&gt; ]? &lt;font-family&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The shorthand sets <c>font-style</c>, <c>font-variant</c>, <c>font-weight</c>, <c>font-stretch</c>,
    /// <c>font-size</c>, and <c>font-family</c>, each to its initial value where the value omits it, and resets
    /// <c>font-size-adjust</c> and <c>font-kerning</c> to their initial values, as CSS Fonts Level 4 defines. SVG text has
    /// no line height, so a line height is checked and dropped. The system font keywords are invalid.
    /// </remarks>
    /// <param name="source">The value characters, without <c>!important</c>.</param>
    /// <param name="style">The style that receives the values.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontShorthand(ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        source = SvgNumberParser.Trim(source);
        if (TryApplyCssWideKeyword(source, ref style, FontShorthandLonghands))
        {
            return true;
        }

        SvgFontStyle fontStyle = SvgFontStyle.Normal;
        SvgFontVariant variant = SvgFontVariant.Normal;
        float weight = 400F;
        float stretch = 100F;
        int seen = 0;
        ReadOnlySpan<char> rest = source;
        SvgLength size;

        // Up to four optional parts before the size, in any order. normal matches any part that is still unset.
        while (true)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int wordLength);
            if (word.IsEmpty)
            {
                return false;
            }

            if (TryParseKeyword(word, SystemFontKeywords, out _))
            {
                return false;
            }

            if (word.Equals("normal", StringComparison.OrdinalIgnoreCase) && seen < 4)
            {
                seen++;
            }
            else if (TryParseFontStyle(word, out SvgFontStyle parsedStyle))
            {
                fontStyle = parsedStyle;
                seen++;
            }
            else if (word.Equals("small-caps", StringComparison.OrdinalIgnoreCase))
            {
                variant = SvgFontVariant.SmallCaps;
                seen++;
            }
            else if (!char.IsAsciiDigit(word[0]) && word[0] != '.' && TryParseFontWeight(word, out float parsedWeight))
            {
                weight = parsedWeight;
                seen++;
            }
            else if (TryParseKeyword(word, FontStretchKeywords, out int stretchIndex))
            {
                stretch = FontStretchPercentages[stretchIndex];
                seen++;
            }
            else if (SvgNumberParser.TryParseNumber(word, out float number) && number is >= 1F and <= 1000F)
            {
                // A number without a unit is a weight; a font size always has a unit in CSS.
                weight = number;
                seen++;
            }
            else
            {
                // The first word that is not an optional part starts the size, which may carry the line height.
                int slash = word.IndexOf('/');
                ReadOnlySpan<char> sizeText = slash >= 0 ? word[..slash] : word;
                if (!TryParseFontSize(sizeText, out size))
                {
                    return false;
                }

                rest = SvgNumberParser.SkipWhitespace(rest[wordLength..]);
                if (slash >= 0 || (!rest.IsEmpty && rest[0] == '/'))
                {
                    // A line height follows the slash: normal, a number, a length, or a percentage.
                    ReadOnlySpan<char> lineHeight = slash >= 0 ? word[(slash + 1)..] : SvgNumberParser.SkipWhitespace(rest[1..]);
                    if (slash < 0)
                    {
                        lineHeight = ReadWord(lineHeight, out int lineHeightLength);
                        rest = SvgNumberParser.SkipWhitespace(SvgNumberParser.SkipWhitespace(rest[1..])[lineHeightLength..]);
                    }
                    else if (lineHeight.IsEmpty)
                    {
                        lineHeight = ReadWord(rest, out int lineHeightLength);
                        rest = SvgNumberParser.SkipWhitespace(rest[lineHeightLength..]);
                    }

                    if (!lineHeight.Equals("normal", StringComparison.OrdinalIgnoreCase) && !SvgLength.TryParseNonNegative(lineHeight, out _))
                    {
                        return false;
                    }
                }

                break;
            }

            if (seen > 4)
            {
                return false;
            }

            rest = SvgNumberParser.SkipWhitespace(rest[wordLength..]);
        }

        if (!TryParseFontFamily(rest, out SvgFontFamilyName[] families))
        {
            return false;
        }

        SvgTextStyle text = style.GetOrCreateExtended().GetOrCreateText();
        text.FontStyle = fontStyle;
        text.FontVariant = variant;
        text.FontWeight = weight;
        text.FontStretch = stretch;
        text.FontFamily = families;
        style.FontSize = size;
        style.MarkValue(SvgPropertyId.FontStyle);
        style.MarkValue(SvgPropertyId.FontVariant);
        style.MarkValue(SvgPropertyId.FontWeight);
        style.MarkValue(SvgPropertyId.FontStretch);
        style.MarkValue(SvgPropertyId.FontFamily);
        style.MarkValue(SvgPropertyId.FontSize);
        style.MarkInitial(SvgPropertyId.FontSizeAdjust);
        style.MarkInitial(SvgPropertyId.FontKerning);
        return true;
    }

    /// <summary>
    /// Parses the value of the <c>text-decoration</c> shorthand: <c>none</c>, or any of a line set, a style, and a color,
    /// each at most once. It sets <c>text-decoration-line</c>, <c>text-decoration-style</c>, and
    /// <c>text-decoration-color</c>, each to its initial value where the value omits it.
    /// </summary>
    /// <param name="source">The value characters, without <c>!important</c>.</param>
    /// <param name="style">The style that receives the values.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseTextDecorationShorthand(ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        source = SvgNumberParser.Trim(source);
        if (TryApplyCssWideKeyword(source, ref style, TextDecorationShorthandLonghands))
        {
            return true;
        }

        SvgTextDecorationLine line = SvgTextDecorationLine.None;
        SvgTextDecorationStyle decorationStyle = SvgTextDecorationStyle.Solid;
        SvgPaint color = SvgTextStyle.Initial.TextDecorationColor;
        bool hasStyle = false;
        bool hasColor = false;

        // The line component: 0 before it, 1 while its keywords are read, 2 after it. Its keywords must be adjacent.
        int lineState = 0;
        bool hasNone = false;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int wordLength);
            rest = SvgNumberParser.SkipWhitespace(rest[wordLength..]);
            if (TryParseLineKeyword(word, out SvgTextDecorationLine lineKeyword))
            {
                // Each line keyword appears once, and none stands alone.
                if (lineState == 2 || hasNone || (line & lineKeyword) != 0)
                {
                    return false;
                }

                line |= lineKeyword;
                lineState = 1;
                continue;
            }

            if (lineState == 1)
            {
                lineState = 2;
            }

            if (lineState == 0 && word.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                hasNone = true;
                lineState = 2;
            }
            else if (!hasStyle && TryParseKeyword(word, TextDecorationStyleKeywords, out int styleIndex))
            {
                decorationStyle = (SvgTextDecorationStyle)styleIndex;
                hasStyle = true;
            }
            else if (!hasColor && SvgPaint.TryParseColorOrCurrentColor(word, this.colors, out SvgPaint parsedColor))
            {
                color = parsedColor;
                hasColor = true;
            }
            else
            {
                return false;
            }
        }

        if (lineState == 0 && !hasStyle && !hasColor)
        {
            return false;
        }

        SvgTextStyle text = style.GetOrCreateExtended().GetOrCreateText();
        text.TextDecorationLine = line;
        text.TextDecorationStyle = decorationStyle;
        text.TextDecorationColor = color;
        style.MarkValue(SvgPropertyId.TextDecorationLine);
        style.MarkValue(SvgPropertyId.TextDecorationStyle);
        style.MarkValue(SvgPropertyId.TextDecorationColor);
        return true;
    }

    /// <summary>
    /// Applies a CSS-wide keyword to every longhand of a shorthand.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="style">The style that receives the keyword.</param>
    /// <param name="longhands">The longhands.</param>
    /// <returns><see langword="true"/> if the value is a CSS-wide keyword; otherwise, <see langword="false"/>.</returns>
    private static bool TryApplyCssWideKeyword(ReadOnlySpan<char> source, ref SvgSpecifiedStyle style, ReadOnlySpan<SvgPropertyId> longhands)
    {
        bool inherit = source.Equals("inherit", StringComparison.OrdinalIgnoreCase);
        bool initial = source.Equals("initial", StringComparison.OrdinalIgnoreCase);
        bool unset = source.Equals("unset", StringComparison.OrdinalIgnoreCase);
        if (!inherit && !initial && !unset)
        {
            return false;
        }

        foreach (SvgPropertyId property in longhands)
        {
            if (inherit || (unset && SvgSpecifiedStyle.IsInherited(property)))
            {
                style.MarkInherit(property);
            }
            else
            {
                style.MarkInitial(property);
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the first word of a value: the characters up to the next whitespace that is not inside parentheses or
    /// quotes, so a color function such as <c>rgb(1, 2, 3)</c> stays one word.
    /// </summary>
    /// <param name="source">The value, without leading whitespace.</param>
    /// <param name="length">When this method returns, contains the number of characters of the word.</param>
    /// <returns>The word; empty when the value is empty.</returns>
    private static ReadOnlySpan<char> ReadWord(ReadOnlySpan<char> source, out int length)
    {
        int depth = 0;
        char quote = '\0';
        length = 0;
        while (length < source.Length)
        {
            char c = source[length];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && SvgNumberParser.IsWhitespace(c))
            {
                break;
            }

            length++;
        }

        return source[..length];
    }

    /// <summary>
    /// Parses a <c>font-family</c> value: a comma-separated list of family names and generic families. A name is a
    /// quoted string, or a run of identifiers that joins with single spaces. An unquoted generic keyword is a generic
    /// family; a quoted one is a name.
    /// </summary>
    /// <param name="source">The value.</param>
    /// <param name="families">When this method returns <see langword="true"/>, contains the entries in order.</param>
    /// <returns><see langword="true"/> if the value is a valid list; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontFamily(ReadOnlySpan<char> source, out SvgFontFamilyName[] families)
    {
        families = [];
        List<SvgFontFamilyName> entries = [];
        ReadOnlySpan<char> rest = SvgNumberParser.SkipWhitespace(source);
        while (true)
        {
            if (rest.IsEmpty)
            {
                return false;
            }

            if (rest[0] is '"' or '\'')
            {
                int close = rest[1..].IndexOf(rest[0]);
                if (close < 0)
                {
                    return false;
                }

                entries.Add(new SvgFontFamilyName(rest.Slice(1, close).ToString()));
                rest = SvgNumberParser.SkipWhitespace(rest[(close + 2)..]);
            }
            else
            {
                int end = rest.IndexOf(',');
                ReadOnlySpan<char> name = SvgNumberParser.Trim(end < 0 ? rest : rest[..end]);
                if (!TryParseUnquotedFamily(name, out SvgFontFamilyName entry))
                {
                    return false;
                }

                entries.Add(entry);
                rest = end < 0 ? [] : rest[end..];
            }

            if (rest.IsEmpty)
            {
                break;
            }

            if (rest[0] != ',')
            {
                return false;
            }

            rest = SvgNumberParser.SkipWhitespace(rest[1..]);
        }

        families = [.. entries];
        return true;
    }

    /// <summary>
    /// Parses an unquoted <c>font-family</c> entry: a generic keyword, or identifiers separated by whitespace.
    /// </summary>
    /// <param name="name">The trimmed entry.</param>
    /// <param name="entry">When this method returns <see langword="true"/>, contains the entry.</param>
    /// <returns><see langword="true"/> if the entry is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseUnquotedFamily(ReadOnlySpan<char> name, out SvgFontFamilyName entry)
    {
        entry = default;
        if (name.IsEmpty)
        {
            return false;
        }

        SvgGenericFontFamily generic = name switch
        {
            _ when name.Equals("serif", StringComparison.OrdinalIgnoreCase) => SvgGenericFontFamily.Serif,
            _ when name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) => SvgGenericFontFamily.SansSerif,
            _ when name.Equals("monospace", StringComparison.OrdinalIgnoreCase) => SvgGenericFontFamily.Monospace,
            _ when name.Equals("cursive", StringComparison.OrdinalIgnoreCase) => SvgGenericFontFamily.Cursive,
            _ when name.Equals("fantasy", StringComparison.OrdinalIgnoreCase) => SvgGenericFontFamily.Fantasy,
            _ => SvgGenericFontFamily.None
        };

        if (generic != SvgGenericFontFamily.None)
        {
            entry = new SvgFontFamilyName(generic);
            return true;
        }

        // Each identifier starts with a letter, an underscore, a hyphen, or a non-ASCII character, and holds no CSS
        // delimiters. The CSS-wide keywords and default are reserved and cannot name a family.
        ValueStringBuilder builder = new(stackalloc char[64]);
        try
        {
            ReadOnlySpan<char> rest = name;
            while (!rest.IsEmpty)
            {
                int length = 0;
                while (length < rest.Length && !SvgNumberParser.IsWhitespace(rest[length]))
                {
                    length++;
                }

                ReadOnlySpan<char> identifier = rest[..length];
                if (!IsIdentifier(identifier)
                    || identifier.Equals("inherit", StringComparison.OrdinalIgnoreCase)
                    || identifier.Equals("initial", StringComparison.OrdinalIgnoreCase)
                    || identifier.Equals("unset", StringComparison.OrdinalIgnoreCase)
                    || identifier.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(identifier);
                rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            }

            entry = new SvgFontFamilyName(builder.AsSpan().ToString());
            return true;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Returns whether a word is a CSS identifier without escapes.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns><see langword="true"/> if the word is an identifier; otherwise, <see langword="false"/>.</returns>
    private static bool IsIdentifier(ReadOnlySpan<char> word)
    {
        if (word.IsEmpty)
        {
            return false;
        }

        int start = word[0] == '-' ? 1 : 0;
        if (start == word.Length)
        {
            return false;
        }

        char first = word[start];
        if (!(char.IsAsciiLetter(first) || first == '_' || first > 0x7F || (start == 1 && first == '-')))
        {
            return false;
        }

        foreach (char c in word)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || c > 0x7F))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a <c>font-style</c> value: <c>normal</c>, <c>italic</c>, or <c>oblique</c> with an optional angle.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the style.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontStyle(ReadOnlySpan<char> source, out SvgFontStyle value)
    {
        if (TryParseKeyword(source, FontStyleKeywords, out int keyword))
        {
            value = (SvgFontStyle)keyword;
            return true;
        }

        // oblique <angle>, with the angle between -90 and 90 degrees.
        value = SvgFontStyle.Oblique;
        if (source.Length > 7
            && source[..7].Equals("oblique", StringComparison.OrdinalIgnoreCase)
            && SvgNumberParser.IsWhitespace(source[7]))
        {
            return SvgNumberParser.TryParseAngle(source[8..], out float angle) && angle is >= -90F and <= 90F;
        }

        return false;
    }

    /// <summary>
    /// Parses a <c>font-weight</c> value: <c>normal</c>, <c>bold</c>, <c>bolder</c>, <c>lighter</c>, or a number from 1 to
    /// 1000.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the weight.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontWeight(ReadOnlySpan<char> source, out float value)
    {
        value = source switch
        {
            _ when source.Equals("normal", StringComparison.OrdinalIgnoreCase) => 400F,
            _ when source.Equals("bold", StringComparison.OrdinalIgnoreCase) => 700F,
            _ when source.Equals("bolder", StringComparison.OrdinalIgnoreCase) => SvgTextStyle.Bolder,
            _ when source.Equals("lighter", StringComparison.OrdinalIgnoreCase) => SvgTextStyle.Lighter,
            _ => 0F
        };

        if (value != 0F)
        {
            return true;
        }

        return SvgNumberParser.TryParseNumber(source, out value) && value is >= 1F and <= 1000F;
    }

    /// <summary>
    /// Parses a <c>font-stretch</c> value: a keyword or a non-negative percentage.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the width as a percentage.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontStretch(ReadOnlySpan<char> source, out float value)
    {
        if (TryParseKeyword(source, FontStretchKeywords, out int keyword))
        {
            value = FontStretchPercentages[keyword];
            return true;
        }

        value = 0F;
        if (!SvgLength.TryParse(source, out SvgLength length) || length.Unit != SvgLengthUnit.Percent || length.Value < 0F)
        {
            return false;
        }

        value = length.Value;
        return true;
    }

    /// <summary>
    /// Parses a <c>font-variant</c> value: <c>normal</c>, <c>none</c>, or a list of the keywords of the CSS shorthand
    /// with at most one capital letter keyword.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the capital letter forms.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFontVariant(ReadOnlySpan<char> source, out SvgFontVariant value)
    {
        value = SvgFontVariant.Normal;
        if (source.Equals("normal", StringComparison.OrdinalIgnoreCase) || source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool hasCaps = false;
        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            if (TryParseKeyword(word, FontVariantCapsKeywords, out int caps) && caps > 0)
            {
                if (hasCaps)
                {
                    return false;
                }

                value = (SvgFontVariant)caps;
                hasCaps = true;
            }
            else if (!TryParseKeyword(word, FontVariantOtherKeywords, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a <c>text-decoration-line</c> value: <c>none</c>, or one or more line keywords, each at most once.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the lines.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseTextDecorationLine(ReadOnlySpan<char> source, out SvgTextDecorationLine value)
    {
        value = SvgTextDecorationLine.None;
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ReadOnlySpan<char> rest = source;
        while (!rest.IsEmpty)
        {
            ReadOnlySpan<char> word = ReadWord(rest, out int length);
            rest = SvgNumberParser.SkipWhitespace(rest[length..]);
            if (!TryParseLineKeyword(word, out SvgTextDecorationLine line) || (value & line) != 0)
            {
                return false;
            }

            value |= line;
        }

        return value != SvgTextDecorationLine.None;
    }

    /// <summary>
    /// Parses one line keyword of <c>text-decoration-line</c>.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the line.</param>
    /// <returns><see langword="true"/> if the word is a line keyword; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseLineKeyword(ReadOnlySpan<char> word, out SvgTextDecorationLine value)
    {
        value = word switch
        {
            _ when word.Equals("underline", StringComparison.OrdinalIgnoreCase) => SvgTextDecorationLine.Underline,
            _ when word.Equals("overline", StringComparison.OrdinalIgnoreCase) => SvgTextDecorationLine.Overline,
            _ when word.Equals("line-through", StringComparison.OrdinalIgnoreCase) => SvgTextDecorationLine.LineThrough,
            _ when word.Equals("blink", StringComparison.OrdinalIgnoreCase) => SvgTextDecorationLine.Blink,
            _ => SvgTextDecorationLine.None
        };

        return value != SvgTextDecorationLine.None;
    }

    /// <summary>
    /// Parses a <c>baseline-shift</c> value: <c>baseline</c>, <c>sub</c>, <c>super</c>, a length, or a percentage.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="kind">When this method returns <see langword="true"/>, contains the kind of the value.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the length for <see cref="SvgBaselineShiftKind.Length"/>.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseBaselineShift(ReadOnlySpan<char> source, out SvgBaselineShiftKind kind, out SvgLength length)
    {
        length = default;
        kind = source switch
        {
            _ when source.Equals("baseline", StringComparison.OrdinalIgnoreCase) => SvgBaselineShiftKind.Baseline,
            _ when source.Equals("sub", StringComparison.OrdinalIgnoreCase) => SvgBaselineShiftKind.Sub,
            _ when source.Equals("super", StringComparison.OrdinalIgnoreCase) => SvgBaselineShiftKind.Super,
            _ => SvgBaselineShiftKind.Length
        };

        return kind != SvgBaselineShiftKind.Length || SvgLength.TryParse(source, out length);
    }
}
