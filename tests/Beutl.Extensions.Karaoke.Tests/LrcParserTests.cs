using System.Text;
using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Tests;

public class LrcParserTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void ParsesSimpleLinesAndIdTags()
    {
        LyricsDocument document = LrcParser.Parse("""
            [ti:テスト曲]
            [ar:歌手]
            [00:01.00]一行目
            [00:03.50]二行目
            [00:06]三行目
            """);

        Assert.That(document.Title, Is.EqualTo("テスト曲"));
        Assert.That(document.Artist, Is.EqualTo("歌手"));
        Assert.That(document.Lines.Select(l => l.Text), Is.EqualTo(new[] { "一行目", "二行目", "三行目" }));
        Assert.That(document.Lines[0].Start, Is.EqualTo(S(1)));
        Assert.That(document.Lines[0].End, Is.EqualTo(S(3.5)));
        Assert.That(document.Lines[2].End, Is.EqualTo(S(6) + LrcParser.LastLineLength));
        Assert.That(document.Lines[0].HasWordTiming, Is.False);
        Assert.That(document.Lines[0].Words.Single(), Is.EqualTo(new LyricWord(0, 3, S(1), S(3.5))));
    }

    [Test]
    public void RepeatedTimeTagsExpandIntoSortedLines()
    {
        LyricsDocument document = LrcParser.Parse("""
            [00:10.00][00:30.00]サビ
            [00:20.00]Aメロ
            """);

        Assert.That(document.Lines.Select(l => (l.Start, l.Text)),
            Is.EqualTo(new[] { (S(10), "サビ"), (S(20), "Aメロ"), (S(30), "サビ") }));
    }

    [Test]
    public void EmptyLineEndsThePreviousLine()
    {
        LyricsDocument document = LrcParser.Parse("""
            [00:01.00]歌
            [00:04.00]
            [00:10.00]次
            """);

        Assert.That(document.Lines, Has.Count.EqualTo(2));
        Assert.That(document.Lines[0].End, Is.EqualTo(S(4)));
    }

    [Test]
    public void ParsesEnhancedWordTiming()
    {
        LyricsDocument document = LrcParser.Parse("""
            [00:01.00]<00:01.00>あ<00:01.50>い<00:02.00>うえ<00:03.00>
            [00:05.00]次の行
            """);

        LyricLine line = document.Lines[0];
        Assert.That(line.Text, Is.EqualTo("あいうえ"));
        Assert.That(line.HasWordTiming, Is.True);
        Assert.That(line.Words, Is.EqualTo(new[]
        {
            new LyricWord(0, 1, S(1), S(1.5)),
            new LyricWord(1, 1, S(1.5), S(2)),
            new LyricWord(2, 2, S(2), S(3)),
        }));
        Assert.That(line.SungEnd, Is.EqualTo(S(3)));
        Assert.That(line.End, Is.EqualTo(S(5)));
    }

    [Test]
    public void TextBeforeTheFirstWordTagStartsWithTheLine()
    {
        LyricLine line = LrcParser.Parse("[00:02.00]前<00:03.00>後").Lines.Single();

        Assert.That(line.Words, Is.EqualTo(new[]
        {
            new LyricWord(0, 1, S(2), S(3)),
            new LyricWord(1, 1, S(3), S(7)),
        }));
    }

    [Test]
    public void OffsetMovesLyricsEarlier()
    {
        LyricLine line = LrcParser.Parse("""
            [offset:+500]
            [00:02.00]<00:02.00>あ<00:03.00>
            """).Lines.Single();

        Assert.That(line.Start, Is.EqualTo(S(1.5)));
        Assert.That(line.Words[0].Start, Is.EqualTo(S(1.5)));
        Assert.That(line.Words[0].End, Is.EqualTo(S(2.5)));
    }

    [Test]
    public void AcceptsThreeDigitFractionsAndColonSeparators()
    {
        LyricsDocument document = LrcParser.Parse("[00:01.250]a\n[00:02:50]b");
        Assert.That(document.Lines.Select(l => l.Start), Is.EqualTo(new[] { S(1.25), S(2.5) }));
    }

    [Test]
    public void DecodesShiftJisFiles()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(932).GetBytes("[00:01.00]カラオケ");
        Assert.That(TextFileDecoder.Decode(bytes), Is.EqualTo("[00:01.00]カラオケ"));
        Assert.That(TextFileDecoder.Decode(Encoding.UTF8.GetBytes("[00:01.00]歌詞")), Is.EqualTo("[00:01.00]歌詞"));
    }
}
