// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Kerning and the OpenType features of the font feature and font variant properties.
/// </content>
/// <remarks>
/// <para>
/// The features resolve in the order of CSS Fonts Level 4, section 7.2: the defaults of the shaper; then the
/// <c>font-variant-*</c> longhands (which the <c>font-variant</c> and <c>font</c> shorthands set) and
/// <c>font-kerning</c>; then <c>letter-spacing</c>, which turns the optional ligatures off when it is not zero; then
/// <c>font-feature-settings</c>, whose entries override everything before them, a later entry for the same tag winning.
/// The Chromium oracle applies all of these to SVG text as to HTML text.
/// </para>
/// <para>
/// SixLabors.Fonts takes features through <see cref="TextShaper.ShapeRun(Font, TextShapingBuffer, Tag[])"/>, which only
/// turns features on, each with the value 1, and through <see cref="TextShapingBuffer.KerningMode"/>. So a resolved
/// feature with a value of 1 or more is turned on (a larger value, which selects an alternate of a <c>salt</c> or
/// <c>swsh</c> feature, selects the first alternate); <c>kern</c> off sets <see cref="KerningMode.None"/>; and
/// <c>liga</c> off breaks every cluster of several graphemes into single graphemes, the mechanism that
/// <c>letter-spacing</c> uses, which also undoes <c>clig</c>, <c>dlig</c>, and <c>hlig</c> ligatures and keeps the
/// required ligatures of cursive scripts. Other default features, such as <c>calt</c>, cannot be turned off through the
/// public API, so <c>no-contextual</c> and <c>"calt" 0</c> have no effect.
/// </para>
/// <para>
/// The SVG 1.1 <c>kerning</c> property, which SVG 2 removed, turns kerning off when it has a length, as SVG 1.1 defines
/// and resvg does; Chromium ignores it, and the extra spacing that a non-zero length asks for is not applied. A font
/// without a feature shows its normal glyphs: Chromium synthesizes small capitals, superscripts, and subscripts then,
/// which is not done here.
/// </para>
/// </remarks>
internal sealed partial class SvgTextShaper
{
    /// <summary>
    /// The largest number of resolved entries kept on the stack; longer <c>font-feature-settings</c> lists rent an array.
    /// </summary>
    private const int MaxStackFeatures = 64;

    /// <summary>
    /// Sets the kerning mode and adds the features that the font feature and font variant properties turn on.
    /// </summary>
    /// <param name="buffer">The buffer that holds the text of the run.</param>
    /// <param name="style">The computed text style of the run.</param>
    /// <param name="features">The features to turn on, in addition to the defaults of the shaper.</param>
    static partial void ConfigureFeatures(TextShapingBuffer buffer, SvgTextStyle style, List<Tag> features)
    {
        if (style.FontKerning == SvgFontKerning.None || style.Kerning.IsSpecified)
        {
            buffer.KerningMode = KerningMode.None;
        }

        SvgFontFeatureSetting[] settings = style.FontFeatureSettings;
        int capacity = 32 + settings.Length;
        FeatureEntry[]? rented = null;
        Span<FeatureEntry> entries = capacity <= MaxStackFeatures
            ? stackalloc FeatureEntry[MaxStackFeatures]
            : rented = ArrayPool<FeatureEntry>.Shared.Rent(capacity);

        // Step 4: the variant longhands; font-kerning set the buffer above. Step 5, letter-spacing, only affects liga,
        // which BreaksLigatures resolves.
        int count = AddVariantFeatures(style, entries);

        // Step 6: font-feature-settings, with later positions than the variant features, so they win per tag.
        for (int i = 0; i < settings.Length; i++)
        {
            entries[count] = new FeatureEntry(settings[i].Tag.Value, settings[i].Value, count);
            count++;
        }

        // Sort by tag, then by order, so the last entry of each tag is its resolved value. The sort costs
        // O(n log n) for n entries, which keeps a hostile list of thousands of settings linear-logarithmic.
        Span<FeatureEntry> used = entries[..count];
        used.Sort(static (a, b) => a.Tag != b.Tag ? a.Tag.CompareTo(b.Tag) : a.Order.CompareTo(b.Order));
        for (int i = 0; i < used.Length; i++)
        {
            if (i + 1 < used.Length && used[i + 1].Tag == used[i].Tag)
            {
                continue;
            }

            Tag tag = new(used[i].Tag);
            int value = used[i].Value;
            if (tag == KnownFeatureTags.Kerning)
            {
                // Only font-feature-settings has a kern entry; it overrides font-kerning in both directions.
                buffer.KerningMode = value > 0 ? KerningMode.Standard : KerningMode.None;
            }
            else if (value > 0 && tag != KnownFeatureTags.Ligatures)
            {
                // liga is on by default; turning it off is BreaksLigatures.
                features.Add(tag);
            }
        }

        if (rented is not null)
        {
            ArrayPool<FeatureEntry>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Returns whether the clusters of several graphemes in a run must break into single graphemes because the resolved
    /// value of <c>liga</c> is off: from <c>font-variant-ligatures</c>, then from a non-zero <c>letter-spacing</c>, as
    /// CSS disables optional ligatures then, then from <c>font-feature-settings</c>.
    /// </summary>
    /// <param name="style">The computed text style of the run.</param>
    /// <returns><see langword="true"/> if ligatures break; otherwise, <see langword="false"/>.</returns>
    private static bool BreaksLigatures(SvgTextStyle style)
    {
        SvgFontVariantLigatures ligatures = style.FontVariantLigatures;
        bool off = (ligatures & (SvgFontVariantLigatures.None | SvgFontVariantLigatures.NoCommonLigatures)) != 0;
        if (style.LetterSpacing.IsSpecified && style.LetterSpacing.Value != 0F)
        {
            off = true;
        }

        Tag liga = KnownFeatureTags.Ligatures;
        foreach (SvgFontFeatureSetting setting in style.FontFeatureSettings)
        {
            if (setting.Tag == liga)
            {
                off = setting.Value == 0;
            }
        }

        return off;
    }

    /// <summary>
    /// Adds the features of the <c>font-variant-*</c> longhands, as CSS Fonts Level 4 maps their keywords, with the value
    /// 1 or, for the <c>no-*</c> ligature keywords and <c>none</c>, 0.
    /// </summary>
    /// <param name="style">The computed text style.</param>
    /// <param name="entries">The entries; room for at least 32.</param>
    /// <returns>The number of entries added.</returns>
    private static int AddVariantFeatures(SvgTextStyle style, Span<FeatureEntry> entries)
    {
        int count = 0;
        SvgFontVariantLigatures ligatures = style.FontVariantLigatures;
        if ((ligatures & SvgFontVariantLigatures.None) != 0)
        {
            Add(entries, ref count, KnownFeatureTags.Ligatures, 0);
            Add(entries, ref count, KnownFeatureTags.ContextualLigatures, 0);
            Add(entries, ref count, KnownFeatureTags.DiscretionaryLigatures, 0);
            Add(entries, ref count, KnownFeatureTags.HistoricalLigatures, 0);
            Add(entries, ref count, KnownFeatureTags.ContextualAlternates, 0);
        }
        else if (ligatures != SvgFontVariantLigatures.Normal)
        {
            // Each keyword pair maps to one feature, and common-ligatures to two: liga and clig.
            AddPair(entries, ref count, ligatures, 1, KnownFeatureTags.Ligatures);
            AddPair(entries, ref count, ligatures, 1, KnownFeatureTags.ContextualLigatures);
            AddPair(entries, ref count, ligatures, 3, KnownFeatureTags.DiscretionaryLigatures);
            AddPair(entries, ref count, ligatures, 5, KnownFeatureTags.HistoricalLigatures);
            AddPair(entries, ref count, ligatures, 7, KnownFeatureTags.ContextualAlternates);
        }

        switch (style.FontVariantCaps)
        {
            case SvgFontVariant.SmallCaps:
                Add(entries, ref count, KnownFeatureTags.SmallCapitals, 1);
                break;
            case SvgFontVariant.AllSmallCaps:
                Add(entries, ref count, KnownFeatureTags.SmallCapitals, 1);
                Add(entries, ref count, KnownFeatureTags.SmallCapitalsFromCapitals, 1);
                break;
            case SvgFontVariant.PetiteCaps:
                Add(entries, ref count, KnownFeatureTags.PetiteCapitals, 1);
                break;
            case SvgFontVariant.AllPetiteCaps:
                Add(entries, ref count, KnownFeatureTags.PetiteCapitals, 1);
                Add(entries, ref count, KnownFeatureTags.PetiteCapitalsFromCapitals, 1);
                break;
            case SvgFontVariant.Unicase:
                Add(entries, ref count, KnownFeatureTags.Unicase, 1);
                break;
            case SvgFontVariant.TitlingCaps:
                Add(entries, ref count, KnownFeatureTags.Titling, 1);
                break;
        }

        SvgFontVariantNumeric numeric = style.FontVariantNumeric;
        if (numeric != SvgFontVariantNumeric.Normal)
        {
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.LiningNums), KnownFeatureTags.LiningFigures);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.OldstyleNums), KnownFeatureTags.OldstyleFigures);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.ProportionalNums), KnownFeatureTags.ProportionalFigures);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.TabularNums), KnownFeatureTags.TabularFigures);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.DiagonalFractions), KnownFeatureTags.Fractions);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.StackedFractions), KnownFeatureTags.AlternativeFractions);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.Ordinal), KnownFeatureTags.Ordinals);
            AddFlag(entries, ref count, numeric.HasFlag(SvgFontVariantNumeric.SlashedZero), KnownFeatureTags.SlashedZero);
        }

        SvgFontVariantEastAsian eastAsian = style.FontVariantEastAsian;
        if (eastAsian != SvgFontVariantEastAsian.Normal)
        {
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Jis78), KnownFeatureTags.Jis78Forms);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Jis83), KnownFeatureTags.Jis83Forms);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Jis90), KnownFeatureTags.Jis90Forms);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Jis04), KnownFeatureTags.Jis2004);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Simplified), KnownFeatureTags.SimplifiedForms);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Traditional), KnownFeatureTags.TraditionalForms);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.FullWidth), KnownFeatureTags.FullWidths);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.ProportionalWidth), KnownFeatureTags.ProportionalWidths);
            AddFlag(entries, ref count, eastAsian.HasFlag(SvgFontVariantEastAsian.Ruby), KnownFeatureTags.RubyNotationForms);
        }

        if (style.FontVariantPosition != SvgFontVariantPosition.Normal)
        {
            Tag position = style.FontVariantPosition == SvgFontVariantPosition.Super ? KnownFeatureTags.Superscript : KnownFeatureTags.Subscript;
            Add(entries, ref count, position, 1);
        }

        return count;

        static void Add(Span<FeatureEntry> entries, ref int count, Tag tag, int value)
        {
            entries[count] = new FeatureEntry(tag.Value, value, count);
            count++;
        }

        static void AddFlag(Span<FeatureEntry> entries, ref int count, bool set, Tag tag)
        {
            if (set)
            {
                Add(entries, ref count, tag, 1);
            }
        }

        // The keyword of a pair at bit "on" turns the feature on, its negation at the next bit turns it off.
        static void AddPair(Span<FeatureEntry> entries, ref int count, SvgFontVariantLigatures value, int on, Tag tag)
        {
            if (((int)value & (1 << on)) != 0)
            {
                Add(entries, ref count, tag, 1);
            }
            else if (((int)value & (2 << on)) != 0)
            {
                Add(entries, ref count, tag, 0);
            }
        }
    }

    /// <summary>
    /// One feature setting during resolution: its tag, its value, and its position in resolution order.
    /// </summary>
    private readonly struct FeatureEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FeatureEntry"/> struct.
        /// </summary>
        /// <param name="tag">The tag value.</param>
        /// <param name="value">The setting value.</param>
        /// <param name="order">The position in resolution order; a larger position wins.</param>
        public FeatureEntry(uint tag, int value, int order)
        {
            this.Tag = tag;
            this.Value = value;
            this.Order = order;
        }

        /// <summary>
        /// Gets the tag value.
        /// </summary>
        public uint Tag { get; }

        /// <summary>
        /// Gets the setting value: 0 for off, 1 or more for on.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Gets the position in resolution order.
        /// </summary>
        public int Order { get; }
    }
}
