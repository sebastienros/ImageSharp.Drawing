// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Options that control how an SVG document loads.
/// </summary>
public sealed class SvgLoadOptions
{
    private float dpi = 96F;
    private SvgLimits limits = new();

    /// <summary>
    /// Gets or sets the resolution, in pixels per inch, that converts absolute units such as <c>in</c>, <c>cm</c>,
    /// <c>mm</c>, <c>pt</c>, and <c>pc</c> to pixels. The default is 96.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number greater than zero.</exception>
    public float Dpi
    {
        get => this.dpi;
        set
        {
            Guard.MustBeGreaterThan(value, 0F, nameof(this.Dpi));
            Guard.MustBeLessThan(value, float.PositiveInfinity, nameof(this.Dpi));
            this.dpi = value;
        }
    }

    /// <summary>
    /// Gets or sets the initial value of the <c>color</c> property, which <c>currentColor</c> refers to when the
    /// document does not set <c>color</c>. The default is black.
    /// </summary>
    public Color CurrentColor { get; set; } = Color.Black;

    /// <summary>
    /// Gets or sets the input limits.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SvgLimits Limits
    {
        get => this.limits;
        set
        {
            Guard.NotNull(value, nameof(this.Limits));
            this.limits = value;
        }
    }
}
