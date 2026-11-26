// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feImage</c> filter primitive, which renders an image resource or a referenced element into its subregion. Not
/// rendered yet: a stub of package X2.
/// </summary>
/// <remarks>
/// <para>
/// It reads href, preserveAspectRatio, and image-rendering; SvgFilterRenderContext.RenderDocument draws a document into
/// filter space.
/// </para>
/// <para>
/// Until the primitive is implemented, <see cref="Create"/> reports it as unsupported, so an element whose filter uses
/// it draws without the filter. The implementation replaces this file: a sealed subclass of
/// <see cref="SvgFilterPrimitive"/> whose <see cref="Create"/> returns <see cref="SvgFilterBuildResult.Created"/>.
/// </para>
/// </remarks>
internal static class SvgFeImage
{
    /// <summary>
    /// Creates the primitive of an <c>feImage</c> element.
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
