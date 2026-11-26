// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feTile</c> filter primitive, which repeats the subregion of its input over its own subregion. Not rendered
/// yet: a stub of package X2.
/// </summary>
/// <remarks>
/// <para>
/// Its default subregion is the filter region (override HasFilterRegionAsDefaultSubregion); its reach is the whole
/// input subregion.
/// </para>
/// <para>
/// Until the primitive is implemented, <see cref="Create"/> reports it as unsupported, so an element whose filter uses
/// it draws without the filter. The implementation replaces this file: a sealed subclass of
/// <see cref="SvgFilterPrimitive"/> whose <see cref="Create"/> returns <see cref="SvgFilterBuildResult.Created"/>.
/// </para>
/// </remarks>
internal static class SvgFeTile
{
    /// <summary>
    /// Creates the primitive of an <c>feTile</c> element.
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
