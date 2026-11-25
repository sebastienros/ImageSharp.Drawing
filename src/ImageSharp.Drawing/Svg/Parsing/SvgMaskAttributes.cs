// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of <c>mask</c>. An unspecified or invalid length is <c>default(SvgLength)</c> and an unspecified keyword
/// is <c>Unspecified</c>; the consumer applies the defaults.
/// </summary>
internal struct SvgMaskAttributes
{
    /// <summary>
    /// Gets or sets <c>x</c>. The default is -10%.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c>. The default is -10%.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c>. The default is 120%. A negative value is kept, because SVG makes it an error that
    /// disables the mask, which differs from the default.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>. The default is 120%. A negative value is kept, as for <see cref="Width"/>.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>maskUnits</c>, which applies to <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, and
    /// <see cref="Height"/>. The default is <c>objectBoundingBox</c>.
    /// </summary>
    public SvgCoordinateUnits Units { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>maskContentUnits</c>, which applies to the content. The default is <c>userSpaceOnUse</c>.
    /// </summary>
    public SvgCoordinateUnits ContentUnits { readonly get; set; }
}
