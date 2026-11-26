// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for the lighting filter primitives: <c>feDiffuseLighting</c> and <c>feSpecularLighting</c> with
/// each light source (<c>feDistantLight</c>, <c>fePointLight</c>, <c>feSpotLight</c>), <c>primitiveUnits</c> set to
/// <c>objectBoundingBox</c>, light positions in a transformed user space, the linearRGB and sRGB color spaces, and invalid
/// and extreme values. The diffuse document also renders at twice the size, because light positions and
/// <c>surfaceScale</c> must follow the scale of the render, and the units document renders stretched to twice its width,
/// a scale that differs per axis. The reference images were accepted only after a comparison with the Chromium render
/// oracle in <c>tests/SvgTools</c> at the same size.
/// </summary>
[GroupOutput("Svg")]
public class SvgFilterLightingRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-lighting-diffuse")]
    [WithBlankImage(400, 400, PixelTypes.Rgba32, "filter-lighting-diffuse")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-lighting-specular")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-lighting-units")]
    [WithBlankImage(400, 200, PixelTypes.Rgba32, "filter-lighting-units")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-lighting-errors")]
    public void DrawSvg_LightingFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"));
        using Image<TPixel> image = provider.GetImage();
        string output = image.Width == image.Height ? $"{name}-{image.Width}" : $"{name}-{image.Width}x{image.Height}";
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
