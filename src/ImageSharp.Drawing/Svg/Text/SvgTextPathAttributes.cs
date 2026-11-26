// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of a <c>textPath</c> element. The <see cref="SvgElement.DataIndex"/> of such an element points to one
/// of these. <see cref="SvgTextPaths"/> lays text out on the path.
/// </summary>
internal struct SvgTextPathAttributes
{
    /// <summary>
    /// Gets or sets the id of the referenced element, from <c>href</c> or <c>xlink:href</c> (<c>href</c> wins), or
    /// <see langword="null"/> when there is no same-document reference.
    /// </summary>
    public string? Href { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>startOffset</c>: a length or a percentage of the path length; unspecified when missing or invalid.
    /// </summary>
    public SvgLength StartOffset { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>method</c>.
    /// </summary>
    public SvgTextPathMethod Method { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>spacing</c>.
    /// </summary>
    public SvgTextPathSpacing Spacing { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>side</c>.
    /// </summary>
    public SvgTextPathSide Side { readonly get; set; }

    /// <summary>
    /// Gets or sets the geometry of the SVG 2 <c>path</c> attribute, or <see langword="null"/> when it is missing. As for
    /// the <c>d</c> attribute, the valid prefix of the path data is kept.
    /// </summary>
    public IPath? Path { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>textLength</c>: unspecified when missing, invalid, or negative.
    /// </summary>
    public SvgLength TextLength { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>lengthAdjust</c>.
    /// </summary>
    public SvgLengthAdjust LengthAdjust { readonly get; set; }
}
