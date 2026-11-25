// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The test of an attribute selector beyond its name.
/// </summary>
internal readonly struct SvgCssAttributeSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssAttributeSelector"/> struct.
    /// </summary>
    /// <param name="attributeOperator">The operator.</param>
    /// <param name="value">The value, or <see langword="null"/> for presence.</param>
    /// <param name="ignoreCase">Whether the value compares ASCII case-insensitively.</param>
    /// <param name="anyNamespace">Whether the attribute may be in any namespace.</param>
    public SvgCssAttributeSelector(SvgCssAttributeOperator attributeOperator, string? value, bool ignoreCase, bool anyNamespace)
    {
        this.Operator = attributeOperator;
        this.Value = value;
        this.IgnoreCase = ignoreCase;
        this.AnyNamespace = anyNamespace;
    }

    /// <summary>
    /// Gets the operator.
    /// </summary>
    public SvgCssAttributeOperator Operator { get; }

    /// <summary>
    /// Gets the value, with escapes decoded; <see langword="null"/> for presence.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// Gets a value indicating whether the selector has the <c>i</c> flag, so the value compares ASCII
    /// case-insensitively. Without the flag, values in an XML document compare case-sensitively.
    /// </summary>
    public bool IgnoreCase { get; }

    /// <summary>
    /// Gets a value indicating whether the selector has the <c>*|</c> namespace prefix and matches the attribute in any
    /// namespace. Without it, only attributes in no namespace match.
    /// </summary>
    public bool AnyNamespace { get; }
}
