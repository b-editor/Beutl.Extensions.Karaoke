using System.Text.Json.Nodes;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Extensions.Karaoke.Tests;

// Renders the drawable through Beutl's own scene pipeline on a 1920x1080 scene. Outlines are removed so
// every text pixel is either the unsung white or the sung red.
public class KaraokeRenderTests
{
    private static KaraokeLyrics Lyrics(string lrc) => new(LrcParser.Parse(lrc).Lines);

    private static KaraokeDrawable CreateDrawable(string lrc)
    {
        var drawable = new KaraokeDrawable();
        drawable.Lyrics.Value = Lyrics(lrc);
        drawable.Layout.Value = KaraokeLayout.SingleLine;
        drawable.FadeDuration.Value = 0f;
        drawable.TextFill.Value = new SolidColorBrush(Colors.White);
        drawable.SungFill.Value = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
        drawable.TextStroke.Value = null;
        drawable.SungStroke.Value = null;
        return drawable;
    }

    private static (int MinX, int MaxX, int Red, int White, int RedMaxX, int WhiteMinX) Analyze(Bitmap frame)
    {
        int minX = int.MaxValue, maxX = -1, red = 0, white = 0, redMaxX = -1, whiteMinX = int.MaxValue;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                (byte r, byte g, byte b, byte a) = HeadlessScene.GetPixel(frame, x, y);
                if (a < 200)
                    continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                if (r > 200 && g < 60 && b < 60)
                {
                    red++;
                    redMaxX = Math.Max(redMaxX, x);
                }
                else if (r > 230 && g > 230 && b > 230)
                {
                    white++;
                    whiteMinX = Math.Min(whiteMinX, x);
                }
            }
        }

        return (minX, maxX, red, white, redMaxX, whiteMinX);
    }

    private const string Lyric = "[00:00.00]<00:00.00>あいうえお<00:02.00>\n[00:05.00]つぎ";

    [Test]
    public void SungPartIsRecoloredFromTheLeft()
    {
        Scene scene = HeadlessScene.Create((CreateDrawable(Lyric), MediaTime.FromSeconds(10)));

        using Bitmap frame = HeadlessScene.Render(scene, MediaTime.FromSeconds(1.0));
        HeadlessScene.SaveForInspection(frame, "karaoke-half.png");
        var result = Analyze(frame);

        Assert.That(result.Red, Is.GreaterThan(500));
        Assert.That(result.White, Is.GreaterThan(500));
        // Every red pixel is left of every white one, and the boundary is about half way along the line.
        Assert.That(result.RedMaxX, Is.LessThanOrEqualTo(result.WhiteMinX + 1));
        double boundary = (result.RedMaxX - result.MinX) / (double)(result.MaxX - result.MinX);
        Assert.That(boundary, Is.EqualTo(0.5).Within(0.12));
    }

    [Test]
    public void LineIsUnsungBeforeItStartsAndFullySungAtItsEnd()
    {
        Scene scene = HeadlessScene.Create((CreateDrawable(Lyric), MediaTime.FromSeconds(10)));

        using Bitmap before = HeadlessScene.Render(scene, MediaTime.FromSeconds(-0.0));
        using Bitmap after = HeadlessScene.Render(scene, MediaTime.FromSeconds(2.5));

        Assert.That(Analyze(before).Red, Is.Zero);
        Assert.That(Analyze(before).White, Is.GreaterThan(500));
        Assert.That(Analyze(after).White, Is.Zero);
        Assert.That(Analyze(after).Red, Is.GreaterThan(500));
    }

    [Test]
    public void LineDisappearsAfterTheTailAndTheNextLineAppears()
    {
        Scene scene = HeadlessScene.Create((CreateDrawable(Lyric), MediaTime.FromSeconds(10)));

        // The first line is sung by 2 s and stays 0.8 s; the next line may appear 1.5 s before 5 s.
        using Bitmap gap = HeadlessScene.Render(scene, MediaTime.FromSeconds(3.2));
        using Bitmap next = HeadlessScene.Render(scene, MediaTime.FromSeconds(4.0));

        Assert.That(Analyze(gap).MaxX, Is.EqualTo(-1));
        Assert.That(Analyze(next).White, Is.GreaterThan(200));
    }

    [Test]
    public void TwoLineLayoutUsesTwoRows()
    {
        KaraokeDrawable drawable = CreateDrawable("[00:00.00]いちぎょうめ\n[00:01.00]にぎょうめ\n[00:03.00]");
        drawable.Layout.Value = KaraokeLayout.TwoLines;
        drawable.LeadTime.Value = 2f;
        Scene scene = HeadlessScene.Create((drawable, MediaTime.FromSeconds(10)));

        using Bitmap frame = HeadlessScene.Render(scene, MediaTime.FromSeconds(0.5));
        HeadlessScene.SaveForInspection(frame, "karaoke-two-lines.png");

        // Collect the rows that contain text; two separated bands are expected.
        var rows = Enumerable.Range(0, frame.Height)
            .Where(y => Enumerable.Range(0, frame.Width).Any(x => HeadlessScene.GetPixel(frame, x, y).A > 200))
            .ToArray();
        int gaps = rows.Zip(rows.Skip(1), (a, b) => b - a > 1 ? 1 : 0).Sum();
        Assert.That(gaps, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void DefaultStyleDrawsBothOutlines()
    {
        var drawable = new KaraokeDrawable();
        drawable.Lyrics.Value = Lyrics(Lyric);
        drawable.Layout.Value = KaraokeLayout.SingleLine;
        Scene scene = HeadlessScene.Create((drawable, MediaTime.FromSeconds(10)));

        using Bitmap frame = HeadlessScene.Render(scene, MediaTime.FromSeconds(1.0));
        HeadlessScene.SaveForInspection(frame, "karaoke-default-style.png");

        int navyOutline = 0, sungFill = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                (byte r, byte g, byte b, byte a) = HeadlessScene.GetPixel(frame, x, y);
                if (a < 200)
                    continue;
                if (r < 40 && g < 60 && b > 70 && b < 130)
                    navyOutline++;
                else if (r < 80 && g > 110 && g < 170 && b > 220)
                    sungFill++;
            }
        }

        Assert.That(navyOutline, Is.GreaterThan(300), "unsung outline");
        Assert.That(sungFill, Is.GreaterThan(300), "sung fill");
    }

    [Test]
    public void SurvivesSerializationRoundTrip()
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(5) } };
        KaraokeDrawable original = CreateDrawable(Lyric);
        original.FontSize.Value = 96f;
        element.AddObject(original);

        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (KaraokeDrawable)((Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element))).Objects.Single();

        Assert.That(restored.Lyrics.Value, Is.EqualTo(original.Lyrics.Value));
        Assert.That(restored.Lyrics.Value.Lines.Any(l => l.HasWordTiming), Is.True);
        Assert.That(restored.FontSize.Value, Is.EqualTo(96f));
        Assert.That(restored.Layout.Value, Is.EqualTo(KaraokeLayout.SingleLine));
    }

    [TestCase(30, 1)]
    [TestCase(30000, 1001)]
    public void TheBackHalfOfASplitShowsWhatTheWholeElementShowed(int numerator, int denominator)
    {
        MediaTime at = MediaTime.FromFrames(37, new Rational(numerator, denominator));
        Scene whole = HeadlessScene.Create((CreateDrawable(Lyric), MediaTime.FromSeconds(10)));
        Scene split = HeadlessScene.Create((CreateDrawable(Lyric), MediaTime.FromSeconds(10)));
        Element front = split.Children[0];
        Element back = HeadlessScene.Split(split, front, at);

        var backDrawable = back.Objects.OfType<KaraokeDrawable>().Single();
        Assert.That(backDrawable.TimeOffset.Value, Is.EqualTo(-at));
        Assert.That(front.Objects.OfType<KaraokeDrawable>().Single().TimeOffset.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(backDrawable.Lyrics.Value.Count, Is.EqualTo(2), "Both halves keep every line.");

        // Fitting the back half to its content still ends where the lyrics end.
        whole.Children[0].Objects.OfType<KaraokeDrawable>().Single().TryGetOriginalDuration(out MediaTime wholeDuration);
        Assert.That(backDrawable.TryGetOriginalDuration(out MediaTime backDuration), Is.True);
        Assert.That(backDuration, Is.EqualTo(wholeDuration - at));

        // The cut falls in the middle of the first line, which carries on colouring; then the next line shows.
        foreach (MediaTime time in new[] { at, at + MediaTime.FromSeconds(0.25), MediaTime.FromSeconds(5.5) })
        {
            using Bitmap expected = HeadlessScene.Render(whole, time);
            using Bitmap actual = HeadlessScene.Render(split, time);
            HeadlessScene.AssertSameFrame(expected, actual);
        }
    }
}
