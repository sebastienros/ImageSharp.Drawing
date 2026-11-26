// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feSpecularLighting</c> filter primitive: lights the alpha surface of its input with the specular term of the
/// Phong model, <c>ks * pow(N.H, specularExponent) * Light</c>, into an image whose alpha is its largest color channel.
/// </summary>
/// <remarks>
/// <para>
/// Attributes: <c>surfaceScale</c> (initial 1), <c>specularConstant</c> (initial 1), and <c>specularExponent</c> (initial
/// 1) are numbers; a missing or invalid value takes the initial value. The exponent is clamped to the range 1 to 128 that
/// SVG 1.1 gives for it, as Chromium does; the resvg reference does not draw the element for an exponent of 0.
/// <c>kernelUnitLength</c> is ignored, as Chromium ignores it. <c>lighting-color</c> gives the light color.
/// </para>
/// <para>
/// Filter Effects allows any non-negative <c>specularConstant</c> and does not say what a negative one does. Chromium
/// then gives opaque black over the subregion, and so does this primitive. A constant of 0 follows the equation and gives
/// transparent black (Chromium gives opaque black for 0 too).
/// </para>
/// <para>
/// Without a light source child the result is transparent black, as in Chromium; with several, the first one counts.
/// </para>
/// </remarks>
internal sealed class SvgFeSpecularLighting : SvgFilterPrimitive
{
    /// <summary>
    /// The smallest specular exponent.
    /// </summary>
    internal const float MinExponent = 1F;

    /// <summary>
    /// The largest specular exponent.
    /// </summary>
    internal const float MaxExponent = 128F;

    private readonly SvgLightSource light;
    private readonly Rgba32 color;
    private readonly float surfaceScale;
    private readonly float specularConstant;
    private readonly float specularExponent;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeSpecularLighting"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="light">The light source.</param>
    /// <param name="color">The lighting color, sRGB.</param>
    /// <param name="surfaceScale">The surface scale.</param>
    /// <param name="specularConstant">The specular constant.</param>
    /// <param name="specularExponent">The specular exponent, in the range 1 to 128.</param>
    private SvgFeSpecularLighting(int input, SvgLightSource light, Rgba32 color, float surfaceScale, float specularConstant, float specularExponent)
        : base([input])
    {
        this.light = light;
        this.color = color;
        this.surfaceScale = surfaceScale;
        this.specularConstant = specularConstant;
        this.specularExponent = specularExponent;
    }

    /// <summary>
    /// Creates the primitive of an <c>feSpecularLighting</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.Transparent"/> without a light source.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        SvgLightSource? light = SvgLightSource.Create(context);
        if (light is null)
        {
            return SvgFilterBuildResult.Transparent;
        }

        float exponent = Math.Clamp(SvgLighting.GetNumber(context, SvgAttributeId.SpecularExponent, 1F), MinExponent, MaxExponent);
        primitive = new SvgFeSpecularLighting(
            context.GetInput(context.Attributes.In),
            light,
            context.ResolveColor(context.Style.FilterColors.LightingColor).ToPixel<Rgba32>(),
            SvgLighting.GetNumber(context, SvgAttributeId.SurfaceScale, 1F),
            SvgLighting.GetNumber(context, SvgAttributeId.SpecularConstant, 1F),
            exponent);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
        => SvgLighting.GetInputArea(area);

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        if (this.specularConstant < 0F)
        {
            output.Pixels.Fill(new Rgba32(0, 0, 0, 255));
            return;
        }

        SvgLighting.Render(in context, inputs[0], output, this.light, this.color, this.surfaceScale, true, this.specularConstant, this.specularExponent);
    }
}
