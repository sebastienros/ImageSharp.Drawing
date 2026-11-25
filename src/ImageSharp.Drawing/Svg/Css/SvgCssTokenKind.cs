// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The token kinds of CSS Syntax Level 3, section 4.
/// </summary>
internal enum SvgCssTokenKind : byte
{
    /// <summary>
    /// The end of the input.
    /// </summary>
    EndOfFile,

    /// <summary>
    /// One or more whitespace characters.
    /// </summary>
    Whitespace,

    /// <summary>
    /// An identifier.
    /// </summary>
    Ident,

    /// <summary>
    /// A function name followed by <c>(</c>.
    /// </summary>
    Function,

    /// <summary>
    /// <c>@</c> followed by a name.
    /// </summary>
    AtKeyword,

    /// <summary>
    /// <c>#</c> followed by a name.
    /// </summary>
    Hash,

    /// <summary>
    /// A quoted string.
    /// </summary>
    String,

    /// <summary>
    /// A string that a newline ended.
    /// </summary>
    BadString,

    /// <summary>
    /// An unquoted <c>url(...)</c>.
    /// </summary>
    Url,

    /// <summary>
    /// A malformed unquoted <c>url(...)</c>.
    /// </summary>
    BadUrl,

    /// <summary>
    /// A single character that starts no other token.
    /// </summary>
    Delim,

    /// <summary>
    /// A number.
    /// </summary>
    Number,

    /// <summary>
    /// A number followed by <c>%</c>.
    /// </summary>
    Percentage,

    /// <summary>
    /// A number followed by a unit name.
    /// </summary>
    Dimension,

    /// <summary>
    /// <c>&lt;!--</c>.
    /// </summary>
    Cdo,

    /// <summary>
    /// <c>--&gt;</c>.
    /// </summary>
    Cdc,

    /// <summary>
    /// <c>:</c>.
    /// </summary>
    Colon,

    /// <summary>
    /// <c>;</c>.
    /// </summary>
    Semicolon,

    /// <summary>
    /// <c>,</c>.
    /// </summary>
    Comma,

    /// <summary>
    /// <c>[</c>.
    /// </summary>
    OpenSquare,

    /// <summary>
    /// <c>]</c>.
    /// </summary>
    CloseSquare,

    /// <summary>
    /// <c>(</c>.
    /// </summary>
    OpenParenthesis,

    /// <summary>
    /// <c>)</c>.
    /// </summary>
    CloseParenthesis,

    /// <summary>
    /// <c>{</c>.
    /// </summary>
    OpenCurly,

    /// <summary>
    /// <c>}</c>.
    /// </summary>
    CloseCurly
}
