// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One element of the <see cref="SvgElementTree"/>. Elements are stored in document order in one array, so this is a
/// record of indexes rather than an object with references.
/// </summary>
/// <remarks>
/// The subtree of the element at index <c>i</c> is the range <c>[i, SubtreeEnd)</c>. The first child, when there is one,
/// is at <c>i + 1</c>; the next sibling of a child <c>c</c> is at <c>c.SubtreeEnd</c> while that index is less than the
/// parent <see cref="SubtreeEnd"/>.
/// </remarks>
internal struct SvgElement
{
    /// <summary>
    /// Gets or sets the element kind.
    /// </summary>
    public SvgElementKind Kind { readonly get; set; }

    /// <summary>
    /// Gets or sets the local name of the element. Names come from the reader name table, so equal names of known elements
    /// are the same instance.
    /// </summary>
    public string LocalName { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the parent element, or -1 for the root.
    /// </summary>
    public int Parent { readonly get; set; }

    /// <summary>
    /// Gets or sets the index after the last element of the subtree.
    /// </summary>
    public int SubtreeEnd { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>id</c> attribute, or <see langword="null"/>.
    /// </summary>
    public string? Id { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the first class name in the class table.
    /// </summary>
    public int ClassStart { readonly get; set; }

    /// <summary>
    /// Gets or sets the number of class names.
    /// </summary>
    public int ClassCount { readonly get; set; }

    /// <summary>
    /// Gets or sets the index into the attribute table of the element kind, or -1 when the kind has no attribute table.
    /// </summary>
    public int DataIndex { readonly get; set; }

    /// <summary>
    /// Gets or sets the index into the transform table, or -1 when the element has no valid <c>transform</c>.
    /// </summary>
    public int TransformIndex { readonly get; set; }

    /// <summary>
    /// Gets or sets the index into the style table, or -1 when no style property is specified for the element.
    /// </summary>
    public int StyleIndex { readonly get; set; }
}
