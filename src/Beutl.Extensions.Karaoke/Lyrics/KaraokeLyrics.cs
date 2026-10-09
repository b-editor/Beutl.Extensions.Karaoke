using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>
/// The lyrics an object shows: an immutable list of lines sorted by start. Edits return a new instance, so a
/// property holding one records each edit as a plain value change that undo can restore.
/// </summary>
/// <remarks>
/// <para>
/// Lines are kept in the form LRC can express, so the project file can store them as LRC text and read back
/// the same lyrics: times are whole milliseconds, a line ends no later than the next one starts, and timed
/// runs cover the text without gaps.
/// </para>
/// <para>
/// Deliberately not <see cref="IEnumerable{T}"/>: Beutl's serializer writes any enumerable value element by
/// element and skips <see cref="KaraokeLyricsJsonConverter"/>, which the reader then cannot parse.
/// </para>
/// </remarks>
[JsonConverter(typeof(KaraokeLyricsJsonConverter))]
public sealed class KaraokeLyrics : IEquatable<KaraokeLyrics>
{
    private static readonly TimeSpan s_minimumLength = TimeSpan.FromMilliseconds(1);

    private readonly LyricLine[] _lines;

    public KaraokeLyrics(IEnumerable<LyricLine> lines)
    {
        LyricLine[] sorted = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .Select((l, i) => (Line: l, Order: i))
            .OrderBy(p => Round(p.Line.Start))
            .ThenBy(p => p.Order)
            .Select(p => p.Line)
            .ToArray();

        _lines = new LyricLine[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            TimeSpan? next = i + 1 < sorted.Length ? Max(TimeSpan.Zero, Round(sorted[i + 1].Start)) : null;
            _lines[i] = Normalize(sorted[i], next);
        }

        Lines = Array.AsReadOnly(_lines);
    }

    public static KaraokeLyrics Empty { get; } = new([]);

    public IReadOnlyList<LyricLine> Lines { get; }

    public int Count => _lines.Length;

    public KaraokeLyrics Add(LyricLine line) => new(_lines.Append(line));

    public KaraokeLyrics AddRange(IEnumerable<LyricLine> lines) => new(_lines.Concat(lines));

    public KaraokeLyrics Replace(int index, LyricLine line)
    {
        var lines = (LyricLine[])_lines.Clone();
        lines[index] = line;
        return new KaraokeLyrics(lines);
    }

    public KaraokeLyrics RemoveAt(IEnumerable<int> indices)
    {
        var removed = new HashSet<int>(indices);
        return new KaraokeLyrics(_lines.Where((_, i) => !removed.Contains(i)));
    }

    public bool Equals(KaraokeLyrics? other)
    {
        if (other == null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (other._lines.Length != _lines.Length)
            return false;

        for (int i = 0; i < _lines.Length; i++)
        {
            if (!LinesEqual(_lines[i], other._lines[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as KaraokeLyrics);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_lines.Length);
        foreach (LyricLine line in _lines.Take(16))
        {
            hash.Add(line.Start);
            hash.Add(line.Text);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => $"{Count} lines";

    /// <summary>Compares lines by value; <see cref="LyricLine"/>'s own equality compares its word list by reference.</summary>
    public static bool LinesEqual(LyricLine a, LyricLine b)
    {
        return a.Start == b.Start && a.End == b.End && a.Text == b.Text && a.HasWordTiming == b.HasWordTiming
               && a.Words.SequenceEqual(b.Words);
    }

    /// <summary>
    /// Brings a line into the form LRC can express, the way <see cref="LrcParser"/> would read it back. Returns
    /// the same instance when it already is.
    /// </summary>
    /// <param name="next">When the following line starts, if any.</param>
    public static LyricLine Normalize(LyricLine line, TimeSpan? next)
    {
        // One LRC line holds one line of text.
        string raw = line.Text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        int leading = raw.Length - raw.TrimStart().Length;
        string text = raw.Trim();

        TimeSpan start = Max(TimeSpan.Zero, Round(line.Start));
        TimeSpan end = Round(line.End);
        if (next is { } n && end > n)
            end = n;
        if (end <= start)
            end = start + s_minimumLength;

        LyricLine normalized;
        if (!line.HasWordTiming || line.Words.Count == 0)
        {
            normalized = new LyricLine(start, end, text, [new LyricWord(0, text.Length, start, end)], false);
        }
        else
        {
            // Rebuild the runs from their start marks, exactly as the parser does: a run lasts until the next
            // mark, and text before the first mark is sung from the line's start.
            var marks = line.Words
                .OrderBy(w => w.CharStart)
                .Select(w => (Char: Math.Clamp(w.CharStart - leading, 0, text.Length), Time: Max(TimeSpan.Zero, Round(w.Start))))
                .ToList();
            marks.Add((text.Length, Max(TimeSpan.Zero, Round(line.Words.MaxBy(w => w.CharStart + w.Length)!.End))));
            if (marks[0].Char > 0)
                marks.Insert(0, (0, start));

            var words = new List<LyricWord>();
            for (int i = 0; i < marks.Count; i++)
            {
                int charStart = marks[i].Char;
                int charEnd = i + 1 < marks.Count ? marks[i + 1].Char : text.Length;
                if (charEnd <= charStart)
                    continue;

                TimeSpan wordStart = marks[i].Time;
                TimeSpan wordEnd = i + 1 < marks.Count ? marks[i + 1].Time : end;
                if (wordEnd < wordStart)
                    wordEnd = wordStart;
                words.Add(new LyricWord(charStart, charEnd - charStart, wordStart, wordEnd));
            }

            normalized = words.Count == 0
                ? new LyricLine(start, end, text, [new LyricWord(0, text.Length, start, end)], false)
                : new LyricLine(start, end, text, words, true);
        }

        return LinesEqual(normalized, line) ? line : normalized;
    }

    private static TimeSpan Round(TimeSpan time) => TimeSpan.FromMilliseconds(Math.Round(time.TotalMilliseconds));

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>Stores lyrics as an array of LRC lines, the form a karaoke author can read and edit by hand.</summary>
public sealed class KaraokeLyricsJsonConverter : JsonConverter<KaraokeLyrics>
{
    public override KaraokeLyrics Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return KaraokeLyrics.Empty;
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("歌詞は LRC の行の配列で書く必要があります。");

        var lines = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("歌詞の行は文字列で書く必要があります。");
            lines.Add(reader.GetString() ?? "");
        }

        return new KaraokeLyrics(LrcParser.Parse(string.Join('\n', lines)).Lines);
    }

    public override void Write(Utf8JsonWriter writer, KaraokeLyrics value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string line in LrcWriter.ToLines(value.Lines))
            writer.WriteStringValue(line);
        writer.WriteEndArray();
    }
}
