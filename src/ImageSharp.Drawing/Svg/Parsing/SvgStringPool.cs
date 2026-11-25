// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Interns strings by their characters during one document read.
/// </summary>
/// <remarks>
/// Class names repeat across many elements, and design tools write them in both <c>class</c> attributes and style sheets.
/// Interning gives one string per distinct name, so repeated names do not allocate, and equal names in elements and
/// selectors are the same instance, which makes the equality test during matching a reference comparison.
/// </remarks>
internal sealed class SvgStringPool
{
    private readonly HashSet<string> strings = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgStringPool"/> class.
    /// </summary>
    public SvgStringPool() => this.lookup = this.strings.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Returns the pooled string with the given characters, adding it on first use.
    /// </summary>
    /// <param name="value">The characters.</param>
    /// <returns>The pooled string.</returns>
    public string Get(ReadOnlySpan<char> value)
    {
        if (!this.lookup.TryGetValue(value, out string? result))
        {
            result = value.ToString();
            _ = this.strings.Add(result);
        }

        return result;
    }
}
