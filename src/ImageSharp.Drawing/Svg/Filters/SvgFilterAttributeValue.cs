// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One attribute of a filter primitive that has no typed field in <see cref="SvgFilterPrimitiveAttributes"/>, parsed when
/// the reader reads it.
/// </summary>
/// <remarks>
/// <para>
/// The attributes of the filter primitives are numbers, lists of numbers, or keywords. The reader stores each value in
/// both forms that a consumer can need: <see cref="Text"/>, the trimmed value as written, for keywords; and the numbers
/// that the value holds, separated by whitespace and an optional comma, for everything else. <see cref="IsNumberList"/>
/// says whether the whole value is such a list; when it is not, <see cref="NumberCount"/> is 0. An empty value is a list
/// of no numbers. Each primitive applies its own rule for a value that is missing, invalid, or of the wrong length: the
/// specification differs per attribute.
/// </para>
/// <para>
/// Read the numbers with <see cref="SvgElementTree.GetFilterNumbers(in SvgFilterAttributeValue)"/>.
/// </para>
/// </remarks>
internal readonly struct SvgFilterAttributeValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterAttributeValue"/> struct.
    /// </summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="text">The trimmed value.</param>
    /// <param name="numberStart">The index of the first number in the number table.</param>
    /// <param name="numberCount">The number of numbers.</param>
    /// <param name="isNumberList">Whether the whole value is a list of numbers.</param>
    public SvgFilterAttributeValue(SvgAttributeId attribute, string text, int numberStart, int numberCount, bool isNumberList)
    {
        this.Attribute = attribute;
        this.Text = text;
        this.NumberStart = numberStart;
        this.NumberCount = numberCount;
        this.IsNumberList = isNumberList;
    }

    /// <summary>
    /// Gets the attribute.
    /// </summary>
    public SvgAttributeId Attribute { get; }

    /// <summary>
    /// Gets the trimmed value as written.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the index of the first number in the number table of <see cref="SvgFilterTables"/>.
    /// </summary>
    public int NumberStart { get; }

    /// <summary>
    /// Gets the number of numbers, or 0 when the value is not a list of numbers.
    /// </summary>
    public int NumberCount { get; }

    /// <summary>
    /// Gets a value indicating whether the whole value is a list of finite numbers, separated by whitespace and an optional
    /// comma. An empty value is an empty list.
    /// </summary>
    public bool IsNumberList { get; }
}
