using System.Globalization;
using System.Text;

namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>Writes lyrics as enhanced LRC, which <see cref="LrcParser"/> reads back unchanged.</summary>
public static class LrcWriter
{
    public static string ToLrc(IReadOnlyList<LyricLine> lines) => string.Join('\n', ToLines(lines)) + "\n";

    /// <summary>
    /// One LRC line per lyric line: "[start]text", or "[start]&lt;t0&gt;き&lt;t1&gt;み&lt;end&gt;" with per-word
    /// times. A line that ends before the next one starts is followed by an empty "[end]" line, which is how LRC
    /// marks where a line stops.
    /// </summary>
    public static IReadOnlyList<string> ToLines(IReadOnlyList<LyricLine> lines)
    {
        var result = new List<string>(lines.Count + 1);
        for (int i = 0; i < lines.Count; i++)
        {
            LyricLine line = lines[i];
            var sb = new StringBuilder();
            sb.Append('[').Append(FormatTag(line.Start)).Append(']');
            if (line.HasWordTiming)
            {
                foreach (LyricWord word in line.Words)
                    sb.Append('<').Append(FormatTag(word.Start)).Append('>').Append(line.Text, word.CharStart, word.Length);
                sb.Append('<').Append(FormatTag(line.Words[^1].End)).Append('>');
            }
            else
            {
                sb.Append(line.Text);
            }

            result.Add(sb.ToString());

            if (i + 1 >= lines.Count || line.End < lines[i + 1].Start)
                result.Add($"[{FormatTag(line.End)}]");
        }

        return result;
    }

    /// <summary>"mm:ss.fff", minutes counting past an hour, as LRC time tags write it.</summary>
    public static string FormatTag(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    /// <summary>"m:ss.fff", or "h:mm:ss.fff" from an hour on, for showing times in the editor.</summary>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            return "-" + FormatTime(-time);

        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    /// <summary>Reads seconds ("12.5") or clock times ("1:02.5", "0:01:02.5").</summary>
    public static bool TryParseTime(string value, out TimeSpan time)
    {
        time = default;
        string[] parts = value.Trim().Split(':');
        if (parts.Length > 3)
            return false;

        double seconds = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || number < 0)
                return false;
            seconds = seconds * 60 + number;
        }

        time = TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
        return true;
    }
}
