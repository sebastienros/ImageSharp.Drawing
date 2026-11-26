// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for SVG filters in combination with the other features. Each reference image was compared
/// with the Chromium render oracle at the same size, with Open Sans as the only font (<c>node tests/SvgTools/render-svg.js
/// --font-dir &lt;dir with OpenSans-Regular.ttf&gt; --generic-family "serif=Open Sans" --width 200 ...</c>; see
/// <c>tests/SvgTools/README.md</c>). The renders differ from Chromium by antialiasing only: at most 6 pixels of the
/// compositing document and none of the others differ by more than 24 levels outside the glyphs of the text, which
/// Chromium hints, and the edge of the unfiltered circle of the root document, where Chromium covers less than the
/// exact area (alpha 86 where this renderer gives 156) with or without the filter.
/// </summary>
[GroupOutput("Svg")]
public class SvgFilterIntegrationRenderTests
{
    /// <summary>
    /// The feature files in <c>tests/Images/Input/Svg/Features</c>:
    /// <list type="bullet">
    /// <item><description><c>filter-integration-compositing</c>: one element with a filter, <c>clip-path</c>,
    /// <c>mask</c>, <c>opacity</c>, and a blend mode; filtered elements under a coverage clip, a blend group, an opacity
    /// group, a rotated clip, and a skewed mask.</description></item>
    /// <item><description><c>filter-integration-content</c>: filters inside <c>pattern</c>, <c>marker</c>, and
    /// <c>mask</c> content; on <c>use</c>, a nested <c>svg</c>, <c>image</c>, and <c>text</c>; nested
    /// filters.</description></item>
    /// <item><description><c>filter-integration-root</c>: a filter on the root element over filtered
    /// content.</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-integration-compositing")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-integration-content")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "filter-integration-root")]
    public void DrawSvg_FilterIntegration<TPixel>(TestImageProvider<TPixel> provider, string name)
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
