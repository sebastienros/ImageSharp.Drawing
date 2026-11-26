// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Flags of a <see cref="SvgTextCharacter"/>.
/// </summary>
[Flags]
internal enum SvgTextCharacterFlags : byte
{
    /// <summary>
    /// No flag.
    /// </summary>
    None = 0,

    /// <summary>
    /// The character is the first code unit of a code point, so position lists address it.
    /// </summary>
    Addressable = 1,

    /// <summary>
    /// An <c>x</c> list gives the character a value.
    /// </summary>
    HasX = 2,

    /// <summary>
    /// A <c>y</c> list gives the character a value.
    /// </summary>
    HasY = 4,

    /// <summary>
    /// A <c>dx</c>, <c>dy</c>, or <c>rotate</c> list gives the character its own value.
    /// </summary>
    HasShift = 8,

    /// <summary>
    /// Any list gives the character its own value.
    /// </summary>
    OwnPosition = HasX | HasY | HasShift
}
