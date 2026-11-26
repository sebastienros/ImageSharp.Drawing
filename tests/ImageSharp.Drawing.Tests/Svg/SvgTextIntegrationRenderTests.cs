// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for SVG text features in combination. Each reference image was compared with the Chromium
/// render oracle with the same fonts (<c>node tests/SvgTools/render-svg.js --font-dir &lt;dir with the fonts of the
/// test&gt; --generic-family serif="Open Sans" --width 200 ...</c>; see <c>tests/SvgTools/README.md</c>). Glyph
/// positions match Chromium up to its hinting. The decoration lines differ where the renderer follows the font metrics
/// and SVG 1.1 (thickness, overline position, the paint of nested lines), as <c>SvgResolver.TextDecorations.cs</c>
/// documents; Chromium also does not render the <c>switch</c> branch for the language <c>en</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgTextIntegrationRenderTests
{
    /// <summary>
    /// The feature files in <c>tests/Images/Input/Svg/Features</c>:
    /// <list type="bullet">
    /// <item><description><c>text-integration-vertical</c> (Open Sans, Noto Sans KR, me_quran): baselines,
    /// decorations, and <c>textLength</c> in vertical text, vertical text on a path, <c>sideways-lr</c>, and
    /// decorations and <c>textLength</c> in right-to-left and bidirectional text.</description></item>
    /// <item><description><c>text-integration-paint</c> (Open Sans, Inter Light): paint servers in both unit types on
    /// text on a path and with decorations, text in <c>clipPath</c>, <c>mask</c>, <c>pattern</c>, and <c>marker</c>
    /// content, <c>use</c> of text, <c>switch</c> with <c>systemLanguage</c>, and CSS selectors that set font and
    /// decoration properties.</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-integration-vertical")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-integration-paint")]
    public void DrawSvg_TextIntegration<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgLoadOptions options = name == "text-integration-vertical"
            ? SvgTextDirectionRenderTests.Options
            : SvgTextDecorationBaselineRenderTests.Options;
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"), options);
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
