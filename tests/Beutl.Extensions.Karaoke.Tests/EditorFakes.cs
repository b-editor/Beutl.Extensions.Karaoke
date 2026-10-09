using System.Reactive;
using System.Reactive.Linq;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Extensions.Karaoke.Lyrics;
using Beutl.Extensions.Karaoke.Editing;
using Beutl.Extensions.Karaoke.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.Extensions.Karaoke.Tests;

/// <summary>A scene with one karaoke element, the editor services the tool tab reads, and the tab itself.</summary>
internal sealed class EditorFixture : IDisposable
{
    private readonly Dictionary<Type, object> _services = [];
    private readonly CoreObjectOperationObserver _observer;
    private readonly IDisposable _historySubscription;

    public const string DefaultLyrics = "[00:01.000]いち\n[00:03.000]<00:03.000>に<00:03.500>い<00:04.000>\n[00:05.000]さん\n[00:07.000]\n";

    /// <param name="withPlayer">Whether the editor has a preview player the tab can start and watch.</param>
    public EditorFixture(string lyrics = DefaultLyrics, bool withPlayer = false)
    {
        Scene = new Scene(1920, 1080, "Fixture");
        Drawable = new KaraokeDrawable();
        Drawable.Lyrics.CurrentValue = new KaraokeLyrics(LrcParser.Parse(lyrics).Lines);
        Element = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(10), ZIndex = 2 };
        Element.AddObject(Drawable);
        Scene.AddChild(Element);

        // Wired like the editor's EditViewModel: the observer reports changes in the scene to the history.
        var sequence = new OperationSequenceGenerator();
        _observer = new CoreObjectOperationObserver(null, Scene, sequence);
        History = new HistoryManager(Scene, sequence);
        _historySubscription = History.Subscribe(_observer);
        _services[typeof(IEditorSelection)] = Selection;
        _services[typeof(IEditorClock)] = Clock;
        _services[typeof(HistoryManager)] = History;
        _services[typeof(Scene)] = Scene;
        if (withPlayer)
        {
            Player = new FakePlayer();
            _services[typeof(IPreviewPlayer)] = Player;
        }

        Context = new FakeEditorContext(Scene, this);
        ViewModel = new KaraokeTabViewModel(new KaraokeTabExtension(), Context) { Time = Time };
    }

    public Scene Scene { get; }

    public Element Element { get; }

    public KaraokeDrawable Drawable { get; }

    public HistoryManager History { get; }

    public FakeSelection Selection { get; } = new();

    public FakeClock Clock { get; } = new();

    /// <summary>The real time the tab times taps with while playing.</summary>
    public ManualTime Time { get; } = new();

    public FakePlayer? Player { get; }

    public FakeEditorContext Context { get; }

    public KaraokeTabViewModel ViewModel { get; }

    /// <summary>Adds another karaoke element to the scene, as a second object the tab can be opened for.</summary>
    public KaraokeDrawable AddKaraoke(string lyrics, TimeSpan start)
    {
        var drawable = new KaraokeDrawable();
        drawable.Lyrics.CurrentValue = new KaraokeLyrics(LrcParser.Parse(lyrics).Lines);
        var element = new Element { Start = start, Length = TimeSpan.FromSeconds(10), ZIndex = 5 };
        element.AddObject(drawable);
        Scene.AddChild(element);
        return drawable;
    }

    public string[] Texts => Drawable.Lyrics.CurrentValue.Lines.Select(l => l.Text).ToArray();

    public IReadOnlyList<LyricLine> Lines => Drawable.Lyrics.CurrentValue.Lines;

    public object? GetService(Type type) => _services.GetValueOrDefault(type);

    public void Dispose()
    {
        ViewModel.Dispose();
        _historySubscription.Dispose();
        History.Dispose();
        _observer.Dispose();
    }
}

internal sealed class FakeSelection : IEditorSelection
{
    public IReactiveProperty<CoreObject?> SelectedObject { get; } = new ReactivePropertySlim<CoreObject?>();

    public IReadOnlyReactiveProperty<int?> SelectedLayerNumber { get; } = new ReactivePropertySlim<int?>();
}

internal sealed class FakeClock : IEditorClock
{
    public IReactiveProperty<TimeSpan> CurrentTime { get; } = new ReactivePropertySlim<TimeSpan>();

    public IReadOnlyReactiveProperty<TimeSpan> MaximumTime { get; } = new ReactivePropertySlim<TimeSpan>(TimeSpan.FromMinutes(5));
}

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class ManualTime : TimeProvider
{
    public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(100);

    public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Now.Ticks;
}

/// <summary>A preview player that plays when told to; frames are shown by setting the clock.</summary>
internal sealed class FakePlayer : IPreviewPlayer
{
    public ReactivePropertySlim<bool> Playing { get; } = new();

    public int PauseCount { get; private set; }

    public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; } = new ReactivePropertySlim<Ref<Bitmap>?>();

    public IObservable<Unit> AfterRendered => Observable.Never<Unit>();

    public IReadOnlyReactiveProperty<bool> IsPlaying => Playing;

    public Task Pause()
    {
        PauseCount++;
        Playing.Value = false;
        return Task.CompletedTask;
    }
}

internal sealed class FakeEditorContext(CoreObject obj, EditorFixture fixture) : IEditorContext, IContextCommandHandler
{
    /// <summary>The commands the tab asked the editor to run, in order.</summary>
    public List<string> Commands { get; } = [];

    public Task ExecuteAsync(ContextCommandExecution execution)
    {
        Commands.Add(execution.CommandName);
        if (execution.CommandName == "PlayPause" && fixture.Player is { } player)
        {
            if (player.Playing.Value)
                return player.Pause();
            player.Playing.Value = true;
        }

        return Task.CompletedTask;
    }

    public CoreObject Object => obj;

    public EditorExtension Extension => throw new NotSupportedException();

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

    public object? GetService(Type serviceType) => fixture.GetService(serviceType);

    /// <summary>Every OpenToolTab call, in order; the open tabs are the distinct ones.</summary>
    public List<IToolContext> OpenRequests { get; } = [];

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext => OpenRequests.OfType<T>().FirstOrDefault(condition);

    public T? FindToolTab<T>()
        where T : IToolContext => OpenRequests.OfType<T>().FirstOrDefault();

    public bool OpenToolTab(IToolContext item)
    {
        OpenRequests.Add(item);
        return true;
    }

    public void CloseToolTab(IToolContext item)
    {
        OpenRequests.RemoveAll(t => ReferenceEquals(t, item));
    }
}
