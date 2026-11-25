// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of a pen, used to share one <see cref="Pen"/> between strokes with equal properties.
/// </summary>
internal readonly struct SvgPenKey : IEquatable<SvgPenKey>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPenKey"/> struct.
    /// </summary>
    /// <param name="brush">The stroke brush. Brushes are shared, so the key compares it by reference.</param>
    /// <param name="width">The stroke width in user units.</param>
    /// <param name="lineCap">The line cap.</param>
    /// <param name="lineJoin">The line join.</param>
    /// <param name="miterLimit">The miter limit.</param>
    /// <param name="pattern">The dash pattern in multiples of the width, or <see langword="null"/> for a solid stroke.</param>
    /// <param name="patternOffset">The dash offset in multiples of the width.</param>
    public SvgPenKey(Brush brush, float width, LineCap lineCap, LineJoin lineJoin, float miterLimit, float[]? pattern, float patternOffset)
    {
        this.Brush = brush;
        this.Width = width;
        this.LineCap = lineCap;
        this.LineJoin = lineJoin;
        this.MiterLimit = miterLimit;
        this.Pattern = pattern;
        this.PatternOffset = patternOffset;
    }

    /// <summary>
    /// Gets the stroke brush.
    /// </summary>
    public Brush Brush { get; }

    /// <summary>
    /// Gets the stroke width in user units.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the line cap.
    /// </summary>
    public LineCap LineCap { get; }

    /// <summary>
    /// Gets the line join.
    /// </summary>
    public LineJoin LineJoin { get; }

    /// <summary>
    /// Gets the miter limit.
    /// </summary>
    public float MiterLimit { get; }

    /// <summary>
    /// Gets the dash pattern in multiples of the width, or <see langword="null"/> for a solid stroke.
    /// </summary>
    public float[]? Pattern { get; }

    /// <summary>
    /// Gets the dash offset in multiples of the width.
    /// </summary>
    public float PatternOffset { get; }

    /// <inheritdoc/>
    public bool Equals(SvgPenKey other)
        => ReferenceEquals(this.Brush, other.Brush)
        && this.Width == other.Width
        && this.LineCap == other.LineCap
        && this.LineJoin == other.LineJoin
        && this.MiterLimit == other.MiterLimit
        && this.PatternOffset == other.PatternOffset
        && (ReferenceEquals(this.Pattern, other.Pattern)
            || (this.Pattern is not null && other.Pattern is not null && this.Pattern.AsSpan().SequenceEqual(other.Pattern)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SvgPenKey other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this.Brush),
            this.Width,
            this.LineCap,
            this.LineJoin,
            this.MiterLimit,
            this.PatternOffset,
            this.Pattern?.Length ?? -1);
}
