using System.Text.Json;
using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Tests;

// The lyrics are the value the element stores: their normal form decides what the project file keeps and
// what undo restores.
public class KaraokeLyricsTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static LyricLine Untimed(double start, double end, string text) =>
        new(S(start), S(end), text, [new LyricWord(0, text.Length, S(start), S(end))], false);

    private static KaraokeLyrics Parse(string lrc) => new(LrcParser.Parse(lrc).Lines);

    [Test]
    public void LinesAreSortedAndEndBeforeTheNextOneStarts()
    {
        var lyrics = new KaraokeLyrics([Untimed(5, 9, "に"), Untimed(1, 7, "いち"), Untimed(9, 10, "さん")]);

        Assert.That(lyrics.Lines.Select(l => l.Text), Is.EqualTo(new[] { "いち", "に", "さん" }));
        // "いち" ran past the start of "に"; LRC cannot express that, so it ends where "に" starts.
        Assert.That(lyrics.Lines[0].End, Is.EqualTo(S(5)));
        Assert.That(lyrics.Lines[0].Words.Single().End, Is.EqualTo(S(5)));
    }

    [Test]
    public void TimesAreWholeMillisecondsAndTextIsOneTrimmedLine()
    {
        var lyrics = new KaraokeLyrics([Untimed(1.23456, 2.5, "  二行の\n歌詞  "), Untimed(3, 4, "   ")]);

        LyricLine line = lyrics.Lines.Single();
        Assert.That(line.Start, Is.EqualTo(TimeSpan.FromMilliseconds(1235)));
        Assert.That(line.Text, Is.EqualTo("二行の 歌詞"));
    }

    [Test]
    public void TimedRunsCoverTheTextWithoutGaps()
    {
        // Runs given with a gap between them and an end that overlaps the next run.
        var line = new LyricLine(S(1), S(5), "あいう",
            [new LyricWord(0, 1, S(1), S(1.2)), new LyricWord(1, 1, S(1.5), S(2.5)), new LyricWord(2, 1, S(2), S(3))], true);

        LyricLine normal = new KaraokeLyrics([line]).Lines.Single();

        Assert.That(normal.Words.Select(w => (w.Start.TotalSeconds, w.End.TotalSeconds)),
            Is.EqualTo(new[] { (1.0, 1.5), (1.5, 2.0), (2.0, 3.0) }));
    }

    [Test]
    public void AlreadyNormalLinesKeepTheirInstance()
    {
        KaraokeLyrics lyrics = Parse("[00:01.000]いち\n[00:03.000]<00:03.000>に<00:03.500>い<00:04.000>\n[00:05.000]\n");
        var again = new KaraokeLyrics(lyrics.Lines);

        Assert.That(again.Lines[0], Is.SameAs(lyrics.Lines[0]));
        Assert.That(again.Lines[1], Is.SameAs(lyrics.Lines[1]));
    }

    [Test]
    public void LrcRoundTripKeepsTimingGapsAndTheLastEnd()
    {
        var lyrics = new KaraokeLyrics(
        [
            new LyricLine(S(1), S(3), "きみ", [new LyricWord(0, 1, S(1), S(1.4)), new LyricWord(1, 1, S(1.4), S(2.2))], true),
            // Gaps after both lines: each ends before the next one starts, so an empty "[end]" line follows it.
            Untimed(4, 6, "間奏の前"),
            Untimed(8, 12.345, "最後の行"),
        ]);

        IReadOnlyList<string> lrc = LrcWriter.ToLines(lyrics.Lines);
        var restored = Parse(string.Join('\n', lrc));

        Assert.That(lrc, Is.EqualTo(new[]
        {
            "[00:01.000]<00:01.000>き<00:01.400>み<00:02.200>",
            "[00:03.000]",
            "[00:04.000]間奏の前",
            "[00:06.000]",
            "[00:08.000]最後の行",
            "[00:12.345]",
        }));
        Assert.That(restored, Is.EqualTo(lyrics));
    }

    [Test]
    public void JsonStoresLrcLines()
    {
        KaraokeLyrics lyrics = Parse("[00:01.000]<00:01.000>Hello <00:01.500>world<00:02.000>\n[01:05.250]一分後\n[01:09.000]\n");

        string json = JsonSerializer.Serialize(lyrics);
        var restored = JsonSerializer.Deserialize<KaraokeLyrics>(json);

        using (JsonDocument document = JsonDocument.Parse(json))
            Assert.That(document.RootElement[0].GetString(), Is.EqualTo("[00:01.000]<00:01.000>Hello <00:01.500>world<00:02.000>"));
        Assert.That(restored, Is.EqualTo(lyrics));
        Assert.That(JsonSerializer.Deserialize<KaraokeLyrics>("null"), Is.Null.Or.EqualTo(KaraokeLyrics.Empty));
        Assert.That(() => JsonSerializer.Deserialize<KaraokeLyrics>("[1]"), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void EqualityComparesLinesByValue()
    {
        Assert.That(Parse("[00:01.00]あ\n[00:02.00]い"), Is.EqualTo(Parse("[00:01.000]あ\n[00:02.000]い")));
        Assert.That(Parse("[00:01.00]あ"), Is.Not.EqualTo(Parse("[00:01.00]い")));
        Assert.That(Parse("[00:01.00]あ\n[00:02.00]い").RemoveAt([0]).Lines.Single().Text, Is.EqualTo("い"));
    }

    [TestCase("1:02.5", 62.5)]
    [TestCase("62.5", 62.5)]
    [TestCase("1:00:01", 3601)]
    public void ParsesTimes(string text, double seconds)
    {
        Assert.That(LrcWriter.TryParseTime(text, out TimeSpan time), Is.True);
        Assert.That(time, Is.EqualTo(S(seconds)));
        Assert.That(LrcWriter.TryParseTime(LrcWriter.FormatTime(time), out TimeSpan back), Is.True);
        Assert.That(back, Is.EqualTo(time));
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("-1")]
    [TestCase("1:2:3:4")]
    public void RejectsUnreadableTimes(string text)
    {
        Assert.That(LrcWriter.TryParseTime(text, out _), Is.False);
    }
}

public class KaraokeSegmenterTests
{
    private static string[] Units(string text) =>
        KaraokeSegmenter.Split(text).Select(u => text.Substring(u.Start, u.Length)).ToArray();

    [Test]
    public void SmallKanaJoinTheBeatBefore()
    {
        Assert.That(Units("きゃりーぱみゅぱみゅ"), Is.EqualTo(new[] { "きゃ", "り", "ー", "ぱ", "みゅ", "ぱ", "みゅ" }));
    }

    [Test]
    public void LatinWordsAreOneUnitWithTheirSpace()
    {
        Assert.That(Units("Hello my friend"), Is.EqualTo(new[] { "Hello ", "my ", "friend" }));
        Assert.That(Units("I'm 20"), Is.EqualTo(new[] { "I'm ", "20" }));
    }

    [Test]
    public void PunctuationAndBracketsStickToTheirNeighbours()
    {
        Assert.That(Units("「君の名は」"), Is.EqualTo(new[] { "「君", "の", "名", "は」" }));
        Assert.That(Units("夜空に、星！"), Is.EqualTo(new[] { "夜", "空", "に、", "星！" }));
    }

    [Test]
    public void KeepsSurrogatePairsWhole()
    {
        Assert.That(Units("歌🎵"), Is.EqualTo(new[] { "歌", "🎵" }));
    }

    [Test]
    public void UnitsCoverTheWholeText()
    {
        foreach (string text in new[] { " 前の空白", "（括弧だけ", "後ろの空白 ", "「" })
        {
            List<(int Start, int Length)> units = KaraokeSegmenter.Split(text);
            Assert.That(string.Concat(units.Select(u => text.Substring(u.Start, u.Length))), Is.EqualTo(text), text);
        }
    }
}

public class LyricsImportTests
{
    [Test]
    public void TimedLrcIsKeptAsIs()
    {
        List<LyricLine> lines = LyricsImport.Parse("[00:01.00]いち\n[00:02.00]に", out bool timed);

        Assert.That(timed, Is.True);
        Assert.That(lines.Select(l => l.Start.TotalSeconds), Is.EqualTo(new[] { 1.0, 2.0 }));
    }

    [Test]
    public void PlainTextBecomesEvenlySpacedLines()
    {
        List<LyricLine> lines = LyricsImport.Parse("\uFEFF[ti:タイトル]\n一行目\n\n  二行目  \r\n三行目", out bool timed);

        Assert.That(timed, Is.False);
        Assert.That(lines.Select(l => l.Text), Is.EqualTo(new[] { "一行目", "二行目", "三行目" }));
        Assert.That(lines.Select(l => l.Start.TotalSeconds), Is.EqualTo(new[] { 0.0, 4.0, 8.0 }));
        Assert.That(lines.All(l => !l.HasWordTiming), Is.True);
    }
}
