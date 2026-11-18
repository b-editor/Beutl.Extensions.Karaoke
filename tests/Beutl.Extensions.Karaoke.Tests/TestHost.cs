using Beutl.Configuration;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Extensions.Karaoke.Tests;

// Loads the extension once before any test, as Beutl does at startup, so new objects get the defaults they get in
// the app.
[SetUpFixture]
public sealed class TestHost
{
    // The installed fonts, read from the folders Beutl's font settings list by default.
    private static readonly Lazy<FontManager> s_fonts = new(() => new FontManager(new FontConfig()));

    private readonly LibraryService _library = new();

    public static FontManager Fonts => s_fonts.Value;

    // The services Beutl passes to Extension.Load, with the given library.
    public static IExtensionContext CreateContext(LibraryService library) =>
        new ExtensionContext(new ServiceCollection().AddSingleton(Fonts).AddSingleton(library).BuildServiceProvider());

    [OneTimeSetUp]
    public void LoadExtension()
    {
        new KaraokeExtension().Load(CreateContext(_library));
    }

    [OneTimeTearDown]
    public void DisposeLibrary()
    {
        _library.Dispose();
    }
}
