// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for the CSS features of the renderer: custom properties and <c>var()</c>, the geometry
/// properties and <c>d</c>, the CSS <c>transform</c> with <c>transform-box</c>, the color functions of CSS Color 4 and
/// 5, and the math functions of CSS Values 4. The reference images were accepted only after a comparison with the
/// Chromium render oracle in <c>tests/SvgTools</c> at the same size.
/// </summary>
[GroupOutput("Svg")]
public class SvgCssFeatureRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "css-var")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "css-geometry")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "css-transform")]
    [WithBlankImage(200, 125, PixelTypes.Rgba32, "css-color")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "css-calc")]
    public void DrawSvg_CssFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
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
