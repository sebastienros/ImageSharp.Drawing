// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One text content element of a laid out <c>text</c> element: the <c>text</c> element itself, a <c>tspan</c>, an
/// <c>a</c>, a <c>textPath</c>, or a <c>tref</c>, with its computed style and its range of characters.
/// </summary>
/// <remarks>
/// Spans are in document order, which is preorder: a span comes before its descendants, and the characters of a span
/// include those of its descendants. Span 0 is the <c>text</c> element.
/// </remarks>
internal sealed class SvgTextSpan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextSpan"/> class.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="kind">The element kind.</param>
    /// <param name="parent">The index of the parent span, or -1 for the <c>text</c> element.</param>
    /// <param name="style">The computed style of the element.</param>
    /// <param name="textPath">The element index of the nearest <c>textPath</c> ancestor or self, or -1.</param>
    public SvgTextSpan(int element, SvgElementKind kind, int parent, in SvgComputedStyle style, int textPath)
    {
        this.Element = element;
        this.Kind = kind;
        this.Parent = parent;
        this.Style = style;
        this.TextPath = textPath;
    }

    /// <summary>
    /// Gets the element index.
    /// </summary>
    public int Element { get; }

    /// <summary>
    /// Gets the element kind.
    /// </summary>
    public SvgElementKind Kind { get; }

    /// <summary>
    /// Gets the index of the parent span, or -1 for the <c>text</c> element.
    /// </summary>
    public int Parent { get; }

    /// <summary>
    /// Gets the computed style of the element.
    /// </summary>
    public SvgComputedStyle Style { get; }

    /// <summary>
    /// Gets the element index of the <c>textPath</c> that the characters of the span lie on, or -1. Package T2 maps
    /// such characters to the path in stage 6; until then they do not render.
    /// </summary>
    public int TextPath { get; }

    /// <summary>
    /// Gets or sets the index of the first character of the span in <see cref="SvgTextContent.Text"/>.
    /// </summary>
    public int CharStart { get; set; }

    /// <summary>
    /// Gets or sets the index after the last character of the span.
    /// </summary>
    public int CharEnd { get; set; }

    /// <summary>
    /// Gets or sets the index of the first addressable character of the span: the count of addressable characters before
    /// it. Position lists of the element apply from here.
    /// </summary>
    public int AddressableStart { get; set; }

    /// <summary>
    /// Gets or sets the index after the last addressable character of the span.
    /// </summary>
    public int AddressableEnd { get; set; }
}
