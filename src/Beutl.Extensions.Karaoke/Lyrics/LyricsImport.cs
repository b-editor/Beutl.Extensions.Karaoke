namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>Reads lyrics to import: timed LRC, or plain text that the editor times afterwards.</summary>
public static class LyricsImport
{
    /// <summary>How far apart untimed lines are placed until they are timed.</summary>
    public static readonly TimeSpan PlainLineSpacing = TimeSpan.FromSeconds(4);

    /// <param name="content">LRC or plain text.</param>
    /// <param name="timed">Whether the text carried times; plain lines are spaced <see cref="PlainLineSpacing"/> apart from zero.</param>
    public static List<LyricLine> Parse(string content, out bool timed)
    {
        LyricsDocument document = LrcParser.Parse(content);
        if (document.Lines.Count > 0)
        {
            timed = true;
            return document.Lines.ToList();
        }

        timed = false;
        var lines = new List<LyricLine>();
        TimeSpan start = TimeSpan.Zero;
        foreach (string raw in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string text = raw.Trim().TrimStart('\uFEFF');
            // Skip blank lines and LRC tags such as "[ti:タイトル]".
            if (text.Length == 0 || (text.StartsWith('[') && text.EndsWith(']')))
                continue;

            TimeSpan end = start + PlainLineSpacing;
            lines.Add(new LyricLine(start, end, text, [new LyricWord(0, text.Length, start, end)], false));
            start = end;
        }

        return lines;
    }

    /// <exception cref="IOException">The file could not be read.</exception>
    public static List<LyricLine> ReadFile(string path, out bool timed)
    {
        return Parse(TextFileDecoder.Decode(File.ReadAllBytes(path)), out timed);
    }
}
