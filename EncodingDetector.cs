using System.Text;
using System.Globalization;

/// <summary>
/// Utility class for decoding byte sequences with unknown encoding by selecting the most natural-looking result.
/// </summary>
public static class EncodingDetector
{
    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Automatically decodes the given byte span using the most natural-looking result
    /// from the specified encoding candidates.
    /// </summary>
    /// <param name="bytes">The byte span to decode.</param>
    /// <param name="encodings">
    /// Optional array of encoding candidates to try. If null, the method uses:
    /// UTF-16 (LE), UTF-8 (without BOM), and the system's ANSI code page.
    /// </param>
    /// <returns>
    /// The decoded string that appears most natural. If all candidates are rejected due to garbled characters,
    /// the method falls back to decoding with <see cref="Encoding.Default"/>.
    /// </returns>
    public static string DecodeAuto(ReadOnlySpan<byte> bytes, Encoding[]? encodings = null)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        encodings ??= new[]
        {
            Encoding.Unicode,
            new UTF8Encoding(false),
            Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage),
        };

        string? bestText = null;
        int bestScore = -1;
        foreach (var enc in encodings)
        {
            var processed = RemoveBomIfPresent(bytes, enc);
            if (!TryDecode(processed, enc, out var text))
                continue;

            var chars = text.AsSpan();
            if (ContainsDefinitelyGarbledChar(chars))
                continue;

            int score = ScoreText(chars);
            if (score > bestScore)
            {
                bestScore = score;
                bestText = text;
            }
        }

        return bestText ?? Encoding.Default.GetString(bytes.ToArray());
    }

    /// <summary>
    /// Automatically decodes the given byte span using the most natural-looking result
    /// from the specified encoding candidates.
    /// </summary>
    /// <param name="ptr">Pointer to the beginning of the byte sequence.</param>
    /// <param name="length">
    /// The maximum number of bytes in the string pointed to by <paramref name="ptr"/>.
    /// If a null byte (0x00) is found, decoding stops at that point.
    /// <param name="encodings">
    /// Optional array of encoding candidates to try. If null, the method uses:
    /// UTF-16 (LE), UTF-8 (without BOM), and the system's ANSI code page.
    /// </param>
    /// <returns>
    /// The decoded string that appears most natural. If all candidates are rejected due to garbled characters,
    /// the method falls back to decoding with <see cref="Encoding.Default"/>.
    /// </returns>
    public static unsafe string DecodeAuto(byte* ptr, int length, Encoding[]? encodings = null)
    {
        var span = new Span<byte>(ptr, length);
        var index = span.IndexOf((byte)0);
        if (index >= 0)
            span = span.Slice(0, index);

        return DecodeAuto(span, encodings);
    }

    /// <summary>
    /// Removes the BOM (Byte Order Mark) from the beginning of the byte span if present for the specified encoding.
    /// </summary>
    /// <param name="bytes">The original byte span.</param>
    /// <param name="encoding">The encoding to check for BOM.</param>
    /// <returns>The byte span with BOM removed if present.</returns>
    private static ReadOnlySpan<byte> RemoveBomIfPresent(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0 && bytes.StartsWith(preamble))
        {
            return bytes.Slice(preamble.Length);
        }
        return bytes;
    }

    private static bool TryDecode(ReadOnlySpan<byte> bytes, Encoding encoding, out string result)
    {
        try
        {
            result = encoding.GetString(bytes);
            return true;
        }
        catch
        {
            result = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Checks whether the string contains characters that strongly indicate garbled text,
    /// such as replacement characters, control characters, private use area, or invalid Unicode ranges.
    /// </summary>
    /// <param name="text">The decoded string to inspect.</param>
    /// <returns><c>true</c> if the string contains garbled characters; otherwise, <c>false</c>.</returns>
    private static bool ContainsDefinitelyGarbledChar(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            int code = c;

            // U+FFFD: Replacement character (�), inserted when decoding fails
            if (c == '\uFFFD') return true;

            // U+0000–U+001F: C0 control characters
            if (code >= 0x00 && code <= 0x1F) return true;

            // U+007F: DEL
            if (code == 0x7F) return true;

            // U+E000–U+F8FF: Private Use Area
            if (code >= 0xE000 && code <= 0xF8FF) return true;

            // U+D800–U+DFFF: Surrogate halves
            //if (code >= 0xD800 && code <= 0xDFFF) return true;

            // U+FDD0–U+FDEF and U+FFFE/U+FFFF: Noncharacters
            if ((code >= 0xFDD0 && code <= 0xFDEF) || code == 0xFFFE || code == 0xFFFF) return true;
        }

        return false;
    }

    /// <summary>
    /// Scores the naturalness of the string based on the proportion of readable characters
    /// such as letters, digits, whitespace, and punctuation. Returns a score from 0 to 100.
    /// </summary>
    /// <param name="text">The decoded string to evaluate.</param>
    /// <returns>An integer score representing the naturalness of the string.</returns>
    private static int ScoreText(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
            return 0;

        int readable = 0;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) ||
                char.IsWhiteSpace(c) ||
                char.IsPunctuation(c))
                readable++;
        }

        return readable * 100 / text.Length;
    }
}
