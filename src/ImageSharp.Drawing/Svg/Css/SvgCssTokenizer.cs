// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The CSS tokenizer of CSS Syntax Level 3, section 4.3, over a span of characters. Tokens are ranges of the source, so
/// tokenizing allocates nothing; <see cref="Unescape(ReadOnlySpan{char})"/> decodes a value only when it has escapes.
/// </summary>
/// <remarks>
/// The structure and the escape, string, URL, and comment handling follow the CSS tokenizer of the Jint project
/// (BSD 2-Clause License, see THIRD-PARTY-NOTICES), adapted to work on a span and to return ranges. The tokenizer works on
/// UTF-16 code units. Every character at or above U+0080 is a name character in CSS, so surrogate pairs need no special
/// case. The input preprocessing of section 3.3 is folded into the character tests: carriage return and form feed count as
/// newlines, and NUL counts as a name character as its replacement U+FFFD does.
/// </remarks>
internal ref struct SvgCssTokenizer
{
    /// <summary>
    /// The character that <see cref="Peek(int)"/> returns past the end of the input. U+FFFF is a noncharacter, so valid
    /// CSS does not contain it.
    /// </summary>
    private const char EndOfInput = '\uFFFF';

    private readonly ReadOnlySpan<char> source;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssTokenizer"/> struct.
    /// </summary>
    /// <param name="source">The characters to tokenize.</param>
    public SvgCssTokenizer(ReadOnlySpan<char> source)
    {
        this.source = source;
        this.position = 0;
    }

    /// <summary>
    /// Gets the index of the next character to read.
    /// </summary>
    public readonly int Position => this.position;

    /// <summary>
    /// Reads the next token. Comments are skipped. At the end of the input every call returns
    /// <see cref="SvgCssTokenKind.EndOfFile"/>.
    /// </summary>
    /// <returns>The token.</returns>
    public SvgCssToken Next()
    {
        this.SkipComments();
        int start = this.position;
        if (start >= this.source.Length)
        {
            return new SvgCssToken(SvgCssTokenKind.EndOfFile, start, start, start, start, false, false);
        }

        char c = this.source[start];
        if (IsWhitespace(c))
        {
            while (this.position < this.source.Length && IsWhitespace(this.source[this.position]))
            {
                this.position++;
            }

            return this.Make(SvgCssTokenKind.Whitespace, start);
        }

        switch (c)
        {
            case '"' or '\'':
                return this.ConsumeString(start, c);
            case '#':
                if (IsNameCharacter(this.Peek(1)) || this.IsValidEscape(start + 1))
                {
                    bool isId = this.WouldStartIdentifier(start + 1);
                    this.position++;
                    int nameStart = this.position;
                    bool hasEscape = this.ConsumeName();
                    return new SvgCssToken(SvgCssTokenKind.Hash, start, this.position, nameStart, this.position, hasEscape, isId);
                }

                break;
            case '(':
                return this.MakeSingle(SvgCssTokenKind.OpenParenthesis, start);
            case ')':
                return this.MakeSingle(SvgCssTokenKind.CloseParenthesis, start);
            case '[':
                return this.MakeSingle(SvgCssTokenKind.OpenSquare, start);
            case ']':
                return this.MakeSingle(SvgCssTokenKind.CloseSquare, start);
            case '{':
                return this.MakeSingle(SvgCssTokenKind.OpenCurly, start);
            case '}':
                return this.MakeSingle(SvgCssTokenKind.CloseCurly, start);
            case ',':
                return this.MakeSingle(SvgCssTokenKind.Comma, start);
            case ':':
                return this.MakeSingle(SvgCssTokenKind.Colon, start);
            case ';':
                return this.MakeSingle(SvgCssTokenKind.Semicolon, start);
            case '+' or '.' when this.WouldStartNumber(start):
                return this.ConsumeNumeric(start);
            case '-':
                if (this.WouldStartNumber(start))
                {
                    return this.ConsumeNumeric(start);
                }

                if (this.Peek(1) == '-' && this.Peek(2) == '>')
                {
                    this.position += 3;
                    return this.Make(SvgCssTokenKind.Cdc, start);
                }

                if (this.WouldStartIdentifier(start))
                {
                    return this.ConsumeIdentLike(start);
                }

                break;
            case '<':
                if (this.Peek(1) == '!' && this.Peek(2) == '-' && this.Peek(3) == '-')
                {
                    this.position += 4;
                    return this.Make(SvgCssTokenKind.Cdo, start);
                }

                break;
            case '@':
                if (this.WouldStartIdentifier(start + 1))
                {
                    this.position++;
                    int nameStart = this.position;
                    bool hasEscape = this.ConsumeName();
                    return new SvgCssToken(SvgCssTokenKind.AtKeyword, start, this.position, nameStart, this.position, hasEscape, false);
                }

                break;
            case '\\':
                if (this.IsValidEscape(start))
                {
                    return this.ConsumeIdentLike(start);
                }

                break;
            default:
                if (char.IsAsciiDigit(c))
                {
                    return this.ConsumeNumeric(start);
                }

                if (IsNameStart(c))
                {
                    return this.ConsumeIdentLike(start);
                }

                break;
        }

        return this.MakeSingle(SvgCssTokenKind.Delim, start);
    }

    /// <summary>
    /// Decodes the escape sequences of a token value, as section 4.3.7 consumes them.
    /// </summary>
    /// <param name="raw">The raw value characters.</param>
    /// <returns>The decoded value.</returns>
    public static string Unescape(ReadOnlySpan<char> raw)
    {
        ValueStringBuilder builder = new(stackalloc char[128]);
        Span<char> encoded = stackalloc char[2];
        try
        {
            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c != '\\')
                {
                    builder.Append(c);
                    i++;
                    continue;
                }

                i++;
                if (i >= raw.Length)
                {
                    // An escape at the end of the input is U+FFFD.
                    builder.Append('\uFFFD');
                    break;
                }

                if (IsNewline(raw[i]))
                {
                    // An escaped newline in a string is a line continuation and produces nothing.
                    i += raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n' ? 2 : 1;
                    continue;
                }

                if (!char.IsAsciiHexDigit(raw[i]))
                {
                    builder.Append(raw[i]);
                    i++;
                    continue;
                }

                // Up to six hex digits, then one optional whitespace character.
                int value = 0;
                int digits = 0;
                while (digits < 6 && i < raw.Length && char.IsAsciiHexDigit(raw[i]))
                {
                    value = (value << 4) | HexValue(raw[i]);
                    i++;
                    digits++;
                }

                if (i < raw.Length && IsWhitespace(raw[i]))
                {
                    i += raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n' ? 2 : 1;
                }

                // Zero, surrogates, and values beyond the Unicode range decode to U+FFFD.
                if (value is 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                {
                    value = 0xFFFD;
                }

                int length = new Rune(value).EncodeToUtf16(encoded);
                builder.Append(encoded[..length]);
            }

            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Returns whether a character is CSS whitespace after preprocessing: space, tab, or a newline.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> if the character is whitespace; otherwise, <see langword="false"/>.</returns>
    public static bool IsWhitespace(char c) => c is ' ' or '\t' || IsNewline(c);

    /// <summary>
    /// Returns whether a character is a newline after preprocessing: line feed, carriage return, or form feed.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> if the character is a newline; otherwise, <see langword="false"/>.</returns>
    private static bool IsNewline(char c) => c is '\n' or '\r' or '\f';

    /// <summary>
    /// Returns whether a character starts a name: a letter, <c>_</c>, a non-ASCII character, or NUL.
    /// </summary>
    /// <param name="c">The character, or <see cref="EndOfInput"/> past the end of the input.</param>
    /// <returns><see langword="true"/> if the character starts a name; otherwise, <see langword="false"/>.</returns>
    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c is '_' or '\0' || (c >= 0x80 && c != EndOfInput);

    /// <summary>
    /// Returns whether a character continues a name.
    /// </summary>
    /// <param name="c">The character, or <see cref="EndOfInput"/> past the end of the input.</param>
    /// <returns><see langword="true"/> if the character continues a name; otherwise, <see langword="false"/>.</returns>
    private static bool IsNameCharacter(char c) => IsNameStart(c) || char.IsAsciiDigit(c) || c == '-';

    /// <summary>
    /// Returns the value of a hexadecimal digit.
    /// </summary>
    /// <param name="c">The digit.</param>
    /// <returns>The value from 0 to 15.</returns>
    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    /// <summary>
    /// Returns the character at an offset from the current position.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <returns>The character, or <see cref="EndOfInput"/> past the end.</returns>
    private readonly char Peek(int offset) => this.At(this.position + offset);

    /// <summary>
    /// Returns the character at an index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The character, or <see cref="EndOfInput"/> past the end.</returns>
    private readonly char At(int index) => (uint)index < (uint)this.source.Length ? this.source[index] : EndOfInput;

    /// <summary>
    /// Returns whether the two characters at <paramref name="index"/> are a valid escape (section 4.3.8).
    /// </summary>
    /// <param name="index">The index of the backslash.</param>
    /// <returns><see langword="true"/> if they are a valid escape; otherwise, <see langword="false"/>.</returns>
    private readonly bool IsValidEscape(int index) => this.At(index) == '\\' && !IsNewline(this.At(index + 1));

    /// <summary>
    /// Returns whether the characters at <paramref name="index"/> would start an identifier (section 4.3.9).
    /// </summary>
    /// <param name="index">The index of the first character.</param>
    /// <returns><see langword="true"/> if they would start an identifier; otherwise, <see langword="false"/>.</returns>
    private readonly bool WouldStartIdentifier(int index)
    {
        char first = this.At(index);
        if (first == '-')
        {
            char second = this.At(index + 1);
            return IsNameStart(second) || second == '-' || this.IsValidEscape(index + 1);
        }

        return IsNameStart(first) || this.IsValidEscape(index);
    }

    /// <summary>
    /// Returns whether the characters at <paramref name="index"/> would start a number (section 4.3.10).
    /// </summary>
    /// <param name="index">The index of the first character.</param>
    /// <returns><see langword="true"/> if they would start a number; otherwise, <see langword="false"/>.</returns>
    private readonly bool WouldStartNumber(int index)
    {
        char first = this.At(index);
        if (first is '+' or '-')
        {
            char second = this.At(index + 1);
            return char.IsAsciiDigit(second) || (second == '.' && char.IsAsciiDigit(this.At(index + 2)));
        }

        return first == '.' ? char.IsAsciiDigit(this.At(index + 1)) : char.IsAsciiDigit(first);
    }

    /// <summary>
    /// Skips comments at the current position. An unclosed comment runs to the end of the input.
    /// </summary>
    private void SkipComments()
    {
        while (this.Peek(0) == '/' && this.Peek(1) == '*')
        {
            int end = this.source[(this.position + 2)..].IndexOf("*/");
            this.position = end < 0 ? this.source.Length : this.position + 2 + end + 2;
        }
    }

    /// <summary>
    /// Consumes a name (section 4.3.12) from the current position.
    /// </summary>
    /// <returns><see langword="true"/> if the name contains escapes; otherwise, <see langword="false"/>.</returns>
    private bool ConsumeName()
    {
        bool hasEscape = false;
        while (true)
        {
            if (IsNameCharacter(this.Peek(0)))
            {
                this.position++;
            }
            else if (this.IsValidEscape(this.position))
            {
                this.position++;
                this.ConsumeEscape();
                hasEscape = true;
            }
            else
            {
                return hasEscape;
            }
        }
    }

    /// <summary>
    /// Consumes the part of an escape after the backslash (section 4.3.7): up to six hex digits and one whitespace
    /// character, or one other character.
    /// </summary>
    private void ConsumeEscape()
    {
        if (this.position >= this.source.Length)
        {
            return;
        }

        if (!char.IsAsciiHexDigit(this.source[this.position]))
        {
            this.position++;
            return;
        }

        int digits = 0;
        while (digits < 6 && char.IsAsciiHexDigit(this.Peek(0)))
        {
            this.position++;
            digits++;
        }

        char next = this.Peek(0);
        if (IsWhitespace(next))
        {
            this.position += next == '\r' && this.Peek(1) == '\n' ? 2 : 1;
        }
    }

    /// <summary>
    /// Consumes a numeric token (section 4.3.3): a number, a percentage, or a dimension.
    /// </summary>
    /// <param name="start">The index of the first character.</param>
    /// <returns>The token.</returns>
    private SvgCssToken ConsumeNumeric(int start)
    {
        // The number grammar is shared with the SVG value parsers. CSS numbers need digits after a decimal point.
        this.position += SvgNumberParser.ScanNumber(this.source[start..], false);
        int numberEnd = this.position;
        if (this.WouldStartIdentifier(this.position))
        {
            bool hasEscape = this.ConsumeName();
            return new SvgCssToken(SvgCssTokenKind.Dimension, start, this.position, numberEnd, this.position, hasEscape, false);
        }

        if (this.Peek(0) == '%')
        {
            this.position++;
            return new SvgCssToken(SvgCssTokenKind.Percentage, start, this.position, start, numberEnd, false, false);
        }

        return new SvgCssToken(SvgCssTokenKind.Number, start, this.position, start, numberEnd, false, false);
    }

    /// <summary>
    /// Consumes an identifier, a function, or a URL (section 4.3.4).
    /// </summary>
    /// <param name="start">The index of the first character.</param>
    /// <returns>The token.</returns>
    private SvgCssToken ConsumeIdentLike(int start)
    {
        bool hasEscape = this.ConsumeName();
        int nameEnd = this.position;
        if (this.Peek(0) != '(')
        {
            return new SvgCssToken(SvgCssTokenKind.Ident, start, this.position, start, nameEnd, hasEscape, false);
        }

        this.position++;
        if (hasEscape || !this.source[start..nameEnd].Equals("url", StringComparison.OrdinalIgnoreCase))
        {
            return new SvgCssToken(SvgCssTokenKind.Function, start, this.position, start, nameEnd, hasEscape, false);
        }

        // A quoted URL is a function token whose argument is a string token.
        int next = this.position;
        while (IsWhitespace(this.At(next)))
        {
            next++;
        }

        if (this.At(next) is '"' or '\'')
        {
            return new SvgCssToken(SvgCssTokenKind.Function, start, this.position, start, nameEnd, false, false);
        }

        this.position = next;
        return this.ConsumeUrl(start);
    }

    /// <summary>
    /// Consumes an unquoted URL after <c>url(</c> and any whitespace (section 4.3.6).
    /// </summary>
    /// <param name="start">The index of the first character of the token.</param>
    /// <returns>The token.</returns>
    private SvgCssToken ConsumeUrl(int start)
    {
        int valueStart = this.position;
        bool hasEscape = false;
        while (true)
        {
            if (this.position >= this.source.Length)
            {
                return new SvgCssToken(SvgCssTokenKind.Url, start, this.position, valueStart, this.position, hasEscape, false);
            }

            char c = this.source[this.position];
            if (c == ')')
            {
                this.position++;
                return new SvgCssToken(SvgCssTokenKind.Url, start, this.position, valueStart, this.position - 1, hasEscape, false);
            }

            if (IsWhitespace(c))
            {
                int valueEnd = this.position;
                while (IsWhitespace(this.Peek(0)))
                {
                    this.position++;
                }

                if (this.position >= this.source.Length || this.source[this.position] == ')')
                {
                    if (this.position < this.source.Length)
                    {
                        this.position++;
                    }

                    return new SvgCssToken(SvgCssTokenKind.Url, start, this.position, valueStart, valueEnd, hasEscape, false);
                }

                return this.ConsumeBadUrl(start);
            }

            if (c is '"' or '\'' or '(' || IsNonPrintable(c))
            {
                return this.ConsumeBadUrl(start);
            }

            if (c == '\\')
            {
                if (!this.IsValidEscape(this.position))
                {
                    return this.ConsumeBadUrl(start);
                }

                this.position++;
                this.ConsumeEscape();
                hasEscape = true;
                continue;
            }

            this.position++;
        }
    }

    /// <summary>
    /// Consumes the rest of a bad URL (section 4.3.14), up to and including <c>)</c>, so the parser stays in sync.
    /// </summary>
    /// <param name="start">The index of the first character of the token.</param>
    /// <returns>The bad URL token.</returns>
    private SvgCssToken ConsumeBadUrl(int start)
    {
        while (this.position < this.source.Length)
        {
            if (this.source[this.position] == ')')
            {
                this.position++;
                break;
            }

            if (this.IsValidEscape(this.position))
            {
                this.position++;
                this.ConsumeEscape();
            }
            else
            {
                this.position++;
            }
        }

        return this.Make(SvgCssTokenKind.BadUrl, start);
    }

    /// <summary>
    /// Consumes a string token (section 4.3.5). A newline ends the string as a bad string and is not consumed.
    /// </summary>
    /// <param name="start">The index of the opening quote.</param>
    /// <param name="quote">The quote character.</param>
    /// <returns>The token.</returns>
    private SvgCssToken ConsumeString(int start, char quote)
    {
        this.position++;
        int valueStart = this.position;
        bool hasEscape = false;
        while (true)
        {
            if (this.position >= this.source.Length)
            {
                return new SvgCssToken(SvgCssTokenKind.String, start, this.position, valueStart, this.position, hasEscape, false);
            }

            char c = this.source[this.position];
            if (c == quote)
            {
                this.position++;
                return new SvgCssToken(SvgCssTokenKind.String, start, this.position, valueStart, this.position - 1, hasEscape, false);
            }

            if (IsNewline(c))
            {
                return this.Make(SvgCssTokenKind.BadString, start);
            }

            if (c == '\\')
            {
                hasEscape = true;
                this.position++;
                char next = this.Peek(0);
                if (IsNewline(next))
                {
                    this.position += next == '\r' && this.Peek(1) == '\n' ? 2 : 1;
                }
                else
                {
                    this.ConsumeEscape();
                }

                continue;
            }

            this.position++;
        }
    }

    /// <summary>
    /// Returns whether a character is a non-printable code point (section 4.2). NUL is not one, because preprocessing
    /// replaces it with U+FFFD.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> if the character is non-printable; otherwise, <see langword="false"/>.</returns>
    private static bool IsNonPrintable(char c) => c is (>= '\u0001' and <= '\b') or '\v' or (>= '\u000E' and <= '\u001F') or '\u007F';

    /// <summary>
    /// Creates a token from <paramref name="start"/> to the current position whose value is the whole token.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The index of the first character.</param>
    /// <returns>The token.</returns>
    private readonly SvgCssToken Make(SvgCssTokenKind kind, int start)
        => new(kind, start, this.position, start, this.position, false, false);

    /// <summary>
    /// Consumes one character and creates a token for it.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The index of the character.</param>
    /// <returns>The token.</returns>
    private SvgCssToken MakeSingle(SvgCssTokenKind kind, int start)
    {
        this.position = start + 1;
        return this.Make(kind, start);
    }
}
