// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.Fonts.Unicode;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Represents a unicode string and all associated attributes
/// for each character required for the Bidi algorithm
/// </summary>
/// <remarks>
/// <para>
/// Temporary copy of <c>src/SixLabors.Fonts/Unicode/BidiData.cs</c> of SixLabors.Fonts
/// (https://github.com/SixLabors/Fonts), tag <c>v3.2.1</c> (commit 50dae68), where the type is internal. SVG text
/// layout needs the embedding levels of the Unicode bidirectional algorithm, and SixLabors.Fonts does not expose them.
/// Remove this copy when SixLabors.Fonts exposes the algorithm publicly. The code stays as close to the source as it
/// compiles, so that it can be compared with upstream. Edits: the namespace, these remarks, and a private copy of
/// <see cref="GetCanonicalType"/>, which is internal in <see cref="CodePoint"/>.
/// </para>
/// </remarks>
internal partial class BidiData
{
    /// <summary>
    /// The carriage return code point.
    /// </summary>
    private const int CarriageReturn = 0x000D;

    /// <summary>
    /// The line feed code point.
    /// </summary>
    private const int LineFeed = 0x000A;

    private ArrayBuilder<BidiCharacterType> types;
    private ArrayBuilder<BidiPairedBracketType> pairedBracketTypes;
    private ArrayBuilder<int> pairedBracketValues;
    private ArrayBuilder<BidiCharacterType> savedTypes;
    private ArrayBuilder<BidiPairedBracketType> savedPairedBracketTypes;
    private ArrayBuilder<sbyte> tempLevelBuffer;
    private readonly List<int> paragraphEnds = new();

    public sbyte ParagraphEmbeddingLevel { get; private set; }

    public bool HasBrackets { get; private set; }

    public bool HasEmbeddings { get; private set; }

    public bool HasIsolates { get; private set; }

    /// <summary>
    /// Gets the code point positions immediately after each newline function.
    /// A carriage-return and line-feed pair contributes one position after the pair.
    /// </summary>
    public ReadOnlySpan<int> ParagraphEnds => CollectionsMarshal.AsSpan(this.paragraphEnds);

    /// <summary>
    /// Gets the length of the data held by the BidiData
    /// </summary>
    public int Length => this.types.Length;

    /// <summary>
    /// Gets the bidi character type of each code point
    /// </summary>
    public ArraySlice<BidiCharacterType> Types { get; private set; }

    /// <summary>
    /// Gets a value indicating whether every resolved level is provably zero in a
    /// left-to-right paragraph, allowing the bidirectional algorithm to be skipped.
    /// True only when the text contains no embedding or isolate initiators and no
    /// codepoint whose type can raise an embedding level: strong right-to-left letters
    /// and Arabic numbers force non-zero levels, and boundary neutrals are excluded
    /// conservatively because their resolved levels depend on removed-character
    /// handling. Everything else (L, EN, separators, whitespace, neutrals, NSM) keeps
    /// level zero.
    /// </summary>
    public bool IsUniformLeftToRight
    {
        get
        {
            if (this.HasEmbeddings || this.HasIsolates)
            {
                return false;
            }

            ArraySlice<BidiCharacterType> types = this.Types;
            for (int i = 0; i < types.Length; i++)
            {
                BidiCharacterType type = types[i];
                if (type is BidiCharacterType.RightToLeft
                    or BidiCharacterType.ArabicLetter
                    or BidiCharacterType.ArabicNumber
                    or BidiCharacterType.BoundaryNeutral)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Gets the paired bracket type for each code point
    /// </summary>
    public ArraySlice<BidiPairedBracketType> PairedBracketTypes { get; private set; }

    /// <summary>
    /// Gets the paired bracket value for code point
    /// </summary>
    /// <remarks>
    /// The paired bracket values are the code points
    /// of each character where the opening code point
    /// is replaced with the closing code point for easier
    /// matching.  Also, bracket code points are mapped
    /// to their canonical equivalents
    /// </remarks>
    public ArraySlice<int> PairedBracketValues { get; private set; }

    /// <summary>
    /// Initialize with a text value.
    /// </summary>
    /// <param name="text">The text to process.</param>
    /// <param name="paragraphEmbeddingLevel">The paragraph embedding level</param>
    public void Init(ReadOnlySpan<char> text, sbyte paragraphEmbeddingLevel)
    {
        // Set working buffer sizes
        // TODO: This allocates more than it should for some arrays.
        int length = CodePoint.GetCodePointCount(text);
        this.types.Length = length;
        this.pairedBracketTypes.Length = length;
        this.pairedBracketValues.Length = length;

        this.paragraphEnds.Clear();
        this.ParagraphEmbeddingLevel = paragraphEmbeddingLevel;

        // Resolve the BidiCharacterType, paired bracket type and paired
        // bracket values for all code points
        this.HasBrackets = false;
        this.HasEmbeddings = false;
        this.HasIsolates = false;

        int i = 0;
        bool previousWasCarriageReturn = false;
        var codePointEnumerator = new SpanCodePointEnumerator(text);
        while (codePointEnumerator.MoveNext())
        {
            CodePoint codePoint = codePointEnumerator.Current;

            if (CodePoint.IsNewLine(codePoint))
            {
                if (codePoint.Value == LineFeed && previousWasCarriageReturn)
                {
                    // CRLF is one newline function, so extend the boundary recorded
                    // for CR instead of introducing an empty paragraph between them.
                    this.paragraphEnds[^1] = i + 1;
                }
                else
                {
                    this.paragraphEnds.Add(i + 1);
                }
            }

            previousWasCarriageReturn = codePoint.Value == CarriageReturn;

            // ASCII values answer from the precomputed tables: one classification,
            // one bracket type, and one pairing value per code point instead of the
            // property lookups below. ASCII contains no embedding or isolate
            // initiators, so the flag bookkeeping is skipped as well.
            if (codePoint.IsAscii)
            {
                int asciiValue = codePoint.Value;
                this.types[i] = (BidiCharacterType)AsciiCharacterTypes[asciiValue];

                BidiPairedBracketType asciiPbt = (BidiPairedBracketType)AsciiPairedBracketTypes[asciiValue];
                this.pairedBracketTypes[i] = asciiPbt;
                if (asciiPbt != BidiPairedBracketType.None)
                {
                    this.pairedBracketValues[i] = AsciiPairedBracketValues[asciiValue];
                    this.HasBrackets = true;
                }

                i++;
                continue;
            }

            BidiClass bidi = CodePoint.GetBidiClass(codePoint);

            // Look up BidiCharacterType
            BidiCharacterType dir = bidi.CharacterType;
            this.types[i] = dir;

            switch (dir)
            {
                case BidiCharacterType.LeftToRightEmbedding:
                case BidiCharacterType.LeftToRightOverride:
                case BidiCharacterType.RightToLeftEmbedding:
                case BidiCharacterType.RightToLeftOverride:
                case BidiCharacterType.PopDirectionalFormat:
                    this.HasEmbeddings = true;
                    break;

                case BidiCharacterType.LeftToRightIsolate:
                case BidiCharacterType.RightToLeftIsolate:
                case BidiCharacterType.FirstStrongIsolate:
                case BidiCharacterType.PopDirectionalIsolate:
                    this.HasIsolates = true;
                    break;
            }

            // Lookup paired bracket types
            BidiPairedBracketType pbt = bidi.PairedBracketType;
            this.pairedBracketTypes[i] = pbt;

            if (pbt == BidiPairedBracketType.Open)
            {
                // Opening bracket types can never have a null pairing.
                bidi.TryGetPairedBracket(out CodePoint paired);
                this.pairedBracketValues[i] = GetCanonicalType(paired).Value;

                this.HasBrackets = true;
            }
            else if (pbt == BidiPairedBracketType.Close)
            {
                this.pairedBracketValues[i] = GetCanonicalType(codePoint).Value;
                this.HasBrackets = true;
            }

            i++;
        }

        // Create slices on work buffers
        this.Types = this.types.AsSlice();
        this.PairedBracketTypes = this.pairedBracketTypes.AsSlice();
        this.PairedBracketValues = this.pairedBracketValues.AsSlice();
    }

    /// <summary>
    /// Save the Types and PairedBracketTypes of this bididata
    /// </summary>
    /// <remarks>
    /// This is used when processing embedded style runs with
    /// BidiCharacterType overrides.  TextLayout saves the data,
    /// overrides the style runs to neutral, processes the bidi
    /// data for the entire paragraph and then restores this data
    /// before processing the embedded runs.
    /// </remarks>
    public void SaveTypes()
    {
        // Capture the types data
        this.savedTypes.Clear();
        this.savedTypes.Add(this.types.AsSlice());
        this.savedPairedBracketTypes.Clear();
        this.savedPairedBracketTypes.Add(this.pairedBracketTypes.AsSlice());
    }

    /// <summary>
    /// Restore the data saved by SaveTypes
    /// </summary>
    public void RestoreTypes()
    {
        this.types.Clear();
        this.types.Add(this.savedTypes.AsSlice());
        this.pairedBracketTypes.Clear();
        this.pairedBracketTypes.Add(this.savedPairedBracketTypes.AsSlice());
    }

    /// <summary>
    /// Gets a temporary level buffer. Used by TextLayout when
    /// resolving style runs with different BidiCharacterType.
    /// </summary>
    /// <param name="length">Length of the required ExpandableBuffer</param>
    /// <returns>An uninitialized level ExpandableBuffer</returns>
    public ArraySlice<sbyte> GetTempLevelBuffer(int length)
    {
        this.tempLevelBuffer.Clear();
        return this.tempLevelBuffer.Add(length, false);
    }

    /// <summary>
    /// Gets the canonical representation of a given codepoint.
    /// <see href="http://www.unicode.org/L2/L2013/13123-norm-and-bpa.pdf"/>
    /// </summary>
    /// <remarks>
    /// A copy of the internal <c>CodePoint.GetCanonicalType</c> of SixLabors.Fonts 3.2.1
    /// (<c>src/SixLabors.Fonts/Unicode/CodePoint.cs</c>). The paired bracket values of U+3008 and U+3009 are those of
    /// their canonical equivalents U+2329 and U+232A, so that either form of a bracket pairs with the other (rule BD16).
    /// </remarks>
    /// <param name="codePoint">The code point to be mapped.</param>
    /// <returns>The mapped canonical code point, or the passed <paramref name="codePoint"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CodePoint GetCanonicalType(in CodePoint codePoint)
    {
        if (codePoint.Value == 0x3008)
        {
            return new CodePoint(0x2329);
        }

        if (codePoint.Value == 0x3009)
        {
            return new CodePoint(0x232A);
        }

        return codePoint;
    }
}
