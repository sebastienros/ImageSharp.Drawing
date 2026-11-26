// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of a <c>filter</c> element. An unspecified or invalid length is <c>default(SvgLength)</c> and an
/// unspecified keyword is <see cref="SvgCoordinateUnits.Unspecified"/>; the resolve stage applies the defaults.
/// </summary>
internal struct SvgFilterAttributes
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
    /// Gets or sets <c>width</c>. The default is 120%. A negative value is kept, because it differs from the default.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>. The default is 120%. A negative value is kept, as for <see cref="Width"/>.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>filterUnits</c>, which applies to <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, and
    /// <see cref="Height"/>. The default is <c>objectBoundingBox</c>.
    /// </summary>
    public SvgCoordinateUnits Units { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>primitiveUnits</c>, which applies to the subregions and the lengths of the primitives. The default
    /// is <c>userSpaceOnUse</c>.
    /// </summary>
    public SvgCoordinateUnits PrimitiveUnits { readonly get; set; }

    /// <summary>
    /// Gets or sets the id that <c>href</c> or <c>xlink:href</c> names, or <see langword="null"/>. SVG 1.1 lets a filter
    /// inherit attributes and primitives through it; Filter Effects removed it, and Chromium ignores it. The resolve
    /// stage does not follow it.
    /// </summary>
    public string? Href { readonly get; set; }
}
