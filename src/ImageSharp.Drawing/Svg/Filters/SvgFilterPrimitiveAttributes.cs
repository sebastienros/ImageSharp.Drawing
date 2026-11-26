// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The attributes of a filter primitive (<c>feBlend</c> to <c>feTurbulence</c>) or of a child of one (<c>feMergeNode</c>,
/// the transfer functions, and the light sources).
/// </summary>
/// <remarks>
/// <para>
/// The attributes that every primitive has are typed fields. Every other attribute of Filter Effects Level 1 is stored
/// as an <see cref="SvgFilterAttributeValue"/> in the value table of <see cref="SvgFilterTables"/>; the values of one
/// element are contiguous, from <see cref="ValueStart"/>, because the reader reads all attributes of an element before
/// its children. Read them with <see cref="SvgElementTree.TryGetFilterValue(int, SvgAttributeId, out SvgFilterAttributeValue)"/>.
/// </para>
/// <para>
/// The light sources store <c>x</c> and <c>y</c> as numbers in the value table, because they are numbers there, not
/// lengths; their <see cref="X"/> and <see cref="Y"/> stay unspecified.
/// </para>
/// </remarks>
internal struct SvgFilterPrimitiveAttributes
{
    /// <summary>
    /// Gets or sets <c>x</c> of the primitive subregion, or unspecified.
    /// </summary>
    public SvgLength X { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>y</c> of the primitive subregion, or unspecified.
    /// </summary>
    public SvgLength Y { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>width</c> of the primitive subregion, or unspecified. A negative value is kept.
    /// </summary>
    public SvgLength Width { readonly get; set; }

    /// <summary>
    /// Gets or sets <c>height</c> of the primitive subregion, or unspecified. A negative value is kept.
    /// </summary>
    public SvgLength Height { readonly get; set; }

    /// <summary>
    /// Gets or sets the trimmed <c>in</c> attribute, or <see langword="null"/> when it is missing.
    /// </summary>
    public string? In { readonly get; set; }

    /// <summary>
    /// Gets or sets the trimmed <c>in2</c> attribute, or <see langword="null"/> when it is missing.
    /// </summary>
    public string? In2 { readonly get; set; }

    /// <summary>
    /// Gets or sets the trimmed <c>result</c> attribute, or <see langword="null"/> when it is missing or empty.
    /// </summary>
    public string? Result { readonly get; set; }

    /// <summary>
    /// Gets or sets the whole <c>href</c> URL of an <c>feImage</c>, trimmed, or <see langword="null"/>. Like the URL of an
    /// <c>image</c>, it can name a resource or, with a fragment, an element of the document.
    /// </summary>
    public string? Href { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>preserveAspectRatio</c> of an <c>feImage</c>.
    /// </summary>
    public SvgPreserveAspectRatio PreserveAspectRatio { readonly get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="PreserveAspectRatio"/> was specified with a valid value.
    /// </summary>
    public bool HasPreserveAspectRatio { readonly get; set; }

    /// <summary>
    /// Gets or sets the index of the first value of the element in the value table.
    /// </summary>
    public int ValueStart { readonly get; set; }

    /// <summary>
    /// Gets or sets the number of values of the element in the value table.
    /// </summary>
    public int ValueCount { readonly get; set; }
}
