using Beutl.Services;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Media;

namespace Beutl.Extensions.Karaoke.Tests;

public class LibraryRegistrationTests
{
    [Test]
    public void LibraryItemCanBeDroppedOnTheTimeline()
    {
        using var library = new LibraryService();
        new KaraokeExtension().Load(TestHost.CreateContext(library));

        // The timeline only accepts library items that carry the EngineObject format; the Drawable format alone
        // shows in the library but is refused on drop.
        Assert.That(library.GetTypesFromFormat(KnownLibraryItemFormats.EngineObject), Does.Contain(typeof(KaraokeDrawable)));
        Assert.That(library.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Does.Contain(typeof(KaraokeDrawable)));
    }

    [Test]
    public void NewObjectsUseNotoSansJpWhenTheHostHasIt()
    {
        var notoSansJp = new FontFamily("Noto Sans JP");
        Assume.That(TestHost.Fonts.IsRegistered(notoSansJp), Is.True, "Needs Noto Sans JP among the installed fonts.");
        using var library = new LibraryService();
        new KaraokeExtension().Load(TestHost.CreateContext(library));

        // Beutl bundles the font, so in the app a new object starts with it.
        Assert.That(new KaraokeDrawable().FontFamily.Value, Is.EqualTo(notoSansJp));
    }
}
