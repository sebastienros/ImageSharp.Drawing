// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feTurbulence</c> filter primitive: generates Perlin turbulence or fractal noise over its subregion.
/// </summary>
/// <remarks>
/// <para>
/// The noise is the algorithm of the reference code in Filter Effects Level 1 (section 9.21): the Park and Miller
/// random number generator seeds a lattice of 256 gradient vectors per channel and a permutation table; the noise of a
/// channel at a point interpolates the gradients of the four lattice points around it with the s-curve
/// <c>3 t^2 - 2 t^3</c>; and an octave loop sums the noise (fractal noise) or its absolute value (turbulence) at doubling
/// frequencies with halving weights. Fractal noise maps to a channel value with <c>(sum + 1) / 2</c>, turbulence with
/// <c>sum</c>, both clamped to 0 to 1. The channel values are unpremultiplied colors in the color space of the
/// primitive. With <c>stitchTiles="stitch"</c> the frequencies and the lattice wrap at the subregion as the reference
/// code does.
/// </para>
/// <para>
/// One difference from the reference code of Filter Effects Level 1: its setup draws a new gradient when a vector is
/// longer than 1 ("respect uniformity", a change since the 2014 draft). The reference code of SVG 1.1 normalizes every
/// vector instead. Chromium renders the SVG 1.1 table: with it, every turbulence render of the conformance suite matches
/// the Chromium oracle within one or two 8-bit levels, while with the newer table the noise is a different pattern.
/// The setup here follows SVG 1.1, and keeps the guard of the newer code against a zero vector (the SVG 1.1 code would
/// divide by zero there).
/// </para>
/// <para>
/// Points: the reference code leaves open which point a pixel samples. The noise of filter pixel <c>(i, j)</c> is
/// evaluated at the user space point of the pixel center plus half a user unit on each axis, which is the sampling
/// point of Chromium (measured at three output scales and a fractional filter region origin). The noise is therefore a
/// function of user space: it does not change with the output scale or the position of the subregion.
/// <c>baseFrequency</c> is in user units for both <c>primitiveUnits</c>, as the specification does not make it
/// relative to the bounding box and Chromium also renders it.
/// </para>
/// <para>
/// Attributes: <c>baseFrequency</c> holds one or two numbers (missing or another count: the initial 0); a negative
/// value is unsupported in Filter Effects, and the result is then transparent black, as Chromium renders it.
/// <c>numOctaves</c> is an integer (missing or not one integer: the initial 1), clamped to 0 to
/// <see cref="MaxOctaves"/>: the specification lets an implementation clamp it for 8-bit color, and Chromium renders
/// identical results from 9 octaves on. <c>seed</c> is truncated toward zero. <c>type</c> is <c>fractalNoise</c> or
/// <c>turbulence</c> (the initial value, also for an invalid keyword), and <c>stitchTiles</c> is <c>stitch</c> or
/// <c>noStitch</c> (the initial value). The work per pixel is four channels times the number of octaves.
/// </para>
/// </remarks>
internal sealed class SvgFeTurbulence : SvgFilterPrimitive
{
    /// <summary>
    /// The largest number of octaves.
    /// </summary>
    internal const int MaxOctaves = 9;

    // Constants of the reference code.
    private const int RandM = 2147483647; // 2^31 - 1
    private const int RandA = 16807; // 7^5, a primitive root of RandM
    private const int RandQ = 127773; // RandM / RandA
    private const int RandR = 2836; // RandM % RandA
    private const int BSize = 0x100;
    private const int BMask = 0xff;
    private const int PerlinN = 0x1000;

    // The permutation table of the reference code, uLatticeSelector, of BSize + BSize + 2 entries.
    private readonly int[] lattice;

    // The gradients of the reference code, fGradient[channel][index][component], stored per lattice index with the four
    // channels in the lanes of one vector: gradientX[index] holds component 0 of the R, G, B, and A gradients, and
    // gradientY[index] component 1. One lattice lookup then serves all four channels.
    private readonly Vector4[] gradientX;
    private readonly Vector4[] gradientY;
    private readonly Vector2 baseFrequency;
    private readonly int octaves;
    private readonly bool fractalNoise;
    private readonly bool stitch;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeTurbulence"/> class.
    /// </summary>
    /// <param name="seed">The seed, truncated.</param>
    /// <param name="baseFrequency">The base frequencies along x and y in user units; neither is negative.</param>
    /// <param name="octaves">The number of octaves, from 0 to <see cref="MaxOctaves"/>.</param>
    /// <param name="fractalNoise">Whether the type is <c>fractalNoise</c>; otherwise <c>turbulence</c>.</param>
    /// <param name="stitch">Whether the tiles are stitched.</param>
    private SvgFeTurbulence(long seed, Vector2 baseFrequency, int octaves, bool fractalNoise, bool stitch)
        : base([])
    {
        this.baseFrequency = baseFrequency;
        this.octaves = octaves;
        this.fractalNoise = fractalNoise;
        this.stitch = stitch;
        this.lattice = new int[BSize + BSize + 2];
        this.gradientX = new Vector4[BSize + BSize + 2];
        this.gradientY = new Vector4[BSize + BSize + 2];
        this.Initialize(seed);
    }

    /// <summary>
    /// Creates the primitive of an <c>feTurbulence</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>, or <see cref="SvgFilterBuildResult.Transparent"/> for a negative frequency.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        primitive = null;
        Vector2 frequency = Vector2.Zero;
        if (context.TryGetValue(SvgAttributeId.BaseFrequency, out SvgFilterAttributeValue value) && value.NumberCount is 1 or 2)
        {
            ReadOnlySpan<float> numbers = context.GetNumbers(in value);
            frequency = new Vector2(numbers[0], numbers.Length == 2 ? numbers[1] : numbers[0]);
        }

        if (frequency.X < 0F || frequency.Y < 0F)
        {
            return SvgFilterBuildResult.Transparent;
        }

        int octaves = 1;
        if (context.TryGetValue(SvgAttributeId.NumOctaves, out SvgFilterAttributeValue octaveValue) && octaveValue.NumberCount == 1)
        {
            float number = context.GetNumbers(in octaveValue)[0];
            octaves = number == MathF.Floor(number) ? (int)Math.Clamp(number, 0F, MaxOctaves) : 1;
        }

        // Truncation toward zero; the clamp keeps the conversion defined, and the setup maps every seed into the range
        // of the generator.
        long seed = 0;
        if (context.TryGetValue(SvgAttributeId.Seed, out SvgFilterAttributeValue seedValue) && seedValue.NumberCount == 1)
        {
            seed = (long)Math.Clamp(context.GetNumbers(in seedValue)[0], -1e12F, 1e12F);
        }

        bool fractalNoise = context.TryGetValue(SvgAttributeId.Type, out SvgFilterAttributeValue type) && type.Text == "fractalNoise";
        bool stitch = context.TryGetValue(SvgAttributeId.StitchTiles, out SvgFilterAttributeValue stitchValue) && stitchValue.Text == "stitch";
        primitive = new SvgFeTurbulence(seed, frequency, octaves, fractalNoise, stitch);
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        Rectangle area = output.Area;
        Vector2 scale = context.Scale;
        Vector2 translation = context.Translation;
        double frequencyX = this.baseFrequency.X;
        double frequencyY = this.baseFrequency.Y;
        Stitch stitch = default;
        bool stitching = this.stitch;
        if (stitching)
        {
            // The tile of the reference code is the primitive subregion in user space.
            RectangleF tile = this.Subregion;
            frequencyX = AdjustFrequency(frequencyX, tile.Width);
            frequencyY = AdjustFrequency(frequencyY, tile.Height);
            stitch.Width = (int)((tile.Width * frequencyX) + 0.5);
            stitch.WrapX = (int)((tile.X * frequencyX) + PerlinN + stitch.Width);
            stitch.Height = (int)((tile.Height * frequencyY) + 0.5);
            stitch.WrapY = (int)((tile.Y * frequencyY) + PerlinN + stitch.Height);
        }

        Vector4 half = new(0.5F);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            // The user space point of the pixel center, plus half a user unit (see the remarks).
            double pointY = ((y + 0.5 - translation.Y) / scale.Y) + 0.5;
            Span<Rgba32> row = output.GetRow(y);
            for (int i = 0; i < row.Length; i++)
            {
                double pointX = ((area.Left + i + 0.5 - translation.X) / scale.X) + 0.5;
                Vector4 sum = this.Turbulence(pointX * frequencyX, pointY * frequencyY, stitching, stitch);
                Vector4 color = this.fractalNoise ? (sum + Vector4.One) * half : sum;
                row[i] = SvgFilterColor.Premultiply(color);
            }
        }
    }

    /// <summary>
    /// Adjusts a frequency for stitching, as the reference code does: to the nearest frequency, by ratio, that fits a
    /// whole number of lattice cells in the tile.
    /// </summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="size">The tile size along the axis.</param>
    /// <returns>The adjusted frequency.</returns>
    private static double AdjustFrequency(double frequency, double size)
    {
        if (frequency == 0)
        {
            return frequency;
        }

        double low = Math.Floor(size * frequency) / size;
        double high = Math.Ceiling(size * frequency) / size;
        return frequency / low < high / frequency ? low : high;
    }

    /// <summary>
    /// The random number generator of the reference code (Park and Miller): <c>r = (a * r) mod m</c>, with Schrage's
    /// method so that the product does not overflow.
    /// </summary>
    /// <param name="seed">The previous number, in the range 1 to 2^31 - 2.</param>
    /// <returns>The next number, in the same range.</returns>
    internal static long Random(long seed)
    {
        long result = (RandA * (seed % RandQ)) - (RandR * (seed / RandQ));
        return result <= 0 ? result + RandM : result;
    }

    /// <summary>
    /// The s-curve of the reference code.
    /// </summary>
    /// <param name="t">The fraction, in the range 0 to 1.</param>
    /// <returns><c>3 t^2 - 2 t^3</c>.</returns>
    private static float SCurve(float t) => t * t * (3F - (2F * t));

    /// <summary>
    /// Fills the lattice and the gradients from a seed: the <c>init</c> function of the reference code.
    /// </summary>
    /// <param name="seed">The seed attribute, truncated.</param>
    private void Initialize(long seed)
    {
        // setup_seed: map the seed into the range of the generator.
        if (seed <= 0)
        {
            seed = -(seed % (RandM - 1)) + 1;
        }

        if (seed > RandM - 1)
        {
            seed = RandM - 1;
        }

        // The gradients of the four channels in turn: two random numbers per vector, each mapped to [-1, 1) in steps of
        // 1/256, normalized to unit length (the SVG 1.1 setup; see the remarks). A zero vector draws again.
        Span<float> gradient = stackalloc float[2];
        Span<float> components = stackalloc float[BSize * 2 * 4];
        for (int k = 0; k < 4; k++)
        {
            for (int i = 0; i < BSize; i++)
            {
                this.lattice[i] = i;
                do
                {
                    for (int j = 0; j < 2; j++)
                    {
                        seed = Random(seed);
                        gradient[j] = (float)((seed % (BSize + BSize)) - BSize) / BSize;
                    }
                }
                while (gradient[0] == 0F && gradient[1] == 0F);

                // The reference code divides in double precision and stores doubles; the gradients are stored as floats.
                double length = Math.Sqrt((gradient[0] * (double)gradient[0]) + (gradient[1] * (double)gradient[1]));
                components[(((k * BSize) + i) * 2) + 0] = (float)(gradient[0] / length);
                components[(((k * BSize) + i) * 2) + 1] = (float)(gradient[1] / length);
            }
        }

        // Shuffle the permutation table, from the last entry down to entry 1.
        for (int i = BSize - 1; i > 0; i--)
        {
            int k = this.lattice[i];
            seed = Random(seed);
            int j = (int)(seed % BSize);
            this.lattice[i] = this.lattice[j];
            this.lattice[j] = k;
        }

        // The tables repeat their first BSize + 2 entries, so that lattice[i + b] needs no wrap.
        for (int i = 0; i < BSize + 2; i++)
        {
            // Channel k of gradient s starts at components[((k * BSize) + s) * 2]; the channels are BSize * 2 apart.
            int s = (i % BSize) * 2;
            const int c = BSize * 2;
            this.lattice[BSize + i] = this.lattice[i];
            Vector4 x = new(components[s], components[c + s], components[(2 * c) + s], components[(3 * c) + s]);
            Vector4 y = new(components[s + 1], components[c + s + 1], components[(2 * c) + s + 1], components[(3 * c) + s + 1]);
            this.gradientX[i] = x;
            this.gradientY[i] = y;
            this.gradientX[BSize + i] = x;
            this.gradientY[BSize + i] = y;
        }
    }

    /// <summary>
    /// Sums the octaves of the noise at a point of noise space: the <c>turbulence</c> function of the reference code,
    /// for the four channels at once.
    /// </summary>
    /// <param name="x">The x coordinate times the base frequency.</param>
    /// <param name="y">The y coordinate times the base frequency.</param>
    /// <param name="stitching">Whether to stitch.</param>
    /// <param name="stitch">The stitch values of the first octave.</param>
    /// <returns>The sum of each channel.</returns>
    private Vector4 Turbulence(double x, double y, bool stitching, Stitch stitch)
    {
        Vector4 sum = Vector4.Zero;
        float ratio = 1F;
        for (int octave = 0; octave < this.octaves; octave++)
        {
            Vector4 noise = this.Noise(x, y, stitching, in stitch);
            sum += (this.fractalNoise ? noise : Vector4.Abs(noise)) / ratio;
            x *= 2;
            y *= 2;
            ratio *= 2F;
            if (stitching)
            {
                // Subtracting PerlinN before the doubling and adding it after simplifies to subtracting it once.
                stitch.Width *= 2;
                stitch.WrapX = (2 * stitch.WrapX) - PerlinN;
                stitch.Height *= 2;
                stitch.WrapY = (2 * stitch.WrapY) - PerlinN;
            }
        }

        return sum;
    }

    /// <summary>
    /// The gradient noise at a point of noise space: the <c>noise2</c> function of the reference code, for the four
    /// channels at once.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="stitching">Whether to stitch.</param>
    /// <param name="stitch">The stitch values of the octave.</param>
    /// <returns>The noise of each channel.</returns>
    private Vector4 Noise(double x, double y, bool stitching, in Stitch stitch)
    {
        // The reference code truncates t = v + PerlinN to the lattice cell and keeps the fraction. The truncation goes
        // through long, so that a coordinate beyond the int range (where the reference code overflows) still gives a
        // lattice index; the fraction is exact either way.
        double t = x + PerlinN;
        double cell = Math.Truncate(t);
        int bx0 = (int)(long)cell;
        int bx1 = bx0 + 1;
        float rx0 = (float)(t - cell);
        float rx1 = rx0 - 1F;
        t = y + PerlinN;
        cell = Math.Truncate(t);
        int by0 = (int)(long)cell;
        int by1 = by0 + 1;
        float ry0 = (float)(t - cell);
        float ry1 = ry0 - 1F;

        if (stitching)
        {
            // Lattice points on the right or bottom edge of the tile take the vector of the opposite edge.
            bx0 = bx0 >= stitch.WrapX ? bx0 - stitch.Width : bx0;
            bx1 = bx1 >= stitch.WrapX ? bx1 - stitch.Width : bx1;
            by0 = by0 >= stitch.WrapY ? by0 - stitch.Height : by0;
            by1 = by1 >= stitch.WrapY ? by1 - stitch.Height : by1;
        }

        bx0 &= BMask;
        bx1 &= BMask;
        by0 &= BMask;
        by1 &= BMask;
        int i = this.lattice[bx0];
        int j = this.lattice[bx1];
        int b00 = this.lattice[i + by0];
        int b10 = this.lattice[j + by0];
        int b01 = this.lattice[i + by1];
        int b11 = this.lattice[j + by1];
        float sx = SCurve(rx0);
        float sy = SCurve(ry0);

        // u and v are the dot products of the corner gradients with the vectors from the corners to the point, per
        // channel lane; the results interpolate along x and then along y.
        Vector4[] gx = this.gradientX;
        Vector4[] gy = this.gradientY;
        Vector4 u = (gx[b00] * rx0) + (gy[b00] * ry0);
        Vector4 v = (gx[b10] * rx1) + (gy[b10] * ry0);
        Vector4 a = Vector4.Lerp(u, v, sx);
        u = (gx[b01] * rx0) + (gy[b01] * ry1);
        v = (gx[b11] * rx1) + (gy[b11] * ry1);
        Vector4 b = Vector4.Lerp(u, v, sx);
        return Vector4.Lerp(a, b, sy);
    }

    /// <summary>
    /// The stitch values of the reference code (StitchInfo) for one octave.
    /// </summary>
    private struct Stitch
    {
        /// <summary>
        /// Gets or sets how much to subtract from an x lattice index to wrap.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets how much to subtract from a y lattice index to wrap.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the smallest x lattice index that wraps.
        /// </summary>
        public int WrapX { get; set; }

        /// <summary>
        /// Gets or sets the smallest y lattice index that wraps.
        /// </summary>
        public int WrapY { get; set; }
    }
}
