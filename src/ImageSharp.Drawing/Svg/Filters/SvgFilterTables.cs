// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The tables that the reader fills for filters: the attributes of <c>filter</c> elements, of the filter primitives, and
/// of their children.
/// </summary>
/// <remarks>
/// The reader creates one instance on the first filter element of a document, so documents without filters allocate
/// nothing for it. The element tree takes the instance over; it does not change after the reader returns.
/// </remarks>
internal sealed class SvgFilterTables
{
    /// <summary>
    /// Gets the attributes of <c>filter</c> elements.
    /// </summary>
    public List<SvgFilterAttributes> Filters { get; } = [];

    /// <summary>
    /// Gets the attributes of filter primitives and of their children.
    /// </summary>
    public List<SvgFilterPrimitiveAttributes> Primitives { get; } = [];

    /// <summary>
    /// Gets the attribute values that <see cref="SvgFilterPrimitiveAttributes.ValueStart"/> indexes.
    /// </summary>
    public List<SvgFilterAttributeValue> Values { get; } = [];

    /// <summary>
    /// Gets the numbers that <see cref="SvgFilterAttributeValue.NumberStart"/> indexes.
    /// </summary>
    public List<float> Numbers { get; } = [];

    /// <summary>
    /// Returns whether an element kind stores its attributes in <see cref="Primitives"/>.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <returns><see langword="true"/> for a filter primitive or a child of one; otherwise, <see langword="false"/>.</returns>
    public static bool IsPrimitiveRecord(SvgElementKind kind) => kind >= SvgElementKind.FeBlend;

    /// <summary>
    /// Returns whether an element kind is a filter primitive, a direct child of <c>filter</c> that produces a result.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <returns><see langword="true"/> for a filter primitive; otherwise, <see langword="false"/>.</returns>
    public static bool IsPrimitive(SvgElementKind kind) => kind is >= SvgElementKind.FeBlend and <= SvgElementKind.FeTurbulence;

    /// <summary>
    /// Returns whether an element kind is a light source.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <returns><see langword="true"/> for <c>feDistantLight</c>, <c>fePointLight</c>, and <c>feSpotLight</c>; otherwise, <see langword="false"/>.</returns>
    public static bool IsLightSource(SvgElementKind kind) => kind is >= SvgElementKind.FeDistantLight and <= SvgElementKind.FeSpotLight;

    /// <summary>
    /// Adds the default record for a new <c>filter</c> element or filter primitive.
    /// </summary>
    /// <param name="kind">The element kind: <see cref="SvgElementKind.Filter"/> or a kind for which <see cref="IsPrimitiveRecord(SvgElementKind)"/> is true.</param>
    /// <returns>The index of the record.</returns>
    public int Add(SvgElementKind kind)
    {
        if (kind == SvgElementKind.Filter)
        {
            this.Filters.Add(default);
            return this.Filters.Count - 1;
        }

        this.Primitives.Add(new SvgFilterPrimitiveAttributes { ValueStart = this.Values.Count });
        return this.Primitives.Count - 1;
    }

    /// <summary>
    /// Stores a reference attribute: the id of a <c>filter</c>, or the whole URL of an <c>feImage</c>.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <param name="dataIndex">The index of the record.</param>
    /// <param name="value">The <c>href</c> value.</param>
    public void SetHref(SvgElementKind kind, int dataIndex, string value)
    {
        ReadOnlySpan<char> reference = SvgNumberParser.Trim(value);
        if (kind == SvgElementKind.Filter)
        {
            // Only a same-document fragment names an element.
            CollectionsMarshal.AsSpan(this.Filters)[dataIndex].Href = reference.Length > 1 && reference[0] == '#' ? reference[1..].ToString() : null;
        }
        else if (kind == SvgElementKind.FeImage)
        {
            CollectionsMarshal.AsSpan(this.Primitives)[dataIndex].Href = reference.Length == value.Length ? value : reference.ToString();
        }
    }

    /// <summary>
    /// Parses an attribute of a <c>filter</c> element, a filter primitive, or a child of a primitive into its record.
    /// Attributes that do not apply are ignored.
    /// </summary>
    /// <param name="kind">The element kind.</param>
    /// <param name="dataIndex">The index of the record.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The attribute value.</param>
    public void Set(SvgElementKind kind, int dataIndex, SvgAttributeId attribute, string value)
    {
        if (kind == SvgElementKind.Filter)
        {
            SetFilter(ref CollectionsMarshal.AsSpan(this.Filters)[dataIndex], attribute, value);
            return;
        }

        ref SvgFilterPrimitiveAttributes primitive = ref CollectionsMarshal.AsSpan(this.Primitives)[dataIndex];
        bool light = IsLightSource(kind);
        switch (attribute)
        {
            // The light sources have numbers for x and y, which go to the value table below.
            case SvgAttributeId.X when !light:
                primitive.X = Length(value);
                return;
            case SvgAttributeId.Y when !light:
                primitive.Y = Length(value);
                return;
            case SvgAttributeId.Width:
                primitive.Width = Length(value);
                return;
            case SvgAttributeId.Height:
                primitive.Height = Length(value);
                return;
            case SvgAttributeId.In:
                primitive.In = Trimmed(value);
                return;
            case SvgAttributeId.In2:
                primitive.In2 = Trimmed(value);
                return;
            case SvgAttributeId.Result:
                string result = Trimmed(value);
                primitive.Result = result.Length > 0 ? result : null;
                return;
            case SvgAttributeId.PreserveAspectRatio:
                if (SvgPreserveAspectRatio.TryParse(value, out SvgPreserveAspectRatio aspect))
                {
                    primitive.PreserveAspectRatio = aspect;
                    primitive.HasPreserveAspectRatio = true;
                }

                return;
        }

        // Every other attribute keeps its text and its numbers. The values of one element are contiguous, because the
        // reader reads all attributes of an element before the next element starts. A repeated attribute is not
        // possible in well-formed XML.
        string text = Trimmed(value);
        int start = this.Numbers.Count;
        bool isList = ParseNumbers(text, this.Numbers);
        if (!isList)
        {
            this.Numbers.RemoveRange(start, this.Numbers.Count - start);
        }

        this.Values.Add(new SvgFilterAttributeValue(attribute, text, start, this.Numbers.Count - start, isList));
        primitive.ValueCount++;
    }

    /// <summary>
    /// Parses an attribute of a <c>filter</c> element.
    /// </summary>
    /// <param name="filter">The record.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="value">The value.</param>
    private static void SetFilter(ref SvgFilterAttributes filter, SvgAttributeId attribute, string value)
    {
        switch (attribute)
        {
            case SvgAttributeId.X:
                filter.X = Length(value);
                break;
            case SvgAttributeId.Y:
                filter.Y = Length(value);
                break;
            case SvgAttributeId.Width:
                filter.Width = Length(value);
                break;
            case SvgAttributeId.Height:
                filter.Height = Length(value);
                break;
            case SvgAttributeId.FilterUnits:
                filter.Units = Units(value);
                break;
            case SvgAttributeId.PrimitiveUnits:
                filter.PrimitiveUnits = Units(value);
                break;
        }
    }

    /// <summary>
    /// Parses a list of numbers separated by whitespace and an optional comma.
    /// </summary>
    /// <param name="source">The trimmed value.</param>
    /// <param name="numbers">The table that receives the numbers.</param>
    /// <returns><see langword="true"/> if the whole value is such a list; otherwise, <see langword="false"/>.</returns>
    private static bool ParseNumbers(ReadOnlySpan<char> source, List<float> numbers)
    {
        while (!source.IsEmpty)
        {
            if (!SvgNumberParser.TryReadNumber(source, false, out float number, out int length))
            {
                return false;
            }

            numbers.Add(number);
            source = source[length..];
            if (!SvgNumberParser.TrySkipCommaWhitespace(ref source))
            {
                // A trailing comma.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a value without leading and trailing whitespace, reusing the string when it has none.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The trimmed value.</returns>
    private static string Trimmed(string value)
    {
        ReadOnlySpan<char> trimmed = SvgNumberParser.Trim(value);
        return trimmed.Length == value.Length ? value : trimmed.ToString();
    }

    /// <summary>
    /// Parses a length, or returns the unspecified length.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The length.</returns>
    private static SvgLength Length(string value) => SvgLength.TryParse(value, out SvgLength length) ? length : default;

    /// <summary>
    /// Parses a coordinate system keyword. Keywords are case-sensitive, as in the SVG grammar.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The units, or <see cref="SvgCoordinateUnits.Unspecified"/> for an invalid value.</returns>
    private static SvgCoordinateUnits Units(string value)
        => SvgNumberParser.Trim(value) switch
        {
            "userSpaceOnUse" => SvgCoordinateUnits.UserSpaceOnUse,
            "objectBoundingBox" => SvgCoordinateUnits.ObjectBoundingBox,
            _ => SvgCoordinateUnits.Unspecified
        };
}
