// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A paint of a draw node whose brush depends on the device transform of each render: a <c>pattern</c>, whose tile
/// renders at the output resolution, or an <c>image</c>, whose sampled level follows the output scale.
/// </summary>
/// <remarks>
/// <para>
/// The resolve stage stores a deferred paint where a draw node holds its fill brush or its pen brush, so the render node
/// keeps its size and every rule that treats paints alike (opacity push-down, bounds, culling) still applies. The emitter
/// replaces the paint with a real brush through <see cref="Resolve(Brush?, in Matrix4x4)"/> and
/// <see cref="Resolve(Pen?, in Matrix4x4)"/> right before it records the command. A deferred paint is never given to the
/// canvas itself.
/// </para>
/// <para>
/// A deferred paint is immutable and is shared between concurrent renders of its document. Whatever it caches for those
/// renders is thread-safe.
/// </para>
/// </remarks>
internal abstract class SvgDeferredPaint : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgDeferredPaint"/> class.
    /// </summary>
    /// <param name="opacity">The opacity that multiplies the paint, in the range 0 to 1.</param>
    protected SvgDeferredPaint(float opacity) => this.Opacity = opacity;

    /// <summary>
    /// Gets the opacity that multiplies the paint, in the range 0 to 1: the fill or stroke opacity, times any group
    /// opacity pushed down to the painted shape.
    /// </summary>
    public float Opacity { get; }

    /// <summary>
    /// Returns the brush for a fill or a pen, resolving a deferred paint for the transform of the draw node.
    /// </summary>
    /// <param name="brush">The brush of the node, which can be a deferred paint.</param>
    /// <param name="transform">The canvas transform of the node: from its user space to device space.</param>
    /// <returns>The brush, or <see langword="null"/> when the paint paints nothing at this transform.</returns>
    public static Brush? Resolve(Brush? brush, in Matrix4x4 transform)
        => brush is SvgDeferredPaint deferred ? deferred.Resolve(ToAffine(transform)) : brush;

    /// <summary>
    /// Returns the pen of a draw node, with a deferred stroke paint resolved for the transform of the node. A pen whose
    /// brush is not deferred is returned as is; otherwise this allocates a pen for the render.
    /// </summary>
    /// <param name="pen">The pen of the node.</param>
    /// <param name="transform">The canvas transform of the node: from its user space to device space.</param>
    /// <returns>The pen, or <see langword="null"/> when the stroke paints nothing at this transform.</returns>
    public static Pen? Resolve(Pen? pen, in Matrix4x4 transform)
    {
        if (pen?.StrokeFill is not SvgDeferredPaint deferred)
        {
            return pen;
        }

        Brush? brush = deferred.Resolve(ToAffine(transform));
        if (brush is null)
        {
            return null;
        }

        // The resolve stage builds pens with SvgPaintCache.GetPen: a solid pen, or a pattern pen for a dashed stroke.
        PenOptions options = pen.StrokePattern.IsEmpty
            ? new PenOptions(brush, pen.StrokeWidth, null)
            : new PenOptions(brush, pen.StrokeWidth, pen.StrokePattern.ToArray(), pen.StrokePatternOffset);

        options.StrokeOptions = pen.StrokeOptions;
        return pen.StrokePattern.IsEmpty ? new SolidPen(options) : new PatternPen(options);
    }

    /// <summary>
    /// Returns a paint with the same content and its opacity multiplied by <paramref name="opacity"/>.
    /// </summary>
    /// <param name="opacity">The factor, in the range 0 to 1.</param>
    /// <returns>The paint.</returns>
    public abstract SvgDeferredPaint MultiplyOpacity(float opacity);

    /// <summary>
    /// Creates the brush for one draw command.
    /// </summary>
    /// <param name="transform">The affine part of the canvas transform of the node: from its user space to device space.</param>
    /// <returns>The brush in the user space of the node, or <see langword="null"/> when nothing is painted.</returns>
    protected abstract Brush? Resolve(Matrix3x2 transform);

    /// <inheritdoc/>
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => throw new InvalidOperationException("A deferred SVG paint must be resolved before it reaches the canvas.");

    /// <inheritdoc/>
    public override bool Equals(Brush? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>
    /// Returns the affine part of a canvas transform.
    /// </summary>
    /// <param name="transform">The transform.</param>
    /// <returns>The affine part.</returns>
    private static Matrix3x2 ToAffine(in Matrix4x4 transform)
        => new(transform.M11, transform.M12, transform.M21, transform.M22, transform.M41, transform.M42);
}
