// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The parsed CSS basic shape and geometry box of a <c>clip-path</c> value, as <see cref="SvgReference.Shape"/> carries
/// it. An instance never changes after the parser creates it.
/// </summary>
/// <remarks>
/// Lengths keep their specified units, as the other specified values do; percentages refer to the reference box, whose
/// size is known only when the clip is built. A computed style holds the same instance, because no part of a shape
/// computes against the element.
/// </remarks>
internal sealed class SvgClipShape
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgClipShape"/> class.
    /// </summary>
    /// <param name="kind">The shape kind.</param>
    /// <param name="box">The geometry box.</param>
    /// <param name="lengths">The lengths, laid out as <paramref name="kind"/> says.</param>
    public SvgClipShape(SvgBasicShapeKind kind, SvgGeometryBox box, SvgLength[] lengths)
    {
        this.Kind = kind;
        this.Box = box;
        this.Lengths = lengths;
    }

    /// <summary>
    /// Gets the shape kind.
    /// </summary>
    public SvgBasicShapeKind Kind { get; }

    /// <summary>
    /// Gets the geometry box that the shape refers to.
    /// </summary>
    public SvgGeometryBox Box { get; }

    /// <summary>
    /// Gets the lengths of the shape, laid out as <see cref="Kind"/> says.
    /// </summary>
    public SvgLength[] Lengths { get; }

    /// <summary>
    /// Gets the corner radii of <c>round</c> for <see cref="SvgBasicShapeKind.Inset"/>, <see cref="SvgBasicShapeKind.Xywh"/>,
    /// and <see cref="SvgBasicShapeKind.Rect"/>: eight lengths, the horizontal radii of the top left, top right, bottom
    /// right, and bottom left corners, then the vertical radii in the same order, after the CSS rule that fills omitted
    /// corners. <see langword="null"/> when the shape has no <c>round</c>, which means square corners.
    /// </summary>
    public SvgLength[]? Radii { get; init; }

    /// <summary>
    /// Gets the form of the radius of a circle, or of the horizontal radius of an ellipse.
    /// </summary>
    public SvgShapeRadiusKind RadiusXKind { get; init; }

    /// <summary>
    /// Gets the form of the vertical radius of an ellipse.
    /// </summary>
    public SvgShapeRadiusKind RadiusYKind { get; init; }

    /// <summary>
    /// Gets the center of a circle or an ellipse. The default is the center of the reference box.
    /// </summary>
    public SvgShapePosition Position { get; init; } = SvgShapePosition.Center;

    /// <summary>
    /// Gets the fill rule of a polygon or a path. The default is <c>nonzero</c>.
    /// </summary>
    public SvgFillRule FillRule { get; init; }

    /// <summary>
    /// Gets the path of <see cref="SvgBasicShapeKind.Path"/>, in the coordinates of the reference box; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public IPath? Path { get; init; }
}
