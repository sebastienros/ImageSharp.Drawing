// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The faces that text with one set of font properties tries, in order: the primary face, and the face for each
/// character that the primary face does not have.
/// </summary>
/// <remarks>
/// The order is the one documented on <see cref="SvgLoadOptions.FontCollection"/>: the entries of the
/// <c>font-family</c> list, the fallback families, the default family, then every family of the collection in ordinal
/// order of the names. The last part is built only when a character needs it, because it can load every face of a
/// large system collection. Answers are cached per code point.
/// </remarks>
internal sealed class SvgFontChain
{
    private readonly SvgFontMatcher matcher;
    private readonly SvgFontKey key;
    private readonly List<SvgFontFace> faces = [];
    private readonly Dictionary<int, SvgFontFace?> byCodePoint = [];
    private readonly HashSet<SvgFontFace> seen = [];
    private bool collectionAdded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFontChain"/> class and resolves the explicit part of the order.
    /// </summary>
    /// <param name="matcher">The matcher of the load.</param>
    /// <param name="key">The font properties.</param>
    public SvgFontChain(SvgFontMatcher matcher, SvgFontKey key)
    {
        this.matcher = matcher;
        this.key = key;

        foreach (SvgFontFamilyName entry in key.Families)
        {
            this.AddFamily(entry);
        }

        int listed = this.faces.Count;
        foreach (SvgFontFamilyName entry in matcher.FallbackFamilies)
        {
            this.AddFamily(entry);
        }

        // The default family is the primary family when no entry of the list matched.
        int beforeDefault = this.faces.Count;
        this.AddFamily(matcher.DefaultFamily);
        if (listed == 0 && this.faces.Count > beforeDefault)
        {
            SvgFontFace primary = this.faces[^1];
            this.faces.RemoveAt(this.faces.Count - 1);
            this.faces.Insert(0, primary);
        }

        if (this.faces.Count == 0)
        {
            // Without any match, the first family of the collection in name order is the primary family.
            this.AddCollection();
        }

        this.Primary = this.faces.Count > 0 ? this.faces[0] : null;
    }

    /// <summary>
    /// Gets the primary face, or <see langword="null"/> when the collection has no font.
    /// </summary>
    public SvgFontFace? Primary { get; }

    /// <summary>
    /// Returns the first face of the order that has a glyph for a code point.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The face, or <see langword="null"/> when no face of the collection has a glyph.</returns>
    public SvgFontFace? GetFace(int codePoint)
    {
        if (this.Primary is null)
        {
            return null;
        }

        if (this.Primary.HasGlyph(codePoint))
        {
            return this.Primary;
        }

        if (this.byCodePoint.TryGetValue(codePoint, out SvgFontFace? cached))
        {
            return cached;
        }

        SvgFontFace? found = this.Find(codePoint, 1);
        if (found is null && !this.collectionAdded)
        {
            int start = this.faces.Count;
            this.AddCollection();
            found = this.Find(codePoint, start);
        }

        this.byCodePoint.Add(codePoint, found);
        return found;
    }

    /// <summary>
    /// Returns whether a face of the order from an index has a glyph for every given code point.
    /// </summary>
    /// <param name="face">The face to test.</param>
    /// <param name="codePoints">The code points.</param>
    /// <returns><see langword="true"/> if the face has every glyph; otherwise, <see langword="false"/>.</returns>
    public static bool HasGlyphs(SvgFontFace face, ReadOnlySpan<int> codePoints)
    {
        foreach (int codePoint in codePoints)
        {
            if (!face.HasGlyph(codePoint))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the first face that has every code point of a cluster, trying the faces in order.
    /// </summary>
    /// <param name="codePoints">The code points of the cluster that need a glyph.</param>
    /// <returns>The face, or <see langword="null"/> when no face has all of them.</returns>
    public SvgFontFace? GetFace(ReadOnlySpan<int> codePoints)
    {
        if (codePoints.Length == 1)
        {
            return this.GetFace(codePoints[0]);
        }

        if (this.Primary is null)
        {
            return null;
        }

        for (int i = 0; i < this.faces.Count; i++)
        {
            if (HasGlyphs(this.faces[i], codePoints))
            {
                return this.faces[i];
            }
        }

        if (!this.collectionAdded)
        {
            int start = this.faces.Count;
            this.AddCollection();
            for (int i = start; i < this.faces.Count; i++)
            {
                if (HasGlyphs(this.faces[i], codePoints))
                {
                    return this.faces[i];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first face from an index that has a glyph for a code point.
    /// </summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="start">The index of the first face to try.</param>
    /// <returns>The face, or <see langword="null"/>.</returns>
    private SvgFontFace? Find(int codePoint, int start)
    {
        for (int i = start; i < this.faces.Count; i++)
        {
            if (this.faces[i].HasGlyph(codePoint))
            {
                return this.faces[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the selected face of a family entry, unless the entry matches nothing or the face is already in the order.
    /// </summary>
    /// <param name="entry">The entry.</param>
    private void AddFamily(SvgFontFamilyName entry)
    {
        SvgFontFace[] candidates = this.matcher.GetFaces(entry);
        if (candidates.Length == 0)
        {
            return;
        }

        SvgFontFace face = SvgFontMatcher.SelectFace(candidates, this.key.Weight, this.key.Style, this.key.Stretch);
        if (this.seen.Add(face))
        {
            this.faces.Add(face);
        }
    }

    /// <summary>
    /// Adds the selected face of every family of the collection, in ordinal order of the names.
    /// </summary>
    private void AddCollection()
    {
        this.collectionAdded = true;
        foreach (string name in this.matcher.GetFamilyNames())
        {
            this.AddFamily(new SvgFontFamilyName(name));
        }
    }
}
