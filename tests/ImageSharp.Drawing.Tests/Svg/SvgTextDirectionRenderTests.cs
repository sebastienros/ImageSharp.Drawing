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
/// Reference image tests for vertical and bidirectional SVG text. The documents use Open Sans, Noto Sans KR, and
/// me_quran from <c>TestFonts</c>. Each reference image was accepted after a comparison with the Chromium render oracle
/// with the same fonts: <c>node tests/SvgTools/render-svg.js --font-dir tests/ImageSharp.Drawing.Tests/TestFonts
/// --generic-family serif="Open Sans" --width 200 ...</c>; see <c>tests/SvgTools/README.md</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgTextDirectionRenderTests
{
    /// <summary>
    /// The font collection of the tests: Open Sans for Latin text, Noto Sans KR for CJK text with vertical metrics, and
    /// me_quran for Arabic text.
    /// </summary>
    private static readonly Lazy<FontCollection> Fonts = new(() =>
    {
        FontCollection collection = new();
        _ = collection.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        _ = collection.Add(TestFontUtilities.GetPath("NotoSansKR-Regular.otf"));
        _ = collection.Add(TestFontUtilities.GetPath("me_quran_volt_newmet.ttf"));
        return collection;
    });

    /// <summary>
    /// Gets the load options of the tests, with Open Sans as the default family.
    /// </summary>
    internal static SvgLoadOptions Options => new() { FontCollection = Fonts.Value, DefaultFontFamily = "Open Sans" };

    /// <summary>
    /// The feature file <c>tests/Images/Input/Svg/Features/text-direction.svg</c>: vertical text with upright CJK and
    /// sideways Latin glyphs, an anchor, spacing, and a shift in vertical text, <c>sideways-rl</c>, right-to-left Arabic
    /// with both anchors, bidirectional reordering with a position list, <c>bidi-override</c>, and letter spacing on
    /// Arabic.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void DrawSvg_TextDirection<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/text-direction.svg"), Options);
        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    /// <summary>
    /// The feature file <c>tests/Images/Input/Svg/Features/text-bidi.svg</c>: mixed Latin, Hebrew, and Arabic runs with
    /// paired brackets, a lone bracket, numbers in right-to-left text, an isolate next to a plain <c>tspan</c>, brackets in
    /// an embedding, and an override, as the Unicode bidirectional algorithm orders and mirrors them (package T6).
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(240, 200, PixelTypes.Rgba32)]
    public void DrawSvg_TextBidi<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/text-bidi.svg"), Options);
        using Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
