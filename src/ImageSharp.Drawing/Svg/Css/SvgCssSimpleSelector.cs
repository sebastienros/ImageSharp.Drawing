// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One simple selector of a compound selector.
/// </summary>
/// <remarks>
/// The struct stays as small as the type, id, and class selectors that most style sheets consist of need. Attribute
/// selectors and functional pseudo-classes keep their other data in side tables of the style sheet, which
/// <see cref="Data"/> indexes.
/// </remarks>
internal readonly struct SvgCssSimpleSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssSimpleSelector"/> struct.
    /// </summary>
    /// <param name="kind">The selector kind.</param>
    /// <param name="name">The type, id, class, or attribute name; <see langword="null"/> for the other kinds.</param>
    /// <param name="data">The index into the side table of the kind, or 0 for a kind without one.</param>
    public SvgCssSimpleSelector(SvgCssSimpleSelectorKind kind, string? name, int data = 0)
    {
        this.Kind = kind;
        this.Name = name;
        this.Data = data;
    }

    /// <summary>
    /// Gets the selector kind.
    /// </summary>
    public SvgCssSimpleSelectorKind Kind { get; }

    /// <summary>
    /// Gets the type, id, or class name, or the local name of an attribute selector, with escapes decoded; the language
    /// ranges of <c>:lang()</c> (see <see cref="SvgCssLanguageRanges"/>); <see langword="null"/> for the other kinds.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the index into <see cref="SvgStyleSheet.AttributeSelectors"/> for an attribute selector, or into
    /// <see cref="SvgStyleSheet.FunctionalSelectors"/> for <c>:nth-*</c>, <c>:not()</c>, <c>:is()</c>, and
    /// <c>:where()</c>.
    /// </summary>
    public int Data { get; }
}
