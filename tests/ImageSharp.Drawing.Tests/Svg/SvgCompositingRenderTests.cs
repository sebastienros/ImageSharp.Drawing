// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for the compositing features of SVG phase 2: <c>clipPath</c>, <c>mask</c>,
/// <c>mix-blend-mode</c>, <c>isolation</c>, <c>paint-order</c>, <c>switch</c> and the conditional attributes, the
/// <c>a</c> element, <c>vector-effect</c>, <c>overflow</c>, the root <c>transform</c>, and <c>shape-rendering</c>. The
/// reference images were accepted only after a comparison with the Chromium render oracle in <c>tests/SvgTools</c> at
/// the same size.
/// </summary>
[GroupOutput("Svg")]
public class SvgCompositingRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "clip-path")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "mask")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "mix-blend-mode")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "paint-order")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "switch")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "non-scaling-stroke")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "overflow")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "root-transform")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "shape-rendering")]
    public void DrawSvg_CompositingFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, name, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // The tolerance covers rasterizer differences between platforms only; the reference images themselves were
        // checked against the render oracle.
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            name,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
