// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feImage</c> filter primitive: renders an image resource or an element of the document into its subregion.
/// </summary>
/// <remarks>
/// <para>
/// Filter Effects Level 1: an image resource renders as the <c>image</c> element would, and a reference to an element
/// renders as the <c>use</c> element would. The resolve stage turns either into a nested document
/// (<see cref="SvgResolver.ResolveFilterImage"/>), which <see cref="Render"/> draws into the output.
/// </para>
/// <list type="bullet">
/// <item><description>An image resource loads under the rules of <c>image</c>: <c>data:</c> URIs, files only inside
/// <see cref="SvgLoadOptions.ResourceBaseDirectory"/>, and <see cref="SvgLimits.MaxImagePixels"/>. Its box (the pixel
/// rectangle of a raster image, the mapped box of an SVG image) maps into the subregion with the
/// <c>preserveAspectRatio</c> of the primitive, and the subregion clips it. <c>image-rendering</c> of the primitive
/// chooses the sampling of a raster image, as for <c>image</c>.</description></item>
/// <item><description>An element renders in the user space of the filtered element, translated by the origin of the
/// subregion, as the <c>x</c> and <c>y</c> of a <c>use</c> element translate its content; Chromium places it the same
/// way. The element keeps its own transform, opacity, and filter. A reference from inside the content back to the filter
/// is a cycle, which drops the element that closes it.</description></item>
/// </list>
/// <para>
/// A missing <c>href</c>, a reference to nothing, and a resource that does not load give a transparent result. The
/// rendered pixels are sRGB. The primitive is a generator flagged as a color filter, as <c>feFlood</c> is: its result is
/// written in sRGB when it is the last primitive and converted to the color space of the primitive otherwise.
/// </para>
/// </remarks>
internal sealed class SvgFeImage : SvgFilterPrimitive
{
    private readonly SvgDocument content;
    private readonly RectangleF? box;
    private readonly SvgPreserveAspectRatio preserveAspectRatio;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeImage"/> class.
    /// </summary>
    /// <param name="content">The nested document to render. Its node transforms map to the space of <paramref name="box"/>, or to the user space of the filtered element.</param>
    /// <param name="box">The box of an image resource, which maps into the subregion; <see langword="null"/> for an element, which is translated by the subregion origin.</param>
    /// <param name="preserveAspectRatio">The <c>preserveAspectRatio</c> of the primitive.</param>
    private SvgFeImage(SvgDocument content, RectangleF? box, SvgPreserveAspectRatio preserveAspectRatio)
        : base([])
    {
        this.content = content;
        this.box = box;
        this.preserveAspectRatio = preserveAspectRatio;
    }

    /// <inheritdoc/>
    public override bool IsColorFilter => true;

    /// <summary>
    /// Creates the primitive of an <c>feImage</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.Transparent"/> when there is nothing to render.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        ref readonly SvgFilterPrimitiveAttributes attributes = ref context.Attributes;
        if (attributes.Href is null)
        {
            return SvgFilterBuildResult.Transparent;
        }

        SvgImageRendering rendering = context.Style.ImageRendering;
        bool nearest = rendering is SvgImageRendering.OptimizeSpeed or SvgImageRendering.Pixelated or SvgImageRendering.CrispEdges;
        SvgDocument? content = context.Resolver.ResolveFilterImage(attributes.Href, nearest, context.Viewport, out RectangleF? box);
        if (content is null)
        {
            return SvgFilterBuildResult.Transparent;
        }

        primitive = new SvgFeImage(content, box, attributes.PreserveAspectRatio);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        RectangleF subregion = this.Subregion;
        Matrix3x2 documentToUser = this.box is RectangleF box
            ? SvgViewBoxTransform.Create(box, this.preserveAspectRatio, subregion)
            : Matrix3x2.CreateTranslation(subregion.X, subregion.Y);

        // The document renders sRGB pixels; the tag follows them, and the conversion to the color space that the engine
        // asks for is a no-op for the last primitive.
        output.ColorSpace = SvgColorInterpolation.SRgb;
        context.RenderDocument(this.content, documentToUser, output);
        SvgFilterColor.Convert(output, context.OutputColorSpace);
    }
}
