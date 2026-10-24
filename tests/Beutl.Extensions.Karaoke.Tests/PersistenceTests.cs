using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensions.Karaoke.Graphics;
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
            Assert.That(json["minAppVersion"]!.GetValue<string>(), Is.EqualTo("2.0.0-preview.9"));
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
}
