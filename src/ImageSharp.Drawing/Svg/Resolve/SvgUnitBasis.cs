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
        : this(dpi, rootFontSize, viewportSize, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgUnitBasis"/> struct with the style context of a resolve.
    /// </summary>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <param name="rootFontSize">The computed font size of the root element in pixels, for <c>rem</c>.</param>
    /// <param name="viewportSize">The size that <c>vw</c> and <c>vh</c> take 1% of.</param>
    /// <param name="context">The style context of the resolve, or <see langword="null"/> outside one.</param>
    public SvgUnitBasis(float dpi, float rootFontSize, SizeF viewportSize, SvgStyleContext? context)
    {
        this.Dpi = dpi;
        this.RootFontSize = rootFontSize;
        this.ViewportSize = viewportSize;
        this.Context = context;
        this.Font = null;
    }

    /// <summary>
    /// Gets the style context of the resolve: <c>var()</c> substitution, the math functions that mix units, and the font
    /// metrics of <c>ch</c>. <see langword="null"/> outside a resolve; then such values resolve to their fallbacks.
    /// </summary>
    public SvgStyleContext? Context { get; }

    /// <summary>
    /// Gets the computed text style whose first available font <c>ch</c> refers to, or <see langword="null"/> when the
    /// caller did not name one; then <c>ch</c> is half the font size, the fallback of CSS Values 4.
    /// </summary>
    public SvgTextStyle? Font { get; private init; }

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

    /// <summary>
    /// Creates the basis for the root element with the style context of a resolve.
    /// </summary>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <param name="context">The style context of the resolve.</param>
    /// <returns>The basis.</returns>
    public static SvgUnitBasis ForRoot(float dpi, SvgStyleContext? context) => new(dpi, InitialFontSize, DefaultViewportSize, context);

    /// <summary>
    /// Returns this basis for the root element: the same resolution and context, with the values that apply before the
    /// root font size and the intrinsic size are known.
    /// </summary>
    /// <returns>The basis.</returns>
    public SvgUnitBasis ForRoot() => new(this.Dpi, InitialFontSize, DefaultViewportSize, this.Context);

    /// <summary>
    /// Returns this basis with the font that <c>ch</c> refers to. The font is looked up only when a length uses it.
    /// </summary>
    /// <param name="text">The computed text style of the element whose font the unit uses.</param>
    /// <returns>The basis.</returns>
    public SvgUnitBasis WithFont(SvgTextStyle text) => this.Context is null ? this : this with { Font = text };
}
