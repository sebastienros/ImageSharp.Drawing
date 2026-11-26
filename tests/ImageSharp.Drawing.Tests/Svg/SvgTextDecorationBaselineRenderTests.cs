// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for <c>text-decoration</c> and the baseline properties of SVG text. The documents use Open Sans
/// and Inter Light from <c>TestFonts</c>. Each reference image was compared with the Chromium render oracle with the same
/// fonts: <c>node tests/SvgTools/render-svg.js --font-dir tests/ImageSharp.Drawing.Tests/TestFonts --generic-family
/// serif="Open Sans" --width 200 ...</c>; see <c>tests/SvgTools/README.md</c>. The baselines match Chromium up to its
/// rounding of the ascent and descent to whole pixels. The decorations differ from Chromium where the renderer follows
/// the font metrics and SVG 1.1, as <c>SvgResolver.TextDecorations.cs</c> documents: thickness, overline position, the
/// paint of nested lines and of a line set on a <c>g</c>; Chromium also leaves gaps in underlines at descenders
/// (<c>text-decoration-skip-ink</c>), which is not done here.
/// </summary>
[GroupOutput("Svg")]
public class SvgTextDecorationBaselineRenderTests
{
    /// <summary>
    /// The font collection of the tests: Open Sans and Inter Light.
    /// </summary>
    private static readonly Lazy<FontCollection> Fonts = new(() =>
    {
        FontCollection collection = new();
        _ = collection.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        _ = collection.Add(TestFontUtilities.GetPath("Inter-Light.ttf"));
        return collection;
    });

    /// <summary>
    /// Gets the load options of the tests: the collection of the tests, with Open Sans as the default family.
    /// </summary>
    internal static SvgLoadOptions Options => new() { FontCollection = Fonts.Value, DefaultFontFamily = "Open Sans" };

    /// <summary>
    /// The feature files in <c>tests/Images/Input/Svg/Features</c>: decorations (lines, paint from the element that sets
    /// the line, a line set outside the text, spacing, positions, rotation, a clip path, a hidden character) and
    /// baselines (dominant and alignment baselines, nested and cumulative baseline shifts).
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-decoration")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-baseline")]
    public void DrawSvg_TextDecorationAndBaselines<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"), Options);
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
