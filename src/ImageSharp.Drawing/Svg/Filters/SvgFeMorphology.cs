// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feMorphology</c> filter primitive, which erodes or dilates its input by a radius per axis. Not rendered yet:
/// a stub of package X3.
/// </summary>
/// <remarks>
/// <para>
/// It reads operator and radius.
/// </para>
/// <para>
/// Until the primitive is implemented, <see cref="Create"/> reports it as unsupported, so an element whose filter uses
/// it draws without the filter. The implementation replaces this file: a sealed subclass of
/// <see cref="SvgFilterPrimitive"/> whose <see cref="Create"/> returns <see cref="SvgFilterBuildResult.Created"/>.
/// </para>
/// </remarks>
internal static class SvgFeMorphology
{
    /// <summary>
    /// Creates the primitive of an <c>feMorphology</c> element.
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
