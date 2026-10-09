namespace Beutl.Extensions.Karaoke.Lyrics;

/// <summary>A run of characters sung over one time span.</summary>
/// <param name="CharStart">The index of the first character in <see cref="LyricLine.Text"/>.</param>
/// <param name="Length">The number of characters.</param>
public sealed record LyricWord(int CharStart, int Length, TimeSpan Start, TimeSpan End);

/// <summary>One line of lyrics.</summary>
/// <param name="Start">When singing of the line starts.</param>
/// <param name="End">When the next line starts (or a synthetic end for the last line).</param>
/// <param name="Text">The text without timing tags.</param>
/// <param name="Words">The timed runs covering <paramref name="Text"/> in order.</param>
/// <param name="HasWordTiming">Whether the file gave per-word times; otherwise one run spans the line.</param>
public sealed record LyricLine(TimeSpan Start, TimeSpan End, string Text, IReadOnlyList<LyricWord> Words, bool HasWordTiming)
{
    /// <summary>When the last character has been sung.</summary>
    public TimeSpan SungEnd => Words.Count > 0 ? Words[^1].End : End;
}

public sealed record LyricsDocument(IReadOnlyList<LyricLine> Lines, string? Title, string? Artist);
