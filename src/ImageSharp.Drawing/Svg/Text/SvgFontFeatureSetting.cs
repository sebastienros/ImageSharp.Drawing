// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Tables.AdvancedTypographic;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One entry of <c>font-feature-settings</c>: an OpenType feature tag and its value.
/// </summary>
internal readonly struct SvgFontFeatureSetting
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontFeatureSetting"/> struct.
    /// </summary>
    /// <param name="tag">The feature tag.</param>
    /// <param name="value">The value: 0 turns the feature off, 1 turns it on, and a larger number selects an alternate.</param>
    public SvgFontFeatureSetting(Tag tag, int value)
    {
        this.Tag = tag;
        this.Value = value;
    }

    /// <summary>
    /// Gets the feature tag: four ASCII characters from U+20 to U+7E.
    /// </summary>
    public Tag Tag { get; }

    /// <summary>
    /// Gets the value: 0 for <c>off</c>, 1 for <c>on</c> and for an entry without a value, or the given non-negative
    /// integer.
    /// </summary>
    public int Value { get; }
}
