// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The layout data of one UTF-16 code unit of the text of a <c>text</c> element after white space processing: its span
/// and its entries from the position lists.
/// </summary>
/// <remarks>
/// Position values sit on the first code unit of each code point, which is an addressable character; the second unit of
/// a surrogate pair has none. Lengths are in user units of the <c>text</c> element. A value that no list gives is
/// <see cref="float.NaN"/> for <see cref="X"/>, <see cref="Y"/>, and <see cref="Rotate"/>, and 0 for <see cref="Dx"/>
/// and <see cref="Dy"/>; the <see cref="Flags"/> tell which values a list gave.
/// </remarks>
internal struct SvgTextCharacter
{
    /// <summary>
    /// Gets or sets the index of the innermost span that contains the character.
    /// </summary>
    public int Span { readonly get; set; }

    /// <summary>
    /// Gets or sets the flags.
    /// </summary>
    public SvgTextCharacterFlags Flags { readonly get; set; }

    /// <summary>
    /// Gets or sets the absolute horizontal position from an <c>x</c> list.
    /// </summary>
    public float X { readonly get; set; }

    /// <summary>
    /// Gets or sets the absolute vertical position from a <c>y</c> list.
    /// </summary>
    public float Y { readonly get; set; }

    /// <summary>
    /// Gets or sets the horizontal shift from a <c>dx</c> list.
    /// </summary>
    public float Dx { readonly get; set; }

    /// <summary>
    /// Gets or sets the vertical shift from a <c>dy</c> list.
    /// </summary>
    public float Dy { readonly get; set; }

    /// <summary>
    /// Gets or sets the rotation in degrees from a <c>rotate</c> list, including the last value that SVG repeats for the
    /// remaining characters of the element.
    /// </summary>
    public float Rotate { readonly get; set; }

    /// <summary>
    /// Gets a value indicating whether the character is the first code unit of a code point.
    /// </summary>
    public readonly bool IsAddressable => (this.Flags & SvgTextCharacterFlags.Addressable) != 0;

    /// <summary>
    /// Gets a value indicating whether a list gives the character its own position value: <c>x</c>, <c>y</c>,
    /// <c>dx</c>, <c>dy</c>, or an entry of <c>rotate</c> (not its repeated last value).
    /// </summary>
    public readonly bool HasOwnPosition => (this.Flags & SvgTextCharacterFlags.OwnPosition) != 0;
}
