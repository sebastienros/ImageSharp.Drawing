// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

public class SvgColorParserTests
{
    /// <summary>
    /// Gets the named colors of CSS Color Level 4, section 6.1, with their sRGB values.
    /// </summary>
    public static TheoryData<string, string> NamedColors { get; } = new()
    {
        { "aliceblue", "f0f8ff" }, { "antiquewhite", "faebd7" }, { "aqua", "00ffff" }, { "aquamarine", "7fffd4" },
        { "azure", "f0ffff" }, { "beige", "f5f5dc" }, { "bisque", "ffe4c4" }, { "black", "000000" },
        { "blanchedalmond", "ffebcd" }, { "blue", "0000ff" }, { "blueviolet", "8a2be2" }, { "brown", "a52a2a" },
        { "burlywood", "deb887" }, { "cadetblue", "5f9ea0" }, { "chartreuse", "7fff00" }, { "chocolate", "d2691e" },
        { "coral", "ff7f50" }, { "cornflowerblue", "6495ed" }, { "cornsilk", "fff8dc" }, { "crimson", "dc143c" },
        { "cyan", "00ffff" }, { "darkblue", "00008b" }, { "darkcyan", "008b8b" }, { "darkgoldenrod", "b8860b" },
        { "darkgray", "a9a9a9" }, { "darkgreen", "006400" }, { "darkgrey", "a9a9a9" }, { "darkkhaki", "bdb76b" },
        { "darkmagenta", "8b008b" }, { "darkolivegreen", "556b2f" }, { "darkorange", "ff8c00" }, { "darkorchid", "9932cc" },
        { "darkred", "8b0000" }, { "darksalmon", "e9967a" }, { "darkseagreen", "8fbc8f" }, { "darkslateblue", "483d8b" },
        { "darkslategray", "2f4f4f" }, { "darkslategrey", "2f4f4f" }, { "darkturquoise", "00ced1" }, { "darkviolet", "9400d3" },
        { "deeppink", "ff1493" }, { "deepskyblue", "00bfff" }, { "dimgray", "696969" }, { "dimgrey", "696969" },
        { "dodgerblue", "1e90ff" }, { "firebrick", "b22222" }, { "floralwhite", "fffaf0" }, { "forestgreen", "228b22" },
        { "fuchsia", "ff00ff" }, { "gainsboro", "dcdcdc" }, { "ghostwhite", "f8f8ff" }, { "gold", "ffd700" },
        { "goldenrod", "daa520" }, { "gray", "808080" }, { "green", "008000" }, { "greenyellow", "adff2f" },
        { "grey", "808080" }, { "honeydew", "f0fff0" }, { "hotpink", "ff69b4" }, { "indianred", "cd5c5c" },
        { "indigo", "4b0082" }, { "ivory", "fffff0" }, { "khaki", "f0e68c" }, { "lavender", "e6e6fa" },
        { "lavenderblush", "fff0f5" }, { "lawngreen", "7cfc00" }, { "lemonchiffon", "fffacd" }, { "lightblue", "add8e6" },
        { "lightcoral", "f08080" }, { "lightcyan", "e0ffff" }, { "lightgoldenrodyellow", "fafad2" }, { "lightgray", "d3d3d3" },
        { "lightgreen", "90ee90" }, { "lightgrey", "d3d3d3" }, { "lightpink", "ffb6c1" }, { "lightsalmon", "ffa07a" },
        { "lightseagreen", "20b2aa" }, { "lightskyblue", "87cefa" }, { "lightslategray", "778899" }, { "lightslategrey", "778899" },
        { "lightsteelblue", "b0c4de" }, { "lightyellow", "ffffe0" }, { "lime", "00ff00" }, { "limegreen", "32cd32" },
        { "linen", "faf0e6" }, { "magenta", "ff00ff" }, { "maroon", "800000" }, { "mediumaquamarine", "66cdaa" },
        { "mediumblue", "0000cd" }, { "mediumorchid", "ba55d3" }, { "mediumpurple", "9370db" }, { "mediumseagreen", "3cb371" },
        { "mediumslateblue", "7b68ee" }, { "mediumspringgreen", "00fa9a" }, { "mediumturquoise", "48d1cc" }, { "mediumvioletred", "c71585" },
        { "midnightblue", "191970" }, { "mintcream", "f5fffa" }, { "mistyrose", "ffe4e1" }, { "moccasin", "ffe4b5" },
        { "navajowhite", "ffdead" }, { "navy", "000080" }, { "oldlace", "fdf5e6" }, { "olive", "808000" },
        { "olivedrab", "6b8e23" }, { "orange", "ffa500" }, { "orangered", "ff4500" }, { "orchid", "da70d6" },
        { "palegoldenrod", "eee8aa" }, { "palegreen", "98fb98" }, { "paleturquoise", "afeeee" }, { "palevioletred", "db7093" },
        { "papayawhip", "ffefd5" }, { "peachpuff", "ffdab9" }, { "peru", "cd853f" }, { "pink", "ffc0cb" },
        { "plum", "dda0dd" }, { "powderblue", "b0e0e6" }, { "purple", "800080" }, { "rebeccapurple", "663399" },
        { "red", "ff0000" }, { "rosybrown", "bc8f8f" }, { "royalblue", "4169e1" }, { "saddlebrown", "8b4513" },
        { "salmon", "fa8072" }, { "sandybrown", "f4a460" }, { "seagreen", "2e8b57" }, { "seashell", "fff5ee" },
        { "sienna", "a0522d" }, { "silver", "c0c0c0" }, { "skyblue", "87ceeb" }, { "slateblue", "6a5acd" },
        { "slategray", "708090" }, { "slategrey", "708090" }, { "snow", "fffafa" }, { "springgreen", "00ff7f" },
        { "steelblue", "4682b4" }, { "tan", "d2b48c" }, { "teal", "008080" }, { "thistle", "d8bfd8" },
        { "tomato", "ff6347" }, { "turquoise", "40e0d0" }, { "violet", "ee82ee" }, { "wheat", "f5deb3" },
        { "white", "ffffff" }, { "whitesmoke", "f5f5f5" }, { "yellow", "ffff00" }, { "yellowgreen", "9acd32" },
    };

    [Theory]
    [MemberData(nameof(NamedColors))]
    public void TryParse_ReadsEveryCssNamedColor(string name, string hex)
    {
        Rgba32 expected = Rgba(hex);

        Assert.True(SvgColorParser.TryParse(name, null, out Color color));
        Assert.Equal(expected, color.ToPixel<Rgba32>());

        // CSS keywords are ASCII case-insensitive.
        Assert.True(SvgColorParser.TryParse(name.ToUpperInvariant(), null, out Color upper));
        Assert.Equal(expected, upper.ToPixel<Rgba32>());
    }

    [Fact]
    public void TryParse_ReadsTransparentAsTransparentBlack()
    {
        Assert.True(SvgColorParser.TryParse("transparent", null, out Color color));
        Assert.Equal(new Rgba32(0, 0, 0, 0), color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("#f00", 255, 0, 0, 255)]
    [InlineData("#F00", 255, 0, 0, 255)]
    [InlineData("#f008", 255, 0, 0, 0x88)]
    [InlineData("#12aBcD", 0x12, 0xab, 0xcd, 255)]
    [InlineData("#12abcd80", 0x12, 0xab, 0xcd, 0x80)]
    [InlineData("  #000  ", 0, 0, 0, 255)]
    public void TryParse_ReadsHexadecimalNotation(string input, byte r, byte g, byte b, byte a)
    {
        Assert.True(SvgColorParser.TryParse(input, null, out Color color));
        Assert.Equal(new Rgba32(r, g, b, a), color.ToPixel<Rgba32>());
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", 1F, 0F, 0F, 1F)]
    [InlineData("RGB(255,128,0)", 1F, 128F / 255F, 0F, 1F)]
    [InlineData("rgba(255, 0, 0, 0.5)", 1F, 0F, 0F, 0.5F)]
    [InlineData("rgb(255, 0, 0, 50%)", 1F, 0F, 0F, 0.5F)]
    [InlineData("rgb(10%, 20%, 30%)", 0.1F, 0.2F, 0.3F, 1F)]
    [InlineData("rgb(255 0 0)", 1F, 0F, 0F, 1F)]
    [InlineData("rgb(255 0 0 / 0.25)", 1F, 0F, 0F, 0.25F)]
    [InlineData("rgb(100% 0 51 / 50%)", 1F, 0F, 0.2F, 0.5F)]
    [InlineData("rgba(255 0 0)", 1F, 0F, 0F, 1F)]
    [InlineData("rgb(300, -10, 0)", 1F, 0F, 0F, 1F)]
    [InlineData("rgb(0, 0, 0, 2)", 0F, 0F, 0F, 1F)]
    [InlineData("rgb( 0 , 0 , 255 )", 0F, 0F, 1F, 1F)]
    [InlineData("rgb(127.5, 0, 0)", 0.5F, 0F, 0F, 1F)]
    public void TryParse_ReadsRgbFunctions(string input, float r, float g, float b, float a)
        => AssertColor(input, new Vector4(r, g, b, a));

    [Theory]
    [InlineData("hsl(0, 100%, 50%)", 1F, 0F, 0F, 1F)]
    [InlineData("hsl(120, 100%, 50%)", 0F, 1F, 0F, 1F)]
    [InlineData("hsl(240deg 100% 50%)", 0F, 0F, 1F, 1F)]
    [InlineData("hsl(0.5turn 100% 50%)", 0F, 1F, 1F, 1F)]
    [InlineData("hsl(200grad 100% 50%)", 0F, 1F, 1F, 1F)]
    [InlineData("hsl(-120, 100%, 50%)", 0F, 0F, 1F, 1F)]
    [InlineData("hsl(480, 100%, 50%)", 0F, 1F, 0F, 1F)]
    [InlineData("hsl(0, 0%, 50%)", 0.5F, 0.5F, 0.5F, 1F)]
    [InlineData("hsl(0, 100%, 100%)", 1F, 1F, 1F, 1F)]
    [InlineData("hsl(0, 100%, 25%)", 0.5F, 0F, 0F, 1F)]
    [InlineData("hsla(120, 100%, 50%, 0.5)", 0F, 1F, 0F, 0.5F)]
    [InlineData("hsl(120 100 50 / 25%)", 0F, 1F, 0F, 0.25F)]
    public void TryParse_ReadsHslFunctions(string input, float r, float g, float b, float a)
        => AssertColor(input, new Vector4(r, g, b, a));

    [Fact]
    public void TryParse_ReadsHslHueInRadians()
        => AssertColor("hsl(3.14159265rad 100% 50%)", new Vector4(0F, 1F, 1F, 1F));

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#ff")]
    [InlineData("#fffff")]
    [InlineData("#fffffff")]
    [InlineData("#ggg")]
    [InlineData("fff")]
    [InlineData("ff0000")]
    [InlineData("bad")]
    [InlineData("notacolor")]
    [InlineData("red blue")]
    [InlineData("currentColor")]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("rgb(10%, 2, 3)")]
    [InlineData("rgb(1 2 3 4)")]
    [InlineData("rgb(1, 2, 3,)")]
    [InlineData("rgb(1 2 3 /)")]
    [InlineData("rgb(1, 2 3)")]
    [InlineData("rgb (1, 2, 3)")]
    [InlineData("rgb(1deg, 2, 3)")]
    [InlineData("hsl(120, 100, 50)")]
    [InlineData("hsl(120, 100%)")]
    [InlineData("hsl(120%, 100%, 50%)")]
    [InlineData("cmyk(1, 2, 3, 4)")]
    [InlineData("rgb(1, 2, 3")]
    public void TryParse_RejectsInvalidColors(string input)
        => Assert.False(SvgColorParser.TryParse(input, null, out _));

    [Fact]
    public void TryParse_CacheReturnsTheSameColors()
    {
        SvgColorCache cache = new();

        Assert.True(SvgColorParser.TryParse("#336699", cache, out Color first));
        Assert.True(SvgColorParser.TryParse("#336699", cache, out Color second));
        Assert.True(SvgColorParser.TryParse("Navy", cache, out Color navy));
        Assert.True(SvgColorParser.TryParse("Navy", cache, out Color navyAgain));

        Assert.Equal(new Rgba32(0x33, 0x66, 0x99, 255), first.ToPixel<Rgba32>());
        Assert.Equal(first, second);
        Assert.Equal(new Rgba32(0, 0, 0x80, 255), navy.ToPixel<Rgba32>());
        Assert.Equal(navy, navyAgain);
    }

    private static void AssertColor(string input, Vector4 expected)
    {
        Assert.True(SvgColorParser.TryParse(input, null, out Color color));
        Vector4 actual = color.ToScaledVector4();
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
        Assert.Equal(expected.W, actual.W, 3);
    }

    private static Rgba32 Rgba(string hex)
        => new(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16), 255);
}
