// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The layer effect of a luminance mask: it writes the luminance of each pixel, times its alpha, into the alpha channel.
/// The mask layer then composites with <see cref="PixelAlphaCompositionMode.DestIn"/>, which keeps the layer below only
/// where the mask has coverage.
/// </summary>
/// <remarks>
/// <para>
/// CSS Masking defines the coverage of a luminance mask as <c>(0.2125 R + 0.7154 G + 0.0721 B) × A</c> on straight
/// (not premultiplied) color values. The product with alpha is not linear in straight values, so a
/// <see cref="ColorMatrixLayerEffect"/>, which transforms straight values, cannot express it.
/// </para>
/// <para>
/// The canvas runs the effect on a copy of the layer region when the layer closes, before the layer composites, and
/// writes the result back in place. The effect changes only alpha; DestIn ignores the color channels. The effects are
/// immutable and shared, so a render allocates no effect.
/// </para>
/// </remarks>
internal sealed class SvgMaskLayerEffect : LayerEffect
{
    /// <summary>
    /// The luminance coefficient of red.
    /// </summary>
    private const float RedCoefficient = 0.2125F;

    /// <summary>
    /// The luminance coefficient of green.
    /// </summary>
    private const float GreenCoefficient = 0.7154F;

    /// <summary>
    /// The luminance coefficient of blue.
    /// </summary>
    private const float BlueCoefficient = 0.0721F;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgMaskLayerEffect"/> class.
    /// </summary>
    /// <param name="operation">The operation that implements the effect.</param>
    private SvgMaskLayerEffect(Action<IImageProcessingContext> operation)
        : base(operation)
    {
    }

    /// <summary>
    /// Gets the effect for luminance on sRGB values, the default of <c>mask-type: luminance</c>.
    /// </summary>
    /// <remarks>
    /// The rows convert with unassociated alpha, which <see cref="LuminanceToAlpha"/> needs. For a pixel format that stores
    /// straight alpha this is its native representation, so the modifier changes nothing for such a format.
    /// </remarks>
    public static SvgMaskLayerEffect Luminance { get; }
        = new(static context => context.ProcessPixelRowsAsVector4(LuminanceToAlpha, PixelConversionModifiers.UnPremultiply));

    /// <summary>
    /// Gets the effect for luminance on linear-light values, for <c>color-interpolation: linearRGB</c> on the mask. The
    /// conversion expands the sRGB transfer function before the operation sees the values.
    /// </summary>
    public static SvgMaskLayerEffect LinearLuminance { get; }
        = new(static context => context.ProcessPixelRowsAsVector4(
            LuminanceToAlpha,
            PixelConversionModifiers.SRgbCompand | PixelConversionModifiers.UnPremultiply));

    /// <inheritdoc/>
    public override int Reach => 0;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions => null;

    /// <inheritdoc/>
    public override Point WriteBackOffset => default;

    /// <inheritdoc/>
    public override bool IsPassThrough => false;

    /// <summary>
    /// Returns the effect for a mask mode.
    /// </summary>
    /// <param name="mode">The mask mode.</param>
    /// <returns>The effect, or <see langword="null"/> for <see cref="SvgMaskMode.Alpha"/>, which uses the alpha as it is.</returns>
    public static SvgMaskLayerEffect? Get(SvgMaskMode mode)
        => mode switch
        {
            SvgMaskMode.Luminance => Luminance,
            SvgMaskMode.LinearLuminance => LinearLuminance,
            _ => null
        };

    /// <summary>
    /// Replaces the alpha of each pixel of a row with its luminance times its alpha.
    /// </summary>
    /// <param name="row">The pixels as straight RGBA values in the range 0 to 1.</param>
    private static void LuminanceToAlpha(Span<Vector4> row)
    {
        for (int i = 0; i < row.Length; i++)
        {
            ref Vector4 pixel = ref row[i];
            pixel.W *= (RedCoefficient * pixel.X) + (GreenCoefficient * pixel.Y) + (BlueCoefficient * pixel.Z);
        }
    }
}
