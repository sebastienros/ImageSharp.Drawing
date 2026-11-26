// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The outcome of resolving a <c>filter</c> element for one filtered element.
/// </summary>
internal enum SvgFilterOutcome : byte
{
    /// <summary>
    /// The filter resolved to a graph that the element renders through.
    /// </summary>
    Graph,

    /// <summary>
    /// The filter makes the element draw nothing: it has no primitives, or a primitive is in error.
    /// </summary>
    NotRendered,

    /// <summary>
    /// The filter uses a primitive that this build does not render; the element draws without the filter.
    /// </summary>
    Unsupported
}
