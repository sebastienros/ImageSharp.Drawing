// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Prototype tests for the canvas mapping of an SVG mask: a content layer, and inside it a mask layer that composites
/// with <see cref="PixelAlphaCompositionMode.DestIn"/> and carries a luminance-to-alpha effect.
/// </summary>
public class SvgMaskLayerPrototypeTests
{
    private static readonly GraphicsOptions DestIn = new() { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn };

    [Fact]
    public void NestedDestInLayer_KeepsContentWhereMaskIsOpaque()
    {
        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas =>
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(0, 0, 20, 20));
            _ = canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 20, 20));
            canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(0, 0, 20, 20));
            _ = canvas.SaveLayer(DestIn, new Rectangle(0, 0, 20, 20));
            canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(0, 0, 10, 20));
            canvas.Restore();
            canvas.Restore();
        }));

        Assert.Equal(new Rgba32(0, 0, 255, 255), image[5, 5]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), image[15, 5]);
    }

    [Fact]
    public void NestedDestInLayer_ClipInsideMaskLayer_ClearsOutsideRegion()
    {
        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas =>
        {
            _ = canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 20, 20));
            canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(0, 0, 20, 20));
            _ = canvas.SaveLayer(DestIn, new Rectangle(0, 0, 20, 20));
            canvas.Clip(new RectanglePolygon(0, 0, 10, 10));
            canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(0, 0, 20, 20));
            canvas.Restore();
            canvas.Restore();
        }));

        Assert.Equal(new Rgba32(0, 0, 255, 255), image[5, 5]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[15, 5]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image[5, 15]);
    }

    [Fact]
    public void NestedDestInLayer_EffectRunsBeforeComposite()
    {
        using Image<Rgba32> image = new(20, 20);
        image.Mutate(x => x.Paint(canvas =>
        {
            _ = canvas.SaveLayer(new GraphicsOptions(), new Rectangle(0, 0, 20, 20));
            canvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(0, 0, 20, 20));
            _ = canvas.SaveLayer(DestIn, new Rectangle(0, 0, 20, 20), SvgMaskLayerEffect.Luminance);
            canvas.Fill(Brushes.Solid(Color.Black), new RectanglePolygon(0, 0, 10, 10));
            canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(10, 0, 10, 10));
            canvas.Fill(Brushes.Solid(Color.White.WithAlpha(0.5F)), new RectanglePolygon(0, 10, 10, 10));
            canvas.Restore();
            canvas.Restore();
        }));

        Assert.Equal(0, image[5, 5].A);
        Assert.Equal(new Rgba32(0, 0, 255, 255), image[15, 5]);
        Assert.Equal(0, image[15, 15].A);

        // Luminance times alpha: a white pixel at half alpha keeps half of the content.
        Assert.InRange(image[5, 15].A, 120, 135);
    }
}
