using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.ProjectSystem;

namespace Beutl.Extensions.Karaoke.Tests;

// The timeline's "元の長さに変更" and the clamp-to-original-length setting read these values through Element.
public class OriginalDurationTests
{
    private static KaraokeLyrics Lyrics(string lrc) => new(LrcParser.Parse(lrc).Lines);

    [Test]
    public void LastsUntilTheLastLineLeaves()
    {
        var drawable = new KaraokeDrawable();
        // The last line is sung by 8 s and stays for the 0.8 s tail.
        drawable.Lyrics.CurrentValue = Lyrics("[00:01.00]いち\n[00:05.00]<00:05.00>に<00:08.00>\n");
        var element = new Element { Length = TimeSpan.FromSeconds(1) };
        element.AddObject(drawable);

        Assert.That(element.TryGetOriginalDuration(out TimeSpan duration), Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(8.8)).Within(TimeSpan.FromMilliseconds(1)));

        drawable.TimeOffset.CurrentValue = TimeSpan.FromSeconds(2);
        drawable.TailTime.CurrentValue = 0f;
        Assert.That(element.TryGetOriginalDuration(out duration), Is.True);
        Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(10)).Within(TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public void HasNoOriginalDurationWithoutLyrics()
    {
        var drawable = new KaraokeDrawable();
        Assert.That(drawable.HasOriginalDuration(), Is.False);
        Assert.That(drawable.TryGetOriginalDuration(out _), Is.False);

        drawable.Lyrics.CurrentValue = Lyrics("[00:01.00]ある");
        Assert.That(drawable.HasOriginalDuration(), Is.True);
    }
}
