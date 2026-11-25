// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The document-wide values that length units refer to: the resolution for absolute units, the root font size for
/// <c>rem</c>, and the size that the viewport-percentage units <c>vw</c>, <c>vh</c>, <c>vmin</c>, and <c>vmax</c> refer
/// to. <see cref="SvgUnits"/> uses it.
/// </summary>
internal readonly struct SvgUnitBasis
{
    /// <summary>
    /// The font size in pixels that <c>rem</c> refers to while the root element itself is computed: the initial value of
    /// <c>font-size</c>, <c>medium</c>.
    /// </summary>
    public const float InitialFontSize = 16F;

    /// <summary>
    /// The size that viewport-percentage units refer to while the intrinsic size of the document is not known yet, that
    /// is, in the properties and size of the root element: the 300 x 150 default size that browsers give a replaced
    /// element without a size.
    /// </summary>
    public static readonly SizeF DefaultViewportSize = new(300F, 150F);

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgUnitBasis"/> struct.
    /// </summary>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <param name="rootFontSize">The computed font size of the root element in pixels, for <c>rem</c>.</param>
    /// <param name="viewportSize">The size that <c>vw</c> and <c>vh</c> take 1% of.</param>
    public SvgUnitBasis(float dpi, float rootFontSize, SizeF viewportSize)
    {
        this.Dpi = dpi;
        this.RootFontSize = rootFontSize;
        this.ViewportSize = viewportSize;
    }

    /// <summary>
    /// Gets the pixels per inch for absolute units.
    /// </summary>
    public float Dpi { get; }

    /// <summary>
    /// Gets the computed font size of the root element in pixels, which <c>rem</c> refers to.
    /// </summary>
    public float RootFontSize { get; }

    /// <summary>
    /// Gets the size that <c>vw</c> and <c>vh</c> take 1% of: the intrinsic size of the document.
    /// </summary>
    public SizeF ViewportSize { get; }

    /// <summary>
    /// Creates the basis for the root element, before its font size and the intrinsic size of the document are known.
    /// </summary>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <returns>The basis.</returns>
    public static SvgUnitBasis ForRoot(float dpi) => new(dpi, InitialFontSize, DefaultViewportSize);
}
