// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for text on a path and for <c>textLength</c>. The documents use Open Sans from
/// <c>TestFonts</c>. Each reference image was accepted after a comparison with the Chromium render oracle with the same
/// font: <c>node tests/SvgTools/render-svg.js --font-dir tests/ImageSharp.Drawing.Tests/TestFonts --generic-family
/// serif="Open Sans" --width 200 ...</c>; see <c>tests/SvgTools/README.md</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgTextPathRenderTests
{
    /// <summary>
    /// The feature files in <c>tests/Images/Input/Svg/Features</c>: <c>text-path</c> (curves, arcs, a closed path with
    /// <c>side="right"</c>, <c>startOffset</c> as a length and a percentage, shifts and rotations on a path, the
    /// transform of a referenced path, the <c>path</c> attribute, and text after a path) and <c>text-length</c>
    /// (<c>textLength</c> with both <c>lengthAdjust</c> values on <c>text</c>, nested and single <c>tspan</c> elements,
    /// a percentage, an end anchor, and a path).
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-path")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-length")]
    public void DrawSvg_TextPathAndTextLength<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"), SvgTextRenderTests.Options);
        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, name, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            name,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
