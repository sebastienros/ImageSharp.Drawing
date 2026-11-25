// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The geometry attributes of <c>line</c>. An unspecified or invalid attribute is <c>default(SvgLength)</c>, which means 0.
/// </summary>
internal struct SvgLineAttributes
{
    /// <summary>
    /// Gets or sets <c>x1</c>.
    /// </summary>
    public SvgLength X1 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y1</c>.
    /// </summary>
    public SvgLength Y1 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>x2</c>.
    /// </summary>
    public SvgLength X2 { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y2</c>.
    /// </summary>
    public SvgLength Y2 { readonly get; set; }
}
