// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feFlood</c> filter primitive: fills its subregion with <c>flood-color</c> at <c>flood-opacity</c>.
/// </summary>
/// <remarks>
/// The color is an sRGB color. The primitive fills in the color space that the engine asks for: sRGB when it is the last
/// primitive, so the color comes out exact, as in Chromium; otherwise the color space of the primitive, converted in
/// floating point. The primitive has no input.
/// </remarks>
internal sealed class SvgFeFlood : SvgFilterPrimitive
{
    private readonly Vector4 color;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeFlood"/> class.
    /// </summary>
    /// <param name="color">The straight sRGB color, with the flood opacity multiplied into its alpha.</param>
    private SvgFeFlood(Vector4 color)
        : base([])
        => this.color = color;

    /// <inheritdoc/>
    public override bool HandlesColorSpaces => true;

    /// <summary>
    /// Creates the primitive of an <c>feFlood</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        SvgFilterStyle colors = context.Style.FilterColors;
        Vector4 color = context.ResolveColor(colors.FloodColor).ToPixel<RgbaVector>().ToScaledVector4();
        color.W *= colors.FloodOpacity;
        primitive = new SvgFeFlood(color);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
        => output.Pixels.Fill(SvgFilterColor.Premultiply(SvgFilterColor.FromSrgb(this.color, context.OutputColorSpace)));
}
