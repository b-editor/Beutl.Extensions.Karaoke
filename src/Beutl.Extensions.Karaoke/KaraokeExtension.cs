using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Services;

namespace Beutl.Extensions.Karaoke;

[Export]
public sealed class KaraokeExtension : Extension
{
    public override string Name => "Karaoke";

    public override string DisplayName => "カラオケ字幕";

    public override void Load()
    {
        base.Load();
        LibraryService.Current.RegisterGroup("カラオケ字幕", g => g
            // BindDrawable also binds the EngineObject format, which the timeline requires to accept a drop.
            .AddMultiple("カラオケ字幕", "取り込んだ歌詞を表示し、歌った部分から色を変えます", m => m.BindDrawable<KaraokeDrawable>()));
    }
}
