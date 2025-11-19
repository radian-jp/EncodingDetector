using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Utility class for decoding byte sequences with unknown encoding by selecting the most natural-looking result.
/// </summary>
public static class EncodingDetector
{
    private enum CodePages
    {
        Utf16LE = 1200,
        Utf16BE = 1201,
        Utf32LE = 12000,
        Utf32BE = 12001,
    }

    private static readonly Encoding[] _defaultEncodings = new[]
    {
        Encoding.Unicode,
        new UTF8Encoding(false),
        Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage),
    };

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

        encodings ??= _defaultEncodings;

        int bytesLen = bytes.Length;
        int maxCharCount = encodings.Max(e => e.GetMaxCharCount(bytesLen));
        Span<char> buf = stackalloc char[maxCharCount];
        Span<char> bestChars = stackalloc char[maxCharCount];

        int bestScore = -1;
        int bestCharsLength = 0;

        foreach (var enc in encodings)
        {
            var processed = RemoveBomIfPresent(bytes, enc);
            if (!enc.TryGetChars(processed, buf, out var written))
                continue;

            var chars = buf.Slice(0, written);
            if (ContainsDefinitelyGarbledChar(chars))
                continue;

            int score = ScoreText(chars);
            if (score > bestScore)
            {
                chars.CopyTo(bestChars);
                bestCharsLength = written;
                bestScore = score;

                if (score == 100)
                    break;
            }
        }

        if (bestCharsLength > 0)
        {
            var resultSpan = bestChars.Slice(0, bestCharsLength);
            int nullIndex = resultSpan.IndexOf('\0');
            if (nullIndex != -1)
                resultSpan = resultSpan.Slice(0, nullIndex);

            return new string(resultSpan);
        }

        return Encoding.Default.GetString(bytes);
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

        encodings ??= _defaultEncodings;

        var bytes = SliceNull(new ReadOnlySpan<byte>(ptr, length), encodings);

        return DecodeAuto(bytes, encodings);
    }

    private static ReadOnlySpan<byte> SliceNull(ReadOnlySpan<byte> bytes, Encoding[] encodings)
    {
        int index = bytes.IndexOf((byte)0);

        var codePages = encodings.Select(x => (CodePages)x.CodePage);
        if (codePages.Contains(CodePages.Utf16LE) || codePages.Contains(CodePages.Utf16BE))
        {
            var char2Span = MemoryMarshal.Cast<byte, char>(bytes);
            int index2 = char2Span.IndexOf('\0');
            index = Math.Max(index2 * 2, index);
        }

        if (codePages.Contains(CodePages.Utf32LE) || codePages.Contains(CodePages.Utf32BE))
        {
            var char4Span = MemoryMarshal.Cast<byte, int>(bytes);
            int index4 = char4Span.IndexOf(0);
            index = Math.Max(index4 * 4, index);
        }
        if (index >= 0)
            bytes = bytes.Slice(0, index);

        return bytes;
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

    /// <summary>
    /// Checks whether the string contains characters that strongly indicate garbled text,
    /// such as replacement characters, control characters, private use area, or invalid Unicode ranges.
    /// </summary>
    /// <param name="text">The decoded string to inspect.</param>
    /// <returns><c>true</c> if the string contains garbled characters; otherwise, <c>false</c>.</returns>
    private static bool ContainsDefinitelyGarbledChar(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\0')
                continue;

            // Replacement character
            if (c == '\uFFFD')
                return true;

            var category = Char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.Control ||
                category == UnicodeCategory.PrivateUse ||
                category == UnicodeCategory.OtherNotAssigned ||
                category == UnicodeCategory.Format)
            {
                return true;
            }

            if (category == UnicodeCategory.Surrogate)
            {
                // Check if it's a valid surrogate pair
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                        return true;
                    i++; // Skip the low surrogate
                }
                else if (char.IsLowSurrogate(c))
                {
                    // Low surrogate without preceding high surrogate
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsKanji(char c)
    {
        return (c >= '\u4E00' && c <= '\u9FFF') ||
               (c >= '\u3400' && c <= '\u4DBF') ||
               (c >= '\uF900' && c <= '\uFAFF');
    }

    private static bool IsAsciiSymbol(char c)
    {
        return c == '\0' ||
               (c >= 0x21 && c <= 0x2F) ||
               (c >= 0x3A && c <= 0x40) ||
               (c >= 0x5B && c <= 0x60) ||
               (c >= 0x7B && c <= 0x7E);
    }

    /// <summary>
    /// It judges the naturalness of a string and returns a score between 0 and 100.
    /// If the ratio of kanji and symbols is high, the score will be lower.
    /// </summary>
    /// <param name="text">The decoded string to evaluate.</param>
    /// <returns>An integer score representing the naturalness of the string.</returns>
    private static int ScoreText(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
            return 0;

        const int symbolThreshold = 20;
        int kanjiOrSymbolCount = 0;
        foreach (var c in text)
        {
            if (IsKanji(c) || IsAsciiSymbol(c))
                kanjiOrSymbolCount++;
        }

        var symbolPercent = kanjiOrSymbolCount * 100 / text.Length;
        if (symbolPercent > symbolThreshold)
            return 100 - symbolPercent;

        return 100;
    }
}
