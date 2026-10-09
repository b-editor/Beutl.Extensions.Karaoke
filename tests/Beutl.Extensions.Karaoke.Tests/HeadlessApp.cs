using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Beutl.Extensions.Karaoke.Tests;

[assembly: AvaloniaTestApplication(typeof(HeadlessApp))]

namespace Beutl.Extensions.Karaoke.Tests;

/// <summary>A headless Avalonia application for exercising the editors without a display.</summary>
public sealed class HeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
