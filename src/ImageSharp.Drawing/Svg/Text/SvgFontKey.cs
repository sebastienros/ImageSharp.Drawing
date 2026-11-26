// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The computed font properties that decide the faces of text: the family list, the weight, the style, and the width.
/// </summary>
/// <remarks>
/// The family list compares by reference. The parser creates one array per declaration and computed styles share it,
/// so text that inherits one declaration shares one key; equal lists from different declarations only cost a second
/// cache entry.
/// </remarks>
internal readonly struct SvgFontKey : IEquatable<SvgFontKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontKey"/> struct.
    /// </summary>
    /// <param name="families">The <c>font-family</c> entries.</param>
    /// <param name="weight">The computed <c>font-weight</c>.</param>
    /// <param name="style">The computed <c>font-style</c>.</param>
    /// <param name="stretch">The computed <c>font-stretch</c>.</param>
    public SvgFontKey(SvgFontFamilyName[] families, float weight, SvgFontStyle style, float stretch)
    {
        this.Families = families;
        this.Weight = weight;
        this.Style = style;
        this.Stretch = stretch;
    }

    /// <summary>
    /// Gets the <c>font-family</c> entries.
    /// </summary>
    public SvgFontFamilyName[] Families { get; }

    /// <summary>
    /// Gets the computed <c>font-weight</c>.
    /// </summary>
    public float Weight { get; }

    /// <summary>
    /// Gets the computed <c>font-style</c>.
    /// </summary>
    public SvgFontStyle Style { get; }

    /// <summary>
    /// Gets the computed <c>font-stretch</c>.
    /// </summary>
    public float Stretch { get; }

    /// <inheritdoc/>
    public bool Equals(SvgFontKey other)
        => ReferenceEquals(this.Families, other.Families)
        && this.Weight == other.Weight
        && this.Style == other.Style
        && this.Stretch == other.Stretch;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SvgFontKey other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this.Families), this.Weight, this.Style, this.Stretch);
}
