// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses the math functions of CSS Values and Units Level 4, section 10: <c>calc()</c>, <c>min()</c>, <c>max()</c>,
/// and <c>clamp()</c>, with the operators <c>+</c>, <c>-</c>, <c>*</c>, and <c>/</c>, parentheses, and the constants
/// <c>pi</c> and <c>e</c>.
/// </summary>
/// <remarks>
/// <para>
/// Types follow CSS: a sum needs operands of one type (numbers, lengths and percentages, or angles), a product needs a
/// number on one side, and a division needs a number on the right. <c>+</c> and <c>-</c> need white space on both
/// sides; <c>calc(10px+5px)</c> is invalid, as in Chromium.
/// </para>
/// <para>
/// Parsing folds the expression: one whose values all have one unit, such as <c>calc(2em * 3)</c> or
/// <c>min(10px, 20px)</c>, becomes a plain number, percentage, length, or angle, which costs nothing later. An expression
/// that mixes units, such as <c>calc(50% - 10px)</c>, stays an <see cref="SvgCalcExpression"/>, because the size that
/// percentages refer to and the font size are known only when the element resolves.
/// </para>
/// <para>
/// Limits: nesting of functions and parentheses deeper than <see cref="MaxDepth"/>, or more than <see cref="MaxTerms"/>
/// values and operators in one expression, make the expression invalid. Both bound the work and the stack of a hostile
/// value; real expressions are far smaller.
/// </para>
/// </remarks>
internal static class SvgCalc
{
    /// <summary>
    /// The deepest nesting of math functions and parentheses in one expression.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// The largest number of values and operators in one expression.
    /// </summary>
    public const int MaxTerms = 256;

    /// <summary>
    /// The slot of angles in degrees in a folded value, after the slots of the <see cref="SvgLengthUnit"/> values.
    /// </summary>
    public const int AngleSlot = (int)SvgLengthUnit.Calc;

    /// <summary>
    /// The number of slots of a folded value.
    /// </summary>
    private const int SlotCount = AngleSlot + 1;

    /// <summary>
    /// The slot of a term whose values use more than one slot.
    /// </summary>
    private const int MixedSlot = -2;

    /// <summary>
    /// The CSS types of a term, as a set: a sum of lengths and percentages has both bits.
    /// </summary>
    [Flags]
    private enum Types : byte
    {
        /// <summary>
        /// No type.
        /// </summary>
        None = 0,

        /// <summary>
        /// A number.
        /// </summary>
        Number = 1,

        /// <summary>
        /// A percentage.
        /// </summary>
        Percent = 2,

        /// <summary>
        /// A length.
        /// </summary>
        Length = 4,

        /// <summary>
        /// An angle.
        /// </summary>
        Angle = 8
    }

    /// <summary>
    /// Returns whether a value starts with a math function name and its parenthesis.
    /// </summary>
    /// <param name="source">The value, without leading white space.</param>
    /// <returns><see langword="true"/> for <c>calc(</c>, <c>min(</c>, <c>max(</c>, or <c>clamp(</c>, ignoring ASCII case.</returns>
    public static bool IsMathFunction(ReadOnlySpan<char> source)
    {
        int open = source.IndexOf('(');
        return open is 3 or 4 or 5 && IsFunctionName(source[..open]);
    }

    /// <summary>
    /// Reads the math function at the start of a value.
    /// </summary>
    /// <param name="source">The characters, starting with the function name.</param>
    /// <param name="variables">The names that the expression may use besides the constants.</param>
    /// <param name="result">When this method returns <see langword="true"/>, contains the value.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read, up to the closing parenthesis.</param>
    /// <returns><see langword="true"/> if a valid math function was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryRead(ReadOnlySpan<char> source, in SvgCalcVariables variables, out SvgCalcResult result, out int length)
    {
        result = default;
        length = 0;
        if (!IsMathFunction(source))
        {
            return false;
        }

        Parser parser = new(source, variables);
        if (!parser.TryParseValue(out Term? term))
        {
            return false;
        }

        length = parser.Position;
        result = ToResult(term!, parser.Operations);
        return true;
    }

    /// <summary>
    /// Parses a value that is exactly one math function of a length or percentage type, with optional surrounding white
    /// space. A number is accepted as a length in user units, as SVG accepts unitless lengths.
    /// </summary>
    /// <remarks>
    /// A result that mixes units goes to <see cref="SvgCalcTable.Current"/>; without a current table it is invalid.
    /// </remarks>
    /// <param name="source">The characters to parse.</param>
    /// <param name="nonNegative">Whether the length must not be negative: a negative result becomes zero, as CSS clamps math functions to the range of the property.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <returns><see langword="true"/> if the value is a valid math function of a length type; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseLength(ReadOnlySpan<char> source, bool nonNegative, out SvgLength value)
    {
        source = SvgNumberParser.Trim(source);
        if (!TryRead(source, default, out SvgCalcResult result, out int length) || length != source.Length)
        {
            value = default;
            return false;
        }

        return TryGetLength(result, nonNegative, out value);
    }

    /// <summary>
    /// Reads a math function of a length or percentage type at the start of a value. See
    /// <see cref="TryParseLength(ReadOnlySpan{char}, bool, out SvgLength)"/>.
    /// </summary>
    /// <param name="source">The characters, starting with the function name.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a valid math function of a length type was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryReadLength(ReadOnlySpan<char> source, out SvgLength value, out int length)
    {
        if (TryRead(source, default, out SvgCalcResult result, out length) && TryGetLength(result, false, out value))
        {
            return true;
        }

        value = default;
        length = 0;
        return false;
    }

    /// <summary>
    /// Parses a value that is exactly one math function of the number type.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the number.</param>
    /// <returns><see langword="true"/> if the value is a valid math function of the number type; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseNumber(ReadOnlySpan<char> source, out float value)
    {
        source = SvgNumberParser.Trim(source);
        if (TryRead(source, default, out SvgCalcResult result, out int length)
            && length == source.Length
            && result.Kind == SvgCalcKind.Number
            && TryNarrow(result.Value, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Parses a value that is exactly one math function of the number or the percentage type, as a fraction: 50% and 0.5
    /// both give 0.5. The fraction is not clamped.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the fraction.</param>
    /// <returns><see langword="true"/> if the value is a valid math function of one of the two types; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseFraction(ReadOnlySpan<char> source, out float value)
    {
        source = SvgNumberParser.Trim(source);
        if (TryRead(source, default, out SvgCalcResult result, out int length) && length == source.Length)
        {
            if (result.Kind == SvgCalcKind.Number && TryNarrow(result.Value, out value))
            {
                return true;
            }

            if (result.Kind == SvgCalcKind.Percent && TryNarrow(result.Value / 100D, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a math function of the angle type at the start of a value.
    /// </summary>
    /// <param name="source">The characters, starting with the function name.</param>
    /// <param name="degrees">When this method returns <see langword="true"/>, contains the angle in degrees.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a valid math function of the angle type was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryReadAngle(ReadOnlySpan<char> source, out float degrees, out int length)
    {
        if (TryRead(source, default, out SvgCalcResult result, out length)
            && result.Kind == SvgCalcKind.Angle
            && TryNarrow(result.Value, out degrees))
        {
            return true;
        }

        degrees = default;
        length = 0;
        return false;
    }

    /// <summary>
    /// Reads a math function of the number type at the start of a value.
    /// </summary>
    /// <param name="source">The characters, starting with the function name.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the number.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a valid math function of the number type was read; otherwise, <see langword="false"/>.</returns>
    public static bool TryReadNumber(ReadOnlySpan<char> source, out float value, out int length)
    {
        if (TryRead(source, default, out SvgCalcResult result, out length)
            && result.Kind == SvgCalcKind.Number
            && TryNarrow(result.Value, out value))
        {
            return true;
        }

        value = default;
        length = 0;
        return false;
    }

    /// <summary>
    /// Converts a parse result of a length or percentage type to a length.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="nonNegative">Whether a negative length becomes zero.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the length.</param>
    /// <returns><see langword="true"/> if the result has a length type and, when it mixes units, the current table took it.</returns>
    private static bool TryGetLength(SvgCalcResult result, bool nonNegative, out SvgLength value)
    {
        value = default;
        switch (result.Kind)
        {
            case SvgCalcKind.Number:
            case SvgCalcKind.Percent:
            case SvgCalcKind.Length:
                if (!TryNarrow(nonNegative ? Math.Max(result.Value, 0D) : result.Value, out float number))
                {
                    return false;
                }

                SvgLengthUnit unit = result.Kind switch
                {
                    SvgCalcKind.Number => SvgLengthUnit.Number,
                    SvgCalcKind.Percent => SvgLengthUnit.Percent,
                    _ => result.Unit
                };

                value = new SvgLength(number, unit);
                return true;
            case SvgCalcKind.Mixed:
                SvgCalcExpression expression = result.Expression!;
                if (nonNegative)
                {
                    // The table holds immutable expressions, so the clamping variant is a copy.
                    expression = expression.WithClampNonNegative();
                }

                if (SvgCalcTable.Current is not SvgCalcTable table || !table.TryAdd(expression, out int index))
                {
                    return false;
                }

                value = new SvgLength(index, SvgLengthUnit.Calc);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Converts a folded value to a finite <see cref="float"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="result">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the value is finite as a <see cref="float"/>; otherwise, <see langword="false"/>.</returns>
    private static bool TryNarrow(double value, out float result)
    {
        result = (float)value;
        return float.IsFinite(result);
    }

    /// <summary>
    /// Builds the result of a parsed expression.
    /// </summary>
    /// <param name="term">The folded root.</param>
    /// <param name="operations">The program of the expression.</param>
    /// <returns>The result.</returns>
    private static SvgCalcResult ToResult(Term term, List<SvgCalcOperation> operations)
    {
        if (term.Opaque || term.Slot == MixedSlot)
        {
            // Only lengths and percentages can mix: numbers and angles each have one slot.
            SvgCalcExpression expression = new([.. operations], (term.Types & Types.Percent) != 0, false);
            return new SvgCalcResult(SvgCalcKind.Mixed, 0D, SvgLengthUnit.Unspecified, expression);
        }

        double value = term.Slots[term.Slot];
        return term.Types switch
        {
            Types.Number => new SvgCalcResult(SvgCalcKind.Number, value, SvgLengthUnit.Number, null),
            Types.Percent => new SvgCalcResult(SvgCalcKind.Percent, value, SvgLengthUnit.Percent, null),
            Types.Angle => new SvgCalcResult(SvgCalcKind.Angle, value, SvgLengthUnit.Unspecified, null),
            _ => new SvgCalcResult(SvgCalcKind.Length, value, (SvgLengthUnit)term.Slot, null),
        };
    }

    /// <summary>
    /// Returns whether a name is one of the math functions, ignoring ASCII case.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> for <c>calc</c>, <c>min</c>, <c>max</c>, or <c>clamp</c>.</returns>
    private static bool IsFunctionName(ReadOnlySpan<char> name)
        => name.Equals("calc", StringComparison.OrdinalIgnoreCase)
        || name.Equals("min", StringComparison.OrdinalIgnoreCase)
        || name.Equals("max", StringComparison.OrdinalIgnoreCase)
        || name.Equals("clamp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether two terms may be added: both numbers, both angles, or both lengths or percentages.
    /// </summary>
    /// <param name="a">The first term.</param>
    /// <param name="b">The second term.</param>
    /// <returns><see langword="true"/> if the types are compatible; otherwise, <see langword="false"/>.</returns>
    private static bool AreCompatible(Term a, Term b)
    {
        const Types lengthPercentage = Types.Length | Types.Percent;
        return (a.Types == Types.Number && b.Types == Types.Number)
            || (a.Types == Types.Angle && b.Types == Types.Angle)
            || ((a.Types & ~lengthPercentage) == 0 && (b.Types & ~lengthPercentage) == 0);
    }

    /// <summary>
    /// A recursive descent parser over the characters of one math function. It folds each subexpression into a
    /// <see cref="Term"/> and, at the same time, writes the postfix program that an <see cref="SvgCalcExpression"/> needs.
    /// </summary>
    private ref struct Parser
    {
        private readonly ReadOnlySpan<char> text;
        private readonly SvgCalcVariables variables;
        private int depth;

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> struct.
        /// </summary>
        /// <param name="text">The characters, starting with the function name.</param>
        /// <param name="variables">The names that the expression may use.</param>
        public Parser(ReadOnlySpan<char> text, in SvgCalcVariables variables)
        {
            this.text = text;
            this.variables = variables;
            this.Position = 0;
            this.depth = 0;
            this.Operations = [];
        }

        /// <summary>
        /// Gets the position after the characters read so far.
        /// </summary>
        public int Position { get; private set; }

        /// <summary>
        /// Gets the program written so far.
        /// </summary>
        public List<SvgCalcOperation> Operations { get; }

        /// <summary>
        /// Parses one value: a number with an optional unit, a parenthesized sum, a math function, or a constant or
        /// variable name.
        /// </summary>
        /// <param name="term">When this method returns <see langword="true"/>, contains the folded value.</param>
        /// <returns><see langword="true"/> if a valid value was read; otherwise, <see langword="false"/>.</returns>
        public bool TryParseValue(out Term? term)
        {
            term = null;
            if (this.Position >= this.text.Length)
            {
                return false;
            }

            char c = this.text[this.Position];
            if (c == '(')
            {
                this.Position++;
                return this.TryEnter() && this.TryParseGroup(out term);
            }

            if (char.IsAsciiLetter(c))
            {
                int start = this.Position;
                while (this.Position < this.text.Length && (char.IsAsciiLetterOrDigit(this.text[this.Position]) || this.text[this.Position] == '-'))
                {
                    this.Position++;
                }

                ReadOnlySpan<char> name = this.text[start..this.Position];
                if (this.Position < this.text.Length && this.text[this.Position] == '(')
                {
                    this.Position++;
                    return IsFunctionName(name) && this.TryEnter() && this.TryParseFunction(name, out term);
                }

                // CSS Values 4 constants, and the names of the caller.
                double constant;
                if (name.Equals("pi", StringComparison.OrdinalIgnoreCase))
                {
                    constant = Math.PI;
                }
                else if (name.Equals("e", StringComparison.OrdinalIgnoreCase))
                {
                    constant = Math.E;
                }
                else if (!this.variables.TryGetValue(name, out constant))
                {
                    return false;
                }

                return this.TryAddValue(constant, (int)SvgLengthUnit.Number, out term);
            }

            if (!SvgNumberParser.TryReadNumber(this.text[this.Position..], false, out float number, out int length))
            {
                return false;
            }

            this.Position += length;
            int slot;
            double value = number;
            if (this.Position < this.text.Length && this.text[this.Position] == '%')
            {
                this.Position++;
                slot = (int)SvgLengthUnit.Percent;
            }
            else
            {
                int unitStart = this.Position;
                while (this.Position < this.text.Length && char.IsAsciiLetter(this.text[this.Position]))
                {
                    this.Position++;
                }

                ReadOnlySpan<char> unit = this.text[unitStart..this.Position];
                if (!TryGetSlot(unit, ref value, out slot))
                {
                    return false;
                }
            }

            return this.TryAddValue(value, slot, out term);
        }

        /// <summary>
        /// Maps a unit to its slot, converting an angle to degrees.
        /// </summary>
        /// <param name="unit">The unit characters; empty for a number.</param>
        /// <param name="value">The number, which an angle unit converts to degrees.</param>
        /// <param name="slot">When this method returns <see langword="true"/>, contains the slot.</param>
        /// <returns><see langword="true"/> if the unit is a known length or angle unit; otherwise, <see langword="false"/>.</returns>
        private static bool TryGetSlot(ReadOnlySpan<char> unit, ref double value, out int slot)
        {
            // CSS Values 4: 1turn = 360deg = 400grad = 2π rad.
            slot = AngleSlot;
            if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (unit.Equals("grad", StringComparison.OrdinalIgnoreCase))
            {
                value *= 0.9D;
                return true;
            }

            if (unit.Equals("rad", StringComparison.OrdinalIgnoreCase))
            {
                value *= 180D / Math.PI;
                return true;
            }

            if (unit.Equals("turn", StringComparison.OrdinalIgnoreCase))
            {
                value *= 360D;
                return true;
            }

            if (SvgLength.TryGetUnit(unit, out SvgLengthUnit lengthUnit) && lengthUnit != SvgLengthUnit.Percent)
            {
                slot = (int)lengthUnit;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Counts one more level of nesting.
        /// </summary>
        /// <returns><see langword="true"/> while the nesting is within <see cref="MaxDepth"/>.</returns>
        private bool TryEnter() => ++this.depth <= MaxDepth;

        /// <summary>
        /// Parses the arguments of a math function after its parenthesis, through the closing parenthesis.
        /// </summary>
        /// <param name="name">The function name.</param>
        /// <param name="term">When this method returns <see langword="true"/>, contains the folded value.</param>
        /// <returns><see langword="true"/> if the arguments are valid; otherwise, <see langword="false"/>.</returns>
        private bool TryParseFunction(ReadOnlySpan<char> name, out Term? term)
        {
            term = null;
            if (name.Equals("calc", StringComparison.OrdinalIgnoreCase))
            {
                return this.TryParseGroup(out term);
            }

            bool isClamp = name.Equals("clamp", StringComparison.OrdinalIgnoreCase);
            List<Term> arguments = [];
            while (true)
            {
                this.SkipWhitespace();
                if (!this.TryParseSum(out Term? argument))
                {
                    return false;
                }

                arguments.Add(argument!);
                this.SkipWhitespace();

                // CSS Syntax closes a function that the value leaves open at its end, as Chromium does.
                char c = this.Position < this.text.Length ? this.text[this.Position++] : ')';
                if (c == ')')
                {
                    break;
                }

                if (c != ',')
                {
                    return false;
                }
            }

            if (isClamp && arguments.Count != 3)
            {
                return false;
            }

            Term first = arguments[0];
            for (int i = 1; i < arguments.Count; i++)
            {
                if (!AreCompatible(first, arguments[i]))
                {
                    return false;
                }
            }

            // The result folds only when every argument is one plain value of the same unit.
            Term result = new() { Slot = first.Slot };
            bool foldable = true;
            foreach (Term argument in arguments)
            {
                result.Types |= argument.Types;
                foldable &= !argument.Opaque && argument.Slot != MixedSlot && argument.Slot == first.Slot;
            }

            SvgCalcOperationCode code;
            if (isClamp)
            {
                code = SvgCalcOperationCode.Clamp;
            }
            else
            {
                code = name.Equals("min", StringComparison.OrdinalIgnoreCase) ? SvgCalcOperationCode.Min : SvgCalcOperationCode.Max;
            }

            if (foldable)
            {
                int slot = first.Slot;
                double value = first.Slots[slot];
                if (isClamp)
                {
                    value = Math.Max(value, Math.Min(arguments[1].Slots[slot], arguments[2].Slots[slot]));
                }
                else
                {
                    for (int i = 1; i < arguments.Count; i++)
                    {
                        double next = arguments[i].Slots[slot];
                        value = code == SvgCalcOperationCode.Min ? Math.Min(value, next) : Math.Max(value, next);
                    }
                }

                result.Slots[slot] = value;
            }
            else
            {
                result.Opaque = true;
                result.Slot = MixedSlot;
            }

            this.depth--;
            term = result;
            return this.TryAddOperation(code, arguments.Count);
        }

        /// <summary>
        /// Parses a sum after an opening parenthesis, through the closing parenthesis.
        /// </summary>
        /// <param name="term">When this method returns <see langword="true"/>, contains the folded value.</param>
        /// <returns><see langword="true"/> if the group is valid; otherwise, <see langword="false"/>.</returns>
        private bool TryParseGroup(out Term? term)
        {
            this.SkipWhitespace();
            if (!this.TryParseSum(out term))
            {
                return false;
            }

            this.SkipWhitespace();

            // CSS Syntax closes a parenthesis that the value leaves open at its end, as Chromium does.
            if (this.Position < this.text.Length)
            {
                if (this.text[this.Position] != ')')
                {
                    return false;
                }

                this.Position++;
            }

            this.depth--;
            return true;
        }

        /// <summary>
        /// Parses a sum of products. <c>+</c> and <c>-</c> need white space on both sides.
        /// </summary>
        /// <param name="term">When this method returns <see langword="true"/>, contains the folded value.</param>
        /// <returns><see langword="true"/> if the sum is valid; otherwise, <see langword="false"/>.</returns>
        private bool TryParseSum(out Term? term)
        {
            if (!this.TryParseProduct(out term))
            {
                return false;
            }

            while (true)
            {
                int save = this.Position;
                bool spaceBefore = this.SkipWhitespace();
                if (!spaceBefore
                    || this.Position + 1 >= this.text.Length
                    || this.text[this.Position] is not ('+' or '-')
                    || !SvgNumberParser.IsWhitespace(this.text[this.Position + 1]))
                {
                    this.Position = save;
                    return true;
                }

                bool subtract = this.text[this.Position] == '-';
                this.Position++;
                this.SkipWhitespace();
                if (!this.TryParseProduct(out Term? right) || !AreCompatible(term!, right!))
                {
                    return false;
                }

                Term left = term!;
                left.Types |= right!.Types;
                left.Opaque |= right.Opaque;
                left.Slot = left.Slot == right.Slot ? left.Slot : MixedSlot;
                double sign = subtract ? -1D : 1D;
                for (int i = 0; i < SlotCount; i++)
                {
                    left.Slots[i] += sign * right.Slots[i];
                }

                if (!this.TryAddOperation(subtract ? SvgCalcOperationCode.Subtract : SvgCalcOperationCode.Add, 0))
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Parses a product of values. One side of <c>*</c> must be a number, and the right side of <c>/</c> must be a
        /// number that is not zero.
        /// </summary>
        /// <param name="term">When this method returns <see langword="true"/>, contains the folded value.</param>
        /// <returns><see langword="true"/> if the product is valid; otherwise, <see langword="false"/>.</returns>
        private bool TryParseProduct(out Term? term)
        {
            if (!this.TryParseValue(out term))
            {
                return false;
            }

            while (true)
            {
                int save = this.Position;
                this.SkipWhitespace();
                if (this.Position >= this.text.Length || this.text[this.Position] is not ('*' or '/'))
                {
                    this.Position = save;
                    return true;
                }

                bool divide = this.text[this.Position] == '/';
                this.Position++;
                this.SkipWhitespace();
                if (!this.TryParseValue(out Term? right))
                {
                    return false;
                }

                Term left = term!;
                Term factor;
                if (right!.Types == Types.Number)
                {
                    factor = right;
                }
                else if (!divide && left.Types == Types.Number)
                {
                    // The number is on the left: the result has the type and the slot of the right side.
                    factor = left;
                    term = right;
                    left = right;
                }
                else
                {
                    return false;
                }

                left.Opaque |= factor.Opaque;
                if (!factor.Opaque)
                {
                    double number = factor.Number();
                    if (divide && number == 0D)
                    {
                        return false;
                    }

                    left.Scale(divide ? 1D / number : number);
                }

                if (!this.TryAddOperation(divide ? SvgCalcOperationCode.Divide : SvgCalcOperationCode.Multiply, 0))
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Adds a value to the program and creates its term.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="slot">The unit slot.</param>
        /// <param name="term">When this method returns <see langword="true"/>, contains the term.</param>
        /// <returns><see langword="true"/> while the expression is within <see cref="MaxTerms"/>.</returns>
        private bool TryAddValue(double value, int slot, out Term? term)
        {
            term = Term.Of(value, slot);
            return this.TryAddOperation(SvgCalcOperationCode.Value, slot, (float)value);
        }

        /// <summary>
        /// Adds an operation to the program.
        /// </summary>
        /// <param name="code">The operation.</param>
        /// <param name="slot">The unit slot or the argument count.</param>
        /// <param name="value">The value of a <see cref="SvgCalcOperationCode.Value"/>.</param>
        /// <returns><see langword="true"/> while the expression is within <see cref="MaxTerms"/>.</returns>
        private bool TryAddOperation(SvgCalcOperationCode code, int slot, float value = 0F)
        {
            if (this.Operations.Count >= MaxTerms)
            {
                return false;
            }

            this.Operations.Add(new SvgCalcOperation(code, slot, value));
            return true;
        }

        /// <summary>
        /// Skips white space.
        /// </summary>
        /// <returns><see langword="true"/> if any white space was skipped; otherwise, <see langword="false"/>.</returns>
        private bool SkipWhitespace()
        {
            int start = this.Position;
            while (this.Position < this.text.Length && SvgNumberParser.IsWhitespace(this.text[this.Position]))
            {
                this.Position++;
            }

            return this.Position > start;
        }
    }

    /// <summary>
    /// A folded subexpression: the coefficient of each unit slot, as a sum of terms, with the CSS types it has. A term
    /// that min, max, or clamp of different units produced cannot fold, and is opaque.
    /// </summary>
    private sealed class Term
    {
        // The coefficients live inline in the term, so a parsed value allocates one object; a separate array per term was
        // the largest allocation of a document whose custom properties hold math functions.
        private SlotBuffer slots;

        /// <summary>
        /// Gets the coefficient of each slot. Meaningless when <see cref="Opaque"/>.
        /// </summary>
        public Span<double> Slots => this.slots;

        /// <summary>
        /// Gets or sets the CSS types of the term.
        /// </summary>
        public Types Types { get; set; }

        /// <summary>
        /// Gets or sets the one slot that the values of the term use, or <see cref="MixedSlot"/>.
        /// </summary>
        public int Slot { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the term cannot fold.
        /// </summary>
        public bool Opaque { get; set; }

        /// <summary>
        /// Creates a term of one value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="slot">The unit slot.</param>
        /// <returns>The term.</returns>
        public static Term Of(double value, int slot)
        {
            Term term = new()
            {
                Slot = slot,
                Types = slot switch
                {
                    (int)SvgLengthUnit.Number => Types.Number,
                    (int)SvgLengthUnit.Percent => Types.Percent,
                    AngleSlot => Types.Angle,
                    _ => Types.Length,
                }
            };

            term.Slots[slot] = value;
            return term;
        }

        /// <summary>
        /// Returns the single number of a number term.
        /// </summary>
        /// <returns>The number.</returns>
        public double Number() => this.Slots[(int)SvgLengthUnit.Number];

        /// <summary>
        /// Multiplies every coefficient by a factor.
        /// </summary>
        /// <param name="factor">The factor.</param>
        public void Scale(double factor)
        {
            Span<double> slots = this.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] *= factor;
            }
        }

        /// <summary>
        /// The inline storage of the coefficients, one per slot.
        /// </summary>
        [InlineArray(SlotCount)]
        private struct SlotBuffer
        {
            private double element0;
        }
    }
}
