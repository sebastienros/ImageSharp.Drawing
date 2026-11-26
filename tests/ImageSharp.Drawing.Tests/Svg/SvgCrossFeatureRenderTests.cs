// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests where the CSS features, the SVG 2 features, and the earlier features meet: <c>var()</c> and
/// math functions inside basic shapes and <c>d</c>, <c>ch</c> in basic shapes, a CSS <c>d</c> with <c>pathLength</c>,
/// dashes, markers, and <c>textPath</c>, geometry from CSS with paint servers, masks, filters, clips, and <c>use</c>,
/// the CSS <c>transform</c> with clip shapes, <c>var()</c> in paint, geometry, filters, and font features, the new color
/// functions in paint servers and filters, <c>:lang()</c> rules that set geometry and custom properties, clip shapes
/// with masks and filters, <c>ch</c> in attributes, a CSS transform with percentages on the root and on a path that
/// <c>textPath</c> references, math functions in <c>transform-origin</c> of paint servers, and the document of the
/// decoder test with text, an image, filters, custom properties, and clip shapes, and text inside embedded SVG images,
/// which draws with the fonts of the outer load options. Each reference image was accepted after a comparison with the
/// Chromium render oracle in <c>tests/SvgTools</c> at the same size, with
/// <c>--font-dir tests/ImageSharp.Drawing.Tests/TestFonts --generic-family serif="Open Sans"</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgCrossFeatureRenderTests
{
    /// <summary>
    /// The documents, in <c>tests/Images/Input/Svg/Features</c>, drawn with the fonts of
    /// <see cref="SvgSvg2RenderTests.TextOptions"/>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-clip-var-calc")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-clip-ch")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-d-path-length")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-geometry-boxes")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-transform-clip")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-var")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-color")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "cross-lang")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-clip-mask-filter")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "cross-ch-attributes")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "cross-text-path-transform")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "cross-root-transform")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "cross-paint-origin")]
    [WithBlankImage(160, 120, PixelTypes.Rgba32, "cross-decoder")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "cross-embedded-text")]
    public void DrawSvg_CrossFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"), SvgSvg2RenderTests.TextOptions);
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
