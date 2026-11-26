// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The values of the text properties: from <see cref="SvgPropertyId.FontFamily"/> to
/// <see cref="SvgPropertyId.GlyphOrientationHorizontal"/>, and from <see cref="SvgPropertyId.FontFeatureSettings"/> to
/// <see cref="SvgPropertyId.WhiteSpaceCollapse"/>. The same type holds specified values, in
/// <see cref="SvgExtendedStyle.Text"/>, and computed values, in <see cref="SvgComputedStyle.Text"/>.
/// </summary>
/// <remarks>
/// <para>
/// Specified and computed values differ only where CSS computes them: a specified <see cref="FontWeight"/> can be
/// <see cref="Bolder"/> or <see cref="Lighter"/>, and specified lengths can be in any unit. Computed lengths are in
/// <see cref="SvgLengthUnit.Px"/>, except the percentages that the properties keep (<see cref="BaselineShift"/>,
/// <see cref="LetterSpacing"/>, and <see cref="WordSpacing"/>, whose percentages refer to the font size where they apply).
/// In a specified style, a value is meaningful only when the bits of its property in <see cref="SvgSpecifiedStyle"/> say
/// so; a computed style has every value.
/// </para>
/// <para>
/// Ownership follows <see cref="SvgExtendedStyle"/>: an instance changes only while the parser or
/// <see cref="SvgComputedStyle.Compute"/> builds it, before any other style refers to it. After that it is shared and never
/// changes, so the resolve stage can compare instances by reference: equal references mean equal values.
/// </para>
/// <para>
/// The only lazily set field is the cache of <see cref="GetChildStyle"/>, which the single-threaded resolve stage fills.
/// </para>
/// </remarks>
internal sealed class SvgTextStyle
{
    /// <summary>
    /// The specified <see cref="FontWeight"/> of the keyword <c>bolder</c>.
    /// </summary>
    public const float Bolder = -1F;

    /// <summary>
    /// The specified <see cref="FontWeight"/> of the keyword <c>lighter</c>.
    /// </summary>
    public const float Lighter = -2F;

    /// <summary>
    /// The <see cref="FontSizeAdjust"/> of <c>none</c>, and the glyph orientation of <c>auto</c>.
    /// </summary>
    public const float None = float.NaN;

    /// <summary>
    /// The mask of the text properties that do not inherit.
    /// </summary>
    private const ulong NonInheritedLowProperties =
        (1UL << (int)SvgPropertyId.TextDecorationLine)
        | (1UL << (int)SvgPropertyId.TextDecorationStyle)
        | (1UL << (int)SvgPropertyId.TextDecorationColor)
        | (1UL << (int)SvgPropertyId.AlignmentBaseline)
        | (1UL << (int)SvgPropertyId.BaselineShift)
        | (1UL << (int)SvgPropertyId.UnicodeBidi);

    /// <summary>
    /// The cache of <see cref="GetChildStyle"/>.
    /// </summary>
    private SvgTextStyle? childStyle;

    /// <summary>
    /// Gets the mask of the text properties.
    /// </summary>
    public static SvgPropertySet Properties { get; } =
        SvgPropertySet.Range(SvgPropertyId.FontFamily, SvgPropertyId.GlyphOrientationHorizontal)
        | SvgPropertySet.Range(SvgPropertyId.FontFeatureSettings, SvgPropertyId.WhiteSpaceCollapse);

    /// <summary>
    /// Gets the mask of the text properties that do not inherit. Every text property from 64 on inherits.
    /// </summary>
    public static SvgPropertySet NonInheritedProperties { get; } = new(NonInheritedLowProperties, 0);

    /// <summary>
    /// Gets the style with the initial value of every text property. It is shared and never changes.
    /// </summary>
    public static SvgTextStyle Initial { get; } = CreateInitial();

    /// <summary>
    /// Gets or sets <c>font-family</c>: the entries in order. Empty, the initial value, means the default family of the
    /// load options.
    /// </summary>
    public SvgFontFamilyName[] FontFamily { get; set; } = [];

    /// <summary>
    /// Gets or sets <c>font-style</c>.
    /// </summary>
    public SvgFontStyle FontStyle { get; set; }

    /// <summary>
    /// Gets or sets <c>font-weight</c>: a number from 1 to 1000. <c>normal</c> is 400 and <c>bold</c> 700. A specified
    /// value can also be <see cref="Bolder"/> or <see cref="Lighter"/>, which compute against the parent weight.
    /// </summary>
    public float FontWeight { get; set; } = 400F;

    /// <summary>
    /// Gets or sets <c>font-stretch</c> as a percentage of the normal width; the keywords map to the percentages of CSS
    /// Fonts Level 4.
    /// </summary>
    public float FontStretch { get; set; } = 100F;

    /// <summary>
    /// Gets or sets <c>font-variant</c>.
    /// </summary>
    public SvgFontVariant FontVariant { get; set; }

    /// <summary>
    /// Gets or sets <c>font-size-adjust</c>: a non-negative number, or <see cref="None"/>.
    /// </summary>
    public float FontSizeAdjust { get; set; } = None;

    /// <summary>
    /// Gets or sets <c>font-kerning</c>.
    /// </summary>
    public SvgFontKerning FontKerning { get; set; }

    /// <summary>
    /// Gets or sets the SVG 1.1 <c>kerning</c> property: unspecified for <c>auto</c>, otherwise a length.
    /// </summary>
    public SvgLength Kerning { get; set; }

    /// <summary>
    /// Gets or sets <c>text-anchor</c>.
    /// </summary>
    public SvgTextAnchor TextAnchor { get; set; }

    /// <summary>
    /// Gets or sets <c>text-decoration-line</c>. Not inherited.
    /// </summary>
    public SvgTextDecorationLine TextDecorationLine { get; set; }

    /// <summary>
    /// Gets or sets <c>text-decoration-style</c>. Not inherited.
    /// </summary>
    public SvgTextDecorationStyle TextDecorationStyle { get; set; }

    /// <summary>
    /// Gets or sets <c>text-decoration-color</c>, of kind <see cref="SvgPaintKind.Color"/> or
    /// <see cref="SvgPaintKind.CurrentColor"/>. Not inherited. The initial value is <c>currentColor</c>.
    /// </summary>
    public SvgPaint TextDecorationColor { get; set; }

    /// <summary>
    /// Gets or sets <c>letter-spacing</c>: unspecified for <c>normal</c>, otherwise a length or a percentage of the font size.
    /// </summary>
    public SvgLength LetterSpacing { get; set; }

    /// <summary>
    /// Gets or sets <c>word-spacing</c>: unspecified for <c>normal</c>, otherwise a length or a percentage of the font size.
    /// </summary>
    public SvgLength WordSpacing { get; set; }

    /// <summary>
    /// Gets or sets <c>dominant-baseline</c>. Inherited, as SVG 2 and CSS Inline Layout define.
    /// </summary>
    public SvgDominantBaseline DominantBaseline { get; set; }

    /// <summary>
    /// Gets or sets <c>alignment-baseline</c>. Not inherited.
    /// </summary>
    public SvgAlignmentBaseline AlignmentBaseline { get; set; }

    /// <summary>
    /// Gets or sets the kind of <c>baseline-shift</c>. Not inherited.
    /// </summary>
    public SvgBaselineShiftKind BaselineShiftKind { get; set; }

    /// <summary>
    /// Gets or sets the amount of <c>baseline-shift</c> when <see cref="BaselineShiftKind"/> is
    /// <see cref="SvgBaselineShiftKind.Length"/>: a length, or a percentage of the line height.
    /// </summary>
    public SvgLength BaselineShift { get; set; }

    /// <summary>
    /// Gets or sets <c>writing-mode</c>.
    /// </summary>
    public SvgWritingMode WritingMode { get; set; }

    /// <summary>
    /// Gets or sets <c>direction</c>.
    /// </summary>
    public SvgDirection Direction { get; set; }

    /// <summary>
    /// Gets or sets <c>unicode-bidi</c>. Not inherited.
    /// </summary>
    public SvgUnicodeBidi UnicodeBidi { get; set; }

    /// <summary>
    /// Gets or sets <c>white-space</c> as the text layout reads it today: the six keywords of CSS Text Level 3. The
    /// shorthand forms of CSS Text Level 4, such as <c>preserve nowrap</c>, leave it unspecified and set only
    /// <see cref="WhiteSpaceCollapse"/>.
    /// </summary>
    public SvgWhiteSpace WhiteSpace { get; set; }

    /// <summary>
    /// Gets or sets <c>white-space-collapse</c>, which both <c>white-space-collapse</c> and the <c>white-space</c>
    /// shorthand set, so it holds the collapse mode of whichever declaration won the cascade.
    /// </summary>
    public SvgWhiteSpaceCollapse WhiteSpaceCollapse { get; set; }

    /// <summary>
    /// Gets or sets <c>font-feature-settings</c>: the entries in order, later entries for the same tag winning. Empty, the
    /// initial value, means <c>normal</c>.
    /// </summary>
    public SvgFontFeatureSetting[] FontFeatureSettings { get; set; } = [];

    /// <summary>
    /// Gets or sets <c>font-variant-ligatures</c>.
    /// </summary>
    public SvgFontVariantLigatures FontVariantLigatures { get; set; }

    /// <summary>
    /// Gets or sets <c>font-variant-caps</c>. The <c>font-variant</c> shorthand sets it as well as <see cref="FontVariant"/>,
    /// which the text layout reads today; <c>font-variant-caps</c> sets only this value.
    /// </summary>
    public SvgFontVariant FontVariantCaps { get; set; }

    /// <summary>
    /// Gets or sets <c>font-variant-numeric</c>.
    /// </summary>
    public SvgFontVariantNumeric FontVariantNumeric { get; set; }

    /// <summary>
    /// Gets or sets <c>font-variant-east-asian</c>.
    /// </summary>
    public SvgFontVariantEastAsian FontVariantEastAsian { get; set; }

    /// <summary>
    /// Gets or sets <c>font-variant-position</c>.
    /// </summary>
    public SvgFontVariantPosition FontVariantPosition { get; set; }

    /// <summary>
    /// Gets or sets <c>text-rendering</c>.
    /// </summary>
    public SvgTextRendering TextRendering { get; set; }

    /// <summary>
    /// Gets or sets <c>glyph-orientation-vertical</c> in degrees, or <see cref="None"/> for <c>auto</c>.
    /// </summary>
    public float GlyphOrientationVertical { get; set; } = None;

    /// <summary>
    /// Gets or sets <c>glyph-orientation-horizontal</c> in degrees. The initial value is 0.
    /// </summary>
    public float GlyphOrientationHorizontal { get; set; }

    /// <summary>
    /// Gets or sets the <c>text-decoration-line</c> values that ancestors set. CSS propagates a decoration to the
    /// descendants of the element that sets it, so text draws the lines of this value and of its own
    /// <see cref="TextDecorationLine"/>. It is not a property: <see cref="GetChildStyle"/> adds the line of the parent,
    /// and <see cref="Clone"/> keeps it.
    /// </summary>
    public SvgTextDecorationLine AncestorDecorationLine { get; set; }

    /// <summary>
    /// Gets a value indicating whether a property that does not inherit has a value other than its initial value.
    /// </summary>
    public bool HasNonInheritedValues
        => this.TextDecorationLine != SvgTextDecorationLine.None
        || this.TextDecorationStyle != SvgTextDecorationStyle.Solid
        || this.TextDecorationColor.Kind != SvgPaintKind.CurrentColor
        || this.AlignmentBaseline != SvgAlignmentBaseline.Auto
        || this.BaselineShiftKind != SvgBaselineShiftKind.Baseline
        || this.UnicodeBidi != SvgUnicodeBidi.Normal;

    /// <summary>
    /// Returns whether a property is a text property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the value of the property lives in this type; otherwise, <see langword="false"/>.</returns>
    public static bool IsTextProperty(SvgPropertyId property) => Properties.Contains(property);

    /// <summary>
    /// Returns a copy that the caller may change before it shares it.
    /// </summary>
    /// <returns>The copy.</returns>
    public SvgTextStyle Clone()
    {
        SvgTextStyle copy = (SvgTextStyle)this.MemberwiseClone();
        copy.childStyle = null;
        return copy;
    }

    /// <summary>
    /// Returns the computed text style that a child starts from: this style with every property that does not inherit
    /// at its initial value. The result is this instance when those properties are already initial, and it is cached, so
    /// children of one parent share it.
    /// </summary>
    /// <returns>The style.</returns>
    public SvgTextStyle GetChildStyle()
    {
        if (!this.HasNonInheritedValues)
        {
            return this;
        }

        if (this.childStyle is null)
        {
            SvgTextStyle child = this.Clone();
            child.CopyProperties(NonInheritedProperties, Initial);
            child.AncestorDecorationLine = this.AncestorDecorationLine | this.TextDecorationLine;
            this.childStyle = child;
        }

        return this.childStyle;
    }

    /// <summary>
    /// Returns a new style that holds the values of <paramref name="target"/> with the values of the properties in
    /// <paramref name="properties"/> taken from <paramref name="source"/>. Neither input changes.
    /// </summary>
    /// <param name="target">The values of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The values of higher precedence.</param>
    /// <param name="properties">The mask of the text properties that <paramref name="source"/> specifies.</param>
    /// <returns>The merged style.</returns>
    public static SvgTextStyle Merge(SvgTextStyle? target, SvgTextStyle source, SvgPropertySet properties)
    {
        SvgTextStyle result = target is null ? new SvgTextStyle() : target.Clone();
        result.CopyProperties(properties, source);
        return result;
    }

    /// <summary>
    /// Copies the values of the properties in a mask from another style.
    /// </summary>
    /// <param name="properties">The mask; bits of other properties are ignored.</param>
    /// <param name="source">The style to copy from.</param>
    public void CopyProperties(SvgPropertySet properties, SvgTextStyle source)
    {
        SvgPropertySet bits = properties & Properties;
        while (!bits.IsEmpty)
        {
            this.CopyProperty(SvgPropertySet.TakeFirst(ref bits), source);
        }
    }

    /// <summary>
    /// Copies the value of one text property from another style.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="source">The style to copy from.</param>
    public void CopyProperty(SvgPropertyId property, SvgTextStyle source)
    {
        switch (property)
        {
            case SvgPropertyId.FontFamily:
                this.FontFamily = source.FontFamily;
                break;
            case SvgPropertyId.FontStyle:
                this.FontStyle = source.FontStyle;
                break;
            case SvgPropertyId.FontWeight:
                this.FontWeight = source.FontWeight;
                break;
            case SvgPropertyId.FontStretch:
                this.FontStretch = source.FontStretch;
                break;
            case SvgPropertyId.FontVariant:
                this.FontVariant = source.FontVariant;
                break;
            case SvgPropertyId.FontSizeAdjust:
                this.FontSizeAdjust = source.FontSizeAdjust;
                break;
            case SvgPropertyId.FontKerning:
                this.FontKerning = source.FontKerning;
                break;
            case SvgPropertyId.Kerning:
                this.Kerning = source.Kerning;
                break;
            case SvgPropertyId.TextAnchor:
                this.TextAnchor = source.TextAnchor;
                break;
            case SvgPropertyId.TextDecorationLine:
                this.TextDecorationLine = source.TextDecorationLine;
                break;
            case SvgPropertyId.TextDecorationStyle:
                this.TextDecorationStyle = source.TextDecorationStyle;
                break;
            case SvgPropertyId.TextDecorationColor:
                this.TextDecorationColor = source.TextDecorationColor;
                break;
            case SvgPropertyId.LetterSpacing:
                this.LetterSpacing = source.LetterSpacing;
                break;
            case SvgPropertyId.WordSpacing:
                this.WordSpacing = source.WordSpacing;
                break;
            case SvgPropertyId.DominantBaseline:
                this.DominantBaseline = source.DominantBaseline;
                break;
            case SvgPropertyId.AlignmentBaseline:
                this.AlignmentBaseline = source.AlignmentBaseline;
                break;
            case SvgPropertyId.BaselineShift:
                this.BaselineShiftKind = source.BaselineShiftKind;
                this.BaselineShift = source.BaselineShift;
                break;
            case SvgPropertyId.WritingMode:
                this.WritingMode = source.WritingMode;
                break;
            case SvgPropertyId.Direction:
                this.Direction = source.Direction;
                break;
            case SvgPropertyId.UnicodeBidi:
                this.UnicodeBidi = source.UnicodeBidi;
                break;
            case SvgPropertyId.WhiteSpace:
                this.WhiteSpace = source.WhiteSpace;
                break;
            case SvgPropertyId.TextRendering:
                this.TextRendering = source.TextRendering;
                break;
            case SvgPropertyId.GlyphOrientationVertical:
                this.GlyphOrientationVertical = source.GlyphOrientationVertical;
                break;
            case SvgPropertyId.GlyphOrientationHorizontal:
                this.GlyphOrientationHorizontal = source.GlyphOrientationHorizontal;
                break;
            case SvgPropertyId.FontFeatureSettings:
                this.FontFeatureSettings = source.FontFeatureSettings;
                break;
            case SvgPropertyId.FontVariantLigatures:
                this.FontVariantLigatures = source.FontVariantLigatures;
                break;
            case SvgPropertyId.FontVariantCaps:
                this.FontVariantCaps = source.FontVariantCaps;
                break;
            case SvgPropertyId.FontVariantNumeric:
                this.FontVariantNumeric = source.FontVariantNumeric;
                break;
            case SvgPropertyId.FontVariantEastAsian:
                this.FontVariantEastAsian = source.FontVariantEastAsian;
                break;
            case SvgPropertyId.FontVariantPosition:
                this.FontVariantPosition = source.FontVariantPosition;
                break;
            case SvgPropertyId.WhiteSpaceCollapse:
                this.WhiteSpaceCollapse = source.WhiteSpaceCollapse;
                break;
        }
    }

    /// <summary>
    /// Creates the style with the initial values.
    /// </summary>
    /// <returns>The style.</returns>
    private static SvgTextStyle CreateInitial()
        => new()
        {
            TextDecorationColor = new SvgPaint(SvgPaintKind.CurrentColor, default, null, SvgPaintKind.None),
        };
}
