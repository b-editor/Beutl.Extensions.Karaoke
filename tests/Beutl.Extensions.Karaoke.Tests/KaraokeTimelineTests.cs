using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Tests;

public class KaraokeTimelineTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static readonly LyricsDocument s_song = LrcParser.Parse("""
        [00:02.00]<00:02.00>あ<00:03.00>い<00:04.00>
        [00:05.00]<00:05.00>う<00:06.00>
        [00:08.00]<00:08.00>え<00:09.00>
        [00:12.00]<00:12.00>お<00:13.00>
        """);

    [Test]
    public void SungCharactersMoveLinearlyWithinEachRun()
    {
        LyricLine line = s_song.Lines[0];

        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(1)), Is.EqualTo(0));
        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(2.5)), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(3)), Is.EqualTo(1));
        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(3.25)), Is.EqualTo(1.25).Within(1e-9));
        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(10)), Is.EqualTo(2));
    }

    [Test]
    public void PauseBetweenRunsHoldsThePosition()
    {
        LyricLine line = LrcParser.Parse("[00:00.00]<00:00.00>a<00:01.00>b<00:02.00> <00:05.00>c<00:06.00>").Lines.Single();

        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(3)), Is.EqualTo(2 + 1.0 / 3).Within(1e-9));
        Assert.That(KaraokeTimeline.GetSungCharacters(line, S(5.5)), Is.EqualTo(3.5).Within(1e-9));
    }

    [Test]
    public void SingleLineShowsOneLineAtATime()
    {
        LineSchedule[] schedule = KaraokeTimeline.Schedule(s_song.Lines, KaraokeLayout.SingleLine, S(1), S(0.5));

        Assert.That(schedule.Select(s => s.Row), Is.All.EqualTo(0));
        // Line 0 is sung until 4 s and stays 0.5 s; line 1 may appear 1 s before 5 s but only once the row is free.
        Assert.That(schedule[0], Is.EqualTo(new LineSchedule(0, 0, S(1), S(4.5))));
        Assert.That(schedule[1], Is.EqualTo(new LineSchedule(1, 0, S(4.5), S(6.5))));
        Assert.That(schedule[2].Show, Is.EqualTo(S(7)));
        AssertNoOverlapPerRow(schedule);
    }

    [Test]
    public void TwoLinesAlternateRows()
    {
        LineSchedule[] schedule = KaraokeTimeline.Schedule(s_song.Lines, KaraokeLayout.TwoLines, S(3), S(0.5));

        Assert.That(schedule.Select(s => s.Row), Is.EqualTo(new[] { 0, 1, 0, 1 }));
        // Line 2 takes row 0 after line 0 leaves at 4.5 s, earlier than its 8 s start minus the 3 s lead.
        Assert.That(schedule[2].Show, Is.EqualTo(S(5)));
        Assert.That(schedule[1].Show, Is.EqualTo(S(2)));
        AssertNoOverlapPerRow(schedule);
    }

    [Test]
    public void LineLeavesItsRowBeforeTheNextOneInThatRowStarts()
    {
        LyricsDocument dense = LrcParser.Parse("""
            [00:00.00]<00:00.00>a<00:03.00>
            [00:01.00]b
            [00:02.00]c
            """);

        LineSchedule[] schedule = KaraokeTimeline.Schedule(dense.Lines, KaraokeLayout.TwoLines, S(1), S(2));

        // Line 0 would stay until 5 s, but line 2 needs row 0 at its 2 s start.
        Assert.That(schedule[0].Hide, Is.EqualTo(S(2)));
        Assert.That(schedule[2].Show, Is.EqualTo(S(2)));
        AssertNoOverlapPerRow(schedule);
    }

    private static void AssertNoOverlapPerRow(LineSchedule[] schedule)
    {
        foreach (IGrouping<int, LineSchedule> row in schedule.GroupBy(s => s.Row))
        {
            LineSchedule[] entries = row.OrderBy(s => s.Show).ToArray();
            for (int i = 1; i < entries.Length; i++)
                Assert.That(entries[i].Show, Is.GreaterThanOrEqualTo(entries[i - 1].Hide));
        }

        foreach (LineSchedule entry in schedule)
            Assert.That(entry.Hide, Is.GreaterThanOrEqualTo(entry.Show));
    }
}
