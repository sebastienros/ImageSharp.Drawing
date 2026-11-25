// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Keeps hexadecimal and named colors by their source text during one document read.
/// </summary>
/// <remarks>
/// ImageSharp parses these forms from a <see cref="string"/> only. Documents repeat a few colors many times, so the cache
/// turns the repeated lookups into span lookups that do not allocate. One reader owns one cache; it is not thread-safe.
/// </remarks>
internal sealed class SvgColorCache
{
    private readonly Dictionary<string, Color> colors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color>.AlternateLookup<ReadOnlySpan<char>> lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgColorCache"/> class.
    /// </summary>
    public SvgColorCache() => this.lookup = this.colors.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Gets a cached color by its source text.
    /// </summary>
    /// <param name="text">The trimmed color text.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the cached color.</param>
    /// <returns><see langword="true"/> if the text is cached; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(ReadOnlySpan<char> text, out Color value) => this.lookup.TryGetValue(text, out value);

    /// <summary>
    /// Adds a parsed color.
    /// </summary>
    /// <param name="text">The trimmed color text.</param>
    /// <param name="value">The color.</param>
    public void Add(string text, Color value) => this.colors[text] = value;
}
