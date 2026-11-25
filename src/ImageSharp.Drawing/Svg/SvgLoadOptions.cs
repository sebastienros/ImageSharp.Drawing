// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
}
