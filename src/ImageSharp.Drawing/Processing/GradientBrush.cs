// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Base class for gradient brushes.
/// Derived brushes define a parameterization that maps each point to a scalar gradient
/// position; this base class maps that position to a color via the sorted color stops
/// and the <see cref="GradientRepetitionMode"/>.
/// </summary>
public abstract class GradientBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    /// <param name="repetitionMode">Defines how the colors are repeated beyond the interval [0..1].</param>
    /// <param name="colorStops">The gradient colors.</param>
    protected GradientBrush(GradientRepetitionMode repetitionMode, params ColorStop[] colorStops)
        : this(repetitionMode, Matrix4x4.Identity, colorStops)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    /// <param name="repetitionMode">Defines how the colors are repeated beyond the interval [0..1].</param>
    /// <param name="gradientTransform">The transform from the gradient's coordinate space to the drawing.</param>
    /// <param name="colorStops">The gradient colors.</param>
    protected GradientBrush(GradientRepetitionMode repetitionMode, Matrix4x4 gradientTransform, params ColorStop[] colorStops)
        : this(repetitionMode, gradientTransform, PixelAlphaRepresentation.Associated, colorStops)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GradientBrush"/> class.
    /// </summary>
    /// <param name="repetitionMode">Defines how the colors are repeated beyond the interval [0..1].</param>
    /// <param name="gradientTransform">The transform from the gradient's coordinate space to the drawing.</param>
    /// <param name="alphaRepresentation">The alpha representation of the stop colors during interpolation.</param>
    /// <param name="colorStops">The gradient colors.</param>
    protected GradientBrush(
        GradientRepetitionMode repetitionMode,
        Matrix4x4 gradientTransform,
        PixelAlphaRepresentation alphaRepresentation,
        params ColorStop[] colorStops)
    {
        this.RepetitionMode = repetitionMode;
        this.GradientTransform = gradientTransform;
        this.AlphaRepresentation = alphaRepresentation;

        InsertionSort(colorStops, (a, b) => a.Ratio.CompareTo(b.Ratio));
        this.ColorStopsArray = colorStops;
    }

    /// <summary>
    /// Gets how the colors are repeated beyond the interval [0..1].
    /// </summary>
    public GradientRepetitionMode RepetitionMode { get; }

    /// <summary>
    /// Gets the transform from the gradient's coordinate space to the drawing. The geometry of
    /// the gradient is defined in its own space and every sample is mapped through the inverse
    /// of this transform, so a skew or a non-uniform scale changes the shape of the gradient.
    /// The transform is affine: a projective transform applied through
    /// <see cref="Brush.Transform(Matrix4x4, Rectangle, Rectangle)"/> projects the geometry
    /// point by point instead, because an affine gradient cannot express perspective.
    /// </summary>
    public Matrix4x4 GradientTransform { get; }

    /// <summary>
    /// Gets the alpha representation of the stop colors during interpolation.
    /// <see cref="PixelAlphaRepresentation.Associated"/> is the default.
    /// All other values interpolate unassociated colors.
    /// </summary>
    public PixelAlphaRepresentation AlphaRepresentation { get; }

    /// <summary>
    /// Gets the color stops for this gradient.
    /// </summary>
    public ReadOnlySpan<ColorStop> ColorStops => this.ColorStopsArray;

    /// <summary>
    /// Gets the color stops array for use by derived applicators.
    /// </summary>
    protected ColorStop[] ColorStopsArray { get; }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is GradientBrush brush)
        {
            return this.RepetitionMode == brush.RepetitionMode
                && this.GradientTransform.Equals(brush.GradientTransform)
                && this.AlphaRepresentation == brush.AlphaRepresentation
                && this.ColorStopsArray?.SequenceEqual(brush.ColorStopsArray) == true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.RepetitionMode, this.GradientTransform, this.AlphaRepresentation, this.ColorStopsArray);

    /// <summary>
    /// Inverts the affine part of <see cref="GradientTransform"/>, which maps drawing
    /// coordinates into the gradient's coordinate space.
    /// </summary>
    /// <param name="drawingToGradient">The inverted affine transform.</param>
    /// <returns>
    /// <see langword="true"/> if the transform can be inverted; otherwise <see langword="false"/>.
    /// </returns>
    internal bool TryGetInverseTransform(out Matrix3x2 drawingToGradient)
    {
        Matrix4x4 m = this.GradientTransform;
        Matrix3x2 affine = new(m.M11, m.M12, m.M21, m.M22, m.M41, m.M42);
        return Matrix3x2.Invert(affine, out drawingToGradient);
    }

    /// <summary>
    /// Sorts the collection in place using a stable insertion sort.
    /// <see cref="Array.Sort{T}(T[], Comparison{T})"/> is not stable and can reorder
    /// equal-ratio color stops, producing non-deterministic gradient results.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="collection">The array to sort in place.</param>
    /// <param name="comparison">The comparison used to order the elements.</param>
    private static void InsertionSort<T>(T[] collection, Comparison<T> comparison)
    {
        int count = collection.Length;
        for (int j = 1; j < count; j++)
        {
            T key = collection[j];

            int i = j - 1;
            for (; i >= 0 && comparison(collection[i], key) > 0; i--)
            {
                collection[i + 1] = collection[i];
            }

            collection[i + 1] = key;
        }
    }

    /// <summary>
    /// Base class for gradient brush applicators.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <typeparam name="TEncoder">The encoder for the alpha representation of the brush.</typeparam>
    internal abstract class GradientBrushRenderer<TPixel, TEncoder> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
        where TEncoder : struct, IGradientPixelEncoder<TPixel>
    {
        private static readonly TPixel Transparent = Color.Transparent.ToPixel<TPixel>();

        /// <summary>
        /// Whether <see cref="Apply"/> packs a row of interpolated vectors with one bulk conversion instead of one
        /// <see cref="IGradientPixelEncoder{TPixel}.Encode"/> per pixel. It holds for <see cref="Rgba32"/> with both
        /// encoders of ImageSharp 5.0.0-alpha.0.38. The per-pixel conversion scales by 255, adds one half, clamps to
        /// [0, 255] (NaN to 0), and truncates. The bulk conversion clamps to [0, 1] (NaN to 0), scales by 255, adds
        /// one half, truncates, and saturates. Both give the same byte for every float, NaN and infinities included.
        /// The associated encoder first divides the color by its alpha with the same lane-wise operator
        /// (<c>UnassociateOperator</c>) in both conversions; a lane-wise division and clamp give the same result at
        /// every vector width.
        /// </summary>
        private static readonly bool EncodesInBulk = typeof(TPixel) == typeof(Rgba32);

        /// <summary>
        /// Whether the encoder takes associated colors, so the bulk conversion must divide by alpha.
        /// </summary>
        private static readonly bool EncodesAssociated = typeof(TEncoder) == typeof(AssociatedGradientPixelEncoder<TPixel>);

        private readonly GradientColorStop[] colorStops;

        private readonly GradientRepetitionMode repetitionMode;

        /// <summary>
        /// Initializes a new instance of the <see cref="GradientBrushRenderer{TPixel, TEncoder}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="brush">The gradient brush.</param>
        protected GradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            GradientBrush brush)
            : base(configuration, options, canvasWidth)
        {
            ColorStop[] colorStops = brush.ColorStopsArray;
            this.colorStops = new GradientColorStop[colorStops.Length];

            // CSS Color 4 interpolates associated colors. SVG 2 interpolates unassociated colors.
            // The renderer stores the stops in the representation of the brush, without a TPixel
            // conversion. The TPixel format cannot change the gradient, and the per-pixel loop
            // does not convert the stops.
            // https://www.w3.org/TR/css-color-4/#interpolation-alpha
            // https://www.w3.org/TR/SVG2/pservers.html#StopColorProperties
            PixelAlphaRepresentation alphaRepresentation = brush.AlphaRepresentation;
            for (int i = 0; i < colorStops.Length; i++)
            {
                ColorStop stop = colorStops[i];
                this.colorStops[i] = new GradientColorStop(stop.Ratio, stop.Color.ToScaledVector4(alphaRepresentation));
            }

            this.repetitionMode = brush.RepetitionMode;

            // The inverse of the affine part maps drawing coordinates into the gradient's space.
            // Derived renderers fold it into their own per-sample mapping. A transform that
            // cannot be inverted yields NaN positions, which the sampler already paints as
            // transparent.
            this.IsTransformed = !brush.GradientTransform.IsIdentity;
            if (this.IsTransformed)
            {
                brush.TryGetInverseTransform(out Matrix3x2 drawingToGradient);
                this.InverseGradientTransform = drawingToGradient;
            }
            else
            {
                this.InverseGradientTransform = Matrix3x2.Identity;
            }
        }

        /// <summary>
        /// Maps a position on the complete gradient into the range that one repetition mode samples.
        /// </summary>
        private interface IRepetitionMode
        {
            /// <summary>
            /// Maps the position in place.
            /// </summary>
            /// <param name="position">The position, which is not NaN.</param>
            /// <returns><see langword="false"/> when the pixel stays transparent.</returns>
            public static abstract bool TryWrap(ref float position);
        }

        /// <summary>
        /// Gets a value indicating whether the brush has a gradient transform, so samples must be
        /// mapped through <see cref="InverseGradientTransform"/> before the gradient is evaluated.
        /// </summary>
        protected bool IsTransformed { get; }

        /// <summary>
        /// Gets the transform from drawing coordinates to the gradient's coordinate space.
        /// </summary>
        protected Matrix3x2 InverseGradientTransform { get; }

        /// <summary>
        /// Gets the gradient color for the pixel at the given device coordinate.
        /// </summary>
        /// <param name="x">The x-coordinate of the pixel in device space.</param>
        /// <param name="y">The y-coordinate of the pixel in device space.</param>
        /// <returns>The blended gradient color converted to <typeparamref name="TPixel"/>.</returns>
        internal TPixel this[int x, int y]

            // Evaluate at pixel centers so gradient sampling lines up with the
            // rasterized coverage positions.
            => this.ColorAt(this.PositionOnGradient(x + 0.5f, y + 0.5f));

        /// <inheritdoc />
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);

            // The positions of the whole row come first, from one call, so the per-pixel work below
            // has no virtual call. The amount buffer is free here: this renderer does not read it otherwise.
            Span<float> positions = workspace.GetAmounts(scanline.Length);
            this.PositionsOnGradient(x, y + 0.5f, positions);

            if (EncodesInBulk)
            {
                // The first blend scratch row is free until the blend below fills it.
                Span<Vector4> colors = workspace.GetBlendScratch(scanline.Length, 3)[..scanline.Length];
                switch (this.repetitionMode)
                {
                    case GradientRepetitionMode.Repeat:
                        this.InterpolatedColorsAt<RepeatMode>(positions, colors);
                        break;
                    case GradientRepetitionMode.Reflect:
                        this.InterpolatedColorsAt<ReflectMode>(positions, colors);
                        break;
                    case GradientRepetitionMode.DontFill:
                        this.InterpolatedColorsAt<DontFillMode>(positions, colors);
                        break;
                    case GradientRepetitionMode.None:
                    default:
                        this.InterpolatedColorsAt<NoneMode>(positions, colors);
                        break;
                }

                if (EncodesAssociated)
                {
                    PixelOperations<TPixel>.Instance.FromAssociatedScaledVector4Destructive(this.Configuration, colors, overlays);
                }
                else
                {
                    PixelOperations<TPixel>.Instance.FromUnassociatedScaledVector4Destructive(this.Configuration, colors, overlays);
                }
            }
            else
            {
                this.ColorsAt(positions, overlays);
            }

            this.Blender.BlendWithCoverage<TPixel>(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                this.Options.BlendPercentage,
                scanline,
                workspace.GetBlendScratch(scanline.Length, 3));
        }

        /// <summary>
        /// Converts a row of gradient positions to pixels.
        /// </summary>
        /// <param name="positions">The positions on the complete gradient.</param>
        /// <param name="overlays">The destination pixels, of the same length as <paramref name="positions"/>.</param>
        private void ColorsAt(ReadOnlySpan<float> positions, Span<TPixel> overlays)
        {
            switch (this.repetitionMode)
            {
                case GradientRepetitionMode.Repeat:
                    this.ColorsAt<RepeatMode>(positions, overlays);
                    break;
                case GradientRepetitionMode.Reflect:
                    this.ColorsAt<ReflectMode>(positions, overlays);
                    break;
                case GradientRepetitionMode.DontFill:
                    this.ColorsAt<DontFillMode>(positions, overlays);
                    break;
                case GradientRepetitionMode.None:
                default:
                    this.ColorsAt<NoneMode>(positions, overlays);
                    break;
            }
        }

        /// <summary>
        /// Calculates the position on the gradient for a given point.
        /// This method is abstract as its content depends on the shape of the gradient.
        /// </summary>
        /// <param name="x">The x-coordinate of the point.</param>
        /// <param name="y">The y-coordinate of the point.</param>
        /// <returns>
        /// The position the given point has on the gradient.
        /// The position is not bound to the [0..1] interval.
        /// Values outside of that interval may be treated differently,
        /// e.g. for the <see cref="GradientRepetitionMode" /> enum.
        /// </returns>
        protected abstract float PositionOnGradient(float x, float y);

        /// <summary>
        /// Calculates the positions on the gradient for the pixel centers of one row segment.
        /// Element <c>i</c> must equal <c>PositionOnGradient((x + i) + 0.5F, y)</c> bit for bit; a derived
        /// renderer overrides this only to remove the virtual call and the invariant branches per pixel.
        /// </summary>
        /// <param name="x">The device x-coordinate of the first pixel.</param>
        /// <param name="y">The device y-coordinate of the pixel centers of the row.</param>
        /// <param name="positions">The destination for one position per pixel.</param>
        protected virtual void PositionsOnGradient(int x, float y, Span<float> positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = this.PositionOnGradient((x + i) + 0.5F, y);
            }
        }

        /// <summary>
        /// Converts a row of gradient positions to pixels with one repetition mode.
        /// </summary>
        /// <typeparam name="TMode">The repetition mode.</typeparam>
        /// <param name="positions">The positions on the complete gradient.</param>
        /// <param name="overlays">The destination pixels, of the same length as <paramref name="positions"/>.</param>
        private void ColorsAt<TMode>(ReadOnlySpan<float> positions, Span<TPixel> overlays)
            where TMode : struct, IRepetitionMode
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float position = positions[i];

                // NaN signals that the parameterization is undefined at this point
                // (e.g. outside the valid branch of a conic); such pixels stay transparent.
                if (float.IsNaN(position) || !TMode.TryWrap(ref position))
                {
                    overlays[i] = Transparent;
                    continue;
                }

                overlays[i] = this.InterpolateAt(position);
            }
        }

        /// <summary>
        /// Converts a row of gradient positions to the interpolated vectors that <typeparamref name="TEncoder"/>
        /// would encode, one pixel at a time, into <typeparamref name="TPixel"/>.
        /// </summary>
        /// <typeparam name="TMode">The repetition mode.</typeparam>
        /// <param name="positions">The positions on the complete gradient.</param>
        /// <param name="colors">The destination vectors, of the same length as <paramref name="positions"/>.</param>
        private void InterpolatedColorsAt<TMode>(ReadOnlySpan<float> positions, Span<Vector4> colors)
            where TMode : struct, IRepetitionMode
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float position = positions[i];

                // A transparent pixel is zero in every channel. Both encoders give that pixel for the zero vector.
                if (float.IsNaN(position) || !TMode.TryWrap(ref position))
                {
                    colors[i] = Vector4.Zero;
                    continue;
                }

                colors[i] = this.InterpolatedColorAt(position);
            }
        }

        /// <summary>
        /// Gets the pixel for one position on the complete gradient.
        /// </summary>
        /// <param name="positionOnCompleteGradient">The position, before repetition handling.</param>
        /// <returns>The gradient color converted to <typeparamref name="TPixel"/>.</returns>
        private TPixel ColorAt(float positionOnCompleteGradient)
        {
            if (float.IsNaN(positionOnCompleteGradient))
            {
                return Transparent;
            }

            bool inside = this.repetitionMode switch
            {
                GradientRepetitionMode.Repeat => RepeatMode.TryWrap(ref positionOnCompleteGradient),
                GradientRepetitionMode.Reflect => ReflectMode.TryWrap(ref positionOnCompleteGradient),
                GradientRepetitionMode.DontFill => DontFillMode.TryWrap(ref positionOnCompleteGradient),
                _ => NoneMode.TryWrap(ref positionOnCompleteGradient),
            };

            return inside ? this.InterpolateAt(positionOnCompleteGradient) : Transparent;
        }

        /// <summary>
        /// Interpolates the color stops at a position after repetition handling and encodes the result.
        /// </summary>
        /// <param name="position">The position on the gradient, after repetition handling.</param>
        /// <returns>The gradient color converted to <typeparamref name="TPixel"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private TPixel InterpolateAt(float position) => TEncoder.Encode(this.InterpolatedColorAt(position));

        /// <summary>
        /// Interpolates the color stops at a position after repetition handling.
        /// </summary>
        /// <param name="position">The position on the gradient, after repetition handling.</param>
        /// <returns>
        /// The color in the alpha representation of the brush, which the encoder converts to <typeparamref name="TPixel"/>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 InterpolatedColorAt(float position)
        {
            // Find the bracketing stops: "to" is the first stop whose ratio is greater than the
            // position, "from" the stop before it. Positions before the first stop return the first
            // stop twice; positions after the last stop (or a NaN position from Repeat with an
            // infinite input) return the last stop twice, which yields the stable edge colors used
            // by GradientRepetitionMode.None. Stop counts are tiny, so a linear scan over the sorted
            // stops beats a binary search.
            GradientColorStop[] stops = this.colorStops;
            int next = 0;
            while (next < stops.Length && !(stops[next].Ratio > position))
            {
                next++;
            }

            ref GradientColorStop from = ref stops[next == 0 ? 0 : next - 1];
            ref GradientColorStop to = ref stops[next == stops.Length ? next - 1 : next];

            if (from.Color == to.Color)
            {
                return from.Color;
            }

            float onLocalGradient = (position - from.Ratio) / (to.Ratio - from.Ratio);
            return Vector4.Lerp(from.Color, to.Color, onLocalGradient);
        }

        /// <summary>
        /// <see cref="GradientRepetitionMode.None"/>: positions outside the stops take the edge colors.
        /// </summary>
        private readonly struct NoneMode : IRepetitionMode
        {
            /// <inheritdoc />
            public static bool TryWrap(ref float position) => true;
        }

        /// <summary>
        /// <see cref="GradientRepetitionMode.Repeat"/>.
        /// </summary>
        private readonly struct RepeatMode : IRepetitionMode
        {
            /// <inheritdoc />
            public static bool TryWrap(ref float position)
            {
                // The floor subtraction moves every position into [0, 1], so the pattern also
                // repeats before the start point. The remainder operator keeps the sign of a
                // negative position, and the brush then pads that position with the first stop.
                position -= MathF.Floor(position);
                return true;
            }
        }

        /// <summary>
        /// <see cref="GradientRepetitionMode.Reflect"/>.
        /// </summary>
        private readonly struct ReflectMode : IRepetitionMode
        {
            /// <inheritdoc />
            public static bool TryWrap(ref float position)
            {
                // Reflection is symmetric about the start point, so the magnitude gives the
                // same color as the signed position. Every second period folds back on itself,
                // so alternate repetitions run the stops in reverse order.
                position = MathF.Abs(position) % 2;
                if (position > 1)
                {
                    position = 2 - position;
                }

                return true;
            }
        }

        /// <summary>
        /// <see cref="GradientRepetitionMode.DontFill"/>: positions outside [0, 1] stay transparent.
        /// </summary>
        private readonly struct DontFillMode : IRepetitionMode
        {
            /// <inheritdoc />
            public static bool TryWrap(ref float position) => position is <= 1 and >= 0;
        }

        /// <summary>
        /// Stores one gradient stop in the representation used for interpolation.
        /// </summary>
        /// <param name="ratio">The stop position.</param>
        /// <param name="color">The stop color in the alpha representation of the brush.</param>
        private readonly struct GradientColorStop(float ratio, Vector4 color)
        {
            /// <summary>
            /// Gets the stop position.
            /// </summary>
            public float Ratio { get; } = ratio;

            /// <summary>
            /// Gets the stop color in the alpha representation of the brush.
            /// </summary>
            public Vector4 Color { get; } = color;
        }
    }
}
