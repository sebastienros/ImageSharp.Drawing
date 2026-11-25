// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Opens the resource that the <c>href</c> of an <c>image</c> element names, under the security rules of
/// <see cref="SvgLoadOptions.ResourceBaseDirectory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Rules:
/// </para>
/// <list type="bullet">
/// <item><description>A <c>data:</c> URI always opens, base64 or percent-encoded.</description></item>
/// <item><description>Nothing is ever fetched from the network: any other URL scheme, <c>file:</c> included, is
/// refused.</description></item>
/// <item><description>A file reference opens only when a base directory is set. It is a relative path, which is
/// percent-decoded and in which <c>\</c> counts as a separator, as in URLs. It is refused when it is absolute, names a
/// drive or a stream (contains <c>:</c>), contains a query or a fragment, or leaves the base directory after
/// normalization. A file that is a symbolic link is refused when its final target leaves the base
/// directory.</description></item>
/// <item><description>Nothing here throws for a bad reference: a refused or broken reference returns
/// <see langword="false"/>, and the image draws nothing.</description></item>
/// </list>
/// </remarks>
internal static class SvgImageSource
{
    /// <summary>
    /// Opens the resource of an <c>href</c>.
    /// </summary>
    /// <param name="href">The whole trimmed URL.</param>
    /// <param name="baseDirectory">The directory that file references resolve against, or <see langword="null"/> when no file may be read.</param>
    /// <param name="stream">When this method returns <see langword="true"/>, contains a readable, seekable stream that the caller disposes.</param>
    /// <param name="mediaType">When this method returns <see langword="true"/>, contains the lowercase media type of a <c>data:</c> URI, or an empty string when none is given or the resource is a file.</param>
    /// <returns><see langword="true"/> if the resource opened; otherwise, <see langword="false"/>.</returns>
    public static bool TryOpen(string href, string? baseDirectory, out Stream? stream, out string mediaType)
    {
        stream = null;
        mediaType = string.Empty;

        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryDecodeDataUri(href.AsSpan(5), out byte[]? data, out mediaType))
            {
                return false;
            }

            stream = new MemoryStream(data!, false);
            return true;
        }

        if (baseDirectory is null || !TryResolveFile(href, baseDirectory, out string? path))
        {
            return false;
        }

        try
        {
            stream = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves a file reference against the base directory and applies the containment rules.
    /// </summary>
    /// <param name="reference">The reference as written.</param>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="path">When this method returns <see langword="true"/>, contains the full path of an existing file inside the base directory.</param>
    /// <returns><see langword="true"/> if the reference names a file inside the base directory; otherwise, <see langword="false"/>.</returns>
    internal static bool TryResolveFile(string reference, string baseDirectory, out string? path)
    {
        path = null;

        // A scheme is a letter followed by letters, digits, '+', '-', or '.', then ':'. Every scheme is refused, and so
        // is any other ':' below, so a drive letter never gets through either.
        if (reference.Length == 0 || reference.IndexOfAny(['?', '#']) >= 0 || !TryPercentDecode(reference, out string? decoded))
        {
            return false;
        }

        // Decoding can produce separators and dot segments ("..%2F"), so every check runs on the decoded text.
        if (decoded!.Length == 0
            || decoded.Contains(':')
            || decoded.Contains('\0')
            || decoded[0] is '/' or '\\'
            || IOPath.IsPathRooted(decoded))
        {
            return false;
        }

        try
        {
            string separator = IOPath.DirectorySeparatorChar.ToString();
            string root = IOPath.GetFullPath(baseDirectory);
            if (!root.EndsWith(IOPath.DirectorySeparatorChar))
            {
                root += separator;
            }

            string relative = decoded.Replace('\\', '/').Replace("/", separator, StringComparison.Ordinal);
            string full = IOPath.GetFullPath(IOPath.Combine(root, relative));
            if (!IsInside(full, root) || !File.Exists(full))
            {
                return false;
            }

            // A symbolic link inside the directory can point outside it. Directory links on the way are part of the
            // directory that the caller chose and are not followed here.
            FileSystemInfo? target = new FileInfo(full).ResolveLinkTarget(true);
            if (target is not null && !IsInside(IOPath.GetFullPath(target.FullName), root))
            {
                return false;
            }

            path = full;
            return true;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns whether a normalized full path lies inside a directory.
    /// </summary>
    /// <param name="full">The full path.</param>
    /// <param name="root">The full directory path, ending with a separator.</param>
    /// <returns><see langword="true"/> if the path is inside the directory; otherwise, <see langword="false"/>.</returns>
    private static bool IsInside(string full, string root)
    {
        // Windows and macOS file systems are case-insensitive by default; Linux file systems are case-sensitive.
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return full.Length > root.Length && full.StartsWith(root, comparison);
    }

    /// <summary>
    /// Decodes the part of a <c>data:</c> URI after the scheme: <c>[media type][;parameters][;base64],data</c>.
    /// </summary>
    /// <param name="content">The URI after <c>data:</c>.</param>
    /// <param name="data">When this method returns <see langword="true"/>, contains the decoded bytes.</param>
    /// <param name="mediaType">When this method returns <see langword="true"/>, contains the lowercase media type, or an empty string.</param>
    /// <returns><see langword="true"/> if the URI decoded; otherwise, <see langword="false"/>.</returns>
    private static bool TryDecodeDataUri(ReadOnlySpan<char> content, out byte[]? data, out string mediaType)
    {
        data = null;
        mediaType = string.Empty;
        int comma = content.IndexOf(',');
        if (comma < 0)
        {
            return false;
        }

        ReadOnlySpan<char> header = content[..comma];
        ReadOnlySpan<char> payload = content[(comma + 1)..];
        bool base64 = false;
        int semicolon = header.IndexOf(';');
        ReadOnlySpan<char> type = semicolon < 0 ? header : header[..semicolon];
        mediaType = type.Trim().ToString().ToLowerInvariant();

        for (ReadOnlySpan<char> parameters = semicolon < 0 ? default : header[(semicolon + 1)..]; !parameters.IsEmpty;)
        {
            int next = parameters.IndexOf(';');
            ReadOnlySpan<char> parameter = (next < 0 ? parameters : parameters[..next]).Trim();
            base64 |= parameter.Equals("base64", StringComparison.OrdinalIgnoreCase);
            parameters = next < 0 ? default : parameters[(next + 1)..];
        }

        if (!base64)
        {
            return TryPercentDecodeBytes(payload, out data);
        }

        // Base64 in attributes often wraps over several lines, and may itself be percent-encoded.
        StringBuilder text = new(payload.Length);
        foreach (char c in payload)
        {
            if (!char.IsWhiteSpace(c))
            {
                _ = text.Append(c);
            }
        }

        string compact = text.ToString();
        if (compact.Contains('%'))
        {
            if (!TryPercentDecode(compact, out string? unescaped))
            {
                return false;
            }

            compact = unescaped!;
        }

        byte[] buffer = new byte[(compact.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(compact, buffer, out int written))
        {
            return false;
        }

        data = buffer.AsSpan(0, written).ToArray();
        return true;
    }

    /// <summary>
    /// Decodes percent escapes into text. The escaped octets are UTF-8.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="decoded">When this method returns <see langword="true"/>, contains the decoded text.</param>
    /// <returns><see langword="true"/> if every escape is valid and the octets are valid UTF-8; otherwise, <see langword="false"/>.</returns>
    private static bool TryPercentDecode(string text, out string? decoded)
    {
        decoded = null;
        if (!text.Contains('%'))
        {
            decoded = text;
            return true;
        }

        if (!TryPercentDecodeBytes(text, out byte[]? bytes))
        {
            return false;
        }

        try
        {
            decoded = new UTF8Encoding(false, true).GetString(bytes!);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decodes percent escapes into octets. Characters that are not escaped are encoded as UTF-8.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="bytes">When this method returns <see langword="true"/>, contains the octets.</param>
    /// <returns><see langword="true"/> if every escape is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryPercentDecodeBytes(ReadOnlySpan<char> text, out byte[]? bytes)
    {
        bytes = null;
        List<byte> result = new(text.Length);
        Span<byte> utf8 = stackalloc byte[4];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%')
            {
                if (i + 2 >= text.Length || !char.IsAsciiHexDigit(text[i + 1]) || !char.IsAsciiHexDigit(text[i + 2]))
                {
                    return false;
                }

                result.Add((byte)((Hex(text[i + 1]) << 4) | Hex(text[i + 2])));
                i += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                // A surrogate pair encodes as one code point; a lone surrogate is invalid text.
                if (i + 1 >= text.Length || !char.IsSurrogatePair(c, text[i + 1]))
                {
                    return false;
                }

                int count = Encoding.UTF8.GetBytes(text.Slice(i, 2), utf8);
                for (int j = 0; j < count; j++)
                {
                    result.Add(utf8[j]);
                }

                i++;
                continue;
            }

            int length = Encoding.UTF8.GetBytes(text.Slice(i, 1), utf8);
            for (int j = 0; j < length; j++)
            {
                result.Add(utf8[j]);
            }
        }

        bytes = [.. result];
        return true;
    }

    /// <summary>
    /// Returns the value of a hexadecimal digit.
    /// </summary>
    /// <param name="c">The digit.</param>
    /// <returns>The value.</returns>
    private static int Hex(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
}
