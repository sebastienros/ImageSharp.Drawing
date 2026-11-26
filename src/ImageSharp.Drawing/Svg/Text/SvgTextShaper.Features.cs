// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Kerning and the OpenType features of <c>font-variant</c>.
/// </content>
/// <remarks>
/// <para>
/// Kerning follows <c>font-kerning</c>: <c>auto</c> and <c>normal</c> keep <see cref="KerningMode.Standard"/>, as Chromium
/// applies kerning for <c>auto</c>, and <c>none</c> turns it off. The SVG 1.1 <c>kerning</c> property, which SVG 2 removed,
/// also turns kerning off when it has a length, as SVG 1.1 defines and resvg does. Chromium ignores <c>kerning</c>.
/// The extra spacing that a non-zero length asks for is not applied.
/// </para>
/// <para>
/// <c>font-variant</c> turns on the features of CSS Fonts Level 3 for <c>font-variant-caps</c>. A font without the
/// feature shows its normal glyphs; Chromium synthesizes small capitals from scaled capitals then, which is not done here.
/// </para>
/// </remarks>
internal sealed partial class SvgTextShaper
{
    /// <summary>
    /// Sets the kerning mode and adds the features of <c>font-variant</c>.
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

        switch (style.FontVariant)
        {
            case SvgFontVariant.SmallCaps:
                features.Add(KnownFeatureTags.SmallCapitals);
                break;
            case SvgFontVariant.AllSmallCaps:
                features.Add(KnownFeatureTags.SmallCapitals);
                features.Add(KnownFeatureTags.SmallCapitalsFromCapitals);
                break;
            case SvgFontVariant.PetiteCaps:
                features.Add(KnownFeatureTags.PetiteCapitals);
                break;
            case SvgFontVariant.AllPetiteCaps:
                features.Add(KnownFeatureTags.PetiteCapitals);
                features.Add(KnownFeatureTags.PetiteCapitalsFromCapitals);
                break;
            case SvgFontVariant.Unicase:
                features.Add(KnownFeatureTags.Unicase);
                break;
            case SvgFontVariant.TitlingCaps:
                features.Add(KnownFeatureTags.Titling);
                break;
        }
    }
}
