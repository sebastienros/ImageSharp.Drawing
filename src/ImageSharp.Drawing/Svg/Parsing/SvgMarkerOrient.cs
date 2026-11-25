// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A specified <c>orient</c> value of a <c>marker</c>. <c>default(SvgMarkerOrient)</c> is unspecified, which behaves as
/// the angle 0.
/// </summary>
internal readonly struct SvgMarkerOrient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgMarkerOrient"/> struct.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="angle">The angle in degrees for <see cref="SvgMarkerOrientKind.Angle"/>; otherwise 0.</param>
    public SvgMarkerOrient(SvgMarkerOrientKind kind, float angle)
    {
        this.Kind = kind;
        this.Angle = angle;
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public SvgMarkerOrientKind Kind { get; }

    /// <summary>
    /// Gets the angle in degrees for <see cref="SvgMarkerOrientKind.Angle"/>. A positive angle turns the x axis toward
    /// the y axis.
    /// </summary>
    public float Angle { get; }

    /// <summary>
    /// Parses <c>auto</c>, <c>auto-start-reverse</c>, or an angle. The keywords are case-sensitive, as in the SVG
    /// grammar. An angle is a number with an optional unit, as <see cref="SvgNumberParser.TryParseAngle"/> reads it.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the value is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out SvgMarkerOrient value)
    {
        source = SvgNumberParser.Trim(source);
        if (source.SequenceEqual("auto"))
        {
            value = new SvgMarkerOrient(SvgMarkerOrientKind.Auto, 0F);
            return true;
        }

        if (source.SequenceEqual("auto-start-reverse"))
        {
            value = new SvgMarkerOrient(SvgMarkerOrientKind.AutoStartReverse, 0F);
            return true;
        }

        if (SvgNumberParser.TryParseAngle(source, out float degrees))
        {
            value = new SvgMarkerOrient(SvgMarkerOrientKind.Angle, degrees);
            return true;
        }

        value = default;
        return false;
    }
}
