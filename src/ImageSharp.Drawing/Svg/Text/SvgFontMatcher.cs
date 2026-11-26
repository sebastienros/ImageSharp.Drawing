// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers.Binary;
using System.Globalization;
using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;
using SixLabors.Fonts.WellKnownIds;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 2 of the text layout pipeline, the font part: matches computed font properties to faces of the font collection
/// and gives the fallback order for characters that a face does not have.
/// </summary>
/// <remarks>
/// <para>
/// One matcher serves one document load. It is created on the first text that the resolve stage lays out, so documents
/// without text never touch the font collection. It caches every lookup, because the collection lookups of
/// SixLabors.Fonts scan the collection. It is not thread-safe; the resolve stage is single-threaded.
/// </para>
/// <para>
/// The order in which families are tried is documented on <see cref="SvgLoadOptions.FontCollection"/>. Within a family,
/// <see cref="SelectFace"/> applies the CSS Fonts Level 4 matching rules for <c>font-stretch</c>, <c>font-style</c>, and
/// <c>font-weight</c>, in that order. Bold and italic that the selected face lacks are synthesized by SixLabors.Fonts,
/// as Chromium synthesizes them (see <see cref="GetFont"/>).
/// </para>
/// </remarks>
internal sealed class SvgFontMatcher
{
    private static readonly Tag Os2Tag = Tag.Parse("OS/2");

    private readonly SvgLoadOptions options;
    private readonly SvgGenericFontFamilies genericFamilies;
    private readonly SvgFontFamilyName defaultFamily;
    private readonly SvgFontFamilyName[] fallbackFamilies;
    private readonly Dictionary<string, SvgFontFace[]> facesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SvgFontKey, SvgFontChain> chains = [];
    private readonly Dictionary<(SvgFontFace Face, float Size, FontStyle Style), Font> fonts = [];
    private string[]? familyNames;
    private IReadOnlyFontCollection? collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontMatcher"/> class.
    /// </summary>
    /// <param name="options">The load options that name the collection and the families.</param>
    public SvgFontMatcher(SvgLoadOptions options)
    {
        this.options = options;
        this.genericFamilies = options.GenericFontFamiliesOrDefault;
        this.defaultFamily = ParseFamilyOption(options.DefaultFontFamily);
        IReadOnlyList<string> fallback = options.FallbackFontFamilies;
        this.fallbackFamilies = new SvgFontFamilyName[fallback.Count];
        for (int i = 0; i < fallback.Count; i++)
        {
            this.fallbackFamilies[i] = ParseFamilyOption(fallback[i]);
        }
    }

    /// <summary>
    /// Gets the default family of the options.
    /// </summary>
    internal SvgFontFamilyName DefaultFamily => this.defaultFamily;

    /// <summary>
    /// Gets the fallback families of the options.
    /// </summary>
    internal ReadOnlySpan<SvgFontFamilyName> FallbackFamilies => this.fallbackFamilies;

    /// <summary>
    /// Gets the font collection: the collection of the options, or the system fonts, which are looked up here on first use.
    /// </summary>
    private IReadOnlyFontCollection Collection => this.collection ??= this.options.FontCollection ?? SystemFonts.Collection;

    /// <summary>
    /// Returns the fallback chain for a text style: the faces to try, in order, for each character.
    /// </summary>
    /// <param name="style">The computed text style.</param>
    /// <returns>The chain, shared by every style with the same font properties.</returns>
    public SvgFontChain GetChain(SvgTextStyle style)
    {
        SvgFontKey key = new(style.FontFamily, style.FontWeight, style.FontStyle, style.FontStretch);
        if (!this.chains.TryGetValue(key, out SvgFontChain? chain))
        {
            chain = new SvgFontChain(this, key);
            this.chains.Add(key, chain);
        }

        return chain;
    }

    /// <summary>
    /// Returns the font of a face at a size, with the bold and italic synthesis that the requested properties need.
    /// </summary>
    /// <remarks>
    /// SixLabors.Fonts synthesizes a bold weight when the requested style has <see cref="FontStyle.Bold"/> and the resolved
    /// face is lighter than 600, and an oblique slant when the requested style has <see cref="FontStyle.Italic"/> and the
    /// face is not italic, as Chromium does. The face is reached through its own style slot, so adding a flag that the
    /// slot lacks keeps the face and turns synthesis on.
    /// </remarks>
    /// <param name="face">The face.</param>
    /// <param name="size">The font size in pixels.</param>
    /// <param name="weight">The requested weight.</param>
    /// <param name="fontStyle">The requested <c>font-style</c>.</param>
    /// <returns>The font.</returns>
    public Font GetFont(SvgFontFace face, float size, float weight, SvgFontStyle fontStyle)
    {
        FontStyle style = face.Style;
        if (weight >= 600F && face.Weight < 600F)
        {
            style |= FontStyle.Bold;
        }

        if (fontStyle != SvgFontStyle.Normal && !face.Italic)
        {
            style |= FontStyle.Italic;
        }

        if (!this.fonts.TryGetValue((face, size, style), out Font? font))
        {
            font = face.Family.CreateFont(size, style);
            this.fonts.Add((face, size, style), font);
        }

        return font;
    }

    /// <summary>
    /// Returns the faces of a family name: the faces of the family of that name, and the faces whose typographic family
    /// name (name ID 16) is that name.
    /// </summary>
    /// <param name="name">The family name.</param>
    /// <returns>The faces; empty when the collection has no such family.</returns>
    internal SvgFontFace[] GetFaces(string name)
    {
        if (this.facesByName.TryGetValue(name, out SvgFontFace[]? faces))
        {
            return faces;
        }

        List<SvgFontFace> found = [];
        if (this.Collection.TryGet(name, out FontFamily family))
        {
            AddFaces(family, found);
        }

        // Faces of a large family often have legacy family names such as "Noto Sans Light", with the shared name in the
        // typographic family name. Only families whose name starts with the requested name can have it.
        foreach (string candidate in this.GetFamilyNames())
        {
            if (candidate.Length > name.Length
                && candidate[name.Length] == ' '
                && candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && this.Collection.TryGet(candidate, out FontFamily member))
            {
                int start = found.Count;
                AddFaces(member, found);
                for (int i = found.Count - 1; i >= start; i--)
                {
                    string typographic = found[i].Metrics.Description.GetNameById(CultureInfo.InvariantCulture, KnownNameIds.TypographicFamilyName);
                    if (!string.Equals(typographic, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found.RemoveAt(i);
                    }
                }
            }
        }

        faces = [.. found];
        this.facesByName.Add(name, faces);
        return faces;
    }

    /// <summary>
    /// Returns the faces of an entry of a family list: a generic name takes the first of its families that the collection
    /// has.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The faces; empty when nothing matches.</returns>
    internal SvgFontFace[] GetFaces(SvgFontFamilyName entry)
    {
        if (entry.Generic == SvgGenericFontFamily.None)
        {
            return this.GetFaces(entry.Name!);
        }

        foreach (string name in this.genericFamilies.Get(entry.Generic))
        {
            SvgFontFace[] faces = this.GetFaces(name);
            if (faces.Length > 0)
            {
                return faces;
            }
        }

        return [];
    }

    /// <summary>
    /// Returns the family names of the collection in ordinal order.
    /// </summary>
    /// <returns>The names.</returns>
    internal string[] GetFamilyNames()
    {
        if (this.familyNames is null)
        {
            List<string> names = [];
            foreach (FontFamily family in this.Collection.Families)
            {
                names.Add(family.Name);
            }

            names.Sort(StringComparer.Ordinal);
            this.familyNames = [.. names];
        }

        return this.familyNames;
    }

    /// <summary>
    /// Selects the face of a family that CSS font matching picks for the requested properties.
    /// </summary>
    /// <remarks>
    /// CSS Fonts Level 4, section 5.2: first the width (an exact width, else for a request at or below 100% the narrower
    /// widths from the widest, then the wider ones; above 100% the other way round), then the style (italic prefers
    /// italic; normal prefers normal; SixLabors.Fonts does not tell oblique from italic), then the weight (an exact
    /// weight; between 400 and 500 the weights up to 500 ascending, then below descending, then above 500 ascending;
    /// below 400 the lighter weights descending, then the heavier ascending; above 500 the heavier ascending, then the
    /// lighter descending).
    /// </remarks>
    /// <param name="faces">The faces of the family; not empty.</param>
    /// <param name="weight">The requested weight.</param>
    /// <param name="style">The requested style.</param>
    /// <param name="stretch">The requested width as a percentage.</param>
    /// <returns>The face.</returns>
    internal static SvgFontFace SelectFace(SvgFontFace[] faces, float weight, SvgFontStyle style, float stretch)
    {
        float width = faces[0].Width;
        float widthScore = float.MaxValue;
        foreach (SvgFontFace face in faces)
        {
            float score = StretchScore(face.Width, stretch);
            if (score < widthScore)
            {
                widthScore = score;
                width = face.Width;
            }
        }

        bool wantItalic = style != SvgFontStyle.Normal;
        bool hasStyle = false;
        foreach (SvgFontFace face in faces)
        {
            hasStyle |= face.Width == width && face.Italic == wantItalic;
        }

        bool italic = hasStyle ? wantItalic : !wantItalic;
        SvgFontFace? best = null;
        float bestScore = float.MaxValue;
        foreach (SvgFontFace face in faces)
        {
            if (face.Width != width || face.Italic != italic)
            {
                continue;
            }

            float score = WeightScore(face.Weight, weight);
            if (score < bestScore)
            {
                bestScore = score;
                best = face;
            }
        }

        return best ?? faces[0];
    }

    /// <summary>
    /// Scores a face width for a requested width: lower is better, following the CSS search order.
    /// </summary>
    /// <param name="width">The face width.</param>
    /// <param name="requested">The requested width.</param>
    /// <returns>The score.</returns>
    internal static float StretchScore(float width, float requested)
    {
        if (width == requested)
        {
            return 0F;
        }

        bool narrower = width < requested;
        float distance = MathF.Abs(width - requested);

        // The preferred side scores below every width of the other side.
        bool preferred = requested <= 100F ? narrower : !narrower;
        return preferred ? distance : 1000F + distance;
    }

    /// <summary>
    /// Scores a face weight for a requested weight: lower is better, following the CSS search order.
    /// </summary>
    /// <param name="weight">The face weight.</param>
    /// <param name="requested">The requested weight.</param>
    /// <returns>The score.</returns>
    internal static float WeightScore(float weight, float requested)
    {
        if (weight == requested)
        {
            return 0F;
        }

        float distance = MathF.Abs(weight - requested);
        if (requested is >= 400F and <= 500F)
        {
            if (weight > requested && weight <= 500F)
            {
                return distance;
            }

            return weight < requested ? 1000F + distance : 2000F + distance;
        }

        if (requested < 400F)
        {
            return weight < requested ? distance : 1000F + distance;
        }

        return weight > requested ? distance : 1000F + distance;
    }

    /// <summary>
    /// Parses a family option: a CSS generic family name or a family name.
    /// </summary>
    /// <param name="value">The option value.</param>
    /// <returns>The entry.</returns>
    private static SvgFontFamilyName ParseFamilyOption(string value)
    {
        string name = value.Trim();
        return name.ToLowerInvariant() switch
        {
            "serif" => new SvgFontFamilyName(SvgGenericFontFamily.Serif),
            "sans-serif" => new SvgFontFamilyName(SvgGenericFontFamily.SansSerif),
            "monospace" => new SvgFontFamilyName(SvgGenericFontFamily.Monospace),
            "cursive" => new SvgFontFamilyName(SvgGenericFontFamily.Cursive),
            "fantasy" => new SvgFontFamilyName(SvgGenericFontFamily.Fantasy),
            _ => new SvgFontFamilyName(name)
        };
    }

    /// <summary>
    /// Adds the faces of a SixLabors.Fonts family: one per style slot.
    /// </summary>
    /// <param name="family">The family.</param>
    /// <param name="faces">The list that receives the faces.</param>
    private static void AddFaces(FontFamily family, List<SvgFontFace> faces)
    {
        foreach (FontStyle style in family.GetAvailableStyles().Span)
        {
            if (family.TryGetMetrics(style, out FontMetrics? metrics) && !ContainsMetrics(faces, metrics))
            {
                // A face without outlines cannot draw, so it takes no part in matching.
                SvgFontFace face = new(family, style, metrics, ReadWidth(metrics));
                if (face.HasOutlines)
                {
                    faces.Add(face);
                }
            }
        }

        static bool ContainsMetrics(List<SvgFontFace> faces, FontMetrics metrics)
        {
            foreach (SvgFontFace face in faces)
            {
                if (ReferenceEquals(face.Metrics, metrics))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Reads the width class of a face from its OS/2 table and converts it to the percentage of CSS Fonts Level 4.
    /// </summary>
    /// <param name="metrics">The face metrics.</param>
    /// <returns>The width; 100 when the table is missing or the class is out of range.</returns>
    private static float ReadWidth(FontMetrics metrics)
    {
        // usWidthClass is the big-endian 16-bit value at offset 6 of the OS/2 table, after version, xAvgCharWidth, and
        // usWeightClass.
        if (!metrics.TryGetTableData(Os2Tag, out ReadOnlyMemory<byte> table) || table.Length < 8)
        {
            return 100F;
        }

        int widthClass = BinaryPrimitives.ReadUInt16BigEndian(table.Span[6..]);
        return widthClass switch
        {
            1 => 50F,
            2 => 62.5F,
            3 => 75F,
            4 => 87.5F,
            6 => 112.5F,
            7 => 125F,
            8 => 150F,
            9 => 200F,
            _ => 100F
        };
    }
}
