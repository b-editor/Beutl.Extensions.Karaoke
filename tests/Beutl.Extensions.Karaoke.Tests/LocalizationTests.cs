using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Beutl.Extensions.Karaoke.Editing;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Localization;
using Beutl.Extensions.Karaoke.Lyrics;

namespace Beutl.Extensions.Karaoke.Tests;

[SetCulture("fr-FR")]
public class LocalizationTests
{
    [TestCase("en-US", "Karaoke subtitles", "Single line")]
    [TestCase("ja-JP", "カラオケ字幕", "1行")]
    [TestCase("zh-CN", "卡拉 OK 字幕", "单行")]
    [TestCase("ko-KR", "노래방 자막", "한 줄")]
    [TestCase("es-ES", "Subtítulos de karaoke", "Una línea")]
    [TestCase("es-MX", "Subtítulos de karaoke", "Una línea")]
    [TestCase("de-DE", "Karaoke subtitles", "Single line")]
    public void UsesTheHostsUICultureAndFallsBackToEnglish(string culture, string title, string singleLine)
    {
        using var scope = new UiCultureScope(culture);

        Assert.That(Strings.KaraokeTitle, Is.EqualTo(title));
        Assert.That(new KaraokeExtension().DisplayName, Is.EqualTo(title));
        Assert.That(new KaraokeTabExtension().DisplayName, Is.EqualTo(title));
        Assert.That(typeof(KaraokeDrawable).GetCustomAttribute<DisplayAttribute>()!.GetName(), Is.EqualTo(title));
        Assert.That(typeof(KaraokeLayout).GetField(nameof(KaraokeLayout.SingleLine))!
            .GetCustomAttribute<DisplayAttribute>()!.GetName(), Is.EqualTo(singleLine));

        // Beutl reads these attributes for its property panel. The resource class and getters must be public.
        foreach (PropertyInfo property in typeof(KaraokeDrawable).GetProperties())
        {
            if (property.GetCustomAttribute<DisplayAttribute>() is not { ResourceType: not null } display)
                continue;
            Assert.That(display.GetName(), Is.Not.Null.And.Not.Empty, property.Name);
            if (display.Description != null)
                Assert.That(display.GetDescription(), Is.Not.Null.And.Not.Empty, property.Name);
        }
    }

    [TestCase("ja")]
    [TestCase("zh-CN")]
    [TestCase("ko")]
    [TestCase("es")]
    public void SatelliteResourcesHaveAllKeysAndMatchingFormatArguments(string culture)
    {
        var neutral = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var localized = Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false);
        Assert.That(localized, Is.Not.Null, "The language must ship its own satellite assembly.");
        var expected = neutral.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        var actual = localized!.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
        Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));

        foreach ((string key, string text) in actual)
        {
            Assert.That(text, Is.Not.Empty, key);
            string[] Arguments(string value) => Regex.Matches(value, @"\{(\d+)(?:[^}]*?)\}")
                .Select(m => m.Groups[1].Value).Order().ToArray();
            Assert.That(Arguments(text), Is.EqualTo(Arguments(expected[key])), key);
            Assert.DoesNotThrow(() => string.Format(CultureInfo.CurrentCulture, text, 1234), key);
        }
    }
}

internal sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public UiCultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
