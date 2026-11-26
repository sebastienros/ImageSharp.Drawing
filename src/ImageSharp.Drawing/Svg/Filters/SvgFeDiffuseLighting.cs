// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feDiffuseLighting</c> filter primitive: lights the alpha surface of its input with the diffuse term of the
/// Phong model, <c>kd * N.L * Light</c>, into an opaque image.
/// </summary>
/// <remarks>
/// <para>
/// Attributes: <c>surfaceScale</c> (initial 1) and <c>diffuseConstant</c> (initial 1) are numbers; a missing or invalid
/// value takes the initial value, as in Chromium. A negative <c>diffuseConstant</c> is computed as written and gives
/// black, as in Chromium and the resvg reference. <c>kernelUnitLength</c> is ignored, as Chromium ignores it.
/// <c>lighting-color</c> gives the light color. The surface, the light source, and the equations are described in
/// <see cref="SvgLighting"/> and <see cref="SvgLightSource"/>.
/// </para>
/// <para>
/// Without a light source child the result is transparent black, as in Chromium; Filter Effects asks for exactly one.
/// With several, the first one counts.
/// </para>
/// </remarks>
internal sealed class SvgFeDiffuseLighting : SvgFilterPrimitive
{
    private readonly SvgLightSource light;
    private readonly Rgba32 color;
    private readonly float surfaceScale;
    private readonly float diffuseConstant;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeDiffuseLighting"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="light">The light source.</param>
    /// <param name="color">The lighting color, sRGB.</param>
    /// <param name="surfaceScale">The surface scale.</param>
    /// <param name="diffuseConstant">The diffuse constant.</param>
    private SvgFeDiffuseLighting(int input, SvgLightSource light, Rgba32 color, float surfaceScale, float diffuseConstant)
        : base([input])
    {
        this.light = light;
        this.color = color;
        this.surfaceScale = surfaceScale;
        this.diffuseConstant = diffuseConstant;
    }

    /// <summary>
    /// Creates the primitive of an <c>feDiffuseLighting</c> element.
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

        primitive = new SvgFeDiffuseLighting(
            context.GetInput(context.Attributes.In),
            light,
            context.ResolveColor(context.Style.FilterColors.LightingColor).ToPixel<Rgba32>(),
            SvgLighting.GetNumber(context, SvgAttributeId.SurfaceScale, 1F),
            SvgLighting.GetNumber(context, SvgAttributeId.DiffuseConstant, 1F));
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context)
        => SvgLighting.GetInputArea(area);

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
        => SvgLighting.Render(in context, inputs[0], output, this.light, this.color, this.surfaceScale, false, this.diffuseConstant, 1F);
}
