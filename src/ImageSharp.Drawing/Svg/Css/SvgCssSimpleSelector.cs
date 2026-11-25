// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One simple selector of a compound selector.
/// </summary>
internal readonly struct SvgCssSimpleSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssSimpleSelector"/> struct.
    /// </summary>
    /// <param name="kind">The selector kind.</param>
    /// <param name="name">The type, id, or class name; <see langword="null"/> for the universal selector.</param>
    public SvgCssSimpleSelector(SvgCssSimpleSelectorKind kind, string? name)
    {
        this.Kind = kind;
        this.Name = name;
    }

    /// <summary>
    /// Gets the selector kind.
    /// </summary>
    public SvgCssSimpleSelectorKind Kind { get; }

    /// <summary>
    /// Gets the type, id, or class name, with escapes decoded; <see langword="null"/> for the universal selector.
    /// </summary>
    public string? Name { get; }
}
