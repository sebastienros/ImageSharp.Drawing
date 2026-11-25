// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>overflow</c> property.
/// </summary>
/// <remarks>
/// SVG 2 sets <c>overflow: hidden</c> on <c>svg</c>, <c>symbol</c>, <c>image</c>, <c>marker</c>, and <c>pattern</c> in its user agent style sheet; <see cref="SvgComputedStyle.Overflow"/> includes that rule.
/// </remarks>
internal enum SvgOverflow : byte
{
    /// <summary>
    /// <c>visible</c>, the initial value: content outside the viewport is not clipped.
    /// </summary>
    Visible,

    /// <summary>
    /// <c>hidden</c>: content is clipped to the viewport.
    /// </summary>
    Hidden,

    /// <summary>
    /// <c>scroll</c>: a static renderer has no scrolling, so the content is clipped as with <see cref="Hidden"/>.
    /// </summary>
    Scroll,

    /// <summary>
    /// <c>auto</c>. SVG 1.1 treats it as <see cref="Visible"/>; CSS Overflow treats it as a scroll container that clips. Browsers differ, so the consumer decides.
    /// </summary>
    Auto
}
