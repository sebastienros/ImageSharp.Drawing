// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for SVG filters: one document per primitive (<c>feFlood</c>, <c>feOffset</c> with
/// <c>feMerge</c>, <c>feGaussianBlur</c>, <c>feColorMatrix</c>, <c>feComposite</c>, <c>feBlend</c>) and per engine rule
/// (regions and units, subregions, color spaces, transforms, content outside the canvas, error cases, and the bounds of
/// layers around filtered content). The blur document also renders at a second size, because filters run on the pixel
/// grid of each render, and a blur accuracy document blurs hard edges and single pixels with standard deviations from
/// 0.5 to 12 pixels, across the switch from the Gaussian kernel to the three box blurs at 2 pixels. The reference images
/// were accepted only after a comparison with the Chromium render oracle in <c>tests/SvgTools</c> at the same size.
/// </summary>
[GroupOutput("Svg")]
public class SvgFilterRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-flood")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-offset-merge")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-blur")]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, "filter-blur")]
    [WithBlankImage(400, 200, PixelTypes.Rgba32, "filter-blur-accuracy")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-color-matrix")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-composite")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-blend")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-region")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-subregion")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-color-space")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-transform")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-outside-canvas")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-errors")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-bounds")]
    public void DrawSvg_FilterFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
        using Image<TPixel> image = provider.GetImage();
        string output = $"{name}-{image.Width}";
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, output, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // The tolerance covers rasterizer differences between platforms only; the reference images themselves were
        // checked against the render oracle.
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            output,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
