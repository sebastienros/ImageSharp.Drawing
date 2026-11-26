// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the CSS <c>transform</c> property (CSS Transforms Level 1 and 2): <c>none</c> or a list of transform
/// functions. A list whose arguments are all known when it is parsed folds into one matrix; a list with a percentage, a
/// unit other than <c>px</c>, or a math function that mixes units keeps its functions until the reference box and the
/// font size are known.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is the CSS one, not the one of the SVG attribute: function names are ASCII case-insensitive, arguments are
/// separated by commas, angles need a unit unless they are zero, and lengths need a unit unless they are zero. Each
/// argument can be a math function (<see cref="SvgCalc"/>). A percentage in a translation refers to the size of the
/// reference box that <c>transform-box</c> selects. A scale factor can be a percentage.
/// </para>
/// <para>
/// The three-dimensional functions (<c>matrix3d</c>, <c>translate3d</c>, <c>translateZ</c>, <c>scale3d</c>,
/// <c>scaleZ</c>, <c>rotate3d</c>, <c>rotateX</c>, <c>rotateY</c>, <c>rotateZ</c>, <c>perspective</c>) compose as 4 x 4
/// matrices, as CSS Transforms Level 2 defines, and the result is flattened to the plane by dropping its z row and
/// column, which is how Chromium draws them on SVG elements: <c>rotateX(60deg)</c> scales y by one half. A perspective
/// that combines with a rotation about the x or y axis makes a projective map, which a 2D affine matrix cannot hold; the
/// flattening then keeps its affine part only.
/// </para>
/// <para>
/// Instances never change after they are created.
/// </para>
/// </remarks>
internal sealed class SvgCssTransform
{
    /// <summary>
    /// The value <c>none</c>.
    /// </summary>
    public static readonly SvgCssTransform None = new(Matrix3x2.Identity, null);

    // The functions in written order, or null when the matrix is known.
    private readonly Function[]? functions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssTransform"/> class.
    /// </summary>
    /// <param name="matrix">The folded matrix, when the functions are null.</param>
    /// <param name="functions">The functions, or <see langword="null"/>.</param>
    private SvgCssTransform(Matrix3x2 matrix, Function[]? functions)
    {
        this.Matrix = matrix;
        this.functions = functions;
    }

    /// <summary>
    /// The transform functions.
    /// </summary>
    private enum Kind : byte
    {
        /// <summary>
        /// A name that is not a transform function.
        /// </summary>
        Unknown,

        /// <summary>
        /// <c>matrix()</c>: six numbers.
        /// </summary>
        Matrix,

        /// <summary>
        /// <c>matrix3d()</c>: sixteen numbers, in column-major order.
        /// </summary>
        Matrix3d,

        /// <summary>
        /// <c>translate()</c>: one or two lengths or percentages.
        /// </summary>
        Translate,

        /// <summary>
        /// <c>translateX()</c>.
        /// </summary>
        TranslateX,

        /// <summary>
        /// <c>translateY()</c>.
        /// </summary>
        TranslateY,

        /// <summary>
        /// <c>translateZ()</c>: a length.
        /// </summary>
        TranslateZ,

        /// <summary>
        /// <c>translate3d()</c>: two lengths or percentages and a length.
        /// </summary>
        Translate3d,

        /// <summary>
        /// <c>scale()</c>: one or two numbers or percentages.
        /// </summary>
        Scale,

        /// <summary>
        /// <c>scaleX()</c>.
        /// </summary>
        ScaleX,

        /// <summary>
        /// <c>scaleY()</c>.
        /// </summary>
        ScaleY,

        /// <summary>
        /// <c>scaleZ()</c>.
        /// </summary>
        ScaleZ,

        /// <summary>
        /// <c>scale3d()</c>: three numbers or percentages.
        /// </summary>
        Scale3d,

        /// <summary>
        /// <c>rotate()</c> and <c>rotateZ()</c>: an angle.
        /// </summary>
        Rotate,

        /// <summary>
        /// <c>rotateX()</c>.
        /// </summary>
        RotateX,

        /// <summary>
        /// <c>rotateY()</c>.
        /// </summary>
        RotateY,

        /// <summary>
        /// <c>rotate3d()</c>: three numbers for the axis and an angle.
        /// </summary>
        Rotate3d,

        /// <summary>
        /// <c>skew()</c>: one or two angles.
        /// </summary>
        Skew,

        /// <summary>
        /// <c>skewX()</c>.
        /// </summary>
        SkewX,

        /// <summary>
        /// <c>skewY()</c>.
        /// </summary>
        SkewY,

        /// <summary>
        /// <c>perspective()</c>: a length, or <c>none</c>.
        /// </summary>
        Perspective
    }

    /// <summary>
    /// The form that one argument of a function takes.
    /// </summary>
    private enum ArgumentForm : byte
    {
        /// <summary>
        /// A number.
        /// </summary>
        Number,

        /// <summary>
        /// A number or a percentage, as a factor.
        /// </summary>
        NumberOrPercentage,

        /// <summary>
        /// A length or a percentage.
        /// </summary>
        LengthPercentage,

        /// <summary>
        /// A length.
        /// </summary>
        Length,

        /// <summary>
        /// An angle, in degrees.
        /// </summary>
        Angle
    }

    /// <summary>
    /// Gets the folded matrix. Meaningful only when <see cref="IsStatic"/> is true.
    /// </summary>
    public Matrix3x2 Matrix { get; }

    /// <summary>
    /// Gets a value indicating whether the value folded into <see cref="Matrix"/> when it was parsed.
    /// </summary>
    public bool IsStatic => this.functions is null;

    /// <summary>
    /// Gets a value indicating whether the value is the identity: <c>none</c>, or functions that cancel out.
    /// </summary>
    public bool IsIdentity => this.IsStatic && this.Matrix.IsIdentity;

    /// <summary>
    /// Creates a value of one known matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>The value.</returns>
    public static SvgCssTransform FromMatrix(Matrix3x2 matrix) => matrix.IsIdentity ? None : new SvgCssTransform(matrix, null);

    /// <summary>
    /// Parses a <c>transform</c> property value.
    /// </summary>
    /// <param name="source">The value.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out SvgCssTransform? value)
    {
        value = null;
        source = SvgNumberParser.Trim(source);
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            value = None;
            return true;
        }

        if (source.IsEmpty)
        {
            return false;
        }

        List<Function> functions = [];
        bool isStatic = true;
        Span<SvgLength> arguments = stackalloc SvgLength[16];
        while (!source.IsEmpty)
        {
            int nameLength = 0;
            while (nameLength < source.Length && char.IsAsciiLetterOrDigit(source[nameLength]))
            {
                nameLength++;
            }

            // A CSS function token has its parenthesis directly after the name.
            if (nameLength == 0 || nameLength == source.Length || source[nameLength] != '(')
            {
                return false;
            }

            Kind kind = GetKind(source[..nameLength]);
            if (kind == Kind.Unknown)
            {
                return false;
            }

            source = SvgNumberParser.SkipWhitespace(source[(nameLength + 1)..]);
            int count = 0;
            bool perspectiveNone = false;
            if (kind == Kind.Perspective && source.StartsWith("none", StringComparison.OrdinalIgnoreCase))
            {
                perspectiveNone = true;
                source = SvgNumberParser.SkipWhitespace(source[4..]);
            }

            while (!perspectiveNone && !source.IsEmpty && source[0] != ')')
            {
                if (count > 0)
                {
                    if (source[0] != ',')
                    {
                        return false;
                    }

                    source = SvgNumberParser.SkipWhitespace(source[1..]);
                }

                if (count == arguments.Length || !TryReadArgument(source, GetForm(kind, count), out arguments[count], out int length))
                {
                    return false;
                }

                isStatic &= IsKnownArgument(arguments[count]);
                count++;
                source = SvgNumberParser.SkipWhitespace(source[length..]);
            }

            if (source.IsEmpty || !HasValidCount(kind, count, perspectiveNone))
            {
                return false;
            }

            functions.Add(new Function(kind, perspectiveNone ? [] : arguments[..count].ToArray()));
            source = SvgNumberParser.SkipWhitespace(source[1..]);
        }

        if (!isStatic)
        {
            value = new SvgCssTransform(Matrix3x2.Identity, [.. functions]);
            return true;
        }

        Matrix3x2 matrix = Compose(functions, default, 0F, default);
        if (!IsFinite(matrix))
        {
            return false;
        }

        value = FromMatrix(matrix);
        return true;
    }

    /// <summary>
    /// Returns the matrix of the value for a reference box and a font size.
    /// </summary>
    /// <param name="referenceBox">The size of the reference box, which percentages of translations refer to.</param>
    /// <param name="fontSize">The computed font size of the element, for <c>em</c>.</param>
    /// <param name="units">The document values that the other units refer to.</param>
    /// <returns>The matrix; the identity when the result is not finite.</returns>
    public Matrix3x2 Resolve(SizeF referenceBox, float fontSize, SvgUnitBasis units)
    {
        if (this.functions is null)
        {
            return this.Matrix;
        }

        Matrix3x2 matrix = Compose(this.functions, referenceBox, fontSize, units);
        return IsFinite(matrix) ? matrix : Matrix3x2.Identity;
    }

    /// <summary>
    /// Composes functions into one flattened matrix.
    /// </summary>
    /// <remarks>
    /// The first function is the outermost, as in the SVG attribute, so with row vectors each function multiplies the
    /// result from the left. The 4 x 4 result is flattened by keeping the rows and columns of x, y, and the translation.
    /// </remarks>
    /// <param name="functions">The functions.</param>
    /// <param name="referenceBox">The size of the reference box.</param>
    /// <param name="fontSize">The font size.</param>
    /// <param name="units">The document values.</param>
    /// <returns>The matrix.</returns>
    private static Matrix3x2 Compose(IReadOnlyList<Function> functions, SizeF referenceBox, float fontSize, SvgUnitBasis units)
    {
        Matrix4x4 result = Matrix4x4.Identity;
        foreach (Function function in functions)
        {
            result = function.ToMatrix(referenceBox, fontSize, units) * result;
        }

        return new Matrix3x2(result.M11, result.M12, result.M21, result.M22, result.M41, result.M42);
    }

    /// <summary>
    /// Returns whether an argument is known when it is parsed: a number, an angle, a <c>px</c> length, or zero.
    /// </summary>
    /// <param name="value">The argument.</param>
    /// <returns><see langword="true"/> if the argument needs no reference; otherwise, <see langword="false"/>.</returns>
    private static bool IsKnownArgument(SvgLength value) => value.Unit is SvgLengthUnit.Number or SvgLengthUnit.Px;

    /// <summary>
    /// Reads one argument in the form that its function takes.
    /// </summary>
    /// <param name="source">The characters, at the argument.</param>
    /// <param name="form">The form.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the argument: a length, or a number of unit <see cref="SvgLengthUnit.Number"/> for numbers, factors, and angles in degrees.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if an argument was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryReadArgument(ReadOnlySpan<char> source, ArgumentForm form, out SvgLength value, out int length)
    {
        value = default;
        bool math = SvgCalc.IsMathFunction(source);
        float number;
        switch (form)
        {
            case ArgumentForm.Angle:
                if (!(math ? SvgCalc.TryReadAngle(source, out number, out length) : SvgNumberParser.TryReadAngle(source, false, out number, out length)))
                {
                    return false;
                }

                value = new SvgLength(number, SvgLengthUnit.Number);
                return true;
            case ArgumentForm.Number:
                if (!(math ? SvgCalc.TryReadNumber(source, out number, out length) : SvgNumberParser.TryReadNumber(source, false, out number, out length)))
                {
                    return false;
                }

                value = new SvgLength(number, SvgLengthUnit.Number);
                return true;
            case ArgumentForm.NumberOrPercentage:
                if (math)
                {
                    if (!SvgCalc.TryRead(source, default, out SvgCalcResult factor, out length)
                        || factor.Kind is not (SvgCalcKind.Number or SvgCalcKind.Percent))
                    {
                        return false;
                    }

                    number = (float)(factor.Kind == SvgCalcKind.Percent ? factor.Value / 100D : factor.Value);
                }
                else
                {
                    if (!SvgNumberParser.TryReadNumber(source, false, out number, out length))
                    {
                        return false;
                    }

                    if (length < source.Length && source[length] == '%')
                    {
                        number /= 100F;
                        length++;
                    }
                }

                value = new SvgLength(number, SvgLengthUnit.Number);
                return float.IsFinite(number);
            default:
                if (!(math ? SvgCalc.TryReadLength(source, out value, out length) : SvgLength.TryRead(source, out value, out length)))
                {
                    return false;
                }

                // CSS lengths need a unit unless they are zero, and the z axis takes no percentage.
                return (value.Unit != SvgLengthUnit.Number || value.Value == 0F)
                    && (form == ArgumentForm.LengthPercentage || value.Unit != SvgLengthUnit.Percent);
        }
    }

    /// <summary>
    /// Returns the form of an argument of a function.
    /// </summary>
    /// <param name="kind">The function.</param>
    /// <param name="index">The index of the argument.</param>
    /// <returns>The form.</returns>
    private static ArgumentForm GetForm(Kind kind, int index)
        => kind switch
        {
            Kind.Matrix or Kind.Matrix3d => ArgumentForm.Number,
            Kind.Translate or Kind.TranslateX or Kind.TranslateY => ArgumentForm.LengthPercentage,
            Kind.Translate3d => index < 2 ? ArgumentForm.LengthPercentage : ArgumentForm.Length,
            Kind.TranslateZ or Kind.Perspective => ArgumentForm.Length,
            Kind.Scale or Kind.ScaleX or Kind.ScaleY or Kind.ScaleZ or Kind.Scale3d => ArgumentForm.NumberOrPercentage,
            Kind.Rotate3d => index < 3 ? ArgumentForm.Number : ArgumentForm.Angle,
            _ => ArgumentForm.Angle,
        };

    /// <summary>
    /// Returns whether a function has a valid number of arguments.
    /// </summary>
    /// <param name="kind">The function.</param>
    /// <param name="count">The number of arguments.</param>
    /// <param name="perspectiveNone">Whether the argument of <c>perspective()</c> is <c>none</c>.</param>
    /// <returns><see langword="true"/> if the count is valid; otherwise, <see langword="false"/>.</returns>
    private static bool HasValidCount(Kind kind, int count, bool perspectiveNone)
        => kind switch
        {
            Kind.Matrix => count == 6,
            Kind.Matrix3d => count == 16,
            Kind.Translate or Kind.Scale or Kind.Skew => count is 1 or 2,
            Kind.Translate3d or Kind.Scale3d => count == 3,
            Kind.Rotate3d => count == 4,
            Kind.Perspective => perspectiveNone ? count == 0 : count == 1,
            _ => count == 1,
        };

    /// <summary>
    /// Maps a function name to its function, ignoring ASCII case.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>The function, or <see cref="Kind.Unknown"/>.</returns>
    private static Kind GetKind(ReadOnlySpan<char> name)
    {
        Span<char> lower = stackalloc char[16];
        if (name.Length > lower.Length)
        {
            return Kind.Unknown;
        }

        int length = name.ToLowerInvariant(lower);
        return lower[..length] switch
        {
            "matrix" => Kind.Matrix,
            "matrix3d" => Kind.Matrix3d,
            "translate" => Kind.Translate,
            "translatex" => Kind.TranslateX,
            "translatey" => Kind.TranslateY,
            "translatez" => Kind.TranslateZ,
            "translate3d" => Kind.Translate3d,
            "scale" => Kind.Scale,
            "scalex" => Kind.ScaleX,
            "scaley" => Kind.ScaleY,
            "scalez" => Kind.ScaleZ,
            "scale3d" => Kind.Scale3d,
            "rotate" or "rotatez" => Kind.Rotate,
            "rotatex" => Kind.RotateX,
            "rotatey" => Kind.RotateY,
            "rotate3d" => Kind.Rotate3d,
            "skew" => Kind.Skew,
            "skewx" => Kind.SkewX,
            "skewy" => Kind.SkewY,
            "perspective" => Kind.Perspective,
            _ => Kind.Unknown
        };
    }

    /// <summary>
    /// Returns whether every element of a matrix is finite.
    /// </summary>
    /// <param name="m">The matrix.</param>
    /// <returns><see langword="true"/> if the matrix is finite; otherwise, <see langword="false"/>.</returns>
    private static bool IsFinite(Matrix3x2 m)
        => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21)
        && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);

    /// <summary>
    /// One transform function with its arguments.
    /// </summary>
    private readonly struct Function
    {
        private readonly SvgLength[] arguments;

        /// <summary>
        /// Initializes a new instance of the <see cref="Function"/> struct.
        /// </summary>
        /// <param name="kind">The function.</param>
        /// <param name="arguments">The arguments; numbers, factors, and angles in degrees are of unit <see cref="SvgLengthUnit.Number"/>.</param>
        public Function(Kind kind, SvgLength[] arguments)
        {
            this.Kind = kind;
            this.arguments = arguments;
        }

        /// <summary>
        /// Gets the function.
        /// </summary>
        public Kind Kind { get; }

        /// <summary>
        /// Returns the matrix of the function, with row vectors as <see cref="Matrix4x4"/> uses them.
        /// </summary>
        /// <param name="referenceBox">The size of the reference box.</param>
        /// <param name="fontSize">The font size.</param>
        /// <param name="units">The document values.</param>
        /// <returns>The matrix.</returns>
        public Matrix4x4 ToMatrix(SizeF referenceBox, float fontSize, SvgUnitBasis units)
        {
            ReadOnlySpan<SvgLength> a = this.arguments;
            int count = a.Length;
            const float toRadians = MathF.PI / 180F;
            switch (this.Kind)
            {
                case Kind.Matrix:
                    // matrix(a, b, c, d, e, f) maps (x, y) to (a*x + c*y + e, b*x + d*y + f).
                {
                    Matrix4x4 matrix = Matrix4x4.Identity;
                    matrix.M11 = a[0].Value;
                    matrix.M12 = a[1].Value;
                    matrix.M21 = a[2].Value;
                    matrix.M22 = a[3].Value;
                    matrix.M41 = a[4].Value;
                    matrix.M42 = a[5].Value;
                    return matrix;
                }

                case Kind.Matrix3d:
                {
                    // The column-major order of CSS is the row order of the row-vector matrix: M11, M12, ..., M44.
                    return new Matrix4x4(
                        a[0].Value,
                        a[1].Value,
                        a[2].Value,
                        a[3].Value,
                        a[4].Value,
                        a[5].Value,
                        a[6].Value,
                        a[7].Value,
                        a[8].Value,
                        a[9].Value,
                        a[10].Value,
                        a[11].Value,
                        a[12].Value,
                        a[13].Value,
                        a[14].Value,
                        a[15].Value);
                }

                case Kind.Translate:
                    return Matrix4x4.CreateTranslation(X(a[0]), count == 2 ? Y(a[1]) : 0F, 0F);
                case Kind.TranslateX:
                    return Matrix4x4.CreateTranslation(X(a[0]), 0F, 0F);
                case Kind.TranslateY:
                    return Matrix4x4.CreateTranslation(0F, Y(a[0]), 0F);
                case Kind.TranslateZ:
                    return Matrix4x4.CreateTranslation(0F, 0F, Z(a[0]));
                case Kind.Translate3d:
                    return Matrix4x4.CreateTranslation(X(a[0]), Y(a[1]), Z(a[2]));
                case Kind.Scale:
                    return Matrix4x4.CreateScale(a[0].Value, count == 2 ? a[1].Value : a[0].Value, 1F);
                case Kind.ScaleX:
                    return Matrix4x4.CreateScale(a[0].Value, 1F, 1F);
                case Kind.ScaleY:
                    return Matrix4x4.CreateScale(1F, a[0].Value, 1F);
                case Kind.ScaleZ:
                    return Matrix4x4.CreateScale(1F, 1F, a[0].Value);
                case Kind.Scale3d:
                    return Matrix4x4.CreateScale(a[0].Value, a[1].Value, a[2].Value);
                case Kind.Rotate:
                    // A positive angle turns the x axis toward the y axis, which is clockwise on a y-down canvas.
                    return Matrix4x4.CreateRotationZ(a[0].Value * toRadians);
                case Kind.RotateX:
                    return Matrix4x4.CreateRotationX(a[0].Value * toRadians);
                case Kind.RotateY:
                    return Matrix4x4.CreateRotationY(a[0].Value * toRadians);
                case Kind.Rotate3d:
                {
                    // CSS Transforms Level 2 normalizes the axis; an axis of length zero is the identity.
                    Vector3 axis = new(a[0].Value, a[1].Value, a[2].Value);
                    float axisLength = axis.Length();
                    return axisLength > 0F && float.IsFinite(axisLength)
                        ? Matrix4x4.CreateFromAxisAngle(axis / axisLength, a[3].Value * toRadians)
                        : Matrix4x4.Identity;
                }

                case Kind.Skew:
                {
                    // skew(ax, ay) maps (x, y) to (x + tan(ax) * y, tan(ay) * x + y); ay defaults to 0.
                    Matrix4x4 skew = Matrix4x4.Identity;
                    skew.M21 = MathF.Tan(a[0].Value * toRadians);
                    skew.M12 = count == 2 ? MathF.Tan(a[1].Value * toRadians) : 0F;
                    return skew;
                }

                case Kind.SkewX:
                {
                    Matrix4x4 skew = Matrix4x4.Identity;
                    skew.M21 = MathF.Tan(a[0].Value * toRadians);
                    return skew;
                }

                case Kind.SkewY:
                {
                    Matrix4x4 skew = Matrix4x4.Identity;
                    skew.M12 = MathF.Tan(a[0].Value * toRadians);
                    return skew;
                }

                default:
                {
                    // perspective(d): the z row of the column-vector matrix gets -1/d, which is M34 with row vectors.
                    // CSS Transforms Level 2 clamps d to at least 1px; none is the identity.
                    Matrix4x4 perspective = Matrix4x4.Identity;
                    if (count == 1)
                    {
                        perspective.M34 = -1F / MathF.Max(Z(a[0]), 1F);
                    }

                    return perspective;
                }
            }

            float X(SvgLength value) => SvgUnits.ToUser(value, referenceBox.Width, fontSize, units, 0F);

            float Y(SvgLength value) => SvgUnits.ToUser(value, referenceBox.Height, fontSize, units, 0F);

            float Z(SvgLength value) => SvgUnits.ToUser(value, 0F, fontSize, units, 0F);
        }
    }
}
