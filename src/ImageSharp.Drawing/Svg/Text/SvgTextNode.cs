// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One run of character data inside a <c>text</c> element, as the XML reader delivered it: text, CDATA, and white space
/// nodes, with entities expanded. The reader keeps character data only inside <c>text</c> elements.
/// </summary>
/// <remarks>
/// Character data and child elements interleave in document order. A node belongs to its <see cref="Parent"/> and comes
/// before every child of the parent whose element index is at least <see cref="Position"/>, and after the others.
/// </remarks>
internal readonly struct SvgTextNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextNode"/> struct.
    /// </summary>
    /// <param name="parent">The index of the element that contains the characters.</param>
    /// <param name="position">The element count when the node was read: the index of the next element in document order.</param>
    /// <param name="start">The index of the first character in the character table.</param>
    /// <param name="length">The number of characters.</param>
    public SvgTextNode(int parent, int position, int start, int length)
    {
        this.Parent = parent;
        this.Position = position;
        this.Start = start;
        this.Length = length;
    }

    /// <summary>
    /// Gets the index of the element that contains the characters.
    /// </summary>
    public int Parent { get; }

    /// <summary>
    /// Gets the index of the next element in document order when the node was read.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the index of the first character in the character table of the element tree.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of characters.
    /// </summary>
    public int Length { get; }
}
