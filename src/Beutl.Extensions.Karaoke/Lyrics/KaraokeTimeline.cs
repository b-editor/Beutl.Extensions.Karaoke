namespace Beutl.Extensions.Karaoke.Lyrics;

public enum KaraokeLayout
{
    /// <summary>One line at a time, centered.</summary>
    SingleLine,

    /// <summary>Two alternating rows, the upper one left-aligned and the lower one right-aligned.</summary>
    TwoLines,
}

/// <summary>When a line is on screen and in which row.</summary>
public readonly record struct LineSchedule(int LineIndex, int Row, TimeSpan Show, TimeSpan Hide);

/// <summary>Decides which lines are on screen and how far each has been sung.</summary>
public static class KaraokeTimeline
{
    /// <param name="lines">The lyrics, sorted by start.</param>
    /// <param name="layout">The row arrangement.</param>
    /// <param name="lead">How long before its start a line may appear.</param>
    /// <param name="tail">How long a line stays after its last character has been sung.</param>
    public static LineSchedule[] Schedule(IReadOnlyList<LyricLine> lines, KaraokeLayout layout, TimeSpan lead, TimeSpan tail)
    {
        int rows = layout == KaraokeLayout.TwoLines ? 2 : 1;
        var hide = new TimeSpan[lines.Count];
        var schedule = new LineSchedule[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            // A line gives up its row no later than the start of the line that takes the row next...
            TimeSpan h = lines[i].SungEnd + tail;
            if (i + rows < lines.Count && lines[i + rows].Start < h)
                h = lines[i + rows].Start;

            // ...and appears once the row is free, but never after it starts being sung.
            TimeSpan show = lines[i].Start - lead;
            if (i >= rows && hide[i - rows] > show)
                show = hide[i - rows];

            hide[i] = h > show ? h : show;
            schedule[i] = new LineSchedule(i, i % rows, show, hide[i]);
        }

        return schedule;
    }

    /// <summary>
    /// How far a line has been sung at <paramref name="time"/>, as a position in characters: within a run the
    /// position moves linearly, so 2.5 means two characters and half of the third.
    /// </summary>
    public static double GetSungCharacters(LyricLine line, TimeSpan time)
    {
        double sung = 0;
        foreach (LyricWord word in line.Words)
        {
            if (time >= word.End)
            {
                sung = word.CharStart + word.Length;
                continue;
            }

            if (time > word.Start)
            {
                double progress = (time - word.Start).TotalSeconds / (word.End - word.Start).TotalSeconds;
                return word.CharStart + word.Length * progress;
            }

            return sung;
        }

        return sung;
    }
}
