// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A color of CSS Color Level 4 in its own color space, with components that may lie outside the sRGB gamut and may be
/// missing (<c>none</c>). The color functions build it; <see cref="ToColor"/> maps it to the sRGB output.
/// </summary>
/// <remarks>
/// <para>
/// The conversions follow the sample code of CSS Color Level 4, section 19 ("Sample code for Color Conversions"): every
/// space converts through CIE XYZ relative to D65, with the Bradford adaptation of that section for the spaces relative to
/// D50 (Lab, LCH, ProPhoto RGB, XYZ D50). The matrices are those of the section, which gives most of them as rational
/// numbers.
/// </para>
/// <para>
/// Gamut mapping: <see cref="ToColor"/> clips each sRGB channel to 0 to 1 after the conversion. CSS Color 4 section 14
/// recommends a chroma reduction in OkLCh instead, but the Chromium oracle clips: for example <c>lab(50 100 0)</c>,
/// <c>oklch(0.7 0.3 30)</c>, and <c>color(display-p3 0 1 0)</c> render as the clipped values exactly, so this renderer
/// clips too.
/// </para>
/// </remarks>
internal readonly struct SvgCssColor
{
    // CSS Color 4, section 19: the white points by their 4-figure CIE x,y chromaticities.
    private static readonly Vector3D D50 = new(0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585);

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssColor"/> struct.
    /// </summary>
    /// <param name="space">The color space of the components.</param>
    /// <param name="c0">The first component.</param>
    /// <param name="c1">The second component.</param>
    /// <param name="c2">The third component.</param>
    /// <param name="alpha">The alpha, 0 to 1.</param>
    /// <param name="missing">The missing components: bit 0 to 2 for the components, bit 3 for alpha.</param>
    public SvgCssColor(SvgColorSpace space, double c0, double c1, double c2, double alpha, int missing)
    {
        this.Space = space;
        this.C0 = c0;
        this.C1 = c1;
        this.C2 = c2;
        this.Alpha = alpha;
        this.Missing = missing;
    }

    /// <summary>
    /// Gets the color space of the components.
    /// </summary>
    public SvgColorSpace Space { get; }

    /// <summary>
    /// Gets the first component: red, L, hue of HSL and HWB, or X.
    /// </summary>
    public double C0 { get; }

    /// <summary>
    /// Gets the second component.
    /// </summary>
    public double C1 { get; }

    /// <summary>
    /// Gets the third component.
    /// </summary>
    public double C2 { get; }

    /// <summary>
    /// Gets the alpha, 0 to 1.
    /// </summary>
    public double Alpha { get; }

    /// <summary>
    /// Gets the missing components (<c>none</c>): bits 0 to 2 for the components, bit 3 for alpha. A missing component
    /// counts as zero, except in <c>color-mix()</c>, which takes it from the other color (CSS Color 4, section 13.4).
    /// </summary>
    public int Missing { get; }

    /// <summary>
    /// Creates the color of an sRGB <see cref="Color"/>.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The color in <see cref="SvgColorSpace.Srgb"/>.</returns>
    public static SvgCssColor FromColor(Color color)
    {
        Vector4 rgba = color.ToScaledVector4();
        return new SvgCssColor(SvgColorSpace.Srgb, rgba.X, rgba.Y, rgba.Z, rgba.W, 0);
    }

    /// <summary>
    /// Returns whether a space is cylindrical: its hue component needs hue interpolation.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <returns>The index of the hue component, or -1 for a rectangular space.</returns>
    public static int GetHueIndex(SvgColorSpace space)
        => space switch
        {
            SvgColorSpace.Hsl or SvgColorSpace.Hwb => 0,
            SvgColorSpace.Lch or SvgColorSpace.Oklch => 2,
            _ => -1
        };

    /// <summary>
    /// Returns the component of an index.
    /// </summary>
    /// <param name="index">0 to 2, or 3 for alpha.</param>
    /// <returns>The component.</returns>
    public double Get(int index) => index switch
    {
        0 => this.C0,
        1 => this.C1,
        2 => this.C2,
        _ => this.Alpha
    };

    /// <summary>
    /// Converts the color to another space. The components that the conversion keeps (CSS Color 4, section 13.4: "analogous
    /// components", here the same component of two spaces of the same family) stay missing; the hue becomes missing when it
    /// is powerless (section 4.4.1).
    /// </summary>
    /// <param name="space">The target space.</param>
    /// <returns>The converted color.</returns>
    public SvgCssColor ConvertTo(SvgColorSpace space)
    {
        if (space == this.Space)
        {
            return this;
        }

        Vector3D xyz = ToXyzD65(this.Space, new Vector3D(this.C0, this.C1, this.C2));
        Vector3D result = FromXyzD65(space, xyz);
        int missing = this.Missing & 8;
        if (IsAnalogous(this.Space, space))
        {
            missing |= this.Missing & 7;
        }

        // A hue whose chroma or saturation is (almost) zero is powerless, and so missing.
        double epsilon = space switch
        {
            SvgColorSpace.Lch => 0.0015,
            SvgColorSpace.Oklch => 0.000004,
            _ => -1
        };

        if (epsilon > 0 && result.Y <= epsilon)
        {
            missing |= 4;
        }
        else if (space == SvgColorSpace.Hsl && result.Y <= 100D / 100000D)
        {
            missing |= 1;
        }
        else if (space == SvgColorSpace.Hwb && result.Y + result.Z >= 100 - 1E-9)
        {
            missing |= 1;
        }

        return new SvgCssColor(space, result.X, result.Y, result.Z, this.Alpha, missing);
    }

    /// <summary>
    /// Maps the color to an sRGB <see cref="Color"/>: converts it to gamma-encoded sRGB and clips each channel to 0 to 1,
    /// as the Chromium oracle does.
    /// </summary>
    /// <returns>The color.</returns>
    public Color ToColor()
    {
        Vector3D rgb = this.Space == SvgColorSpace.Srgb
            ? new Vector3D(this.C0, this.C1, this.C2)
            : FromXyzD65(SvgColorSpace.Srgb, ToXyzD65(this.Space, new Vector3D(this.C0, this.C1, this.C2)));

        return Color.FromScaledVector(new Vector4(Clip(rgb.X), Clip(rgb.Y), Clip(rgb.Z), Clip(this.Alpha)));

        static float Clip(double value) => double.IsNaN(value) ? 0F : (float)Math.Clamp(value, 0D, 1D);
    }

    /// <summary>
    /// Returns whether a conversion between two spaces keeps the meaning of each component: the RGB spaces among
    /// themselves, and the same space.
    /// </summary>
    /// <param name="from">The source space.</param>
    /// <param name="to">The target space.</param>
    /// <returns><see langword="true"/> if missing components carry over.</returns>
    private static bool IsAnalogous(SvgColorSpace from, SvgColorSpace to)
    {
        static bool IsRgb(SvgColorSpace space) => space <= SvgColorSpace.Rec2020;
        static bool IsXyz(SvgColorSpace space) => space is SvgColorSpace.XyzD50 or SvgColorSpace.XyzD65;
        return (IsRgb(from) && IsRgb(to))
            || (IsXyz(from) && IsXyz(to))
            || (from is SvgColorSpace.Lab or SvgColorSpace.Oklab && to is SvgColorSpace.Lab or SvgColorSpace.Oklab)
            || (from is SvgColorSpace.Lch or SvgColorSpace.Oklch && to is SvgColorSpace.Lch or SvgColorSpace.Oklch);
    }

    /// <summary>
    /// Converts components of a space to CIE XYZ relative to D65.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="c">The components.</param>
    /// <returns>The XYZ value.</returns>
    private static Vector3D ToXyzD65(SvgColorSpace space, Vector3D c)
    {
        switch (space)
        {
            case SvgColorSpace.Srgb:
                return LinearSrgbToXyz(c.Map(SrgbToLinear));
            case SvgColorSpace.SrgbLinear:
                return LinearSrgbToXyz(c);
            case SvgColorSpace.DisplayP3:
                return LinearP3ToXyz(c.Map(SrgbToLinear));
            case SvgColorSpace.DisplayP3Linear:
                return LinearP3ToXyz(c);
            case SvgColorSpace.A98Rgb:
                // CSS Color 4, section 19, lin_a98rgb_to_XYZ, with the transfer function of lin_a98rgb.
                return Multiply(
                    c.Map(v => Math.CopySign(Math.Pow(Math.Abs(v), 563D / 256D), v)),
                    [
                        573536D / 994567, 263643D / 1420810, 187206D / 994567,
                        591459D / 1989134, 6239551D / 9945670, 374412D / 4972835,
                        53769D / 1989134, 351524D / 4972835, 4929758D / 4972835,
                    ]);
            case SvgColorSpace.ProphotoRgb:
                // Section 19, lin_ProPhoto and lin_ProPhoto_to_XYZ, which give D50 XYZ.
                return D50ToD65(Multiply(
                    c.Map(v => Math.Abs(v) <= 16D / 512D ? v / 16D : Math.CopySign(Math.Pow(Math.Abs(v), 1.8D), v)),
                    [
                        0.79776664490064230, 0.13518129740053308, 0.03134773412839220,
                        0.28807482881940130, 0.71183523424187300, 0.00008993693872564,
                        0, 0, 0.82510460251046020,
                    ]));
            case SvgColorSpace.Rec2020:
                // Section 19, lin_2020 and lin_2020_to_XYZ.
                return Multiply(
                    c.Map(v => Math.CopySign(Math.Pow(Math.Abs(v), 2.4D), v)),
                    [
                        63426534D / 99577255, 20160776D / 139408157, 47086771D / 278816314,
                        26158966D / 99577255, 472592308D / 697040785, 8267143D / 139408157,
                        0, 19567812D / 697040785, 295819943D / 278816314,
                    ]);
            case SvgColorSpace.XyzD50:
                return D50ToD65(c);
            case SvgColorSpace.XyzD65:
                return c;
            case SvgColorSpace.Lab:
                return D50ToD65(LabToXyzD50(c));
            case SvgColorSpace.Lch:
                return D50ToD65(LabToXyzD50(PolarToRectangular(c)));
            case SvgColorSpace.Oklab:
                return OklabToXyz(c);
            case SvgColorSpace.Oklch:
                return OklabToXyz(PolarToRectangular(c));
            case SvgColorSpace.Hsl:
                return ToXyzD65(SvgColorSpace.Srgb, HslToSrgb(c));
            default:
                return ToXyzD65(SvgColorSpace.Srgb, HwbToSrgb(c));
        }
    }

    /// <summary>
    /// Converts CIE XYZ relative to D65 to the components of a space.
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="xyz">The XYZ value.</param>
    /// <returns>The components.</returns>
    private static Vector3D FromXyzD65(SvgColorSpace space, Vector3D xyz)
    {
        switch (space)
        {
            case SvgColorSpace.Srgb:
                return XyzToLinearSrgb(xyz).Map(LinearToSrgb);
            case SvgColorSpace.SrgbLinear:
                return XyzToLinearSrgb(xyz);
            case SvgColorSpace.DisplayP3:
                return XyzToLinearP3(xyz).Map(LinearToSrgb);
            case SvgColorSpace.DisplayP3Linear:
                return XyzToLinearP3(xyz);
            case SvgColorSpace.A98Rgb:
                // Section 19, XYZ_to_lin_a98rgb and gam_a98rgb.
                return Multiply(
                    xyz,
                    [
                        1829569D / 896150, -506331D / 896150, -308931D / 896150,
                        -851781D / 878810, 1648619D / 878810, 36519D / 878810,
                        16779D / 1248040, -147721D / 1248040, 1266979D / 1248040,
                    ])
                    .Map(v => Math.CopySign(Math.Pow(Math.Abs(v), 256D / 563D), v));
            case SvgColorSpace.ProphotoRgb:
                // Section 19, XYZ_to_lin_ProPhoto and gam_ProPhoto.
                return Multiply(
                    D65ToD50(xyz),
                    [
                        1.34578688164715830, -0.25557208737979464, -0.05110186497554526,
                        -0.54463070512490190, 1.50824774284514680, 0.02052744743642139,
                        0, 0, 1.21196754563894520,
                    ])
                    .Map(v => Math.Abs(v) >= 1D / 512D ? Math.CopySign(Math.Pow(Math.Abs(v), 1D / 1.8D), v) : 16D * v);
            case SvgColorSpace.Rec2020:
                // Section 19, XYZ_to_lin_2020 and gam_2020.
                return Multiply(
                    xyz,
                    [
                        30757411D / 17917100, -6372589D / 17917100, -4539589D / 17917100,
                        -19765991D / 29648200, 47925759D / 29648200, 467509D / 29648200,
                        792561D / 44930125, -1921689D / 44930125, 42328811D / 44930125,
                    ])
                    .Map(v => Math.CopySign(Math.Pow(Math.Abs(v), 1D / 2.4D), v));
            case SvgColorSpace.XyzD50:
                return D65ToD50(xyz);
            case SvgColorSpace.XyzD65:
                return xyz;
            case SvgColorSpace.Lab:
                return XyzD50ToLab(D65ToD50(xyz));
            case SvgColorSpace.Lch:
                return RectangularToPolar(XyzD50ToLab(D65ToD50(xyz)));
            case SvgColorSpace.Oklab:
                return XyzToOklab(xyz);
            case SvgColorSpace.Oklch:
                return RectangularToPolar(XyzToOklab(xyz));
            case SvgColorSpace.Hsl:
                return SrgbToHsl(FromXyzD65(SvgColorSpace.Srgb, xyz));
            default:
                return SrgbToHwb(FromXyzD65(SvgColorSpace.Srgb, xyz));
        }
    }

    /// <summary>
    /// The sRGB transfer function to linear light, extended to negative values (section 19, <c>lin_sRGB</c>).
    /// </summary>
    /// <param name="value">The encoded value.</param>
    /// <returns>The linear value.</returns>
    private static double SrgbToLinear(double value)
    {
        double abs = Math.Abs(value);
        return abs <= 0.04045 ? value / 12.92 : Math.CopySign(Math.Pow((abs + 0.055) / 1.055, 2.4), value);
    }

    /// <summary>
    /// The sRGB transfer function from linear light, extended to negative values (section 19, <c>gam_sRGB</c>).
    /// </summary>
    /// <param name="value">The linear value.</param>
    /// <returns>The encoded value.</returns>
    private static double LinearToSrgb(double value)
    {
        double abs = Math.Abs(value);
        return abs > 0.0031308 ? Math.CopySign((1.055 * Math.Pow(abs, 1 / 2.4)) - 0.055, value) : 12.92 * value;
    }

    /// <summary>
    /// Section 19, <c>lin_sRGB_to_XYZ</c>.
    /// </summary>
    /// <param name="c">Linear sRGB.</param>
    /// <returns>XYZ D65.</returns>
    private static Vector3D LinearSrgbToXyz(Vector3D c)
        => Multiply(
            c,
            [
                506752D / 1228815, 87881D / 245763, 12673D / 70218,
                87098D / 409605, 175762D / 245763, 12673D / 175545,
                7918D / 409605, 87881D / 737289, 1001167D / 1053270,
            ]);

    /// <summary>
    /// Section 19, <c>XYZ_to_lin_sRGB</c>.
    /// </summary>
    /// <param name="c">XYZ D65.</param>
    /// <returns>Linear sRGB.</returns>
    private static Vector3D XyzToLinearSrgb(Vector3D c)
        => Multiply(
            c,
            [
                12831D / 3959, -329D / 214, -1974D / 3959,
                -851781D / 878810, 1648619D / 878810, 36519D / 878810,
                705D / 12673, -2585D / 12673, 705D / 667,
            ]);

    /// <summary>
    /// Section 19, <c>lin_P3_to_XYZ</c>.
    /// </summary>
    /// <param name="c">Linear Display P3.</param>
    /// <returns>XYZ D65.</returns>
    private static Vector3D LinearP3ToXyz(Vector3D c)
        => Multiply(
            c,
            [
                608311D / 1250200, 189793D / 714400, 198249D / 1000160,
                35783D / 156275, 247089D / 357200, 198249D / 2500400,
                0, 32229D / 714400, 5220557D / 5000800,
            ]);

    /// <summary>
    /// Section 19, <c>XYZ_to_lin_P3</c>.
    /// </summary>
    /// <param name="c">XYZ D65.</param>
    /// <returns>Linear Display P3.</returns>
    private static Vector3D XyzToLinearP3(Vector3D c)
        => Multiply(
            c,
            [
                446124D / 178915, -333277D / 357830, -72051D / 178915,
                -14852D / 17905, 63121D / 35810, 423D / 17905,
                11844D / 330415, -50337D / 660830, 316169D / 330415,
            ]);

    /// <summary>
    /// Section 19, <c>D65_to_D50</c>: the Bradford chromatic adaptation.
    /// </summary>
    /// <param name="c">XYZ D65.</param>
    /// <returns>XYZ D50.</returns>
    private static Vector3D D65ToD50(Vector3D c)
        => Multiply(
            c,
            [
                1.0479297925449969, 0.022946870601609652, -0.05019226628920524,
                0.02962780877005599, 0.9904344267538799, -0.017073799063418826,
                -0.009243040646204504, 0.015055191490298152, 0.7518742814281371,
            ]);

    /// <summary>
    /// Section 19, <c>D50_to_D65</c>.
    /// </summary>
    /// <param name="c">XYZ D50.</param>
    /// <returns>XYZ D65.</returns>
    private static Vector3D D50ToD65(Vector3D c)
        => Multiply(
            c,
            [
                0.955473421488075, -0.02309845494876471, 0.06325924320057072,
                -0.0283697093338637, 1.0099953980813041, 0.021041441191917323,
                0.012314014864481998, -0.020507649298898964, 1.330365926242124,
            ]);

    /// <summary>
    /// Section 19, <c>Lab_to_XYZ</c>.
    /// </summary>
    /// <param name="lab">L, a, b.</param>
    /// <returns>XYZ D50.</returns>
    private static Vector3D LabToXyzD50(Vector3D lab)
    {
        const double kappa = 24389D / 27D;
        const double epsilon = 216D / 24389D;
        double lightness116 = lab.X / 116D;
        double a500 = lab.Y / 500D;
        double b200 = lab.Z / 200D;
        double f1 = lightness116 + (16D / 116D);
        double f0 = f1 + a500;
        double f2 = f1 - b200;
        double x = f0 * f0 * f0 > epsilon ? f0 * f0 * f0 : (lightness116 + a500) * (116D / kappa);
        double y = lab.X > kappa * epsilon ? Math.Pow((lab.X + 16D) / 116D, 3D) : lab.X / kappa;
        double z = f2 * f2 * f2 > epsilon ? f2 * f2 * f2 : (lightness116 - b200) * (116D / kappa);
        return new Vector3D(x * D50.X, y * D50.Y, z * D50.Z);
    }

    /// <summary>
    /// Section 19, <c>XYZ_to_Lab</c>.
    /// </summary>
    /// <param name="xyz">XYZ D50.</param>
    /// <returns>L, a, b.</returns>
    private static Vector3D XyzD50ToLab(Vector3D xyz)
    {
        const double kappa = 24389D / 27D;
        const double epsilon = 216D / 24389D;
        static double F(double value) => value > epsilon ? Math.Cbrt(value) : ((kappa * value) + 16D) / 116D;
        double f0 = F(xyz.X / D50.X);
        double f1 = F(xyz.Y / D50.Y);
        double f2 = F(xyz.Z / D50.Z);
        return new Vector3D((116D * f1) - 16D, 500D * (f0 - f1), 200D * (f1 - f2));
    }

    /// <summary>
    /// Section 19, <c>OKLab_to_XYZ</c>.
    /// </summary>
    /// <param name="lab">L, a, b.</param>
    /// <returns>XYZ D65.</returns>
    private static Vector3D OklabToXyz(Vector3D lab)
    {
        Vector3D lms = Multiply(
            lab,
            [
                1, 0.3963377773761749, 0.2158037573099136,
                1, -0.1055613458156586, -0.0638541728258133,
                1, -0.0894841775298119, -1.2914855480194092,
            ]);

        return Multiply(
            lms.Map(v => v * v * v),
            [
                1.2268798758459243, -0.5578149944602171, 0.2813910456659647,
                -0.0405757452148008, 1.1122868032803170, -0.0717110580655164,
                -0.0763729366746601, -0.4214933324022432, 1.5869240198367816,
            ]);
    }

    /// <summary>
    /// Section 19, <c>XYZ_to_OKLab</c>. The cube root keeps the sign, as the sample code notes.
    /// </summary>
    /// <param name="xyz">XYZ D65.</param>
    /// <returns>L, a, b.</returns>
    private static Vector3D XyzToOklab(Vector3D xyz)
    {
        Vector3D lms = Multiply(
            xyz,
            [
                0.8190224379967030, 0.3619062600528904, -0.1288737815209879,
                0.0329836539323885, 0.9292868615863434, 0.0361446663506424,
                0.0481771893596242, 0.2642395317527308, 0.6335478284694309,
            ]);

        return Multiply(
            lms.Map(Math.Cbrt),
            [
                0.2104542683093140, 0.7936177747023054, -0.0040720430116193,
                1.9779985324311684, -2.4285922420485799, 0.4505937096174110,
                0.0259040424655478, 0.7827717124575296, -0.8086757549230774,
            ]);
    }

    /// <summary>
    /// Converts L, C, hue to L, a, b (section 9.6).
    /// </summary>
    /// <param name="lch">L, C, hue in degrees.</param>
    /// <returns>L, a, b.</returns>
    private static Vector3D PolarToRectangular(Vector3D lch)
    {
        double radians = lch.Z * Math.PI / 180D;
        return new Vector3D(lch.X, lch.Y * Math.Cos(radians), lch.Y * Math.Sin(radians));
    }

    /// <summary>
    /// Converts L, a, b to L, C, hue (section 9.5), the hue in 0 to 360 degrees.
    /// </summary>
    /// <param name="lab">L, a, b.</param>
    /// <returns>L, C, hue in degrees.</returns>
    private static Vector3D RectangularToPolar(Vector3D lab)
    {
        double hue = Math.Atan2(lab.Z, lab.Y) * 180D / Math.PI;
        return new Vector3D(lab.X, Math.Sqrt((lab.Y * lab.Y) + (lab.Z * lab.Z)), hue < 0 ? hue + 360D : hue);
    }

    /// <summary>
    /// Converts HSL to sRGB (section 7.1), without clamping.
    /// </summary>
    /// <param name="hsl">Hue in degrees, saturation and lightness 0 to 100.</param>
    /// <returns>sRGB.</returns>
    private static Vector3D HslToSrgb(Vector3D hsl)
    {
        double hue = hsl.X % 360D;
        if (hue < 0)
        {
            hue += 360D;
        }

        double saturation = hsl.Y / 100D;
        double lightness = hsl.Z / 100D;
        double a = saturation * Math.Min(lightness, 1D - lightness);
        double F(double n)
        {
            double k = (n + (hue / 30D)) % 12D;
            return lightness - (a * Math.Max(-1D, Math.Min(Math.Min(k - 3D, 9D - k), 1D)));
        }

        return new Vector3D(F(0D), F(8D), F(4D));
    }

    /// <summary>
    /// Converts HWB to sRGB (section 8.1): whiteness and blackness that sum to 100% or more give a gray.
    /// </summary>
    /// <param name="hwb">Hue in degrees, whiteness and blackness 0 to 100.</param>
    /// <returns>sRGB.</returns>
    private static Vector3D HwbToSrgb(Vector3D hwb)
    {
        double white = hwb.Y / 100D;
        double black = hwb.Z / 100D;
        if (white + black >= 1D)
        {
            double gray = white / (white + black);
            return new Vector3D(gray, gray, gray);
        }

        Vector3D rgb = HslToSrgb(new Vector3D(hwb.X, 100D, 50D));
        return rgb.Map(v => (v * (1D - white - black)) + white);
    }

    /// <summary>
    /// Converts sRGB to HSL (section 7.2).
    /// </summary>
    /// <param name="rgb">sRGB.</param>
    /// <returns>Hue in degrees, saturation and lightness 0 to 100.</returns>
    private static Vector3D SrgbToHsl(Vector3D rgb)
    {
        double max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        double min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        double hue = 0D;
        double saturation = 0D;
        double lightness = (min + max) / 2D;
        double d = max - min;
        if (d != 0D)
        {
            const double epsilon = 1D / 100000D;
            saturation = Math.Abs(lightness) <= epsilon || Math.Abs(1D - lightness) < epsilon
                ? 0D
                : (max - lightness) / Math.Min(lightness, 1D - lightness);
            hue = max == rgb.X ? ((rgb.Y - rgb.Z) / d) + (rgb.Y < rgb.Z ? 6D : 0D)
                : max == rgb.Y ? ((rgb.Z - rgb.X) / d) + 2D
                : ((rgb.X - rgb.Y) / d) + 4D;
            hue *= 60D;
        }

        // A negative saturation, from a color outside the gamut, turns the hue around (section 7.2).
        if (saturation < 0D)
        {
            hue += 180D;
            saturation = -saturation;
        }

        if (hue >= 360D)
        {
            hue -= 360D;
        }

        return new Vector3D(hue, saturation * 100D, lightness * 100D);
    }

    /// <summary>
    /// Converts sRGB to HWB (section 8.2).
    /// </summary>
    /// <param name="rgb">sRGB.</param>
    /// <returns>Hue in degrees, whiteness and blackness 0 to 100.</returns>
    private static Vector3D SrgbToHwb(Vector3D rgb)
    {
        Vector3D hsl = SrgbToHsl(rgb);
        double white = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        double black = 1D - Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        return new Vector3D(hsl.X, white * 100D, black * 100D);
    }

    /// <summary>
    /// Multiplies a column vector by a 3 x 3 matrix.
    /// </summary>
    /// <param name="v">The vector.</param>
    /// <param name="m">The matrix, row by row: the CSS sample code writes it the same way.</param>
    /// <returns>The product.</returns>
    private static Vector3D Multiply(Vector3D v, ReadOnlySpan<double> m)
        => new(
            (m[0] * v.X) + (m[1] * v.Y) + (m[2] * v.Z),
            (m[3] * v.X) + (m[4] * v.Y) + (m[5] * v.Z),
            (m[6] * v.X) + (m[7] * v.Y) + (m[8] * v.Z));

    /// <summary>
    /// Three components in double precision.
    /// </summary>
    private readonly struct Vector3D
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Vector3D"/> struct.
        /// </summary>
        /// <param name="x">The first component.</param>
        /// <param name="y">The second component.</param>
        /// <param name="z">The third component.</param>
        public Vector3D(double x, double y, double z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        /// <summary>
        /// Gets the first component.
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Gets the second component.
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Gets the third component.
        /// </summary>
        public double Z { get; }

        /// <summary>
        /// Applies a function to each component.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <returns>The result.</returns>
        public Vector3D Map(Func<double, double> function) => new(function(this.X), function(this.Y), function(this.Z));
    }
}
