// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The outcome of creating one filter primitive from its element. The specification decides per primitive and per
/// attribute which outcome an invalid value has.
/// </summary>
internal enum SvgFilterBuildResult : byte
{
    /// <summary>
    /// The primitive was created.
    /// </summary>
    Created,

    /// <summary>
    /// The primitive is disabled and its result is its first input, as Filter Effects defines for example for a zero
    /// <c>stdDeviation</c>. The builder resolves the <c>in</c> attribute of the element as that input.
    /// </summary>
    PassThrough,

    /// <summary>
    /// The primitive is disabled and its result is transparent black.
    /// </summary>
    Transparent,

    /// <summary>
    /// This build does not render the primitive. The element draws without the filter, so that it does not disappear.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The primitive is in error, which puts the whole filter in error: the element that references the filter does not
    /// render.
    /// </summary>
    Error
}
