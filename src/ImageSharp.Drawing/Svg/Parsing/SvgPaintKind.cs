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
    Url,

    /// <summary>
    /// <c>context-fill</c>: the fill of the context element. Inside marker content the context element is the shape that
    /// the marker decorates; inside the content of a <c>use</c> element it is the <c>use</c> element. Without a context
    /// element the paint is <c>none</c>. Only <c>fill</c> and <c>stroke</c> accept it.
    /// </summary>
    ContextFill,

    /// <summary>
    /// <c>context-stroke</c>: the stroke of the context element, with the same rules as <see cref="ContextFill"/>.
    /// </summary>
    ContextStroke
}
