// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The internal contract of one filter primitive: its inputs, its reach, the color space it needs, and one method that
/// fills its output from its inputs. Each primitive is one sealed type in its own file.
/// </summary>
/// <remarks>
/// <para>
/// Life cycle. The resolve stage creates the primitive through the static <c>Create</c> method of its type, which
/// <see cref="SvgFilterBuilder"/> calls with an <see cref="SvgFilterBuildContext"/>. The method reads the attributes
/// (<see cref="SvgFilterBuildContext.TryGetValue(SvgAttributeId, out SvgFilterAttributeValue)"/>), resolves the inputs (<see cref="SvgFilterBuildContext.GetInput"/>),
/// converts lengths with <c>primitiveUnits</c> (<see cref="SvgFilterBuildContext.ToUserX"/>), and returns an
/// <see cref="SvgFilterBuildResult"/>. The builder then sets <see cref="Subregion"/> and <see cref="ColorSpace"/>. After
/// that the primitive is immutable and shared by every render of the document, so a primitive keeps no per-render state
/// in fields.
/// </para>
/// <para>
/// Space. Everything a primitive stores is in the user space of the filtered element (lengths already converted from
/// <c>primitiveUnits</c>). At render time <see cref="SvgFilterRenderContext.Scale"/> gives filter pixels per user unit
/// along each axis; a primitive scales its lengths by it. The engine owns the images, clips every result to the
/// subregion, and converts the inputs to <see cref="ColorSpace"/> unless <see cref="PreservesColorSpace"/> is true.
/// </para>
/// <para>
/// Rendering. <see cref="Render"/> fills the output image, which the engine allocated transparent with the area that the
/// later primitives need, clipped to the subregion. An input image can have any area, including an empty one: a pixel
/// outside its area is transparent black. The inputs are premultiplied and in <see cref="ColorSpace"/> (or, for a
/// primitive that preserves the color space, all in the space of the first input); the output must be premultiplied and
/// in the same space. The engine never passes the output as an input.
/// </para>
/// </remarks>
internal abstract class SvgFilterPrimitive
{
    /// <summary>
    /// The input index of <c>SourceGraphic</c>: the element rendered without the filter.
    /// </summary>
    public const int SourceGraphic = -1;

    /// <summary>
    /// The input index of <c>SourceAlpha</c>: the alpha channel of <c>SourceGraphic</c>, with black color channels.
    /// </summary>
    public const int SourceAlpha = -2;

    private readonly int[] inputs;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterPrimitive"/> class.
    /// </summary>
    /// <param name="inputs">
    /// The inputs: the index of an earlier primitive of the graph, <see cref="SourceGraphic"/>, or <see cref="SourceAlpha"/>,
    /// as <see cref="SvgFilterBuildContext.GetInput"/> returns them. Empty for a primitive without input.
    /// </param>
    protected SvgFilterPrimitive(int[] inputs) => this.inputs = inputs;

    /// <summary>
    /// Gets the inputs, in the order in which <see cref="Render"/> receives them.
    /// </summary>
    public ReadOnlySpan<int> Inputs => this.inputs;

    /// <summary>
    /// Gets or sets the primitive subregion in the user space of the filtered element, with the defaults of Filter
    /// Effects applied. Only the builder sets it.
    /// </summary>
    public RectangleF Subregion { get; set; }

    /// <summary>
    /// Gets or sets the color space that the primitive computes in, from its <c>color-interpolation-filters</c>:
    /// <see cref="SvgColorInterpolation.SRgb"/> or <see cref="SvgColorInterpolation.LinearRgb"/>. Only the builder sets it.
    /// </summary>
    public SvgColorInterpolation ColorSpace { get; set; }

    /// <summary>
    /// Gets a value indicating whether the primitive only moves or combines pixels in a way that gives the same result in
    /// either color space, so that its inputs need no conversion. The output then has the color space of the inputs. When
    /// the inputs are in different color spaces, the engine converts them all to <see cref="ColorSpace"/>.
    /// </summary>
    public virtual bool PreservesColorSpace => false;

    /// <summary>
    /// Gets a value indicating whether the primitive maps each pixel on its own, as a color filter: it then reads its
    /// input in whatever color space the input has and writes its output in
    /// <see cref="SvgFilterRenderContext.OutputColorSpace"/>, converting in floating point with
    /// <see cref="SvgFilterColor.Convert(System.Numerics.Vector4, SvgColorInterpolation, SvgColorInterpolation)"/>, and the
    /// engine converts nothing for it.
    /// </summary>
    /// <remarks>
    /// Storing linear light in 8 bits loses the dark colors: every sRGB value up to 12 becomes 0 or 1. Chromium fuses the
    /// conversions into color filters, so a color matrix or a transfer function sees the exact converted value; a color
    /// filter primitive does the same, and the result of the last primitive is written in sRGB directly.
    /// </remarks>
    public virtual bool IsColorFilter => false;

    /// <summary>
    /// Gets a value indicating whether the default subregion is the filter region even when every input is the result of
    /// another primitive. Filter Effects makes this so for <c>feTile</c>, whose output extends past its input.
    /// </summary>
    public virtual bool HasFilterRegionAsDefaultSubregion => false;

    /// <summary>
    /// Returns the area of one input that the output needs: the reach of the primitive. The engine walks the graph from
    /// the last primitive backward with this method, so only pixels that can reach the visible output are computed.
    /// </summary>
    /// <remarks>
    /// The default is the output area, which is right for point operations and generators. A primitive whose output
    /// pixel depends on input pixels elsewhere grows or moves the area: by the kernel radius, by the offset, and so on. A
    /// primitive that cannot bound its reach returns a large area; the engine clips it to the filter region.
    /// </remarks>
    /// <param name="input">The position of the input in <see cref="Inputs"/>.</param>
    /// <param name="area">The output area that later primitives need, in filter space. It is not empty.</param>
    /// <param name="context">The context of the run. Only its scale, subregion, and filter region are set during this walk.</param>
    /// <returns>The input area in filter space.</returns>
    public virtual Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context) => area;

    /// <summary>
    /// Fills the output image from the input images.
    /// </summary>
    /// <param name="context">The context of the run.</param>
    /// <param name="inputs">The input images, in the order of <see cref="Inputs"/>.</param>
    /// <param name="output">The output image: transparent, with an area inside the subregion of the primitive, and not empty.</param>
    public abstract void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output);
}
