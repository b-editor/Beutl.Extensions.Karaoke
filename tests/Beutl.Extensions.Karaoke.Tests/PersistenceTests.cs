using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Extensions.Karaoke.Tests;

public class PersistenceTests
{
    [Test]
    public void Preview8LyricsOffsetAndKeyFramesSurviveFileMigration()
    {
        const string earlier = """
            {
              "minAppVersion": "2.0.0-preview.8",
              "Start": "00:00:01",
              "Length": "00:00:10",
              "Objects": [{
                "$type": "[Beutl.Extensions.Karaoke]Beutl.Extensions.Karaoke.Graphics:KaraokeDrawable",
                "Id": "40a15022-8a2c-4b63-aea8-5ab4aed3b74b",
                "Lyrics": ["[00:01.000]<00:01.000>歌<00:02.000>詞<00:03.000>", "[00:04.000]"],
                "TimeOffset": "00:00:00.5000000",
                "Animations": {
                  "Opacity": {
                    "$type": "[Beutl.Engine]Beutl.Animation:KeyFrameAnimation<[System.Private.CoreLib]System:Single>",
                    "UseGlobalClock": false,
                    "KeyFrames": [{
                      "$type": "[Beutl.Engine]Beutl.Animation:KeyFrame<[System.Private.CoreLib]System:Single>",
                      "KeyTime": "00:00:02",
                      "Value": 50
                    }]
                  }
                }
              }]
            }
            """;
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var uri = new Uri(Path.Combine(directory, "karaoke.belm"));
        try
        {
            File.WriteAllText(uri.LocalPath, earlier);
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            var drawable = (KaraokeDrawable)element.Objects.Single();
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
            Assert.That(drawable.Id.Value, Is.EqualTo(Guid.Parse("40a15022-8a2c-4b63-aea8-5ab4aed3b74b")));
            Assert.That(drawable.TimeOffset.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(drawable.Lyrics.Value.Lines.Single().Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 1.0, 2.0 }));
            Assert.That(drawable.TimeOffset.IsAnimatable, Is.False);
            var animation = drawable.Opacity.GetKeyFrameAnimation();
            Assert.That(animation, Is.TypeOf<KeyFrameAnimation<float>>());
            var keyFrame = (KeyFrame<float>)animation!.KeyFrames.Single();
            Assert.That(keyFrame.KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(keyFrame.Value.Value, Is.EqualTo(50f));

            CoreSerializer.StoreToUri(element, uri);
            var json = JsonNode.Parse(File.ReadAllText(uri.LocalPath))!;
            Assert.That(json["minAppVersion"]!.GetValue<string>(), Is.EqualTo("2.0.0-preview.11"));
            Assert.That(json["Start"]!.GetValue<long>(), Is.EqualTo(element.Start.Value.Flicks));
            Assert.That(json["Objects"]![0]!["TimeOffset"]!.GetValue<long>(), Is.EqualTo(drawable.TimeOffset.Value.Flicks));
            Assert.That(json["Objects"]![0]!["Lyrics"]!.AsArray().All(line => line is JsonValue value && value.TryGetValue<string>(out _)), Is.True);

            var reread = (KaraokeDrawable)CoreSerializer.RestoreFromUri<Element>(uri).Objects.Single();
            Assert.That(reread.Lyrics.Value, Is.EqualTo(drawable.Lyrics.Value));
            Assert.That(reread.TimeOffset.Value, Is.EqualTo(drawable.TimeOffset.Value));
            Assert.That(reread.Opacity.GetKeyFrameAnimation()!.KeyFrames.Single().KeyTime.Value, Is.EqualTo(keyFrame.KeyTime.Value));
            Assert.That(reread.Opacity.GetKeyFrameAnimation()!.GetAnimatedValue(MediaTime.FromSeconds(2)), Is.EqualTo(50f));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Preview9LyricsPensAndKeyFramesSurviveTheNewFormatFloor()
    {
        // Trimmed from a file preview.9 saved: times are flicks, and pens have no StrokeOrder yet.
        const string earlier = """
            {
              "minAppVersion": "2.0.0-preview.9",
              "Start": 705600000,
              "Length": 8467200000,
              "Objects": [{
                "$type": "[Beutl.Extensions.Karaoke].Graphics:KaraokeDrawable",
                "Id": "40a15022-8a2c-4b63-aea8-5ab4aed3b74b",
                "Animations": {
                  "Opacity": {
                    "$type": "[Beutl.Engine]Beutl.Animation:KeyFrameAnimation<[System.Private.CoreLib]System:Single>",
                    "UseGlobalClock": false,
                    "KeyFrames": [{
                      "$type": "[Beutl.Engine]Beutl.Animation:KeyFrame<[System.Private.CoreLib]System:Single>",
                      "KeyTime": 2822400000,
                      "Value": 100
                    }]
                  }
                },
                "Lyrics": ["[00:00.000]<00:00.000>あいう<00:02.000>", "[00:02.500]つぎ", "[00:04.000]"],
                "TextStroke": {
                  "Brush": { "$type": "[Beutl.Engine]Beutl.Media:SolidColorBrush", "Color": "Black" },
                  "Thickness": 6,
                  "StrokeJoin": 1,
                  "StrokeAlignment": 0
                },
                "TimeOffset": 352800000
              }]
            }
            """;
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var uri = new Uri(Path.Combine(directory, "karaoke.belm"));
        try
        {
            File.WriteAllText(uri.LocalPath, earlier);
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            var drawable = (KaraokeDrawable)element.Objects.Single();
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(drawable.TimeOffset.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(drawable.Lyrics.Value.Lines.Select(l => l.Text), Is.EqualTo(new[] { "あいう", "つぎ" }));
            Assert.That(drawable.Lyrics.Value.Lines[0].HasWordTiming, Is.True);
            Pen stroke = drawable.TextStroke.Value!;
            Assert.That(stroke.Thickness.Value, Is.EqualTo(6f));
            Assert.That(stroke.StrokeJoin.Value, Is.EqualTo(StrokeJoin.Round));
            Assert.That(stroke.StrokeAlignment.Value, Is.EqualTo(StrokeAlignment.Center));
            Assert.That(stroke.StrokeOrder.Value, Is.EqualTo(StrokeOrder.AboveFill));
            Assert.That(drawable.Opacity.GetKeyFrameAnimation()!.KeyFrames.Single().KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));

            CoreSerializer.StoreToUri(element, uri);
            var json = JsonNode.Parse(File.ReadAllText(uri.LocalPath))!;
            JsonNode saved = json["Objects"]![0]!;
            Assert.That(json["minAppVersion"]!.GetValue<string>(), Is.EqualTo("2.0.0-preview.11"));
            Assert.That(saved["Lyrics"]!.AsArray().Select(line => line!.GetValue<string>()),
                Is.EqualTo(new[] { "[00:00.000]<00:00.000>あいう<00:02.000>", "[00:02.500]つぎ", "[00:04.000]" }));
            Assert.That(saved["TimeOffset"]!.GetValue<long>(), Is.EqualTo(352800000L));
            Assert.That(saved["TextStroke"]!["Thickness"]!.GetValue<float>(), Is.EqualTo(6f));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Preview10LyricsPensAndKeyFramesSurviveTheNewFormatFloor()
    {
        // Trimmed from a file preview.10 saved: the lyrics, times and pens keep their form under the new floor.
        const string earlier = """
            {
              "Start": 705600000,
              "Length": 8467200000,
              "Objects": [{
                "Id": "ed64cfd2-27e2-418c-9fe2-cfed426a7ba9",
                "Animations": {
                  "Opacity": {
                    "UseGlobalClock": false,
                    "KeyFrames": [{
                      "Value": 20,
                      "KeyTime": 0,
                      "$type": "[Beutl.Engine]Beutl.Animation:KeyFrame<[System.Private.CoreLib]System:Single>"
                    }, {
                      "Value": 100,
                      "KeyTime": 1411200000,
                      "$type": "[Beutl.Engine]Beutl.Animation:KeyFrame<[System.Private.CoreLib]System:Single>"
                    }],
                    "$type": "[Beutl.Engine]Beutl.Animation:KeyFrameAnimation<[System.Private.CoreLib]System:Single>"
                  }
                },
                "Lyrics": [
                  "[00:00.500]<00:00.500>きら<00:01.200>きら<00:02.000>ひかる<00:03.000>",
                  "[00:03.200]おそらの ほしよ",
                  "[00:06.000]Twinkle, twinkle",
                  "[00:08.500]"
                ],
                "Layout": 1,
                "FontWeight": 400,
                "FontSize": 96,
                "SungStroke": {
                  "Brush": { "Color": "DarkOrange", "$type": "[Beutl.Engine]Beutl.Media:SolidColorBrush" },
                  "Thickness": 10,
                  "StrokeJoin": 1,
                  "StrokeAlignment": 2,
                  "StrokeOrder": 0
                },
                "TimeOffset": 176400000,
                "$type": "[Beutl.Extensions.Karaoke].Graphics:KaraokeDrawable"
              }],
              "minAppVersion": "2.0.0-preview.10"
            }
            """;
        string[] lyrics =
        [
            "[00:00.500]<00:00.500>きら<00:01.200>きら<00:02.000>ひかる<00:03.000>",
            "[00:03.200]おそらの ほしよ",
            "[00:06.000]Twinkle, twinkle",
            "[00:08.500]",
        ];
        string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var uri = new Uri(Path.Combine(directory, "karaoke.belm"));
        try
        {
            File.WriteAllText(uri.LocalPath, earlier);
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            var drawable = (KaraokeDrawable)element.Objects.Single();
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(drawable.TimeOffset.Value, Is.EqualTo(MediaTime.FromSeconds(0.25)));
            Assert.That(drawable.Lyrics.Value.Lines.Select(l => l.Text), Is.EqualTo(new[] { "きらきらひかる", "おそらの ほしよ", "Twinkle, twinkle" }));
            Assert.That(drawable.Lyrics.Value.Lines[0].Words.Select(w => w.Start.TotalSeconds), Is.EqualTo(new[] { 0.5, 1.2, 2.0 }));
            Assert.That(drawable.Layout.Value, Is.EqualTo(KaraokeLayout.TwoLines));
            Assert.That(drawable.FontWeight.Value, Is.EqualTo(FontWeight.Regular));
            Assert.That(drawable.FontSize.Value, Is.EqualTo(96f));
            Pen stroke = drawable.SungStroke.Value!;
            Assert.That(stroke.Thickness.Value, Is.EqualTo(10f));
            Assert.That(stroke.StrokeAlignment.Value, Is.EqualTo(StrokeAlignment.Outside));
            Assert.That(drawable.Opacity.GetKeyFrameAnimation()!.KeyFrames.Cast<KeyFrame<float>>().Select(k => (k.KeyTime.Value, k.Value.Value)),
                Is.EqualTo(new[] { (MediaTime.Zero, 20f), (MediaTime.FromSeconds(2), 100f) }));

            CoreSerializer.StoreToUri(element, uri);
            var json = JsonNode.Parse(File.ReadAllText(uri.LocalPath))!;
            JsonNode saved = json["Objects"]![0]!;
            Assert.That(json["minAppVersion"]!.GetValue<string>(), Is.EqualTo("2.0.0-preview.11"));
            Assert.That(saved["Lyrics"]!.AsArray().Select(line => line!.GetValue<string>()), Is.EqualTo(lyrics));
            Assert.That(saved["TimeOffset"]!.GetValue<long>(), Is.EqualTo(176400000L));
            Assert.That(saved["SungStroke"]!["Thickness"]!.GetValue<float>(), Is.EqualTo(10f));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
