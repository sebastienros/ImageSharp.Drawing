// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The length, points, and directions along the path of a <c>textPath</c>, for the placement of text clusters.
/// </summary>
/// <remarks>
/// <para>
/// The path is flattened with the curve flattening of the library (<see cref="ILineSegment.CopyTo"/>, which
/// <see cref="IPath.ToLinearGeometry"/> also uses), mapped through a transform, and stored as chords with their
/// cumulative start distances. A query is a binary search, so placing N clusters on a path of S chords costs
/// O(N log S). <see cref="IPath.TryGetPathPointAtDistance(float, Vector2, out PathPoint)"/> walks the segments from the
/// start for every query and wraps distances on closed paths; text on a path needs neither, because Chromium hides the
/// clusters past the end of a closed path.
/// </para>
/// <para>
/// Lengths add over the subpaths as Skia measures them: the gap between two subpaths does not count, and the closing
/// segment of a closed subpath does. Directions follow the curves: at a vertex inside a curve segment the tangent is
/// the bisector of its two chords, and along a chord the tangent turns linearly from the tangent at its start to the
/// tangent at its end. For a circle flattened into equal chords this gives the exact tangent at every vertex and at the
/// middle of every chord. Chromium evaluates the tangent on the curve itself, so the chord direction alone would turn
/// glyphs on a curve by up to half the turn of a chord. Vertices between segments and the vertices of polylines keep
/// sharp corners.
/// </para>
/// </remarks>
internal sealed class SvgTextPathMeasure
{
    /// <summary>
    /// The largest size of the path in flattening units. It bounds the number of chords of a hostile path whose size is
    /// huge compared with the tolerance.
    /// </summary>
    private const float MaxFlattenedExtent = 1E6F;

    private readonly Chord[] chords;
    private readonly float[] distances;
    private readonly Vector2 firstPoint;
    private readonly Vector2 lastPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextPathMeasure"/> class.
    /// </summary>
    /// <param name="chords">The chords in path order.</param>
    /// <param name="firstPoint">The first point of the path.</param>
    /// <param name="lastPoint">The last point of the path.</param>
    private SvgTextPathMeasure(Chord[] chords, Vector2 firstPoint, Vector2 lastPoint)
    {
        this.chords = chords;
        this.distances = new float[chords.Length];
        float distance = 0F;
        for (int i = 0; i < chords.Length; i++)
        {
            this.distances[i] = distance;
            distance += chords[i].Length;
        }

        this.Length = distance;
        this.firstPoint = firstPoint;
        this.lastPoint = lastPoint;
    }

    /// <summary>
    /// Gets the length of the path in units of the target space.
    /// </summary>
    public float Length { get; }

    /// <summary>
    /// Measures a path.
    /// </summary>
    /// <param name="path">The path in its own coordinates.</param>
    /// <param name="transform">The transform from the path coordinates to the target space.</param>
    /// <param name="tolerance">The largest distance between a curve and its chords, in units of the target space.</param>
    /// <returns>The measure, or <see langword="null"/> when the path has no point.</returns>
    public static SvgTextPathMeasure? Create(IPath path, Matrix3x2 transform, float tolerance)
    {
        // The library flattens a curve to within 1/64 of its chords at the scale it is given. The scale k maps that
        // bound to the tolerance in the target space, through the largest scale of the transform. It never flattens
        // more coarsely than the library does at scale 1.
        float transformScale = MathF.Max(
            new Vector2(transform.M11, transform.M12).Length(),
            new Vector2(transform.M21, transform.M22).Length());
        float k = MathF.Max(1F, 1F / (64F * tolerance)) * transformScale;
        RectangleF bounds = path.Bounds;
        float extent = MathF.Max(bounds.Width, bounds.Height);
        if (!(extent * k <= MaxFlattenedExtent))
        {
            // Also reached for a zero tolerance, where k is infinite.
            k = extent > 0F ? MaxFlattenedExtent / extent : 1F;
        }

        if (!(k > 0F) || !float.IsFinite(k))
        {
            return null;
        }

        Builder builder = new(new Vector2(k), Matrix3x2.CreateScale(1F / k) * transform);
        IEnumerable<IPath> figures = path is ComplexPolygon complex ? complex.Paths : [path];
        foreach (IPath figure in figures)
        {
            builder.AddFigure(figure);
        }

        return builder.Build();
    }

    /// <summary>
    /// Returns the point and the direction at a distance along the path.
    /// </summary>
    /// <param name="distance">The distance, from 0 to <see cref="Length"/>.</param>
    /// <param name="point">The point.</param>
    /// <returns>The direction in degrees, clockwise from the positive x axis in the y-down target space.</returns>
    public float GetPoint(float distance, out Vector2 point)
    {
        if (this.chords.Length == 0)
        {
            point = this.firstPoint;
            return 0F;
        }

        if (distance >= this.Length)
        {
            point = this.lastPoint;
            return ToDegrees(this.chords[^1].EndTangent);
        }

        // The last chord that starts at or before the distance.
        int index = Array.BinarySearch(this.distances, distance);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, this.chords.Length - 1);
        ref readonly Chord chord = ref this.chords[index];
        float local = MathF.Max(distance - this.distances[index], 0F);
        point = chord.Start + (chord.Direction * local);
        Vector2 tangent = Vector2.Lerp(chord.StartTangent, chord.EndTangent, MathF.Min(local / chord.Length, 1F));
        return ToDegrees(tangent.LengthSquared() > 0F ? tangent : chord.Direction);
    }

    /// <summary>
    /// Returns the angle of a direction in degrees.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The angle, clockwise from the positive x axis in a y-down space.</returns>
    private static float ToDegrees(Vector2 direction) => MathF.Atan2(direction.Y, direction.X) * (180F / MathF.PI);

    /// <summary>
    /// One chord of the flattened path.
    /// </summary>
    private readonly struct Chord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Chord"/> struct, with the chord direction as both tangents.
        /// </summary>
        /// <param name="start">The start point.</param>
        /// <param name="direction">The unit direction.</param>
        /// <param name="length">The length.</param>
        /// <param name="startsSmooth">Whether the start point lies inside a curve segment.</param>
        public Chord(Vector2 start, Vector2 direction, float length, bool startsSmooth)
        {
            this.Start = start;
            this.Direction = direction;
            this.Length = length;
            this.StartsSmooth = startsSmooth;
            this.StartTangent = direction;
            this.EndTangent = direction;
        }

        /// <summary>
        /// Gets the start point.
        /// </summary>
        public Vector2 Start { get; }

        /// <summary>
        /// Gets the unit direction.
        /// </summary>
        public Vector2 Direction { get; }

        /// <summary>
        /// Gets the length.
        /// </summary>
        public float Length { get; }

        /// <summary>
        /// Gets a value indicating whether the start point lies inside a curve segment.
        /// </summary>
        public bool StartsSmooth { get; }

        /// <summary>
        /// Gets the tangent at the start point.
        /// </summary>
        public Vector2 StartTangent { get; init; }

        /// <summary>
        /// Gets the tangent at the end point.
        /// </summary>
        public Vector2 EndTangent { get; init; }
    }

    /// <summary>
    /// Collects the chords of a path, one subpath at a time.
    /// </summary>
    private sealed class Builder
    {
        private readonly Vector2 scale;
        private readonly Matrix3x2 toTarget;
        private readonly List<Chord> chords = [];
        private readonly List<Vector2> points = [];
        private readonly List<bool> smooth = [];
        private Vector2? firstPoint;
        private Vector2 lastPoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="Builder"/> class.
        /// </summary>
        /// <param name="scale">The flattening scale.</param>
        /// <param name="toTarget">The transform from flattened points to the target space.</param>
        public Builder(Vector2 scale, Matrix3x2 toTarget)
        {
            this.scale = scale;
            this.toTarget = toTarget;
        }

        /// <summary>
        /// Adds the chords of one figure of a path.
        /// </summary>
        /// <param name="figure">The figure.</param>
        public void AddFigure(IPath figure)
        {
            if (figure is not Path simple)
            {
                // Other path types have no segments to classify: their vertices keep sharp corners.
                LinearGeometry geometry = figure.ToLinearGeometry(this.scale);
                foreach (LinearContour contour in geometry.GetContours())
                {
                    foreach (PointF point in geometry.GetContourPoints(in contour))
                    {
                        this.AddPoint(Vector2.Transform(point, this.toTarget), false);
                    }

                    this.EndSubpath(contour.IsClosed);
                }

                return;
            }

            foreach (ILineSegment segment in simple.LineSegments)
            {
                int count = segment.LinearVertexCount(this.scale);
                PointF[] buffer = ArrayPool<PointF>.Shared.Rent(count);
                Span<PointF> vertices = buffer.AsSpan(0, count);
                segment.CopyTo(vertices, false, this.scale);

                // The inner vertices of a curve lie on a smooth curve; the ends of every segment may be corners.
                bool curve = segment is not LinearLineSegment;
                for (int i = 0; i < count; i++)
                {
                    this.AddPoint(Vector2.Transform(vertices[i], this.toTarget), curve && i > 0 && i < count - 1);
                }

                ArrayPool<PointF>.Shared.Return(buffer);
            }

            this.EndSubpath(simple.IsClosed);
        }

        /// <summary>
        /// Returns the measure of the chords so far.
        /// </summary>
        /// <returns>The measure, or <see langword="null"/> when no subpath had a point.</returns>
        public SvgTextPathMeasure? Build()
            => this.firstPoint is Vector2 first ? new SvgTextPathMeasure([.. this.chords], first, this.lastPoint) : null;

        /// <summary>
        /// Adds a point of the current subpath unless it repeats the previous point.
        /// </summary>
        /// <param name="point">The point in the target space.</param>
        /// <param name="isSmooth">Whether the point lies inside a curve segment.</param>
        private void AddPoint(Vector2 point, bool isSmooth)
        {
            if (this.points.Count > 0 && this.points[^1] == point)
            {
                // A repeated point joins two segments, where the direction may turn sharply.
                this.smooth[^1] = false;
                return;
            }

            this.points.Add(point);
            this.smooth.Add(isSmooth);
        }

        /// <summary>
        /// Turns the points of the current subpath into chords with the tangents at their ends, and starts a new subpath.
        /// </summary>
        /// <param name="closed">Whether the subpath is closed, which adds the closing chord.</param>
        private void EndSubpath(bool closed)
        {
            List<Vector2> points = this.points;
            if (points.Count == 0)
            {
                return;
            }

            if (closed && points[^1] != points[0])
            {
                points.Add(points[0]);
                this.smooth.Add(false);
            }

            this.firstPoint ??= points[0];
            this.lastPoint = points[^1];

            int firstChord = this.chords.Count;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector2 delta = points[i + 1] - points[i];
                float length = delta.Length();
                if (length > 0F && float.IsFinite(length))
                {
                    this.chords.Add(new Chord(points[i], delta / length, length, this.smooth[i]));
                }
            }

            // A vertex inside a curve takes the bisector of its two chords as its tangent. At other vertices each chord
            // keeps its own direction.
            for (int c = firstChord; c < this.chords.Count; c++)
            {
                Chord chord = this.chords[c];
                Vector2 start = c > firstChord && chord.StartsSmooth
                    ? Bisect(this.chords[c - 1].Direction, chord.Direction)
                    : chord.Direction;
                Vector2 end = c + 1 < this.chords.Count && this.chords[c + 1].StartsSmooth
                    ? Bisect(chord.Direction, this.chords[c + 1].Direction)
                    : chord.Direction;
                this.chords[c] = chord with { StartTangent = start, EndTangent = end };
            }

            points.Clear();
            this.smooth.Clear();

            static Vector2 Bisect(Vector2 a, Vector2 b)
            {
                Vector2 sum = a + b;
                return sum.LengthSquared() > 1E-12F ? Vector2.Normalize(sum) : b;
            }
        }
    }
}
