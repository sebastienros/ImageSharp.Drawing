// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The output of stage 1 of the text layout pipeline (<see cref="SvgTextCharacters"/>): the characters of one
/// <c>text</c> element after white space processing, in logical order, with their spans and position values.
/// </summary>
/// <remarks>
/// Contract for the later stages: <see cref="Text"/> and <see cref="Characters"/> have the same length, one entry per
/// UTF-16 code unit. Every character belongs to exactly one innermost span, and the spans are in document order with
/// nested character ranges. Characters inside a <c>textPath</c> are included and carry the path in their span
/// (<see cref="SvgTextSpan.TextPath"/>); a <c>tref</c> holds the characters of the <c>text</c> element it references.
/// </remarks>
internal sealed class SvgTextContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextContent"/> class.
    /// </summary>
    /// <param name="text">The characters.</param>
    /// <param name="characters">The layout data per character.</param>
    /// <param name="spans">The spans in document order.</param>
    public SvgTextContent(string text, SvgTextCharacter[] characters, SvgTextSpan[] spans)
    {
        this.Text = text;
        this.Characters = characters;
        this.Spans = spans;
    }

    /// <summary>
    /// Gets the characters after white space processing.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the layout data per character.
    /// </summary>
    public SvgTextCharacter[] Characters { get; }

    /// <summary>
    /// Gets the spans in document order. Span 0 is the <c>text</c> element.
    /// </summary>
    public SvgTextSpan[] Spans { get; }

    /// <summary>
    /// Gets or sets the direction data, which stage 3 sets before shaping. <see langword="null"/> for horizontal text
    /// without right-to-left content, which keeps the logical order.
    /// </summary>
    public SvgTextDirections? Directions { get; set; }

    /// <summary>
    /// Returns the span of an element.
    /// </summary>
    /// <remarks>
    /// The spans are in document order and the element tree numbers its elements in document order, so the element
    /// indices of the spans ascend and a binary search finds the span. A text with many spans then pays O(log n) per
    /// lookup, such as the bounding box of each <c>tspan</c> that has a clip in bounding box units.
    /// </remarks>
    /// <param name="element">The element index.</param>
    /// <returns>The span index, or -1 when no span is the element.</returns>
    public int FindSpan(int element)
    {
        SvgTextSpan[] spans = this.Spans;
        int low = 0;
        int high = spans.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            int value = spans[middle].Element;
            if (value == element)
            {
                return middle;
            }

            if (value < element)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }
}
