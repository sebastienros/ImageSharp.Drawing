// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Xml;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The element, attribute, property, and namespace names that the reader knows, and the maps from atomized names to ids.
/// </summary>
/// <remarks>
/// The reader pre-seeds its <see cref="XmlNameTable"/> with these constants. <see cref="NameTable.Add(string)"/> stores the
/// first instance of each name, and C# string literals are interned, so every name that the XML reader returns for a known
/// name is the same instance as the constant here. The maps therefore compare keys by reference, which is cheaper than
/// hashing the characters. They must only be queried with names that come from a seeded name table.
/// </remarks>
internal static class SvgNames
{
    /// <summary>The SVG namespace.</summary>
    public const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>The XLink namespace.</summary>
    public const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    /// <summary>The <c>svg</c> element name.</summary>
    public const string Svg = "svg";

    /// <summary>The <c>g</c> element name.</summary>
    public const string G = "g";

    /// <summary>The <c>defs</c> element name.</summary>
    public const string Defs = "defs";

    /// <summary>The <c>use</c> element name.</summary>
    public const string Use = "use";

    /// <summary>The <c>symbol</c> element name.</summary>
    public const string Symbol = "symbol";

    /// <summary>The <c>path</c> element name.</summary>
    public const string Path = "path";

    /// <summary>The <c>rect</c> element name.</summary>
    public const string Rect = "rect";

    /// <summary>The <c>circle</c> element name.</summary>
    public const string Circle = "circle";

    /// <summary>The <c>ellipse</c> element name.</summary>
    public const string Ellipse = "ellipse";

    /// <summary>The <c>line</c> element name.</summary>
    public const string Line = "line";

    /// <summary>The <c>polyline</c> element name.</summary>
    public const string Polyline = "polyline";

    /// <summary>The <c>polygon</c> element name.</summary>
    public const string Polygon = "polygon";

    /// <summary>The <c>linearGradient</c> element name.</summary>
    public const string LinearGradient = "linearGradient";

    /// <summary>The <c>radialGradient</c> element name.</summary>
    public const string RadialGradient = "radialGradient";

    /// <summary>The <c>stop</c> element name.</summary>
    public const string Stop = "stop";

    /// <summary>The <c>style</c> element and attribute name.</summary>
    public const string Style = "style";

    /// <summary>The <c>id</c> attribute name.</summary>
    public const string Id = "id";

    /// <summary>The <c>class</c> attribute name.</summary>
    public const string Class = "class";

    /// <summary>The <c>transform</c> attribute name.</summary>
    public const string Transform = "transform";

    /// <summary>The <c>href</c> attribute name.</summary>
    public const string Href = "href";

    /// <summary>The <c>color</c> property name.</summary>
    public const string Color = "color";

    /// <summary>The <c>display</c> property name.</summary>
    public const string Display = "display";

    /// <summary>The <c>visibility</c> property name.</summary>
    public const string Visibility = "visibility";

    /// <summary>The <c>opacity</c> property name.</summary>
    public const string Opacity = "opacity";

    /// <summary>The <c>fill</c> property name.</summary>
    public const string Fill = "fill";

    /// <summary>The <c>fill-opacity</c> property name.</summary>
    public const string FillOpacity = "fill-opacity";

    /// <summary>The <c>fill-rule</c> property name.</summary>
    public const string FillRule = "fill-rule";

    /// <summary>The <c>stroke</c> property name.</summary>
    public const string Stroke = "stroke";

    /// <summary>The <c>stroke-opacity</c> property name.</summary>
    public const string StrokeOpacity = "stroke-opacity";

    /// <summary>The <c>stroke-width</c> property name.</summary>
    public const string StrokeWidth = "stroke-width";

    /// <summary>The <c>stroke-linecap</c> property name.</summary>
    public const string StrokeLineCap = "stroke-linecap";

    /// <summary>The <c>stroke-linejoin</c> property name.</summary>
    public const string StrokeLineJoin = "stroke-linejoin";

    /// <summary>The <c>stroke-miterlimit</c> property name.</summary>
    public const string StrokeMiterLimit = "stroke-miterlimit";

    /// <summary>The <c>stroke-dasharray</c> property name.</summary>
    public const string StrokeDashArray = "stroke-dasharray";

    /// <summary>The <c>stroke-dashoffset</c> property name.</summary>
    public const string StrokeDashOffset = "stroke-dashoffset";

    /// <summary>The <c>font-size</c> property name.</summary>
    public const string FontSize = "font-size";

    /// <summary>The <c>stop-color</c> property name.</summary>
    public const string StopColor = "stop-color";

    /// <summary>The <c>stop-opacity</c> property name.</summary>
    public const string StopOpacity = "stop-opacity";

    /// <summary>
    /// Gets the map from atomized SVG element name to element kind.
    /// </summary>
    public static Dictionary<object, SvgElementKind> ElementKinds { get; } = new(ReferenceEqualityComparer.Instance)
    {
        [Svg] = SvgElementKind.Svg,
        [G] = SvgElementKind.G,
        [Defs] = SvgElementKind.Defs,
        [Use] = SvgElementKind.Use,
        [Symbol] = SvgElementKind.Symbol,
        [Path] = SvgElementKind.Path,
        [Rect] = SvgElementKind.Rect,
        [Circle] = SvgElementKind.Circle,
        [Ellipse] = SvgElementKind.Ellipse,
        [Line] = SvgElementKind.Line,
        [Polyline] = SvgElementKind.Polyline,
        [Polygon] = SvgElementKind.Polygon,
        [LinearGradient] = SvgElementKind.LinearGradient,
        [RadialGradient] = SvgElementKind.RadialGradient,
        [Stop] = SvgElementKind.Stop,
        [Style] = SvgElementKind.Style,
        ["a"] = SvgElementKind.A,
        ["clipPath"] = SvgElementKind.ClipPath,
        ["mask"] = SvgElementKind.Mask,
        ["pattern"] = SvgElementKind.Pattern,
        ["marker"] = SvgElementKind.Marker,
        ["image"] = SvgElementKind.Image,
        ["switch"] = SvgElementKind.Switch,
        ["text"] = SvgElementKind.Text,
        ["tspan"] = SvgElementKind.TSpan,
        ["textPath"] = SvgElementKind.TextPath,
        ["filter"] = SvgElementKind.Filter,
        ["foreignObject"] = SvgElementKind.ForeignObject,
        ["title"] = SvgElementKind.Title,
        ["desc"] = SvgElementKind.Desc,
        ["metadata"] = SvgElementKind.Metadata,
    };

    /// <summary>
    /// Gets the map from atomized attribute name, in no namespace, to attribute id.
    /// </summary>
    public static Dictionary<object, SvgAttributeId> Attributes { get; } = new(ReferenceEqualityComparer.Instance)
    {
        [Id] = SvgAttributeId.Id,
        [Class] = SvgAttributeId.Class,
        [Style] = SvgAttributeId.Style,
        [Transform] = SvgAttributeId.Transform,
        ["x"] = SvgAttributeId.X,
        ["y"] = SvgAttributeId.Y,
        ["width"] = SvgAttributeId.Width,
        ["height"] = SvgAttributeId.Height,
        ["rx"] = SvgAttributeId.Rx,
        ["ry"] = SvgAttributeId.Ry,
        ["cx"] = SvgAttributeId.Cx,
        ["cy"] = SvgAttributeId.Cy,
        ["r"] = SvgAttributeId.R,
        ["fx"] = SvgAttributeId.Fx,
        ["fy"] = SvgAttributeId.Fy,
        ["fr"] = SvgAttributeId.Fr,
        ["x1"] = SvgAttributeId.X1,
        ["y1"] = SvgAttributeId.Y1,
        ["x2"] = SvgAttributeId.X2,
        ["y2"] = SvgAttributeId.Y2,
        ["points"] = SvgAttributeId.Points,
        ["d"] = SvgAttributeId.D,
        ["viewBox"] = SvgAttributeId.ViewBox,
        ["preserveAspectRatio"] = SvgAttributeId.PreserveAspectRatio,
        [Href] = SvgAttributeId.Href,
        ["offset"] = SvgAttributeId.Offset,
        ["gradientUnits"] = SvgAttributeId.GradientUnits,
        ["gradientTransform"] = SvgAttributeId.GradientTransform,
        ["spreadMethod"] = SvgAttributeId.SpreadMethod,
        ["type"] = SvgAttributeId.Type,
    };

    /// <summary>
    /// Gets the map from atomized presentation attribute name to property. Presentation attribute names are
    /// case-sensitive, as XML names are.
    /// </summary>
    public static Dictionary<object, SvgPropertyId> PresentationAttributes { get; } = CreatePresentationAttributes();

    /// <summary>
    /// Creates a name table that holds every known name, so that the XML reader returns the constant instances.
    /// </summary>
    /// <returns>The seeded name table.</returns>
    public static NameTable CreateNameTable()
    {
        NameTable table = new();
        _ = table.Add(SvgNamespace);
        _ = table.Add(XLinkNamespace);
        foreach (object name in ElementKinds.Keys)
        {
            _ = table.Add((string)name);
        }

        foreach (object name in Attributes.Keys)
        {
            _ = table.Add((string)name);
        }

        foreach (object name in PresentationAttributes.Keys)
        {
            _ = table.Add((string)name);
        }

        return table;
    }

    /// <summary>
    /// Creates the presentation attribute map from the property names.
    /// </summary>
    /// <returns>The map.</returns>
    private static Dictionary<object, SvgPropertyId> CreatePresentationAttributes()
    {
        Dictionary<object, SvgPropertyId> map = new(ReferenceEqualityComparer.Instance);
        foreach (string name in SvgPropertyParser.PropertyNames)
        {
            _ = SvgPropertyParser.TryGetProperty(name, out SvgPropertyId property);
            map[name] = property;
        }

        return map;
    }
}
