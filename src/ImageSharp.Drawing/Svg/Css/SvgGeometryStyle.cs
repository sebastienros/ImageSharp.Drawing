// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The values of the geometry properties (<c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>, <c>cx</c>, <c>cy</c>,
/// <c>r</c>, <c>rx</c>, <c>ry</c>, <c>d</c>), of <c>transform-box</c> and <c>transform-origin</c>, and of the CSS
/// <c>transform</c>. None of them inherits. The same type holds specified values, in
/// <see cref="SvgExtendedStyle.Geometry"/>, and computed values, in <see cref="SvgComputedStyle.Geometry"/>.
/// </summary>
/// <remarks>
/// <para>
/// The attributes of the same names stay the fast path: the reader keeps them in the attribute records of the elements,
/// and only a declaration in <c>style</c> or in a style sheet sets these properties. A computed value therefore counts
/// only for the properties in <see cref="FromStyle"/>; for the others the element uses its attribute. A declaration wins
/// over the attribute, as for every presentation attribute: <c>x: initial</c> gives 0 whatever the attribute says.
/// </para>
/// <para>
/// Lengths: <see cref="Width"/>, <see cref="Height"/>, <see cref="Rx"/>, and <see cref="Ry"/> are unspecified for
/// <c>auto</c>. In a computed style an absolute or font-relative length is in pixels and a percentage stays a
/// percentage, as <see cref="SvgComputedStyle"/> keeps other lengths; a number without a unit is a length in user units,
/// which Chromium accepts in these properties.
/// </para>
/// <para>
/// Ownership follows <see cref="SvgExtendedStyle"/>. Computed instances never change after they are created; an element
/// that specifies none of these properties has <see cref="Initial"/>, so the common case allocates nothing.
/// </para>
/// </remarks>
internal sealed class SvgGeometryStyle
{
    /// <summary>
    /// Gets the mask of the properties whose values live in this type: the geometry properties, <c>transform-box</c>,
    /// <c>transform-origin</c>, and the CSS <c>transform</c>.
    /// </summary>
    /// <remarks>
    /// <c>transform-origin</c> and <c>transform</c> have ids below 64, but few documents specify them, so their values
    /// live here rather than in every <see cref="SvgExtendedStyle"/> and every <see cref="SvgComputedStyle"/>. This keeps
    /// both types at the size they had before the geometry properties came.
    /// </remarks>
    public static SvgPropertySet Properties { get; } =
        SvgPropertySet.Range(SvgPropertyId.X, SvgPropertyId.TransformBox)
        | SvgPropertySet.Of(SvgPropertyId.TransformOrigin)
        | SvgPropertySet.Of(SvgPropertyId.Transform);

    /// <summary>
    /// Gets the instance with the initial values, which computed styles share. It never changes.
    /// </summary>
    public static SvgGeometryStyle Initial { get; } = new();

    /// <summary>
    /// Gets or sets <c>x</c>. Initial 0.
    /// </summary>
    public SvgLength X { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets <c>y</c>. Initial 0.
    /// </summary>
    public SvgLength Y { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets <c>width</c>: a non-negative length or percentage, or unspecified for <c>auto</c>, the initial value.
    /// </summary>
    public SvgLength Width { get; set; }

    /// <summary>
    /// Gets or sets <c>height</c>: a non-negative length or percentage, or unspecified for <c>auto</c>, the initial value.
    /// </summary>
    public SvgLength Height { get; set; }

    /// <summary>
    /// Gets or sets <c>cx</c>. Initial 0.
    /// </summary>
    public SvgLength Cx { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets <c>cy</c>. Initial 0.
    /// </summary>
    public SvgLength Cy { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets <c>r</c>: a non-negative length or percentage. Initial 0.
    /// </summary>
    public SvgLength R { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets <c>rx</c>: a non-negative length or percentage, or unspecified for <c>auto</c>, the initial value.
    /// </summary>
    public SvgLength Rx { get; set; }

    /// <summary>
    /// Gets or sets <c>ry</c>: a non-negative length or percentage, or unspecified for <c>auto</c>, the initial value.
    /// </summary>
    public SvgLength Ry { get; set; }

    /// <summary>
    /// Gets or sets <c>d</c>: the path of <c>path()</c>, in user units, or <see langword="null"/> for <c>none</c>, the
    /// initial value. <c>path()</c> takes the whole string or nothing: a string with an error makes the declaration
    /// invalid, as Chromium does, unlike the <c>d</c> attribute, which renders up to the first error.
    /// </summary>
    public IPath? D { get; set; }

    /// <summary>
    /// Gets or sets <c>transform-box</c>. Initial <c>view-box</c>.
    /// </summary>
    public SvgTransformBox TransformBox { get; set; } = SvgTransformBox.ViewBox;

    /// <summary>
    /// Gets or sets the horizontal component of <c>transform-origin</c>. In a specified style, keywords are stored as
    /// percentages: <c>left</c> is 0%, <c>center</c> 50%, and <c>right</c> 100%; in a computed style, it is in
    /// <see cref="SvgLengthUnit.Px"/> or <see cref="SvgLengthUnit.Percent"/>. The initial value is 0: CSS Transforms makes
    /// 0 0 the used initial value for SVG elements without a CSS layout box, as if the user agent style sheet set it.
    /// </summary>
    public SvgLength TransformOriginX { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets the vertical component of <c>transform-origin</c>, with the rules of <see cref="TransformOriginX"/>:
    /// <c>top</c> is 0%, <c>center</c> 50%, and <c>bottom</c> 100%.
    /// </summary>
    public SvgLength TransformOriginY { get; set; } = new(0F, SvgLengthUnit.Px);

    /// <summary>
    /// Gets or sets the specified CSS <c>transform</c> value; <c>none</c> is the identity. The reader folds it into the
    /// transform table of the element after the cascade, so a computed style does not use it.
    /// </summary>
    public System.Numerics.Matrix3x2 Transform { get; set; } = System.Numerics.Matrix3x2.Identity;

    /// <summary>
    /// Gets or sets the CSS <c>transform</c> value as parsed, or <see langword="null"/> when no declaration sets it. In a
    /// specified style it holds the same value as <see cref="Transform"/>, and also a value that needs the reference box
    /// or the font size (a percentage, a unit other than <c>px</c>), for which <see cref="Transform"/> is the identity. In
    /// a computed style it is the value that wins over the transform table of the element, which also covers a value
    /// that <c>var()</c> substitution produced after the reader built that table.
    /// </summary>
    public SvgCssTransform? CssTransform { get; set; }

    /// <summary>
    /// Gets or sets, in a computed style, the properties whose computed value comes from a declaration (a value,
    /// <c>initial</c>, or <c>inherit</c> from a parent value that came from a declaration). The element uses its attribute
    /// for the other geometry properties. Empty in a specified style, whose bits are in <see cref="SvgSpecifiedStyle"/>.
    /// </summary>
    public SvgPropertySet FromStyle { get; set; }

    /// <summary>
    /// Gets or sets, in a computed style, the properties specified as <c>inherit</c> whose parent has no computed value
    /// from a declaration: the value is the computed value of the parent element from its attribute of the same name,
    /// which only the resolve stage can read (and, when the bit is also set in the parent's own
    /// <see cref="FromParentAttribute"/>, the value of the parent's parent, and so on). Empty in a specified style.
    /// </summary>
    public SvgPropertySet FromParentAttribute { get; set; }

    /// <summary>
    /// Returns whether a property is a geometry property or <c>transform-box</c>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the value of the property lives in this type; otherwise, <see langword="false"/>.</returns>
    public static bool IsGeometryProperty(SvgPropertyId property) => property is >= SvgPropertyId.X and <= SvgPropertyId.TransformBox;

    /// <summary>
    /// Returns the computed length of a geometry property.
    /// </summary>
    /// <param name="property">A property from <see cref="SvgPropertyId.X"/> to <see cref="SvgPropertyId.Ry"/>.</param>
    /// <returns>The length.</returns>
    public SvgLength GetLength(SvgPropertyId property)
        => property switch
        {
            SvgPropertyId.X => this.X,
            SvgPropertyId.Y => this.Y,
            SvgPropertyId.Width => this.Width,
            SvgPropertyId.Height => this.Height,
            SvgPropertyId.Cx => this.Cx,
            SvgPropertyId.Cy => this.Cy,
            SvgPropertyId.R => this.R,
            SvgPropertyId.Rx => this.Rx,
            _ => this.Ry,
        };

    /// <summary>
    /// Sets the length of a geometry property.
    /// </summary>
    /// <param name="property">A property from <see cref="SvgPropertyId.X"/> to <see cref="SvgPropertyId.Ry"/>.</param>
    /// <param name="value">The length.</param>
    public void SetLength(SvgPropertyId property, SvgLength value)
    {
        switch (property)
        {
            case SvgPropertyId.X:
                this.X = value;
                break;
            case SvgPropertyId.Y:
                this.Y = value;
                break;
            case SvgPropertyId.Width:
                this.Width = value;
                break;
            case SvgPropertyId.Height:
                this.Height = value;
                break;
            case SvgPropertyId.Cx:
                this.Cx = value;
                break;
            case SvgPropertyId.Cy:
                this.Cy = value;
                break;
            case SvgPropertyId.R:
                this.R = value;
                break;
            case SvgPropertyId.Rx:
                this.Rx = value;
                break;
            default:
                this.Ry = value;
                break;
        }
    }

    /// <summary>
    /// Copies the value of one property from another instance.
    /// </summary>
    /// <param name="property">A property in <see cref="Properties"/>.</param>
    /// <param name="source">The instance to copy from.</param>
    public void CopyProperty(SvgPropertyId property, SvgGeometryStyle source)
    {
        switch (property)
        {
            case SvgPropertyId.D:
                this.D = source.D;
                break;
            case SvgPropertyId.TransformBox:
                this.TransformBox = source.TransformBox;
                break;
            case SvgPropertyId.TransformOrigin:
                this.TransformOriginX = source.TransformOriginX;
                this.TransformOriginY = source.TransformOriginY;
                break;
            case SvgPropertyId.Transform:
                this.Transform = source.Transform;
                this.CssTransform = source.CssTransform;
                break;
            default:
                this.SetLength(property, source.GetLength(property));
                break;
        }
    }

    /// <summary>
    /// Returns a copy that the caller may change before it shares it.
    /// </summary>
    /// <returns>The copy.</returns>
    public SvgGeometryStyle Clone() => (SvgGeometryStyle)this.MemberwiseClone();

    /// <summary>
    /// Returns a new instance that holds the values of <paramref name="target"/> with the values of the properties in
    /// <paramref name="properties"/> taken from <paramref name="source"/>. Neither input changes.
    /// </summary>
    /// <param name="target">The values of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The values of higher precedence.</param>
    /// <param name="properties">The mask of the properties that <paramref name="source"/> specifies.</param>
    /// <returns>The merged values.</returns>
    public static SvgGeometryStyle Merge(SvgGeometryStyle? target, SvgGeometryStyle source, SvgPropertySet properties)
    {
        SvgGeometryStyle result = target is null ? new SvgGeometryStyle() : target.Clone();
        SvgPropertySet bits = properties & Properties;
        while (!bits.IsEmpty)
        {
            result.CopyProperty(SvgPropertySet.TakeFirst(ref bits), source);
        }

        return result;
    }
}
