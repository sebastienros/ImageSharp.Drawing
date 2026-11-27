// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

/// <summary>
/// Checks that a gradient renderer paints every pixel of a row with the color that it gives for that pixel alone.
/// </summary>
public class GradientBrushRendererTests
{
    private const int Width = 211;
    private const int Height = 23;

    public static TheoryData<string, GradientRepetitionMode, PixelAlphaRepresentation> Cases { get; } = CreateCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_MatchesColorPerPixel_Rgba32(string brushName, GradientRepetitionMode mode, PixelAlphaRepresentation representation)
        => AssertRowMatchesPixels<Rgba32>(CreateBrush(brushName, mode, representation));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_MatchesColorPerPixel_Bgra32(string brushName, GradientRepetitionMode mode, PixelAlphaRepresentation representation)
        => AssertRowMatchesPixels<Bgra32>(CreateBrush(brushName, mode, representation));

    /// <summary>
    /// Selects the encoder that the alpha representation of the brush selects, and checks the rows.
    /// </summary>
    private static void AssertRowMatchesPixels<TPixel>(GradientBrush brush)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (brush.AlphaRepresentation == PixelAlphaRepresentation.Associated)
        {
            AssertRowMatchesPixels<TPixel, AssociatedGradientPixelEncoder<TPixel>>(brush);
        }
        else
        {
            AssertRowMatchesPixels<TPixel, UnassociatedGradientPixelEncoder<TPixel>>(brush);
        }
    }

    /// <summary>
    /// Renders every row through <see cref="BrushRenderer{TPixel}.Apply"/> with full coverage and source
    /// composition, which stores the source pixel, and compares it with the color the renderer gives for
    /// the pixel alone.
    /// </summary>
    private static void AssertRowMatchesPixels<TPixel, TEncoder>(GradientBrush brush)
        where TPixel : unmanaged, IPixel<TPixel>
        where TEncoder : struct, IGradientPixelEncoder<TPixel>
    {
        GraphicsOptions options = new() { AlphaCompositionMode = PixelAlphaCompositionMode.Src };
        BrushRenderer<TPixel> renderer = brush.CreateRenderer<TPixel>(Configuration.Default, options, Width, new RectangleF(0, 0, Width, Height));
        GradientBrush.GradientBrushRenderer<TPixel, TEncoder> gradient = Assert.IsAssignableFrom<GradientBrush.GradientBrushRenderer<TPixel, TEncoder>>(renderer);

        using BrushWorkspace<TPixel> workspace = new(MemoryAllocator.Default, Width);
        TPixel[] row = new TPixel[Width - 3];
        float[] coverage = new float[row.Length];
        coverage.AsSpan().Fill(1F);

        for (int y = 0; y < Height; y++)
        {
            Array.Clear(row);
            renderer.Apply(row, coverage, 3, y, workspace);
            for (int i = 0; i < row.Length; i++)
            {
                TPixel expected = gradient[3 + i, y];
                if (!expected.Equals(row[i]))
                {
                    Assert.Fail($"Pixel ({3 + i}, {y}) differs: expected {expected}, actual {row[i]}.");
                }
            }
        }
    }

    private static GradientBrush CreateBrush(string name, GradientRepetitionMode mode, PixelAlphaRepresentation representation)
    {
        ColorStop[] stops =
        [
            new(0.1F, Color.ParseHex("#FF000080")),
            new(0.35F, Color.ParseHex("#00FF00")),
            new(0.35F, Color.ParseHex("#0000FF20")),
            new(0.8F, Color.ParseHex("#FFFF00F0")),
        ];

        Matrix4x4 transform = Matrix4x4.CreateRotationZ(0.4F) * Matrix4x4.CreateScale(1.3F, 0.7F, 1F) * Matrix4x4.CreateTranslation(5F, -3F, 0F);
        return name switch
        {
            "Linear" => new LinearGradientBrush(new PointF(13.3F, 2F), new PointF(97.6F, 19F), mode, Matrix4x4.Identity, representation, stops),
            "LinearTransformed" => new LinearGradientBrush(new PointF(13.3F, 2F), new PointF(97.6F, 19F), mode, transform, representation, stops),
            "Radial" => new RadialGradientBrush(new PointF(60.5F, 11F), 47F, mode, Matrix4x4.Identity, representation, stops),
            "RadialTransformed" => new RadialGradientBrush(new PointF(60.5F, 11F), 47F, mode, transform, representation, stops),
            "TwoCircle" => new RadialGradientBrush(new PointF(40F, 10F), 5F, new PointF(90F, 12F), 60F, mode, Matrix4x4.Identity, representation, stops),
            "TwoCircleNarrow" => new RadialGradientBrush(new PointF(40F, 10F), 30F, new PointF(150F, 12F), 10F, mode, transform, representation, stops),
            _ => new EllipticGradientBrush(new PointF(100F, 11F), new PointF(160F, 30F), 0.4F, mode, Matrix4x4.Identity, representation, stops),
        };
    }

    private static TheoryData<string, GradientRepetitionMode, PixelAlphaRepresentation> CreateCases()
    {
        TheoryData<string, GradientRepetitionMode, PixelAlphaRepresentation> data = [];
        string[] brushes = ["Linear", "LinearTransformed", "Radial", "RadialTransformed", "TwoCircle", "TwoCircleNarrow", "Elliptic"];
        foreach (string brush in brushes)
        {
            foreach (GradientRepetitionMode mode in Enum.GetValues<GradientRepetitionMode>())
            {
                data.Add(brush, mode, PixelAlphaRepresentation.Associated);
            }

            data.Add(brush, GradientRepetitionMode.Reflect, PixelAlphaRepresentation.Unassociated);
        }

        return data;
    }
}
