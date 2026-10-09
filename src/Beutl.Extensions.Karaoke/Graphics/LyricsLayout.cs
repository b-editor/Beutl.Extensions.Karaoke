using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Extensions.Karaoke.Graphics;

/// <summary>The shaped text of every lyric line and the x position of each character boundary.</summary>
/// <remarks>
/// Each line is shaped twice because a text's outline comes from its own pen: one copy carries the unsung
/// outline and the other the sung one. Drawn texts are not disposed: a render node recorded for an earlier
/// frame may still hold them, and their Skia handles are finalizable, so a replaced layout is left to the
/// garbage collector.
/// </remarks>
internal sealed class LyricsLayout
{
    private readonly IReadOnlyList<LyricLine> _lines;
    private readonly FontFamily _font;
    private readonly float _size;
    private readonly FontWeight _weight;

    public LyricsLayout(IReadOnlyList<LyricLine> lines, FontFamily font, float size, FontWeight weight)
    {
        _lines = lines;
        _font = font;
        _size = size;
        _weight = weight;
        Texts = new FormattedText[lines.Count];
        SungTexts = new FormattedText[lines.Count];
        CharX = new float[lines.Count][];

        for (int i = 0; i < lines.Count; i++)
        {
            string text = lines[i].Text;
            Texts[i] = Create(text, text.Length);
            SungTexts[i] = Create(text, text.Length);
            var boundaries = new float[text.Length + 1];
            for (int n = 1; n <= text.Length; n++)
            {
                using FormattedText prefix = Create(text, n);
                boundaries[n] = prefix.Bounds.Width;
            }

            CharX[i] = boundaries;
            MaxWidth = Math.Max(MaxWidth, Texts[i].Bounds.Width);

            // Rows are sized by the font's ascent and descent, not by each line's ink, so every row has the
            // same baseline offset whatever characters it contains.
            FontMetrics metrics = Texts[i].Metrics;
            Ascent = Math.Min(Ascent, metrics.Ascent);
            Descent = Math.Max(Descent, metrics.Descent);
        }

        if (LineHeight <= 0)
        {
            Ascent = -size;
            Descent = 0;
        }
    }

    /// <summary>The texts drawn with the unsung fill and outline.</summary>
    public FormattedText[] Texts { get; }

    /// <summary>The texts drawn with the sung fill and outline.</summary>
    public FormattedText[] SungTexts { get; }

    /// <summary>For each line, the x of every character boundary; the last entry is the line width.</summary>
    public float[][] CharX { get; }

    public float MaxWidth { get; }

    /// <summary>The distance from the baseline up to the top of a row (negative).</summary>
    public float Ascent { get; }

    /// <summary>The distance from the baseline down to the bottom of a row.</summary>
    public float Descent { get; }

    public float LineHeight => Descent - Ascent;

    public bool Matches(IReadOnlyList<LyricLine> lines, FontFamily font, float size, FontWeight weight)
    {
        return ReferenceEquals(lines, _lines) && font.Equals(_font) && size == _size && weight == _weight;
    }

    /// <summary>The x position of a fractional character position.</summary>
    public float GetX(int line, double characters)
    {
        float[] boundaries = CharX[line];
        if (characters <= 0)
            return 0;
        if (characters >= boundaries.Length - 1)
            return boundaries[^1];

        int whole = (int)characters;
        double fraction = characters - whole;
        return (float)(boundaries[whole] + (boundaries[whole + 1] - boundaries[whole]) * fraction);
    }

    private FormattedText Create(string text, int length)
    {
        return new FormattedText
        {
            Font = _font,
            Size = Math.Max(1, _size),
            Weight = _weight,
            Text = new StringSpan(text, 0, length),
        };
    }
}
