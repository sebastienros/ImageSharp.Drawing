// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feComponentTransfer</c> filter primitive, which applies a transfer function (identity, table, discrete,
/// linear, gamma) to each channel of its input, on unpremultiplied values. Not rendered yet: a stub of package X2.
/// </summary>
/// <remarks>
/// <para>
/// Its children feFuncR, feFuncG, feFuncB, and feFuncA hold the functions; read their attributes with
/// SvgFilterBuildContext.TryGetValue(int, SvgAttributeId, out SvgFilterAttributeValue).
/// </para>
/// <para>
/// Until the primitive is implemented, <see cref="Create"/> reports it as unsupported, so an element whose filter uses
/// it draws without the filter. The implementation replaces this file: a sealed subclass of
/// <see cref="SvgFilterPrimitive"/> whose <see cref="Create"/> returns <see cref="SvgFilterBuildResult.Created"/>.
/// </para>
/// </remarks>
internal static class SvgFeComponentTransfer
{
    /// <summary>
    /// Creates the primitive of an <c>feComponentTransfer</c> element.
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
