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
        drawable.Lyrics.Value = Lyrics("[00:01.00]いち\n[00:05.00]<00:05.00>に<00:08.00>\n");
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) } };
        element.AddObject(drawable);

        Assert.That(element.TryGetOriginalDuration(out MediaTime duration), Is.True);
        Assert.That(duration.ToSeconds(), Is.EqualTo(8.8).Within(0.001));

        drawable.TimeOffset.Value = MediaTime.FromSeconds(2);
        drawable.TailTime.Value = 0f;
        Assert.That(element.TryGetOriginalDuration(out duration), Is.True);
        Assert.That(duration.ToSeconds(), Is.EqualTo(10).Within(0.001));
    }

    [Test]
    public void HasNoOriginalDurationWithoutLyrics()
    {
        var drawable = new KaraokeDrawable();
        Assert.That(drawable.HasOriginalDuration(), Is.False);
        Assert.That(drawable.TryGetOriginalDuration(out _), Is.False);

        drawable.Lyrics.Value = Lyrics("[00:01.00]ある");
        Assert.That(drawable.HasOriginalDuration(), Is.True);
    }
}
