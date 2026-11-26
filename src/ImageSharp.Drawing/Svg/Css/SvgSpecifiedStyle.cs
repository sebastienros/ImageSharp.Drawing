// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

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
/// <see langword="null"/> until the style specifies one of them. See <see cref="SvgExtendedStyle"/> for its ownership
/// rules.
/// </para>
/// <para>
/// Mask layout. The bits of the properties below 64 are three <see cref="ulong"/> fields of this struct. The bits of the
/// properties from <see cref="SvgPropertyId.X"/> on live in <see cref="Extended"/>, next to their values: a style that
/// specifies one of them has an extension anyway, and the style table, which has one entry per styled element, keeps the
/// size it had with 64 properties. The properties are the same either way; <see cref="Specified"/>, <see cref="Inherit"/>,
/// and <see cref="Initial"/> return both words as one <see cref="SvgPropertySet"/>.
/// </para>
/// <para>
/// Declarations whose value contains <c>var()</c> are kept as text until the computed-value stage, as CSS Custom
/// Properties Level 1 defines. Such a declaration sets the bit of its property in <see cref="Pending"/> and leaves the
/// value field and the <see cref="Specified"/> bit as they were, so the value of the declaration of next lower precedence
/// stays in the value field; a later declaration of the property without <c>var()</c> clears the pending bit again. The
/// pending text and the custom properties (<c>--name</c>) live in <see cref="SvgExtendedStyle.Variables"/>.
/// </para>
/// <para>
/// The resolve stage computes a value as follows: the value field when <see cref="HasValue(SvgPropertyId)"/> is true;
/// the parent computed value when the <see cref="Inherit"/> bit is set, or when nothing is specified and
/// <see cref="IsInherited(SvgPropertyId)"/> is true; otherwise the initial value listed on <see cref="SvgPropertyId"/>.
/// A property with a <see cref="Pending"/> bit computes from its pending text instead, after <c>var()</c> substitution
/// (<see cref="SvgComputedStyle"/>).
/// </para>
/// </remarks>
internal struct SvgSpecifiedStyle
{
    /// <summary>
    /// The mask of the properties below 64 whose values live in <see cref="Extended"/>: every property from
    /// <see cref="SvgPropertyId.ClipPath"/> to <see cref="SvgPropertyId.LightingColor"/>. Every property from 64 on lives
    /// there too.
    /// </summary>
    public const ulong ExtendedLowProperties = ~((1UL << (int)SvgPropertyId.ClipPath) - 1);

    /// <summary>
    /// The inline properties that inherit by default.
    /// </summary>
    private const ulong InheritedLowProperties =
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
        | (1UL << (int)SvgPropertyId.ColorInterpolation)
        | (1UL << (int)SvgPropertyId.ColorInterpolationFilters);

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

    // The bits of the properties below 64. The bits from 64 on are in the extension; see the remarks of the type.
    private ulong specifiedLow;
    private ulong inheritLow;
    private ulong initialLow;

    /// <summary>
    /// Gets the mask of the properties that inherit by default.
    /// </summary>
    public static SvgPropertySet InheritedProperties { get; } =
        new SvgPropertySet(InheritedLowProperties, 0) | (SvgTextStyle.Properties & ~SvgTextStyle.NonInheritedProperties);

    /// <summary>
    /// Gets the mask of the properties whose values live in <see cref="Extended"/>: every property from
    /// <see cref="SvgPropertyId.ClipPath"/> on.
    /// </summary>
    public static SvgPropertySet ExtendedProperties { get; } = new(ExtendedLowProperties, ulong.MaxValue);

    /// <summary>
    /// Gets the mask of the properties that have a specified value or keyword.
    /// </summary>
    public readonly SvgPropertySet Specified => new(this.specifiedLow, this.Extended?.SpecifiedHigh ?? 0);

    /// <summary>
    /// Gets the mask of the properties specified as <c>inherit</c>, or as <c>unset</c> on an inherited property.
    /// </summary>
    public readonly SvgPropertySet Inherit => new(this.inheritLow, this.Extended?.InheritHigh ?? 0);

    /// <summary>
    /// Gets the mask of the properties specified as <c>initial</c>, or as <c>unset</c> on a property that does not inherit.
    /// </summary>
    public readonly SvgPropertySet Initial => new(this.initialLow, this.Extended?.InitialHigh ?? 0);

    /// <summary>
    /// Gets the mask of the properties whose winning declaration contains <c>var()</c>. Its text is in
    /// <see cref="SvgExtendedStyle.Variables"/>.
    /// </summary>
    public readonly SvgPropertySet Pending => this.Extended?.Variables?.Pending ?? default;

    /// <summary>
    /// Gets the bits of <see cref="Specified"/> for the properties below 64, for the hot paths of the cascade.
    /// </summary>
    public readonly ulong SpecifiedLow => this.specifiedLow;

    /// <summary>
    /// Gets a value indicating whether the style specifies nothing: no property, no pending declaration, and no custom
    /// property.
    /// </summary>
    public readonly bool IsEmpty => this.specifiedLow == 0 && (this.Extended is null || !this.Extended.HasHighContent);

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
    /// Gets the values of the properties from <see cref="SvgPropertyId.ClipPath"/> on, the mask bits of the properties
    /// from <see cref="SvgPropertyId.X"/> on, and the <c>var()</c> declarations and custom properties; or
    /// <see langword="null"/> when the style specifies none of them.
    /// </summary>
    public SvgExtendedStyle? Extended { readonly get; private set; }

    /// <summary>
    /// Returns whether a property inherits by default.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is inherited; otherwise, <see langword="false"/>.</returns>
    public static bool IsInherited(SvgPropertyId property) => InheritedProperties.Contains(property);

    /// <summary>
    /// Returns whether a property has a specified value or keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is specified; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsSpecified(SvgPropertyId property)
        => (int)property < 64
            ? (this.specifiedLow & (1UL << (int)property)) != 0
            : this.Extended is SvgExtendedStyle extended && (extended.SpecifiedHigh & HighBit(property)) != 0;

    /// <summary>
    /// Returns whether a property is specified as <c>inherit</c>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property takes the parent value; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsInherit(SvgPropertyId property)
        => (int)property < 64
            ? (this.inheritLow & (1UL << (int)property)) != 0
            : this.Extended is SvgExtendedStyle extended && (extended.InheritHigh & HighBit(property)) != 0;

    /// <summary>
    /// Returns whether a property is specified as <c>initial</c>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property takes the initial value; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsInitial(SvgPropertyId property)
        => (int)property < 64
            ? (this.initialLow & (1UL << (int)property)) != 0
            : this.Extended is SvgExtendedStyle extended && (extended.InitialHigh & HighBit(property)) != 0;

    /// <summary>
    /// Returns whether the value field of a property holds the specified value.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property has a specified value that is not a keyword; otherwise, <see langword="false"/>.</returns>
    public readonly bool HasValue(SvgPropertyId property)
        => this.IsSpecified(property) && !this.IsInherit(property) && !this.IsInitial(property);

    /// <summary>
    /// Returns whether the winning declaration of a property contains <c>var()</c>, so that its value is the pending text in
    /// <see cref="SvgExtendedStyle.Variables"/>.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property has a pending declaration; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsPending(SvgPropertyId property) => this.Extended?.Variables?.Pending.Contains(property) == true;

    /// <summary>
    /// Marks a property as specified by the value that the caller has just stored in its value field.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkValue(SvgPropertyId property) => this.Mark(property, false, false);

    /// <summary>
    /// Marks a property as specified by the <c>inherit</c> keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkInherit(SvgPropertyId property) => this.Mark(property, true, false);

    /// <summary>
    /// Marks a property as specified by the <c>initial</c> keyword.
    /// </summary>
    /// <param name="property">The property.</param>
    public void MarkInitial(SvgPropertyId property) => this.Mark(property, false, true);

    /// <summary>
    /// Records a declaration whose value contains <c>var()</c>. The value field and the other bits of the property stay
    /// as they are; see the remarks of the type. Only the property parser calls this, while it fills a style that is not
    /// yet stored in a table.
    /// </summary>
    /// <param name="property">The property, or a longhand of <paramref name="shorthand"/>.</param>
    /// <param name="text">The value text, without <c>!important</c>.</param>
    /// <param name="shorthand">The shorthand whose value <paramref name="text"/> is, or <see langword="null"/> when it is the value of <paramref name="property"/>.</param>
    public void MarkPending(SvgPropertyId property, string text, SvgShorthand? shorthand)
        => this.GetOrCreateExtended().GetOrCreateVariables().SetPending(property, text, shorthand);

    /// <summary>
    /// Records a custom property declaration (<c>--name: value</c>). Only the property parser calls this, while it fills
    /// a style that is not yet stored in a table.
    /// </summary>
    /// <param name="name">The name, with its leading <c>--</c>. Custom property names are case-sensitive.</param>
    /// <param name="value">The value.</param>
    public void SetCustomProperty(string name, SvgCustomPropertyValue value)
        => this.GetOrCreateExtended().GetOrCreateVariables().SetCustomProperty(name, value);

    /// <summary>
    /// Returns the extension that holds the values of the properties from <see cref="SvgPropertyId.ClipPath"/> on,
    /// creating it when the style has none. Only the property parser calls this, while it fills a style that is not yet
    /// stored in a table; see <see cref="SvgExtendedStyle"/>.
    /// </summary>
    /// <returns>The extension, which the caller may change.</returns>
    public SvgExtendedStyle GetOrCreateExtended() => this.Extended ??= new SvgExtendedStyle();

    /// <summary>
    /// Makes properties unspecified again. Only the code that fills a style that is not yet stored in a table calls this,
    /// because it changes the masks of the extension.
    /// </summary>
    /// <param name="properties">The properties.</param>
    public void Unmark(SvgPropertySet properties)
    {
        this.specifiedLow &= ~properties.Low;
        this.inheritLow &= ~properties.Low;
        this.initialLow &= ~properties.Low;
        if (this.Extended is SvgExtendedStyle extended && properties.High != 0)
        {
            extended.SpecifiedHigh &= ~properties.High;
            extended.InheritHigh &= ~properties.High;
            extended.InitialHigh &= ~properties.High;
        }
    }

    /// <summary>
    /// Returns a copy of this style without custom properties and pending declarations: each property with a pending
    /// declaration keeps the value of its declaration of next lower precedence. The extension of this style does not
    /// change.
    /// </summary>
    /// <returns>The copy.</returns>
    public readonly SvgSpecifiedStyle WithoutVariables()
    {
        SvgSpecifiedStyle copy = this;
        if (this.Extended?.Variables is not null)
        {
            copy.Extended = this.Extended.WithoutVariables();
        }

        return copy;
    }

    /// <summary>
    /// Applies the specified properties of <paramref name="source"/> over this style, as a declaration of higher
    /// precedence does in the cascade. Properties that <paramref name="source"/> does not specify keep their values.
    /// Values of extended properties are shared or merged into a new <see cref="SvgExtendedStyle"/>, so no extension
    /// changes.
    /// </summary>
    /// <param name="source">The style of higher precedence.</param>
    public void Apply(in SvgSpecifiedStyle source)
    {
        // Visit only the set bits of the inline properties; a typical declaration block specifies a few properties.
        ulong applied = source.specifiedLow;
        ulong bits = applied & ~ExtendedLowProperties;
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

        // A style without an extension shares the extension of the source, because no stored extension changes; the
        // source extension also carries the high mask bits, which this style cannot have without an extension. A style
        // with its own extension gets a new merged one. Fields of properties that the source does not give a value are
        // meaningless, so taking them along is harmless: the bits decide which fields count.
        SvgExtendedStyle? sourceExtended = source.Extended;
        if (sourceExtended is not null && ((applied & ExtendedLowProperties) != 0 || sourceExtended.HasHighContent))
        {
            this.Extended = this.Extended is null
                ? sourceExtended
                : SvgExtendedStyle.Merge(this.Extended, sourceExtended, new SvgPropertySet(applied, sourceExtended.SpecifiedHigh));
        }
        else if (this.Extended?.Variables is SvgVariableStyle variables && (variables.Pending.Low & applied) != 0)
        {
            // A declaration of the source without var() beats a pending declaration of this style.
            this.Extended = this.Extended.WithoutPending(new SvgPropertySet(applied, 0));
        }

        // The keyword bits of the applied properties come from the source; the bits of the other properties stay. The
        // high bits were merged with the extension above.
        this.specifiedLow |= applied;
        this.inheritLow = (this.inheritLow & ~applied) | source.inheritLow;
        this.initialLow = (this.initialLow & ~applied) | source.initialLow;
    }

    /// <summary>
    /// Returns the bit of a property from 64 on in the high word of a mask.
    /// </summary>
    /// <param name="property">The property; its id is at least 64.</param>
    /// <returns>The bit.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong HighBit(SvgPropertyId property) => 1UL << ((int)property - 64);

    /// <summary>
    /// Sets the specified bit of a property and its keyword bits, and clears a pending declaration of the property: a
    /// later declaration without <c>var()</c> wins over it.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="inherit">Whether the value is the <c>inherit</c> keyword.</param>
    /// <param name="initial">Whether the value is the <c>initial</c> keyword.</param>
    private void Mark(SvgPropertyId property, bool inherit, bool initial)
    {
        if ((int)property < 64)
        {
            ulong bit = 1UL << (int)property;
            this.specifiedLow |= bit;
            this.inheritLow = inherit ? this.inheritLow | bit : this.inheritLow & ~bit;
            this.initialLow = initial ? this.initialLow | bit : this.initialLow & ~bit;
            this.Extended?.Variables?.RemovePending(property);
            return;
        }

        SvgExtendedStyle extended = this.GetOrCreateExtended();
        ulong high = HighBit(property);
        extended.SpecifiedHigh |= high;
        extended.InheritHigh = inherit ? extended.InheritHigh | high : extended.InheritHigh & ~high;
        extended.InitialHigh = initial ? extended.InitialHigh | high : extended.InitialHigh & ~high;
        extended.Variables?.RemovePending(property);
    }
}
