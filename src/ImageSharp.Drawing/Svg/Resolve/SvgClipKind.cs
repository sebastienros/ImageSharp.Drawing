// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// How a <c>clipPath</c> reference applies to the element that references it.
/// </summary>
internal enum SvgClipKind : byte
{
    /// <summary>
    /// The reference does not clip: it is <c>none</c>, names no <c>clipPath</c>, or is a cycle. Browsers ignore such a
    /// reference.
    /// </summary>
    None,

    /// <summary>
    /// The clip region is empty, so the element renders nothing: the <c>clipPath</c> has no child that adds geometry, or
    /// uses <c>objectBoundingBox</c> units with an empty bounding box.
    /// </summary>
    Empty,

    /// <summary>
    /// The clip region is the intersection of single paths, each with an outline that does not depend on the fill rule,
    /// so canvas clips express it exactly.
    /// </summary>
    Geometry,

    /// <summary>
    /// The clip region needs coverage: a union of several children, a child with its own <c>clip-path</c>, or an
    /// <c>evenodd</c> rule on a path that can cross itself. The clip draws its children into an alpha mask layer.
    /// </summary>
    Coverage
}
