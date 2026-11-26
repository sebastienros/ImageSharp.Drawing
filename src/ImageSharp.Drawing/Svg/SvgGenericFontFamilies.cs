// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The font families that the CSS generic family names of an SVG document map to. Each generic name has a list of
/// family names, tried in order; the first family that the font collection has is the family of the generic name.
/// </summary>
/// <remarks>
/// <para>
/// The defaults list families that are common on Windows, macOS, and Linux, so that most system font collections
/// resolve each generic name. The result depends only on the font collection: a collection that has none of the
/// listed families resolves the generic name to nothing, and font matching continues with the next family of the
/// <c>font-family</c> list (see <see cref="SvgLoadOptions.FontCollection"/>).
/// </para>
/// <para>
/// Setting a property copies the list, so later changes to the source list have no effect.
/// </para>
/// </remarks>
public sealed class SvgGenericFontFamilies
{
    // The default lists are read-only, so every instance shares them.
    private static readonly IReadOnlyList<string> DefaultSerif = Copy(["Times New Roman", "Times", "Noto Serif", "DejaVu Serif", "Liberation Serif"]);
    private static readonly IReadOnlyList<string> DefaultSansSerif = Copy(["Arial", "Helvetica", "Noto Sans", "DejaVu Sans", "Liberation Sans"]);
    private static readonly IReadOnlyList<string> DefaultMonospace = Copy(["Courier New", "Courier", "Noto Mono", "DejaVu Sans Mono", "Liberation Mono"]);
    private static readonly IReadOnlyList<string> DefaultCursive = Copy(["Comic Sans MS", "Apple Chancery", "URW Chancery L"]);
    private static readonly IReadOnlyList<string> DefaultFantasy = Copy(["Impact", "Papyrus", "URW Bookman"]);

    private IReadOnlyList<string> serif = DefaultSerif;
    private IReadOnlyList<string> sansSerif = DefaultSansSerif;
    private IReadOnlyList<string> monospace = DefaultMonospace;
    private IReadOnlyList<string> cursive = DefaultCursive;
    private IReadOnlyList<string> fantasy = DefaultFantasy;

    /// <summary>
    /// Gets an instance with the default families. It is never changed, so the resolve stage can share it.
    /// </summary>
    internal static SvgGenericFontFamilies Default { get; } = new();

    /// <summary>
    /// Gets or sets the families of <c>serif</c>. The default is Times New Roman, Times, Noto Serif, DejaVu Serif, and
    /// Liberation Serif.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> Serif
    {
        get => this.serif;
        set => this.serif = Copy(value, nameof(this.Serif));
    }

    /// <summary>
    /// Gets or sets the families of <c>sans-serif</c>. The default is Arial, Helvetica, Noto Sans, DejaVu Sans, and
    /// Liberation Sans.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> SansSerif
    {
        get => this.sansSerif;
        set => this.sansSerif = Copy(value, nameof(this.SansSerif));
    }

    /// <summary>
    /// Gets or sets the families of <c>monospace</c>. The default is Courier New, Courier, Noto Mono, DejaVu Sans Mono,
    /// and Liberation Mono.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> Monospace
    {
        get => this.monospace;
        set => this.monospace = Copy(value, nameof(this.Monospace));
    }

    /// <summary>
    /// Gets or sets the families of <c>cursive</c>. The default is Comic Sans MS, Apple Chancery, and URW Chancery L.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> Cursive
    {
        get => this.cursive;
        set => this.cursive = Copy(value, nameof(this.Cursive));
    }

    /// <summary>
    /// Gets or sets the families of <c>fantasy</c>. The default is Impact, Papyrus, and URW Bookman.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a <see langword="null"/> or empty name.</exception>
    public IReadOnlyList<string> Fantasy
    {
        get => this.fantasy;
        set => this.fantasy = Copy(value, nameof(this.Fantasy));
    }

    /// <summary>
    /// Returns the families of a generic name.
    /// </summary>
    /// <param name="generic">The generic family; not <see cref="SvgGenericFontFamily.None"/>.</param>
    /// <returns>The family names.</returns>
    internal IReadOnlyList<string> Get(SvgGenericFontFamily generic)
        => generic switch
        {
            SvgGenericFontFamily.Serif => this.serif,
            SvgGenericFontFamily.SansSerif => this.sansSerif,
            SvgGenericFontFamily.Monospace => this.monospace,
            SvgGenericFontFamily.Cursive => this.cursive,
            _ => this.fantasy
        };

    /// <summary>
    /// Copies a list of family names into a read-only list, checking every name.
    /// </summary>
    /// <param name="value">The names.</param>
    /// <param name="name">The property name, for the exception.</param>
    /// <returns>The copy.</returns>
    internal static IReadOnlyList<string> Copy(IReadOnlyList<string> value, string name = "value")
    {
        Guard.NotNull(value, name);
        string[] copy = new string[value.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            string family = value[i];
            if (string.IsNullOrEmpty(family))
            {
                throw new ArgumentException("A font family name must not be null or empty.", name);
            }

            copy[i] = family;
        }

        return Array.AsReadOnly(copy);
    }
}
