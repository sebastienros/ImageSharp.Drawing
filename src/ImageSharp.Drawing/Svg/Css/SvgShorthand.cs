// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The CSS shorthands that the property parser expands into their longhands. A shorthand is not a property of its own,
/// so it has no member in <see cref="SvgPropertyId"/>, except <see cref="FontVariant"/> and <see cref="WhiteSpace"/>,
/// which the text layout also reads as properties.
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
    TextDecoration,

    /// <summary>
    /// <c>font-variant</c> as the shorthand of <c>font-variant-ligatures</c>, <c>-caps</c>, <c>-numeric</c>,
    /// <c>-east-asian</c>, and <c>-position</c>. The declaration also sets <see cref="SvgPropertyId.FontVariant"/>, the
    /// older value that no renderer code reads any more, so its name maps to that property; this member names the shorthand in a
    /// <see cref="SvgPendingValue"/>.
    /// </summary>
    FontVariant,

    /// <summary>
    /// <c>white-space</c> as the shorthand of <c>white-space-collapse</c> (CSS Text Level 4). The declaration also sets
    /// <see cref="SvgPropertyId.WhiteSpace"/>, so its name maps to that property; this member names the shorthand in a
    /// <see cref="SvgPendingValue"/>.
    /// </summary>
    WhiteSpace
}
