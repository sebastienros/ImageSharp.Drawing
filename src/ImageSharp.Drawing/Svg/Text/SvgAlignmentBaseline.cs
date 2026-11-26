// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>alignment-baseline</c> property, with the SVG 1.1 and the CSS Inline Layout keywords.
/// </summary>
internal enum SvgAlignmentBaseline : byte
{
    /// <summary>
    /// <c>auto</c>, the initial value.
    /// </summary>
    Auto,

    /// <summary>
    /// <c>baseline</c>.
    /// </summary>
    Baseline,

    /// <summary>
    /// <c>before-edge</c> (SVG 1.1).
    /// </summary>
    BeforeEdge,

    /// <summary>
    /// <c>text-before-edge</c>.
    /// </summary>
    TextBeforeEdge,

    /// <summary>
    /// <c>middle</c>.
    /// </summary>
    Middle,

    /// <summary>
    /// <c>central</c>.
    /// </summary>
    Central,

    /// <summary>
    /// <c>after-edge</c> (SVG 1.1).
    /// </summary>
    AfterEdge,

    /// <summary>
    /// <c>text-after-edge</c>.
    /// </summary>
    TextAfterEdge,

    /// <summary>
    /// <c>ideographic</c>.
    /// </summary>
    Ideographic,

    /// <summary>
    /// <c>alphabetic</c>.
    /// </summary>
    Alphabetic,

    /// <summary>
    /// <c>hanging</c>.
    /// </summary>
    Hanging,

    /// <summary>
    /// <c>mathematical</c>.
    /// </summary>
    Mathematical,

    /// <summary>
    /// <c>top</c>.
    /// </summary>
    Top,

    /// <summary>
    /// <c>center</c>.
    /// </summary>
    Center,

    /// <summary>
    /// <c>bottom</c>.
    /// </summary>
    Bottom,

    /// <summary>
    /// <c>text-top</c>.
    /// </summary>
    TextTop,

    /// <summary>
    /// <c>text-bottom</c>.
    /// </summary>
    TextBottom
}
