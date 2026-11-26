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
/// Reference image tests for SVG 2 and CSS features: CSS basic shapes in <c>clip-path</c>, <c>pathLength</c>, the
/// <c>:lang()</c> selector, the font feature and font variant properties, the white space properties, <c>textPath</c>
/// with <c>pathLength</c>, and <c>paint-order</c>, <c>vector-effect</c>, and <c>mix-blend-mode</c> on text and through
/// <c>use</c>. Each reference image was accepted after a comparison with the Chromium render oracle in
/// <c>tests/SvgTools</c> at the same size; the text documents with
/// <c>--font-dir tests/ImageSharp.Drawing.Tests/TestFonts --generic-family serif="Open Sans"</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgSvg2RenderTests
{
    /// <summary>
    /// The font collection of the text documents: Open Sans, Inter Light, and Noto Sans KR from <c>TestFonts</c>.
    /// </summary>
    private static readonly Lazy<FontCollection> Fonts = new(() =>
    {
        FontCollection collection = new();
        _ = collection.Add(TestFontUtilities.GetPath("OpenSans-Regular.ttf"));
        _ = collection.Add(TestFontUtilities.GetPath("Inter-Light.ttf"));
        _ = collection.Add(TestFontUtilities.GetPath("NotoSansKR-Regular.otf"));
        return collection;
    });

    /// <summary>
    /// Gets the load options of the text documents.
    /// </summary>
    internal static SvgLoadOptions TextOptions => new() { FontCollection = Fonts.Value, DefaultFontFamily = "Open Sans" };

    /// <summary>
    /// The documents without text, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(400, 500, PixelTypes.Rgba32, "clip-path-shape")]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, "clip-path-shape-root")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "path-length")]
    [WithBlankImage(200, 50, PixelTypes.Rgba32, "lang-selector")]
    public void DrawSvg_Svg2Feature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
        => Draw(provider, name, new SvgLoadOptions());

    /// <summary>
    /// The text documents, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(400, 526, PixelTypes.Rgba32, "text-font-features")]
    [WithBlankImage(400, 200, PixelTypes.Rgba32, "text-paint-use")]
    public void DrawSvg_Svg2TextFeature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
        => Draw(provider, name, TextOptions);

    private static void Draw<TPixel>(TestImageProvider<TPixel> provider, string name, SvgLoadOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg"), options);
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
