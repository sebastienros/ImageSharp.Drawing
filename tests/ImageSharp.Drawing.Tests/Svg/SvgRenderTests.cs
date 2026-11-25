// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Reference image tests for <see cref="DrawingCanvas.DrawSvg(SvgDocument, RectangleF)"/>. Each test draws one SVG file
/// into a transparent image of the given size. The reference images were accepted only after a comparison with the
/// Chromium render oracle in <c>tests/SvgTools</c> at the same size; see <c>tests/SvgTools/README.md</c>.
/// </summary>
[GroupOutput("Svg")]
public class SvgRenderTests
{
    /// <summary>
    /// The feature files: one small document per feature, in <c>tests/Images/Input/Svg/Features</c>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="name">The file name without extension.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "shapes")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "fill-rule")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "stroke-caps-joins")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "stroke-dash")]
    [WithBlankImage(240, 240, PixelTypes.Rgba32, "stroke-dash-scaled")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "stroke-width-opacity")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "transforms")]
    [WithBlankImage(480, 400, PixelTypes.Rgba32, "viewbox-nested")]
    [WithBlankImage(300, 150, PixelTypes.Rgba32, "root-align")]
    [WithBlankImage(300, 120, PixelTypes.Rgba32, "root-stretch")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "use-symbol")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "gradient-linear")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "gradient-radial")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "gradient-href")]
    [WithBlankImage(300, 300, PixelTypes.Rgba32, "gradient-transformed")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "opacity")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "css")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "css-selectors")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "current-color")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "units")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "display-visibility")]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, "transform-origin")]
    public void DrawSvg_Feature<TPixel>(TestImageProvider<TPixel> provider, string name)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, SvgDocument.Load(TestFile.GetInputFileFullPath($"Svg/Features/{name}.svg")), name);

    /// <summary>
    /// The Ghostscript Tiger, the path fill and stroke benchmark document.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32)]
    public void DrawSvg_Tiger<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, SvgDocument.Load(TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger)), "tiger");

    /// <summary>
    /// The Paris map with 50,000 paths. The file has no size and no view box, so the test gives it the 1096 x 1060
    /// viewport that the benchmarks and the oracle use.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(548, 530, PixelTypes.Rgba32)]
    public void DrawSvg_Paris<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string text = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.Svg.Paris30k));
        int root = text.IndexOf("<svg", StringComparison.Ordinal) + 4;
        text = string.Concat(text.AsSpan(0, root), " viewBox=\"0 0 1096 1060\"", text.AsSpan(root));

        RunTest(provider, SvgDocument.Parse(text), "paris");
    }

    /// <summary>
    /// The icons, the two illustrations, and the two design-tool exports of the benchmark corpus.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="file">The corpus file.</param>
    [Theory]
    [WithBlankImage(256, 256, PixelTypes.Rgba32, TestImages.Svg.FluentSoftIceCream)]
    [WithBlankImage(256, 256, PixelTypes.Rgba32, TestImages.Svg.FluentMoose)]
    [WithBlankImage(128, 128, PixelTypes.Rgba32, TestImages.Svg.CarbonCrab)]
    [WithBlankImage(400, 300, PixelTypes.Rgba32, TestImages.Svg.StyledLandscape)]
    public void DrawSvg_Corpus<TPixel>(TestImageProvider<TPixel> provider, string file)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, SvgDocument.Load(TestFile.GetInputFileFullPath(file)), System.IO.Path.GetFileNameWithoutExtension(file));

    /// <summary>
    /// Every icon of the corpus at 96 pixels.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(96, 96, PixelTypes.Rgba32)]
    public void DrawSvg_Icons<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Draw every icon before the first comparison, so one run writes all outputs.
        List<Image<TPixel>> images = [];
        try
        {
            foreach (string file in TestImages.Svg.Icons)
            {
                images.Add(Draw(provider, SvgDocument.Load(TestFile.GetInputFileFullPath(file)), System.IO.Path.GetFileNameWithoutExtension(file)));
            }

            for (int i = 0; i < images.Count; i++)
            {
                Compare(provider, images[i], System.IO.Path.GetFileNameWithoutExtension(TestImages.Svg.Icons[i]));
            }
        }
        finally
        {
            foreach (Image<TPixel> image in images)
            {
                image.Dispose();
            }
        }
    }

    /// <summary>
    /// Drawing at an origin uses the intrinsic size, so the result equals drawing into the rectangle of that size.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(240, 220, PixelTypes.Rgba32)]
    public void DrawSvg_AtOrigin_EqualsIntrinsicDestination<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Load(TestFile.GetInputFileFullPath("Svg/Features/shapes.svg"));

        using Image<TPixel> atOrigin = provider.GetImage();
        using Image<TPixel> inRectangle = provider.GetImage();
        atOrigin.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new PointF(20, 10))));
        inRectangle.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(20, 10, 200, 200))));

        ImageComparer.Exact.VerifySimilarity(inRectangle, atOrigin);
        Assert.Equal(Color.Transparent.ToPixel<TPixel>(), atOrigin[5, 5]);
        Assert.NotEqual(Color.Transparent.ToPixel<TPixel>(), atOrigin[30, 20]);
    }

    /// <summary>
    /// The canvas transform at the call applies to the whole document, and the canvas state is the same after the call.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void DrawSvg_ComposesWithCanvasTransform<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        SvgDocument document = SvgDocument.Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"#ff0000\"/></svg>");

        using Image<TPixel> image = provider.GetImage();
        int saveCount = 0;
        int afterCount = 0;
        image.Mutate(x => x.Paint(
            new DrawingOptions { Transform = System.Numerics.Matrix4x4.CreateTranslation(100, 50, 0) },
            canvas =>
            {
                saveCount = canvas.SaveCount;
                canvas.DrawSvg(document, new RectangleF(0, 0, 40, 40));
                afterCount = canvas.SaveCount;
            }));

        Assert.Equal(saveCount, afterCount);
        Assert.Equal(Color.Red.ToPixel<TPixel>(), image[120, 70]);
        Assert.Equal(Color.Transparent.ToPixel<TPixel>(), image[20, 20]);
        Assert.Equal(Color.Transparent.ToPixel<TPixel>(), image[145, 70]);
    }

    /// <summary>
    /// Draws a document into the whole image and compares the result with its reference image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="document">The document.</param>
    /// <param name="name">The name of the output file.</param>
    private static void RunTest<TPixel>(TestImageProvider<TPixel> provider, SvgDocument document, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = Draw(provider, document, name);
        Compare(provider, image, name);
    }

    /// <summary>
    /// Draws a document into the whole image of the provider and saves the result to the test output.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="document">The document.</param>
    /// <param name="name">The name of the output file.</param>
    /// <returns>The image. The caller disposes it.</returns>
    private static Image<TPixel> Draw<TPixel>(TestImageProvider<TPixel> provider, SvgDocument document, string name)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Image<TPixel> image = provider.GetImage();
        image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
        image.DebugSave(provider, name, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        return image;
    }

    /// <summary>
    /// Compares an image with its reference image. The tolerance covers rasterizer differences between platforms only;
    /// the reference images themselves were checked against the render oracle.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="image">The image.</param>
    /// <param name="name">The name of the reference file.</param>
    private static void Compare<TPixel>(TestImageProvider<TPixel> provider, Image<TPixel> image, string name)
        where TPixel : unmanaged, IPixel<TPixel>
        => image.CompareToReferenceOutput(
            ImageComparer.TolerantPercentage(0.01F),
            provider,
            name,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
}
