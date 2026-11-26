// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of a <c>text</c> or <c>tspan</c> element. The <see cref="SvgElement.DataIndex"/> of such an element
/// points to one of these.
/// </summary>
/// <remarks>
/// The position lists are ranges in the list tables of the element tree; see
/// <see cref="SvgElementTree.GetTextLengths(SvgListRange)"/> and <see cref="SvgElementTree.GetRotations(SvgListRange)"/>.
/// A list attribute that is missing or invalid has an empty range. An invalid list is dropped as a whole, as Chromium
/// drops an invalid attribute value.
/// </remarks>
internal struct SvgTextAttributes
{
    /// <summary>
    /// Gets or sets the absolute horizontal positions of <c>x</c>.
    /// </summary>
    public SvgListRange X { readonly get; set; }

    /// <summary>
    /// Gets or sets the absolute vertical positions of <c>y</c>.
    /// </summary>
    public SvgListRange Y { readonly get; set; }

    /// <summary>
    /// Gets or sets the horizontal shifts of <c>dx</c>.
    /// </summary>
    public SvgListRange Dx { readonly get; set; }

    /// <summary>
    /// Gets or sets the vertical shifts of <c>dy</c>.
    /// </summary>
    public SvgListRange Dy { readonly get; set; }

    /// <summary>
    /// Gets or sets the rotations of <c>rotate</c>, in degrees.
    /// </summary>
    public SvgListRange Rotate { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>textLength</c>: unspecified when missing, invalid, or negative.
    /// </summary>
    public SvgLength TextLength { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>lengthAdjust</c>. A missing or invalid value is <see cref="SvgLengthAdjust.Spacing"/>.
    /// </summary>
    public SvgLengthAdjust LengthAdjust { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the first character data node of the element's subtree in
    /// the node table that <see cref="SvgElementTree.GetTextNodes(int)"/> reads. Set on <c>text</c> elements only.
    /// </summary>
    public int FirstTextNode { readonly get; set; }

    /// <summary>
    /// Gets or sets the number of character data nodes of the element's subtree. Set on <c>text</c> elements only.
    /// </summary>
    public int TextNodeCount { readonly get; set; }
}
