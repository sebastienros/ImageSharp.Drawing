// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The light source of a lighting filter primitive: <c>feDistantLight</c>, <c>fePointLight</c>, or <c>feSpotLight</c>.
/// The resolve stage creates it in the user space of the filtered element; each render maps it to filter space.
/// </summary>
/// <remarks>
/// <para>
/// Filter Effects Level 1 defines the unit vector <c>L</c> from a surface point to the light and the light color at that
/// point. A distant light has the same <c>L</c> everywhere, from <c>azimuth</c> and <c>elevation</c> in degrees. A point
/// light and a spot light have a position, and <c>L</c> is the normalized difference between the position and the surface
/// point <c>(x, y, Z(x, y))</c>. A spot light also scales the light color by <c>pow(-L.S, specularExponent)</c>, where
/// <c>S</c> is the unit vector from the light to <c>pointsAt</c>, and gives no light where <c>-L.S</c> is not positive
/// or lies outside the cone of <c>limitingConeAngle</c>.
/// </para>
/// <para>
/// Attributes: every value is a number; a missing value, a value with units or a percentage, and a list of more than one
/// number take the initial value 0. Positions follow <c>primitiveUnits</c>: with <c>objectBoundingBox</c>, <c>x</c> and
/// <c>y</c> are fractions of the box, and <c>z</c> is a fraction of the normalized diagonal of the box,
/// <c>sqrt((width^2 + height^2) / 2)</c>, the rule of SVG for lengths that are neither horizontal nor vertical.
/// </para>
/// <para>
/// The spot light exponent is used as given when it is positive; a zero or negative value takes the initial value 1,
/// as the reference images of the resvg suite and Chromium do. Chromium clamps the exponent to the range 1 to 128, which
/// Filter Effects does not ask for, so it differs for exponents below 1 and above 128; the resvg reference uses an
/// exponent of 0.5 as given. A <c>limitingConeAngle</c> of any sign gives the same cone, because only its cosine
/// is used. The specification asks for smoothing at the edge of the cone; as in Chromium, the light fades linearly over
/// <see cref="ConeSmoothing"/> in the cosine of the angle inside the cone. A cone angle of 0 lights nothing, as the
/// specification defines; Chromium treats it as no cone. A spot light whose <c>pointsAt</c> equals its position has no
/// direction and gives no light, as in Chromium.
/// </para>
/// </remarks>
internal sealed class SvgLightSource
{
    /// <summary>
    /// The width of the band of cosines inside the cone edge over which a spot light fades to nothing. Measured from the
    /// Chromium oracle: the light falls linearly to zero across this band.
    /// </summary>
    internal const float ConeSmoothing = 0.016F;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgLightSource"/> class.
    /// </summary>
    /// <param name="kind">The kind of the light source element.</param>
    /// <param name="vector">The unit vector to the light for a distant light; the position otherwise.</param>
    /// <param name="axis">The unit vector from a spot light to the point it points at; zero when it has no direction.</param>
    /// <param name="exponent">The positive exponent of a spot light.</param>
    /// <param name="coneCosine">The cosine of the cone angle of a spot light, or <see langword="null"/> without a cone.</param>
    private SvgLightSource(SvgElementKind kind, Vector3 vector, Vector3 axis, float exponent, float? coneCosine)
    {
        this.Kind = kind;
        this.Vector = vector;
        this.Axis = axis;
        this.Exponent = exponent;
        this.ConeCosine = coneCosine;
    }

    /// <summary>
    /// Gets the kind of the light source element: <see cref="SvgElementKind.FeDistantLight"/>,
    /// <see cref="SvgElementKind.FePointLight"/>, or <see cref="SvgElementKind.FeSpotLight"/>.
    /// </summary>
    public SvgElementKind Kind { get; }

    /// <summary>
    /// Gets the unit vector from the surface to a distant light, or the position of a point light or spot light. Both are
    /// in user space for a light source from <see cref="Create"/> and in filter space for one from <see cref="ToFilter"/>.
    /// </summary>
    public Vector3 Vector { get; }

    /// <summary>
    /// Gets the unit vector <c>S</c> from a spot light to the point it points at; zero when the two points coincide.
    /// </summary>
    public Vector3 Axis { get; }

    /// <summary>
    /// Gets the exponent of a spot light, positive.
    /// </summary>
    public float Exponent { get; }

    /// <summary>
    /// Gets the cosine of the cone angle of a spot light, or <see langword="null"/> when no cone limits the light.
    /// </summary>
    public float? ConeCosine { get; }

    /// <summary>
    /// Finds the light source of the lighting primitive that the context is on and creates it.
    /// </summary>
    /// <remarks>
    /// The content model of Filter Effects asks for exactly one light source child. Chromium uses the first direct child
    /// that is a light source and ignores other children; a light source deeper in the tree does not count.
    /// </remarks>
    /// <param name="context">The build context, moved to the lighting primitive.</param>
    /// <returns>The light source, or <see langword="null"/> when the primitive has no light source child.</returns>
    public static SvgLightSource? Create(SvgFilterBuildContext context)
    {
        SvgElementTree tree = context.Tree;
        int light = tree.GetFirstChild(context.Element);
        while (light >= 0 && !SvgFilterTables.IsLightSource(tree[light].Kind))
        {
            light = tree.GetNextSibling(light);
        }

        if (light < 0)
        {
            return null;
        }

        SvgElementKind kind = tree[light].Kind;
        if (kind == SvgElementKind.FeDistantLight)
        {
            // Filter Effects: L = (cos(azimuth) cos(elevation), sin(azimuth) cos(elevation), sin(elevation)), with the
            // angles in degrees. The angles are reduced in double precision so that large values keep their meaning.
            double azimuth = GetNumber(context, light, SvgAttributeId.Azimuth) * (Math.PI / 180D);
            double elevation = GetNumber(context, light, SvgAttributeId.Elevation) * (Math.PI / 180D);
            Vector3 direction = new(
                (float)(Math.Cos(azimuth) * Math.Cos(elevation)),
                (float)(Math.Sin(azimuth) * Math.Cos(elevation)),
                (float)Math.Sin(elevation));
            return new SvgLightSource(kind, direction, Vector3.Zero, 1F, null);
        }

        Vector3 position = GetPoint(context, light, SvgAttributeId.X, SvgAttributeId.Y, SvgAttributeId.Z);
        if (kind == SvgElementKind.FePointLight)
        {
            return new SvgLightSource(kind, position, Vector3.Zero, 1F, null);
        }

        Vector3 pointsAt = GetPoint(context, light, SvgAttributeId.PointsAtX, SvgAttributeId.PointsAtY, SvgAttributeId.PointsAtZ);
        Vector3 axis = pointsAt - position;
        float length = axis.Length();
        axis = length > 0F && float.IsFinite(length) ? axis / length : Vector3.Zero;

        float exponent = GetNumber(context, light, SvgAttributeId.SpecularExponent, 1F);
        if (!(exponent > 0F))
        {
            exponent = 1F;
        }

        float? coneCosine = null;
        if (context.TryGetValue(light, SvgAttributeId.LimitingConeAngle, out SvgFilterAttributeValue cone) && cone.NumberCount == 1)
        {
            coneCosine = (float)Math.Cos(context.GetNumbers(in cone)[0] * (Math.PI / 180D));
        }

        return new SvgLightSource(kind, position, axis, exponent, coneCosine);
    }

    /// <summary>
    /// Maps the light source to filter space: positions scale by <paramref name="scale"/> and move by
    /// <paramref name="translation"/>, and <c>z</c> scales by <paramref name="zScale"/>. Directions (the vector to a
    /// distant light, the spot light axis) scale the same way and normalize again.
    /// </summary>
    /// <param name="scale">The filter pixels per user unit along x and y.</param>
    /// <param name="translation">The position of the user space origin in filter space.</param>
    /// <param name="zScale">The filter pixels per user unit along z.</param>
    /// <returns>The light source in filter space.</returns>
    public SvgLightSource ToFilter(Vector2 scale, Vector2 translation, float zScale)
    {
        Vector3 map = new(scale, zScale);
        if (this.Kind == SvgElementKind.FeDistantLight)
        {
            return new SvgLightSource(this.Kind, Vector3.Normalize(this.Vector * map), Vector3.Zero, 1F, null);
        }

        Vector3 offset = new(translation, 0F);
        Vector3 position = (this.Vector * map) + offset;
        Vector3 axis = this.Axis;
        if (this.Kind == SvgElementKind.FeSpotLight && axis != Vector3.Zero)
        {
            // The axis is a direction in user space; it maps through the scale only, then normalizes again.
            axis = Vector3.Normalize(axis * map);
        }

        return new SvgLightSource(this.Kind, position, axis, this.Exponent, this.ConeCosine);
    }

    /// <summary>
    /// Returns the unit vector from a surface point to the light and the factor that the light color has at that point.
    /// </summary>
    /// <param name="surface">The surface point with its height <c>Z(x, y)</c>, in the space of this light source.</param>
    /// <param name="factor">Receives the factor of the light color: 1 for a distant or point light, the spot light falloff otherwise.</param>
    /// <returns>The unit vector <c>L</c>; zero where the light coincides with the surface point.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 GetLight(Vector3 surface, out float factor)
    {
        factor = 1F;
        if (this.Kind == SvgElementKind.FeDistantLight)
        {
            return this.Vector;
        }

        Vector3 light = this.Vector - surface;
        float length = light.Length();
        light = length > 0F ? light / length : Vector3.Zero;
        if (this.Kind == SvgElementKind.FeSpotLight)
        {
            factor = this.GetSpotFactor(light);
        }

        return light;
    }

    /// <summary>
    /// Returns the falloff of a spot light along one light vector: <c>pow(-L.S, specularExponent)</c>, zero where
    /// <c>-L.S</c> is not positive or lies outside the cone, and faded linearly across the smoothing band inside the cone.
    /// </summary>
    /// <param name="light">The unit vector <c>L</c> from the surface point to the light.</param>
    /// <returns>The factor in the range 0 to 1.</returns>
    private float GetSpotFactor(Vector3 light)
    {
        float cosine = -Vector3.Dot(light, this.Axis);

        // The negated comparison also rejects NaN.
        if (!(cosine > 0F))
        {
            return 0F;
        }

        // The initial exponent 1 is the common case; it needs no power function.
        float factor = this.Exponent == 1F ? cosine : MathF.Pow(cosine, this.Exponent);
        if (this.ConeCosine is float cone)
        {
            float inside = cosine - cone;
            if (!(inside >= 0F))
            {
                return 0F;
            }

            if (inside < ConeSmoothing)
            {
                factor *= inside / ConeSmoothing;
            }
        }

        return factor;
    }

    /// <summary>
    /// Reads a position of a light source and converts it from <c>primitiveUnits</c> to user space.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="light">The light source element.</param>
    /// <param name="x">The attribute of the x coordinate.</param>
    /// <param name="y">The attribute of the y coordinate.</param>
    /// <param name="z">The attribute of the z coordinate.</param>
    /// <returns>The position in user space.</returns>
    private static Vector3 GetPoint(SvgFilterBuildContext context, int light, SvgAttributeId x, SvgAttributeId y, SvgAttributeId z)
    {
        Vector2 point = context.ToUserPoint(GetNumber(context, light, x), GetNumber(context, light, y));
        float depth = GetNumber(context, light, z);
        if (context.PrimitiveUnits == SvgCoordinateUnits.ObjectBoundingBox)
        {
            RectangleF box = context.BoundingBox!.Value;
            depth *= MathF.Sqrt(((box.Width * box.Width) + (box.Height * box.Height)) / 2F);
        }

        return new Vector3(point, depth);
    }

    /// <summary>
    /// Reads a number attribute of a light source element.
    /// </summary>
    /// <param name="context">The build context.</param>
    /// <param name="light">The light source element.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="initial">The value of a missing or invalid attribute.</param>
    /// <returns>The number.</returns>
    private static float GetNumber(SvgFilterBuildContext context, int light, SvgAttributeId attribute, float initial = 0F)
        => context.TryGetValue(light, attribute, out SvgFilterAttributeValue value) && value.NumberCount == 1
            ? context.GetNumbers(in value)[0]
            : initial;
}
