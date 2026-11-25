// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry and reference attributes of <c>use</c>. An unspecified or invalid length is <c>default(SvgLength)</c>.
/// </summary>
internal struct SvgUseAttributes
{
    /// <summary>
    /// Gets or sets the id of the referenced element, without the leading <c>#</c>. <c>href</c> wins over
    /// <c>xlink:href</c>. It is <see langword="null"/> when no reference is specified or when the reference is not a
    /// same-document fragment.
    /// </summary>
    public string? Href { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>x</c>.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c>.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c>. Never negative.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>. Never negative.
    /// </summary>
    public SvgLength Height { readonly get; set; }
}
