// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

/// <summary>
/// Checks the coverage that a rectangle clip gives each pixel: the product of the overlap of the pixel
/// with the rectangle on each axis when antialiased, and a test of the pixel centre when not.
/// </summary>
public class RectangleClipCoverageTests
{
    public static TheoryData<bool, bool, float, float, float, float> Cases { get; } = CreateCases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void FillThroughRectangleClip_HasAxisCoverage(bool antialias, bool difference, float left, float top, float width, float height)
    {
        const int imageWidth = 97;
        const int imageHeight = 9;
        using Image<Rgba32> image = new(imageWidth, imageHeight);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = antialias } };
        ClipOperation operation = difference ? ClipOperation.Difference : ClipOperation.Intersection;

        image.Mutate(c => c.Paint(options, canvas =>
        {
            canvas.Clip(operation, new RectanglePolygon(left, top, width, height));
            canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(0, 0, imageWidth, imageHeight));
        }));

        for (int y = 0; y < imageHeight; y++)
        {
            float yCoverage = AxisCoverage(y, top, top + height, antialias);
            for (int x = 0; x < imageWidth; x++)
            {
                float coverage = AxisCoverage(x, left, left + width, antialias) * yCoverage;
                if (difference)
                {
                    coverage = 1F - coverage;
                }

                int expected = (int)MathF.Round(coverage * 255F);
                int actual = image[x, y].A;
                Assert.True(Math.Abs(expected - actual) <= 1, $"Pixel ({x}, {y}): expected alpha {expected}, actual {actual}.");
            }
        }
    }

    private static float AxisCoverage(int pixel, float start, float end, bool antialias)
    {
        if (!antialias)
        {
            float centre = pixel + 0.5F;
            return centre >= start && centre <= end ? 1F : 0F;
        }

        return Math.Clamp(MathF.Min(pixel + 1F, end) - MathF.Max(pixel, start), 0F, 1F);
    }

    private static TheoryData<bool, bool, float, float, float, float> CreateCases()
    {
        TheoryData<bool, bool, float, float, float, float> data = [];
        (float Left, float Top, float Width, float Height)[] rectangles =
        [
            (0F, 0F, 97F, 9F),
            (10.25F, 1.5F, 60.5F, 6F),
            (-3.75F, -0.5F, 50F, 4.25F),
            (40.5F, 2F, 80F, 10F),
            (12F, 3F, 0.4F, 3F),
            (12.3F, 3.3F, 0.4F, 0.4F),
            (5.5F, 0.5F, 1F, 8F),
            (20F, 2F, 37F, 5F),
            (1E-9F, 0F, 96.99999F, 9F),
        ];

        foreach (bool antialias in new[] { true, false })
        {
            foreach (bool difference in new[] { false, true })
            {
                foreach ((float left, float top, float width, float height) in rectangles)
                {
                    data.Add(antialias, difference, left, top, width, height);
                }
            }
        }

        return data;
    }
}
