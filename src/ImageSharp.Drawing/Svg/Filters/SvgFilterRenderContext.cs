// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The state of one filter run that a primitive sees: the mapping from user space to filter space, the regions in
/// filter space, the color space of the primitive, and the services of the engine.
/// </summary>
/// <remarks>
/// <para>
/// Filter space is a pixel grid along the axes of the user space of the filtered element: a user space point
/// <c>p</c> lies at <c>p * Scale + Translation</c>, and filter pixel <c>(i, j)</c> covers <c>[i, i + 1) x [j, j + 1)</c>.
/// When the element is drawn with only a scale and a translation, filter space is the device pixel grid. A length of a
/// primitive scales by <see cref="Scale"/>.X horizontally and <see cref="Scale"/>.Y vertically.
/// </para>
/// <para>
/// The context is created by the engine per primitive and per run, and is valid only during the call it is passed to.
/// </para>
/// </remarks>
internal readonly struct SvgFilterRenderContext
{
    private readonly SvgFilterEngine engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterRenderContext"/> struct.
    /// </summary>
    /// <param name="engine">The engine of the run.</param>
    /// <param name="filterRegion">The filter region in filter space, in whole pixels.</param>
    /// <param name="subregion">The subregion of the primitive in filter space, in whole pixels, inside the filter region.</param>
    /// <param name="colorSpace">The color space of the primitive.</param>
    /// <param name="outputColorSpace">The color space that the output image is in.</param>
    public SvgFilterRenderContext(
        SvgFilterEngine engine,
        Rectangle filterRegion,
        Rectangle subregion,
        SvgColorInterpolation colorSpace,
        SvgColorInterpolation outputColorSpace)
    {
        this.engine = engine;
        this.FilterRegion = filterRegion;
        this.Subregion = subregion;
        this.ColorSpace = colorSpace;
        this.OutputColorSpace = outputColorSpace;
    }

    /// <summary>
    /// Gets the number of filter pixels per user unit along the x and the y axis. Both are greater than zero.
    /// </summary>
    public Vector2 Scale => this.engine.Scale;

    /// <summary>
    /// Gets the position of the user space origin in filter space.
    /// </summary>
    public Vector2 Translation => this.engine.Translation;

    /// <summary>
    /// Gets the transform from the user space of the filtered element to filter space. It only scales and translates.
    /// </summary>
    public Matrix3x2 UserToFilter => new(this.Scale.X, 0F, 0F, this.Scale.Y, this.Translation.X, this.Translation.Y);

    /// <summary>
    /// Gets the filter region in filter space, in whole pixels. No result has pixels outside it.
    /// </summary>
    public Rectangle FilterRegion { get; }

    /// <summary>
    /// Gets the subregion of the primitive in filter space, in whole pixels, clipped to the filter region. The output of
    /// the primitive is transparent outside it.
    /// </summary>
    public Rectangle Subregion { get; }

    /// <summary>
    /// Gets the color space that the primitive computes in.
    /// </summary>
    public SvgColorInterpolation ColorSpace { get; }

    /// <summary>
    /// Gets the color space of the output image. It is <see cref="ColorSpace"/>, except for a color filter primitive
    /// (<see cref="SvgFilterPrimitive.IsColorFilter"/>), which the engine can ask for another space, and for a primitive
    /// that preserves the color space of its inputs.
    /// </summary>
    public SvgColorInterpolation OutputColorSpace { get; }

    /// <summary>
    /// Gets the allocator for scratch memory. A primitive returns what it allocates before it returns.
    /// </summary>
    public MemoryAllocator Allocator => this.engine.Allocator;

    /// <summary>
    /// Maps a rectangle from user space to filter space, without rounding.
    /// </summary>
    /// <param name="rectangle">The rectangle in user space.</param>
    /// <returns>The rectangle in filter space.</returns>
    public RectangleF ToFilter(RectangleF rectangle)
        => new(
            (rectangle.X * this.Scale.X) + this.Translation.X,
            (rectangle.Y * this.Scale.Y) + this.Translation.Y,
            rectangle.Width * this.Scale.X,
            rectangle.Height * this.Scale.Y);

    /// <summary>
    /// Renders a document into an image of filter space, source-over onto its pixels, as <c>SourceGraphic</c> renders and
    /// as <c>feImage</c> renders a referenced element or a nested document. The image keeps its color space tag; the
    /// rendered pixels are sRGB.
    /// </summary>
    /// <param name="document">The document. Its node transforms map to its user space.</param>
    /// <param name="documentToUser">The transform from the user space of the document to the user space of the filtered element. It must only scale and translate.</param>
    /// <param name="target">The image that receives the pixels.</param>
    public void RenderDocument(SvgDocument document, Matrix3x2 documentToUser, SvgFilterImage target)
        => this.engine.RenderDocument(document, documentToUser, target);
}
