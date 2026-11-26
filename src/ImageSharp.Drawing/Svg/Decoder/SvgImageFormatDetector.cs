// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using SixLabors.ImageSharp.Formats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Detects SVG documents from the start of their data.
/// </summary>
/// <remarks>
/// <para>
/// The detector accepts data whose first element is <c>svg</c>, with or without a namespace prefix. Before it, an XML
/// declaration, processing instructions, comments, and white space can appear. A <c>DOCTYPE</c> that names <c>svg</c> as
/// the root element is also accepted, so the internal subset of a long <c>DOCTYPE</c> does not need to fit. Any other
/// first element, a <c>DOCTYPE</c> of another root, or other content is not SVG. The text can be UTF-8 with or without a
/// byte order mark, or UTF-16 with a byte order mark. Gzip data (<c>.svgz</c>) is inflated and its start is checked the
/// same way.
/// </para>
/// <para>
/// The root element, or the <c>DOCTYPE</c>, must start within the first <see cref="HeaderSize"/> bytes: 512 bytes, which
/// are 256 characters of UTF-16. For gzip data, the first 512 compressed bytes are inflated and the root must start
/// within the first 512 inflated bytes. Editor exports start their root element after about 40 to 150 bytes. A document
/// whose root starts later, for example after a long comment, is not detected; pass <see cref="SvgDecoder"/> to the
/// decoding methods to read it. Bytes after the first 512 are ignored, so the result does not depend on the other
/// registered detectors. The detector checks only the start of the data, so a detected document can still fail to
/// decode.
/// </para>
/// <para>
/// ImageSharp calls a detector only when the data has at least <see cref="HeaderSize"/> bytes. The
/// <see cref="SvgConfigurationModule"/> therefore also registers a detector for shorter data. This method itself
/// accepts a header of any length. It never throws, and it allocates no memory for text data; for gzip data it
/// allocates the fixed state of one inflater.
/// </para>
/// </remarks>
public sealed class SvgImageFormatDetector : IImageFormatDetector
{
    /// <summary>
    /// The number of bytes that detection reads: 512. ImageSharp reads the header of every detection into a stack buffer
    /// when the largest header size of the registered detectors is at most 512 bytes, so a larger window would make the
    /// detection of every format allocate. The root element of every file of the benchmark corpus and of the resvg test
    /// suite starts within the first 277 bytes.
    /// </summary>
    internal const int WindowSize = 512;

    /// <summary>
    /// Gets the number of bytes that detection reads: 512.
    /// </summary>
    public int HeaderSize => WindowSize;

    /// <summary>
    /// Detects whether a header is the start of an SVG document.
    /// </summary>
    /// <param name="header">The first bytes of the data. Bytes after the first <see cref="HeaderSize"/> are ignored.</param>
    /// <param name="format">When this method returns <see langword="true"/>, contains <see cref="SvgFormat.Instance"/>; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the header is the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    public bool TryDetectFormat(ReadOnlySpan<byte> header, [NotNullWhen(true)] out IImageFormat? format)
    {
        format = IsSvg(header) ? SvgFormat.Instance : null;
        return format is not null;
    }

    /// <summary>
    /// Returns whether a header is the start of an SVG document, as text or as gzip data.
    /// </summary>
    /// <param name="header">The first bytes of the data.</param>
    /// <returns><see langword="true"/> if the header is the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    internal static bool IsSvg(ReadOnlySpan<byte> header)
    {
        if (header.Length > WindowSize)
        {
            header = header[..WindowSize];
        }

        // The gzip magic number and the deflate method (RFC 1952). No XML text starts with these bytes.
        if (header.Length >= 3 && header[0] == 0x1F && header[1] == 0x8B && header[2] == 8)
        {
            return IsCompressedSvg(header);
        }

        return IsSvgText(header);
    }

    /// <summary>
    /// Returns whether gzip data inflates to the start of an SVG document.
    /// </summary>
    /// <remarks>
    /// The header ends inside the compressed data, so the inflater reaches the end of its input early. The bytes that it
    /// inflated up to that point are kept, whether it then reports the end or an error.
    /// </remarks>
    /// <param name="header">The gzip data, at most <see cref="WindowSize"/> bytes.</param>
    /// <returns><see langword="true"/> if the inflated text is the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    private static bool IsCompressedSvg(ReadOnlySpan<byte> header)
    {
        // GZipStream reads from a stream, so the header is copied into a pooled array that a memory stream wraps.
        byte[] compressed = ArrayPool<byte>.Shared.Rent(header.Length);
        Span<byte> inflated = stackalloc byte[WindowSize];
        int length = 0;
        try
        {
            header.CopyTo(compressed);
            using MemoryStream input = new(compressed, 0, header.Length, false);
            using GZipStream gzip = new(input, CompressionMode.Decompress);
            while (length < inflated.Length)
            {
                int read = gzip.Read(inflated[length..]);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // Corrupt data, or data cut off by the header: the bytes inflated so far are checked.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
        }

        return IsSvgText(inflated[..length]);
    }

    /// <summary>
    /// Returns whether text data is the start of an SVG document. A byte order mark selects UTF-8 or UTF-16; without one
    /// the text is UTF-8, or another encoding that is compatible with ASCII.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns><see langword="true"/> if the data is the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    private static bool IsSvgText(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return IsSvgProlog(new HeaderText(data[3..], 1, false));
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return IsSvgProlog(new HeaderText(data[2..], 2, false));
        }

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return IsSvgProlog(new HeaderText(data[2..], 2, true));
        }

        return IsSvgProlog(new HeaderText(data, 1, false));
    }

    /// <summary>
    /// Reads the prolog of an XML document and returns whether its first element, or its <c>DOCTYPE</c>, names
    /// <c>svg</c>.
    /// </summary>
    /// <remarks>
    /// The prolog is a sequence of white space, processing instructions (the XML declaration among them), and comments,
    /// with at most one <c>DOCTYPE</c>. A construct that does not end within the text leaves the result undecided, which
    /// counts as not SVG.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> if the text is the start of an SVG document; otherwise, <see langword="false"/>.</returns>
    private static bool IsSvgProlog(HeaderText text)
    {
        int i = 0;
        while (true)
        {
            i = SkipWhiteSpace(text, i);
            if (i + 1 >= text.Length || text[i] != '<')
            {
                return false;
            }

            char next = text[i + 1];
            if (next == '?')
            {
                int end = IndexOf(text, i + 2, "?>");
                if (end < 0)
                {
                    return false;
                }

                i = end + 2;
                continue;
            }

            if (next == '!')
            {
                if (StartsWith(text, i + 2, "--"))
                {
                    int end = IndexOf(text, i + 4, "-->");
                    if (end < 0)
                    {
                        return false;
                    }

                    i = end + 3;
                    continue;
                }

                // A DOCTYPE names the root element. White space must separate the keyword from the name.
                int name = i + 9;
                if (!StartsWith(text, i + 2, "DOCTYPE") || name >= text.Length || !IsWhiteSpace(text[name]))
                {
                    return false;
                }

                return IsSvgName(text, SkipWhiteSpace(text, name));
            }

            return IsSvgName(text, i + 1);
        }
    }

    /// <summary>
    /// Returns whether a name starts at an index, is followed by a delimiter within the text, and has the local name
    /// <c>svg</c>, with or without a prefix. XML names are case-sensitive.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first character of the name.</param>
    /// <returns><see langword="true"/> if the name is <c>svg</c> or ends with <c>:svg</c>; otherwise, <see langword="false"/>.</returns>
    private static bool IsSvgName(HeaderText text, int start)
    {
        int end = start;
        while (end < text.Length && !IsWhiteSpace(text[end]) && text[end] is not ('>' or '/' or '['))
        {
            end++;
        }

        // A name that reaches the end of the text may continue after it.
        if (end >= text.Length || end - start < 3)
        {
            return false;
        }

        int local = end - 3;
        return (local == start || text[local - 1] == ':')
            && text[local] == 's'
            && text[local + 1] == 'v'
            && text[local + 2] == 'g';
    }

    /// <summary>
    /// Returns the index of the first character at or after an index that is not XML white space.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index to start at.</param>
    /// <returns>The index, or the length of the text.</returns>
    private static int SkipWhiteSpace(HeaderText text, int index)
    {
        while (index < text.Length && IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// Returns whether a character is XML white space.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for space, tab, carriage return, and line feed; otherwise, <see langword="false"/>.</returns>
    private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\r' or '\n';

    /// <summary>
    /// Returns whether the text has an ASCII string at an index.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index.</param>
    /// <param name="value">The string.</param>
    /// <returns><see langword="true"/> if the characters at the index equal the string; otherwise, <see langword="false"/>.</returns>
    private static bool StartsWith(HeaderText text, int index, string value)
    {
        if (index + value.Length > text.Length)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (text[index + i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the index of the first occurrence of an ASCII string at or after an index.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="index">The index to start at.</param>
    /// <param name="value">The string.</param>
    /// <returns>The index, or -1 when the string does not occur.</returns>
    private static int IndexOf(HeaderText text, int index, string value)
    {
        for (int i = index; i + value.Length <= text.Length; i++)
        {
            if (StartsWith(text, i, value))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The characters of a header in one of the encodings the detector reads. Only ASCII characters are compared, so a
    /// UTF-8 header is read byte by byte and a UTF-16 header code unit by code unit.
    /// </summary>
    private readonly ref struct HeaderText
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly int width;
        private readonly bool bigEndian;

        /// <summary>
        /// Initializes a new instance of the <see cref="HeaderText"/> struct.
        /// </summary>
        /// <param name="data">The bytes after the byte order mark.</param>
        /// <param name="width">The number of bytes of one code unit: 1 or 2.</param>
        /// <param name="bigEndian">Whether a two-byte code unit has its high byte first.</param>
        public HeaderText(ReadOnlySpan<byte> data, int width, bool bigEndian)
        {
            this.data = data;
            this.width = width;
            this.bigEndian = bigEndian;
        }

        /// <summary>
        /// Gets the number of whole code units.
        /// </summary>
        public int Length => this.data.Length / this.width;

        /// <summary>
        /// Gets the code unit at an index.
        /// </summary>
        /// <param name="index">The index, less than <see cref="Length"/>.</param>
        /// <returns>The code unit.</returns>
        public char this[int index]
        {
            get
            {
                if (this.width == 1)
                {
                    return (char)this.data[index];
                }

                int offset = index * 2;
                return this.bigEndian
                    ? (char)((this.data[offset] << 8) | this.data[offset + 1])
                    : (char)(this.data[offset] | (this.data[offset + 1] << 8));
            }
        }
    }
}
