// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The specified style values of one element, or of one declaration block, before inheritance.
/// </summary>
/// <remarks>
/// <para>
/// Each property of <see cref="SvgPropertyId"/> has a typed value and three bits in the masks <see cref="Specified"/>,
/// <see cref="Inherit"/>, and <see cref="Initial"/>; the bit index is the <see cref="SvgPropertyId"/> value. A property
/// whose <see cref="Specified"/> bit is clear has no specified value, and its value field is meaningless. When the bit is
/// set, the value field holds the value unless the <see cref="Inherit"/> bit (the <c>inherit</c> keyword) or the
/// <see cref="Initial"/> bit (the <c>initial</c> keyword) is also set. <c>unset</c> sets <see cref="Inherit"/> for an
/// inherited property and <see cref="Initial"/> otherwise.
/// </para>
/// <para>
/// The values of the properties from <see cref="SvgPropertyId.ClipPath"/> on live in <see cref="Extended"/>, which is
/// <see langword="null"/> until the style specifies one of them with a value. See <see cref="SvgExtendedStyle"/> for its
/// ownership rules.
/// </para>
/// <para>
/// The resolve stage computes a value as follows: the value field when <see cref="HasValue(SvgPropertyId)"/> is true;
/// the parent computed value when the <see cref="Inherit"/> bit is set, or when nothing is specified and
/// <see cref="IsInherited(SvgPropertyId)"/> is true; otherwise the initial value listed on <see cref="SvgPropertyId"/>.
/// </para>
/// </remarks>
internal struct SvgSpecifiedStyle
{
    /// <summary>
    /// The mask of the properties that inherit by default.
    /// </summary>
    public const ulong InheritedProperties =
        (1UL << (int)SvgPropertyId.Color)
        | (1UL << (int)SvgPropertyId.Visibility)
        | (1UL << (int)SvgPropertyId.Fill)
        | (1UL << (int)SvgPropertyId.FillOpacity)
        | (1UL << (int)SvgPropertyId.FillRule)
        | (1UL << (int)SvgPropertyId.Stroke)
        | (1UL << (int)SvgPropertyId.StrokeOpacity)
        | (1UL << (int)SvgPropertyId.StrokeWidth)
        | (1UL << (int)SvgPropertyId.StrokeLineCap)
        | (1UL << (int)SvgPropertyId.StrokeLineJoin)
        | (1UL << (int)SvgPropertyId.StrokeMiterLimit)
        | (1UL << (int)SvgPropertyId.StrokeDashArray)
        | (1UL << (int)SvgPropertyId.StrokeDashOffset)
        | (1UL << (int)SvgPropertyId.FontSize)
        | (1UL << (int)SvgPropertyId.ClipRule)
        | (1UL << (int)SvgPropertyId.PaintOrder)
        | (1UL << (int)SvgPropertyId.MarkerStart)
        | (1UL << (int)SvgPropertyId.MarkerMid)
        | (1UL << (int)SvgPropertyId.MarkerEnd)
        | (1UL << (int)SvgPropertyId.ImageRendering)
        | (1UL << (int)SvgPropertyId.ShapeRendering)
        | (1UL << (int)SvgPropertyId.ColorInterpolation);

    /// <summary>
    /// The mask of the properties whose values live in <see cref="Extended"/>: every property from
    /// <see cref="SvgPropertyId.ClipPath"/> on.
    /// </summary>
    public const ulong ExtendedProperties = ~((1UL << (int)SvgPropertyId.ClipPath) - 1);

    /// <summary>
    /// The <c>stroke-linecap</c> value in the low 4 bits and the <c>stroke-linejoin</c> value in the high 4 bits.
    /// </summary>
    /// <remarks>
    /// The style table holds one entry per styled element, so its size matters for large documents. The struct ends with
    /// its byte-sized fields; sharing one byte between the two keywords makes room for the <see cref="Extended"/>
    /// reference inside the last 8-byte block, so the struct keeps its phase 1 size of 264 bytes. Without it, the struct
    /// grows to 272 bytes and loading the Paris benchmark document allocates about 1 MB more.
    /// </remarks>
    private byte strokeKeywords;

    /// <summary>
    /// Gets the mask of the properties that have a specified value or keyword.
    /// </summary>
    public ulong Specified { readonly get; private set; }

    /// <summary>
    /// Gets the mask of the properties specified as <c>inherit</c>, or as <c>unset</c> on an inherited property.
    /// </summary>
    public ulong Inherit { readonly get; private set; }

    /// <summary>
    /// Gets the mask of the properties specified as <c>initial</c>, or as <c>unset</c> on a property that does not inherit.
    /// </summary>
    public ulong Initial { readonly get; private set; }

    /// <summary>
    /// Gets a value indicating whether no property is specified.
    /// </summary>
    public readonly bool IsEmpty => this.Specified == 0;

    /// <summary>
    /// Gets or sets the <c>color</c> value. The keyword <c>currentColor</c> on <c>color</c> is stored as <c>inherit</c>, as
    /// CSS Color Level 4 defines.
    /// </summary>
    public Color Color { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>display</c> value.
    /// </summary>
    public SvgDisplay Display { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>visibility</c> value.
    /// </summary>
    public SvgVisibility Visibility { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>opacity</c> value, clamped to the range 0 to 1.
    /// </summary>
    public float Opacity { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>fill</c> value.
    /// </summary>
    public SvgPaint Fill { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>fill-opacity</c> value, clamped to the range 0 to 1.
    /// </summary>
    public float FillOpacity { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>fill-rule</c> value.
    /// </summary>
    public SvgFillRule FillRule { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke</c> value.
    /// </summary>
    public SvgPaint Stroke { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke-opacity</c> value, clamped to the range 0 to 1.
    /// </summary>
    public float StrokeOpacity { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke-width</c> value. It is never negative.
    /// </summary>
    public SvgLength StrokeWidth { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke-linecap</c> value.
    /// </summary>
    public SvgStrokeLineCap StrokeLineCap
    {
        readonly get => (SvgStrokeLineCap)(this.strokeKeywords & 0x0F);
        set => this.strokeKeywords = (byte)((this.strokeKeywords & 0xF0) | (byte)value);
    }

    /// <summary>
    /// Gets or sets the <c>stroke-linejoin</c> value.
    /// </summary>
    public SvgStrokeLineJoin StrokeLineJoin
    {
        readonly get => (SvgStrokeLineJoin)(this.strokeKeywords >> 4);
        set => this.strokeKeywords = (byte)((this.strokeKeywords & 0x0F) | ((byte)value << 4));
    }

    /// <summary>
    /// Gets or sets the <c>stroke-miterlimit</c> value. It is never less than 1.
    /// </summary>
    public float StrokeMiterLimit { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke-dasharray</c> value. Every length in it is non-negative.
    /// </summary>
    public SvgDashArray StrokeDashArray { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stroke-dashoffset</c> value.
    /// </summary>
    public SvgLength StrokeDashOffset { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>font-size</c> value. It is never negative. Absolute-size keywords are stored in pixels and the
    /// relative keywords <c>larger</c> and <c>smaller</c> as percentages of the parent font size.
    /// </summary>
    public SvgLength FontSize { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stop-color</c> value, of kind <see cref="SvgPaintKind.Color"/> or
    /// <see cref="SvgPaintKind.CurrentColor"/>.
    /// </summary>
    public SvgPaint StopColor { readonly get; set; }

    /// <summary>
    /// Gets or sets the <c>stop-opacity</c> value, clamped to the range 0 to 1.
    /// </summary>
    public float StopOpacity { readonly get; set; }

    /// <summary>
    /// Gets the values of the properties from <see cref="SvgPropertyId.ClipPath"/> on, or <see langword="null"/> when the
    /// style specifies none of them with a value.
    /// </summary>
    public SvgExtendedStyle? Extended { readonly get; private set; }

    /// <summary>
    /// Returns whether a property inherits by default.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is inherited; otherwise, <see langword="false"/>.</returns>
    public static bool IsInherited(SvgPropertyId property) => (InheritedProperties & Bit(property)) != 0;

    /// <summary>
    /// Returns whether a property has a specified value or keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is specified; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsSpecified(SvgPropertyId property) => (this.Specified & Bit(property)) != 0;

    /// <summary>
    /// Returns whether a property is specified as <c>inherit</c>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property takes the parent value; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsInherit(SvgPropertyId property) => (this.Inherit & Bit(property)) != 0;

    /// <summary>
    /// Returns whether a property is specified as <c>initial</c>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property takes the initial value; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsInitial(SvgPropertyId property) => (this.Initial & Bit(property)) != 0;

    /// <summary>
    /// Returns whether the value field of a property holds the specified value.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property has a specified value that is not a keyword; otherwise, <see langword="false"/>.</returns>
    public readonly bool HasValue(SvgPropertyId property)
        => ((this.Specified & ~(this.Inherit | this.Initial)) & Bit(property)) != 0;

    /// <summary>
    /// Marks a property as specified by the value that the caller has just stored in its value field.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkValue(SvgPropertyId property)
    {
        ulong bit = Bit(property);
        this.Specified |= bit;
        this.Inherit &= ~bit;
        this.Initial &= ~bit;
    }

    /// <summary>
    /// Marks a property as specified by the <c>inherit</c> keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkInherit(SvgPropertyId property)
    {
        ulong bit = Bit(property);
        this.Specified |= bit;
        this.Inherit |= bit;
        this.Initial &= ~bit;
    }

    /// <summary>
    /// Marks a property as specified by the <c>initial</c> keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkInitial(SvgPropertyId property)
    {
        ulong bit = Bit(property);
        this.Specified |= bit;
        this.Inherit &= ~bit;
        this.Initial |= bit;
    }

    /// <summary>
    /// Returns the extension that holds the values of the properties from <see cref="SvgPropertyId.ClipPath"/> on,
    /// creating it when the style has none. Only the property parser calls this, while it fills a style that is not yet
    /// stored in a table; see <see cref="SvgExtendedStyle"/>.
    /// </summary>
    /// <returns>The extension, which the caller may change.</returns>
    public SvgExtendedStyle GetOrCreateExtended() => this.Extended ??= new SvgExtendedStyle();

    /// <summary>
    /// Applies the specified properties of <paramref name="source"/> over this style, as a declaration of higher
    /// precedence does in the cascade. Properties that <paramref name="source"/> does not specify keep their values.
    /// Values of extended properties are shared or merged into a new <see cref="SvgExtendedStyle"/>, so no extension
    /// changes.
    /// </summary>
    /// <param name="source">The style of higher precedence.</param>
    public void Apply(in SvgSpecifiedStyle source)
    {
        // Visit only the set bits of the phase 1 properties; a typical declaration block specifies a few properties.
        ulong bits = source.Specified & ~ExtendedProperties;
        while (bits != 0)
        {
            SvgPropertyId property = (SvgPropertyId)BitOperations.TrailingZeroCount(bits);
            bits &= bits - 1;
            switch (property)
            {
                case SvgPropertyId.Color:
                    this.Color = source.Color;
                    break;
                case SvgPropertyId.Display:
                    this.Display = source.Display;
                    break;
                case SvgPropertyId.Visibility:
                    this.Visibility = source.Visibility;
                    break;
                case SvgPropertyId.Opacity:
                    this.Opacity = source.Opacity;
                    break;
                case SvgPropertyId.Fill:
                    this.Fill = source.Fill;
                    break;
                case SvgPropertyId.FillOpacity:
                    this.FillOpacity = source.FillOpacity;
                    break;
                case SvgPropertyId.FillRule:
                    this.FillRule = source.FillRule;
                    break;
                case SvgPropertyId.Stroke:
                    this.Stroke = source.Stroke;
                    break;
                case SvgPropertyId.StrokeOpacity:
                    this.StrokeOpacity = source.StrokeOpacity;
                    break;
                case SvgPropertyId.StrokeWidth:
                    this.StrokeWidth = source.StrokeWidth;
                    break;
                case SvgPropertyId.StrokeLineCap:
                    this.StrokeLineCap = source.StrokeLineCap;
                    break;
                case SvgPropertyId.StrokeLineJoin:
                    this.StrokeLineJoin = source.StrokeLineJoin;
                    break;
                case SvgPropertyId.StrokeMiterLimit:
                    this.StrokeMiterLimit = source.StrokeMiterLimit;
                    break;
                case SvgPropertyId.StrokeDashArray:
                    this.StrokeDashArray = source.StrokeDashArray;
                    break;
                case SvgPropertyId.StrokeDashOffset:
                    this.StrokeDashOffset = source.StrokeDashOffset;
                    break;
                case SvgPropertyId.FontSize:
                    this.FontSize = source.FontSize;
                    break;
                case SvgPropertyId.StopColor:
                    this.StopColor = source.StopColor;
                    break;
                case SvgPropertyId.StopOpacity:
                    this.StopOpacity = source.StopOpacity;
                    break;
            }
        }

        // A style without extended values shares the extension of the source, because no stored extension changes. A
        // style with its own values gets a new merged extension. Fields of properties that the source does not give a
        // value are meaningless, so taking them along is harmless: the bits of this style decide which fields count.
        ulong extended = source.Specified & ExtendedProperties;
        if (extended != 0 && source.Extended is not null)
        {
            this.Extended = this.Extended is null ? source.Extended : SvgExtendedStyle.Merge(this.Extended, source.Extended, extended);
        }

        // The keyword bits of the applied properties come from the source; the bits of the other properties stay.
        ulong applied = source.Specified;
        this.Specified |= applied;
        this.Inherit = (this.Inherit & ~applied) | source.Inherit;
        this.Initial = (this.Initial & ~applied) | source.Initial;
    }

    /// <summary>
    /// Returns the mask bit of a property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>The bit.</returns>
    private static ulong Bit(SvgPropertyId property) => 1UL << (int)property;
}
