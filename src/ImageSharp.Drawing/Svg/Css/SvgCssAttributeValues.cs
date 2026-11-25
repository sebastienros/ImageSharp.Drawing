// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The raw values of the attributes that attribute selectors test, for the elements of one document.
/// </summary>
/// <remarks>
/// <para>
/// The reader keeps no raw attribute values in the element tree. When a style sheet has attribute selectors, it records
/// the attributes with the tested local names here, from the element where it learns the name on, and reads the start of
/// the document a second time for the elements before. Entries therefore arrive in any element order.
/// <see cref="Complete(int)"/> sorts them by element with a counting sort, so the entries of one element are a contiguous
/// range found in constant time.
/// </para>
/// <para>
/// The table lives only while the cascade runs; the element tree does not keep it.
/// </para>
/// </remarks>
internal sealed class SvgCssAttributeValues
{
    private static readonly Entry[] NoEntries = [];

    private readonly List<Entry> pending = [];
    private Entry[] entries = NoEntries;
    private int[]? starts;

    /// <summary>
    /// Records an attribute of an element.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="name">The attribute local name.</param>
    /// <param name="hasNamespace">Whether the attribute is in a namespace.</param>
    /// <param name="value">The attribute value.</param>
    public void Add(int element, string name, bool hasNamespace, string value) => this.pending.Add(new Entry(element, name, hasNamespace, value));

    /// <summary>
    /// Sorts the recorded entries by element. Call once, after the last entry is added.
    /// </summary>
    /// <param name="elementCount">The number of elements of the document.</param>
    public void Complete(int elementCount)
    {
        // Counting sort: count the entries per element, turn the counts into start offsets, then place each entry.
        int[] starts = new int[elementCount + 1];
        foreach (Entry entry in this.pending)
        {
            starts[entry.Element + 1]++;
        }

        for (int i = 1; i < starts.Length; i++)
        {
            starts[i] += starts[i - 1];
        }

        Entry[] sorted = new Entry[this.pending.Count];
        int[] next = (int[])starts.Clone();
        foreach (Entry entry in this.pending)
        {
            sorted[next[entry.Element]++] = entry;
        }

        this.entries = sorted;
        this.starts = starts;
        this.pending.Clear();
    }

    /// <summary>
    /// Gets the recorded attributes of an element.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns>The entries.</returns>
    public ReadOnlySpan<Entry> Get(int element)
    {
        int start = this.starts![element];
        return this.entries.AsSpan(start, this.starts[element + 1] - start);
    }

    /// <summary>
    /// One recorded attribute.
    /// </summary>
    internal readonly struct Entry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> struct.
        /// </summary>
        /// <param name="element">The element index.</param>
        /// <param name="name">The attribute local name.</param>
        /// <param name="hasNamespace">Whether the attribute is in a namespace.</param>
        /// <param name="value">The attribute value.</param>
        public Entry(int element, string name, bool hasNamespace, string value)
        {
            this.Element = element;
            this.Name = name;
            this.HasNamespace = hasNamespace;
            this.Value = value;
        }

        /// <summary>
        /// Gets the element index.
        /// </summary>
        public int Element { get; }

        /// <summary>
        /// Gets the attribute local name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets a value indicating whether the attribute is in a namespace, such as <c>xlink:href</c>.
        /// </summary>
        public bool HasNamespace { get; }

        /// <summary>
        /// Gets the attribute value as the XML reader reports it, after attribute value normalization.
        /// </summary>
        public string Value { get; }
    }
}
