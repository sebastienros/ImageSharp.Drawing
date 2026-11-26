// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for the filter primitives that move or combine neighboring pixels or generate noise:
/// <c>feMorphology</c>, <c>feConvolveMatrix</c>, <c>feDisplacementMap</c>, and <c>feTurbulence</c>, one document each.
/// The turbulence document also renders at a second size, because the noise is a function of user space and must not
/// change with the output scale. The reference images were accepted only after a comparison with the Chromium render
/// oracle in <c>tests/SvgTools</c> at the same size; the documents note where this renderer follows the specification
/// instead.
/// </summary>
[GroupOutput("Svg")]
public class SvgFilterSpatialRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-morphology")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-convolve")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-displacement")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-turbulence")]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, "filter-turbulence")]
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
