// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The unit of an <see cref="SvgLength"/>. The parser keeps the unit; the resolve stage converts lengths to user units.
/// </summary>
internal enum SvgLengthUnit : byte
{
    /// <summary>
    /// The length is not specified. This is the unit of <c>default(SvgLength)</c>.
    /// </summary>
    Unspecified,

    /// <summary>
    /// A number without a unit, in user units.
    /// </summary>
    Number,

    /// <summary>
    /// Pixels (<c>px</c>), equal to user units.
    /// </summary>
    Px,

    /// <summary>
    /// Points (<c>pt</c>), 1/72 of an inch.
    /// </summary>
    Pt,

    /// <summary>
    /// Picas (<c>pc</c>), 1/6 of an inch.
    /// </summary>
    Pc,

    /// <summary>
    /// Millimeters (<c>mm</c>).
    /// </summary>
    Mm,

    /// <summary>
    /// Centimeters (<c>cm</c>).
    /// </summary>
    Cm,

    /// <summary>
    /// Inches (<c>in</c>).
    /// </summary>
    In,

    /// <summary>
    /// The computed font size of the element (<c>em</c>).
    /// </summary>
    Em,

    /// <summary>
    /// The x-height of the element font (<c>ex</c>).
    /// </summary>
    Ex,

    /// <summary>
    /// A percentage (<c>%</c>). <see cref="SvgLength.Value"/> holds the percentage, so 50% has the value 50.
    /// </summary>
    Percent
}
