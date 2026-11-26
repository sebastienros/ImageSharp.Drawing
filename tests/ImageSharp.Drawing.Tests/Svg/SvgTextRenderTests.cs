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
/// Reference image tests for SVG text. The documents use Open Sans from <c>TestFonts</c>, the only font of the
/// collection. Each reference image was accepted after a comparison with the Chromium render oracle with the same font:
/// <c>node tests/SvgTools/render-svg.js --font-dir tests/ImageSharp.Drawing.Tests/TestFonts --generic-family
/// serif="Open Sans" --width 200 ...</c>; see <c>tests/SvgTools/README.md</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgTextRenderTests
{
    /// <summary>
    /// The font collection of the tests: Open Sans only.
    /// </summary>
    private static readonly Lazy<FontCollection> Fonts = new(() =>
    {
        FontCollection collection = new();
        _ = collection.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        return collection;
    });

    /// <summary>
    /// Gets the load options of the tests: the Open Sans collection, which is also the default family.
    /// </summary>
    internal static SvgLoadOptions Options => new() { FontCollection = Fonts.Value, DefaultFontFamily = "Open Sans" };

    /// <summary>
    /// The text feature files in <c>tests/Images/Input/Svg/Features</c>: layout (positions, anchors, spacing, white
    /// space), paint (paint servers in both unit types, patterns, paint order, opacity on a span), and compositing (text
    /// in a clip path, a mask, and pattern content, text through use, transformed text).
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-layout")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-paint")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "text-compositing")]
    public void DrawSvg_Text<TPixel>(TestImageProvider<TPixel> provider, string name)
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
