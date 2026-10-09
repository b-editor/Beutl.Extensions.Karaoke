using Beutl.Services;
using Beutl.Extensions.Karaoke.Graphics;

namespace Beutl.Extensions.Karaoke.Tests;

public class LibraryRegistrationTests
{
    [Test]
    public void LibraryItemCanBeDroppedOnTheTimeline()
    {
        new KaraokeExtension().Load();

        // The timeline only accepts library items that carry the EngineObject format; the Drawable format alone
        // shows in the library but is refused on drop.
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.EngineObject), Does.Contain(typeof(KaraokeDrawable)));
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Does.Contain(typeof(KaraokeDrawable)));
    }
}
