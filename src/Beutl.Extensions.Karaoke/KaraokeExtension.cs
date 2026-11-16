using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Extensions.Karaoke.Localization;
using Beutl.Services;

namespace Beutl.Extensions.Karaoke;

[Export]
public sealed class KaraokeExtension : Extension
{
    public override string Name => "Karaoke";

    public override string DisplayName => Strings.KaraokeTitle;

    public override void Load()
    {
        base.Load();
        LibraryService.Current.RegisterGroup(Strings.KaraokeTitle, g => g
            // BindDrawable also binds the EngineObject format, which the timeline requires to accept a drop.
            .AddMultiple(Strings.KaraokeTitle, Strings.KaraokeDescription, m => m.BindDrawable<KaraokeDrawable>()));
    }
}
