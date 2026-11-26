// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A declaration whose value contains <c>var()</c>, kept as text until the computed-value stage (CSS Custom Properties
/// Level 1, "pending-substitution value").
/// </summary>
internal readonly struct SvgPendingValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPendingValue"/> struct.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="text">The value text.</param>
    /// <param name="shorthand">The shorthand of the declaration, or <see langword="null"/>.</param>
    public SvgPendingValue(SvgPropertyId property, string text, SvgShorthand? shorthand)
    {
        this.Property = property;
        this.Text = text;
        this.Shorthand = shorthand;
    }

    /// <summary>
    /// Gets the property whose value the declaration gives.
    /// </summary>
    public SvgPropertyId Property { get; }

    /// <summary>
    /// Gets the value text, with <c>var()</c> unsubstituted and without <c>!important</c>. Comments inside a value from a
    /// style sheet or a <c>style</c> attribute are replaced by a space and escapes are decoded, as for other declarations.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the shorthand whose value <see cref="Text"/> is, or <see langword="null"/> when <see cref="Text"/> is the value
    /// of <see cref="Property"/> itself. CSS gives every longhand of a shorthand with <c>var()</c> the text of the
    /// shorthand: after substitution, the text parses as the shorthand and the longhand takes its part of the result.
    /// </summary>
    public SvgShorthand? Shorthand { get; }
}
