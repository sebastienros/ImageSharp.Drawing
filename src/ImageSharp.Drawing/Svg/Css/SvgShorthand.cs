// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The CSS shorthands that the property parser expands into their longhands. A shorthand is not a property of its own,
/// so it has no member in <see cref="SvgPropertyId"/>.
/// </summary>
internal enum SvgShorthand : byte
{
    /// <summary>
    /// <c>marker</c>: <c>marker-start</c>, <c>marker-mid</c>, and <c>marker-end</c>. CSS only; SVG has no
    /// <c>marker</c> presentation attribute.
    /// </summary>
    Marker,

    /// <summary>
    /// <c>font</c>: the font longhands. CSS only, as in Chromium.
    /// </summary>
    Font,

    /// <summary>
    /// <c>text-decoration</c>: <c>text-decoration-line</c>, <c>-style</c>, and <c>-color</c>. Also a presentation
    /// attribute.
    /// </summary>
    TextDecoration
}
