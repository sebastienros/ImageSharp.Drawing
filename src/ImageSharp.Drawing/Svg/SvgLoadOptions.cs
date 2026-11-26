// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Options that control how an SVG document loads.
/// </summary>
public sealed class SvgLoadOptions
{
    /// <summary>
    /// The default of <see cref="Languages"/>. It is read-only, so every instance can share it.
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultLanguages = Array.AsReadOnly<string>(["en"]);

    private float dpi = 96F;
    private SvgLimits limits = new();
    private IReadOnlyList<string> languages = DefaultLanguages;
    private string? resourceBaseDirectory;
    private SvgGenericFontFamilies? genericFontFamilies;
    private string defaultFontFamily = "serif";
    private IReadOnlyList<string> fallbackFontFamilies = [];

    /// <summary>
    /// Gets or sets the resolution, in pixels per inch, that converts absolute units such as <c>in</c>, <c>cm</c>,
    /// <c>mm</c>, <c>pt</c>, and <c>pc</c> to pixels. The default is 96.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number greater than zero.</exception>
    public float Dpi
    {
        get => this.dpi;
        set
        {
            Guard.MustBeGreaterThan(value, 0F, nameof(this.Dpi));
            Guard.MustBeLessThan(value, float.PositiveInfinity, nameof(this.Dpi));
            this.dpi = value;
        }
    }

    /// <summary>
    /// Gets or sets the initial value of the <c>color</c> property, which <c>currentColor</c> refers to when the
    /// document does not set <c>color</c>. The default is black.
    /// </summary>
    public Color CurrentColor { get; set; } = Color.Black;

    /// <summary>
    /// Gets or sets the input limits.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SvgLimits Limits
    {
        get => this.limits;
        set
        {
            Guard.NotNull(value, nameof(this.Limits));
            this.limits = value;
        }
    }

    /// <summary>
    /// Gets or sets CSS text that applies to the document as a user style sheet. Its rules have user origin: they rank
    /// below the rules of the document for normal declarations and above them for <c>!important</c> declarations.
    /// <see langword="null"/> or an empty string means no user style sheet. The default is <see langword="null"/>.
    /// </summary>
    public string? UserStyleSheet { get; set; }

    /// <summary>
    /// Gets or sets the language tags that the <c>systemLanguage</c> attribute matches, most preferred first. A listed
    /// language in the attribute matches a tag that is equal to it, ignoring case. It also matches when one of the two
    /// starts with the other followed by <c>-</c>, so <c>en</c> matches <c>en-US</c> and <c>en-US</c> matches <c>en</c>.
    /// The default is <c>en</c>; it does not follow the culture of the machine, so the output is the same everywhere.
    /// An empty list matches no language.
    /// </summary>
    /// <remarks>
    /// Setting the property copies the list, so later changes to the source list have no effect.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty tag.</exception>
    public IReadOnlyList<string> Languages
    {
        get => this.languages;
        set
        {
            Guard.NotNull(value, nameof(this.Languages));
            string[] copy = new string[value.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                string tag = value[i];
                if (string.IsNullOrEmpty(tag))
                {
                    throw new ArgumentException("A language tag must not be null or empty.", nameof(this.Languages));
                }

                copy[i] = tag;
            }

            this.languages = Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Gets or sets the directory that file references of <c>image</c> elements resolve against. A reference that is
    /// absolute, uses a URL scheme other than <c>data:</c>, or leaves the directory does not load. The default is
    /// <see langword="null"/>: no file is read, and only <c>data:</c> URIs load. <see cref="SvgDocument.Load(string)"/>
    /// does not set it, so reading other files is always an explicit choice of the caller.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty or consists only of white space.</exception>
    public string? ResourceBaseDirectory
    {
        get => this.resourceBaseDirectory;
        set
        {
            if (value is not null)
            {
                Guard.NotNullOrWhiteSpace(value, nameof(this.ResourceBaseDirectory));
            }

            this.resourceBaseDirectory = value;
        }
    }

    /// <summary>
    /// Gets or sets the fonts that text in the document uses. The default is <see langword="null"/>, which means the
    /// system fonts (<see cref="SystemFonts.Collection"/>); they are looked up only when the document has text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fonts come from this collection only. The document never causes a font file to be read: <c>@font-face</c> rules
    /// are ignored. A family name matches the family of that name in the collection, ignoring case; the faces of a family
    /// also include the faces whose typographic family name (OpenType name ID 16) is that name, so <c>font-weight</c>,
    /// <c>font-style</c>, and <c>font-stretch</c> select among them with the CSS font matching rules.
    /// </para>
    /// <para>
    /// The font of a piece of text is the first match in this order:
    /// </para>
    /// <list type="number">
    /// <item><description>Each entry of its <c>font-family</c> list, in order. A generic name (<c>serif</c>,
    /// <c>sans-serif</c>, <c>monospace</c>, <c>cursive</c>, <c>fantasy</c>) matches the first family of
    /// <see cref="GenericFontFamilies"/> for that name that the collection has.</description></item>
    /// <item><description><see cref="DefaultFontFamily"/>.</description></item>
    /// <item><description>The first family of the collection in ordinal order of the family names.</description></item>
    /// </list>
    /// <para>
    /// A character that this font has no glyph for takes the first font that has one, from: the remaining entries of the
    /// <c>font-family</c> list, then <see cref="FallbackFontFamilies"/> in order, then <see cref="DefaultFontFamily"/>,
    /// then every family of the collection in ordinal order of the family names. When no font has a glyph, the first font
    /// draws its missing glyph. When the collection has no font at all, text draws nothing; the document still loads.
    /// </para>
    /// </remarks>
    public IReadOnlyFontCollection? FontCollection { get; set; }

    /// <summary>
    /// Gets or sets the families that the CSS generic family names map to.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public SvgGenericFontFamilies GenericFontFamilies
    {
        get => this.genericFontFamilies ??= new SvgGenericFontFamilies();
        set
        {
            Guard.NotNull(value, nameof(this.GenericFontFamilies));
            this.genericFontFamilies = value;
        }
    }

    /// <summary>
    /// Gets or sets the family that text uses when its <c>font-family</c> list is empty or none of its entries is in the
    /// <see cref="FontCollection"/>. The value is a family name or a CSS generic family name. The default is
    /// <c>serif</c>, as in browsers.
    /// </summary>
    /// <exception cref="ArgumentException">The value is <see langword="null"/> or empty.</exception>
    public string DefaultFontFamily
    {
        get => this.defaultFontFamily;
        set
        {
            Guard.NotNullOrWhiteSpace(value, nameof(this.DefaultFontFamily));
            this.defaultFontFamily = value;
        }
    }

    /// <summary>
    /// Gets or sets the families, in order, that supply glyphs for characters that the chosen font does not have, after
    /// the remaining entries of the <c>font-family</c> list. The entries are family names or CSS generic family names.
    /// The default is an empty list. Setting the property copies the list.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> FallbackFontFamilies
    {
        get => this.fallbackFontFamilies;
        set => this.fallbackFontFamilies = SvgGenericFontFamilies.Copy(value, nameof(this.FallbackFontFamilies));
    }

    /// <summary>
    /// Gets the generic family map without creating one: the shared default when the caller set none.
    /// </summary>
    internal SvgGenericFontFamilies GenericFontFamiliesOrDefault => this.genericFontFamilies ?? SvgGenericFontFamilies.Default;
}
