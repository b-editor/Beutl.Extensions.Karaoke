using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>
/// Reads LRC lyrics, including several time tags per line ("[00:12.00][01:30.00]…"), the enhanced
/// per-word format ("[00:12.00]&lt;00:12.00&gt;あ&lt;00:12.40&gt;い…"), and the [offset:] tag.
/// </summary>
public static partial class LrcParser
{
    /// <summary>The end given to the last line when nothing else bounds it.</summary>
    public static readonly TimeSpan LastLineLength = TimeSpan.FromSeconds(5);

    [GeneratedRegex(@"^\[(\d+):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex LineTimeTag();

    [GeneratedRegex(@"<(\d+):(\d{1,2})(?:[.:](\d{1,3}))?>")]
    private static partial Regex WordTimeTag();

    [GeneratedRegex(@"^\[([A-Za-z#]+):(.*)\]\s*$")]
    private static partial Regex IdTag();

    public static LyricsDocument Parse(string content)
    {
        string? title = null;
        string? artist = null;
        TimeSpan offset = TimeSpan.Zero;
        var entries = new List<(TimeSpan Time, string Body, int Order)>();

        foreach (string rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0)
                continue;

            var times = new List<TimeSpan>();
            Match match;
            while ((match = LineTimeTag().Match(line)).Success)
            {
                times.Add(ToTime(match));
                line = line[match.Length..];
            }

            if (times.Count == 0)
            {
                Match id = IdTag().Match(line);
                if (!id.Success)
                    continue;

                string value = id.Groups[2].Value.Trim();
                switch (id.Groups[1].Value.ToLowerInvariant())
                {
                    case "ti":
                        title = value;
                        break;
                    case "ar":
                        artist = value;
                        break;
                    case "offset" when int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ms):
                        offset = TimeSpan.FromMilliseconds(ms);
                        break;
                }

                continue;
            }

            foreach (TimeSpan time in times)
                entries.Add((time, line, entries.Count));
        }

        // A positive offset makes the lyrics appear earlier.
        entries = entries
            .Select(e => (Time: Max(TimeSpan.Zero, e.Time - offset), e.Body, e.Order))
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Order)
            .ToList();

        var lines = new List<LyricLine>();
        for (int i = 0; i < entries.Count; i++)
        {
            (TimeSpan start, string body, _) = entries[i];
            TimeSpan? next = i + 1 < entries.Count ? entries[i + 1].Time : null;
            LyricLine? line = BuildLine(start, next, body, offset);
            if (line != null)
                lines.Add(line);
        }

        return new LyricsDocument(lines, title, artist);
    }

    private static LyricLine? BuildLine(TimeSpan start, TimeSpan? next, string body, TimeSpan offset)
    {
        var text = new StringBuilder();
        var marks = new List<(int CharIndex, TimeSpan Time)>();
        int position = 0;
        foreach (Match match in WordTimeTag().Matches(body))
        {
            text.Append(body, position, match.Index - position);
            marks.Add((text.Length, Max(TimeSpan.Zero, ToTime(match) - offset)));
            position = match.Index + match.Length;
        }

        text.Append(body, position, body.Length - position);

        // Trim the line's surrounding spaces and move the marks with the text.
        string raw = text.ToString();
        int leading = raw.Length - raw.TrimStart().Length;
        string trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return null; // An empty line only marks where the previous one ends.

        marks = marks.Select(m => (Math.Clamp(m.CharIndex - leading, 0, trimmed.Length), m.Time)).ToList();
        TimeSpan lastMark = marks.Count > 0 ? marks.Max(m => m.Time) : start;
        TimeSpan end = next ?? Max(lastMark, start + LastLineLength);
        if (end <= start)
            end = start + TimeSpan.FromMilliseconds(1);

        var words = new List<LyricWord>();
        if (marks.Count == 0)
        {
            words.Add(new LyricWord(0, trimmed.Length, start, end));
            return new LyricLine(start, end, trimmed, words, false);
        }

        // Text before the first mark is sung from the line's start.
        if (marks[0].CharIndex > 0)
            marks.Insert(0, (0, start));

        for (int i = 0; i < marks.Count; i++)
        {
            int charStart = marks[i].CharIndex;
            int charEnd = i + 1 < marks.Count ? marks[i + 1].CharIndex : trimmed.Length;
            if (charEnd <= charStart)
                continue;

            TimeSpan wordStart = marks[i].Time;
            TimeSpan wordEnd = i + 1 < marks.Count ? marks[i + 1].Time : end;
            if (wordEnd < wordStart)
                wordEnd = wordStart;
            words.Add(new LyricWord(charStart, charEnd - charStart, wordStart, wordEnd));
        }

        return new LyricLine(start, end, trimmed, words, true);
    }

    private static TimeSpan ToTime(Match match)
    {
        int minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double fraction = 0;
        if (match.Groups[3].Success)
        {
            string digits = match.Groups[3].Value;
            fraction = int.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, digits.Length);
        }

        return TimeSpan.FromSeconds(minutes * 60 + seconds + fraction);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
