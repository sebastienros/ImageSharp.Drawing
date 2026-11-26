// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>dominant-baseline</c> property, with the SVG 1.1 and the CSS Inline Layout keywords.
/// </summary>
internal enum SvgDominantBaseline : byte
{
    /// <summary>
    /// <c>auto</c>, the initial value.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>use-script</c> (SVG 1.1).
    /// </summary>
    UseScript,

    /// <summary>
    /// <c>no-change</c> (SVG 1.1).
    /// </summary>
    NoChange,

    /// <summary>
    /// <c>reset-size</c> (SVG 1.1).
    /// </summary>
    ResetSize,

    /// <summary>
    /// <c>alphabetic</c>.
    /// </summary>
    Alphabetic,

    /// <summary>
    /// <c>ideographic</c>.
    /// </summary>
    Ideographic,

    /// <summary>
    /// <c>middle</c>.
    /// </summary>
    Middle,

    /// <summary>
    /// <c>central</c>.
    /// </summary>
    Central,

    /// <summary>
    /// <c>mathematical</c>.
    /// </summary>
    Mathematical,

    /// <summary>
    /// <c>hanging</c>.
    /// </summary>
    Hanging,

    /// <summary>
    /// <c>text-before-edge</c>.
    /// </summary>
    TextBeforeEdge,

    /// <summary>
    /// <c>text-after-edge</c>.
    /// </summary>
    TextAfterEdge,

    /// <summary>
    /// <c>text-top</c>.
    /// </summary>
    TextTop,

    /// <summary>
    /// <c>text-bottom</c>.
    /// </summary>
    TextBottom
}
