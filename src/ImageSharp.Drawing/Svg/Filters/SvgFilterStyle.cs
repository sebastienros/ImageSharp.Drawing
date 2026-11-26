// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The values of the filter properties: <c>filter</c>, <c>color-interpolation-filters</c>, <c>flood-color</c>,
/// <c>flood-opacity</c>, and <c>lighting-color</c>.
/// </summary>
/// <remarks>
/// <para>
/// As specified values, an instance hangs off <see cref="SvgExtendedStyle.Filters"/> only when a style gives one of these
/// properties a value, and the ownership rules of <see cref="SvgExtendedStyle"/> apply: the property parser changes the
/// instance of the style it fills, and <see cref="Merge"/> never changes an instance.
/// </para>
/// <para>
/// As computed values, <see cref="SvgComputedStyle.Filters"/> holds an instance with all five values. Computed instances
/// never change after they are created. An element that specifies none of the properties takes
/// <see cref="GetChildStyle"/> of its parent: one of three shared instances that hold the initial values with the
/// inherited <c>color-interpolation-filters</c>, so the common case allocates nothing.
/// </para>
/// </remarks>
internal sealed class SvgFilterStyle
{
    /// <summary>
    /// The mask of the filter properties.
    /// </summary>
    public const ulong Properties =
        (1UL << (int)SvgPropertyId.Filter)
        | (1UL << (int)SvgPropertyId.ColorInterpolationFilters)
        | (1UL << (int)SvgPropertyId.FloodColor)
        | (1UL << (int)SvgPropertyId.FloodOpacity)
        | (1UL << (int)SvgPropertyId.LightingColor);

    /// <summary>
    /// The mask of the paint properties that live in <see cref="SvgComputedStyle.FilterColors"/>.
    /// </summary>
    public const ulong ColorProperties =
        (1UL << (int)SvgPropertyId.FloodColor)
        | (1UL << (int)SvgPropertyId.FloodOpacity)
        | (1UL << (int)SvgPropertyId.LightingColor);

    /// <summary>
    /// The shared computed instances with the initial values of the properties that do not inherit, one per value of
    /// <c>color-interpolation-filters</c>, in <see cref="SvgColorInterpolation"/> order.
    /// </summary>
    private static readonly SvgFilterStyle[] InitialStyles =
    [
        CreateInitial(SvgColorInterpolation.SRgb),
        CreateInitial(SvgColorInterpolation.Auto),
        CreateInitial(SvgColorInterpolation.LinearRgb),
    ];

    // Whether this is one of the shared initial instances, whose children can share it.
    private bool isInitial;

    /// <summary>
    /// Gets the instance that holds the initial values: <c>none</c>, <c>linearRGB</c>, black, 1, and white.
    /// </summary>
    public static SvgFilterStyle Initial => InitialStyles[(int)SvgColorInterpolation.LinearRgb];

    /// <summary>
    /// Gets or sets the <c>filter</c> value, or <see langword="null"/> for <c>none</c>.
    /// </summary>
    public SvgFilterList? Filter { get; set; }

    /// <summary>
    /// Gets or sets the <c>color-interpolation-filters</c> value.
    /// </summary>
    public SvgColorInterpolation ColorInterpolationFilters { get; set; }

    /// <summary>
    /// Gets or sets the <c>flood-color</c> value, of kind <see cref="SvgPaintKind.Color"/> or <see cref="SvgPaintKind.CurrentColor"/>.
    /// </summary>
    public SvgPaint FloodColor { get; set; }

    /// <summary>
    /// Gets or sets the <c>flood-opacity</c> value, clamped to the range 0 to 1.
    /// </summary>
    public float FloodOpacity { get; set; }

    /// <summary>
    /// Gets or sets the <c>lighting-color</c> value, of kind <see cref="SvgPaintKind.Color"/> or <see cref="SvgPaintKind.CurrentColor"/>.
    /// </summary>
    public SvgPaint LightingColor { get; set; }

    /// <summary>
    /// Returns whether a property is a filter property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is in <see cref="Properties"/>; otherwise, <see langword="false"/>.</returns>
    public static bool IsFilterProperty(SvgPropertyId property) => (Properties & (1UL << (int)property)) != 0;

    /// <summary>
    /// Returns a new instance that holds the values of <paramref name="target"/> with the values of the properties in
    /// <paramref name="properties"/> taken from <paramref name="source"/>. Neither input changes.
    /// </summary>
    /// <param name="target">The values of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The values of higher precedence.</param>
    /// <param name="properties">The mask of the filter properties that <paramref name="source"/> gives a value.</param>
    /// <returns>The merged values.</returns>
    public static SvgFilterStyle Merge(SvgFilterStyle? target, SvgFilterStyle source, ulong properties)
    {
        SvgFilterStyle result = target is null ? new SvgFilterStyle() : target.Clone();
        result.CopyFrom(source, properties);
        return result;
    }

    /// <summary>
    /// Returns a copy of this instance, which the caller may change.
    /// </summary>
    /// <returns>The copy.</returns>
    public SvgFilterStyle Clone()
    {
        SvgFilterStyle clone = (SvgFilterStyle)this.MemberwiseClone();
        clone.isInitial = false;
        return clone;
    }

    /// <summary>
    /// Returns the computed values that a child starts from: the initial values of the properties that do not inherit,
    /// with the <c>color-interpolation-filters</c> of this instance. The result is a shared instance.
    /// </summary>
    /// <returns>The computed values of a child that specifies no filter property.</returns>
    public SvgFilterStyle GetChildStyle() => this.isInitial ? this : InitialStyles[(int)this.ColorInterpolationFilters];

    /// <summary>
    /// Copies the values of some filter properties from another instance. Only an instance that is not yet shared may
    /// change.
    /// </summary>
    /// <param name="source">The instance to copy from.</param>
    /// <param name="properties">The mask of the filter properties to copy.</param>
    public void CopyFrom(SvgFilterStyle source, ulong properties)
    {
        if ((properties & (1UL << (int)SvgPropertyId.Filter)) != 0)
        {
            this.Filter = source.Filter;
        }

        if ((properties & (1UL << (int)SvgPropertyId.ColorInterpolationFilters)) != 0)
        {
            this.ColorInterpolationFilters = source.ColorInterpolationFilters;
        }

        if ((properties & (1UL << (int)SvgPropertyId.FloodColor)) != 0)
        {
            this.FloodColor = source.FloodColor;
        }

        if ((properties & (1UL << (int)SvgPropertyId.FloodOpacity)) != 0)
        {
            this.FloodOpacity = source.FloodOpacity;
        }

        if ((properties & (1UL << (int)SvgPropertyId.LightingColor)) != 0)
        {
            this.LightingColor = source.LightingColor;
        }
    }

    /// <summary>
    /// Creates a shared instance with the initial values and a given <c>color-interpolation-filters</c>.
    /// </summary>
    /// <param name="colorSpace">The color space.</param>
    /// <returns>The instance.</returns>
    private static SvgFilterStyle CreateInitial(SvgColorInterpolation colorSpace)
        => new()
        {
            ColorInterpolationFilters = colorSpace,
            FloodColor = new SvgPaint(SvgPaintKind.Color, Color.Black, null, SvgPaintKind.None),
            FloodOpacity = 1F,
            LightingColor = new SvgPaint(SvgPaintKind.Color, Color.White, null, SvgPaintKind.None),
            isInitial = true,
        };
}
