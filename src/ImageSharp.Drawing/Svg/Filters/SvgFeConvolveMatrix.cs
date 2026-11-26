// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feConvolveMatrix</c> filter primitive, which applies a convolution kernel to its input. Not rendered yet: a
/// stub of package X3.
/// </summary>
/// <remarks>
/// <para>
/// It reads order, kernelMatrix, divisor, bias, targetX, targetY, edgeMode, kernelUnitLength, and preserveAlpha.
/// Several invalid values put the filter in error (SvgFilterBuildResult.Error).
/// </para>
/// <para>
/// Until the primitive is implemented, <see cref="Create"/> reports it as unsupported, so an element whose filter uses
/// it draws without the filter. The implementation replaces this file: a sealed subclass of
/// <see cref="SvgFilterPrimitive"/> whose <see cref="Create"/> returns <see cref="SvgFilterBuildResult.Created"/>.
/// </para>
/// </remarks>
internal static class SvgFeConvolveMatrix
{
    /// <summary>
    /// Creates the primitive of an <c>feConvolveMatrix</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">Always <see langword="null"/> for now.</param>
    /// <returns><see cref="SvgFilterBuildResult.Unsupported"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        return SvgFilterBuildResult.Unsupported;
    }
}
