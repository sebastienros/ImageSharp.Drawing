// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of an <see cref="SvgPaint"/> value.
/// </summary>
internal enum SvgPaintKind : byte
{
    /// <summary>
    /// No paint (<c>none</c>). This is also the kind of <c>default(SvgPaint)</c>.
    /// </summary>
    None,

    /// <summary>
    /// The computed value of the <c>color</c> property (<c>currentColor</c>).
    /// </summary>
    CurrentColor,

    /// <summary>
    /// A color.
    /// </summary>
    Color,

    /// <summary>
    /// A paint server reference (<c>url(...)</c>) with an optional fallback.
    /// </summary>
    Url
}
