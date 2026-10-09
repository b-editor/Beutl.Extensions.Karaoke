using System.Text;

namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>Decodes text files as UTF-8, falling back to Shift_JIS, which many Japanese lyric files use.</summary>
public static class TextFileDecoder
{
    static TextFileDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return Encoding.UTF8.GetString(span[3..]);
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return Encoding.Unicode.GetString(span[2..]);
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return Encoding.BigEndianUnicode.GetString(span[2..]);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }
}
