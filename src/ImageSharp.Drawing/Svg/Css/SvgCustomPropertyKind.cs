// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of the specified value of a custom property declaration.
/// </summary>
internal enum SvgCustomPropertyKind : byte
{
    /// <summary>
    /// Token text, in <see cref="SvgCustomPropertyValue.Text"/>.
    /// </summary>
    Text,

    /// <summary>
    /// <c>inherit</c>, and <c>unset</c>, which acts as <c>inherit</c> because custom properties inherit.
    /// </summary>
    Inherit,

    /// <summary>
    /// <c>initial</c>: the guaranteed-invalid value.
    /// </summary>
    Initial
}
