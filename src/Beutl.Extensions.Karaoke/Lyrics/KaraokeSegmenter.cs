namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>Splits a lyric line into the units a singer times one by one.</summary>
/// <remarks>
/// Each kana or kanji is a unit; a small kana (ゃ, ぁ, …) joins the character before it, as "きゃ" is sung as
/// one beat; a run of Latin letters or digits is one word; spaces and closing punctuation join the unit before
/// them, and opening brackets the unit after them.
/// </remarks>
public static class KaraokeSegmenter
{
    private const string SmallKana = "ぁぃぅぇぉゃゅょゎゕゖァィゥェォャュョヮヵヶㇰㇱㇲㇳㇴㇵㇶㇷㇸㇹㇺㇻㇼㇽㇾㇿ";
    // The long vowel mark "ー" is not here: it is its own beat ("ラーメン").
    private const string Trailing = "、。，．,.!！?？・…‥」』）)】〕〉》〙〛~〜゛゜\"'";
    private const string Opening = "「『（(【〔〈《〘〚";

    public static List<(int Start, int Length)> Split(string text)
    {
        var units = new List<(int Start, int Length)>();
        int i = 0;
        int? pending = null;
        while (i < text.Length)
        {
            char c = text[i];
            if (units.Count > 0 && pending == null && (char.IsWhiteSpace(c) || SmallKana.Contains(c) || Trailing.Contains(c)))
            {
                (int s, int l) = units[^1];
                units[^1] = (s, l + 1);
                i++;
                continue;
            }

            if (Opening.Contains(c) || char.IsWhiteSpace(c))
            {
                pending ??= i;
                i++;
                continue;
            }

            int start = pending ?? i;
            pending = null;
            if (IsWordChar(c))
            {
                while (i < text.Length && IsWordChar(text[i]))
                    i++;
            }
            else
            {
                i++;
                if (char.IsHighSurrogate(c) && i < text.Length && char.IsLowSurrogate(text[i]))
                    i++;
            }

            units.Add((start, i - start));
        }

        // Brackets or spaces with nothing after them belong to the last unit.
        if (pending is { } rest)
        {
            if (units.Count > 0)
                units[^1] = (units[^1].Start, text.Length - units[^1].Start);
            else
                units.Add((rest, text.Length - rest));
        }

        return units;
    }

    private static bool IsWordChar(char c) => c == '\'' || (c < '\u2E80' && char.IsLetterOrDigit(c));
}
