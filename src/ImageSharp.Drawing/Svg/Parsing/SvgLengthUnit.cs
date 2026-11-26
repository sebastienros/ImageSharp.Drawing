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
    Percent,

    /// <summary>
    /// The advance of the "0" glyph of the element font (<c>ch</c>), which the resolve stage reads from the font; without
    /// a font it is half the font size, the fallback of CSS Values 4. See <see cref="SvgUnits"/>.
    /// </summary>
    Ch,

    /// <summary>
    /// The computed font size of the root element (<c>rem</c>).
    /// </summary>
    Rem,

    /// <summary>
    /// Quarter-millimeters (<c>Q</c>), 1/40 of a centimeter.
    /// </summary>
    Q,

    /// <summary>
    /// 1% of the width of the initial containing block (<c>vw</c>). For a standalone document that is the intrinsic
    /// size of the document.
    /// </summary>
    Vw,

    /// <summary>
    /// 1% of the height of the initial containing block (<c>vh</c>).
    /// </summary>
    Vh,

    /// <summary>
    /// The smaller of <see cref="Vw"/> and <see cref="Vh"/> (<c>vmin</c>).
    /// </summary>
    Vmin,

    /// <summary>
    /// The larger of <see cref="Vw"/> and <see cref="Vh"/> (<c>vmax</c>).
    /// </summary>
    Vmax,

    /// <summary>
    /// A math function (<c>calc()</c>, <c>min()</c>, <c>max()</c>, <c>clamp()</c>) that mixes units, such as
    /// <c>calc(50% - 10px)</c>, so it has no value until its references are known. <see cref="SvgLength.Value"/> holds
    /// the index of the expression in the <see cref="SvgCalcTable"/> of the document. A math function with one unit is
    /// stored as a plain length of that unit instead.
    /// </summary>
    Calc
}
